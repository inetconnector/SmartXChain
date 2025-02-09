using Newtonsoft.Json;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using SQLite;

namespace SmartXChain.BlockchainCore;

public static class BlockchainStorage
{
    /// <summary>
    ///     Clears all data from the Blocks and Transactions tables.
    /// </summary>
    public static bool ClearDatabase(string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database file does not exist: {databasePath}");
                return false;
            }

            using (var db = new SQLiteConnection(databasePath))
            {
                try
                {
                    db.DeleteAll<Block>();
                }
                catch (Exception e)
                {
                }

                try
                {
                    db.DeleteAll<Transaction>();
                }
                catch (Exception e)
                {
                }
            }

            Logger.Log("Database cleared successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to clear database.");
            return false;
        }
    }

    /// <summary>
    ///     Saves block and transactions to database.
    /// </summary>
    public static bool SaveBlock(Block block, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        Directory.CreateDirectory(blockchainPath);

        try
        {
            using (var db = new SQLiteConnection(databasePath))
            {
                db.CreateTable<DBTransaction>();
                db.CreateTable<DBBlock>();

                foreach (var transaction in block.Transactions)
                {
                    var dbTransaction = new DBTransaction
                    {
                        ID = transaction.ID,
                        ParentBlock = block.Hash,
                        Gas = transaction.Gas,
                        Amount = transaction.Amount,
                        Data = transaction.Data,
                        Info = transaction.Info,
                        Recipient = transaction.Recipient,
                        Sender = transaction.Sender,
                        Timestamp = transaction.Timestamp
                    };
                    db.Insert(dbTransaction);
                }

                var dbBlock = new DBBlock
                {
                    Hash = block.Hash,
                    Timestamp = block.Timestamp,
                    Nonce = block.Nonce,
                    PreviousHash = block.PreviousHash,
                    SmartContractsJson = JsonConvert.SerializeObject(block.SmartContracts),
                    ApprovesJson = JsonConvert.SerializeObject(block.Approves)
                };

                db.InsertOrReplace(dbBlock);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Error saving block {block.Hash}");
            return false;
        }
    }

    public static Block? GetBlockByHash(string hash, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database not found at {databasePath}");
                return null;
            }

            using (var db = new SQLiteConnection(databasePath))
            {
                var dbBlock = db.Table<DBBlock>().FirstOrDefault(b => b.Hash == hash);
                if (dbBlock == null) return null;

                var block = new Block
                {
                    Hash = dbBlock.Hash,
                    Timestamp = dbBlock.Timestamp,
                    Nonce = dbBlock.Nonce,
                    PreviousHash = dbBlock.PreviousHash
                };

                return block;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Blocks could not be retrieved from SQLite");
            return null;
        }
    }

    /// <summary>
    ///     Retrieves the code of a deployed smart contract from the blockchain database.
    /// </summary>
    public static string? GetContractCode(string? contractName, string blockchainPath, string chainId)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            Logger.LogError("Contract name is null or empty.");
            return null;
        }

        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database not found at {databasePath}");
                return null;
            }

            using (var db = new SQLiteConnection(databasePath))
            {
                var blocks = db.Table<DBBlock>().ToList();
                foreach (var dbBlock in blocks)
                {
                    var smartContracts =
                        JsonConvert.DeserializeObject<Dictionary<string, SmartContract?>>(dbBlock.SmartContractsJson);
                    if (smartContracts != null && smartContracts.ContainsKey(contractName))
                    {
                        var contractCode =
                            Serializer.DeserializeFromBase64<string>(smartContracts[contractName]
                                ?.SerializedContractCode);
                        if (contractCode != null)
                            return contractCode;
                    }
                }
            }

            Logger.LogError($"Contract '{contractName}' not found.");
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Could not retrieve contract code");
            return null;
        }
    }


    /// <summary>
    ///     Retrieves all transactions for a given user from the blockchain database as a JSON string.
    /// </summary>
    public static string GetUserTransactions(string user, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var transactions = new List<Dictionary<string, object>>();

        try
        {
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database not found at {databasePath}");
                return JsonConvert.SerializeObject(new { error = "Database not found" });
            }

            using (var db = new SQLiteConnection(databasePath))
            {
                var userTransactions = db.Table<Transaction>()
                    .Where(t => t.Sender == user || t.Recipient == user)
                    .ToList();

                transactions = userTransactions.Select(t => new Dictionary<string, object>
                {
                    { "TransactionType", t.TransactionType },
                    { "Sender", t.Sender },
                    { "Recipient", t.Recipient },
                    { "Amount", t.Amount },
                    { "Timestamp", t.Timestamp },
                    { "ChainInfo", t.Data },
                    { "Info", t.Info }
                }).ToList();
            }

            return JsonConvert.SerializeObject(transactions);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Could not retrieve transactions for user {user}");
            return JsonConvert.SerializeObject(new { error = "An error occurred" });
        }
    }

    /// <summary>
    ///     Retrieves contract names from the database.
    /// </summary>
    public static List<string> GetContractNames(string blockchainPath, string chainId, string? nameFilter = null,
        int maxResults = 1)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var contractNames = new List<string>();

        try
        {
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database not found at {databasePath}");
                return contractNames;
            }

            using (var db = new SQLiteConnection(databasePath))
            {
                var blocks = db.Table<DBBlock>().ToList();
                foreach (var dbBlock in blocks)
                {
                    var smartContracts =
                        JsonConvert.DeserializeObject<Dictionary<string, SmartContract?>>(dbBlock.SmartContractsJson);
                    if (smartContracts != null)
                        foreach (var contractName in smartContracts.Keys)
                            if (string.IsNullOrEmpty(nameFilter) || contractName.Contains(nameFilter))
                            {
                                contractNames.Add(contractName);
                                if (contractNames.Count >= maxResults)
                                    break;
                            }
                }
            }

            return contractNames.Distinct().Take(maxResults).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Could not retrieve contract names");
            return contractNames;
        }
    }


    /// <summary>
    ///     Retrieves the most recent blocks along with their transactions.
    /// </summary>
    public static IEnumerable<Block> GetLatestBlocksWithTransactions(string blockchainPath, string chainId, int count)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var blocks = new List<Block>();

        try
        {
            using (var db = new SQLiteConnection(databasePath))
            {
                var recentBlocks = db.Table<DBBlock>()
                    .OrderByDescending(b => b.Timestamp)
                    .Take(count)
                    .ToList();

                foreach (var dbBlock in recentBlocks)
                {
                    var block = new Block
                    {
                        Hash = dbBlock.Hash,
                        Timestamp = dbBlock.Timestamp,
                        Nonce = dbBlock.Nonce,
                        PreviousHash = dbBlock.PreviousHash,
                        Transactions = db.Table<Transaction>().Where(t => t.ParentBlock == dbBlock.Hash).ToList()
                    };

                    blocks.Add(block);
                }
            }

            return blocks;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve blocks with transactions.");
            return blocks;
        }
    }

    /// <summary>
    ///     Retrieves transactions for a specific block.
    /// </summary>
    private static List<Transaction> GetTransactionsForBlock(string blockHash, SQLiteConnection db)
    {
        try
        {
            return db.Table<Transaction>().Where(t => t.ParentBlock == blockHash).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Failed to retrieve transactions for block {blockHash}");
            return new List<Transaction>();
        }
    }

    /// <summary>
    ///     Retrieves the most recent gas transaction.
    /// </summary>
    public static Transaction? GetLatestTransaction(string blockchainPath, string chainId,
        Transaction.TransactionTypes type = Transaction.TransactionTypes.Gas)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            using (var db = new SQLiteConnection(databasePath))
            {
                return db.Table<Transaction>()
                    .Where(t => t.TransactionType == type)
                    .OrderByDescending(t => t.Timestamp)
                    .FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve the latest gas transaction.");
            return null;
        }
    }

    /// <summary>
    ///     Searches for transactions by user.
    /// </summary>
    public static IEnumerable<Transaction> SearchTransactionsByUser(string user, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            using (var db = new SQLiteConnection(databasePath))
            {
                return db.Table<Transaction>()
                    .Where(t => t.Sender == user || t.Recipient == user)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Failed to retrieve transactions for user {user}.");
            return Enumerable.Empty<Transaction>();
        }
    }

    /// <summary>
    ///     Searches for a block by hash and retrieves its transactions.
    /// </summary>
    public static Block? SearchBlockByHashWithTransactions(string hash, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            using (var db = new SQLiteConnection(databasePath))
            {
                var block = db.Table<Block>().FirstOrDefault(b => b.Hash == hash);
                if (block != null)
                    block.Transactions = db.Table<Transaction>().Where(t => t.ParentBlock == hash).ToList();

                return block;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Failed to retrieve block by hash {hash}.");
            return null;
        }
    }

    /// <summary>
    ///     Retrieves the most recent transactions.
    /// </summary>
    public static IEnumerable<Transaction> GetRecentTransactions(string blockchainPath, string chainId, int count)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            using (var db = new SQLiteConnection(databasePath))
            {
                return db.Table<Transaction>()
                    .OrderByDescending(t => t.Timestamp)
                    .Take(count)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve recent transactions.");
            return Enumerable.Empty<Transaction>();
        }
    }

    public class DBTransaction
    {
        public Guid ID { get; set; }
        public string ParentBlock { get; set; }
        public string Sender { get; set; }
        public string Recipient { get; set; }
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Gas { get; set; }
        public string Data { get; set; }
        public string Info { get; set; }
    }

    public class DBBlock
    {
        public DateTime Timestamp { get; set; } = DateTime.MinValue;
        public string PreviousHash { get; set; }
        public string Hash { get; set; }
        public int Nonce { get; set; }
        public string SmartContractsJson { get; set; }
        public string ApprovesJson { get; set; }
    }
}