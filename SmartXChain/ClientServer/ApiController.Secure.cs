using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using EmbedIO.WebApi; 
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.Server;

/// <summary>
///     WebApiController for REST Services, integrated with SecurePeer for encryption and signing.
/// </summary>
public partial class BlockchainServer
{
    /// <summary>
    ///     for secured API calls with http
    /// </summary>
    public partial class ApiController : WebApiController
    {
        /// <summary>
        ///     Sends a secure encrypted response back to the client.
        /// </summary>
        private async Task SendSecureResponse(string message, string aliceSharedKey)
        {
            if (!string.IsNullOrEmpty(aliceSharedKey))
            {
                var (encryptedMessage, iv, hmac) = SecurePeer.GetBob(aliceSharedKey)
                    .EncryptAndSign(message);

                var securePayload = new SecurePayload
                {
                    SharedKey = Convert.ToBase64String(SecurePeer.Bob.GetPublicKey()),
                    EncryptedMessage = Convert.ToBase64String(encryptedMessage),
                    IV = Convert.ToBase64String(iv),
                    HMAC = Convert.ToBase64String(hmac)
                };

                var response = JsonSerializer.Serialize(securePayload);
                await HttpContext.SendStringAsync(response, "application/json", Encoding.UTF8);
            }
            else
            {
                Logger.LogError("SendSecureResponse failed. sender SharedKey is null.");
            }
        }

        /// <summary>
        ///     Handles the registration logic by validating the format, address, and signature of the node.
        /// </summary>
        /// <param name="message">The registration message containing node address and signature.</param>
        /// <returns>"ok" if successful, or an error message if registration fails.</returns>
        private string HandleRegistration(string message)
        {
            var parts = message.Split(new[] { ':' }, 2);
            if (parts.Length != 2 || parts[0] != "Register") return "Invalid registration format";

            var remainingParts = parts[1];
            var addressSignatureParts = remainingParts.Split('|');
            if (addressSignatureParts.Length != 2) return "Invalid registration format";

            var nodeAddress = addressSignatureParts[0];
            var signature = addressSignatureParts[1];

            if (!ValidateSignature(nodeAddress, signature))
            {
                Logger.Log($"ValidateSignature failed. Node not registered: {nodeAddress} Signature: {signature}");
                return "";
            }

            Node.AddNodeIP(nodeAddress);
            Logger.Log($"Node registered: {nodeAddress}");

            return "ok";
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
        ///     Processes a vote message and validates the block data.
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
        ///     Removes inactive nodes that have exceeded the heartbeat timeout from the registry.
        /// </summary>
        private static void RemoveInactiveNodes()
        {
            var now = DateTime.UtcNow;

            var inactiveNodes = Node.CurrentNodeIP_LastActive
                .Where(kvp => (now - kvp.Value).TotalSeconds > HeartbeatTimeoutSeconds)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var node in inactiveNodes)
            {
                Node.RemoveNodeIP(node);
                Logger.Log($"Node removed: {node} (Inactive)");
            }
        }


        /// <summary>
        ///     Payload structure for secure messages.
        /// </summary>
        internal class SecurePayload
        {
            public string SharedKey { get; set; }
            public string EncryptedMessage { get; set; }
            public string IV { get; set; }
            public string HMAC { get; set; }
        }

        #region SECURED API CALLS

        /// <summary>
        ///     Returns the public key of the server for secure key exchange.
        /// </summary>
        /// <returns>The public key as a Base64-encoded string.</returns>
        [Route(HttpVerbs.Get, "/GetPublicKey")]
        public async Task GetPublicKey()
        {
            try
            {
                var publicKey = SecurePeer.Bob.GetPublicKey();
                var publicKeyBase64 = Convert.ToBase64String(publicKey);

                var responseObject = new
                {
                    PublicKey = publicKeyBase64,
                    DllFingerprint = Crypt.GenerateFileFingerprint(Assembly.GetExecutingAssembly().Location),
                    ChainID = Config.Default.ChainId
                };
                 
                var responseJson = System.Text.Json.JsonSerializer.Serialize(responseObject);
                var responseBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(responseJson));
                  
                // Send the public key as a response
                await HttpContext.SendStringAsync(responseBase64, "text/plain", Encoding.UTF8);

                if (Config.Default.Debug)
                    Logger.Log($"Public key served: {publicKeyBase64}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error serving public key.");
                HttpContext.Response.StatusCode = 500;
                await HttpContext.SendStringAsync("Error: Unable to serve public key.", "text/plain", Encoding.UTF8);
            }
        }
        /// <summary>
        ///     Handles the registration of a node in the blockchain network securely.
        ///     Validates the node's address and signature and adds it to the registered nodes.
        /// </summary>
        [Route(HttpVerbs.Post, "/Register")]
        public async Task Register()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();

                if (Config.Default.Debug)
                    Logger.Log($"Register: {encryptedPayload}");

                // Deserialize the payload and decrypt
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                var peerUrl = HttpContext.Request.RemoteEndPoint.ToString();

                lock (PublicKeyCache)
                {
                    PublicKeyCache.Clear();
                }
                 
                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    var result = HandleRegistration(message);

                    await SendSecureResponse(result, aliceSharedKey);
                }
                else
                {
                    Logger.LogError("Register request failed. payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: Register request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: Register request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Handles the reboot of a node in the blockchain network securely. 
        /// </summary>
        [Route(HttpVerbs.Post, "/RebootChain")]
        public async Task RebootChain()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();

                if (Config.Default.Debug)
                    Logger.Log($"RebootChain: {encryptedPayload}");

                // Deserialize the payload and decrypt
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);
                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );
 
                    if (Config.Default.ChainId == "{3683DDE3-C2D3-4565-8E1C-50C8E0E2AAC2}")
                    {
                        Logger.Log($"Reboot not initiated.");
                        await SendSecureResponse("", aliceSharedKey); 
                    }
                    else
                    {
                        Logger.Log($"Reboot initiated for {Config.Default.ChainId}");
                        await SendSecureResponse("ok", aliceSharedKey);
                        Functions.RestartApplication();
                    } 
                }
                else
                {
                    Logger.LogError("RebootChain request failed. payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: Register request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: Register request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Handles requests for the list of nodes in the network securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/Nodes")]
        public async Task Nodes()
        {
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (Config.Default.Debug)
                    Logger.Log($"Nodes: {encryptedPayload}");

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (message.Contains(".")) //has IPs
                    {
                        var result = HandleNodes(message);
                        await SendSecureResponse(result, aliceSharedKey);

                        if (Config.Default.Debug && result.Length > 0)
                            Logger.Log($"Nodes: {result}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error processing nodes request.");
                await SendSecureResponse("Error: Nodes request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Handles heartbeat pings from nodes in the network securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/Heartbeat")]
        public async Task Heartbeat()
        {
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (Config.Default.Debug)
                    Logger.Log($"Heartbeat: {encryptedPayload}");

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);


                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    HandleHeartbeat(message);
                }

                await SendSecureResponse("ok", aliceSharedKey);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error processing heartbeat request.");
                await SendSecureResponse("Error: Heartbeat failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Processes a vote message, validating its block and returning a response if successful.
        /// </summary>
        [Route(HttpVerbs.Post, "/Vote")]
        public async Task Vote()
        {
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (Config.Default.Debug)
                    Logger.Log($"Vote: {encryptedPayload}");

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    var result = HandleVote(message);
                    await SendSecureResponse(result, aliceSharedKey);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error processing vote request.");
                await SendSecureResponse("Error: Vote failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Retrieves the entire blockchain in Base64 format.
        /// </summary>
        [Route(HttpVerbs.Post, "/GetChain")]
        public async Task GetChain()
        { 
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (Config.Default.Debug)
                    Logger.Log($"GetChain: {encryptedPayload}");

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (Startup != null && Startup.Blockchain != null)
                    {
                        var chainData = Startup.Blockchain.ToBase64();

                        if (message != null) 
                            await SendSecureResponse(chainData, aliceSharedKey);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error retrieving blockchain.");
                await SendSecureResponse("Error: Could not retrieve blockchain.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Processes a new block received by the server and adds it to the blockchain if valid.
        /// </summary>
        [Route(HttpVerbs.Post, "/NewBlock")]
        public async Task NewBlock()
        {
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (Config.Default.Debug)
                    Logger.Log($"NewBlock: {encryptedPayload}");

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    Block? newBlock = null;

                    try
                    {
                        newBlock = Block.FromBase64(message);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex, $"ERROR: {ex.Message}\n{ex.StackTrace}");
                    }

                    if (newBlock != null && Startup.Blockchain != null &&
                        Startup.Blockchain.AddBlock(newBlock, true, false))
                    { 
                        await SendSecureResponse("ok", aliceSharedKey);
                    }
                    else
                    { 
                        await SendSecureResponse("Error: Block rejected.", aliceSharedKey);
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error processing new block request.");
                await SendSecureResponse("Error: Could not process block.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Pushes a new blockchain to the server securely.
        ///     Replaces the existing blockchain if the incoming chain is valid and longer.
        /// </summary>
        [Route(HttpVerbs.Post, "/PushChain")]
        public async Task PushChain()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                
                if (Config.Default.Debug)
                    Logger.Log($"PushChain: {encryptedPayload}");

                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    var serializedChain = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (Config.Default.Debug)
                        Logger.Log($"Decrypted PushChain message: {serializedChain}");

                    var incomingChain = Blockchain.FromBase64(serializedChain);

                    if (Startup.Blockchain != null && Startup.Blockchain.SmartContracts.Count == 0)
                    {
                        if (Startup.Blockchain.Chain != null)
                            lock (Startup.Blockchain.Chain)
                            {
                                if (incomingChain != null &&
                                    incomingChain.Chain != null &&
                                    incomingChain.Chain.Count > Startup.Blockchain.Chain.Count &&
                                    incomingChain.IsValid())
                                {
                                    Startup.Blockchain = incomingChain;
                                    Node.SaveBlockChain(incomingChain, Startup.Node);
                                }
                            }

                        await SendSecureResponse("ok", aliceSharedKey);
                        return;
                    }
                }
                else
                {
                    Logger.LogError("PushChain request failed. Payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: PushChain request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: PushChain request failed.", aliceSharedKey);
            }

            if (bob != null)
                await SendSecureResponse("", aliceSharedKey);
        }

        /// <summary>
        ///     Retrieves a block's details securely using an HTTP GET request by block number.
        ///     The block's details are encrypted before sending.
        /// </summary>
        /// <param name="blockString">The block number as a string.</param>
        [Route(HttpVerbs.Get, "/GetBlock/{block}")]
        public async Task GetBlock(string blockString)
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                // Validate block number input
                if (!int.TryParse(blockString, out var block))
                {
                    HttpContext.Response.StatusCode = 400;
                    await SendSecureResponse($"ERROR: Invalid block number: {blockString}", aliceSharedKey);
                    return;
                }

                // Validate block existence
                if (Startup.Blockchain == null || block < 0 || 
                    (Startup.Blockchain.Chain!=null && block >= Startup.Blockchain.Chain.Count))
                {
                    HttpContext.Response.StatusCode = 404;
                    await SendSecureResponse($"ERROR: Block {block} not found.", aliceSharedKey);
                    return;
                }

                // Retrieve the block
                if (Startup.Blockchain.Chain != null)
                {
                    var blockData = Startup.Blockchain.Chain[block];

                    if (Config.Default.Debug)
                        Logger.Log($"Sent block {block}");

                    // Encrypt the response
                    var response = blockData.ToString();
                    if (!string.IsNullOrEmpty(HttpContext.Request.Headers["SharedKey"]))
                    {
                        aliceSharedKey = HttpContext.Request.Headers["SharedKey"];
                        if (aliceSharedKey != null) bob = SecurePeer.GetBob(aliceSharedKey);
                    }

                    if (bob != null && !string.IsNullOrEmpty(aliceSharedKey))
                    {
                        await SendSecureResponse(response, aliceSharedKey);
                    }
                    else
                    {
                        // If encryption isn't available, fall back to plaintext (not recommended)
                        Logger.Log("Warning: SharedKey not provided. Sending response in plaintext.");
                        await HttpContext.SendStringAsync(response, "application/json", Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: GetBlock request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: GetBlock request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Provides the current block count in the blockchain securely.
        ///     Decrypts the incoming message, processes it, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/BlockCount")]
        public async Task BlockCount()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                // Get and log the encrypted payload
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
               
                if (Config.Default.Debug)
                    Logger.Log($"BlockCount: {encryptedPayload}");

                // Deserialize and decrypt the payload
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (Config.Default.Debug)
                        Logger.Log($"Decrypted BlockCount message: {message}");

                    // Update block count if necessary
                    if (Startup.Blockchain != null &&
                        Startup.Blockchain.Chain != null &&
                        _blockCount != Startup.Blockchain.Chain.Count)
                    {
                        _blockCount = Startup.Blockchain.Chain.Count;

                        if (Config.Default.Debug)
                            Logger.Log($"Updated BlockCount: {Startup.Blockchain.Chain.Count}");
                    }

                    // Process the decrypted message
                    if (message.Contains(':'))
                    {
                        var sp = message.Split(':');
                        if (sp.Length == 5)
                        {
                            var remoteBlockCount = Convert.ToInt64(sp[^1]);
                            var remoteServer = $"{sp[1]}:{sp[2]}:{sp[3]}";
                            if (remoteBlockCount < _blockCount && NetworkUtils.IsValidServer(remoteServer))
                                Node.AddNodeIP(remoteServer);
                        }
                    }

                    // Send the secure response
                    await SendSecureResponse(_blockCount.ToString(), aliceSharedKey);
                }
                else
                {
                    Logger.LogError("BlockCount request failed. Payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: BlockCount request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: BlockCount request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Verifies code integrity or specific blockchain-related operations securely.
        ///     Decrypts the incoming message, processes verification, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/VerifyCode")]
        public async Task VerifyCode()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                
                if (Config.Default.Debug)
                    Logger.Log($"VerifyCode: {encryptedPayload}");

                // Deserialize and decrypt the payload
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (Config.Default.Debug)
                        Logger.Log($"Decrypted VerifyCode message: {message}");

                    // Perform code verification logic
                    var result = HandleVerifyCode(message);
                    
                    if (Config.Default.Debug)
                        Logger.Log($"VerifyCode Result: {result}");

                    // Send the secure response
                    await SendSecureResponse(result, aliceSharedKey);
                }
                else
                {
                    Logger.LogError("VerifyCode request failed. Payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: VerifyCode request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: VerifyCode request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Handles the addition of new servers to the network securely.
        ///     Decrypts the incoming message, processes the server addition, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/PushServers")]
        public async Task PushServers()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                // Read the encrypted payload from the request
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();

                if (Config.Default.Debug)
                    Logger.Log($"PushServers: {encryptedPayload}");

                // Deserialize and decrypt the payload
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (alicePayload != null)
                {
                    // Initialize the shared key and SecurePeer (Bob)
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    // Decrypt and verify the encrypted payload
                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (Config.Default.Debug)
                        Logger.Log($"Decrypted PushServers message: {message}");

                    // Process the decrypted message
                    var serverAdded = false;
                    foreach (var server in message.Split(','))
                        if (server.StartsWith("http://") && !Node.CurrentNodeIPs.Contains(server))
                        {
                            Node.AddNodeIP(server);
                            serverAdded = true;
                        }

                    // Create the encrypted response
                    var response = serverAdded ? "ok" : "";
                    await SendSecureResponse(response, aliceSharedKey);
                }
                else
                {
                    Logger.LogError("PushServers request failed. Payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: PushServers request failed.");

                // Error handling: send an encrypted error message
                if (bob != null) await SendSecureResponse("Error: PushServers request failed.", aliceSharedKey);
            }
        }

        /// <summary>
        ///     Validates the integrity of the blockchain securely.
        ///     The response is encrypted and sent securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/ValidateChain")]
        public async Task ValidateChain()
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                
                if (Config.Default.Debug)
                    Logger.Log($"ValidateChain: {encryptedPayload}");

                // Deserialize and decrypt the payload
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);

                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (Config.Default.Debug)
                        Logger.Log($"Decrypted ValidateChain message: {message}");

                    // Validate the blockchain
                    var isValid = Startup.Blockchain.IsValid();
                    
                    if (Config.Default.Debug)
                        Logger.Log($"Blockchain validation result: {isValid}");

                    // Send the secure response
                    var response = isValid ? "ok" : "";
                    await SendSecureResponse(response, aliceSharedKey);
                }
                else
                {
                    Logger.LogError("ValidateChain request failed. Payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: ValidateChain request failed.");
                if (bob != null)
                    await SendSecureResponse("Error: ValidateChain request failed.", aliceSharedKey);
            }
        }

        #endregion
    }
}