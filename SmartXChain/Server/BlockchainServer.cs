using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
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

    /// <summary>
    ///     Concurrent dictionary storing registered nodes and their last heartbeat timestamp.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _registeredNodes = new();

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

    /// <summary>
    ///     Represents the startup state of the blockchain node.
    /// </summary>
    internal static NodeStartupResult Startup { get; private set; }

    /// <summary>
    ///     Starts the main server and configures routing for API endpoints.
    /// </summary>
    private void StartMainServer()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel()
                    .UseUrls($"http://0.0.0.0:{Config.Default.Port}")
                    .Configure(app =>
                    {
                        // Routing for Endpoints
                        app.UseRouting();
                        app.UseEndpoints(endpoints => { RestEndpoints(endpoints); });
                    });
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                //logging.AddConsole(options =>
                //{
                //    options.LogToStandardErrorThreshold = LogLevel.Warning;
                //});
            })
            .Build();

        host.Run();
    }

    private void RestEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Define REST-Endpoints
        endpoints.MapPost("/api/Register", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            Logger.Log($"Register: {message}");
            var result = HandleRegistration(message);
            await context.Response.WriteAsync(result);
        });

        endpoints.MapPost("/api/GetNodes", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var result = HandleGetNodes(message);
            await context.Response.WriteAsync(result);

            if (Config.Default.Debug && result.Length > 0)
                Logger.Log($"GetNodes: {result}");
        });

        endpoints.MapPost("/api/PushServers", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            Logger.Log($"PushServers: {message}");
            var serverAdded = false;
            foreach (var server in message.Split(','))
                if (server.StartsWith("http://") && !_registeredNodes.ContainsKey(server))
                {
                    _registeredNodes.TryAdd(server, DateTime.UtcNow);

                    if (!Node.CurrentNodeIPs.Contains(server))
                        Node.CurrentNodeIPs.Add(server);

                    serverAdded = true;
                }

            if (serverAdded)
                await context.Response.WriteAsync("ok");
            else
                await context.Response.WriteAsync("");
        });

        endpoints.MapPost("/api/Vote", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            Logger.Log($"Vote: {message}");
            var result = HandleVote(message);
            Logger.Log($"Vote Result: {result}");
            await context.Response.WriteAsync(result);
        });

        endpoints.MapPost("/api/VerifyCode", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            Logger.Log($"VerifyCode: {message}");
            var result = HandleVerifyCode(message);
            Logger.Log($"VerifyCode Result: {result}");
            await context.Response.WriteAsync(result);
        });

        endpoints.MapPost("/api/Heartbeat", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            if (Config.Default.Debug)
                Logger.Log($"Heartbeat: {message}");
            HandleHeartbeat(message);
            await context.Response.WriteAsync("ok");
        });

        endpoints.MapPost("/api/GetBlockCount", async context =>
        {
            if (_blockCount != Startup.Blockchain.Chain.Count)
            {
                _blockCount = Startup.Blockchain.Chain.Count;
                if (Config.Default.Debug)
                    Logger.Log($"GetBlockCount: {Startup.Blockchain.Chain.Count}");
            }

            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            if (message.Contains(':'))
            {
                var sp = message.Split(':');
                if (sp.Length == 5)
                {
                    var remoteBlockCount = Convert.ToInt64(sp.ToArray().Last());
                    var remoteServer = sp[1] + ":" + sp[2] + ":" + sp[3];
                    if (remoteBlockCount < _blockCount)
                        if (NetworkUtils.IsValidServer(remoteServer))
                            lock (Node.DiscoveredServers)
                            {
                                if (!Node.DiscoveredServers.Contains(remoteServer))
                                    Node.DiscoveredServers.Add(remoteServer);
                            }
                }
            }

            await context.Response.WriteAsync(_blockCount.ToString());
        });

        endpoints.MapPost("/api/ValidateChain", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var isvalid = Startup.Blockchain.IsValid();
            if (Config.Default.Debug)
                Logger.Log($"ValidateChain: {isvalid}");
            if (isvalid)
                await context.Response.WriteAsync("ok");
            else
                await context.Response.WriteAsync("");
        });

        endpoints.MapPost("/api/GetChain", async context =>
        {
            var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
            if (Config.Default.Debug)
                Logger.Log($"GetChain: {message}");
            await context.Response.WriteAsync(Startup.Blockchain.ToBase64());
        });

        endpoints.MapPost("/api/GetBlock/{block:int}", async context =>
        {
            var blockIndexStr = (string)context.Request.RouteValues["block"];
            if (!int.TryParse(blockIndexStr, out var blockIndex))
            {
                context.Response.StatusCode = 400; // Bad Request
                await context.Response.WriteAsync("ERROR: Invalid block index.");
                return;
            }

            if (blockIndex < 0 || blockIndex >= Startup.Blockchain.Chain.Count)
            {
                context.Response.StatusCode = 404; // Not Found
                await context.Response.WriteAsync($"ERROR: Block {blockIndex} not found.");
                return;
            }

            if (Config.Default.Debug)
                Logger.Log(
                    $"Sent block {blockIndex} {Startup.Blockchain.Chain[blockIndex].Hash} parent:{Startup.Blockchain.Chain[blockIndex].PreviousHash}");
            var block = Startup.Blockchain.Chain[blockIndex].ToBase64();

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(block);
        });

        endpoints.MapPost("/api/PushChain", async context =>
        {
            try
            {
                var serializedChain = await new StreamReader(context.Request.Body).ReadToEndAsync();
                Logger.Log($"PushChain: {serializedChain}");
                var incomingChain = Blockchain.FromBase64(serializedChain);
                if (Startup.Blockchain != null && Startup.Blockchain.SmartContracts.Count == 0)
                {
                    lock (Startup.Blockchain.Chain)
                    {
                        if (incomingChain != null &&
                            incomingChain.Chain.Count > Startup.Blockchain.Chain.Count &&
                            incomingChain.IsValid())
                        {
                            Startup.Blockchain = incomingChain;
                            Node.SaveBlockChain(incomingChain, Startup.Node);
                        }
                    }

                    await context.Response.WriteAsync("ok");
                    return;
                }
            }
            catch (Exception e)
            {
                Logger.Log($"ERROR: {e.Message}\n{e.StackTrace}");
            }

            await context.Response.WriteAsync("");
        });

        endpoints.MapPost("/api/NewBlock", async context =>
        {
            var serializedBlock = await new StreamReader(context.Request.Body).ReadToEndAsync();
            if (Config.Default.Debug)
                Logger.Log($"NewBlock: {serializedBlock}");
            Block newBlock = null;
            try
            {
                newBlock = Block.FromBase64(serializedBlock);
            }
            catch (Exception e)
            {
                Logger.Log($"ERROR: {e.Message}\n{e.StackTrace}");
            }

            if (newBlock != null && Startup.Blockchain != null && Startup.Blockchain.AddBlock(newBlock, true, false))
                await context.Response.WriteAsync("ok");
            else
                await context.Response.WriteAsync("");
        });
    }

    /// <summary>
    ///     Starts the server asynchronously.
    /// </summary>
    public static async Task<(BlockchainServer?, NodeStartupResult?)> StartServerAsync(bool loadExisting = true)
    {
        NodeStartupResult? result = null;

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
        _registeredNodes[nodeAddress] = DateTime.UtcNow;
        Logger.Log($"Node registered: {nodeAddress}");

        return "ok";
    }

    /// <summary>
    ///     Retrieves a list of active nodes, removing any that are inactive based on heartbeat timestamps.
    /// </summary>
    /// <param name="message">A dummy message for compatibility (not used).</param>
    /// <returns>A comma-separated list of active node addresses.</returns>
    private string HandleGetNodes(string message)
    {
        RemoveInactiveNodes();
        if (_registeredNodes.Keys.Count > 0)
        {
            var nodes = string.Join(",", _registeredNodes.Keys.Where(node => !string.IsNullOrWhiteSpace(node)));
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
    private string HandleVerifyCode(string message)
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

        var now = DateTime.UtcNow;
        _registeredNodes[nodeAddress] = now;
        if (!Node.CurrentNodeIPs.Contains(nodeAddress))
            lock (Node.CurrentNodeIPs)
            {
                Node.CurrentNodeIPs.Add(nodeAddress);
            }

        if (Config.Default.Debug)
            Logger.Log($"Heartbeat {nodeAddress} - {now} (HandleHeartbeat)");

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
        var inactiveNodes = _registeredNodes
            .Where(kvp => (now - kvp.Value).TotalSeconds > HeartbeatTimeoutSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        // Remove inactive nodes from the registry
        foreach (var node in inactiveNodes)
        {
            _registeredNodes.TryRemove(node, out _);

            // Remove from CurrentNodeIPs
            var updatedNodeIPs = new ConcurrentBag<string>(Node.CurrentNodeIPs.Where(ip => ip != node));
            Node.CurrentNodeIPs = updatedNodeIPs;

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
                    var response = await httpClient.PostAsync("/api/GetNodes", content);

                    // If the response is successful, update the list of registered nodes
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();

                        foreach (var node in responseBody.Split(','))
                            if (node.Contains("http"))
                                _registeredNodes.TryAdd(node, DateTime.UtcNow);
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