using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.Server;

public class BlockchainServer
{
    private const int HeartbeatTimeoutSeconds = 30; // Maximum time before a node is considered inactive 
    private readonly List<string> _peerServers = new(); // Addresses of other peer registration servers

    private readonly ConcurrentDictionary<string, DateTime>
        _registeredNodes = new(); // Registered node addresses and their last heartbeat timestamp

    private readonly string _serverAddressExtern; // Extern Address of this server 
    private readonly string _serverAddressIntern; // Intern address of this server  

    public BlockchainServer(string externIP, string internIP)
    {
        _serverAddressExtern = $"http://{externIP}:{Config.Default.Port}";
        _serverAddressIntern = $"http://{internIP}:{Config.Default.Port}";

        Logger.LogMessage($"Starting server at {_serverAddressIntern}/{_serverAddressExtern}...");
    }

    public static NodeStartupResult Startup { get; private set; }

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
                        app.UseEndpoints(endpoints =>
                        {
                            // Define REST-Endpoints
                            endpoints.MapPost("/api/Register", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"Register: {message}");
                                var result = HandleRegistration(message);
                                await context.Response.WriteAsync(result);
                            });

                            endpoints.MapPost("/api/GetNodes", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                var result = HandleGetNodes(message);
                                await context.Response.WriteAsync(result);
                                if (result.Length > 0)
                                    Logger.LogMessage($"GetNodes: {result}");
                            });

                            endpoints.MapPost("/api/PushServers", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"PushServers: {message}");
                                var serverAdded = false;
                                foreach (var server in message.Split(','))
                                    if (server.StartsWith("http://") && !_registeredNodes.ContainsKey(server))
                                    {
                                        _registeredNodes.TryAdd(message, DateTime.UtcNow);
                                        if (!Node.CurrentNodeIPs.Contains(server)) Node.CurrentNodeIPs.Add(server);
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
                                Logger.LogMessage($"Vote: {message}");
                                var result = HandleVote(message);
                                await context.Response.WriteAsync(result);
                            });

                            endpoints.MapPost("/api/VerifyCode", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"VerifyCode: {message}");
                                var result = HandleVerifyCode(message);
                                await context.Response.WriteAsync(result);
                            });

                            endpoints.MapPost("/api/Heartbeat", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"Heartbeat: {message}");
                                HandleHeartbeat(message);
                                await context.Response.WriteAsync("ok");
                            });

                            endpoints.MapPost("/api/GetBlockCount", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"GetBlockCount: {Startup.Blockchain.Chain.Count}");

                                await context.Response.WriteAsync(Startup.Blockchain.Chain.Count.ToString());
                            });

                            endpoints.MapPost("/api/ValidateChain", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                var isvalid = Startup.Blockchain.IsValid();
                                Logger.LogMessage($"ValidateChain: {isvalid}");
                                if (isvalid)
                                    await context.Response.WriteAsync("ok");
                                else
                                    await context.Response.WriteAsync("");
                            });

                            endpoints.MapPost("/api/GetChain", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"GetChain: {message}");
                                await context.Response.WriteAsync(Startup.Blockchain.ToBase64());
                            });

                            endpoints.MapPost("/api/GetBlock/{block:int}", async context =>
                            {
                                var blockIndexStr = (string)context.Request.RouteValues["block"];
                                if (!int.TryParse(blockIndexStr, out var blockIndex))
                                {
                                    context.Response.StatusCode = 400; // Bad Request
                                    await context.Response.WriteAsync("Error: Invalid block index.");
                                    return;
                                }

                                if (blockIndex < 0 || blockIndex >= Startup.Blockchain.Chain.Count)
                                {
                                    context.Response.StatusCode = 404; // Not Found
                                    await context.Response.WriteAsync($"Error: Block {blockIndex} not found.");
                                    return;
                                }

                                Logger.LogMessage(
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
                                    Logger.LogMessage($"PushChain: {serializedChain}");
                                    var incomingChain = Blockchain.FromBase64(serializedChain);
                                    if (Startup.Blockchain.SmartContracts.Count == 0)
                                    {
                                        lock (Startup.Blockchain.Chain)
                                        {
                                            if (incomingChain != null &&
                                                incomingChain.Chain.Count > Startup.Blockchain.Chain.Count &&
                                                incomingChain.IsValid())
                                                Startup.Blockchain = incomingChain;
                                        }

                                        await context.Response.WriteAsync("ok");
                                        return;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Logger.LogMessage($"Error: {e.Message}\n{e.StackTrace}");
                                }

                                await context.Response.WriteAsync("");
                            });

                            endpoints.MapPost("/api/NewBlock", async context =>
                            {
                                var serializedBlock = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"NewBlock: {serializedBlock}");
                                Block newBlock = null;
                                try
                                {
                                    newBlock = Block.FromBase64(serializedBlock);
                                }
                                catch (Exception e)
                                {
                                    Logger.LogMessage($"Error: {e.Message}\n{e.StackTrace}");
                                }

                                if (newBlock != null && Startup.Blockchain.AddBlock(newBlock, true, false))
                                    await context.Response.WriteAsync("ok");
                                else
                                    await context.Response.WriteAsync("");
                            });
                        });
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

    public static async Task<(BlockchainServer?, NodeStartupResult?)> StartServerAsync()
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

                Logger.LogMessage(
                    $"Server node for blockchain '{Config.Default.SmartXchain}' started at {NetworkUtils.IP}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error starting server: {ex.Message}");
            }
        });
        Startup = result;
        return (server, result);
    }

    public static async Task<NodeStartupResult> StartNode(string walletAddress)
    {
        var node = await Node.Start();

        // Create blockchain
        var blockchain = new Blockchain(2, walletAddress);

        // Publish server IP
        var nodeTransaction = new Transaction
        {
            Sender = Blockchain.SystemAddress,
            Recipient = Blockchain.SystemAddress,
            Data = Convert.ToBase64String(Encoding.ASCII.GetBytes(NetworkUtils.IP)), // Store data as Base64 string
            Timestamp = DateTime.UtcNow
        };

        blockchain.AddTransaction(nodeTransaction);

        Startup = new NodeStartupResult(blockchain, node);
        return Startup;
    }


    public void Start()
    {
        // 1. Discover and register with peer servers
        Task.Run(() => DiscoverAndRegisterWithPeers());

        // 2. Start the main server to listen for incoming messages
        Task.Run(() => StartMainServer());

        // 3. Background task to synchronize with peer servers
        Task.Run(() => SynchronizeWithPeers());
    }

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

                    _peerServers.Add(peer);
                    validPeers.Add(peer);
                }

            Logger.LogMessage($"Static peers discovered: {string.Join(", ", validPeers)}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error processing static peers: {ex.Message}");
        }
    }

    private string RemoveFingerprint(string message)
    {
        return message.Substring(Crypt.AssemblyFingerprint.Length + 1);
    }

    private string HandleRegistration(string message)
    {
        var parts = message.Split(new[] { ':' }, 2);
        if (parts.Length != 2 || parts[0] != "Register") return "Invalid registration format";

        var remainingParts = parts[1];

        // Split the remaining data into node address and signature
        var addressSignatureParts = remainingParts.Split(new[] { ':' }, 2);
        if (addressSignatureParts.Length != 2) return "Invalid registration format";

        var nodeAddress = addressSignatureParts[0]; // Node address (e.g., "http://127.0.0.1")
        var signature = addressSignatureParts[1]; // Signature for validation

        // Security check using signature validation
        if (!ValidateSignature(nodeAddress, signature)) return "";

        // Register the node
        _registeredNodes[nodeAddress] = DateTime.UtcNow;
        Logger.LogMessage($"Node registered: {nodeAddress}");

        return "ok";
    }

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

    private string HandleVote(string message)
    {
        const string prefix = "Vote:";
        if (!message.StartsWith(prefix))
        {
            Logger.LogMessage("Invalid Vote message received.");
            return "";
        }

        try
        {
            var base64 = message.Substring(prefix.Length);
            var block = Block.FromBase64(base64);
            if (block != null)
            {
                var hash = block.Hash;
                if (block.CalculateHash() == hash) return "ok#" + Config.Default.MinerAddress;
            }
        }
        catch (Exception e)
        {
            Logger.LogMessage($"Invalid Vote message received. {e.Message}");
        }

        return "";
    }

    private string HandleVerifyCode(string message)
    {
        const string prefix = "VerifyCode:";
        if (!message.StartsWith(prefix))
        {
            Logger.LogMessage("Invalid verification request received.");
            return "";
        }

        var compressedBase64Data = message.Substring(prefix.Length);
        var code = Compress.DecompressString(Convert.FromBase64String(compressedBase64Data));

        var codecheck = "";
        var isCodeSafe = CodeSecurityAnalyzer.IsCodeSafe(code, ref codecheck);

        return isCodeSafe ? "ok" : $"failed: {codecheck}";
    }

    private void HandleHeartbeat(string message)
    {
        const string prefix = "Heartbeat:";
        if (!message.StartsWith(prefix))
        {
            Logger.LogMessage("Invalid Heartbeat message received.");
            return;
        }

        var nodeAddress = message.Substring(prefix.Length);

        if (!Uri.IsWellFormedUriString(nodeAddress, UriKind.Absolute))
        {
            Logger.LogMessage("Invalid node address in heartbeat received.");
            return;
        }

        var now = DateTime.UtcNow;
        _registeredNodes[nodeAddress] = now;
        if (!Node.CurrentNodeIPs.Contains(nodeAddress))
            lock (Node.CurrentNodeIPs)
            {
                Node.CurrentNodeIPs.Add(nodeAddress);
            }

        Logger.LogMessage($"Heartbeat {nodeAddress} - {now} (HandleHeartbeat)");

        CleanupResources();
    }

    private void CleanupResources()
    {
        // Force garbage collection to free up unused resources
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

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
            Logger.LogMessage($"Node removed: {node} (Inactive)");
        }
    }

    private bool ValidateSignature(string nodeAddress, string signature)
    {
        // Validate the signature using HMACSHA256 with the server secret
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.Default.SmartXchain)))
        {
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(nodeAddress));
            var computedSignature = Convert.ToBase64String(computedHash);

            return computedSignature == signature;
        }
    }

    private async void SynchronizeWithPeers()
    {
        while (true)
        {
            foreach (var peer in _peerServers)
                try
                {
                    using var httpClient = new HttpClient { BaseAddress = new Uri(peer) };

                    var content = new StringContent(JsonSerializer.Serialize(""), Encoding.UTF8,
                        "application/json");
                    var response = await httpClient.PostAsync("/api/GetNodes", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();

                        foreach (var node in responseBody.Split(','))
                            _registeredNodes.TryAdd(node, DateTime.UtcNow);
                    }
                    else
                    {
                        Logger.LogMessage($"Error synchronizing with peer {peer}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error synchronizing with peer {peer}: {ex.Message}");
                }

            await Task.Delay(20000);
        }
    }

    internal static async void BroadcastToPeers(List<string> serversList, string command, string message)
    {
        foreach (var peer in serversList)
            await Task.Run(async () =>
            {
                try
                {
                    using var httpClient = new HttpClient { BaseAddress = new Uri(peer) };
                    //var content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8,
                    //    "application/json");                    
                    var content = new StringContent(message);

                    var url = $"/api/{command}";
                    Logger.LogMessage($"BroadcastToPeers async: {url}\n{content}");
                    var response = await httpClient.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = response.Content.ReadAsStringAsync().Result;
                        Logger.LogMessage($"BroadcastToPeers response: {responseString}");
                    }
                    else
                    {
                        var error = $"ERROR: BroadcastToPeers {response.StatusCode} - {response.ReasonPhrase}";
                        Logger.LogMessage(error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Error sending to peer {peer}: {ex.Message}");
                }
            });
    }

    public class NodeStartupResult
    {
        public NodeStartupResult(Blockchain blockchain, Node node)
        {
            Blockchain = blockchain;
            Node = node;
        }

        public Blockchain Blockchain { get; set; }
        public Node Node { get; private set; }
    }
}