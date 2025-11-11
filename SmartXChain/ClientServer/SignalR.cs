using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace SmartXChain.ClientServer;

/// <summary>
///     Encapsulates SignalR hub communication for SmartXChain nodes, including messaging and WebRTC signaling helpers.
/// </summary>
public class SignalRClient
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pendingRequests = new();

    /// <summary>
    ///     Gets the underlying SignalR hub connection used for communication.
    /// </summary>
    public HubConnection Connection { get; private set; }

    /// <summary>
    ///     Gets the URL of the SignalR server this client is connected to.
    /// </summary>
    public string ServerUrl { get; private set; } = string.Empty;

    /// <summary>
    ///     Raised when a broadcast message is received from the hub.
    /// </summary>
    public event Action<string> OnBroadcastMessageReceived;

    /// <summary>
    ///     Raised when a WebRTC offer is received via the hub.
    /// </summary>
    public event Action<string> OnOfferReceived;

    /// <summary>
    ///     Raised when a WebRTC answer is received via the hub.
    /// </summary>
    public event Action<string> OnAnswerReceived;

    /// <summary>
    ///     Raised when another node requests an SDP offer from the client.
    /// </summary>
    public event Action<string> OnGetOfferReceived;

    /// <summary>
    ///     Raised when a remote ICE candidate is received.
    /// </summary>
    public event Action<string> OnIceCandidateReceived;

    /// <summary>
    ///     Establishes a connection to the provided SignalR server using the supplied JWT token.
    /// </summary>
    /// <param name="serverUrl">The SignalR hub URL.</param>
    /// <param name="jwtToken">JWT token used for authentication.</param>
    public async Task ConnectAsync(string serverUrl, string jwtToken)
    {
        ServerUrl = serverUrl;
        Connection = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => { options.AccessTokenProvider = () => Task.FromResult(jwtToken); })
            .WithAutomaticReconnect()
            .Build();

        RegisterEventHandlers();
        await StartConnectionAsync();
    }

    /// <summary>
    ///     Waits until the hub connection is assigned a connection identifier or the timeout expires.
    /// </summary>
    /// <param name="timeout">The maximum time to wait for the identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the wait operation.</param>
    /// <returns>The connection identifier if available; otherwise, an empty string.</returns>
    public async Task<string> WaitForConnectionIdAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (Connection == null)
            return string.Empty;

        var start = DateTime.UtcNow;
        while (string.IsNullOrEmpty(Connection.ConnectionId))
        {
            if (DateTime.UtcNow - start > timeout)
                return string.Empty;

            await Task.Delay(100, cancellationToken);
        }

        return Connection.ConnectionId;
    }

    private void RegisterEventHandlers()
    {
        Connection.On<string>("ReceiveMessage", message => OnBroadcastMessageReceived?.Invoke(message));
        Connection.On<string>("ReceiveRequestResponse", OnRequestResponseReceived);
        Connection.On<string>("ReceiveOffer", HandleOfferReceived);
        Connection.On<string>("ReceiveAnswer", HandleAnswerReceived);
        Connection.On<string>("GetOffer", HandleGetOffer);
        Connection.On<string>("ReceiveIceCandidate", HandleIceCandidateReceived);

        Connection.Closed += async error =>
        {
            Logger.Log("Connection closed. Attempting to reconnect...");
            await HandleReconnection();
        };
        Connection.Reconnecting += error =>
        {
            Logger.Log("Reconnecting...");
            return Task.CompletedTask;
        };
        Connection.Reconnected += connectionId =>
        {
            Logger.Log("Reconnected successfully. Connection ID: " + connectionId);
            return Task.CompletedTask;
        };
    }

    private void OnRequestResponseReceived(string data)
    {
        try
        {
            var incoming = JsonSerializer.Deserialize<RequestMessage>(data);
            if (incoming != null && !string.IsNullOrEmpty(incoming.CorrelationId))
                if (_pendingRequests.TryGetValue(incoming.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(incoming.Payload);
                    return;
                }

            Logger.Log($"[SignalR] Unsolicited message received: {data}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[SignalR] Error in OnRequestResponseReceived: {ex.Message}");
        }
    }

    private void HandleOfferReceived(string offer)
    {
        try
        {
            OnOfferReceived?.Invoke(offer);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SignalR] Error handling offer: {ex.Message}");
        }
    }

    private void HandleAnswerReceived(string answer)
    {
        try
        {
            OnAnswerReceived?.Invoke(answer);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SignalR] Error handling answer: {ex.Message}");
        }
    }

    private void HandleGetOffer(string answer)
    {
        try
        {
            var rtc = new WebRTC();
            rtc.InitializeAsync();
            var offer = rtc.GetOffer();
            OnGetOfferReceived?.Invoke(offer);
            Logger.Log($"SignalR Offer sent {offer}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "[SignalR] Error handling answer");
        }
    }

    private void HandleIceCandidateReceived(string candidate)
    {
        try
        {
            OnIceCandidateReceived?.Invoke(candidate);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "[SignalR] Error handling ICE candidate");
        }
    }

    /// <summary>
    ///     Sends a request message to the specified target and awaits a correlated response.
    /// </summary>
    /// <param name="targetUser">The SignalR user identifier of the target node.</param>
    /// <param name="payload">The JSON payload to send.</param>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for a response.</param>
    /// <returns>The response payload, a timeout message, or <c>null</c> if the target returned no payload.</returns>
    public async Task<string?> SendRequestAsync(string targetUser, string payload, int timeoutMs = 5000)
    {
        if (Connection.State != HubConnectionState.Connected) return "SignalR not connected.";

        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string?>();
        _pendingRequests[correlationId] = tcs;

        var requestObj = new RequestMessage { CorrelationId = correlationId, Payload = payload };
        var json = JsonSerializer.Serialize(requestObj);

        await SafeInvokeAsync(() => Connection.InvokeAsync("SendRequest", targetUser, json));
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        _pendingRequests.TryRemove(correlationId, out _);

        return completedTask == tcs.Task ? tcs.Task.Result : "Timeout: No response received.";
    }

    private async Task StartConnectionAsync()
    {
        try
        {
            await Connection.StartAsync();
            Logger.Log("SignalR connection established successfully.");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Error while establishing the connection");
            await HandleReconnection();
        }
    }

    private async Task HandleReconnection()
    {
        while (Connection.State != HubConnectionState.Connected)
            try
            {
                Logger.Log("Attempting to reconnect...");
                await Connection.StartAsync();
                Logger.Log("SignalR connection re-established successfully.");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error while reconnecting: {ex.Message}");
                await Task.Delay(3000);
            }
    }

    /// <summary>
    ///     Sends a WebRTC SDP offer to the specified peer via the hub.
    /// </summary>
    /// <param name="targetUser">The target node identifier.</param>
    /// <param name="offer">The SDP offer to send.</param>
    public async Task SendOffer(string targetUser, string offer)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("SendOffer", targetUser, offer));
    }

    /// <summary>
    ///     Sends a WebRTC SDP answer to the specified peer via the hub.
    /// </summary>
    /// <param name="targetUser">The target node identifier.</param>
    /// <param name="answer">The SDP answer to send.</param>
    public async Task SendAnswer(string targetUser, string answer)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("SendAnswer", targetUser, answer));
    }

    /// <summary>
    ///     Sends an ICE candidate message to the specified peer via the hub.
    /// </summary>
    /// <param name="targetUser">The target node identifier.</param>
    /// <param name="candidate">The ICE candidate payload.</param>
    public async Task SendIceCandidate(string targetUser, string candidate)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("SendIceCandidate", targetUser, candidate));
    }

    /// <summary>
    ///     Broadcasts a message to all connected hub clients.
    /// </summary>
    /// <param name="message">The message payload to broadcast.</param>
    public async Task BroadcastMessage(string message)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("BroadcastMessage", message));
    }

    /// <summary>
    ///     Requests a WebRTC SDP offer from a remote node through the hub.
    /// </summary>
    /// <param name="nodeAddress">The node address to request the offer from.</param>
    /// <returns>The SDP offer if available; otherwise, an empty string.</returns>
    public async Task<string> GetOfferFromServer(string nodeAddress)
    {
        if (Connection.State != HubConnectionState.Connected)
        {
            Logger.LogError($"SignalR-connection not available to get offer from {nodeAddress}");
            return string.Empty;
        }

        try
        {
            var offer = await Connection.InvokeAsync<string>("GetOffer", nodeAddress);
            Logger.Log($"Got offer from {nodeAddress}: {offer}");
            return offer;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Error getting offer from {nodeAddress}");
            return string.Empty;
        }
    }

    private async Task SafeInvokeAsync(Func<Task> invokeFunction)
    {
        try
        {
            await invokeFunction();
        }
        catch (Exception ex)
        {
            Logger.Log($"Error while sending message: {ex.Message}");
            if (Connection.State != HubConnectionState.Connected)
            {
                Logger.Log("Connection lost. Attempting to resend...");
                await HandleReconnection();
                await invokeFunction();
            }
        }
    }

    private class RequestMessage
    {
        public string CorrelationId { get; set; }
        public string Payload { get; set; }
    }
}