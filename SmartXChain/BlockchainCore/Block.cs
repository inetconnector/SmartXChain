using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartXChain.Contracts;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     Represents a blockchain block containing transactions, hash, issuer, and optional smart contracts.
/// </summary>
public sealed class Block
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Block" /> class with the specified transactions and previous hash.
    /// </summary>
    /// <param name="transactions">The list of transactions included in this block.</param>
    /// <param name="previousHash">The hash of the previous block in the chain.</param>
    public Block(List<Transaction>? transactions, string? previousHash)
    {
        Timestamp = DateTime.UtcNow;
        Transactions = transactions ?? new List<Transaction>();
        PreviousHash = previousHash ?? string.Empty;
        Hash = CalculateHash();
        Issuer = Config.Default.MinerAddress;
        NodeAddress = Config.Default.NodeAddress;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="Block" /> class for deserialization.
    /// </summary>
    public Block()
    {
        Timestamp = DateTime.UtcNow;
        Transactions = new List<Transaction>();
        PreviousHash = string.Empty;
        Hash = string.Empty;
        Issuer = string.Empty;
        NodeAddress = string.Empty;
        Nonce = 0;
    }

    /// <summary>Gets or sets the UTC timestamp when this block was created.</summary>
    [JsonInclude]
    public DateTime Timestamp { get; internal set; }

    /// <summary>Gets or sets the list of transactions contained in this block.</summary>
    [JsonInclude]
    public List<Transaction> Transactions { get; internal set; } = new();

    /// <summary>Gets or sets the hash of the previous block in the chain.</summary>
    [JsonInclude]
    public string PreviousHash { get; internal set; } = string.Empty;

    /// <summary>Gets or sets the unique hash of this block.</summary>
    [JsonInclude]
    public string Hash { get; internal set; } = string.Empty;

    /// <summary>Gets or sets the address of the node that mined or issued this block.</summary>
    [JsonInclude]
    public string Issuer { get; internal set; } = string.Empty;

    /// <summary>Gets or sets the network node address that created this block.</summary>
    [JsonInclude]
    public string NodeAddress { get; internal set; } = string.Empty;

    /// <summary>Gets or sets the nonce value used in proof-of-work mining.</summary>
    [JsonInclude]
    public int Nonce { get; internal set; }

    /// <summary>
    ///     Gets a dictionary of <see cref="SmartContract" /> objects deployed within this block.
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
                    var contractName = transaction.Info.Substring(2);
                    if (!contracts.ContainsKey(contractName))
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

    /// <summary>Gets the Base64-encoded serialized form of this block.</summary>
    [JsonIgnore]
    public string Base64Encoded => Convert.ToBase64String(GetBytes());

    /// <summary>Gets the list of hashes from transactions that approve this block (used in Tangle structures).</summary>
    [JsonInclude]
    public List<string> Approves { get; private set; } = new();

    /// <summary>
    ///     Calculates the SHA-256 hash of this block.
    /// </summary>
    /// <returns>The Base64-encoded SHA-256 hash string.</returns>
    public string CalculateHash()
    {
        using var sha256 = SHA256.Create();
        var sb = new StringBuilder();

        foreach (var transaction in Transactions)
            sb.Append(transaction.CalculateHash());

        var rawData = $"{sb}-{PreviousHash}-{Nonce}";
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    ///     Mines the block by incrementing the nonce until the hash meets the specified difficulty.
    /// </summary>
    /// <param name="difficulty">The number of leading zeros required in the hash prefix.</param>
    public void Mine(int difficulty)
    {
        if (difficulty <= 0)
        {
            Hash = CalculateHash();
            Issuer = Config.Default.MinerAddress;
            NodeAddress = Config.Default.NodeAddress;
        }
        else
        {
            var prefix = new string('0', difficulty);
            do
            {
                Nonce++;
                Hash = CalculateHash();
            } while (!Hash.StartsWith(prefix, StringComparison.Ordinal));
        }

        Logger.Log($"Block mined: {Hash}");
    }

    /// <summary>
    ///     Serializes and compresses this block into a byte array.
    /// </summary>
    /// <returns>The compressed byte array representing this block.</returns>
    public byte[] GetBytes()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        return Compress.CompressString(json);
    }

    /// <summary>
    ///     Converts this block to a Base64 string representation.
    /// </summary>
    /// <returns>The Base64-encoded string representing this block.</returns>
    public string ToBase64()
    {
        return Convert.ToBase64String(GetBytes());
    }

    /// <summary>
    ///     Reconstructs a <see cref="Block" /> instance from a Base64 string.
    /// </summary>
    /// <param name="base64">The Base64-encoded block data.</param>
    /// <returns>The deserialized <see cref="Block" /> instance, or <c>null</c> if deserialization fails.</returns>
    public static Block? FromBase64(string base64)
    {
        try
        {
            var data = Convert.FromBase64String(base64);
            var json = Compress.DecompressString(data);
            return JsonSerializer.Deserialize<Block>(json);
        }
        catch (Exception e)
        {
            Logger.LogException(e, "Block deserialization failed.");
            return null;
        }
    }

    /// <summary>
    ///     Saves this block to disk as compressed data.
    /// </summary>
    /// <param name="path">The file path where the block should be stored.</param>
    public void Save(string path)
    {
        File.WriteAllBytes(path, GetBytes());
        Logger.Log($"Block saved to {path}");
    }

    /// <summary>
    ///     Loads a <see cref="Block" /> from compressed file data.
    /// </summary>
    /// <param name="path">The path to the file containing the serialized block.</param>
    /// <returns>The deserialized <see cref="Block" /> instance.</returns>
    public static Block? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var json = Compress.DecompressString(bytes);
        return JsonSerializer.Deserialize<Block>(json);
    }

    /// <summary>
    ///     Returns a formatted JSON string representation of the block for debugging and logging.
    /// </summary>
    /// <returns>A human-readable JSON string describing the block.</returns>
    public override string ToString()
    {
        var info = new
        {
            Timestamp,
            PreviousHash,
            Hash,
            Nonce,
            Transactions,
            SmartContracts
        };
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(info, options);
    }
}