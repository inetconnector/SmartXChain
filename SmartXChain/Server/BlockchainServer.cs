using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmbedIO;
using EmbedIO.WebApi;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using Swan.Logging;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.Server;

/// <summary>
///     The BlockchainServer class handles blockchain operations such as node registration, synchronization,
///     and serving API endpoints for blockchain interaction.
/// </summary>
public partial class BlockchainServer
{
    private const int HeartbeatTimeoutSeconds = 30; // Maximum time before a node is considered inactive
    private readonly List<string> _peerServers = new(); // Addresses of other peer registration servers

    private readonly string _serverAddressExtern; // External address of this server
    private readonly string _serverAddressIntern; // Internal address of this server
    private int _blockCount;

    /// <summary>
    ///     Initializes a new instance of the BlockchainServer class with specified external and internal IP addresses.
    /// </summary>
    public BlockchainServer(string externIP, string internIP)
    {
        _serverAddressExtern = $"http://{externIP}:{Config.Default.Port}";
        _serverAddressIntern = $"http://{internIP}:{Config.Default.Port}";

        Logger.Log($"Starting server at {_serverAddressIntern}/{_serverAddressExtern}...");
    }
    private WebServer _server;

    public void StartMainServer()
    {
        // Initialisiere den Webserver mit EmbedIO 
        _server = new WebServer(o => o
                .WithUrlPrefix($"http://*:{Config.Default.Port}/") 
                .WithMode(HttpListenerMode.EmbedIO))
                .WithLocalSessionManager()
                .WithWebApi("/api", m => m.WithController<ApiController>());

        // Starte den Server asynchron
        _server.RunAsync();
        Console.WriteLine($"Server started at {NetworkUtils.IP}");
        _server.StateChanged += (s, e) => $"WebServer New State - {e.NewState}".Info();
    }

    public void StopServer()
    {
        _server?.Dispose();
        Console.WriteLine("Server stopped.");
    }

    /// <summary>
    ///     Represents the startup state of the blockchain node.
    /// </summary>
    internal static NodeStartupResult Startup { get; private set; }

    /// <summary>
    ///     Starts the server asynchronously.
    /// </summary>
    public static async Task<(BlockchainServer?, BlockchainServer.NodeStartupResult?)> StartServerAsync(bool loadExisting = true)
    {
        BlockchainServer.NodeStartupResult? result = null;

        // Initialize and start the node
        await Task.Run(async () => { result = await StartNode(Config.Default.MinerAddress); });

        if (result is null or { Blockchain: null })
            throw new InvalidOperationException("Failed to initialize the blockchain node.");

        BlockchainServer? server = null;

        // Initialize and start the server
        await Task.Run(() =>
        {
            try
            {
                server = new BlockchainServer(NetworkUtils.IP, NetworkUtils.GetLocalIP());
                server.Start();

                Logger.Log(
                    $"Server node for blockchain '{Config.Default.ChainId}' started at {NetworkUtils.IP}");
            }
            catch (Exception ex)
            {
                Logger.Log($"Error starting server: {ex.Message}");
            }
        });
        Startup = result;


        if (loadExisting)
        {
            var chainPath = "";
            try
            {
                chainPath = Path.Combine(Config.Default.BlockchainPath, "chain-" + result!.Node.ChainId);
                if (File.Exists(chainPath))
                {
                    result!.Blockchain = Blockchain.Load(chainPath);
                }
                else
                {
                    Logger.Log($"No existing chain found in {chainPath}");
                    Logger.Log("Waiting for synchronization...");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error loading existing chain from {chainPath}");
                Logger.Log($"ERROR: {ex.Message}");
            }
        }

        return (server, result);
    }

    /// <summary>
    ///     Discovers peers from the configuration and registers them in the peer server list,
    ///     excluding the current server addresses.
    /// </summary>
    private void DiscoverAndRegisterWithPeers()
    {
        var validPeers = new List<string>();

        try
        {
            foreach (var peer in Config.Default.Peers)
                if (!string.IsNullOrEmpty(peer) && peer.StartsWith("http://"))
                {
                    if (peer == _serverAddressExtern) continue;
                    if (peer == _serverAddressIntern) continue;
                    if (!_peerServers.Contains(peer))
                    {
                        _peerServers.Add(peer);
                        validPeers.Add(peer);
                    }
                }

            Logger.Log($"Static peers discovered: {string.Join(", ", validPeers)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error processing static peers: {ex.Message}");
        }
    }

    /// <summary>
    ///     Removes the fingerprint identifier from a message string.
    /// </summary>
    /// <param name="message">The input message containing a fingerprint.</param>
    /// <returns>The message without the fingerprint.</returns>
    private string RemoveFingerprint(string message)
    {
        return message.Substring(Crypt.AssemblyFingerprint.Length + 1);
    }

    /// <summary>
    ///     Handles the registration of a node by validating its address and signature, and adds it to the registered nodes.
    /// </summary>
    /// <param name="message">The registration message containing node address and signature.</param>
    /// <returns>"ok" if successful, or an error message if registration fails.</returns>
    private string HandleRegistration(string message)
    {
        var parts = message.Split(new[] { ':' }, 2);
        if (parts.Length != 2 || parts[0] != "Register") return "Invalid registration format";

        var remainingParts = parts[1];

        // Split the remaining data into node address and signature
        var addressSignatureParts = remainingParts.Split('|');
        if (addressSignatureParts.Length != 2) return "Invalid registration format";

        var nodeAddress = addressSignatureParts[0]; // Node address (e.g., "http://127.0.0.1")
        var signature = addressSignatureParts[1]; // Signature for validation

        // Security check using signature validation
        if (!ValidateSignature(nodeAddress, signature))
        {
            Logger.Log($"ValidateSignature failed. Node not registered: {nodeAddress} Signature: {signature}");
            return "";
        }
         
        // Register the node
        Node.AddNodeIP(nodeAddress); 
        Logger.Log($"Node registered: {nodeAddress}");

        return "ok";
    }

    /// <summary>
    ///     Retrieves a list of active nodes, removing any that are inactive based on heartbeat timestamps.
    /// </summary>
    /// <param name="message">A dummy message for compatibility (not used).</param>
    /// <returns>A comma-separated list of active node addresses.</returns>
    private string HandleNodes(string message)
    {
        RemoveInactiveNodes();
        
        if (Node.CurrentNodeIPs.Count > 0)
        {
            var nodes = string.Join(",", Node.CurrentNodeIPs.Where(node => !string.IsNullOrWhiteSpace(node)));
            return nodes.TrimEnd(',');
        }

        return "";
    }

    /// <summary>
    ///     Processes a vote message, validating its block and returning a response if successful.
    /// </summary>
    /// <param name="message">The vote message containing block data in Base64 format.</param>
    /// <returns>"ok" with miner address if the vote is valid, or an error message otherwise.</returns>
    private string HandleVote(string message)
    {
        const string prefix = "Vote:";
        if (!message.StartsWith(prefix))
        {
            Logger.Log("Invalid Vote message received.");
            return "";
        }

        try
        {
            var base64 = message.Substring(prefix.Length);
            var block = Block.FromBase64(base64);
            if (block != null)
            {
                var hash = block.Hash;
                var calculatedHash = block.CalculateHash();
                if (calculatedHash == hash) return "ok#" + Config.Default.MinerAddress;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"Invalid Vote message received. {e.Message}");
        }

        return "";
    }

    /// <summary>
    ///     Verifies code by decompressing and validating it against security rules.
    /// </summary>
    /// <param name="message">The verification message containing compressed Base64 code.</param>
    /// <returns>"ok" if the code is safe, or an error message if validation fails.</returns>
    private static string HandleVerifyCode(string message)
    {
        const string prefix = "VerifyCode:";
        if (!message.StartsWith(prefix))
        {
            Logger.Log("Invalid verification request received.");
            return "";
        }

        var compressedBase64Data = message.Substring(prefix.Length);
        var code = Compress.DecompressString(Convert.FromBase64String(compressedBase64Data));

        var codecheck = "";
        var isCodeSafe = CodeSecurityAnalyzer.IsCodeSafe(code, ref codecheck);

        return isCodeSafe ? "ok" : $"failed: {codecheck}";
    }

    /// <summary>
    ///     Handles heartbeat messages from nodes to update their last active timestamp.
    /// </summary>
    /// <param name="message">The heartbeat message containing the node address.</param>
    private void HandleHeartbeat(string message)
    {
        const string prefix = "Heartbeat:";
        if (!message.StartsWith(prefix))
        {
            Logger.Log("Invalid Heartbeat message received.");
            return;
        }

        var nodeAddress = message.Substring(prefix.Length);

        if (!Uri.IsWellFormedUriString(nodeAddress, UriKind.Absolute))
        {
            Logger.Log("Invalid node address in heartbeat received.");
            return;
        }
         
        Node.AddNodeIP(nodeAddress);  

        if (Config.Default.Debug)
            Logger.Log($"Heartbeat {nodeAddress} - {DateTime.Now} (HandleHeartbeat)");

        GC.Collect();
        GC.WaitForPendingFinalizers();
    }


    /// <summary>
    ///     Removes inactive nodes that have exceeded the heartbeat timeout from the registry.
    /// </summary>
    private void RemoveInactiveNodes()
    {
        var now = DateTime.UtcNow;

        // Identify nodes that have exceeded the heartbeat timeout
        var inactiveNodes = Node.CurrentNodeIP_LastActive
            .Where(kvp => (now - kvp.Value).TotalSeconds > HeartbeatTimeoutSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        // Remove inactive nodes from the registry
        foreach (var node in inactiveNodes)
        {
            Node.RemoveNodeIP(node);  
            Logger.Log($"Node removed: {node} (Inactive)");
        }
    }

    /// <summary>
    ///     Validates a node's signature using HMACSHA256 with the server's secret key.
    /// </summary>
    /// <param name="nodeAddress">The node address being validated.</param>
    /// <param name="signature">The provided signature to validate.</param>
    /// <returns>True if the signature is valid; otherwise, false.</returns>
    private bool ValidateSignature(string nodeAddress, string signature)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.Default.ChainId)))
        {
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(nodeAddress));
            var computedSignature = Convert.ToBase64String(computedHash);

            return computedSignature == signature;
        }
    }

    /// <summary>
    ///     Continuously synchronizes with peer servers to update the list of active nodes.
    /// </summary>
    private async void SynchronizeWithPeers()
    {
        while (true)
        {
            foreach (var peer in _peerServers)
                try
                {
                    // Initialize HTTP client for communication with the peer
                    using var httpClient = new HttpClient { BaseAddress = new Uri(peer) };

                    // Send an empty request to the /api/GetNodes endpoint
                    var content = new StringContent(JsonSerializer.Serialize(""), Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync("/api/Nodes", content);

                    // If the response is successful, update the list of registered nodes
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();

                        foreach (var node in responseBody.Split(','))
                            if (node.Contains("http"))
                            {
                                Node.AddNodeIP(node); 
                            }
                    }
                    else
                    {
                        // Log any error with the response from the peer
                        Logger.Log($"Error synchronizing with peer {peer}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    // Log exceptions that occur during the synchronization process
                    Logger.Log($"Error synchronizing with peer {peer}: {ex.Message}");
                }

            // Wait for 20 seconds before the next synchronization cycle
            await Task.Delay(20000);
        }
    }

    /// <summary>
    ///     Broadcasts a message to a list of peer servers, targeting a specific API endpoint command.
    /// </summary>
    /// <param name="serversList">List of peer server URLs to send the message to.</param>
    /// <param name="command">The API command to invoke on each peer server.</param>
    /// <param name="message">The message content to be sent to the peers.</param>
    internal static async void BroadcastToPeers(ConcurrentBag<string> serversList, string command, string message)
    {
        foreach (var peer in serversList)
        {
            if (peer.Contains(NetworkUtils.IP))
                continue;

            await Task.Run(async () =>
            {
                try
                {
                    // Initialize HTTP client for communication with the peer
                    using var httpClient = new HttpClient { BaseAddress = new Uri(peer) };

                    // Prepare the message content to send to the specified API endpoint
                    var content = new StringContent(message);
                    var url = $"/api/{command}";

                    // Log the broadcast request details
                    if (Config.Default.Debug)
                        Logger.Log($"BroadcastToPeers async: {url}\n{content}");
                    var response = await httpClient.PostAsync(url, content);

                    // If the response is successful, log the response content
                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = response.Content.ReadAsStringAsync().Result;
                        if (Config.Default.Debug)
                            Logger.Log($"BroadcastToPeers response: {responseString}");
                    }
                    else
                    {
                        // Log an error message if the response status indicates failure
                        var error = $"ERROR: BroadcastToPeers {response.StatusCode} - {response.ReasonPhrase}";
                        Logger.Log(error);
                    }
                }
                catch (Exception ex)
                {
                    // Log exceptions that occur during the broadcasting process
                    Logger.Log($"Error sending to peer {peer}: {ex.Message}");
                }
            });
        }
    }
}