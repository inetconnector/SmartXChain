using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartXChain.Contracts;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public sealed class Block
{
    public Block(List<Transaction> transactions, string previousHash)
    {
        if (Timestamp == DateTime.MinValue)
            Timestamp = DateTime.UtcNow;

        Transactions = transactions;
        PreviousHash = previousHash;
        Hash = CalculateHash();
    }

    public Block()
    {
    }

    [JsonInclude] public DateTime Timestamp { get; internal set; } = DateTime.MinValue;
    [JsonInclude] public List<Transaction> Transactions { get; internal set; }
    [JsonInclude] public string PreviousHash { get; internal set; }
    [JsonInclude] public string Hash { get; internal set; }
    [JsonInclude] public string Issuer { get; internal set; }
    [JsonInclude] public string NodeAddress { get; internal set; }
    [JsonInclude] public int Nonce { get; internal set; }

    /// <summary>
    ///     Get a dictionary of SmartContract from the block
    /// </summary>
    [JsonInclude]
    public Dictionary<string, SmartContract?> SmartContracts
    {
        get
        {
            var contracts = new Dictionary<string, SmartContract?>();
            foreach (var transaction in Transactions)
                if (transaction.Recipient == Blockchain.SystemAddress &&
                    transaction.Info.StartsWith("$$") &&
                    !string.IsNullOrEmpty(transaction.Data))
                {
                    var contractName = transaction.Info.Substring(2); // Removes "$$" from the start

                    if (!contracts.ContainsKey(contractName)) // Avoids duplicates
                        try
                        {
                            var contractCode = Serializer.DeserializeFromBase64<string>(transaction.Data);
                            var contract = new SmartContract(
                                transaction.Sender,
                                Serializer.SerializeToBase64(contractCode),
                                contractName
                            );
                            contracts[contractName] = contract;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, $"Failed to deserialize contract '{contractName}'");
                        }
                }

            return contracts;
        }
    }

    [JsonIgnore] public string Base64Encoded => Convert.ToBase64String(GetBytes());


    /// List
    /// <Transactions> which have approved the Transaction. Used by Tangle.cs</Transactions>
    [JsonInclude]
    public List<string> Approves { get; private set; } = new();

    /// <summary>
    ///     Calculates the hash of the block based on its properties.
    /// </summary>
    /// <returns>A string representing the hash of the block.</returns>
    public string CalculateHash()
    {
        using var sha256 = SHA256.Create();

        var transactionsHash = "";
        foreach (var transaction in Transactions)
            transactionsHash += transaction.CalculateHash();

        var rawData = $"{transactionsHash}-{PreviousHash}-{Nonce}";
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    ///     Mines the block by adjusting the nonce until the hash meets the difficulty requirements.
    /// </summary>
    /// <param name="difficulty">The number of leading zeros required in the hash.</param>
    public void Mine(int difficulty)
    {
        if (difficulty == 0)
        {
            Hash = CalculateHash();
            Issuer = Config.Default.MinerAddress;
            NodeAddress = Config.Default.NodeAddress;
        }
        else
        {
            var hashPrefix = new string('0', difficulty);
            while (!Hash.StartsWith(hashPrefix))
            {
                Nonce++;
                Hash = CalculateHash();
            }
        }

        Logger.Log($"Blocks mined: {Hash}");
    }

    /// <summary>
    ///     Serializes the block into a compressed byte array.
    /// </summary>
    /// <returns>A byte array representing the compressed block data.</returns>
    public byte[] GetBytes()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(this, options);
        var compressedData = Compress.CompressString(jsonString);
        return compressedData;
    }

    /// <summary>
    ///     Converts the block into a Base64-encoded string.
    /// </summary>
    /// <returns>A Base64 string representation of the block.</returns>
    public string ToBase64()
    {
        return Convert.ToBase64String(GetBytes());
    }

    /// <summary>
    ///     Reconstructs a block from a Base64-encoded string.
    /// </summary>
    /// <param name="base64String">The Base64 string representation of the block.</param>
    /// <returns>A Blocks object or null if deserialization fails.</returns>
    public static Block? FromBase64(string base64String)
    {
        try
        {
            var compressedData = Convert.FromBase64String(base64String);
            var jsonString = Compress.DecompressString(compressedData);
            var block = JsonSerializer.Deserialize<Block>(jsonString);
            return block;
        }
        catch (Exception e)
        {
            Logger.LogException(e, "Blocks decompress failed.");
        }

        return null;
    }

    /// <summary>
    ///     Saves the block to a file as a compressed byte array.
    /// </summary>
    /// <param name="filePath">The file path where the block will be saved.</param>
    public void Save(string filePath)
    {
        File.WriteAllBytes(filePath, GetBytes());
        Logger.Log("Blocks saved (compressed) to file.");
    }

    /// <summary>
    ///     Loads a block from a compressed file.
    /// </summary>
    /// <param name="filePath">The file path to load the block from.</param>
    /// <returns>A Blocks object or null if deserialization fails.</returns>
    public static Block? Load(string filePath)
    {
        var compressedData = File.ReadAllBytes(filePath);
        var jsonString = Compress.DecompressString(compressedData);
        var block = JsonSerializer.Deserialize<Block>(jsonString);
        return block;
    }

    /// <summary>
    ///     Returns a JSON representation of the block, excluding empty properties.
    /// </summary>
    /// <returns>A JSON string containing all block properties.</returns>
    public override string ToString()
    {
        var blockDetails = new Dictionary<string, object>
        {
            ["Timestamp"] = Timestamp,
            ["PreviousHash"] = PreviousHash,
            ["Hash"] = Hash,
            ["Nonce"] = Nonce,
            // Serialize Transactions as JSON objects instead of strings
            ["Transactions"] = Transactions,
            ["SmartContracts"] = SmartContracts
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true, // Pretty print JSON
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull // Ignore null values
        };

        return JsonSerializer.Serialize(blockDetails, options);
    }
}