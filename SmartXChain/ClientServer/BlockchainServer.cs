using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json; 
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
    /// <param name="peer">The NodeAddress of the peer.</param>
    /// <returns>The public key of the peer as a byte array, or null if retrieval fails.</returns>
    private static readonly ConcurrentDictionary<string, byte[]> PublicKeyCache = new();

    /// <summary>
    ///     Represents the startup state of the blockchain node.
    /// </summary>
    internal static NodeStartupResult Startup { get; private set; }

    /// <summary>
    ///     Starts the blockchain server by initializing key tasks such as peer discovery, server startup,
    ///     and peer synchronization.
    /// </summary>
    public void Start()
    {
        // 1. Discover and register with peer servers
        Task.Run(() => DiscoverAndRegisterWithPeers());

        // 2. Start the main server to listen for incoming messages
        Task.Run(() => StartServerAsync());

        // 3. Background task to synchronize with peer servers
        Task.Run(() => SynchronizeWithPeers());
    }

    /// <summary>
    ///     Starts a new node on the blockchain and initializes the blockchain for that node.
    /// </summary>
    /// <param name="walletAddress">The wallet address to associate with the node.</param>
    /// <returns>A Task that resolves to a <see cref="NodeStartupResult" /> containing the blockchain and node information.</returns>
    public static async Task<NodeStartupResult> StartNode(string walletAddress)
    {
        // Start the node and initialize its configuration
        var node = await Node.Start();

        // Create a new blockchain with the provided wallet address
        var blockchain = new Blockchain(walletAddress, node.ChainId);

        // Set up the result containing the blockchain and node
        Startup = new NodeStartupResult(blockchain, node);
        return Startup;
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
                server = new BlockchainServer();
                var signalHubs = Config.Default.SignalHubs;
                foreach (var signalHub in signalHubs)
                {
                    using var peerConnection = CreatePeerConnection(signalHub, Startup.Blockchain);
                }
                 
                Logger.Log(
                    $"Server node for blockchain '{Config.Default.ChainId}' started at {Config.Default.NodeAddress}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "starting server");
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
                Logger.LogException(ex, $"loading existing chain from {chainPath}");
                Logger.LogError($"{ex.Message}");
            }
        }

        return (server, result);
    }

    private static async Task CreatePeerConnection(string signalHub, Blockchain blockchain) 
    {
        // Load your secret for JWT
        var signalRSigningKey = Environment.GetEnvironmentVariable("SIGNALR_PASSWORD");
        var name = "smartXchain";

        // Instantiate our SignalR client
        var signalRClient = new SignalRClient();

        // Instantiate our SIPSorcery-based WebRTC manager
        var webRtcManager = new WebRtcManager();

        // Generate a JWT token for the SignalR server
        var jwtToken = GenerateJwtToken(name, signalRSigningKey);

        // Connect the SignalR client to the server
        await signalRClient.ConnectAsync(signalHub, jwtToken);

        // Initialize the SIPSorcery WebRTC peer
        await webRtcManager.InitializeAsync(blockchain);

        WebRtc = webRtcManager;
        SignalR = signalRClient;
    }

    public static WebRtcManager WebRtc { get; set; } = null;
    public static SignalRClient SignalR { get; set; } = null;

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

    /// <summary>
    ///     Discovers peers from the configuration and registers them in the peer server list,
    ///     excluding the current server addresses.
    /// </summary>
    private void DiscoverAndRegisterWithPeers()
    {
        var validPeers = new ConcurrentList<string>();

        try
        {
            foreach (var peer in Config.Default.SignalHubs)
                if (!string.IsNullOrEmpty(peer) && peer.StartsWith("http"))
                    Node.AddNodeIP(peer);

            Logger.Log($"Static peers discovered: {string.Join(", ", validPeers.Count)}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "processing static peers");
        }
    }

    /// <summary>
    ///     Refactored SynchronizeWithPeers to utilize SecureCommunication and process responses.
    /// </summary>
    private async Task SynchronizeWithPeers()
    {
        while (true)
        {
            await Sync();
            await Task.Delay(30000);
        }
    }

    /// <summary>
    ///     Synchronize with peers, get missing blocks from peers
    /// </summary>
    public static async Task Sync()
    {
        foreach (var peer in Node.CurrentNodes)
        {
            if (peer == Config.Default.NodeAddress)
                continue;

            var (success, response) = await SendSecureMessage(peer, "Nodes", Config.Default.NodeAddress);

            if (success && !string.IsNullOrEmpty(response))
                try
                {
                    var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
                    if (responseObject != null)
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"SynchronizeWithPeers  {peer} Result: {responseObject.Message}");

                        foreach (var node in responseObject.Message.Split(','))
                            Node.AddNodeIP(node);

                        foreach (var chain in Blockchain.Blockchains)
                            if (chain.Chain != null)
                            {
                                if (responseObject.FirstHash == chain.Chain.First().Hash &&
                                    Startup.Blockchain != null && Startup.Blockchain.IsValid())
                                {
                                    if (responseObject.BlockCount > chain.Chain.Count)
                                        await GetRemoteChain(responseObject, chain, chain.Chain.Count);
                                }
                                else
                                {
                                    await GetRemoteChain(responseObject, chain, 0);
                                }
                            }
                    }
                    else
                    {
                        Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"Failed to parse or update nodes from peer {peer}");
                }
            else
                Logger.LogError($"Synchronization with peer {peer} failed or returned no response.");
        }
    }


    /// <summary>
    ///     Refactored BroadcastBlockToPeers to utilize SecureCommunication and log responses.
    /// </summary>
    public static async Task BroadcastBlockToPeers(ConcurrentList<string> serversList, List<Block> blocks,
        Blockchain blockchain)
    {
        var message = JsonSerializer.Serialize(blocks);

        var semaphore = new SemaphoreSlim(Config.Default.MaxParallelConnections);

        var tasks = serversList.Select(async peer =>
        {
            if (peer.Contains(Config.Default.NodeAddress))
                return;

            await semaphore.WaitAsync();
            try
            {
                var compressedMessage = Convert.ToBase64String(Compress.CompressString(message));
                var msg = ChainInfo.CreateChainInfo(blockchain, compressedMessage);

                var (success, response) =
                    await SendSecureMessage(peer, "NewBlocks", JsonSerializer.Serialize(msg));

                if (success)
                {
                    if (!string.IsNullOrEmpty(response))
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"Broadcast to {peer} successful. Response: {response}");

                        var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
                        if (responseObject != null)
                        {
                            if (Config.Default.Debug)
                                Logger.Log($"Broadcast to {peer} Result: {responseObject.Message}");
                        }
                        else
                        {
                            Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
                        }
                    }
                    else
                    {
                        Logger.LogError($"Broadcast to {peer} failed. Response is null.");
                    }
                }
                else
                {
                    Logger.LogError($"Broadcast to {peer} failed. SendSecureMessage failed");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static async Task GetRemoteChain(ChainInfo responseObject, Blockchain chain, int fromBlock,
        int chunkSize = 40)
    {
        if (chain.Chain == null)
            return;

        for (var block = fromBlock; block < responseObject.BlockCount; block += chunkSize)
        {
            var toBlock = Math.Min(block + chunkSize - 1, responseObject.BlockCount - 1);

            try
            {
                var (success, response) =
                    await SendSecureMessage(responseObject.URL, $"GetBlocks/{block}/{toBlock}", Config.Default.NodeAddress);

                if (success && !string.IsNullOrEmpty(response))
                {
                    var chainInfo = JsonSerializer.Deserialize<ChainInfo>(response);
                    if (chainInfo != null && Startup.Blockchain != null)
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"GetBlocks {block}-{toBlock} from {responseObject.URL} Success");

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
                                        $"JSON deserialization failed for blocks in range {block}-{toBlock} from {responseObject.URL}");
                                }
                            else
                                Logger.LogError(chainInfo.URL + ": " + chainInfo.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to GetBlocks {block}-{toBlock} from {responseObject.URL}");
            }
        }
    }


    /// <summary>
    ///     Retrieves the list of registered nodes from a specific server.
    /// </summary>
    /// <param name="nodeAddress">The address of the server to query for registered nodes.</param>
    /// <returns>A list of node addresses retrieved from the server.</returns>
    public static async Task<List<string>> GetRegisteredNodesAsync(string nodeAddress)
    {
        var ret = new List<string>();
        if (nodeAddress.Contains(Config.Default.NodeAddress))
            return ret;

        try
        { 
            var response = await WebRtc.SendRequestAsync(nodeAddress, "Nodes"); 

            if (response.ToLower().Contains("error"))
            {
                Logger.LogError($"Timeout from server {nodeAddress}: {response}");
                Node.RemoveNodeAddress(nodeAddress);
                return ret;
            }

            var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
            if (responseObject == null)
            {
                Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
            }
            else
            {
                if (string.IsNullOrEmpty(responseObject.Message))
                {
                    if (Config.Default.Debug)
                        Logger.Log($"No new nodes received from {nodeAddress}");
                    return ret;
                }

                foreach (var address in responseObject.Message.Split(','))
                    if (!string.IsNullOrEmpty(address))
                        ret.Add(address);


                if (Config.Default.Debug)
                    Logger.Log($"Active nodes from server {nodeAddress}: {responseObject.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex,
                $"GetRegisteredNodesAsync: retrieving registered nodes from {nodeAddress} failed");
        }

        return ret;
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
                        var message = $"Vote:{block.Base64Encoded}";
                        Logger.Log($"Sending vote request to: {targetValidator}");
                         
                        var response = await WebRtc.SendRequestAsync(targetValidator, $"Vote:{block.Base64Encoded}");

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


    /// <summary>
    ///     Sends the serialized code of a smart contract to a server for verification.
    /// </summary>
    /// <param name="nodeAddress">The address of the server for verification.</param>
    /// <param name="contract">The smart contract to be verified.</param>
    /// <returns>A boolean indicating whether the code verification was successful.</returns>
    internal static async Task<bool> SendCodeForVerificationAsync(string nodeAddress, SmartContract? contract)
    {
        try
        {
            if (contract != null)
            {
                var message = $"VerifyCode:{contract.SerializedContractCode}";
                var response = await WebRtc.SendRequestAsync(nodeAddress, "Nodes");
                 
                if (Config.Default.Debug)
                {
                    Logger.Log($"Code {contract.Name} sent to {nodeAddress} for verification.");
                    Logger.Log($"Response from server for code {contract.Name}: {response}", false);
                }

                return response == "ok";
            }

            Logger.LogError($"sending code to {nodeAddress} failed: contract is empty");
        }
        catch (Exception ex)
        {
            if (contract != null)
                Logger.LogException(ex, $"sending code {contract.Name} to {nodeAddress} failed");
        }

        return false;
    }

    internal static async Task<(bool success, string? response)> SendSecureMessage(string peer,
        string command, string message)
    {
        throw new NotImplementedException();
    }

    internal static async Task<(bool success, string? response)> SendSecureMessage(ConcurrentList<string> peers,
        string command, string message)
    {
        throw new NotImplementedException();
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
        public NodeStartupResult(Blockchain? blockchain, Node node)
        {
            Blockchain = blockchain;
            Node = node;
        }

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