using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartXChain.Contracts;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

/// <summary>
/// Represents a blockchain block containing transactions, hash, issuer and optional smart contracts.
/// </summary>
public sealed class Block
{
    /// <summary>
    /// Creates a new block with a list of transactions and a link to the previous block hash.
    /// </summary>
    public Block(List<Transaction> transactions, string previousHash)
    {
        Timestamp = DateTime.UtcNow;
        Transactions = transactions ?? new List<Transaction>();
        PreviousHash = previousHash ?? string.Empty;
        Hash = CalculateHash();
    }

    /// <summary>
    /// Default constructor (mainly for deserialization).
    /// </summary>
    public Block()
    {
        Transactions = new List<Transaction>();
        PreviousHash = string.Empty;
        Hash = string.Empty;
        Issuer = string.Empty;
        NodeAddress = string.Empty;
    }

    [JsonInclude] public DateTime Timestamp { get; internal set; } = DateTime.UtcNow;
    [JsonInclude] public List<Transaction> Transactions { get; internal set; } = new();
    [JsonInclude] public string PreviousHash { get; internal set; } = string.Empty;
    [JsonInclude] public string Hash { get; internal set; } = string.Empty;
    [JsonInclude] public string Issuer { get; internal set; } = string.Empty;
    [JsonInclude] public string NodeAddress { get; internal set; } = string.Empty;
    [JsonInclude] public int Nonce { get; internal set; }

    /// <summary>
    /// Dictionary of deployed SmartContracts extracted from this block.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, SmartContract?> SmartContracts
    {
        get
        {
            var contracts = new Dictionary<string, SmartContract?>();
            foreach (var transaction in Transactions)
            {
                if (transaction.Recipient == Blockchain.SystemAddress &&
                    transaction.Info.StartsWith("$$") &&
                    !string.IsNullOrEmpty(transaction.Data))
                {
                    var contractName = transaction.Info.Substring(2);
                    if (!contracts.ContainsKey(contractName))
                    {
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
                }
            }
            return contracts;
        }
    }

    [JsonIgnore]
    public string Base64Encoded => Convert.ToBase64String(GetBytes());

    /// <summary>List of transaction hashes that approve this block (used in Tangle structure).</summary>
    [JsonInclude]
    public List<string> Approves { get; private set; } = new();

    /// <summary>Calculates the SHA-256 hash of the block.</summary>
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
    /// Mines the block by incrementing the nonce until the hash satisfies the difficulty.
    /// </summary>
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

    /// <summary>Serializes and compresses the block to bytes.</summary>
    public byte[] GetBytes()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        return Compress.CompressString(json);
    }

    /// <summary>Encodes the block as a Base64 string.</summary>
    public string ToBase64() => Convert.ToBase64String(GetBytes());

    /// <summary>Reconstructs a block from a Base64 string.</summary>
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

    /// <summary>Saves the block to disk as compressed data.</summary>
    public void Save(string path)
    {
        File.WriteAllBytes(path, GetBytes());
        Logger.Log($"Block saved to {path}");
    }

    /// <summary>Loads a block from compressed file data.</summary>
    public static Block? Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var json = Compress.DecompressString(bytes);
        return JsonSerializer.Deserialize<Block>(json);
    }

    /// <summary>Returns a formatted JSON representation of the block.</summary>
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
        var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        return JsonSerializer.Serialize(info, options);
    }
}
