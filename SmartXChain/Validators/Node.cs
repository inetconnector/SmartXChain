using System.Net;
using System.Security.Cryptography;
using System.Text;
using BlockchainProject.Utils;

namespace BlockchainProject.Validators;

public class Node
{
    public static List<string> CurrentNodeIPs = new();
    private readonly string _nodeAddress;
    private readonly string _sharedSecret;

    public Node(string nodeAddress, string sharedSecret)
    {
        _nodeAddress = nodeAddress;
        _sharedSecret = sharedSecret;
    }

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
        var discoveredServers = node.DiscoverServers(peers.ToArray());

        // Step 2: If no servers were found, start a loop to wait for servers
        if (discoveredServers.Length == 0)
        {
            Console.WriteLine("No active servers found. Waiting for a server...");

            while (discoveredServers.Length == 0)
                try
                {
                    await Task.Delay(5000); // Check every 5 seconds
                    discoveredServers = node.DiscoverServers(peers.ToArray());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during server discovery: {ex.Message}");
                }
        }

        // Step 4: Register with an active server
        Console.WriteLine("Registering with a discovery server...");
        await node.RegisterWithDiscoveryAsync(discoveredServers);

        Console.WriteLine("Node successfully registered.");

        // Step 5: Send heartbeat (every 10 seconds)
        Task.Run(async () =>
        {
            while (true)
                try
                {
                    foreach (var server in discoveredServers) await node.SendHeartbeatAsync(server);
                    Thread.Sleep(10000); // Heartbeat interval
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

    public async Task RegisterWithDiscoveryAsync(string[] discoveryServers)
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
                        await RegisterWithServerAsync(activeServer);
                    else
                        Console.WriteLine($"Error: no nodes from server {serverAddress}");
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
            var signature = GenerateHMACSignature(_nodeAddress, _sharedSecret);
            var response = await SocketManager.GetInstance(serverAddress)
                .SendMessageAsync($"Register:{_nodeAddress}:{signature}");
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
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync($"Heartbeat:{_nodeAddress}");
            Console.WriteLine($"Heartbeat sent to {serverAddress}");
            Console.WriteLine($"Response from server {serverAddress}: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending heartbeat to {serverAddress}: {ex.Message}");
        }
    }


    public string[] DiscoverServers(string[] staticServers)
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
                        .ToArray();

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

        //Console.WriteLine("Falling back to static discovery servers.");
        return staticServers;
    }
}