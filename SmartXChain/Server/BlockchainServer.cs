using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetMQ;
using NetMQ.Sockets;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using SmartXChain.Validators;

namespace SmartXChain.Server;

public class BlockchainServer
{
    private const int HeartbeatTimeoutSeconds = 30; // Maximum time before a node is considered inactive
    private const int MaxChunkSize = 64 * 1024; // 64 KB chunk size for PushChain
    private readonly HashSet<string> _peerServers = new(); // Addresses of other peer registration servers

    private readonly ConcurrentDictionary<string, DateTime>
        _registeredNodes = new(); // Registered node addresses and their last heartbeat timestamp

    private readonly string _serverAddressExtern; // Extern Address of this server 
    private readonly string _serverAddressIntern; // Intern address of this server 
    private Blockchain _blockchain;

    public BlockchainServer(Blockchain blockchain, string externIP, string internIP)
    {
        _serverAddressExtern = $"tcp://{externIP}:{Config.Default.Port}";
        _serverAddressIntern = $"tcp://{internIP}:{Config.Default.Port}";

        Console.WriteLine($"Starting server at {_serverAddressIntern}/{_serverAddressExtern}...");
        _blockchain = blockchain;
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
        var blockchain = new Blockchain(2,   walletAddress, consensus);

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
                if (!string.IsNullOrEmpty(peer) && peer.StartsWith("tcp://"))
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

    private void StartMainServer()
    {
        while (true)
        {
            using var server = new ResponseSocket();
            try
            {
                server.Bind(_serverAddressIntern);
                Console.WriteLine(
                    $"SmartXchain {Config.Default.SmartXchain} bound to: {_serverAddressIntern}/{_serverAddressExtern}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Error binding SmartXchain {Config.Default.SmartXchain} to {_serverAddressExtern} {ex.Message} {ex.InnerException.Message}");
                return; // Exit if the server cannot start
            }

            while (true)
            {
                var message = "";
                try
                {
                    message = server.ReceiveFrameString();
                    if (!string.IsNullOrEmpty(message))
                    {
                        if (!Config.Default.Debug && !message.StartsWith(Crypt.AssemblyFingerprint))
                        {
                            var response = "Invalid fingerprint detected";
                            Console.WriteLine($"{response} Dropping message '{message}'");
                            server.SendFrame(response);
                            continue;
                        }

                        // Remove fingerprint and handle message
                        var strippedMessage = RemoveFingerprint(message);
                        Console.WriteLine($"Received message: {strippedMessage}");

                        ProcessMessage(strippedMessage, server);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message '{message}'\n{ex.Message}");
                    server.Close();
                    break;
                }
            }
        }
    }

    private void ProcessMessage(string message, ResponseSocket server)
    {
        if (message.StartsWith("Register:"))
        {
            HandleRegistration(message, server);
        }
        else if (message == "GetNodes")
        {
            HandleGetNodes(server);
        }
        else if (message.StartsWith("Vote:"))
        {
            server.SendFrame(HandleVote(message));
        }
        else if (message.StartsWith("VerifyCode:"))
        {
            server.SendFrame(HandleVerifyCode(message));
        }
        else if (message.StartsWith("Heartbeat:"))
        {
            HandleHeartbeat(message);
            server.SendFrame("ok");
        }
        else if (message.StartsWith("GetBlockCount"))
        {
            server.SendFrame(_blockchain.Chain.Count.ToString());
        }
        else if (message.StartsWith("GetChain"))
        {
            PushChain(server);
        }
        else if (message.StartsWith("PushChain:"))
        {
            var serializedChain = message.Substring("PushChain:".Length);
            HasChain(serializedChain);
        }
        else if (message.StartsWith("NewBlock:"))
        {
            var serializedBlock = message.Substring("NewBlock:".Length);
            var newBlock = JsonSerializer.Deserialize<Block>(serializedBlock);

            if (newBlock != null && _blockchain.AddBlock(newBlock))
                Console.WriteLine("New block added to the blockchain.");
            else
                Console.WriteLine("Failed to add new block. Invalid or already present.");
        }
        else if (message.StartsWith("AddTransaction:"))
        {
            var transactionJson = message.Substring("AddTransaction:".Length);
            var transaction = JsonSerializer.Deserialize<Transaction>(transactionJson);
            server.SendFrame(transaction != null && AddTransaction(transaction) ? "ok" : "ERROR: Transaction rejected");
        }
        else
        {
            Console.WriteLine("Unknown message type received:" + message);
            server.SendFrame("ERROR: Unknown message");
        }
    }

    private string RemoveFingerprint(string message)
    {
        return message.Substring(Crypt.AssemblyFingerprint.Length + 1);
    }

    private void HandleRegistration(string message, ResponseSocket server)
    {
        // Example message: "Register:tcp://127.0.0.1:signature"

        // Split the message into two parts: "Register" and the remaining data
        var parts = message.Split(new[] { ':' }, 2);
        if (parts.Length != 2 || parts[0] != "Register")
        {
            server.SendFrame("ERROR: Invalid registration format");
            return;
        }

        var remainingParts = parts[1];

        // Split the remaining data into node address and signature
        var addressSignatureParts = remainingParts.Split(new[] { ':' }, 2);
        if (addressSignatureParts.Length != 2)
        {
            server.SendFrame("ERROR: Invalid registration format");
            return;
        }

        var nodeAddress = addressSignatureParts[0]; // Node address (e.g., "tcp://127.0.0.1")
        var signature = addressSignatureParts[1]; // Signature for validation

        // Security check using signature validation
        if (!ValidateSignature(nodeAddress, signature))
        {
            server.SendFrame("ERROR: Invalid signature");
            return;
        }

        // Register the node
        _registeredNodes[nodeAddress] = DateTime.UtcNow;
        Console.WriteLine($"Node registered: {nodeAddress}");

        // Broadcast the registration message to peer servers
        BroadcastToPeers($"Register:{nodeAddress}");

        // Send confirmation response
        server.SendFrame("ok");
    }

    private void HandleGetNodes(ResponseSocket server)
    {
        // Remove inactive nodes before sending the list
        RemoveInactiveNodes();

        // Combine registered node addresses into a single string
        var nodes = string.Join(",", _registeredNodes.Keys);
        server.SendFrame(nodes);
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
                if (block.CalculateHash() == hash) return "ok#"+ Config.Default.MinerAddress;
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
        // example: "Heartbeat:tcp://127.0.0.1:5555"

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

    private void BroadcastToPeers(string message)
    {
        // Send a message to all peer servers
        foreach (var peer in _peerServers)
            Task.Run(() =>
            {
                try
                {
                    using (var client = new RequestSocket())
                    {
                        client.Connect(peer);
                        client.SendFrame(Crypt.AssemblyFingerprint + "#" + message);
                        client.ReceiveFrameString(); // Ignore the response
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending to peer {peer}: {ex.Message}");
                }
            });
    }

    private void SynchronizeWithPeers()
    {
        while (true)
        {
            // Request the list of nodes from all peer servers
            foreach (var peer in _peerServers)
                try
                {
                    using (var client = new RequestSocket())
                    {
                        client.Connect(peer);
                        client.SendFrame(Crypt.AssemblyFingerprint + "#" + "GetNodes");
                        var response = client.ReceiveFrameString();

                        // Synchronize nodes from the response
                        foreach (var node in response.Split(','))
                            _registeredNodes.TryAdd(node, DateTime.UtcNow);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error synchronizing with peer {peer}: {ex.Message}");
                }

            Thread.Sleep(5000); // Synchronize every 5 seconds
        }
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

    #region Chain

    public bool HasChain(string serializedChain)
    {
        // Deserialize the incoming chain
        var incomingChain = JsonSerializer.Deserialize<Blockchain>(serializedChain);

        if (incomingChain != null && incomingChain.Chain.Count > _blockchain.Chain.Count && incomingChain.IsValid())
        {
            _blockchain = incomingChain;
            Console.WriteLine("Blockchain updated with longer valid chain.");
            return true;
        }

        Console.WriteLine("Incoming chain is invalid or not longer.");
        return false;
    }
    private async Task PushChain(ResponseSocket server)
    {
        var tmpFile = Path.GetTempFileName();
        _blockchain.Save(tmpFile);

        const int bufferSize = 32768;
        var buffer = new byte[bufferSize];
        var offset = 0L;

        await using (var fileStream = new FileStream(tmpFile, FileMode.Open, FileAccess.Read))
        {
            var totalLength = fileStream.Length;

            while (offset < totalLength)
            {
                var bytesRead = await fileStream.ReadAsync(buffer, 0, bufferSize);
                if (bytesRead == 0) break;

                var chunk = Convert.ToBase64String(buffer, 0, bytesRead);

                await Task.Run(() => server.SendFrame(Crypt.AssemblyFingerprint + "#" + chunk));
                offset += bytesRead;

                Console.WriteLine($"Sent chunk {offset}/{totalLength}");
            }
        }

        await Task.Run(() => server.SendFrame(Crypt.AssemblyFingerprint + "#" + "END"));
        Console.WriteLine("Finished sending blockchain file in chunks.");
    } 

    public bool AddTransaction(Transaction transaction)
    {
        // Ensure the blockchain is current before adding a transaction
        if (!IsChainCurrent())
        {
            Console.WriteLine("Blockchain is not current. Transaction rejected.");
            return false;
        }

        _blockchain.AddTransaction(transaction);

        // Mine a new block after adding the transaction
        var newBlock = _blockchain.MinePendingTransactions(Config.Default.MinerAddress);
        BroadcastToPeers("NewBlock:" + JsonSerializer.Serialize(newBlock));

        Console.WriteLine("Transaction added and new block mined.");
        return true;
    }


    private bool IsChainCurrent()
    {
        foreach (var peer in _peerServers)
            try
            {
                using (var client = new RequestSocket())
                {
                    client.Connect(peer);
                    client.SendFrame(Crypt.AssemblyFingerprint + "#" + "GetChain");
                    var response = client.ReceiveFrameString();

                    var peerChain = JsonSerializer.Deserialize<Blockchain>(response);
                    if (peerChain != null && peerChain.Chain.Count > _blockchain.Chain.Count && peerChain.IsValid())
                    {
                        Console.WriteLine("Local chain is not the most recent.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking chain with peer {peer}: {ex.Message}");
            }

        return true;
    }

    #endregion
}