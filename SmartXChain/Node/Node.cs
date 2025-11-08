using System.Collections.Concurrent; 
using SmartXChain.Utils;
using static SmartXChain.ClientServer.BlockchainServer;

namespace SmartXChain.Validators;

/// <summary>
///     Represents a blockchain node responsible for server discovery, registration, and synchronization.
/// </summary>
public class Node
{
    private const int TimeoutSeconds = 60;


    /// <summary>
    ///     Sends a heartbeat signal to a specific server to notify it that the node is active.
    /// </summary>
    /// <returns>A Task representing the asynchronous operation.</returns>
    private static readonly ConcurrentDictionary<string, DateTime> LastResponseTimes = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="Node" /> class.
    /// </summary>
    /// <param name="nodeAddress">The address of the node.</param>
    /// <param name="chainId">The identifier of the blockchain chain.</param>
    public Node(string nodeAddress, string chainId)
    {
        NodeAddress = nodeAddress;
        ChainId = chainId;
    }

    /// <summary>
    ///     A Thread safe list of IP addresses for nodes currently known to the system.
    /// </summary>
    public static ConcurrentList<string> CurrentNodes { get; set; } = new();
    public static ConcurrentDictionary <string,string> CurrentNodes_SDP{ get; set; } = new();

    /// <summary>
    ///     A dictionary of IP addresses for nodes with las activity currently known to the system.
    /// </summary>
    public static ConcurrentDictionary<string, DateTime> CurrentNodes_LastActive { get; set; } = new();

    /// <summary>
    ///     Gets the blockchain chain identifier associated with this node.
    /// </summary>
    public string ChainId { get; }

    /// <summary>
    ///     Gets the address of the node.
    /// </summary>
    public string NodeAddress { get; }

    /// <summary>
    ///     Starts the node by discovering, registering with, and synchronizing with peer servers.
    /// </summary>
    /// <returns>A Task resolving to the initialized <see cref="Node" />.</returns>
    internal static async Task<Node> Start()
    {
        var nodeAddress = Config.Default.NodeAddress;

        Logger.Log($"Starting node at {nodeAddress}...");

        var chainId = Config.Default.ChainId;

        var node = new Node(nodeAddress, chainId);
        Logger.Log("Starting server discovery...");


        // Retry server discovery if no active servers are found
        if (Config.Default.SignalHubs.Count == 0)
        {
            Logger.Log("No active signalHubs found. Waiting for a server...");
            while (Config.Default.SignalHubs.Count == 0)
            {
                await Task.Delay(5000);
            }
        }
        return node;
    }


    /// <summary>
    ///     Removes a node from CurrentNodes and CurrentNodes_LastActive
    /// </summary>
    /// <param name="nodeAddress"></param>
    public static void RemoveNodeAddress(string nodeAddress)
    {
        lock (CurrentNodes)
        {
            if (CurrentNodes.Contains(nodeAddress))
            {
                var tempList = new List<string>();

                while (CurrentNodes.TryTake(out var currentIp))
                    if (!currentIp.Equals(nodeAddress, StringComparison.OrdinalIgnoreCase))
                        tempList.Add(currentIp);
                    else
                        Logger.Log($"Node removed {nodeAddress}...");

                CurrentNodes.Clear();
                foreach (var remainingIp in tempList) CurrentNodes.Add(remainingIp);

                CurrentNodes_LastActive.TryRemove(nodeAddress, out _);
                CurrentNodes_SDP.TryRemove(nodeAddress, out _);
            }
        }
    }


    /// <summary>
    ///     Adds a node and updates CurrentNodes_LastActive, ensuring no duplicates (URLs or IPs).
    /// </summary>
    /// <param name="server">The server NodeAddress</param>
    /// <param name="sdp">Webrtc offer</param>
    public static void AddNode(string server, string sdp)
    {
        CurrentNodes_SDP[server] = sdp;
        if (!CurrentNodes.Contains(server))
        {
            CurrentNodes.Add(server);
            if (Config.Default.Debug) Logger.Log($"New server added: {server}");
        }
        CurrentNodes_LastActive[server] = DateTime.UtcNow;
    }
}