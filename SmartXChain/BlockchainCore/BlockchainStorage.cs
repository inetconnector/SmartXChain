using System.Data.SQLite;
using Newtonsoft.Json;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public static class BlockchainStorage
{
    public static bool SaveBlock(Block block, string blockchainPath, string chainId)
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
                            BlockHash TEXT,
                            Sender TEXT,
                            Recipient TEXT,
                            Amount REAL,
                            Timestamp DATETIME,
                            Data TEXT,
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
                        INSERT INTO Transactions (BlockHash, Sender, Recipient, Amount, Timestamp, Data)
                        VALUES (@BlockHash, @Sender, @Recipient, @Amount, @Timestamp, @Data);";
                    using (var command = new SQLiteCommand(insertTransactionQuery, connection))
                    {
                        command.Parameters.AddWithValue("@BlockHash", block.Hash);
                        command.Parameters.AddWithValue("@Sender", transaction.Sender);
                        command.Parameters.AddWithValue("@Recipient", transaction.Recipient);
                        command.Parameters.AddWithValue("@Amount", transaction.Amount);
                        command.Parameters.AddWithValue("@Timestamp", transaction.Timestamp);
                        command.Parameters.AddWithValue("@Data", transaction.Data);
                        command.ExecuteNonQuery();
                    }
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"ERROR: Block could not be saved to SQLite: {ex.Message}");
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
                Logger.Log($"ERROR: Database not found at {databasePath}");
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
                        if (reader.Read()) return Block.FromBase64(reader["Data"].ToString());
                    }
                }
            }

            return null; // Block not found
        }
        catch (Exception ex)
        {
            Logger.Log($"ERROR: Block could not be retrieved from SQLite: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets code of a deployed contract
    /// </summary>
    /// <param name="contractName"></param>
    /// <param name="blockchainPath"></param>
    /// <param name="chainId"></param>
    /// <returns></returns>
    public static string? GetContractCode(string? contractName, string blockchainPath, string chainId)
    {
        if (string.IsNullOrWhiteSpace(contractName))
        {
            Logger.Log("ERROR: Contract name is null or empty.");
            return null;
        }

        var databasePath = Path.Combine(blockchainPath, chainId + ".db");

        try
        {
            // Check if the database file exists
            if (!File.Exists(databasePath))
            {
                Logger.Log($"ERROR: Database not found at {databasePath}");
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
                    command.Parameters.AddWithValue("@ContractName", $"%\"{contractName}\":%"); // Search for the contract key in JSON format

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
                                if (contractCode != null)
                                {
                                    return contractCode;
                                }
                            }
                        }
                    }
                }
            }

            Logger.Log($"ERROR: Contract '{contractName}' not found.");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log($"ERROR: Could not retrieve contract code: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts the contract code from the JSON data if the specified contract name exists.
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
            Logger.Log($"ERROR: Failed to parse SmartContracts JSON: {ex.Message}");
            return null;
        }
    }


    /// <summary>
    /// Get contract names from database
    /// </summary>
    /// <param name="blockchainPath"></param>
    /// <param name="chainId"></param>
    /// <param name="nameFilter"></param>
    /// <param name="maxResults"></param>
    /// <returns></returns>
    public static List<string> GetContractNames(string blockchainPath, string chainId, string? nameFilter = null, int maxResults = 1)
    {
        // Construct the database file path
        var databasePath = Path.Combine(blockchainPath, chainId + ".db");
        var contractNames = new List<string>();

        try
        {
            // Ensure the database file exists
            if (!File.Exists(databasePath))
            {
                Logger.Log($"ERROR: Database not found at {databasePath}");
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
                    command.Parameters.AddWithValue("@NameFilter", nameFilter != null ? $"%{nameFilter}%" : (object)DBNull.Value);
                    command.Parameters.AddWithValue("@MaxResults", maxResults);

                    // Execute the query
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var smartContractsJson = reader["SmartContracts"]?.ToString();

                            if (!string.IsNullOrEmpty(smartContractsJson))
                            {
                                // Parse JSON data and extract contract names
                                contractNames.AddRange(ParseSmartContracts(smartContractsJson, nameFilter));
                            }

                            // Check if the maximum number of results has been reached
                            if (contractNames.Count >= maxResults)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            // Remove duplicates and limit results
            return contractNames.Distinct().Take(maxResults).ToList();
        }
        catch (Exception ex)
        {
            // Log the error
            Logger.Log($"ERROR: Could not retrieve contract names: {ex.Message}");
            return contractNames;
        }
    }

    /// <summary>
    /// Helper method to parse SmartContracts JSON and extract contract names
    /// matching the provided name filter.
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
            Logger.Log($"ERROR: Failed to parse SmartContracts JSON: {ex.Message}");
            return Enumerable.Empty<string>();
        }
    }



}