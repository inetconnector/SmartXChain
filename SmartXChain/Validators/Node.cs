using System.Net;
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
    public  BlockchainServer.NodeStartupResult StartupResult { get; set; }

    public static async Task<Node> Start(bool localRegistrationServer = false)
    {
        // Node Configuration 
        var ip = NetworkUtils.IP;
        if (ip == "") ip = NetworkUtils.GetLocalIP();
        if (localRegistrationServer)
            ip = "127.0.0.1";

        var nodeAddress = $"tcp://{ip}:{Config.Default.Port}"; // Own node address
        var sharedSecret = Config.Default.SmartXchain; // Shared secret with the servers

        // Create a node instance
        var node = new Node(nodeAddress, sharedSecret);

        Console.WriteLine("Starting automatic server discovery...");

        // Known discovery servers (starting points for the search) 
        var peers = Config.Default.Peers;

        // Step 1: Query primary discovery servers
        var discoveredServers = node.DiscoverServers(peers);


        // Step 2: If no servers were found, start a loop to wait for servers
        if (discoveredServers.Count == 0)
        {
            Console.WriteLine("No active servers found. Waiting for a server...");

            while (discoveredServers.Count == 0)
                try
                {
                    await Task.Delay(5000); // Check every 5 seconds
                    discoveredServers = node.DiscoverServers(peers);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during server discovery: {ex.Message}");
                }
        }

        discoveredServers = discoveredServers.Where(ip =>
            !ip.Contains(NetworkUtils.IP) &&
            !ip.Contains(NetworkUtils.GetLocalIP())).ToList();

        // Step 4: Register with an active server
        Console.WriteLine("Registering with a discovery server...");
        await node.RegisterWithDiscoveryAsync(discoveredServers);

        Console.WriteLine("Node successfully registered.");

        // Step 5: Send heartbeat (every 20 seconds)
        Task.Run(async () =>
        {
            while (true)
                try
                {
                    foreach (var server in discoveredServers)
                    {
                        await node.SendHeartbeatAsync(server);
                        await node.CheckBlockchainSize(server);
                    }

                    Thread.Sleep(20000); // Heartbeat interval
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending heartbeat: {ex.Message}");
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
                    Console.WriteLine($"Error retrieving nodes: {ex.Message}");
                }
        });

        return node;
    }

    private async Task CheckBlockchainSize(string server)
    {
        if (StartupResult != null && StartupResult.Blockchain != null)
        {
            Console.WriteLine("CheckBlockchainSize...");
            var newchain =
                await UpdateBlockchain(StartupResult.Blockchain, StartupResult.Node, StartupResult.Consensus);
            if (newchain != null) StartupResult.Blockchain = newchain;
        }
    }

    private static async Task<Blockchain?> UpdateBlockchain(Blockchain blockchain, Node node, SnowmanConsensus consensus)
    {
        Blockchain newChain = null;
        var currentBlockCount = blockchain.Chain.Count;

        try
        {
            foreach (var remoteNode in Node.CurrentNodeIPs)
            {
                // 1. Anfrage: Größe der entfernten Blockchain abrufen
                var blockchainSizeResponse = await SocketManager.GetInstance(remoteNode)
                    .SendMessageAsync("GetBlockCount:" + currentBlockCount);

                if (!int.TryParse(blockchainSizeResponse, out var remoteBlockCount))
                {
                    Console.WriteLine($"Invalid blockchain size response from node {remoteNode}.");
                    continue;
                }

                Console.WriteLine($"Node {remoteNode} blockchain: {remoteBlockCount}, Local blockchain: {currentBlockCount}.");

                if (remoteBlockCount <= currentBlockCount)
                {
                    Console.WriteLine($"Local blockchain is already up to date with node {remoteNode}.");
                    continue;
                }

                Console.WriteLine($"Node {remoteNode} blockchain is larger. Starting synchronization...");

                // 2. Temporäre Datei erstellen
                var tmpFile = Path.GetTempFileName();

                try
                {
                    using (var fileStream = new FileStream(tmpFile, FileMode.Create, FileAccess.Write))
                    {
                        // 3. Blockchain in Chunks abrufen
                        while (true)
                        {
                            var chunkResponse = await SocketManager.GetInstance(remoteNode)
                                .SendMessageAsync("GetChain");

                            if (chunkResponse == "END")
                            {
                                Console.WriteLine($"Received all chunks from node {remoteNode}.");
                                break;
                            }

                            // Chunk-Daten verarbeiten
                            var chunkData = chunkResponse.Split('#', 2);
                            if (chunkData.Length != 2)
                            {
                                Console.WriteLine($"Invalid chunk format received from node {remoteNode}.");
                                continue;
                            }

                            try
                            {
                                var base64Data = chunkData[1];
                                var chunkBytes = Convert.FromBase64String(base64Data);
                                await fileStream.WriteAsync(chunkBytes, 0, chunkBytes.Length);
                            }
                            catch (FormatException)
                            {
                                Console.WriteLine($"Failed to decode chunk data from node {remoteNode}. Skipping...");
                            }
                        }
                    }

                    // 4. Blockchain laden und validieren
                    var updatedBlockchain = Blockchain.Load(tmpFile, consensus);
                    if (updatedBlockchain == null || !updatedBlockchain.IsValid())
                    {
                        Console.WriteLine($"Failed to load or validate the updated blockchain from node {remoteNode}.");
                        continue;
                    }

                    // 5. Konfliktlösung (z. B. gleiche Länge, aber andere Chains)
                    if (updatedBlockchain.Chain.Count == blockchain.Chain.Count &&
                        !updatedBlockchain.Chain.SequenceEqual(blockchain.Chain))
                    {
                        Console.WriteLine($"Conflict detected: Chains of equal length but different content from node {remoteNode}.");
                        continue;
                    }

                    Console.WriteLine($"Blockchain successfully updated from node {remoteNode}.");
                    newChain = updatedBlockchain;
                    break;
                }
                finally
                {
                    // Temporäre Datei löschen
                    if (File.Exists(tmpFile))
                    {
                        File.Delete(tmpFile);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during blockchain synchronization: {ex.Message}");
        }

        return newChain;
    }


    public async Task<(bool, string)> SendVoteRequestAsync(string targetValidator, Block block)
    {
        try
        {
            var message = $"Vote:{block.Base64Encoded}";
            if (block.Verify(message))
            {
                Console.WriteLine($"Sending vote request to: {targetValidator}");
                var response = await SocketManager.GetInstance(targetValidator).SendMessageAsync(message);
                Console.WriteLine($"Response from {targetValidator}: {response}");
                return (true, response);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending vote request: {ex.Message}");
        }

        return (false, "");
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
                        Console.WriteLine($"Error: no nodes from server {serverAddress}");
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to server {serverAddress}: {ex.Message}");
            }
    }

    private async Task RegisterWithServerAsync(string serverAddress)
    {
        try
        {
            var signature = GenerateHMACSignature(NodeAddress, ChainId);
            var response = await SocketManager.GetInstance(serverAddress)
                .SendMessageAsync($"Register:{NodeAddress}:{signature}");
            Console.WriteLine($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error registering with server {serverAddress}: {ex.Message}");
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

        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync("GetNodes");

            if (response == "ERROR: Timeout" || string.IsNullOrEmpty(response))
            {
                Console.WriteLine($"Error: Timeout or empty response from server {serverAddress}");
                return ret;
            }

            foreach (var nodeAddress in response.Split(','))
                if (!string.IsNullOrEmpty(nodeAddress))
                    ret.Add(nodeAddress);

            Console.WriteLine($"Active nodes from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving registered nodes from {serverAddress}: {ex.Message}");
        }

        return ret;
    }

    public async Task SendHeartbeatAsync(string serverAddress)
    {
        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync($"Heartbeat:{NodeAddress}");
            Console.WriteLine($"Heartbeat sent to {serverAddress}");
            Console.WriteLine($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending heartbeat to {serverAddress}: {ex.Message}");
        }
    }

    public List<string> DiscoverServers(List<string> staticServers)
    {
        try
        {
            foreach (var server in staticServers)
            {
                if (!server.StartsWith("tcp://"))
                    continue;

                var serverAddress = server.Replace("tcp://", "").Trim();
                var parts = serverAddress.Split(':');

                if (parts.Length != 2)
                {
                    Console.WriteLine("Invalid server address format: " + server);
                    continue;
                }

                var host = parts[0];
                var port = parts[1];

                try
                {
                    var ipAddresses = Dns.GetHostAddresses(host)
                        .Select(ip => $"tcp://{ip}:{port}")
                        .ToList();

                    if (ipAddresses.Any())
                    {
                        Console.WriteLine("DNS discovery successful: " + string.Join(", ", ipAddresses));
                        return ipAddresses;
                    }
                }
                catch (Exception dnsEx)
                {
                    Console.WriteLine($"DNS resolution failed for {host}: {dnsEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error during server discovery: " + ex.Message);
        }

        // Console.WriteLine("Falling back to static discovery servers.");
        return staticServers;
    }
}