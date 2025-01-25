using System.Collections.Concurrent;
using System.Text.Json; 
using SmartXChain.BlockchainCore;
using SmartXChain.Server;
using SmartXChain.Utils;
using static SmartXChain.Server.BlockchainServer;

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
    public static ConcurrentList<string> CurrentNodeIPs { get; set; } = new();

    /// <summary>
    ///     A dictionary of IP addresses for nodes with las activity currently known to the system.
    /// </summary>
    public static ConcurrentDictionary<string, DateTime> CurrentNodeIP_LastActive { get; set; } = new();

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
        var nodeAddress = Config.Default.URL;
        Logger.Log($"Starting node at {nodeAddress}...");

        var chainId = Config.Default.ChainId;

        var node = new Node(nodeAddress, chainId);
        Logger.Log("Starting server discovery...");

        // Discover servers from the configuration
        var peers = Config.Default.Peers;

        var ipList = new List<string>();
        foreach (var staticIP in node.GetStaticServers(peers))
            if (!ipList.Contains(staticIP))
                ipList.Add(staticIP);


        // Retry server discovery if no active servers are found
        if (ipList.Count == 0)
        {
            Logger.Log("No active servers found. Waiting for a server...");
            while (ipList.Count == 0)
            {
                await Task.Delay(5000);
                foreach (var staticIP in node.GetStaticServers(peers))
                    if (!ipList.Contains(staticIP))
                        ipList.Add(staticIP);
            }
        }

        // Filter out the local node's own IP address
        ipList = ipList
            .Where(ip => !ip.Contains(Config.Default.URL) &&
                         !ip.Contains(Config.Default.ResolvedURL)).ToList();

        // Register with a discovery server
        await node.RegisterWithDiscoveryAsync(ipList);
          
        // Periodically retrieve registered nodes
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    foreach (var server in CurrentNodeIPs)
                    {
                        var nodeIPList = await GetRegisteredNodesAsync(server);

                        foreach (var nodeIP in nodeIPList)
                            AddNodeIP(nodeIP); 
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "retrieving nodes");
                }

                Thread.Sleep(10000);
            }
        });

        return node;
    }

    private List<string> GetStaticServers(List<string> urls)
    {
        var resolvedUrls = new List<string>();

        foreach (var url in urls)
            try
            {
                var resolvedUrl = NetworkUtils.ResolveUrlToIp(url);
                if (!string.IsNullOrEmpty(resolvedUrl)) resolvedUrls.Add(resolvedUrl);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "server discovery failed");
            }

        return resolvedUrls;
    }

    /// <summary>
    ///     Removes a node from CurrentNodeIPs and CurrentNodeIP_LastActive
    /// </summary>
    /// <param name="ip"></param>
    public static void RemoveNodeIP(string ip)
    {
        lock (CurrentNodeIPs)
        {
            if (Config.Default.Peers.Contains(ip)) 
                return; 

            if (CurrentNodeIPs.Contains(ip))
            {
                var tempList = new List<string>();

                while (CurrentNodeIPs.TryTake(out var currentIp))
                    if (!currentIp.Equals(ip, StringComparison.OrdinalIgnoreCase))
                        tempList.Add(currentIp);
                    else
                        Logger.Log($"Node removed {ip}...");

                CurrentNodeIPs.Clear();
                foreach (var remainingIp in tempList) CurrentNodeIPs.Add(remainingIp);

                CurrentNodeIP_LastActive.TryRemove(ip, out _);

                SocketManager.RemoveInstance(ip);
            }           
        } 
    }


    /// <summary>
    ///     Adds a node IP and updates CurrentNodeIP_LastActive, ensuring no duplicates (URLs or IPs).
    /// </summary>
    /// <param name="server">The server URL</param>
    public static void AddNodeIP(string server)
    {
        // Resolve the server URL to its IP
        var resolvedServerIp = NetworkUtils.ResolveUrlToIp(server);
         
        if (string.IsNullOrEmpty(resolvedServerIp))
        {
            if (Config.Default.Debug)
            {
                Logger.Log($"Failed to resolve IP for server: {server}");
            }
            return;
        }
         
        foreach (var node in CurrentNodeIPs)
        {
            var resolvedNodeIp = NetworkUtils.ResolveUrlToIp(node);

            if (resolvedServerIp == resolvedNodeIp)
            {
                if (resolvedNodeIp!=server && CurrentNodeIPs.Contains(resolvedNodeIp))
                {
                    RemoveNodeIP(resolvedNodeIp);
                    CurrentNodeIPs.Add(server); 
                    CurrentNodeIP_LastActive[server] = DateTime.UtcNow;
                }
                return; 
            }
        }
         
        if (NetworkUtils.IsValidServer(server))
        { 
            CurrentNodeIP_LastActive[server] = DateTime.UtcNow; 
            CurrentNodeIPs.Add(server);

            if (Config.Default.Debug)
            {
                Logger.Log($"New server added: {server}");
            }
        }
    }


    /// <summary>
    ///     Registers the node with discovery servers.
    /// </summary>
    /// <param name="discoveryServers">List of discovery server addresses.</param>
    public async Task RegisterWithDiscoveryAsync(List<string> discoveryServers)
    {
        Logger.Log($"Registering with {discoveryServers.Count} discovery servers...");
        foreach (var serverAddress in discoveryServers)
            await RegisterWithServerAsync(serverAddress);
    }

    /// <summary>
    ///     Registers the node with a specific server.
    /// </summary>
    /// <param name="serverAddress">The address of the server to register with.</param>
    private async Task RegisterWithServerAsync(string serverAddress)
    {
        try
        {
            var signature = Crypt.GenerateHMACSignature(NodeAddress, Config.Default.ChainId);
            var response = await SocketManager.GetInstance(serverAddress)
                .SendMessageAsync($"Register:{NodeAddress}|{signature}");

            if (response.Contains("ok")) 
                AddNodeIP(serverAddress);  

            if (Config.Default.Debug)
                Logger.Log($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"RegisterWithServerAsync: registering with server {serverAddress} failed");
        }
    }
}