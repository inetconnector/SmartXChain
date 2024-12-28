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
using SmartXChain.Validators;
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
    private Blockchain _blockchain;

    public BlockchainServer(Blockchain blockchain, string externIP, string internIP)
    {
        _serverAddressExtern = $"http://{externIP}:{Config.Default.Port}";
        _serverAddressIntern = $"http://{internIP}:{Config.Default.Port}";

        Console.WriteLine($"Starting server at {_serverAddressIntern}/{_serverAddressExtern}...");
        _blockchain = blockchain;
    }

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

                            endpoints.MapPost("/api/Broadcast", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"Broadcast: {message}");
                                var result = HandleBroadcast(message);
                                await context.Response.WriteAsync(result);
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
                                Logger.LogMessage($"GetBlockCount: {message}");
                                await context.Response.WriteAsync(_blockchain.Chain.Count.ToString());
                            });

                            endpoints.MapPost("/api/GetChain", async context =>
                            {
                                var message = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"GetChain: {message}");
                                await context.Response.WriteAsync(JsonSerializer.Serialize(_blockchain.Chain));
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

                                if (blockIndex < 0 || blockIndex >= _blockchain.Chain.Count)
                                {
                                    context.Response.StatusCode = 404; // Not Found
                                    await context.Response.WriteAsync($"Error: Block {blockIndex} not found.");
                                    return;
                                }

                                var block = _blockchain.Chain[blockIndex];
                                var blockJson = JsonSerializer.Serialize(block);

                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(blockJson);
                            });

                            endpoints.MapPost("/api/PushChain", async context =>
                            {
                                var serializedChain = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"PushChain: {serializedChain}");
                                var incomingChain = JsonSerializer.Deserialize<Blockchain>(serializedChain);
                                if (_blockchain.SmartContracts.Count == 0)
                                {
                                    lock (_blockchain.Chain)
                                    {
                                        if (incomingChain != null &&
                                            incomingChain.Chain.Count > _blockchain.Chain.Count &&
                                            incomingChain.IsValid())
                                            _blockchain = incomingChain;
                                    }

                                    await context.Response.WriteAsync("ok");
                                }
                                else
                                {
                                    await context.Response.WriteAsync("");
                                }
                            });

                            endpoints.MapPost("/api/NewBlock", async context =>
                            {
                                var serializedBlock = await new StreamReader(context.Request.Body).ReadToEndAsync();
                                Logger.LogMessage($"NewBlock: {serializedBlock}");
                                var newBlock = JsonSerializer.Deserialize<Block>(serializedBlock);

                                if (newBlock != null && _blockchain.AddBlock(newBlock, true, false))
                                    await context.Response.WriteAsync("ok");
                                else
                                    await context.Response.WriteAsync("");
                            });

                            //endpoints.MapPost("/api/AddTransaction", async context =>
                            //{
                            //    var transactionJson = await new StreamReader(context.Request.Body).ReadToEndAsync();
                            //    Logger.LogMessage($"AddTransaction: {transactionJson}");
                            //    var transaction = JsonSerializer.Deserialize<Transaction>(transactionJson);

                            //    if (transaction != null && await AddTransaction(transaction))
                            //        await context.Response.WriteAsync("ok");
                            //    else
                            //        await context.Response.WriteAsync("");
                            //});
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
                server = new BlockchainServer(result.Blockchain, NetworkUtils.IP, NetworkUtils.GetLocalIP());
                server.Start();

                Console.WriteLine(
                    $"Server node for blockchain '{Config.Default.SmartXchain}' started at {NetworkUtils.IP}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error starting server: {ex.Message}");
            }
        });

        return (server, result);
    }

    public static async Task<NodeStartupResult> StartNode(string walletAddress)
    {
        var node = await Node.Start();
        var consensus = new SnowmanConsensus(10, node);

        // Create blockchain
        var blockchain = new Blockchain(2, walletAddress, consensus);

        // Publish server IP
        var nodeTransaction = new Transaction
        {
            Sender = Blockchain.SystemAddress,
            Recipient = Blockchain.SystemAddress,
            Data = Convert.ToBase64String(Encoding.ASCII.GetBytes(NetworkUtils.IP)), // Store data as Base64 string
            Timestamp = DateTime.UtcNow
        };

        blockchain.AddTransaction(nodeTransaction);

        return new NodeStartupResult(consensus, blockchain, node);
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

            Console.WriteLine($"Static peers discovered: {string.Join(", ", validPeers)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing static peers: {ex.Message}");
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
        if (!ValidateSignature(nodeAddress, signature)) return "Invalid signature";

        // Register the node
        _registeredNodes[nodeAddress] = DateTime.UtcNow;
        Console.WriteLine($"Node registered: {nodeAddress}");

        // Broadcast the registration message to peer servers
        BroadcastToPeers($"Register:{nodeAddress}", _peerServers);

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

    private string HandleBroadcast(string message)
    {
        _registeredNodes.TryAdd(message, DateTime.UtcNow);
        // Combine registered node addresses into a single string
        var nodes = string.Join(",", _registeredNodes.Keys);
        return nodes;
    }


    private string HandleVote(string message)
    {
        const string prefix = "Vote:";
        if (!message.StartsWith(prefix))
        {
            Console.WriteLine("Invalid Vote message received.");
            return "";
        }

        try
        {
            var base64 = message.Substring(prefix.Length);
            var block = Block.LoadFromBase64(base64);
            if (block != null)
            {
                var hash = block.Hash;
                if (block.CalculateHash() == hash) return "ok#" + Config.Default.MinerAddress;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Invalid Vote message received. {e.Message}");
        }

        return "";
    }

    private string HandleVerifyCode(string message)
    {
        const string prefix = "VerifyCode:";
        if (!message.StartsWith(prefix))
        {
            Console.WriteLine("Invalid verification request received.");
            return "";
        }

        var compressedBase64Data = message.Substring(prefix.Length);
        var code = Compress.DecompressString(Convert.FromBase64String(compressedBase64Data));
        var isCodeSafe = CodeSecurityAnalyzer.IsCodeSafe(code);
        return isCodeSafe ? "ok" : "";
    }

    private void HandleHeartbeat(string message)
    {
        const string prefix = "Heartbeat:";
        if (!message.StartsWith(prefix))
        {
            Console.WriteLine("Invalid Heartbeat message received.");
            return;
        }

        var nodeAddress = message.Substring(prefix.Length);

        if (!Uri.IsWellFormedUriString(nodeAddress, UriKind.Absolute))
        {
            Console.WriteLine("Invalid node address in heartbeat received.");
            return;
        }

        var now = DateTime.UtcNow;
        _registeredNodes[nodeAddress] = now;
        Console.WriteLine($"Heartbeat {nodeAddress} - {now} (HandleHeartbeat)");

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
            Console.WriteLine($"Node removed: {node} (Inactive)");
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
                        Console.WriteLine($"Error synchronizing with peer {peer}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error synchronizing with peer {peer}: {ex.Message}");
                }

            await Task.Delay(20000);
        }
    }

    internal static async void BroadcastToPeers(string message, List<string> serversList)
    {
        foreach (var peer in serversList)
            await Task.Run(async () =>
            {
                try
                {
                    using var httpClient = new HttpClient { BaseAddress = new Uri(peer) };
                    var payload = new { Message = message };

                    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8,
                        "application/json");
                    var response = await httpClient.PostAsync("/api/Broadcast", content);

                    if (!response.IsSuccessStatusCode)
                        Console.WriteLine($"Error sending to peer {peer}: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending to peer {peer}: {ex.Message}");
                }
            });
    }

    public class NodeStartupResult
    {
        public NodeStartupResult(SnowmanConsensus consensus, Blockchain blockchain, Node node)
        {
            Consensus = consensus;
            Blockchain = blockchain;
            Node = node;
        }

        public SnowmanConsensus Consensus { get; private set; }
        public Blockchain Blockchain { get; set; }
        public Node Node { get; private set; }
    }

    //public bool HasChain(string serializedChain)
    //{
    //    // Deserialize the incoming chain
    //    var incomingChain = JsonSerializer.Deserialize<Blockchain>(serializedChain);

    //    if (incomingChain != null && incomingChain.Chain.Count > _blockchain.Chain.Count && incomingChain.IsValid())
    //    {
    //        _blockchain = incomingChain;
    //        Console.WriteLine("Blockchain updated with longer valid chain.");
    //        return true;
    //    }

    //    Console.WriteLine("Incoming chain is invalid or not longer.");
    //    return false;
    //}

    //public async Task<bool> AddTransaction(Transaction transaction)
    //{
    //    // Ensure the blockchain is current before adding a transaction
    //    if (!await IsChainCurrent())
    //    {
    //        Console.WriteLine("Blockchain is not current. Transaction rejected.");
    //        return false;
    //    }

    //    _blockchain.AddTransaction(transaction);

    //    // Mine a new block after adding the transaction
    //    var newBlock = _blockchain.MinePendingTransactions(Config.Default.MinerAddress);
    //    BroadcastToPeers("NewBlock:" + JsonSerializer.Serialize(newBlock));

    //    Console.WriteLine("Transaction added and new block mined.");
    //    return true;
    //}

    //private async Task<bool> IsChainCurrent()
    //{
    //    foreach (var peer in _peerServers)
    //        try
    //        {
    //            using var httpClient = new HttpClient { BaseAddress = new Uri(peer) };

    //            var response = await httpClient.GetAsync("/api/GetBlockCount");

    //            if (response.IsSuccessStatusCode)
    //            {
    //                var responseBody = await response.Content.ReadAsStringAsync();
    //                var peerChain = JsonSerializer.Deserialize<int>(responseBody);

    //                if (peerChain > _blockchain.Chain.Count)
    //                {
    //                    Console.WriteLine("Local chain is not the most recent.");
    //                    return false;
    //                }
    //            }
    //            else
    //            {
    //                Console.WriteLine($"Error checking chain with peer {peer}: {response.StatusCode}");
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            Console.WriteLine($"Error checking chain with peer {peer}: {ex.Message}");
    //        }

    //    return true;
    //}
}