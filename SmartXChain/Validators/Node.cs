using System.Net;
using System.Net.Sockets;
using SmartXChain.BlockchainCore;
using SmartXChain.Server;
using SmartXChain.Utils;

namespace SmartXChain.Validators;

/// <summary>
///     Represents a blockchain node responsible for server discovery, registration, and synchronization.
/// </summary>
public class Node
{
    /// <summary>
    ///     A list of IP addresses for nodes currently known to the system.
    /// </summary>
    public static List<string> CurrentNodeIPs = new();

    internal static List<string> DiscoveredServers = new();

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
    ///     Gets the blockchain chain identifier associated with this node.
    /// </summary>
    public string ChainId { get; }

    /// <summary>
    ///     Gets the address of the node.
    /// </summary>
    public string NodeAddress { get; }

    /// <summary>
    ///     Gets or sets the startup result containing the blockchain and node details.
    /// </summary>
    internal BlockchainServer.NodeStartupResult StartupResult { get; set; }

    /// <summary>
    ///     Starts the node by discovering, registering with, and synchronizing with peer servers.
    /// </summary>
    /// <param name="localRegistrationServer">Specifies whether to use the local registration server.</param>
    /// <returns>A Task resolving to the initialized <see cref="Node" />.</returns>
    internal static async Task<Node> Start(bool localRegistrationServer = false)
    {
        // Determine the node's IP address
        var ip = NetworkUtils.IP;
        if (string.IsNullOrEmpty(ip)) ip = NetworkUtils.GetLocalIP();
        if (localRegistrationServer) ip = "127.0.0.1";

        var nodeAddress = $"http://{ip}:{Config.Default.Port}";
        var chainId = Config.Default.ChainId;

        var node = new Node(nodeAddress, chainId);
        Logger.LogMessage("Starting automatic server discovery...");

        // Discover servers from the configuration
        var peers = Config.Default.Peers;
        DiscoveredServers = node.DiscoverServers(peers);

        // Retry server discovery if no active servers are found
        if (DiscoveredServers.Count == 0)
        {
            Logger.LogMessage("No active servers found. Waiting for a server...");
            while (DiscoveredServers.Count == 0)
            {
                await Task.Delay(5000);
                DiscoveredServers = node.DiscoverServers(peers);
            }
        }

        // Filter out the local node's own IP address
        DiscoveredServers = DiscoveredServers
            .Where(ip => !ip.Contains(NetworkUtils.IP) && !ip.Contains(NetworkUtils.GetLocalIP())).ToList();

        // Register with a discovery server
        Logger.LogMessage("Registering with a discovery server...");
        await node.RegisterWithDiscoveryAsync(DiscoveredServers);
        Logger.LogMessage("Node successfully registered.");

        // Send periodic heartbeats to the servers
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    foreach (var ip in CurrentNodeIPs)
                        lock (DiscoveredServers)
                        {
                            if (!DiscoveredServers.Contains(ip)) DiscoveredServers.Add(ip);
                        }

                    foreach (var server in DiscoveredServers)
                        if (node != null && node.StartupResult != null)
                        {
                            await node.SendHeartbeatAsync(server);
                            var newChain = await UpdateBlockchainWithMissingBlocks(node.StartupResult.Blockchain,
                                node.StartupResult.Node);
                            if (newChain != null) node.StartupResult.Blockchain = newChain;
                        }
                        else if (node != null && node.StartupResult == null && BlockchainServer.Startup != null)
                        {
                            try
                            {
                                if (!server.Contains(NetworkUtils.IP))
                                {
                                    var response = await SocketManager.GetInstance(server)
                                        .SendMessageAsync("GetChain#" + node.NodeAddress);

                                    var remoteChain = Blockchain.FromBase64(response);

                                    if (BlockchainServer.Startup.Blockchain != null)
                                    {
                                        lock (BlockchainServer.Startup.Blockchain)
                                        {
                                            if (remoteChain != null &&
                                                remoteChain.Chain.Count >=
                                                BlockchainServer.Startup.Blockchain.Chain.Count &&
                                                remoteChain.IsValid())
                                            {
                                                BlockchainServer.Startup.Blockchain = remoteChain;
                                                node.StartupResult = BlockchainServer.Startup;
                                                SaveBlockChain(remoteChain, node);
                                            }
                                        }

                                        Logger.LogMessage(
                                            $"GetChain request to {server} success: Blockchain blocks: {BlockchainServer.Startup.Blockchain.Chain.Count}");
                                    }
                                    if (Config.Default.Debug)
                                        Logger.LogMessage($"Response from server {server}: {response}");
                                } 
                            }
                            catch (Exception ex)
                            {
                                Logger.LogMessage($"Error sending GetChain request to {server}: {ex.Message}");
                            }
                        }
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error sending heartbeat: {ex.Message}");
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
                    foreach (var server in DiscoveredServers)
                    {
                        var nodeIPList = await node.GetRegisteredNodesAsync(server);
                        foreach (var nodeIP in nodeIPList)
                            if (!string.IsNullOrEmpty(nodeIP) && !CurrentNodeIPs.Contains(nodeIP))
                                lock (CurrentNodeIPs)
                                {
                                    CurrentNodeIPs.Add(nodeIP);
                                }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error retrieving nodes: {ex.Message}");
                }

                Thread.Sleep(30000);
            }
        });

        return node;
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
                if (remoteNode.Contains(NetworkUtils.IP))
                    continue;

                if (Config.Default.Debug)
                    Logger.LogMessage($"Checking blockchain with node {remoteNode}...");

                // Retrieve blockchain size from remote node
                var blockchainSizeResponse =
                    await SocketManager.GetInstance(remoteNode)
                        .SendMessageAsync($"GetBlockCount:{node.NodeAddress}:{blockchain.Chain.Count}");

                if (!int.TryParse(blockchainSizeResponse, out var remoteBlockCount))
                {
                    Logger.LogMessage($"Invalid blockchain size response from node {remoteNode}.");
                    continue;
                }

                if (currentBlockCount >= remoteBlockCount)
                {
                    if (Config.Default.Debug)
                        Logger.LogMessage($"Local blockchain is up-to-date compared to node {remoteNode}.");
                    continue;
                }

                // Validate the remote blockchain
                var isRemoteBlockchainValid =
                    await SocketManager.GetInstance(remoteNode).SendMessageAsync("ValidateChain") == "ok";

                if (!isRemoteBlockchainValid)
                {
                    Logger.LogMessage($"Remote blockchain from node {remoteNode} is invalid.");
                    continue;
                }

                Logger.LogMessage($"Synchronizing with node {remoteNode}...");
                // Fetch and add missing blocks
                for (var i = currentBlockCount; i < remoteBlockCount; i++)
                {
                    var blockResponse = await SocketManager.GetInstance(remoteNode).SendMessageAsync($"GetBlock/{i}");
                    var block = Block.FromBase64(blockResponse);

                    if (block != null)
                    {
                        blockchain.AddBlock(block, true, false, i);
                        Logger.LogMessage($"Block {i} added.");
                    }
                }

                SaveBlockChain(blockchain, node);
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error during blockchain synchronization: {ex.Message}");
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
    public static bool SaveBlockChain(Blockchain blockchain, Node node)
    {
        var chainPath = Path.Combine(Config.Default.BlockchainPath, "chain-" + node.ChainId);
        if (blockchain.Save(chainPath) && File.Exists(chainPath))
        {
            blockchain = Blockchain.Load(chainPath);
            Logger.LogMessage($"Blockchain saved to {chainPath}");
            return true;
        }

        Logger.LogMessage($"Blockchain could not be saved to {chainPath}");
        return false;
    }

    /// <summary>
    ///     Registers the node with discovery servers.
    /// </summary>
    /// <param name="discoveryServers">List of discovery server addresses.</param>
    public async Task RegisterWithDiscoveryAsync(List<string> discoveryServers)
    {
        foreach (var serverAddress in discoveryServers) await RegisterWithServerAsync(serverAddress);
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

            if (Config.Default.Debug)
                Logger.LogMessage($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error registering with server {serverAddress}: {ex.Message}");
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
        if (serverAddress.Contains(NetworkUtils.IP))
            return ret;

        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync("GetNodes");

            if (response == "ERROR: Timeout" || string.IsNullOrEmpty(response))
            {
                Logger.LogMessage($"Error: Timeout from server {serverAddress}");
                return ret;
            }
            if (string.IsNullOrEmpty(response))
            {
                Logger.LogMessage($"No new nodes received from {serverAddress}");
                return ret;
            }

            foreach (var nodeAddress in response.Split(','))
                if (!string.IsNullOrEmpty(nodeAddress))
                    ret.Add(nodeAddress);

            if (Config.Default.Debug)
                Logger.LogMessage($"Active nodes from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error retrieving registered nodes from {serverAddress}: {ex.Message}");
        }

        return ret;
    }


    /// <summary>
    ///     Sends a heartbeat signal to a specific server to notify it that the node is active.
    /// </summary>
    /// <param name="serverAddress">The address of the server to send the heartbeat to.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public async Task SendHeartbeatAsync(string serverAddress)
    {
        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync($"Heartbeat:{NodeAddress}");
            if (Config.Default.Debug)
            {
                Logger.LogMessage($"Heartbeat sent to {serverAddress}");
                Logger.LogMessage($"Response from server {serverAddress}: {response}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error sending heartbeat to {serverAddress}: {ex.Message}");
        }
    }


    /// <summary>
    ///     Discovers servers by performing DNS resolution and validation on a list of static servers.
    /// </summary>
    /// <param name="staticServers">List of static server addresses for discovery.</param>
    /// <returns>A list of valid server addresses discovered during the process.</returns>
    public List<string> DiscoverServers(List<string> staticServers)
    {
        try
        {
            foreach (var server in staticServers)
            {
                if (!server.StartsWith("http://"))
                    continue;

                var serverAddress = server.Replace("http://", "").Trim();
                var parts = serverAddress.Split(':');

                if (parts.Length != 2)
                {
                    Logger.LogMessage("Invalid server address format: " + server);
                    continue;
                }

                var host = parts[0];
                var port = parts[1];

                try
                {
                    var ipAddresses = Dns.GetHostAddresses(host)
                        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork) // Only IPv4 addresses
                        .Select(ip => $"http://{ip}:{port}")
                        .ToList();

                    if (ipAddresses.Any())
                    {
                        Logger.LogMessage("DNS discovery successful: " + string.Join(", ", ipAddresses));
                        return ipAddresses;
                    }
                }
                catch (Exception dnsEx)
                {
                    Logger.LogMessage($"DNS resolution failed for {host}: {dnsEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage("Error during server discovery: " + ex.Message);
        }

        return staticServers;
    }
}