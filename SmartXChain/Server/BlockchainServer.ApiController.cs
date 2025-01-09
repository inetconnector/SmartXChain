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

public partial class BlockchainServer
{
    /// <summary>
    ///     WebApiController for REST Services
    /// </summary>
    public class ApiController : WebApiController
    {
        private int _blockCount;

        /// <summary>
        ///     Handles the registration of a node in the blockchain network.
        ///     Validates the node's address and signature and adds it to the registered nodes.
        /// </summary>
        [Route(HttpVerbs.Post, "/Register")]
        public async Task Register()
        {
            var message = await HttpContext.GetRequestBodyAsStringAsync();
            Logger.Log($"Register: {message}");
            var result = HandleRegistration(message);
            await HttpContext.SendStringAsync(result, "text/plain", Encoding.UTF8);
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
        ///     Handles requests for the list of nodes in the network.
        /// </summary>
        [Route(HttpVerbs.Post, "/Nodes")]
        public async Task Nodes()
        {
            var message = await HttpContext.GetRequestBodyAsStringAsync();
            if (message.Contains("."))
            {
                var result = HandleNodes(message);
                await HttpContext.SendStringAsync(result, "text/plain", Encoding.UTF8);

                if (Config.Default.Debug && result.Length > 0)
                    Logger.Log($"Nodes: {result}");
            }
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
        private void RemoveInactiveNodes()
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
        ///     Handles the addition of new servers to the network.
        /// </summary>
        [Route(HttpVerbs.Post, "/PushServers")]
        public async Task PushServers()
        {
            var message = await HttpContext.GetRequestBodyAsStringAsync();
            Logger.Log($"PushServers: {message}");
            var serverAdded = false;

            foreach (var server in message.Split(','))
                if (server.StartsWith("http://") && !Node.CurrentNodeIPs.Contains(server))
                {
                    Node.AddNodeIP(server);
                    serverAdded = true;
                }

            await HttpContext.SendStringAsync(serverAdded ? "ok" : "", "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Processes a vote message, validating its block and returning a response if successful.
        /// </summary>
        [Route(HttpVerbs.Post, "/Vote")]
        public async Task Vote()
        {
            var message = await HttpContext.GetRequestBodyAsStringAsync();
            Logger.Log($"Vote: {message}");
            var result = HandleVote(message);
            Logger.Log($"Vote Result: {result}");
            await HttpContext.SendStringAsync(result, "text/plain", Encoding.UTF8);
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
        ///     Verifies code integrity or specific blockchain-related operations.
        /// </summary>
        [Route(HttpVerbs.Post, "/VerifyCode")]
        public async Task VerifyCode()
        {
            var message = await HttpContext.GetRequestBodyAsStringAsync();
            Logger.Log($"VerifyCode: {message}");
            var result = HandleVerifyCode(message);
            Logger.Log($"VerifyCode Result: {result}");
            await HttpContext.SendStringAsync(result, "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Handles heartbeat pings from nodes in the network.
        /// </summary>
        [Route(HttpVerbs.Post, "/Heartbeat")]
        public async Task Heartbeat()
        {
            var message = await HttpContext.GetRequestBodyAsStringAsync();
            if (Config.Default.Debug)
                Logger.Log($"Heartbeat: {message}");
            HandleHeartbeat(message);
            await HttpContext.SendStringAsync("ok", "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Provides the current block count in the blockchain.
        /// </summary>
        [Route(HttpVerbs.Post, "/BlockCount")]
        public async Task BlockCount()
        {
            if (Startup.Blockchain != null &&
                Startup.Blockchain.Chain != null &&
                _blockCount != Startup.Blockchain.Chain.Count)
            {
                _blockCount = Startup.Blockchain.Chain.Count;
                if (Config.Default.Debug)
                    Logger.Log($"BlockCount: {Startup.Blockchain.Chain.Count}");
            }

            var message = await HttpContext.GetRequestBodyAsStringAsync();
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

            await HttpContext.SendStringAsync(_blockCount.ToString(), "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Validates the integrity of the blockchain.
        /// </summary>
        [Route(HttpVerbs.Post, "/ValidateChain")]
        public async Task ValidateChain()
        {
            var isvalid = Startup.Blockchain.IsValid();
            if (Config.Default.Debug)
                Logger.Log($"ValidateChain: {isvalid}");
            await HttpContext.SendStringAsync(isvalid ? "ok" : "", "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves the entire blockchain in Base64 format.
        /// </summary>
        [Route(HttpVerbs.Post, "/GetChain")]
        public async Task GetChain()
        {
            if (Config.Default.Debug)
                Logger.Log("GetChain called.");
            await HttpContext.SendStringAsync(Startup.Blockchain.ToBase64(), "application/json", Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves the current block count using an HTTP GET request.
        /// </summary>
        [Route(HttpVerbs.Get, "/GetBlockCount")]
        public async Task GetBlockCount()
        {
            if (Startup.Blockchain != null &&
                Startup.Blockchain.Chain != null)
            {
                _blockCount = Startup.Blockchain.Chain.Count;
                if (Config.Default.Debug)
                    Logger.Log($"GetBlockCount: {Startup.Blockchain.Chain.Count}");
            }

            await HttpContext.SendStringAsync(_blockCount.ToString(), "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves specific block data by block number.
        /// </summary>
        /// <param name="block">The block number to retrieve.</param>
        [Route(HttpVerbs.Post, "/GetBlockData/{block}")]
        public async Task GetBlockData(string block)
        {
            if (!int.TryParse(block, out var blockInt))
            {
                HttpContext.Response.StatusCode = 400;
                await HttpContext.SendStringAsync($"ERROR: Invalid block number: {block}", "text/plain", Encoding.UTF8);
                return;
            }

            if (Startup.Blockchain != null && Startup.Blockchain.Chain != null &&
                (Startup.Blockchain == null || blockInt < 0 || blockInt >= Startup.Blockchain.Chain.Count))
            {
                HttpContext.Response.StatusCode = 404;
                await HttpContext.SendStringAsync($"ERROR: Block {block} not found.", "text/plain", Encoding.UTF8);
                return;
            }

            if (Config.Default.Debug) Logger.Log($"Sent block {block}");

            if (Startup.Blockchain != null && Startup.Blockchain.Chain != null)
            {
                var blockData = Startup.Blockchain.Chain[blockInt].ToBase64();

                await HttpContext.SendStringAsync(blockData, "application/json", Encoding.UTF8);
            }
            else
            {
                Logger.Log("ERROR: Blockchain is empty");
            }
        }

        /// <summary>
        ///     Retrieves a block's details using an HTTP GET request by block number.
        /// </summary>
        /// <param name="blockString">The block number as a string.</param>
        [Route(HttpVerbs.Get, "/GetBlock/{block}")]
        public async Task GetBlock(string blockString)
        {
            if (!int.TryParse(blockString, out var block))
            {
                HttpContext.Response.StatusCode = 400;
                await HttpContext.SendStringAsync($"ERROR: Invalid block number: {blockString}", "text/plain",
                    Encoding.UTF8);
                return;
            }

            if (Startup.Blockchain != null && (block < 0 || block >= Startup.Blockchain.Chain.Count))
            {
                HttpContext.Response.StatusCode = 404;
                await HttpContext.SendStringAsync($"ERROR: Block {block} not found.", "text/plain", Encoding.UTF8);
                return;
            }

            if (Config.Default.Debug)
                Logger.Log($"Sent block {block}");
            var blockData = Startup.Blockchain.Chain[block];
            await HttpContext.SendStringAsync(blockData.ToString(), "application/json", Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves all transactions for a specific user.
        /// </summary>
        /// <param name="user">The user's name or identifier.</param>
        [Route(HttpVerbs.Get, "/GetUserTransactions/{user}")]
        public async Task GetUserTransactions(string user)
        {
            if (string.IsNullOrEmpty(user))
            {
                HttpContext.Response.StatusCode = 400;
                await HttpContext.SendStringAsync("ERROR: User name cannot be null or empty.", "text/plain",
                    Encoding.UTF8);
                return;
            }

            var userTransactions =
                BlockchainStorage.GetUserTransactions(user, Config.Default.BlockchainPath, Config.Default.ChainId);

            if (string.IsNullOrEmpty(userTransactions))
            {
                HttpContext.Response.StatusCode = 404;
                await HttpContext.SendStringAsync($"ERROR: No transactions for {user} found.", "text/plain",
                    Encoding.UTF8);
                return;
            }

            if (Config.Default.Debug)
                Logger.Log($"Sent transactions for user {user}");
            await HttpContext.SendStringAsync(userTransactions, "application/json", Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves the code for a specific smart contract.
        /// </summary>
        /// <param name="contract">The name of the contract to retrieve.</param>
        [Route(HttpVerbs.Get, "/GetContractCode/{contract}")]
        public async Task GetContractCode(string contract)
        {
            if (string.IsNullOrEmpty(contract))
            {
                HttpContext.Response.StatusCode = 400;
                await HttpContext.SendStringAsync("ERROR: Contract name cannot be null or empty.", "text/plain",
                    Encoding.UTF8);
                return;
            }

            var contractCode =
                BlockchainStorage.GetContractCode(contract, Config.Default.BlockchainPath, Config.Default.ChainId);

            if (string.IsNullOrEmpty(contractCode))
            {
                HttpContext.Response.StatusCode = 404;
                await HttpContext.SendStringAsync($"ERROR: Contract code for {contract} not found.", "text/plain",
                    Encoding.UTF8);
                return;
            }

            if (Config.Default.Debug)
                Logger.Log($"Sent contract code for {contract}");
            await HttpContext.SendStringAsync(contractCode, "application/json", Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves contract names that match the provided search string.
        /// </summary>
        /// <param name="search">The search term to filter contract names.</param>
        [Route(HttpVerbs.Get, "/GetContractNames/{search}")]
        public async Task GetContractNames(string search)
        {
            var contractNames = BlockchainStorage.GetContractNames(
                Config.Default.BlockchainPath,
                Config.Default.ChainId,
                search,
                50);

            if (Config.Default.Debug)
                Logger.Log($"Sent {contractNames.Count} contract names for filter '{search}'");
            await HttpContext.SendStringAsync(JsonSerializer.Serialize(contractNames), "application/json",
                Encoding.UTF8);
        }

        /// <summary>
        ///     Pushes a new blockchain to the server. Replaces the existing blockchain if the incoming chain is valid and longer.
        /// </summary>
        [Route(HttpVerbs.Post, "/PushChain")]
        public async Task PushChain()
        {
            try
            {
                var serializedChain = await HttpContext.GetRequestBodyAsStringAsync();
                Logger.Log($"PushChain: {serializedChain}");
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

                    await HttpContext.SendStringAsync("ok", "text/plain", Encoding.UTF8);
                    return;
                }
            }
            catch (Exception e)
            {
                Logger.Log($"ERROR: {e.Message}\n{e.StackTrace}");
            }

            await HttpContext.SendStringAsync("", "text/plain", Encoding.UTF8);
        }

        /// <summary>
        ///     Processes a new block received by the server and adds it to the blockchain if valid.
        /// </summary>
        [Route(HttpVerbs.Post, "/NewBlock")]
        public async Task NewBlock()
        {
            var serializedBlock = await HttpContext.GetRequestBodyAsStringAsync();
            if (Config.Default.Debug)
                Logger.Log($"NewBlock: {serializedBlock}");

            Block newBlock = null;

            try
            {
                newBlock = Block.FromBase64(serializedBlock);
            }
            catch (Exception e)
            {
                Logger.Log($"ERROR: {e.Message}\n{e.StackTrace}");
            }

            if (newBlock != null && Startup.Blockchain != null && Startup.Blockchain.AddBlock(newBlock, true, false))
                await HttpContext.SendStringAsync("ok", "text/plain", Encoding.UTF8);
            else
                await HttpContext.SendStringAsync("", "text/plain", Encoding.UTF8);
        }
    }
}