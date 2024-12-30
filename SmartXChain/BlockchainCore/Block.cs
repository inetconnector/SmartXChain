using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartXChain.Contracts;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class Block
{
    public Block(List<Transaction> transactions, string previousHash)
    {
        if (Timestamp==DateTime.MinValue)
            Timestamp = DateTime.UtcNow;

        Transactions = transactions;
        PreviousHash = previousHash;
        Hash = CalculateHash();
        SmartContracts = new List<SmartContract>();
    }

    [JsonInclude] public DateTime Timestamp { get; } = DateTime.MinValue;
    [JsonInclude] public List<Transaction> Transactions { get; }
    [JsonInclude] public string PreviousHash { get; set; }
    [JsonInclude] public string Hash { get; private set; }
    [JsonInclude] public int Nonce { get; private set; } 
    [JsonInclude] public List<SmartContract> SmartContracts { get; private set; } 
    [JsonIgnore] public string Base64Encoded => Convert.ToBase64String(GetBytes());

    public string CalculateHash()
    {
        using var sha256 = SHA256.Create();
        var rawData = $"{string.Join(",", Transactions)}-{PreviousHash}-{Nonce}";
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        Hash = Convert.ToBase64String(bytes);
        return Hash;
    }

    public void Mine(int difficulty)
    {
        var hashPrefix = new string('0', difficulty);
        while (!Hash.StartsWith(hashPrefix))
        {
            Nonce++;
            Hash = CalculateHash();
        }

        Logger.LogMessage($"Block mined: {Hash}");
    }

    public string[] GetDiscoveryServers()
    {
        return Transactions
            .Where(t => t.Data.StartsWith("RegisterDiscoveryServer"))
            .Select(t => t.Data.Split(':')[1])
            .ToArray();
    }

    public byte[] GetBytes()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var jsonString = JsonSerializer.Serialize(this, options);
        var compressedData = Compress.CompressString(jsonString);
        return compressedData;
    }

    public string ToBase64()
    {
        return Convert.ToBase64String(GetBytes());
    }

    public static Block? FromBase64(string base64String)
    {
        var compressedData = Convert.FromBase64String(base64String);
        var jsonString = Compress.DecompressString(compressedData);
        var block = JsonSerializer.Deserialize<Block>(jsonString); 
        return block;
    }

    // Save to compressed file
    public void Save(string filePath)
    {
        File.WriteAllBytes(filePath, GetBytes());
        Logger.LogMessage("Block saved (compressed) to file.");
    }

    // Load from compressed file
    public static Block? Load(string filePath)
    {
        var compressedData = File.ReadAllBytes(filePath);
        var jsonString = Compress.DecompressString(compressedData);
        var block = JsonSerializer.Deserialize<Block>(jsonString);
        return block;
    }

    public bool Verify(string base64BlockMessage)
    {
        const string prefix = "Vote:";
        if (!base64BlockMessage.StartsWith(prefix))
        {
            Logger.LogMessage("Invalid base64BlockMessage received.");
            return false;
        }

        try
        {
            var base64 = base64BlockMessage.Substring(prefix.Length);
            var block = FromBase64(base64);
            if (block != null)
            {
                var hash = block.Hash;
                if (block.CalculateHash() == hash) return true;
            }
        }
        catch (Exception e)
        {
            Logger.LogMessage($"Invalid base64BlockMessage. {e.Message}");
        }

        return false;
    }
}