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
}