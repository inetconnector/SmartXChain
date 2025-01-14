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
        ///     Handles secure requests by decrypting the payload, processing the message, and sending an encrypted response.
        /// </summary>
        /// <param name="handler">The business logic handler that processes the decrypted message and returns a response.</param>
        private async Task HandleSecureRequest(Func<string, Task<string>> handler)
        {
            SecurePeer? bob = null;
            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
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

                    var response = await handler(message);
                    await SendSecureResponse(response, aliceSharedKey);
                }
                else
                {
                    Logger.LogError("Request payload is null.");
                    await SendSecureResponse("Error: Invalid request payload.", aliceSharedKey);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error handling secure request.");
                if (bob != null)
                    await SendSecureResponse("Error: Request failed.", aliceSharedKey);
            }
        }


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

                var responseJson = JsonSerializer.Serialize(responseObject);
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
            await HandleSecureRequest(message =>
            {
                lock (PublicKeyCache)
                {
                    PublicKeyCache.Clear();
                }

                return Task.FromResult(HandleRegistration(message));
            });
        }


        /// <summary>
        ///     Handles the reboot of a node in the blockchain network securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/RebootChain")]
        public async Task RebootChain()
        {
            await HandleSecureRequest(message =>
            {
                if (Config.Default.ChainId == "{3683DDE3-C2D3-4565-8E1C-50C8E0E2AAC2}")
                {
                    Logger.Log("Reboot not initiated.");
                    return Task.FromResult(""); // No action taken
                }

                Logger.Log($"Reboot initiated for {Config.Default.ChainId}");
                Functions.RestartApplication();
                return Task.FromResult("ok");
            });
        }

        /// <summary>
        ///     Handles requests for the list of nodes in the network securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/Nodes")]
        public async Task Nodes()
        {
            await HandleSecureRequest(message =>
            {
                if (message.Contains(".")) // Indicates the presence of IP addresses
                {
                    var result = HandleNodes(message);

                    if (Config.Default.Debug && result.Length > 0)
                        Logger.Log($"Nodes: {result}");

                    return Task.FromResult(result);
                }

                return Task.FromResult(""); // No valid IPs found
            });
        }

        /// <summary>
        ///     Handles heartbeat pings from nodes in the network securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/Heartbeat")]
        public async Task Heartbeat()
        {
            await HandleSecureRequest(async message =>
            {
                HandleHeartbeat(message);
                return "ok";
            });
        }

        /// <summary>
        ///     Processes a vote message, validating its block and returning a response if successful.
        /// </summary>
        [Route(HttpVerbs.Post, "/Vote")]
        public async Task Vote()
        {
            await HandleSecureRequest(message => Task.FromResult(HandleVote(message)));
        }

        /// <summary>
        ///     Retrieves the entire blockchain in Base64 format.
        /// </summary>
        [Route(HttpVerbs.Post, "/GetChain")]
        public async Task GetChain()
        {
            await HandleSecureRequest(message =>
            {
                if (Startup?.Blockchain != null)
                {
                    var chainData = Startup.Blockchain.ToBase64();
                    return Task.FromResult(chainData);
                }

                return Task.FromResult("Error: Blockchain data unavailable.");
            });
        }


        /// <summary>
        ///     Processes a new block received by the server and adds it to the blockchain if valid.
        /// </summary>
        [Route(HttpVerbs.Post, "/NewBlock")]
        public async Task NewBlock()
        {
            await HandleSecureRequest(message =>
            {
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
                    return Task.FromResult("ok");
                return Task.FromResult("Error: Block rejected.");
            });
        }

        /// <summary>
        ///     Pushes a new blockchain to the server securely.
        ///     Replaces the existing blockchain if the incoming chain is valid and longer.
        /// </summary>
        [Route(HttpVerbs.Post, "/PushChain")]
        public async Task PushChain()
        {
            await HandleSecureRequest(serializedChain =>
            {
                var incomingChain = Blockchain.FromBase64(serializedChain);

                if (Startup.Blockchain != null && Startup.Blockchain.SmartContracts.Count == 0 && 
                    Startup.Blockchain.Chain != null)
                    lock (Startup.Blockchain.Chain)
                    {
                        if (incomingChain != null &&
                            incomingChain.Chain != null &&
                            incomingChain.Chain.Count > Startup.Blockchain.Chain.Count &&
                            incomingChain.IsValid())
                        {
                            Startup.Blockchain = incomingChain;
                            Node.SaveBlockChain(incomingChain, Startup.Node);
                            return Task.FromResult("ok");
                        }
                    }

                return Task.FromResult("Error: Invalid or outdated chain.");
            });
        }

        /// <summary>
        ///     Retrieves a block's details securely using an HTTP GET request by block number.
        ///     The block's details are encrypted before sending.
        /// </summary>
        /// <param name="blockString">The block number as a string.</param>
        [Route(HttpVerbs.Get, "/GetBlock/{block}")]
        public async Task GetBlock(string blockString)
        {
            await HandleSecureRequest(_ =>
            {
                if (!int.TryParse(blockString, out var block))
                {
                    HttpContext.Response.StatusCode = 400;
                    return Task.FromResult($"ERROR: Invalid block number: {blockString}");
                }

                if (Startup.Blockchain == null || block < 0 ||
                    (Startup.Blockchain.Chain != null && block >= Startup.Blockchain.Chain.Count))
                {
                    HttpContext.Response.StatusCode = 404;
                    return Task.FromResult($"ERROR: Block {block} not found.");
                }

                var blockData = Startup.Blockchain.Chain?[block];
                if (Config.Default.Debug) Logger.Log($"Sent block {block}");
                return Task.FromResult(blockData?.ToString() ?? "ERROR: Block data unavailable.");
            });
        }

        /// <summary>
        ///     Provides the current block count in the blockchain securely.
        ///     Decrypts the incoming message, processes it, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/BlockCount")]
        public async Task BlockCount()
        {
            await HandleSecureRequest(message =>
            {
                if (Config.Default.Debug) Logger.Log($"Decrypted BlockCount message: {message}");

                // Update block count if necessary
                if (Startup.Blockchain?.Chain != null && _blockCount != Startup.Blockchain.Chain.Count)
                {
                    _blockCount = Startup.Blockchain.Chain.Count;
                    if (Config.Default.Debug) Logger.Log($"Updated BlockCount: {_blockCount}");
                }

                // Process message for node synchronization
                if (message.Contains(':'))
                {
                    var parts = message.Split(':');
                    if (parts.Length == 5)
                    {
                        var remoteBlockCount = Convert.ToInt64(parts[^1]);
                        var remoteServer = $"{parts[1]}:{parts[2]}:{parts[3]}";
                        if (remoteBlockCount < _blockCount && NetworkUtils.IsValidServer(remoteServer))
                            Node.AddNodeIP(remoteServer);
                    }
                }

                return Task.FromResult(_blockCount.ToString());
            });
        }

        /// <summary>
        ///     Verifies code integrity or specific blockchain-related operations securely.
        ///     Decrypts the incoming message, processes verification, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/VerifyCode")]
        public async Task VerifyCode()
        {
            await HandleSecureRequest(message =>
            {
                if (Config.Default.Debug) Logger.Log($"Decrypted VerifyCode message: {message}");

                var result = HandleVerifyCode(message);
                if (Config.Default.Debug) Logger.Log($"VerifyCode Result: {result}");
                return Task.FromResult(result);
            });
        }

        /// <summary>
        ///     Handles the addition of new servers to the network securely.
        ///     Decrypts the incoming message, processes the server addition, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/PushServers")]
        public async Task PushServers()
        {
            await HandleSecureRequest(message =>
            {
                if (Config.Default.Debug) Logger.Log($"Decrypted PushServers message: {message}");

                var serverAdded = false;
                foreach (var server in message.Split(','))
                    if (server.StartsWith("http://") && !Node.CurrentNodeIPs.Contains(server))
                    {
                        Node.AddNodeIP(server);
                        serverAdded = true;
                    }

                return Task.FromResult(serverAdded ? "ok" : "");
            });
        }

        /// <summary>
        ///     Validates the integrity of the blockchain securely.
        ///     The response is encrypted and sent securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/ValidateChain")]
        public async Task ValidateChain()
        {
            await HandleSecureRequest(_ =>
            {
                var isValid = Startup.Blockchain != null && Startup.Blockchain.IsValid();
                if (Config.Default.Debug) Logger.Log($"Blockchain validation result: {isValid}");
                return Task.FromResult(isValid ? "ok" : "error");
            });
        }

        #endregion
    }
}