using System.Collections.Concurrent;
using System.Net;
using SmartXChain.BlockchainCore;
using SmartXChain.Server;
using SmartXChain.Utils;

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
    /// <param name="serverAddress">The address of the server to send the heartbeat to.</param>
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
    ///     A list of IP addresses for nodes currently known to the system.
    /// </summary>
    public static ConcurrentBag<string> CurrentNodeIPs { get; set; } = new();

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
    /// <param name="localRegistrationServer">Specifies whether to use the local registration server.</param>
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
                         !ip.Contains(Config.Default.URL)).ToList();

        // Register with a discovery server
        await node.RegisterWithDiscoveryAsync(ipList);

        // Send periodic heartbeats to the servers
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    foreach (var server in CurrentNodeIPs)
                    {
                        var alive = node != null && await node.SendHeartbeatAsync(server);
                        if (alive)
                        {
                            Blockchain? blockchain = null;
                            if (BlockchainServer.Startup != null) blockchain = BlockchainServer.Startup.Blockchain;

                            if (blockchain != null)
                            {
                                var newChain = await UpdateBlockchainWithMissingBlocks(blockchain, node);
                                if (newChain != null) BlockchainServer.Startup.Blockchain = newChain;
                            }
                            else
                            {
                                try
                                {
                                    if (!server.Contains(Config.Default.URL))
                                    {
                                        var response = await SocketManager.GetInstance(server)
                                            .SendMessageAsync("GetChain#" + node.NodeAddress);

                                        if (response.ToLower().Contains("error"))
                                        {
                                            Logger.Log($"No chaindata from {server}: {response}");
                                        }
                                        else
                                        {
                                            var remoteChain = Blockchain.FromBase64(response);

                                            if (BlockchainServer.Startup != null &&
                                                BlockchainServer.Startup.Blockchain != null)
                                            {
                                                lock (BlockchainServer.Startup.Blockchain)
                                                {
                                                    if (remoteChain != null &&
                                                        remoteChain.Chain != null &&
                                                        BlockchainServer.Startup.Blockchain.Chain != null &&
                                                        remoteChain.Chain.Count >=
                                                        BlockchainServer.Startup.Blockchain.Chain.Count &&
                                                        remoteChain.IsValid())
                                                    {
                                                        BlockchainServer.Startup.Blockchain = remoteChain;
                                                        SaveBlockChain(remoteChain, node);
                                                    }
                                                }

                                                Logger.Log(
                                                    $"GetChain request to {server} success: Blockchain blocks: {BlockchainServer.Startup.Blockchain.Chain.Count}");
                                            }

                                            if (Config.Default.Debug)
                                                Logger.Log($"Response from server {server}: {response}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogException(ex, $"sending GetChain request to {server}"); 
                                }
                            }
                        }
                        else
                        {
                            RemoveNodeIP(server);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"sending heartbeat"); 
                }

                Thread.Sleep(20000);
            }
        });

        // Periodically retrieve registered nodes
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    foreach (var server in CurrentNodeIPs)
                    {
                        var nodeIPList = await node.GetRegisteredNodesAsync(server);

                        foreach (var nodeIP in nodeIPList)
                            if (!string.IsNullOrEmpty(nodeIP) && !CurrentNodeIPs.Contains(nodeIP))
                                CurrentNodeIPs.Add(nodeIP);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"retrieving nodes"); 
                }

                Thread.Sleep(30000);
            }
        });

        return node;
    }

    private static string ResolveUrlToIp(string url)
    {
        try
        {
            var uri = new Uri(url);

            var addresses = Dns.GetHostAddresses(uri.Host);

            if (addresses.Length > 0) return uri.Scheme + "://" + addresses[0] + ":" + uri.Port;
        }
        catch (Exception ex)
        {
        }

        return url;
    }

    private List<string> GetStaticServers(List<string> urls)
    {
        var resolvedUrls = new List<string>();

        foreach (var url in urls)
            try
            {
                var resolvedUrl = ResolveUrlToIp(url);
                if (!string.IsNullOrEmpty(resolvedUrl)) resolvedUrls.Add(resolvedUrl);
            }
            catch (Exception ex)
            {
                Logger.Log("ERROR: server discovery failed: " + ex.Message);
            }

        return resolvedUrls;
    }

    /// <summary>
    ///     Removes a node from CurrentNodeIPs and CurrentNodeIP_LastActive
    /// </summary>
    /// <param name="ip"></param>
    public static void RemoveNodeIP(string ip)
    {
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

    /// <summary>
    ///     and updates CurrentNodeIP_LastActive
    /// </summary>
    /// <param name="server"></param>
    public static void AddNodeIP(string server)
    {
        CurrentNodeIP_LastActive.TryAdd(server, DateTime.UtcNow);
        CurrentNodeIP_LastActive[server] = DateTime.UtcNow;

        if (!CurrentNodeIPs.Contains(server))
            CurrentNodeIPs.Add(server);
    }


    /// <summary>
    ///     Updates the local blockchain with missing blocks from peer nodes.
    /// </summary>
    /// <param name="blockchain">The local blockchain instance to update.</param>
    /// <param name="node">The node instance managing the blockchain.</param>
    /// <returns>A Task resolving to the updated <see cref="Blockchain" /> or null if no updates were made.</returns>
    internal static async Task<Blockchain?> UpdateBlockchainWithMissingBlocks(Blockchain? blockchain, Node node)
    {
        if (blockchain == null || blockchain.Chain == null)
            return null;

        var currentBlockCount = blockchain.Chain.Count;

        try
        {
            foreach (var remoteNode in CurrentNodeIPs)
            {
                var alive = await node.SendHeartbeatAsync(remoteNode);
                if (alive)
                {
                    if (remoteNode.Contains(Config.Default.URL))
                        continue;

                    if (Config.Default.Debug)
                        Logger.Log($"Checking blockchain with node {remoteNode}...");

                    // Retrieve blockchain size from remote node
                    var blockchainSizeResponse =
                        await SocketManager.GetInstance(remoteNode)
                            .SendMessageAsync($"BlockCount:{node.NodeAddress}:{blockchain.Chain.Count}");

                    if (!int.TryParse(blockchainSizeResponse, out var remoteBlockCount))
                    {
                        Logger.Log($"Invalid blockchain size response from node {remoteNode}.");
                        continue;
                    }

                    if (currentBlockCount >= remoteBlockCount)
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"Local blockchain is up-to-date compared to node {remoteNode}.");
                        continue;
                    }

                    // Validate the remote blockchain
                    var isRemoteBlockchainValid =
                        await SocketManager.GetInstance(remoteNode).SendMessageAsync("ValidateChain") == "ok";

                    if (!isRemoteBlockchainValid)
                    {
                        Logger.Log($"Remote blockchain from node {remoteNode} is invalid.");
                        continue;
                    }

                    Logger.Log($"Synchronizing with node {remoteNode}...");
                    // Fetch and add missing blocks
                    for (var i = currentBlockCount; i < remoteBlockCount; i++)
                    {
                        var blockResponse =
                            await SocketManager.GetInstance(remoteNode).SendMessageAsync($"GetBlock/{i}");
                        var block = Block.FromBase64(blockResponse);

                        if (block != null)
                        {
                            blockchain.AddBlock(block, true, false, i);
                            Logger.Log($"Block {i} added.");
                        }
                    }

                    SaveBlockChain(blockchain, node);
                }
                else
                {
                    RemoveNodeIP(remoteNode);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"during blockchain synchronization"); 
        }

        return blockchain;
    }

    /// <summary>
    ///     Saves the blockchain to the specified BlockchainPath.
    /// </summary>
    /// <param name="blockchain">The blockchain instance to be saved.</param>
    /// <param name="node">The node associated with the blockchain, used to identify the chain ID.</param>
    /// <returns>
    ///     Returns <c>true</c> if the blockchain was successfully saved and verified;
    ///     otherwise, <c>false</c>.
    /// </returns>
    public static bool SaveBlockChain(Blockchain? blockchain, Node node)
    {
        var chainPath = Path.Combine(Config.Default.BlockchainPath, "chain-" + node.ChainId);
        if (blockchain.Save(chainPath) && File.Exists(chainPath))
        {
            blockchain = Blockchain.Load(chainPath);
            Logger.Log($"Blockchain saved to {chainPath}");
            return true;
        }

        Logger.Log($"Blockchain could not be saved to {chainPath}");
        return false;
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

            if (response.Contains("ok") && !CurrentNodeIPs.Contains(serverAddress))
            {
                AddNodeIP(serverAddress);
                Logger.Log($"{serverAddress} added to node servers");
            }

            if (Config.Default.Debug)
                Logger.Log($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"registering with server {serverAddress} failed"); 
        }
    }


    /// <summary>
    ///     Retrieves the list of registered nodes from a specific server.
    /// </summary>
    /// <param name="serverAddress">The address of the server to query for registered nodes.</param>
    /// <returns>A list of node addresses retrieved from the server.</returns>
    public async Task<List<string>> GetRegisteredNodesAsync(string serverAddress)
    {
        var ret = new List<string>();
        if (serverAddress.Contains(Config.Default.URL))
            return ret;

        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync("Nodes");

            if (response == "ERROR: Timeout")
            {
                Logger.LogError($"Timeout from server {serverAddress}");
                RemoveNodeIP(serverAddress);
                return ret;
            }

            if (string.IsNullOrEmpty(response))
            {
                if (Config.Default.Debug)
                    Logger.Log($"No new nodes received from {serverAddress}");
                return ret;
            }

            foreach (var nodeAddress in response.Split(','))
                if (!string.IsNullOrEmpty(nodeAddress))
                    ret.Add(nodeAddress);

            if (Config.Default.Debug)
                Logger.Log($"Active nodes from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"ERROR: retrieving registered nodes from {serverAddress} failed");
        }

        return ret;
    }

    /// <summary>
    ///     ^Sends heartbeat with own address
    /// </summary>
    /// <param name="serverAddress"></param>
    /// <returns></returns>
    public async Task<bool> SendHeartbeatAsync(string serverAddress)
    {
        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync($"Heartbeat:{NodeAddress}");

            if (Config.Default.Debug)
            {
                Logger.Log($"Heartbeat sent to {serverAddress}");
                Logger.Log($"Response from server {serverAddress}: {response}");
            }

            if (!string.IsNullOrEmpty(response))
                // Update the last response time
                LastResponseTimes[serverAddress] = DateTime.UtcNow;
            else
                Logger.Log($"No response received from {serverAddress}");

            // Check if the node is considered dead
            if (LastResponseTimes.TryGetValue(serverAddress, out var lastResponseTime))
            {
                var elapsed = DateTime.UtcNow - lastResponseTime;
                if (elapsed.TotalSeconds > TimeoutSeconds)
                {
                    Logger.Log(
                        $"Node {serverAddress} is considered dead (last response {elapsed.TotalSeconds} seconds ago).");
                    return false;
                }
            }
            else
            {
                // No response recorded yet, consider the node dead
                Logger.Log($"Node {serverAddress} has no recorded responses and is considered dead.");
                return false;
            }

            return true; // Node is alive
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"sending heartbeat to {serverAddress} failed"); 
            return false; // Node is considered dead due to exception
        }
    }

    /// <summary>
    ///     Reboot client chains
    /// </summary>
    /// <returns></returns>
    public async Task RebootChainsAsync()
    {
        if (Config.TestNet)
            foreach (var serverAddress in CurrentNodeIPs)
            {
                if (serverAddress == Config.Default.URL)
                    continue;

                try
                {
                    var response = await SocketManager.GetInstance(serverAddress)
                        .SendMessageAsync($"RebootChain:{NodeAddress}");

                    if (Config.Default.Debug)
                    {
                        Logger.Log($"RebootChain sent to {serverAddress}");
                        Logger.Log($"Response from server {serverAddress}: {response}");
                    }

                    Logger.Log(!string.IsNullOrEmpty(response)
                        ? $"ERROR: No response received from {serverAddress}"
                        : $"Shutdown initiated {serverAddress}");
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"sending Shutdown command to {serverAddress} failed"); 
                }
            }
    }
}