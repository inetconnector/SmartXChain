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
                    LogError("Request payload is null.");

                    await SendSecureResponse("Error: Invalid request payload.", aliceSharedKey);
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "handling secure request failed");


                if (bob != null)
                    await SendSecureResponse("Error: Request failed", aliceSharedKey);
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
                LogError("SendSecureResponse failed. sender SharedKey is null.");
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
                Log($"ValidateSignature failed. Node not registered: {nodeAddress} Signature: {signature}");
                return "";
            }

            Node.AddNodeIP(nodeAddress);
            Log($"Node registered: {nodeAddress}");

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

        private static void Log(string message)
        {
            Logger.Log("API: " + message);
        }

        private static void LogError(string message)
        {
            Logger.LogError("API: " + message);
        }

        private static void LogException(Exception exception, string message)
        {
            Logger.LogException(exception, "API: " + message);
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
                Log("Invalid Vote message received.");
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
                Log($"Invalid Vote message received. {e.Message}");
            }

            return "";
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
                    Log($"Public key served: {publicKeyBase64}");
            }
            catch (Exception ex)
            {
                LogException(ex, "Error serving public key.");
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
                    Log("Reboot not initiated.");
                    return Task.FromResult(""); // No action taken
                }

                Log($"Reboot initiated for {Config.Default.ChainId}");
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
                var now = DateTime.UtcNow;

                var inactiveNodes = Node.CurrentNodeIP_LastActive
                    .Where(kvp => (now - kvp.Value).TotalSeconds > NodeTimeoutSeconds)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var node in inactiveNodes)
                {
                    Node.RemoveNodeIP(node);
                    Log($"Node removed: {node} (Inactive)");
                }

                var nodesString = "";

                if (Node.CurrentNodeIPs.Count > 0)
                {
                    var nodes = string.Join(",", Node.CurrentNodeIPs.Where(node => !string.IsNullOrWhiteSpace(node)));
                    nodesString = nodes.TrimEnd(',');
                }

                var responseInfo = CreateChainInfo(nodesString);
                var result = JsonSerializer.Serialize(responseInfo);

                if (Config.Default.Debug && result.Length > 0)
                    Log($"Nodes: {result}");

                return Task.FromResult(result);
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
        ///     Processes a new block or a list of blocks received by the server and adds them to the blockchain if valid.
        /// </summary>
        [Route(HttpVerbs.Post, "/NewBlocks")]
        public async Task NewBlocks()
        {
            await HandleSecureRequest(async message =>
            {
                try
                {
                    var responseInfo = CreateChainInfo("Error: Block(s) rejected");

                    if (Startup.Blockchain != null && Startup.Blockchain.Chain != null)
                    {
                        var allBlocksAdded = true;
                        lock (Startup.Blockchain.Chain)
                        {
                            var chainInfo = JsonSerializer.Deserialize<ChainInfo>(message);

                            if (chainInfo != null)
                            {
                                var blocks = Blockchain.DecodeBlocksFromBase64(chainInfo.Message);
                                foreach (var block in blocks)
                                {
                                    if (block.Nonce == -1)
                                        if (Startup.Blockchain.Chain != null)
                                            Startup.Blockchain.Clear();

                                    if (!Startup.Blockchain.AddBlock(block, false, false))
                                    {
                                        allBlocksAdded = false;
                                        break;
                                    }
                                }
                            }
                        }

                        responseInfo.Message = allBlocksAdded ? "ok" : "Error: One or more blocks rejected";
                    }

                    return await Task.FromResult(JsonSerializer.Serialize(responseInfo));
                }
                catch (Exception ex)
                {
                    LogException(ex, $"ERROR: {ex.Message}\n{ex.StackTrace}");
                    return await Task.FromResult("Error: Unexpected server error");
                }
            });
        }


        /// <summary>
        ///     Provides the current block count in the blockchain securely.
        ///     Decrypts the incoming message, processes it, and responds securely.
        /// </summary>
        [Route(HttpVerbs.Post, "/ChainInfo")]
        public async Task ChainInfo()
        {
            await HandleSecureRequest(message =>
            {
                if (Config.Default.Debug) Log($"Decrypted BlockCount message: {message}");

                if (Startup.Blockchain?.Chain != null)
                {
                    if (!string.IsNullOrEmpty(message))
                    {
                        ChainInfo chainInfo = null;
                        try
                        {
                            chainInfo = JsonSerializer.Deserialize<ChainInfo>(message.Substring("ChainInfo:".Length));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, "ChainInfo deserialize failed: Invalid response structure");
                        }

                        if (chainInfo != null)
                            Node.AddNodeIP(chainInfo.URL);
                    }

                    return Task.FromResult(JsonSerializer.Serialize(CreateChainInfo()));
                }

                return Task.FromResult("");
                ;
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
                if (Config.Default.Debug) Log($"Sent block {block}");
                return Task.FromResult(blockData?.ToString() ?? "ERROR: Block data unavailable.");
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
                if (Config.Default.Debug) Log($"Decrypted VerifyCode message: {message}");

                var result = HandleVerifyCode(message);
                if (Config.Default.Debug) Log($"VerifyCode Result: {result}");
                return Task.FromResult(result);
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
                if (Config.Default.Debug) Log($"Blockchain validation result: {isValid}");
                return Task.FromResult(isValid ? "ok" : "error");
            });
        }

        #endregion
    }
}