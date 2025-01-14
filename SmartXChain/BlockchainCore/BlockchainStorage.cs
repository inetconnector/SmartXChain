using System.Data.SQLite;
using Newtonsoft.Json;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public static class BlockchainStorage
{
    /// <summary>
    /// Saves a block and its associated transactions into the SQLite database.
    /// If the database or required tables do not exist, they are created.
    /// This method ensures that the block data is stored or updated, and its transactions are inserted.
    /// </summary>
    /// <param name="block">The block object containing transactions and metadata to be saved.</param>
    /// <param name="blockchainPath">The file path to the blockchain storage directory.</param>
    /// <param name="chainId">The identifier for the specific blockchain chain.</param>
    /// <returns>True if the block is successfully saved; otherwise, false.</returns>
    public static bool SaveBlock(Block? block, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        Directory.CreateDirectory(blockchainPath);

        try
        {
            // Ensure the database exists
            if (!File.Exists(databasePath))
            {
                SQLiteConnection.CreateFile(databasePath);
                using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
                {
                    connection.Open();

                    // Create tables
                    var createBlockTableQuery = @"
                        CREATE TABLE IF NOT EXISTS Blocks (
                            Hash TEXT PRIMARY KEY,
                            PreviousHash TEXT,
                            Timestamp DATETIME,
                            Nonce INTEGER,
                            SmartContracts TEXT,
                            Transactions TEXT,
                            Base64Encoded TEXT
                        );";
                    using (var command = new SQLiteCommand(createBlockTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }

                    var createTransactionTableQuery = @"
                        CREATE TABLE IF NOT EXISTS Transactions (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            TransactionType INT,
                            BlockHash TEXT,
                            Sender TEXT,
                            Recipient TEXT,
                            Amount REAL,
                            Timestamp DATETIME,
                            Data TEXT,
                            Info TEXT,
                            FOREIGN KEY (BlockHash) REFERENCES Blocks (Hash)
                        );";
                    using (var command = new SQLiteCommand(createTransactionTableQuery, connection))
                    {
                        command.ExecuteNonQuery();
                    }
                }
            }

            // Insert or replace the block data
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var insertBlockQuery = @"
                    INSERT OR REPLACE INTO Blocks (Hash, PreviousHash, Timestamp, Nonce, SmartContracts, Transactions, Base64Encoded)
                    VALUES (@Hash, @PreviousHash, @Timestamp, @Nonce, @SmartContracts, @Transactions, @Base64Encoded);";
                using (var command = new SQLiteCommand(insertBlockQuery, connection))
                {
                    command.Parameters.AddWithValue("@Hash", block.Hash);
                    command.Parameters.AddWithValue("@PreviousHash", block.PreviousHash);
                    command.Parameters.AddWithValue("@Timestamp", block.Timestamp);
                    command.Parameters.AddWithValue("@Nonce", block.Nonce);
                    command.Parameters.AddWithValue("@SmartContracts",
                        JsonConvert.SerializeObject(block.SmartContracts));
                    command.Parameters.AddWithValue("@Transactions", JsonConvert.SerializeObject(block.Transactions));
                    command.Parameters.AddWithValue("@Base64Encoded", block.Base64Encoded);
                    command.ExecuteNonQuery();
                }

                // Insert transactions
                foreach (var transaction in block.Transactions)
                {
                    var insertTransactionQuery = @"
                        INSERT INTO Transactions (TransactionType, BlockHash, Sender, Recipient, Amount, Timestamp, Data, Info)
                        VALUES (@TransactionType, @BlockHash, @Sender, @Recipient, @Amount, @Timestamp, @Data, @Info);";
                    using (var command = new SQLiteCommand(insertTransactionQuery, connection))
                    {
                        command.Parameters.AddWithValue("@TransactionType", transaction.TransactionType);
                        command.Parameters.AddWithValue("@BlockHash", block.Hash);
                        command.Parameters.AddWithValue("@Sender", transaction.Sender);
                        command.Parameters.AddWithValue("@Recipient", transaction.Recipient);
                        command.Parameters.AddWithValue("@Amount", transaction.Amount);
                        command.Parameters.AddWithValue("@Timestamp", transaction.Timestamp);
                        command.Parameters.AddWithValue("@Data", transaction.Data);
                        command.Parameters.AddWithValue("@Info", transaction.Info);
                        command.ExecuteNonQuery();
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Block could not be saved to SQLite");
            return false;
        }
    }

    /// <summary>
    /// Retrieves a specific block by its hash from the SQLite database.
    /// </summary>
    /// <param name="hash">The hash of the block to be retrieved.</param>
    /// <param name="blockchainPath">The file path to the blockchain's storage directory.</param>
    /// <param name="chainId">The unique identifier for the blockchain.</param>
    /// <returns>
    /// The block object if found, otherwise null.
    /// </returns>
    /// <remarks>
    /// The method checks if the database exists and queries the Blocks table for a block matching the provided hash. 
    /// If found, it reconstructs the block from its Base64-encoded data.
    /// </remarks>

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

            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var selectBlockQuery = @"
                    SELECT Hash, PreviousHash, Timestamp, Nonce, SmartContracts, Transactions, Base64Encoded
                    FROM Blocks
                    WHERE Hash = @Hash;";
                using (var command = new SQLiteCommand(selectBlockQuery, connection))
                {
                    command.Parameters.AddWithValue("@Hash", hash);
                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read()) return Block.FromBase64(reader["Base64Encoded"].ToString());
                    }
                }
            }

            return null; // Block not found
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Block could not be retrieved from SQLite");
            return null;
        }
    }

    /// <summary>
    /// Retrieves the code of a deployed smart contract from the blockchain database.
    /// </summary>
    /// <param name="contractName">The name of the contract to search for.</param>
    /// <param name="blockchainPath">The file path to the blockchain's storage directory.</param>
    /// <param name="chainId">The unique identifier of the blockchain chain.</param>
    /// <returns>
    /// A string containing the contract code if found, or null if the contract does not exist
    /// or an error occurs during the search.
    /// </returns>
    /// <remarks>
    /// This method looks for the contract name in the `SmartContracts` JSON field within the `Blocks` table.
    /// If the contract name is found, its corresponding code is deserialized and returned.
    /// Logs errors if the database file is missing or if the contract is not found.
    /// </remarks>
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
            // Check if the database file exists
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database not found at {databasePath}");
                return null;
            }

            // Establish a connection to the SQLite database
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                // Define the SQL query
                var selectContractQuery = @"
                SELECT SmartContracts
                FROM Blocks
                WHERE SmartContracts LIKE @ContractName;
            ";

                using (var command = new SQLiteCommand(selectContractQuery, connection))
                {
                    // Add parameter for contract name search
                    command.Parameters.AddWithValue("@ContractName",
                        $"%\"{contractName}\":%"); // Search for the contract key in JSON format

                    // Execute the query
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var smartContractsJson = reader["SmartContracts"]?.ToString();

                            if (!string.IsNullOrEmpty(smartContractsJson))
                            {
                                // Deserialize JSON and find the contract code
                                var contractCode = ExtractContractCode(smartContractsJson, contractName);
                                if (contractCode != null) return contractCode;
                            }
                        }
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
    ///     Extracts the contract code from the JSON data if the specified contract name exists.
    /// </summary>
    /// <param name="smartContractsJson">JSON string containing smart contracts data.</param>
    /// <param name="contractName">The name of the contract to find.</param>
    /// <returns>The contract code if found, otherwise null.</returns>
    private static string? ExtractContractCode(string smartContractsJson, string contractName)
    {
        try
        {
            // Deserialize JSON into a dictionary
            var contracts = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(smartContractsJson);

            // Check if the contract exists and return its code
            if (contracts != null && contracts.TryGetValue(contractName, out var contractData))
            {
                var serializedCode = contractData?.SerializedContractCode.ToString();
                var contractCode = Serializer.DeserializeFromBase64<string>(serializedCode);
                return contractCode;
            }

            return null;
        }
        catch (JsonException ex)
        {
            Logger.LogException(ex, "Failed to parse SmartContracts JSON");
            return null;
        }
    }

    /// <summary>
    ///     Retrieves all transactions for a given user from the blockchain database as a JSON string.
    /// </summary>
    /// <param name="user">The user whose transactions are being queried (either sender or recipient).</param>
    /// <param name="blockchainPath">The path to the blockchain's storage directory.</param>
    /// <param name="chainId">The identifier of the blockchain chain.</param>
    /// <returns>A JSON string containing all transactions for the specified user, or an error message if the operation fails.</returns>
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

            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var selectTransactionsQuery = @"
                SELECT Id, TransactionType, BlockHash, Sender, Recipient, Amount, Timestamp, Data, Info
                FROM Transactions
                WHERE Sender = @User OR Recipient = @User;
            ";

                using (var command = new SQLiteCommand(selectTransactionsQuery, connection))
                {
                    command.Parameters.AddWithValue("@User", user);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Create a dictionary for each transaction
                            var transaction = new Dictionary<string, object>
                            {
                                { "Id", reader["Id"] },
                                { "TransactionType", reader["TransactionType"] },
                                { "BlockHash", reader["BlockHash"] },
                                { "Sender", reader["Sender"] },
                                { "Recipient", reader["Recipient"] },
                                { "Amount", Convert.ToDouble(reader["Amount"] ?? 0) },
                                { "Timestamp", reader["Timestamp"] },
                                { "Data", reader["Data"] },
                                { "Info", reader["Info"] }
                            };

                            transactions.Add(transaction);
                        }
                    }
                }
            }

            if (transactions.Count == 0)
                return "";

            return JsonConvert.SerializeObject(transactions);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Could not retrieve transactions for user {user}");
        }

        return "";
    }

    /// <summary>
    ///     Get contract names from database
    /// </summary>
    /// <param name="blockchainPath"></param>
    /// <param name="chainId"></param>
    /// <param name="nameFilter"></param>
    /// <param name="maxResults"></param>
    /// <returns></returns>
    public static List<string> GetContractNames(string blockchainPath, string chainId, string? nameFilter = null,
        int maxResults = 1)
    {
        // Construct the database file path
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var contractNames = new List<string>();

        try
        {
            // Ensure the database file exists
            if (!File.Exists(databasePath))
            {
                Logger.LogError($"Database not found at {databasePath}");
                return contractNames;
            }

            // Establish a connection to the SQLite database
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                // Define the SQL query
                var selectContractsQuery = @"
                SELECT SmartContracts
                FROM Blocks
                WHERE (@NameFilter IS NULL OR SmartContracts LIKE @NameFilter)
                LIMIT @MaxResults;
            ";

                using (var command = new SQLiteCommand(selectContractsQuery, connection))
                {
                    // Add parameters securely
                    command.Parameters.AddWithValue("@NameFilter",
                        nameFilter != null ? $"%{nameFilter}%" : DBNull.Value);
                    command.Parameters.AddWithValue("@MaxResults", maxResults);

                    // Execute the query
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var smartContractsJson = reader["SmartContracts"]?.ToString();

                            if (!string.IsNullOrEmpty(smartContractsJson))
                                // Parse JSON data and extract contract names
                                contractNames.AddRange(ParseSmartContracts(smartContractsJson, nameFilter));

                            // Check if the maximum number of results has been reached
                            if (contractNames.Count >= maxResults) break;
                        }
                    }
                }
            }

            // Remove duplicates and limit results
            return contractNames.Distinct().Take(maxResults).ToList();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Could not retrieve contract names");
            return contractNames;
        }
    }

    /// <summary>
    ///     Helper method to parse SmartContracts JSON and extract contract names
    ///     matching the provided name filter.
    /// </summary>
    /// <param name="smartContractsJson">JSON data of SmartContracts.</param>
    /// <param name="nameFilter">Filter to apply to the contract names (keys).</param>
    /// <returns>List of filtered contract names.</returns>
    private static IEnumerable<string> ParseSmartContracts(string smartContractsJson, string? nameFilter)
    {
        try
        {
            // Deserialize JSON into a dictionary
            var contracts = JsonConvert.DeserializeObject<Dictionary<string, object>>(smartContractsJson);

            if (contracts == null)
                return Enumerable.Empty<string>();

            // Filter keys based on the nameFilter
            return string.IsNullOrEmpty(nameFilter)
                ? contracts.Keys
                : contracts.Keys.Where(key => key.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException ex)
        {
            Logger.LogException(ex, "Failed to parse SmartContracts JSON");
            return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    ///     Retrieves the most recent blocks from the blockchain database along with their associated transactions.
    /// </summary>
    /// <param name="blockchainPath">The file path to the blockchain storage directory.</param>
    /// <param name="chainId">The identifier of the blockchain chain.</param>
    /// <param name="count">The number of recent blocks to retrieve.</param>
    /// <returns>
    ///     A collection of the most recent blocks, each containing transaction data.
    ///     If an error occurs, an empty list is returned.
    /// </returns>
    /// <remarks>
    ///     The method connects to the SQLite database, retrieves block data ordered by timestamp in descending order,
    ///     and includes transactions for each block by querying the Transactions table.
    /// </remarks>
    public static IEnumerable<Block> GetLatestBlocksWithTransactions(string blockchainPath, string chainId, int count)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var blocks = new List<Block>();

        try
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var blockQuery = @"
                SELECT Hash, PreviousHash, Timestamp, Nonce, Miner, Base64Encoded
                FROM Blocks
                ORDER BY Timestamp DESC
                LIMIT @Count;";
                using (var blockCommand = new SQLiteCommand(blockQuery, connection))
                {
                    blockCommand.Parameters.AddWithValue("@Count", count);

                    using (var blockReader = blockCommand.ExecuteReader())
                    {
                        while (blockReader.Read())
                        {
                            var blockHash = blockReader["Hash"].ToString();
                            var transactions = GetTransactionsForBlock(blockHash, connection);

                            var block = new Block(transactions, blockReader["PreviousHash"].ToString())
                            {
                                Timestamp = Convert.ToDateTime(blockReader["Timestamp"]),
                                Nonce = Convert.ToInt32(blockReader["Nonce"]),
                                Miner = blockReader["Miner"].ToString(),
                                Hash = blockReader["Hash"].ToString()
                            };
                            blocks.Add(block);
                        }
                    }
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
    ///     Retrieves all transactions associated with a specific block from the database.
    /// </summary>
    /// <param name="blockHash">The hash of the block whose transactions are to be retrieved.</param>
    /// <param name="connection">An open SQLite connection to the blockchain database.</param>
    /// <returns>A list of transactions associated with the specified block.</returns>
    /// <remarks>
    ///     This method queries the `Transactions` table using the block hash as a filter.
    ///     Each transaction is read from the database and deserialized into a `Transaction` object,
    ///     which is then added to a list. The list of transactions is returned at the end.
    /// </remarks>
    private static List<Transaction> GetTransactionsForBlock(string blockHash, SQLiteConnection connection)
    {
        var transactions = new List<Transaction>();

        var transactionQuery = @"
        SELECT Id, TransactionType, Sender, Recipient, Amount, Timestamp, Data, Info
        FROM Transactions
        WHERE BlockHash = @BlockHash;";
        using (var transactionCommand = new SQLiteCommand(transactionQuery, connection))
        {
            transactionCommand.Parameters.AddWithValue("@BlockHash", blockHash);

            using (var transactionReader = transactionCommand.ExecuteReader())
            {
                while (transactionReader.Read())
                {
                    var transaction = new Transaction
                    {
                        ID = Guid.NewGuid(),
                        TransactionType =
                            (Transaction.TransactionTypes)Convert.ToInt32(transactionReader["TransactionType"]),
                        Sender = transactionReader["Sender"].ToString(),
                        Recipient = transactionReader["Recipient"].ToString(),
                        Amount = Convert.ToDecimal(transactionReader["Amount"]),
                        Timestamp = Convert.ToDateTime(transactionReader["Timestamp"]),
                        Data = transactionReader["Data"].ToString(),
                        Info = transactionReader["Info"].ToString()
                    };
                    transactions.Add(transaction);
                }
            }
        }

        return transactions;
    }

    /// <summary>
    ///     Retrieves the most recent gas transaction from the blockchain database.
    ///     This method queries the Transactions table to find the latest transaction where the
    ///     TransactionType is set to Gas. Transactions are ordered by their Timestamp in
    ///     descending order, and the most recent one is returned. If no such transaction is
    ///     found, the method returns null.
    ///     Parameters:
    ///     - blockchainPath: The directory path where the blockchain database is located.
    ///     - chainId: The identifier of the specific blockchain chain.
    ///     Returns:
    ///     - A Transaction object representing the latest gas transaction, or null if no such
    ///     transaction exists.
    ///     Exceptions:
    ///     - Logs any exceptions encountered during the database operation and returns null.
    /// </summary>
    public static Transaction? GetLatestGasTransaction(string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var query = @"
                SELECT Id, TransactionType, Sender, Recipient, Amount, Timestamp, Data, Info
                FROM Transactions
                WHERE TransactionType = @GasTransactionType
                ORDER BY Timestamp DESC
                LIMIT 1;";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@GasTransactionType", (int)Transaction.TransactionTypes.Gas);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                            return new Transaction
                            {
                                ID = Guid.NewGuid(),
                                TransactionType =
                                    (Transaction.TransactionTypes)Convert.ToInt32(reader["TransactionType"]),
                                Sender = reader["Sender"].ToString(),
                                Recipient = reader["Recipient"].ToString(),
                                Amount = Convert.ToDecimal(reader["Amount"]),
                                Timestamp = Convert.ToDateTime(reader["Timestamp"]),
                                Data = reader["Data"].ToString(),
                                Info = reader["Info"].ToString()
                            };
                    }
                }
            }

            return null; // No gas transaction found
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve the latest gas transaction.");
            return null;
        }
    }

    /// <summary>
    ///     Searches for all transactions where the specified user is either the sender or the recipient.
    ///     This method queries the `Transactions` table in the blockchain database to retrieve
    ///     all transactions associated with the given user. The results include details such as
    ///     the transaction type, block hash, sender, recipient, amount, timestamp, additional data, and info.
    ///     Parameters:
    ///     - `user`: The address or identifier of the user whose transactions are being searched.
    ///     - `blockchainPath`: The directory path where the blockchain database is stored.
    ///     - `chainId`: The identifier for the blockchain network.
    ///     Returns:
    ///     - A collection of `Transaction` objects representing all transactions linked to the user.
    ///     Exceptions:
    ///     - Logs any exceptions that occur during database access and returns an empty list of transactions.
    /// </summary>
    public static IEnumerable<Transaction> SearchTransactionsByUser(string user, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var transactions = new List<Transaction>();

        try
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var query = @"
                SELECT Id, TransactionType, BlockHash, Sender, Recipient, Amount, Timestamp, Data, Info
                FROM Transactions
                WHERE Sender = @User OR Recipient = @User;";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@User", user);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var transaction = new Transaction
                            {
                                ID = Guid.NewGuid(),
                                TransactionType =
                                    (Transaction.TransactionTypes)Convert.ToInt32(reader["TransactionType"]),
                                Sender = reader["Sender"].ToString(),
                                Recipient = reader["Recipient"].ToString(),
                                Amount = Convert.ToDecimal(reader["Amount"]),
                                Timestamp = Convert.ToDateTime(reader["Timestamp"]),
                                Data = reader["Data"].ToString(),
                                Info = reader["Info"].ToString()
                            };
                            transactions.Add(transaction);
                        }
                    }
                }
            }

            return transactions;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve transactions by user.");
            return transactions;
        }
    }

    /// <summary>
    ///     Searches for a block in the blockchain database using its hash and retrieves its associated transactions.
    /// </summary>
    /// <param name="hash">The hash of the block to search for.</param>
    /// <param name="blockchainPath">The path to the blockchain database.</param>
    /// <param name="chainId">The identifier of the blockchain chain.</param>
    /// <returns>
    ///     A Block object containing its details and transactions if found, or null if no block with the given hash exists.
    /// </returns>
    /// <exception cref="Exception">Logs an exception if there is an error during database operations.</exception>
    public static Block? SearchBlockByHashWithTransactions(string hash, string blockchainPath, string chainId)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var blockQuery = @"
                SELECT Hash, PreviousHash, Timestamp, Nonce, Miner, Base64Encoded
                FROM Blocks
                WHERE Hash = @Hash;";
                using (var blockCommand = new SQLiteCommand(blockQuery, connection))
                {
                    blockCommand.Parameters.AddWithValue("@Hash", hash);

                    using (var blockReader = blockCommand.ExecuteReader())
                    {
                        if (blockReader.Read())
                        {
                            var transactions = GetTransactionsForBlock(hash, connection);

                            return new Block(transactions, blockReader["PreviousHash"].ToString())
                            {
                                Timestamp = Convert.ToDateTime(blockReader["Timestamp"]),
                                Nonce = Convert.ToInt32(blockReader["Nonce"]),
                                Miner = blockReader["Miner"].ToString(),
                                Hash = blockReader["Hash"].ToString()
                            };
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve block by hash.");
            return null;
        }
    }

    /// <summary>
    ///     Retrieves the most recent transactions from the blockchain database.
    /// </summary>
    /// <param name="blockchainPath">The file path to the blockchain's storage directory.</param>
    /// <param name="chainId">The identifier of the blockchain chain.</param>
    /// <param name="count">The number of recent transactions to retrieve.</param>
    /// <returns>
    ///     A collection of the most recent transactions, including details such as
    ///     transaction type, sender, recipient, amount, timestamp, additional data, and info.
    ///     Returns an empty list if no transactions are found or an error occurs.
    /// </returns>
    public static IEnumerable<Transaction> GetRecentTransactions(string blockchainPath, string chainId, int count)
    {
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var transactions = new List<Transaction>();

        try
        {
            using (var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;"))
            {
                connection.Open();

                var query = @"
                SELECT Id, TransactionType, Sender, Recipient, Amount, Timestamp, Data, Info
                FROM Transactions
                ORDER BY Timestamp DESC
                LIMIT @Count;";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Count", count);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var transaction = new Transaction
                            {
                                ID = Guid.NewGuid(),
                                TransactionType =
                                    (Transaction.TransactionTypes)Convert.ToInt32(reader["TransactionType"]),
                                Sender = reader["Sender"].ToString(),
                                Recipient = reader["Recipient"].ToString(),
                                Amount = Convert.ToDecimal(reader["Amount"]),
                                Timestamp = Convert.ToDateTime(reader["Timestamp"]),
                                Data = reader["Data"].ToString(),
                                Info = reader["Info"].ToString()
                            };
                            transactions.Add(transaction);
                        }
                    }
                }
            }

            return transactions;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to retrieve recent transactions.");
            return transactions;
        }
    }
}