using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace SmartXChain.ClientServer;

public class SignalRClient
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pendingRequests = new();

    private HubConnection _connection;

    /// <summary>
    ///     Event fired when we receive a standard broadcast message (already in your code).
    /// </summary>
    public event Action<string> OnBroadcastMessageReceived;

    /// <summary>
    ///     New event (or direct handler) used specifically to capture request/responses.
    /// </summary>
    private void OnRequestResponseReceived(string data)
    {
        try
        {
            // Attempt to parse the incoming JSON
            var incoming = JsonSerializer.Deserialize<RequestMessage>(data);
            if (incoming != null && !string.IsNullOrEmpty(incoming.CorrelationId))
                // If we have a pending request with this correlation ID, it means it's a response
                if (_pendingRequests.TryGetValue(incoming.CorrelationId, out var tcs))
                {
                    tcs.TrySetResult(incoming.Payload);
                    return;
                }

            // If no match found, handle it as a generic incoming message
            Console.WriteLine($"[SignalR] Received an unsolicited message: {data}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SignalR] Error in OnRequestResponseReceived: {ex.Message}");
        }
    }

    public async Task ConnectAsync(string serverUrl, string jwtToken)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => { options.AccessTokenProvider = () => Task.FromResult(jwtToken); })
            .WithAutomaticReconnect()
            .Build();

        RegisterEventHandlers();
        await StartConnectionAsync();
    }

    private void RegisterEventHandlers()
    {
        // Existing handlers in your code:
        _connection.On<string>("ReceiveMessage", message => OnBroadcastMessageReceived?.Invoke(message));

        // 2) New or repurposed handler to receive request/response payloads
        //    The method name "ReceiveRequestResponse" must match what your SignalR Hub invokes.
        _connection.On<string>("ReceiveRequestResponse", jsonString => OnRequestResponseReceived(jsonString));

        // Reconnection events (already in your code)...
        _connection.Closed += async error =>
        {
            Console.WriteLine("Connection closed. Attempting to reconnect...");
            await HandleReconnection();
        };
        _connection.Reconnecting += error =>
        {
            Console.WriteLine("Reconnecting...");
            return Task.CompletedTask;
        };
        _connection.Reconnected += connectionId =>
        {
            Console.WriteLine("Reconnected successfully. Connection ID: " + connectionId);
            return Task.CompletedTask;
        };
    }


    // ---------------------------------------------------------------------------------------
    // 3) The core method: Send a request with correlation ID, await response, up to a timeout
    // ---------------------------------------------------------------------------------------
    public async Task<string?> SendRequestAsync(string targetUser, string payload, int timeoutMs = 5000)
    {
        if (_connection.State != HubConnectionState.Connected) return "SignalR not connected.";

        // Create a unique correlation ID
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string?>();
        _pendingRequests[correlationId] = tcs;

        // Wrap the data in a JSON object
        var requestObj = new RequestMessage
        {
            CorrelationId = correlationId,
            Payload = payload
        };

        var json = JsonSerializer.Serialize(requestObj);

        // Call a Hub method, e.g. "SendRequest" or something you define server-side
        // The server is then expected to forward or process this
        // and eventually call "ReceiveRequestResponse" with the same correlation ID.
        await SafeInvokeAsync(() =>
            _connection.InvokeAsync("SendRequest", targetUser, json)
        );

        // Wait for the response or timeout
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
        _pendingRequests.TryRemove(correlationId, out _);

        if (completedTask == tcs.Task)
            // If we got a response in time
            return tcs.Task.Result;

        return "Timeout: No response received.";
    }

    // Existing methods for sending Offer/Answer, ICE, etc. (omitted for brevity)...

    public event Action<string> OnOfferReceived;
    public event Action<string> OnAnswerReceived;
    public event Action<string> OnIceCandidateReceived;

    private async Task StartConnectionAsync()
    {
        try
        {
            await _connection.StartAsync();
            Console.WriteLine("SignalR connection established successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while establishing the connection: {ex.Message}");
            await HandleReconnection();
        }
    }

    private async Task HandleReconnection()
    {
        while (_connection.State != HubConnectionState.Connected)
            try
            {
                Console.WriteLine("Attempting to reconnect...");
                await _connection.StartAsync();
                Console.WriteLine("SignalR connection re-established successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error while reconnecting: {ex.Message}");
                await Task.Delay(3000);
            }
    }

    // Methods to send messages to the server
    public async Task SendOffer(string targetUser, string offer)
    {
        await SafeInvokeAsync(() => _connection.InvokeAsync("SendOffer", targetUser, offer));
    }

    public async Task SendAnswer(string targetUser, string answer)
    {
        await SafeInvokeAsync(() => _connection.InvokeAsync("SendAnswer", targetUser, answer));
    }

    public async Task SendIceCandidate(string targetUser, string candidate)
    {
        await SafeInvokeAsync(() => _connection.InvokeAsync("SendIceCandidate", targetUser, candidate));
    }

    public async Task BroadcastMessage(string message)
    {
        await SafeInvokeAsync(() => _connection.InvokeAsync("BroadcastMessage", message));
    }

    private async Task SafeInvokeAsync(Func<Task> invokeFunction)
    {
        try
        {
            await invokeFunction();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error while sending message: {ex.Message}");
            if (_connection.State != HubConnectionState.Connected)
            {
                Console.WriteLine("Connection lost. Attempting to resend...");
                await HandleReconnection();
                await invokeFunction();
            }
        }
    }
    public async Task<string> InvokeApiAsync(string apiName, string parameters)
    {
        // Build the ApiRequest
        var requestObj = new ApiRequest
        { 
            ApiName = apiName,
            Parameters = parameters
        };
        var requestJson = JsonSerializer.Serialize(requestObj);

        // Call the "InvokeApi" method on the hub
        var responseJson = await _connection.InvokeAsync<string>("InvokeApi", requestJson);

        // You can either return the raw JSON, or parse it back into ApiRequest
        // For example, parse it:
        var responseObj = JsonSerializer.Deserialize<ApiRequest>(responseJson);
        if (responseObj == null)
            return "ERROR: Could not parse response from the server.";

        // Return the 'Parameters' portion if you just want the data
        return responseObj.Parameters;
    }

    // If you reuse the same ApiRequest class as WebRtcManager:
    private class ApiRequest
    {
        public string ApiName { get; set; }
        public string Parameters { get; set; }
        public string TargetUser { get; set; }
    }

    // A simple DTO to wrap the correlation ID + payload
    private class RequestMessage
    {
        public string CorrelationId { get; set; }
        public string Payload { get; set; }
    }
}