using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.IdentityModel.Tokens; 
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts; 
using SmartXChain.Utils;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.ClientServer;

/// <summary>
///     Represents the blockchain server responsible for managing node registration, starting the server,
///     and synchronizing with peers.
/// </summary>
public class BlockchainServer
{
    /// <summary>
    ///     Fetches the public key of a peer for establishing secure communication.
    /// </summary> 
    /// <returns>The public key of the peer as a byte array, or null if retrieval fails.</returns>
    private static readonly ConcurrentDictionary<string, byte[]> PublicKeyCache = new();

    /// <summary>
    ///     Represents the startup state of the blockchain node.
    /// </summary>
    internal static NodeStartupResult Startup { get; private set; }


    /// <summary>
    ///     Starts a new node on the blockchain and initializes the blockchain for that node.
    /// </summary>
    /// <returns>A Task that resolves to a <see cref="NodeStartupResult" /> containing the blockchain and node information.</returns>
    /// <summary>
    ///     Starts the server asynchronously.
    /// </summary>
    public static async Task<NodeStartupResult> StartServerAsync(bool loadExisting = true)
    {
        var server = new BlockchainServer();
        var signalHubs = Config.Default.SignalHubs;
        var node = await Node.Start();
        var blockchain = new Blockchain(Config.Default.MinerAddress, node.ChainId);

        if (signalHubs.Count == 0)
            throw new InvalidOperationException("No SignalR hubs configured. Unable to start server.");

        SignalRClient? connectedClient = null;
        foreach (var signalHub in signalHubs)
            try
            {
                connectedClient = await CreatePeerConnection(signalHub, blockchain);
                if (connectedClient?.Connection != null &&
                    connectedClient.Connection.State == HubConnectionState.Connected)
                    break;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"connecting to SignalR hub {signalHub}");
            }

        if (connectedClient == null)
            throw new InvalidOperationException("Unable to connect to any configured SignalR hub.");

        var connectionId = await connectedClient.WaitForConnectionIdAsync(TimeSpan.FromSeconds(20));
        if (string.IsNullOrEmpty(connectionId))
            throw new InvalidOperationException("SignalR connection established but no connection id was provided.");

        Config.Default.NodeAddress = connectionId;
        SignalR = connectedClient;

        Startup = new NodeStartupResult(blockchain, node, server, connectedClient);

        Logger.Log(
            $"Server node for blockchain '{Config.Default.ChainId}' started at {Config.Default.NodeAddress}");

        SignalR.OnBroadcastMessageReceived += SignalR_OnBroadcastMessageReceived;

        if (Config.Default.RedisEnabled)
            await InitializeRedisDiscoveryAsync();

        await BroadcastAvailability();

        if (loadExisting)
        {
            var chainPath = "";
            try
            {
                chainPath = Path.Combine(Config.Default.BlockchainPath, "chain-" + Startup!.Node.ChainId);
                if (File.Exists(chainPath))
                {
                    Startup.Blockchain = Blockchain.Load(chainPath);
                }
                else
                {
                    Logger.Log($"No existing chain found in {chainPath}");
                    Logger.Log("Waiting for synchronization...");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"loading existing chain from {chainPath}");
                Logger.LogError($"{ex.Message}");
            }
        }

        await Sync();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                await Sync();
            }
        });

        return Startup;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BroadcastMessageType
    {
        Availability,
        Blocks
    }

    private string _offer;
    public static async Task<ChainInfo> Info(string info)
    {
        var chaininfo = ChainInfo.CreateChainInfo(Startup.Blockchain, info); 
        var connection = new WebRTC();
        await connection.InitializeAsync();  
        return chaininfo;
    }
    private static Task BroadcastAvailability()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                { 
                    var messageModel = new BroadcastMessageModel
                    {
                        Type = BroadcastMessageType.Availability,
                        Info = await Info(Config.Default.NodeAddress)
                    };

                    var jsonMessage = JsonSerializer.Serialize(messageModel);
                    await SignalR.BroadcastMessage(jsonMessage);
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, "retrieving nodes");
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        });

        return Task.CompletedTask;
    }

    private static async Task InitializeRedisDiscoveryAsync()
    {
        try
        {
            var registry = await RedisNodeRegistry.CreateAsync(
                Config.Default.RedisConnectionString,
                Config.Default.RedisNamespace,
                Config.Default.ChainId,
                Config.Default.NodeAddress,
                TimeSpan.FromSeconds(Config.Default.RedisHeartbeatSeconds),
                TimeSpan.FromSeconds(Config.Default.RedisNodeTtlSeconds));

            Startup.NodeRegistry = registry;

            registry.NodeDiscovered += HandleRedisNodeDiscoveredAsync;
            registry.NodeRemoved += address =>
            {
                Node.RemoveNodeAddress(address);
                Node.CurrentNodes_SDP.TryRemove(address, out _);
                return Task.CompletedTask;
            };

            await registry.RegisterSelfAsync(SignalR.ServerUrl, string.Empty);

            var knownNodes = await registry.GetKnownNodesAsync();
            foreach (var nodeInfo in knownNodes)
                await HandleRedisNodeDiscoveredAsync(nodeInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "initializing redis node discovery");
        }
    }

    private static async Task HandleRedisNodeDiscoveredAsync(RedisNodeRegistry.RedisNodeInfo nodeInfo)
    {
        if (string.IsNullOrWhiteSpace(nodeInfo.NodeAddress) ||
            nodeInfo.NodeAddress == Config.Default.NodeAddress)
            return;

        var sdp = await SignalR.GetOfferFromServer(nodeInfo.NodeAddress);
        if (string.IsNullOrEmpty(sdp))
        {
            Logger.LogWarning($"Failed to retrieve SDP offer for node {nodeInfo.NodeAddress} via SignalR.");
            return;
        }

        Node.CurrentNodes_SDP[nodeInfo.NodeAddress] = sdp;
        if (Node.CurrentNodes.Contains(nodeInfo.NodeAddress))
        {
            Node.CurrentNodes_LastActive[nodeInfo.NodeAddress] = DateTime.UtcNow;
            return;
        }
        Node.AddNode(nodeInfo.NodeAddress, sdp);
    }

    internal static async Task Sync()
    {
        foreach (var nodeAddress in Node.CurrentNodes)
        {
            if (nodeAddress == Config.Default.NodeAddress)
                continue;

            if (!Node.CurrentNodes_SDP.TryGetValue(nodeAddress, out var sdpAddress) ||
                string.IsNullOrEmpty(sdpAddress))
            {
                sdpAddress = await SignalR.GetOfferFromServer(nodeAddress);
                if (string.IsNullOrEmpty(sdpAddress))
                {
                    Logger.LogWarning($"Synchronization skipped: missing SDP for {nodeAddress}.");
                    continue;
                }

                Node.CurrentNodes_SDP[nodeAddress] = sdpAddress;
            }

            try
            {
                var infoPayload = (await Info(nodeAddress)).ToString();
                var response = await WebRTC.Manager.InitOpenAndSendAsync(nodeAddress, sdpAddress, "Nodes", infoPayload);

                if (string.IsNullOrEmpty(response))
                {
                    Logger.LogError($"Synchronization with peer {nodeAddress} failed or returned no response.");
                    continue;
                }

                var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
                if (responseObject == null)
                {
                    Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
                    continue;
                }

                if (Config.Default.Debug)
                    Logger.Log($"SynchronizeWithPeers  {nodeAddress} Result: {responseObject.Message}");

                foreach (var chain in Blockchain.Blockchains)
                    if (responseObject.FirstHash == chain.Chain.First().Hash &&
                        Startup.Blockchain != null && Startup.Blockchain.IsValid())
                    {
                        if (responseObject.BlockCount > chain.Chain.Count)
                            await GetRemoteChain(responseObject, chain.Chain.Count);
                    }
                    else
                    {
                        await GetRemoteChain(responseObject, 0);
                    }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to synchronize with peer {nodeAddress}");
            }
        }
    }

    private static async void SignalR_OnBroadcastMessageReceived(string message)
    {
        try
        {
            var broadcast = JsonSerializer.Deserialize<BroadcastMessageModel>(message);
            if (broadcast != null)
            {
                switch (broadcast.Type)
                {
                    case BroadcastMessageType.Availability:
                    {
                        if (broadcast.Info.Message != Config.Default.NodeAddress && !Node.CurrentNodes.Contains(broadcast.Info.Message))
                        {
                            Logger.Log($"Available node: {broadcast.Info.Message}");
                            if (!Node.CurrentNodes.Contains(broadcast.Info.Message))
                            { 
                                //get sdp via signalR
                                var sdp = await SignalR.GetOfferFromServer(broadcast.Info.NodeAddress); 
                                Node.AddNode(broadcast.Info.Message, sdp); 

                                var messageModel = new BroadcastMessageModel
                                {
                                    Type = BroadcastMessageType.Availability,
                                    Info = await Info(Config.Default.NodeAddress)
                                };

                                var jsonMessage = JsonSerializer.Serialize(messageModel);
                                await SignalR.BroadcastMessage(jsonMessage);
                            }
                        } 
                        break;
                    }
                    case BroadcastMessageType.Blocks:

                        //var blocks=
                        //await WebRtc.SendAsync(broadcast.Info.Message); 
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Fehler beim Verarbeiten der Broadcast-Nachricht: {ex.Message}");
        }
    }

    public class BroadcastMessageModel
    {
        public BroadcastMessageType Type { get; set; }
        public ChainInfo Info { get; set; } 
    }
       
    public static SignalRClient SignalR { get; set; }

    private static async Task<SignalRClient> CreatePeerConnection(string signalHub, Blockchain blockchain)
    {
        // Load your secret for JWT
        var signalRSigningKey = Environment.GetEnvironmentVariable("SIGNALR_PASSWORD");
        var name = "smartXchain";

        // Instantiate our SignalR client
        var signalRClient = new SignalRClient();

        // Generate a JWT token for the SignalR server
        var jwtToken = GenerateJwtToken(name, signalRSigningKey);

        // Connect the SignalR client to the server
        await signalRClient.ConnectAsync(signalHub, jwtToken);

        return signalRClient;
    }
     

    // JWT generator for SignalR authentication
    private static string GenerateJwtToken(string issuer, string signingKey)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(issuer + signingKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = issuer,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            )
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
      
     
    private static async Task GetRemoteChain(ChainInfo responseObject, int fromBlock,
        int chunkSize = 40)
    {
        for (var block = fromBlock; block < responseObject.BlockCount; block += chunkSize)
        {
            var toBlock = Math.Min(block + chunkSize - 1, responseObject.BlockCount - 1);

            try
            { 
                var sdpAddress = await SignalR.GetOfferFromServer(responseObject.NodeAddress);

                string response = await WebRTC.Manager.InitOpenAndSendAsync(responseObject.NodeAddress, sdpAddress, "GetBlocks", $"{block}-{toBlock}");
                if (!string.IsNullOrEmpty(response))
                {
                    var chainInfo = JsonSerializer.Deserialize<ChainInfo>(response);
                    if (chainInfo != null && Startup.Blockchain != null)
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"GetBlocks {block}-{toBlock} from {responseObject.NodeAddress} Success");

                        if (!string.IsNullOrEmpty(chainInfo.Message))
                        {
                            if (!chainInfo.Message.StartsWith("Error"))
                                try
                                {
                                    var blocksString = JsonSerializer.Deserialize<List<string>>(chainInfo.Message);

                                    if (blocksString != null)
                                        foreach (var newBlockString in blocksString)
                                        {
                                            var newBlock = Block.FromBase64(newBlockString);
                                            if (newBlock != null && newBlock.Nonce == -1)
                                                Startup.Blockchain.Clear();

                                            Startup.Blockchain.AddBlock(newBlock, false);
                                        }
                                }
                                catch (JsonException jsonEx)
                                {
                                    Logger.LogException(jsonEx,
                                        $"JSON deserialization failed for blocks in range {block}-{toBlock} from {responseObject.NodeAddress}");
                                }
                            else
                                Logger.LogError(chainInfo.NodeAddress + ": " + chainInfo.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to GetBlocks {block}-{toBlock} from {responseObject.NodeAddress}");
            }
        }
    }

    /// <summary>
    ///     Sends a vote request to a target validator for a specific block.
    /// </summary>
    /// <param name="targetValidator">The address of the target validator.</param>
    /// <param name="block">The block to be validated.</param>
    /// <returns>
    ///     A tuple where the first value indicates if the request was successful, and the second contains the response
    ///     message.
    /// </returns>
    internal static async Task<(bool, string)> SendVoteRequestAsync(string targetValidator, Block? block)
    {
        try
        {
            if (block != null)
            {
                var verifiedBlock = Block.FromBase64(block.Base64Encoded);
                if (verifiedBlock != null)
                {
                    var hash = block.Hash;
                    var calculatedHash = block.CalculateHash();
                    if (calculatedHash == hash)
                    {
                        Logger.Log($"Sending vote request to: {targetValidator}");

                        // Get the remote SDP from your table/dictionary.
                        var remoteSdp = Node.CurrentNodes_SDP[targetValidator];

                        // Call the manager method with all four parameters.
                        string response = await WebRTC.Manager.InitOpenAndSendAsync(targetValidator, remoteSdp, "Vote", block.Base64Encoded);

                        if (Config.Default.Debug)
                            Logger.Log($"Response from {targetValidator}: {response}");
                        return (response.Contains("ok"), response);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "sending vote request");
        }

        Logger.LogError($"block.Verify failed from {targetValidator}");
        return (false, "");
    }

    internal static async Task<bool> SendCodeForVerificationAsync(string nodeAddress, SmartContract? contract)
    {
        try
        {
            if (contract != null)
            {
                // Get the remote SDP from your table (adjust as needed).
                var remoteSdp = Node.CurrentNodes_SDP[nodeAddress];

                // Now call the manager method with all 4 parameters.
                string response = await WebRTC.Manager.InitOpenAndSendAsync(
                    nodeAddress,
                    remoteSdp,
                    "VerifyCode",
                    contract.SerializedContractCode
                );

                if (Config.Default.Debug)
                {
                    Logger.Log($"Code {contract.Name} sent to {nodeAddress} for verification.");
                    Logger.Log($"Response from server for code {contract.Name}: {response}");
                }

                return response == "ok";
            }

            Logger.Log($"Sending code to {nodeAddress} failed: contract is empty");
        }
        catch (Exception ex)
        {
            if (contract != null)
                Logger.LogException(ex, $"Sending code {contract.Name} to {nodeAddress} failed");
        }

        return false;
    }


    /// <summary>
    ///     Represents the result of the node startup process, including the blockchain and the node instance.
    /// </summary>
    public class NodeStartupResult
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NodeStartupResult" /> class.
        /// </summary>
        /// <param name="blockchain">The initialized blockchain instance.</param>
        /// <param name="node">The node associated with the blockchain.</param>
        public NodeStartupResult(Blockchain? blockchain, Node node, BlockchainServer server, SignalRClient signalRClient)
        {
            Blockchain = blockchain;
            Node = node;
            Server = server;
            SignalRClient = signalRClient;
        }

        public SignalRClient SignalRClient { get; set; }

        public RedisNodeRegistry? NodeRegistry { get; set; }

        public BlockchainServer Server { get; set; }

        /// <summary>
        ///     Gets or sets the blockchain associated with the node.
        /// </summary>
        public Blockchain Blockchain { get; set; }

        /// <summary>
        ///     Gets the node instance associated with the blockchain.
        /// </summary>
        public Node Node { get; }
    } 
}