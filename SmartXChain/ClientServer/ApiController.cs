using System.Text;
using System.Text.Json;
using EmbedIO;
using EmbedIO.Routing;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace SmartXChain.Server;

public partial class BlockchainServer
{
    /// <summary>
    ///     for unsecured API calls with http: / https
    /// </summary>
    public partial class ApiController
    {
        #region UNSECURED

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
                Log($"Sent transactions for user {user}");
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
                Log($"Sent contract code for {contract}");
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
                Log($"Sent {contractNames.Count} contract names for filter '{search}'");
            await HttpContext.SendStringAsync(JsonSerializer.Serialize(contractNames), "application/json",
                Encoding.UTF8);
        }

        /// <summary>
        ///     Retrieves the current block count using an HTTP GET request.
        /// </summary>
        [Route(HttpVerbs.Get, "/GetChainInfo")]
        public async Task GetChainInfo()
        {
            if (Startup.Blockchain != null &&
                Startup.Blockchain.Chain != null)
            {
                if (Config.Default.Debug)
                    Log($"GetChainInfo: {Startup.Blockchain.Chain.Count}");


                await HttpContext.SendStringAsync(JsonSerializer.Serialize(CreateChainInfo()), "text/plain",
                    Encoding.UTF8);
                return;
            }

            await HttpContext.SendStringAsync("", "text/plain", Encoding.UTF8);
        }

        public static ChainInfo CreateChainInfo(string message = "ChainInfo")
        {
            if (Startup.Blockchain != null && Startup.Blockchain.Chain != null)
            {
                var chainInfo = new ChainInfo
                {
                    Message = message,
                    ChainID = Startup.Blockchain.ChainId,
                    BlockCount = Startup.Blockchain.Chain.Count,
                    LastHash = Startup.Blockchain.Chain.Last().Hash,
                    FirstHash = Startup.Blockchain.Chain.First().Hash,
                    LastDate = Startup.Blockchain.Chain.Last().Timestamp,
                    URL = Config.Default.URL
                };
                return chainInfo;
            }

            return new ChainInfo
            {
                Message = message,
                URL = Config.Default.URL
            };
        }

        /// <summary>
        ///     Retrieves specific block data by block number.
        /// </summary>
        /// <param name="block">The block number to retrieve.</param>
        [Route(HttpVerbs.Get, "/GetBlockData/{block}")]
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

            if (Config.Default.Debug) Log($"Sent block {block}");

            if (Startup.Blockchain != null && Startup.Blockchain.Chain != null)
            {
                var blockData = Startup.Blockchain.Chain[blockInt].ToBase64();

                await HttpContext.SendStringAsync(blockData, "application/json", Encoding.UTF8);
            }
            else
            {
                LogError("Blockchain is empty");
            }
        }

        #endregion
    }
}