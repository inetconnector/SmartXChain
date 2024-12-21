using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlockchainProject.Utils;
using BlockchainProject.Validators;
using NetMQ;
using NetMQ.Sockets;
using SmartXChain.Utils;

public class BlockchainServer
{
    private const int HeartbeatTimeoutSeconds = 30; // Maximum time before a node is considered inactive
    private const int MaxChunkSize = 64 * 1024; // 64 KB chunk size for PushChain
    private readonly HashSet<string> _peerServers = new(); // Addresses of other peer registration servers

    private readonly ConcurrentDictionary<string, DateTime>
        _registeredNodes = new(); // Registered node addresses and their last heartbeat timestamp

    private readonly string _serverAddress; // Address of this server 
    private Blockchain _blockchain;
    public static async Task<BlockchainServer?> StartServerAsync()
    {
        var serverAddress = $"tcp://{NetworkUtils.GetLocalIP()}:{Config.Default.Port}";

        var ip = await NetworkUtils.GetPublicIPAsync();
        var nodeAddress = $"tcp://{ip}:{Config.Default.Port}"; // Own node address
        var sharedSecret = Config.Default.ServerSecret; // Shared secret with the servers 
        var node = new Node(nodeAddress, sharedSecret);
        BlockchainServer? server = null;
        Console.WriteLine("Starting server node...");
        var thread = new Thread(() =>
        {
            try
            {
                server = new BlockchainServer(serverAddress, node);
                server.Start();

                Console.WriteLine($"Server node for blockchain {Config.Default.ServerSecret} started at {serverAddress}");
            }
            catch (Exception e)
            {
            }
        })
        {
            IsBackground = true
        };
        thread.Start();
        return server;
    }
    public BlockchainServer(string serverAddress, Node node)
    {
        _serverAddress = serverAddress;

        var consensus = new SnowmanConsensus(10, node);
        var blockchain = new Blockchain(2, 5, GetMinerAddress(), consensus);

        _blockchain = blockchain;
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
                    if (peer == _serverAddress) continue;

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
        using (var server = new ResponseSocket())
        {
            try
            {
                server.Bind(_serverAddress);
                Console.WriteLine($"Registration server {Config.Default.ServerSecret} running at: {_serverAddress}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting server at {_serverAddress}: {ex.Message}");
                return; // Exit if the server cannot start
            }

            while (true)
                try
                {
                    var message = server.ReceiveFrameString();
                    if (!message.StartsWith(Crypt.GetExecutingAssemblyFingerprint()))
                    {
                        Console.WriteLine("Invalid fingerprint detected. Dropping message.");
                        continue;
                    }

                    // Remove fingerprint and handle message
                    var strippedMessage = RemoveFingerprint(message);
                    Console.WriteLine($"Received message: {strippedMessage}");

                    ProcessMessage(strippedMessage, server);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
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
            server.SendFrame("OK");
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
            server.SendFrame(transaction != null && AddTransaction(transaction) ? "OK" : "ERROR: Transaction rejected");
        }
        else
        {
            Console.WriteLine("Unknown message type received:" + message);
            server.SendFrame("ERROR: Unknown message");
        }
    }

    private string RemoveFingerprint(string message)
    {
        return message.Substring(Crypt.GetExecutingAssemblyFingerprint().Length + 1);
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
        server.SendFrame("OK");
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
                if (block.CalculateHash() == hash) return "OK";
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
        return isCodeSafe ? "OK" : "";
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

        _registeredNodes[nodeAddress] = DateTime.UtcNow;

        Console.WriteLine($"Heartbeat of node {nodeAddress} received.");
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
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.Default.ServerSecret)))
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
                        client.SendFrame(message);
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
                        client.SendFrame("GetNodes");
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
        var serializedChain = JsonSerializer.Serialize(_blockchain);
        var totalLength = serializedChain.Length;
        var offset = 0;

        while (offset < totalLength)
        {
            var chunkSize = Math.Min(MaxChunkSize, totalLength - offset);
            var chunk = serializedChain.Substring(offset, chunkSize);

            // Send each chunk with an identifier
            await Task.Run(() => server.SendFrame(chunk));
            offset += chunkSize;

            Console.WriteLine($"Sent chunk {offset}/{totalLength}");
        }

        // Send a termination message to indicate the end of the chain
        await Task.Run(() => server.SendFrame("END"));
        Console.WriteLine("Finished sending blockchain in chunks.");
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
        var newBlock = _blockchain.MinePendingTransactions(GetMinerAddress());
        BroadcastToPeers("NewBlock:" + JsonSerializer.Serialize(newBlock));

        Console.WriteLine("Transaction added and new block mined.");
        return true;
    }

    private string GetMinerAddress()
    {
        var minerAddress = Config.Default.MinerAddress;

        if (string.IsNullOrEmpty(minerAddress))
        {
            var newWallet = TrustWalletCompatibleWallet.GenerateWallet();
            minerAddress = newWallet.Item1.First();
            Config.Default.SetMinerAddress(minerAddress, newWallet.Item3);
            Console.WriteLine("New miner address generated and saved.");
        }

        return minerAddress;
    }

    private bool IsChainCurrent()
    {
        foreach (var peer in _peerServers)
            try
            {
                using (var client = new RequestSocket())
                {
                    client.Connect(peer);
                    client.SendFrame("GetChain");
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