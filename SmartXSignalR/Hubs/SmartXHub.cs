using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartXSignalR.Options;
using SmartXSignalR.Services;

namespace SmartXSignalR.Hubs;

/// <summary>
///     SignalR hub used by SmartX blockchain nodes for discovery and WebRTC signalling.
/// </summary>
[Authorize]
public class SmartXHub : Hub
{
    private readonly HubState _state;
    private readonly ILogger<SmartXHub> _logger;
    private readonly SignalRHubOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SmartXHub(HubState state, IOptions<SignalRHubOptions> options, ILogger<SmartXHub> logger)
    {
        _state = state;
        _logger = logger;
        _options = options.Value;
    }

    public override Task OnConnectedAsync()
    {
        var info = _state.RegisterConnection(Context.ConnectionId, Context.User);
        _logger.LogInformation("Connection {ConnectionId} connected (authenticated: {IsAuthenticated}).",
            info.ConnectionId,
            info.IsAuthenticated);

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _state.RemoveConnection(Context.ConnectionId);
        var removed = _state.RemoveOfferRequestsForConnection(Context.ConnectionId).Count;
        if (removed > 0)
            _logger.LogDebug("Removed {Count} pending offer requests for disconnected node {ConnectionId}.",
                removed,
                Context.ConnectionId);

        if (exception != null)
            _logger.LogWarning(exception, "Connection {ConnectionId} disconnected unexpectedly.", Context.ConnectionId);
        else
            _logger.LogInformation("Connection {ConnectionId} disconnected.", Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }

    public async Task BroadcastMessage(string message)
    {
        _state.UpdateFromBroadcast(Context.ConnectionId, message);
        await Clients.Others.SendAsync("ReceiveMessage", message);
    }

    public Task Echo(string message)
    {
        return Clients.Caller.SendAsync("echo", message ?? string.Empty);
    }

    public Task Broadcast(string message)
    {
        return Clients.All.SendAsync("broadcast", message ?? string.Empty);
    }

    public Task SendOffer(string targetConnectionId, string offer)
    {
        if (string.IsNullOrWhiteSpace(targetConnectionId))
            return Task.CompletedTask;

        _state.SetLastOffer(Context.ConnectionId, offer);
        return Clients.Client(targetConnectionId).SendAsync("ReceiveOffer", offer ?? string.Empty);
    }

    public Task SendAnswer(string targetConnectionId, string answer)
    {
        if (string.IsNullOrWhiteSpace(targetConnectionId))
            return Task.CompletedTask;

        return Clients.Client(targetConnectionId).SendAsync("ReceiveAnswer", answer ?? string.Empty);
    }

    public Task SendIceCandidate(string targetConnectionId, string candidate)
    {
        if (string.IsNullOrWhiteSpace(targetConnectionId))
            return Task.CompletedTask;

        return Clients.Client(targetConnectionId).SendAsync("ReceiveIceCandidate", candidate ?? string.Empty);
    }

    public async Task<string> GetOffer(string targetConnectionId, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(targetConnectionId) ||
            string.Equals(targetConnectionId, Context.ConnectionId, StringComparison.Ordinal))
            return string.Empty;

        if (!_state.TryGetConnection(targetConnectionId, out _))
        {
            _logger.LogDebug("GetOffer: target {Target} not connected.", targetConnectionId);
            return string.Empty;
        }

        var cacheDuration = TimeSpan.FromSeconds(Math.Max(0, _options.OfferCacheDurationSeconds));
        if (!forceRefresh && cacheDuration > TimeSpan.Zero &&
            _state.TryGetCachedOffer(targetConnectionId, cacheDuration, out var cachedOffer) &&
            !string.IsNullOrEmpty(cachedOffer))
            return cachedOffer;

        _state.PruneExpiredOfferRequests(TimeSpan.FromSeconds(Math.Max(1, _options.OfferResponseTimeoutSeconds) * 3));

        var offerRequest = _state.TryCreateOfferRequest(Context.ConnectionId, targetConnectionId);
        if (offerRequest == null)
        {
            _logger.LogWarning("GetOffer: unable to create request for {Requester} -> {Target}.", Context.ConnectionId,
                targetConnectionId);
            return string.Empty;
        }

        var payload = JsonSerializer.Serialize(new OfferRequestPayload
        {
            RequestId = offerRequest.RequestId,
            RequesterConnectionId = Context.ConnectionId
        }, _jsonOptions);

        await Clients.Client(targetConnectionId).SendAsync("GetOffer", payload);

        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.OfferResponseTimeoutSeconds));
        var offerTask = offerRequest.CompletionSource.Task;
        var completed = await Task.WhenAny(offerTask, Task.Delay(timeout, Context.ConnectionAborted));
        if (completed != offerTask)
        {
            _state.TryCancelOfferRequest(offerRequest.RequestId, out _);
            _logger.LogWarning("GetOffer request {RequestId} timed out after {Timeout}s (requester={Requester}, target={Target}).",
                offerRequest.RequestId,
                timeout.TotalSeconds,
                Context.ConnectionId,
                targetConnectionId);
            return string.Empty;
        }

        var offer = await offerTask;
        _state.SetLastOffer(targetConnectionId, offer);
        return offer ?? string.Empty;
    }

    public Task SubmitOffer(string requestId, string offer, string? requesterConnectionId = null)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            if (!string.IsNullOrWhiteSpace(requesterConnectionId))
            {
                _state.SetLastOffer(Context.ConnectionId, offer);
                return Clients.Client(requesterConnectionId).SendAsync("ReceiveOffer", offer ?? string.Empty);
            }

            _logger.LogDebug("SubmitOffer received without request id from {ConnectionId}.", Context.ConnectionId);
            return Task.CompletedTask;
        }

        if (_state.TryCompleteOfferRequest(requestId, offer ?? string.Empty, out var pendingRequest) &&
            pendingRequest != null &&
            !string.IsNullOrWhiteSpace(pendingRequest.RequestingConnectionId))
        {
            _state.SetLastOffer(Context.ConnectionId, offer);
            return Clients.Client(pendingRequest.RequestingConnectionId)
                .SendAsync("ReceiveOffer", offer ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(requesterConnectionId))
        {
            _state.SetLastOffer(Context.ConnectionId, offer);
            return Clients.Client(requesterConnectionId).SendAsync("ReceiveOffer", offer ?? string.Empty);
        }

        _logger.LogDebug("SubmitOffer: no pending offer request matched id {RequestId}.", requestId);
        return Task.CompletedTask;
    }

    public async Task SendRequest(string targetConnectionId, string payload)
    {
        if (string.IsNullOrWhiteSpace(targetConnectionId))
            return;

        if (!_state.TryGetConnection(targetConnectionId, out _))
        {
            _logger.LogDebug("SendRequest: target {Target} not connected.", targetConnectionId);
            return;
        }

        await Clients.Client(targetConnectionId).SendAsync("ReceiveRequest", Context.ConnectionId, payload);
    }

    public Task SendRequestResponse(string requesterConnectionId, string payload)
    {
        if (string.IsNullOrWhiteSpace(requesterConnectionId))
            return Task.CompletedTask;

        return Clients.Client(requesterConnectionId).SendAsync("ReceiveRequestResponse", payload ?? string.Empty);
    }

    private sealed class OfferRequestPayload
    {
        public string RequestId { get; init; } = string.Empty;
        public string RequesterConnectionId { get; init; } = string.Empty;
    }
}
