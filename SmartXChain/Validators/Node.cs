using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using SmartXChain.BlockchainCore;
using SmartXChain.Server;
using SmartXChain.Utils;

namespace SmartXChain.Validators;

public class Node
{
    public static List<string> CurrentNodeIPs = new();

    public Node(string nodeAddress, string chainId)
    {
        NodeAddress = nodeAddress;
        ChainId = chainId;
    }

    public string ChainId { get; }
    public string NodeAddress { get; }
    public BlockchainServer.NodeStartupResult StartupResult { get; set; }

    public static async Task<Node> Start(bool localRegistrationServer = false)
    {
        // Node Configuration 
        var ip = NetworkUtils.IP;
        if (ip == "") ip = NetworkUtils.GetLocalIP();
        if (localRegistrationServer)
            ip = "127.0.0.1";

        var nodeAddress = $"http://{ip}:{Config.Default.Port}"; // Own node address
        var chainId = Config.Default.SmartXchain;

        // Create a node instance
        var node = new Node(nodeAddress, chainId);

        Logger.LogMessage("Starting automatic server discovery...");

        // Known discovery servers (starting points for the search) 
        var peers = Config.Default.Peers;

        // Step 1: Query primary discovery servers
        var discoveredServers = node.DiscoverServers(peers);


        // Step 2: If no servers were found, start a loop to wait for servers
        if (discoveredServers.Count == 0)
        {
            Logger.LogMessage("No active servers found. Waiting for a server...");

            while (discoveredServers.Count == 0)
                try
                {
                    await Task.Delay(5000); // Check every 5 seconds
                    discoveredServers = node.DiscoverServers(peers);
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error during server discovery: {ex.Message}");
                }
        }

        discoveredServers = discoveredServers.Where(ip =>
            !ip.Contains(NetworkUtils.IP) &&
            !ip.Contains(NetworkUtils.GetLocalIP())).ToList();

        // Step 4: Register with an active server
        Logger.LogMessage("Registering with a discovery server...");
        await node.RegisterWithDiscoveryAsync(discoveredServers);

        Logger.LogMessage("Node successfully registered.");

        // Step 5: Send heartbeat (every 20 seconds)
        Task.Run(async () =>
        {
            while (true)
                try
                {
                    foreach (var ip in CurrentNodeIPs)
                        if (!discoveredServers.Contains(ip))
                            discoveredServers.Add(ip);

                    foreach (var server in discoveredServers)
                    {
                        await node.SendHeartbeatAsync(server);
                        if (node != null && node.StartupResult != null)
                            await UpdateBlockchainWithMissingBlocks(node.StartupResult.Blockchain,
                                node.StartupResult.Node);
                        else if (node != null && node.StartupResult == null)
                            try
                            {
                                var response = await SocketManager.GetInstance(server)
                                    .SendMessageAsync("GetChain#" + node.NodeAddress);

                                BlockchainServer.Startup.Blockchain = Blockchain.FromBase64(response);
                                node.StartupResult = BlockchainServer.Startup;

                                Logger.LogMessage(
                                    $"GetChain request to {server} success: Blockchain blocks: {BlockchainServer.Startup.Blockchain.Chain.Count}");
                                Logger.LogMessage($"Response from server {server}: {response}");
                            }
                            catch (Exception ex)
                            {
                                Logger.LogMessage($"Error sending GetChain request to {server}: {ex.Message}");
                            }
                    }

                    Thread.Sleep(20000); // Heartbeat interval
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error sending heartbeat: {ex.Message}");
                }
        });

        // Step 6: Retrieve registered nodes (every 30 seconds)
        Task.Run(async () =>
        {
            while (true)
                try
                {
                    foreach (var server in discoveredServers)
                    {
                        var nodeIPList = await node.GetRegisteredNodesAsync(server);

                        if (!nodeIPList.Contains(server))
                            nodeIPList.Add(server);
                        if (nodeIPList.Contains(nodeAddress))
                            nodeIPList.Remove(nodeAddress);

                        foreach (var nodeIP in nodeIPList)
                            if (!string.IsNullOrEmpty(nodeIP))
                                lock (CurrentNodeIPs)
                                {
                                    if (!CurrentNodeIPs.Contains(nodeIP))
                                        CurrentNodeIPs.Add(nodeIP);
                                }
                    }

                    Thread.Sleep(30000); // Retrieve nodes every 30 seconds
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error retrieving nodes: {ex.Message}");
                }
        });

        return node;
    }

    private static async Task<Blockchain?> UpdateBlockchainWithMissingBlocks(Blockchain blockchain, Node node)
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

                Logger.LogMessage($"Checking blockchain with node {remoteNode}...");

                // Step 1: Get blockchain size from remote node
                var blockchainSizeResponse = await SocketManager.GetInstance(remoteNode)
                    .SendMessageAsync("GetBlockCount");

                if (!int.TryParse(blockchainSizeResponse, out var remoteBlockCount))
                {
                    Logger.LogMessage($"Invalid blockchain size response from node {remoteNode}.");
                    continue;
                }

                if (currentBlockCount >= remoteBlockCount)
                {
                    Logger.LogMessage($"Local blockchain is up-to-date compared to node {remoteNode}.");
                    continue;
                }

                // Step 2: Validate the remote blockchain
                var isRemoteBlockchainValid = await SocketManager.GetInstance(remoteNode)
                    .SendMessageAsync("ValidateChain") == "ok";

                if (!isRemoteBlockchainValid)
                {
                    Logger.LogMessage($"Remote blockchain from node {remoteNode} is invalid.");
                    continue;
                }

                // Step 3: Synchronize missing blocks
                for (var i = 0; i < remoteBlockCount; i++)
                {
                    Logger.LogMessage($"Fetching block {i} from node {remoteNode}...");

                    var remoteBlockResponse = await SocketManager.GetInstance(remoteNode)
                        .SendMessageAsync($"GetBlock/{i}");

                    if (string.IsNullOrEmpty(remoteBlockResponse))
                    {
                        Logger.LogMessage($"Failed to fetch block {i} from node {remoteNode}.");
                        break;
                    }

                    var remoteBlock = Block.FromBase64(remoteBlockResponse); 

                    blockchain.AddBlock(remoteBlock, lockChain: true, mineBlock: false, i);
                    Logger.LogMessage($"Added block {i} to the local blockchain.");
                }

                Logger.LogMessage($"Synchronization with node {remoteNode} completed.");
                return blockchain;
            }

            Logger.LogMessage("No synchronization was necessary or possible.");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error during blockchain synchronization: {ex.Message}");
        }

        return blockchain;
    }

    private static async Task<Blockchain?> UpdateBlockchainWithMissingBlocks1(Blockchain blockchain, Node node)
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

                Logger.LogMessage($"Checking blockchain with node {remoteNode}...");

                var blockchainSizeResponse = await SocketManager.GetInstance(remoteNode)
                    .SendMessageAsync("GetBlockCount");

                if (!int.TryParse(blockchainSizeResponse, out var remoteBlockCount))
                {
                    Logger.LogMessage($"Invalid blockchain size response from node {remoteNode}.");
                    continue;
                }

                Logger.LogMessage(
                    $"Node {remoteNode} blockchain: {remoteBlockCount}, Local blockchain: {currentBlockCount}.");

                if (remoteBlockCount < currentBlockCount)
                {
                    Logger.LogMessage($"Local blockchain is longer than or equal to node {remoteNode}.");
                    continue;
                }

                // Validate remote blockchain from genesis block
                var isRemoteBlockchainValid = await SocketManager.GetInstance(remoteNode)
                    .SendMessageAsync("ValidateChain") == "ok";
                if (!isRemoteBlockchainValid)
                {
                    Logger.LogMessage($"Remote blockchain is invalid.");
                    continue;
                }

                for (var i = 0; i < Math.Min(currentBlockCount, remoteBlockCount); i++)
                {
                    var localBlock = blockchain.Chain[i];
                    var remoteBlockResponse = await SocketManager.GetInstance(remoteNode)
                        .SendMessageAsync($"GetBlock/{i}");

                    if (string.IsNullOrEmpty(remoteBlockResponse))
                    {
                        Logger.LogMessage($"Failed to retrieve block {i} from node {remoteNode}.");
                        isRemoteBlockchainValid = false;
                        break;
                    }

                    var remoteBlock = Block.FromBase64(remoteBlockResponse);

                    if (remoteBlock == null)
                    {
                        Logger.LogMessage($"Invalid block {i} received from node {remoteNode}.");
                        isRemoteBlockchainValid = false;
                        break;
                    }

                    // Compare hashes and timestamps for consistency
                    if (i > 0)
                    {
                        var previousRemoteBlock = Block.FromBase64(await SocketManager.GetInstance(remoteNode)
                            .SendMessageAsync($"GetBlock/{i - 1}"));

                        Logger.LogMessage($"Validating Block {i}: PreviousHash={remoteBlock.PreviousHash}, Expected={previousRemoteBlock?.Hash}");

                        if (previousRemoteBlock == null || remoteBlock.PreviousHash != previousRemoteBlock.Hash)
                        {
                            Logger.LogMessage($"Block {i} has an inconsistent PreviousHash in remote blockchain.");
                            isRemoteBlockchainValid = false;
                            break;
                        }

                        if (remoteBlock.Timestamp < previousRemoteBlock.Timestamp)
                        {
                            Logger.LogMessage($"Block {i} has a timestamp earlier than the previous block.");
                            isRemoteBlockchainValid = false;
                            break;
                        }
                    }

                    if (i < currentBlockCount && remoteBlock.Timestamp > localBlock.Timestamp)
                    {
                        Logger.LogMessage($"Local block {i} is older, preferring local blockchain.");
                        isRemoteBlockchainValid = false;
                        break;
                    }
                }

                if (!isRemoteBlockchainValid)
                {
                    Logger.LogMessage($"Remote blockchain from node {remoteNode} is invalid or inconsistent.");
                    continue;
                }

                Logger.LogMessage($"Remote blockchain from node {remoteNode} is preferred. Adding missing blocks...");

                // Synchronize missing blocks
                bool synchronizationSuccessful = true;
                var blockDownloadTasks = new List<Task<(int Index, Block? Block)>>();

                for (var i = currentBlockCount; i < remoteBlockCount; i++)
                {
                    blockDownloadTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var blockResponse = await SocketManager.GetInstance(remoteNode)
                                .SendMessageAsync($"GetBlock/{i}");

                            if (string.IsNullOrEmpty(blockResponse))
                                return (i, null);

                            var block = Block.FromBase64(blockResponse);
                            Logger.LogMessage($"Downloaded Block {i}: Hash={block?.Hash}, PreviousHash={block?.PreviousHash}");
                            return (i, block);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogMessage($"Error retrieving block {i} from node {remoteNode}: {ex.Message}");
                            return (i, null);
                        }
                    }));
                }

                var downloadedBlocks = await Task.WhenAll(blockDownloadTasks);

                foreach (var (index, block) in downloadedBlocks)
                {
                    if (block == null)
                    {
                        Logger.LogMessage($"Failed to retrieve block {index} from node {remoteNode}.");
                        synchronizationSuccessful = false;
                        break;
                    }

                    // Validate the block with the previous block in the chain
                    if (index > 0)
                    {
                        var previousBlock = blockchain.Chain[index - 1];

                        Logger.LogMessage($"Validating Downloaded Block {index}: PreviousHash={block.PreviousHash}, Expected={previousBlock.Hash}");

                        if (block.PreviousHash != previousBlock.Hash)
                        {
                            Logger.LogMessage($"Block {index} has an inconsistent PreviousHash.");
                            synchronizationSuccessful = false;
                            break;
                        }

                        if (block.Timestamp < previousBlock.Timestamp)
                        {
                            Logger.LogMessage($"Block {index} has a timestamp earlier than the previous block.");
                            synchronizationSuccessful = false;
                            break;
                        }
                    }

                    blockchain.AddBlock(block, true, false);
                    Logger.LogMessage($"Added block {index} to blockchain.");
                }

                if (synchronizationSuccessful)
                {
                    Logger.LogMessage(
                        $"Blockchain successfully updated to block {remoteBlockCount} from node {remoteNode}.");
                    Logger.LogMessage($"Blockchain fully synchronized with node {remoteNode}.");
                    return blockchain;
                }
                else
                {
                    Logger.LogMessage($"Synchronization with node {remoteNode} failed.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error during blockchain synchronization: {ex.Message}");
        }

        return blockchain;
    }

    public async Task RegisterWithDiscoveryAsync(List<string> discoveryServers)
    {
        foreach (var serverAddress in discoveryServers)
            try
            {
                if (serverAddress.Contains(NetworkUtils.IP))
                    continue;

                var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync("GetNodes");
                var activeServers = response.Split(',');
                foreach (var activeServer in activeServers)
                    if (!string.IsNullOrEmpty(activeServer))
                    {
                        if (activeServer.Contains(NetworkUtils.IP))
                            continue;
                        await RegisterWithServerAsync(activeServer);
                    }

                    else
                    {
                        Logger.LogMessage($"Error: no nodes from server {serverAddress}");
                    }
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"Error connecting to server {serverAddress}: {ex.Message}");
            }
    }

    private async Task RegisterWithServerAsync(string serverAddress)
    {
        try
        {
            var signature = GenerateHMACSignature(NodeAddress, ChainId);
            var response = await SocketManager.GetInstance(serverAddress)
                .SendMessageAsync($"Register:{NodeAddress}:{signature}");
            Logger.LogMessage($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error registering with server {serverAddress}: {ex.Message}");
        }
    }

    private string GenerateHMACSignature(string message, string secret)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
        {
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToBase64String(hash);
        }
    }

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
                Logger.LogMessage($"Error: Timeout or empty response from server {serverAddress}");
                return ret;
            }

            foreach (var nodeAddress in response.Split(','))
                if (!string.IsNullOrEmpty(nodeAddress))
                    ret.Add(nodeAddress);

            Logger.LogMessage($"Active nodes from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error retrieving registered nodes from {serverAddress}: {ex.Message}");
        }

        return ret;
    }

    public async Task SendHeartbeatAsync(string serverAddress)
    {
        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync($"Heartbeat:{NodeAddress}");
            Logger.LogMessage($"Heartbeat sent to {serverAddress}");
            Logger.LogMessage($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error sending heartbeat to {serverAddress}: {ex.Message}");
        }
    }

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
                        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork) // Nur IPv4-Adressen
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

        //Logger.LogMessage("Falling back to static discovery servers.");
        return staticServers;
    }
}