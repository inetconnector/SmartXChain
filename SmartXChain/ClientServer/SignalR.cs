using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace SmartXChain.ClientServer;

public class SignalRClient
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pendingRequests = new();
    public HubConnection Connection { get; private set; }

    public event Action<string> OnBroadcastMessageReceived;
    public event Action<string> OnOfferReceived;
    public event Action<string> OnAnswerReceived;
    public event Action<string> OnIceCandidateReceived;

    public async Task ConnectAsync(string serverUrl, string jwtToken)
    {
        Connection = new HubConnectionBuilder()
            .WithUrl(serverUrl, options => { options.AccessTokenProvider = () => Task.FromResult(jwtToken); })
            .WithAutomaticReconnect()
            .Build();

        RegisterEventHandlers();
        await StartConnectionAsync();
    }

    private void RegisterEventHandlers()
    {
        Connection.On<string>("ReceiveMessage", message => OnBroadcastMessageReceived?.Invoke(message));
        Connection.On<string>("ReceiveRequestResponse", OnRequestResponseReceived);
        Connection.On<string>("ReceiveOffer", HandleOfferReceived);
        Connection.On<string>("ReceiveAnswer", HandleAnswerReceived);
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

    private void HandleIceCandidateReceived(string candidate)
    {
        try
        {
            OnIceCandidateReceived?.Invoke(candidate);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SignalR] Error handling ICE candidate: {ex.Message}");
        }
    }

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
            Logger.Log($"Error while establishing the connection: {ex.Message}");
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

    public async Task SendOffer(string targetUser, string offer)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("SendOffer", targetUser, offer));
    }

    public async Task SendAnswer(string targetUser, string answer)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("SendAnswer", targetUser, answer));
    }

    public async Task SendIceCandidate(string targetUser, string candidate)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("SendIceCandidate", targetUser, candidate));
    }

    public async Task BroadcastMessage(string message)
    {
        await SafeInvokeAsync(() => Connection.InvokeAsync("BroadcastMessage", message));
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