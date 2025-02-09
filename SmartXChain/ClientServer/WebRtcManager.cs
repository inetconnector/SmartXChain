using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using SIPSorcery.Net;
using SmartXChain.ClientServer;

// Make sure the SIPSorcery NuGet package is referenced.

namespace SmartXChain.ClientServer
{
    /// <summary>
    ///     A simplified WebRTC class that:
    ///     - Generates a local SDP offer (GetOffer)
    ///     - Opens a connection (OpenAsync)
    ///     - Sends messages using a key and waits for a response (SendAsync)
    ///     - Combines all of the above into one step (InitOpenAndSendAsync)
    /// </summary>
    public class WebRTC
    {
        /// <summary>
        ///     WebRTCManager instance.
        /// </summary>
        public static WebRTCManager Manager = new();

        // Dictionary for pending requests correlated by a key.
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new();

        private RTCDataChannel _dataChannel;
        private RTCPeerConnection _peerConnection;

        public string MySDPAddress { get; private set; }
        public string RemoteSDPAddress { get; private set; }

        // Timeout for requests in milliseconds.
        public int RequestTimeoutMs { get; set; } = 5000;

        // Events to forward SDP and ICE candidates to an external signaling channel.
        public event Action<string> OnSdpOfferGenerated;
        public event Action<string> OnSdpAnswerGenerated;
        public event Action<string> OnIceCandidateGenerated;

        /// <summary>
        ///     Combines initialization, opening the connection, and sending a message in one step.
        /// </summary>
        /// <param name="remoteSdp">The remote SDP (offer) from the target peer.</param>
        /// <param name="key">The key for request/response correlation.</param>
        /// <param name="message">The message to send.</param>
        /// <returns>The response as a string.</returns>
        public async Task<string> InitOpenAndSendAsync(string remoteSdp, string key, string message)
        {
            await InitOpenAsync(remoteSdp);
            return await SendAsync(key, message);
        }

        /// <summary>
        ///     Initializes the RTCPeerConnection, creates the DataChannel, sets the remote SDP,
        ///     and waits for the DataChannel to open.
        /// </summary>
        /// <param name="remoteSdp">The remote SDP offer to open the connection.</param>
        public async Task InitOpenAsync(string remoteSdp)
        {
            await InitializeAsync();
            await OpenAsync(remoteSdp);
            await WaitForDataChannelOpenAsync();
        }

        /// <summary>
        ///     Waits until the DataChannel is open or times out.
        /// </summary>
        private async Task WaitForDataChannelOpenAsync(int timeoutMs = 5000)
        {
            var startTime = DateTime.UtcNow;
            while (!_dataChannel.IsOpened)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                    throw new TimeoutException("DataChannel did not open in time.");
                await Task.Delay(100);
            }
        }

        /// <summary>
        ///     Returns true if the DataChannel is active (open).
        /// </summary>
        public bool IsActive()
        {
            return _dataChannel != null && _dataChannel.IsOpened;
        }

        /// <summary>
        ///     Initializes the RTCPeerConnection and creates the DataChannel.
        /// </summary>
        public async Task InitializeAsync()
        {
            // IMPORTANT: For NAT traversal (e.g., behind a FRITZ!Box) you should include a STUN server.
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new() { urls = "stun:stun.l.google.com:19302" }
                }
            };

            _peerConnection = new RTCPeerConnection(config);
            _dataChannel = await _peerConnection.createDataChannel("simple_channel");

            // Subscribe to onopen event.
            _dataChannel.onopen += () => Logger.Log("DataChannel is now open.");

            // Handle incoming messages.
            _dataChannel.onmessage += (dc, protocol, data) =>
            {
                var json = Encoding.UTF8.GetString(data);
                Logger.Log("[DataChannel] Received: " + json);
                HandleIncomingMessage(json);
            };

            // ICE candidate event.
            _peerConnection.onicecandidate += candidate =>
            {
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                {
                    OnIceCandidateGenerated?.Invoke(candidate.candidate);
                    Logger.Log("ICE Candidate generated: " + candidate.candidate);
                }
            };

            Logger.Log("WebRTC: Initialized.");
            await Task.CompletedTask;
        }

        /// <summary>
        ///     Generates a local SDP offer, sets it as the local description, and returns the SDP string.
        /// </summary>
        public string GetOffer()
        {
            var offer = _peerConnection.createOffer();
            _peerConnection.setLocalDescription(offer);
            MySDPAddress = offer.sdp;
            OnSdpOfferGenerated?.Invoke(offer.sdp);
            Logger.Log("Local SDP offer generated.");
            return offer.sdp;
        }

        /// <summary>
        ///     Sets the remote SDP (as an offer) and generates an SDP answer.
        /// </summary>
        /// <param name="remoteSdp">The remote SDP offer received from the peer.</param>
        private async Task OpenAsync(string remoteSdp)
        {
            var desc = new RTCSessionDescriptionInit { sdp = remoteSdp, type = RTCSdpType.offer };
            _peerConnection.setRemoteDescription(desc);
            Logger.Log("WebRTC: Remote SDP offer set.");

            var answer = _peerConnection.createAnswer();
            _peerConnection.setLocalDescription(answer);
            OnSdpAnswerGenerated?.Invoke(answer.sdp);
            Logger.Log("WebRTC: SDP answer generated.");
            RemoteSDPAddress = remoteSdp;
            await Task.CompletedTask;
        }

        /// <summary>
        ///     Sends a message over the DataChannel using a key for request/response correlation and waits for the response.
        /// </summary>
        /// <param name="key">The key for correlating the request and response.</param>
        /// <param name="message">The message to send.</param>
        /// <returns>The response as a string or a timeout message.</returns>
        private async Task<string> SendAsync(string key, string message)
        {
            if (_dataChannel == null || !_dataChannel.IsOpened)
                throw new InvalidOperationException("DataChannel is not open.");

            var tcs = new TaskCompletionSource<string>();
            _pendingRequests[key] = tcs;

            var request = new ApiRequest { ApiName = key, Parameters = message };
            var json = JsonSerializer.Serialize(request);
            var buffer = Encoding.UTF8.GetBytes(json);

            _dataChannel.send(buffer);
            Logger.Log("[DataChannel] Sent: " + json);

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs));
            _pendingRequests.TryRemove(key, out _);

            return completedTask == tcs.Task ? tcs.Task.Result : "Timeout: No response received.";
        }

        /// <summary>
        ///     Processes incoming JSON messages.
        ///     Expects a JSON object with fields "ApiName" and "Parameters".
        /// </summary>
        /// <param name="json">The received JSON message.</param>
        private void HandleIncomingMessage(string json)
        {
            try
            {
                var message = JsonSerializer.Deserialize<ApiRequest>(json);
                if (message != null && !string.IsNullOrEmpty(message.ApiName))
                {
                    if (_pendingRequests.TryGetValue(message.ApiName, out var tcs))
                        tcs.SetResult(message.Parameters);
                    else
                        Logger.Log("No pending request for key: " + message.ApiName);
                }
                else
                {
                    Logger.Log("Invalid message received: " + json);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error processing incoming message: " + ex.Message);
            }
        }

        /// <summary>
        ///     A class that defines the structure of an API request/response.
        /// </summary>
        private class ApiRequest
        {
            public string ApiName { get; set; }
            public string Parameters { get; set; }
        }
    } 
}


public class WebRTCManager
{
    // Cache for active WebRTC connections, keyed by node address.
    private readonly ConcurrentDictionary<string, WebRTC> _connections = new();

    /// <summary>
    ///     Gets an active WebRTC connection for the given node address.
    ///     If none exists or it’s not active, creates and caches a new connection.
    /// </summary>
    /// <param name="nodeAddress">The remote node's address.</param>
    /// <param name="remoteSdp">The remote SDP for the node (from your Node.CurrentNodes_SDP table).</param>
    /// <returns>An active WebRTC connection.</returns>
    public async Task<WebRTC> GetOrCreateConnectionAsync(string nodeAddress, string remoteSdp)
    {
        if (_connections.TryGetValue(nodeAddress, out var connection))
        {
            if (connection.IsActive())
                return connection;
            _connections.TryRemove(nodeAddress, out _);
        }

        connection = new WebRTC();
        await connection.InitOpenAsync(remoteSdp);
        _connections[nodeAddress] = connection;
        return connection;
    }
     

    /// <summary>
    ///     Combines initialization, opening the connection, and sending a message for the given node.
    ///     Uses the provided remote SDP and caches the connection by node address.
    /// </summary>
    /// <param name="nodeAddress">The remote node's address.</param>
    /// <param name="remoteSdp">The remote SDP for the node.</param>
    /// <param name="key">The key for request/response correlation.</param>
    /// <param name="message">The message to send.</param>
    /// <returns>The response from the remote peer as a string.</returns>
    public async Task<string> InitOpenAndSendAsync(string nodeAddress, string remoteSdp, string key, string message)
    {
        var connection = await GetOrCreateConnectionAsync(nodeAddress, remoteSdp);
        return await connection.InitOpenAndSendAsync(remoteSdp, key, message);
    }
}