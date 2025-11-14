using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartXSignalR.Options;

namespace SmartXSignalR.Services;

/// <summary>
///     Tracks connected SignalR clients and outstanding offer requests.
/// </summary>
public class HubState
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
    private readonly ConcurrentDictionary<string, OfferRequestState> _pendingOfferRequests = new();
    private readonly SignalRHubOptions _options;
    private readonly ILogger<HubState> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public HubState(IOptions<SignalRHubOptions> options, ILogger<HubState> logger)
    {
        _options = options?.Value ?? new SignalRHubOptions();
        _logger = logger;
    }

    public ConnectionInfo RegisterConnection(string connectionId, ClaimsPrincipal? user)
    {
        var info = _connections.GetOrAdd(connectionId, id => new ConnectionInfo(id));
        info.LastSeen = DateTime.UtcNow;
        if (user?.Identity != null)
        {
            info.IsAuthenticated = user.Identity.IsAuthenticated;
            info.UserName = user.Identity.Name;
            info.UserIdentifier = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                                   ?? user.FindFirst(ClaimTypes.Name)?.Value
                                   ?? info.UserIdentifier;
        }

        return info;
    }

    public ConnectionInfo? RemoveConnection(string connectionId)
    {
        _connections.TryRemove(connectionId, out var removed);
        return removed;
    }

    public bool TryGetConnection(string connectionId, out ConnectionInfo? info)
    {
        return _connections.TryGetValue(connectionId, out info);
    }

    public bool TryGetCachedOffer(string connectionId, TimeSpan maxAge, out string? offer)
    {
        offer = null;
        if (maxAge <= TimeSpan.Zero)
            return false;

        if (_connections.TryGetValue(connectionId, out var info) &&
            !string.IsNullOrEmpty(info.LastOffer) &&
            info.LastOfferTimestamp is { } timestamp &&
            DateTime.UtcNow - timestamp <= maxAge)
        {
            offer = info.LastOffer;
            return true;
        }

        return false;
    }

    public void SetLastOffer(string connectionId, string? offer)
    {
        var info = _connections.GetOrAdd(connectionId, id => new ConnectionInfo(id));
        info.LastSeen = DateTime.UtcNow;
        info.LastOffer = offer;
        info.LastOfferTimestamp = string.IsNullOrWhiteSpace(offer) ? null : DateTime.UtcNow;
    }

    public OfferRequestState? TryCreateOfferRequest(string requesterConnectionId, string targetConnectionId)
    {
        if (string.IsNullOrWhiteSpace(requesterConnectionId) || string.IsNullOrWhiteSpace(targetConnectionId))
            return null;

        if (_pendingOfferRequests.Count >= _options.MaxPendingOfferRequests)
            return null;

        var requestId = Guid.NewGuid().ToString("N");
        var request = new OfferRequestState(requestId, requesterConnectionId, targetConnectionId);
        return _pendingOfferRequests.TryAdd(requestId, request) ? request : null;
    }

    public bool TryCompleteOfferRequest(string requestId, string offer, out OfferRequestState? request)
    {
        if (_pendingOfferRequests.TryRemove(requestId, out request))
        {
            request.CompletionSource.TrySetResult(offer ?? string.Empty);
            return true;
        }

        request = null;
        return false;
    }

    public bool TryCancelOfferRequest(string requestId, out OfferRequestState? request)
    {
        if (_pendingOfferRequests.TryRemove(requestId, out request))
        {
            request.CompletionSource.TrySetResult(string.Empty);
            return true;
        }

        request = null;
        return false;
    }

    public IReadOnlyCollection<OfferRequestState> RemoveOfferRequestsForConnection(string connectionId)
    {
        var removed = new List<OfferRequestState>();
        foreach (var pair in _pendingOfferRequests.ToArray())
            if (string.Equals(pair.Value.RequestingConnectionId, connectionId, StringComparison.Ordinal) ||
                string.Equals(pair.Value.TargetConnectionId, connectionId, StringComparison.Ordinal))
                if (_pendingOfferRequests.TryRemove(pair.Key, out var request))
                {
                    request.CompletionSource.TrySetResult(string.Empty);
                    removed.Add(request);
                }

        return removed;
    }

    public int PruneExpiredOfferRequests(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
            return 0;

        var removed = 0;
        var threshold = DateTime.UtcNow - maxAge;
        foreach (var pair in _pendingOfferRequests.ToArray())
            if (pair.Value.CreatedAt <= threshold)
                if (_pendingOfferRequests.TryRemove(pair.Key, out var request))
                {
                    request.CompletionSource.TrySetResult(string.Empty);
                    removed++;
                }

        return removed;
    }

    public void UpdateFromBroadcast(string connectionId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var info = _connections.GetOrAdd(connectionId, id => new ConnectionInfo(id));
        info.LastSeen = DateTime.UtcNow;

        try
        {
            var envelope = JsonSerializer.Deserialize<BroadcastMessageEnvelope>(message, _jsonOptions);
            if (envelope == null)
                return;

            info.LastBroadcastType = envelope.Type;
            info.LastBroadcastPayload = message;

            if (envelope.Info != null)
            {
                info.ChainId = envelope.Info.ChainID ?? info.ChainId;
                info.NodeAddress = envelope.Info.NodeAddress ?? info.NodeAddress;
                info.PublicKey = envelope.Info.PublicKey ?? info.PublicKey;
                info.DllFingerprint = envelope.Info.DllFingerprint ?? info.DllFingerprint;
                info.BlockCount = envelope.Info.BlockCount ?? info.BlockCount;

                if (string.Equals(envelope.Type, "Availability", StringComparison.OrdinalIgnoreCase))
                    info.LastAvailabilityBroadcast = DateTime.UtcNow;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse broadcast message from {ConnectionId}.", connectionId);
        }
    }

    private sealed record BroadcastMessageEnvelope
    {
        public string? Type { get; init; }
        public ChainInfoPayload? Info { get; init; }
    }

    private sealed record ChainInfoPayload
    {
        public string? ChainID { get; init; }
        public string? NodeAddress { get; init; }
        public string? Message { get; init; }
        public string? PublicKey { get; init; }
        public string? DllFingerprint { get; init; }
        public int? BlockCount { get; init; }
    }
}

public class ConnectionInfo
{
    public ConnectionInfo(string connectionId)
    {
        ConnectionId = connectionId;
        ConnectedAt = DateTime.UtcNow;
        LastSeen = ConnectedAt;
    }

    public string ConnectionId { get; }
    public DateTime ConnectedAt { get; }
    public DateTime LastSeen { get; set; }
    public string? UserIdentifier { get; set; }
    public string? UserName { get; set; }
    public bool IsAuthenticated { get; set; }
    public string? ChainId { get; set; }
    public string? NodeAddress { get; set; }
    public string? PublicKey { get; set; }
    public string? DllFingerprint { get; set; }
    public int? BlockCount { get; set; }
    public string? LastBroadcastType { get; set; }
    public string? LastBroadcastPayload { get; set; }
    public string? LastOffer { get; set; }
    public DateTime? LastOfferTimestamp { get; set; }
    public DateTime? LastAvailabilityBroadcast { get; set; }
}

public class OfferRequestState
{
    public OfferRequestState(string requestId, string requesterConnectionId, string targetConnectionId)
    {
        RequestId = requestId;
        RequestingConnectionId = requesterConnectionId;
        TargetConnectionId = targetConnectionId;
        CreatedAt = DateTime.UtcNow;
        CompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public string RequestId { get; }
    public string RequestingConnectionId { get; }
    public string TargetConnectionId { get; }
    public DateTime CreatedAt { get; }
    public TaskCompletionSource<string> CompletionSource { get; }
}
