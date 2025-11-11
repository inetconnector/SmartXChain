using System.Text.Json;
using StackExchange.Redis;

namespace SmartXChain.ClientServer;

public sealed class RedisNodeRegistry : IAsyncDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly string _chainId;
    private readonly string _channelName;
    private readonly ConnectionMultiplexer _connection;
    private readonly IDatabase _database;
    private readonly TimeSpan _heartbeatInterval;
    private readonly string _nodeAddress;
    private readonly TimeSpan _nodeEntryTtl;
    private readonly string _nodeKeyPrefix;
    private readonly string _nodeSetKey;
    private readonly ISubscriber _subscriber;
    private Task _heartbeatTask = Task.CompletedTask;
    private string _sdp = string.Empty;
    private string _signalHub = string.Empty;

    private RedisNodeRegistry(ConnectionMultiplexer connection, IDatabase database, ISubscriber subscriber,
        string redisNamespace, string chainId, string nodeAddress, TimeSpan heartbeatInterval, TimeSpan nodeEntryTtl)
    {
        _connection = connection;
        _database = database;
        _subscriber = subscriber;
        _heartbeatInterval = heartbeatInterval;
        _nodeEntryTtl = nodeEntryTtl;
        _chainId = chainId;
        _nodeAddress = nodeAddress;

        _nodeKeyPrefix = $"{redisNamespace}:nodes:{chainId}";
        _nodeSetKey = _nodeKeyPrefix;
        _channelName = $"{_nodeKeyPrefix}:events";
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            await _heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await PublishAsync(RedisNodeEvent.Leave(_nodeAddress));
        await _database.SetRemoveAsync(_nodeSetKey, _nodeAddress);
        await _database.KeyDeleteAsync(BuildNodeKey(_nodeAddress));

        await _subscriber.UnsubscribeAsync(_channelName);
        await _connection.CloseAsync();
        _connection.Dispose();
        _cancellationTokenSource.Dispose();
    }

    public event Func<RedisNodeInfo, Task>? NodeDiscovered;
    public event Func<string, Task>? NodeRemoved;

    public static async Task<RedisNodeRegistry> CreateAsync(string connectionString, string redisNamespace,
        string chainId, string nodeAddress, TimeSpan heartbeatInterval, TimeSpan nodeEntryTtl)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = connection.GetDatabase();
        var subscriber = connection.GetSubscriber();

        var registry = new RedisNodeRegistry(connection, database, subscriber, redisNamespace, chainId, nodeAddress,
            heartbeatInterval, nodeEntryTtl);

        await registry.InitializeAsync();
        return registry;
    }

    public async Task RegisterSelfAsync(string signalHub, string? sdp)
    {
        _signalHub = signalHub ?? string.Empty;
        _sdp = sdp ?? string.Empty;

        await RefreshSelfAsync();
        await PublishAsync(RedisNodeEvent.Join(CreateInfo(DateTime.UtcNow)));
    }

    public async Task<IReadOnlyCollection<RedisNodeInfo>> GetKnownNodesAsync()
    {
        var result = new List<RedisNodeInfo>();
        var members = await _database.SetMembersAsync(_nodeSetKey);

        foreach (var member in members)
        {
            var nodeAddress = member.ToString();
            var info = await GetNodeInfoAsync(nodeAddress);
            if (info == null)
            {
                await _database.SetRemoveAsync(_nodeSetKey, member);
                continue;
            }

            result.Add(info.Value);
        }

        return result;
    }

    private async Task InitializeAsync()
    {
        await _subscriber.SubscribeAsync(_channelName, async (_, value) =>
        {
            try
            {
                await HandleEventAsync(value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "processing redis node event");
            }
        }).ConfigureAwait(false);

        _heartbeatTask = Task.Run(HeartbeatLoop, _cancellationTokenSource.Token);
    }

    private async Task HeartbeatLoop()
    {
        while (!_cancellationTokenSource.IsCancellationRequested)
            try
            {
                await Task.Delay(_heartbeatInterval, _cancellationTokenSource.Token).ConfigureAwait(false);
                await RefreshSelfAsync().ConfigureAwait(false);
                await PublishAsync(RedisNodeEvent.Refresh(CreateInfo(DateTime.UtcNow))).ConfigureAwait(false);
                await RemoveExpiredNodesAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "maintaining redis node registry");
            }
    }

    private async Task RefreshSelfAsync()
    {
        var now = DateTime.UtcNow.ToString("O");
        var key = BuildNodeKey(_nodeAddress);
        var entries = new HashEntry[]
        {
            new("NodeAddress", _nodeAddress),
            new("SignalHub", _signalHub ?? string.Empty),
            new("Sdp", _sdp ?? string.Empty),
            new("LastSeenUtc", now)
        };

        await _database.HashSetAsync(key, entries).ConfigureAwait(false);
        await _database.KeyExpireAsync(key, _nodeEntryTtl).ConfigureAwait(false);
        await _database.SetAddAsync(_nodeSetKey, _nodeAddress).ConfigureAwait(false);
    }

    private async Task RemoveExpiredNodesAsync()
    {
        var members = await _database.SetMembersAsync(_nodeSetKey).ConfigureAwait(false);

        foreach (var member in members)
        {
            var address = member.ToString();
            if (address == _nodeAddress)
                continue;

            var key = BuildNodeKey(address);
            if (await _database.KeyExistsAsync(key).ConfigureAwait(false)) continue;

            await _database.SetRemoveAsync(_nodeSetKey, member).ConfigureAwait(false);
            await PublishAsync(RedisNodeEvent.Leave(address)).ConfigureAwait(false);
            await InvokeNodeRemovedAsync(address).ConfigureAwait(false);
        }
    }

    private async Task HandleEventAsync(RedisValue value)
    {
        if (value.IsNullOrEmpty)
            return;

        RedisNodeEvent? nodeEvent;
        try
        {
            nodeEvent = JsonSerializer.Deserialize<RedisNodeEvent>(value.ToString());
        }
        catch (JsonException ex)
        {
            Logger.LogException(ex, "deserializing redis node event");
            return;
        }

        if (nodeEvent == null)
            return;

        if (nodeEvent.NodeAddress == _nodeAddress)
            return;

        switch (nodeEvent.EventType)
        {
            case RedisNodeEventType.Join:
            case RedisNodeEventType.Refresh when nodeEvent.Node != null:
                if (nodeEvent.Node != null)
                    await InvokeNodeDiscoveredAsync(nodeEvent.Node.Value).ConfigureAwait(false);
                break;
            case RedisNodeEventType.Leave:
                if (!string.IsNullOrEmpty(nodeEvent.NodeAddress))
                    await InvokeNodeRemovedAsync(nodeEvent.NodeAddress).ConfigureAwait(false);
                break;
        }
    }

    private async Task<RedisNodeInfo?> GetNodeInfoAsync(string nodeAddress)
    {
        var key = BuildNodeKey(nodeAddress);
        var entries = await _database.HashGetAllAsync(key).ConfigureAwait(false);
        if (entries.Length == 0)
            return null;

        var dict = entries.ToDictionary(entry => entry.Name.ToString(), entry => entry.Value.ToString());

        return new RedisNodeInfo(
            dict.GetValueOrDefault("NodeAddress", nodeAddress),
            dict.GetValueOrDefault("SignalHub", string.Empty),
            dict.GetValueOrDefault("Sdp", string.Empty),
            ParseTimestamp(dict.GetValueOrDefault("LastSeenUtc"))
        );
    }

    private RedisNodeInfo CreateInfo(DateTime timestampUtc)
    {
        return new RedisNodeInfo(_nodeAddress, _signalHub ?? string.Empty, _sdp ?? string.Empty, timestampUtc);
    }

    private static DateTime ParseTimestamp(string? value)
    {
        if (DateTime.TryParse(value, out var timestamp))
            return timestamp.ToUniversalTime();

        return DateTime.UtcNow;
    }

    private async Task PublishAsync(RedisNodeEvent nodeEvent)
    {
        var payload = JsonSerializer.Serialize(nodeEvent);
        await _subscriber.PublishAsync(_channelName, payload).ConfigureAwait(false);
    }

    private async Task InvokeNodeDiscoveredAsync(RedisNodeInfo info)
    {
        if (NodeDiscovered == null)
            return;

        var handlers = NodeDiscovered.GetInvocationList().Cast<Func<RedisNodeInfo, Task>>();
        foreach (var handler in handlers)
            try
            {
                await handler(info).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"handling redis discovery for {info.NodeAddress}");
            }
    }

    private async Task InvokeNodeRemovedAsync(string nodeAddress)
    {
        if (NodeRemoved == null)
            return;

        var handlers = NodeRemoved.GetInvocationList().Cast<Func<string, Task>>();
        foreach (var handler in handlers)
            try
            {
                await handler(nodeAddress).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"handling redis removal for {nodeAddress}");
            }
    }

    private string BuildNodeKey(string nodeAddress)
    {
        return $"{_nodeKeyPrefix}:{nodeAddress}";
    }

    private sealed record class RedisNodeEvent(RedisNodeEventType EventType, RedisNodeInfo? Node, string NodeAddress)
    {
        public static RedisNodeEvent Join(RedisNodeInfo node)
        {
            return new RedisNodeEvent(RedisNodeEventType.Join, node, node.NodeAddress);
        }

        public static RedisNodeEvent Refresh(RedisNodeInfo node)
        {
            return new RedisNodeEvent(RedisNodeEventType.Refresh, node, node.NodeAddress);
        }

        public static RedisNodeEvent Leave(string nodeAddress)
        {
            return new RedisNodeEvent(RedisNodeEventType.Leave, null, nodeAddress);
        }
    }

    private enum RedisNodeEventType
    {
        Join,
        Refresh,
        Leave
    }

    public readonly record struct RedisNodeInfo(string NodeAddress, string SignalHub, string Sdp, DateTime LastSeenUtc);
}