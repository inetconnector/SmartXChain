using System.Reflection.Metadata;
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
    public class ApiController : WebApiController
    { 
        /// <summary>
        /// Returns the public key of the server for secure key exchange.
        /// </summary>
        /// <returns>The public key as a Base64-encoded string.</returns>
        [Route(HttpVerbs.Get, "/GetPublicKey")]
        public async Task GetPublicKey()
        {
            try
            {
                var publicKey = SecurePeer.Bob.GetPublicKey();
                var publicKeyBase64 = Convert.ToBase64String(publicKey);

                // Send the public key as a response
                await HttpContext.SendStringAsync(publicKeyBase64, "text/plain", Encoding.UTF8);

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
                Logger.Log($"Register: {encryptedPayload}");
               
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

                    var result = HandleRegistration(message);
                      
                    await SendSecureResponse(result, aliceSharedKey);
                }
                else
                {
                    Logger.Log("Error: Register request failed. payload is null.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error: Register request failed.");
                if (bob != null)
                    await SendSecureResponse("Error:  Register request failed.", aliceSharedKey);
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
                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    if (message.Contains("."))
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
            if (Config.Default.Debug)
                Logger.Log("GetChain called.");

            var aliceSharedKey = "";

            try
            {
                var encryptedPayload = await HttpContext.GetRequestBodyAsStringAsync();
                var alicePayload = JsonSerializer.Deserialize<SecurePayload>(encryptedPayload);
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
                        {
                            await SendSecureResponse(chainData, aliceSharedKey);
                        }
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
                if (alicePayload != null)
                {
                    aliceSharedKey = alicePayload.SharedKey;
                    var bob = SecurePeer.GetBob(aliceSharedKey);

                    var message = bob.DecryptAndVerify(
                        Convert.FromBase64String(alicePayload.EncryptedMessage),
                        Convert.FromBase64String(alicePayload.IV),
                        Convert.FromBase64String(alicePayload.HMAC)
                    );

                    Block newBlock = null;

                    try
                    {
                        newBlock = Block.FromBase64(message);
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"ERROR: {e.Message}\n{e.StackTrace}");
                    }

                    if (newBlock != null && Startup.Blockchain != null &&
                        Startup.Blockchain.AddBlock(newBlock, true, false))
                        await SendSecureResponse("ok", aliceSharedKey);
                    else
                        await SendSecureResponse("Error: Block rejected.", aliceSharedKey);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "Error processing new block request."); 
                await SendSecureResponse("Error: Could not process block.", aliceSharedKey);
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
                Logger.Log("Error: SendSecureResponse failed. sender SharedKey is null.");
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
        ///     Payload structure for secure messages.
        /// </summary>
        internal class SecurePayload
        {
            public string SharedKey { get; set; }
            public string EncryptedMessage { get; set; }
            public string IV { get; set; }
            public string HMAC { get; set; }
        }
    }
}