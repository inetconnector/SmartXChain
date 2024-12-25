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
using Block = SmartXChain.BlockchainCore.Block;
using Transaction = SmartXChain.BlockchainCore.Transaction;

namespace SmartXChain.Server;

public class BlockchainServer
{
    private const int HeartbeatTimeoutSeconds = 30;
    private const int MaxChunkSize = 64 * 1024; // 64 KB
    private readonly HashSet<string> _peerServerAdresses = new();
    private readonly ConcurrentDictionary<string, DateTime> _registeredNodes = new();

    public string ServerAddressExtern { get; }
    public string ServerAddressIntern { get; }

    private Blockchain _blockchain;

    public BlockchainServer(Blockchain blockchain, string externIP, string internIP)
    {
        ServerAddressExtern = $"tcp://{externIP}:{Config.Default.Port}";
        ServerAddressIntern = $"tcp://{internIP}:{Config.Default.Port}";

        Console.WriteLine($"Starting server at {ServerAddressIntern}/{ServerAddressExtern}...");
        _blockchain = blockchain;
    }
    public static async Task<(BlockchainServer?, NodeStartupResult?)> StartServerAsync()
    {
        NodeStartupResult? result = null;

        while (true)
        {
            result = await StartNode(Config.Default.MinerAddress);
             
            if (result is null or { Blockchain: null })
            {
                Thread.Sleep(1000);
            }
            else
            {
                break;
            }
        }

        var tcs = new TaskCompletionSource<BlockchainServer?>();

        Task.Run(() =>
        {
            try
            {
                var server = new BlockchainServer(result.Blockchain, NetworkUtils.IP, NetworkUtils.GetLocalIP());
                server.Start();

                Console.WriteLine(
                    $"Server node for blockchain '{Config.Default.SmartXchain}' started at {NetworkUtils.IP}");

                tcs.SetResult(server);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error starting server: {ex.Message}");
                tcs.SetResult(null);
            }
        });

        var server = await tcs.Task;
        Node.StartupResult = result;
        return (server, result);
    }

    public static async Task<NodeStartupResult> StartNode(string walletAddress)
    {
        var node = await Node.Start();
        var consensus = new SnowmanConsensus(10, node);

        var blockchain = new Blockchain(2, 5, walletAddress, consensus);
         
        var nodeTransaction = new Transaction
        {
            Sender = Blockchain.SystemAddress,
            Recipient = Blockchain.SystemAddress,
            Amount = 0,
            Data = Convert.ToBase64String(Encoding.ASCII.GetBytes(NetworkUtils.IP)),
            Timestamp = DateTime.UtcNow
        };

        blockchain.AddTransaction(nodeTransaction);

        return new NodeStartupResult(consensus, blockchain, node);
    }

    public void Start()
    {
        Task.Run(() => DiscoverAndRegisterWithPeers());
        Task.Run(() => StartMainServer());
        Task.Run(() => SynchronizeWithPeers(_peerServerAdresses));
    }

    private void SendResponse(ResponseSocket server, string response)
    {
        try
        {
            server.SendFrame(response);
            if (response.Length>0)
                  Console.WriteLine($"Sent response: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending response: {response}. Exception: {ex.Message}");
        }
    }

    private void DiscoverAndRegisterWithPeers()
    {
        var validPeers = new List<string>();

        try
        {
            foreach (var peer in Config.Default.Peers)
                if (!string.IsNullOrEmpty(peer) && peer.StartsWith("tcp://"))
                {
                    if (peer == ServerAddressExtern || peer == ServerAddressIntern) continue;

                    _peerServerAdresses.Add(peer);
                    validPeers.Add(peer);
                }

            Console.WriteLine($"Static peers discovered: {string.Join(", ", validPeers)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing static peers: {ex.Message}");
        }
    }

    private void StartMainServer(string newServerAddressIntern = "", string newServerAddresesExtern = "")
    {
        var serverAddressExtern = new string(ServerAddressExtern); 
        if (newServerAddresesExtern != serverAddressExtern)
        {
            serverAddressExtern = newServerAddressIntern;
        }
        var serverAddress = new string(ServerAddressIntern);
        if (newServerAddressIntern != serverAddress)
        {
            serverAddress = newServerAddressIntern;
        }
        while (true)
        {
            using var server = new ResponseSocket();
            try
            {
                server.Bind(serverAddress);
                Console.WriteLine(
                    $"SmartXchain {Config.Default.SmartXchain} bound to: {serverAddress}/{serverAddressExtern}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error binding SmartXchain {Config.Default.SmartXchain} to {serverAddress}/{serverAddressExtern} {ex.Message}");
                return;
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
                            SendResponse(server, response);
                            continue;
                        }

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

    private string RemoveFingerprint(string message)
    {
        return message.Substring(Crypt.AssemblyFingerprint.Length + 1);
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
            SendResponse(server, HandleVote(message));
        }
        else if (message.StartsWith("VerifyCode:"))
        {
            SendResponse(server, HandleVerifyCode(message));
        }
        else if (message.StartsWith("Heartbeat:"))
        {
            HandleHeartbeat(message);
            SendResponse(server, "OK");
        }
        else if (message.StartsWith("GetBlockCount:"))
        {
            SendResponse(server, _blockchain.Chain.Count.ToString());
        }
        else if (message.StartsWith("GetChain"))
        {
            PushChain(server).Wait();
        }
        //else if (message.StartsWith("PushChain:"))
        //{
        //    var serializedChain = message.Substring("PushChain:".Length);
        //    HasChain(serializedChain);
        //}
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
            SendResponse(server, transaction != null && AddTransaction(transaction) ? "OK" : "ERROR: Transaction rejected");
        }
        else
        {
            Console.WriteLine("Unknown message type received:" + message);
            SendResponse(server, "ERROR: Unknown message");
        }
    }

    private void HandleRegistration(string message, ResponseSocket server)
    {
        try
        {
            var parts = message.Split(new[] { ':' });
            if (parts.Length != 5 && parts[0] != "Register")
            {
                SendResponse(server, "ERROR: Invalid registration format");
                return;
            }

            var nodeAddress = parts[1] + ":" + parts[2] + ":" + parts[3];
            var signature = parts[4];

            if (!ValidateSignature(nodeAddress, signature))
            {
                SendResponse(server, $"ERROR: Invalid signature {signature}");
                return;
            }

            var now = DateTime.UtcNow;
            _registeredNodes[nodeAddress] = now;
            Console.WriteLine($"Registering {nodeAddress} - {now} (HandleRegistration)");

            Console.WriteLine($"Node registered: {nodeAddress}");

            BroadcastToPeers($"Register:{nodeAddress}");
            SendResponse(server, "OK");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Node not registered: {message}");
            Console.WriteLine(e);
            SendResponse(server, "ERROR: Registration failed");
        }
    }

    private void HandleGetNodes(ResponseSocket server)
    {
        RemoveInactiveNodes();
        var nodes = string.Join(",", _registeredNodes.Keys);
        SendResponse(server, nodes);
        if (!nodes.Contains(ServerAddressExtern))
        { 
            SynchronizeWithPeers(_peerServerAdresses);
        }
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
            if (block != null && block.CalculateHash() == block.Hash)
                return "OK";
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
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private void RemoveInactiveNodes()
    {
        var now = DateTime.UtcNow;

        foreach (var node in _registeredNodes)
        {
            Console.WriteLine(node.Key + ":" + node.Value);
        }

        var inactiveNodes = _registeredNodes
            .Where(kvp => (now - kvp.Value).TotalSeconds > HeartbeatTimeoutSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var node in inactiveNodes)
        {
            _registeredNodes.TryRemove(node, out _);
            Console.WriteLine($"Node removed: {node} (Inactive)");
        }
    }

    private bool ValidateSignature(string nodeAddress, string signature)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.Default.SmartXchain)))
        {
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(nodeAddress));
            var computedSignature = Convert.ToBase64String(computedHash);
            return computedSignature == signature;
        }
    }

    private void BroadcastToPeers(string message)
    {
        foreach (var peer in _peerServerAdresses)
            Task.Run(async () =>
            {
                try
                {
                    var response = await SocketManager.GetInstance(peer).SendMessageAsync(message);
                    Console.WriteLine($"Response from peer {peer}: {response}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending to peer {peer}: {ex.Message}");
                }
            });
    }

    private void SynchronizeWithPeers(HashSet<string> peerServerAdresses)
    {
        while (true)
        {
            foreach (var peerAddress in peerServerAdresses)
                try
                { 
                    var response = SocketManager.GetInstance(peerAddress).SendMessageAsync("GetNodes").Result;

                    foreach (var node in response.Split(','))
                    {
                        if (node.Length>0 && !_registeredNodes.ContainsKey(node))
                        {
                            var now = DateTime.UtcNow;
                            Console.WriteLine($"Registering {node} - {now} (SynchronizeWithPeers)");
                            _registeredNodes.TryAdd(node, now);
                        } 
                    } 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error synchronizing with peer {peerAddress}: {ex.Message}");
                }

            Thread.Sleep(5000);
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

        public SnowmanConsensus Consensus { get; }
        public Blockchain Blockchain { get; set; }
        public Node Node { get; }
    }

    #region Chain
    //public bool HasChain(string serializedChain)
    //{
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

    private async Task PushChain(ResponseSocket server)
    { 
        var tmpFile = Path.GetTempFileName();
        _blockchain.Save(tmpFile);

        const int bufferSize = 32768; 
        var buffer = new byte[bufferSize];
        var offset = 0L;

        using (var fileStream = new FileStream(tmpFile, FileMode.Open, FileAccess.Read))
        {
            var totalLength = fileStream.Length;

            while (offset < totalLength)
            { 
                var bytesRead = await fileStream.ReadAsync(buffer, 0, bufferSize);
                if (bytesRead == 0) break;
                 
                var chunk = Convert.ToBase64String(buffer, 0, bytesRead);
                 
                await Task.Run(() => SendResponse(server, Crypt.AssemblyFingerprint + "#" + chunk));
                offset += bytesRead;

                Console.WriteLine($"Sent chunk {offset}/{totalLength}");
            }
        }
         
        await Task.Run(() => SendResponse(server, Crypt.AssemblyFingerprint + "#" + "END"));
        Console.WriteLine("Finished sending blockchain file in chunks.");
    }


    public bool AddTransaction(Transaction transaction)
    {
        if (!IsChainCurrent())
        {
            Console.WriteLine("Blockchain is not current. Transaction rejected.");
            return false;
        }

        _blockchain.AddTransaction(transaction);

        var newBlock = _blockchain.MinePendingTransactions(Config.Default.MinerAddress);
        BroadcastToPeers("NewBlock:" + JsonSerializer.Serialize(newBlock));

        Console.WriteLine("Transaction added and new block mined.");
        return true;
    }

    private bool IsChainCurrent()
    {
        foreach (var peer in _peerServerAdresses)
            try
            {
                var response = SocketManager.GetInstance(peer).SendMessageAsync("GetChain").Result;
                var peerChain = JsonSerializer.Deserialize<Blockchain>(response);

                if (peerChain != null && peerChain.Chain.Count > _blockchain.Chain.Count && peerChain.IsValid())
                {
                    Console.WriteLine("Local chain is not the most recent.");
                    return false;
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
