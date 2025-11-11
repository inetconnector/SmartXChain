using System.Text.Json;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     ChainInfo object which is exchanged between nodes
/// </summary>
public class ChainInfo
{
    /// <summary>
    ///     Gets or sets the public key identifying the node.
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the fingerprint of the node's contract DLL.
    /// </summary>
    public string DllFingerprint { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the identifier of the blockchain.
    /// </summary>
    public string ChainID { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the number of blocks known in the chain.
    /// </summary>
    public int BlockCount { get; set; }

    /// <summary>
    ///     Gets or sets the message associated with the chain information payload.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the hash of the first block in the chain.
    /// </summary>
    public string FirstHash { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the hash of the most recent block in the chain.
    /// </summary>
    public string LastHash { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the timestamp of the last known block.
    /// </summary>
    public DateTime LastDate { get; set; } = DateTime.MaxValue;

    /// <summary>
    ///     Gets or sets the network address of the reporting node.
    /// </summary>
    public string NodeAddress { get; set; } = string.Empty;

    /// <summary>
    ///     Creates a <see cref="ChainInfo" /> instance representing the supplied blockchain.
    /// </summary>
    /// <param name="blockchain">The blockchain to describe.</param>
    /// <param name="message">An optional status message included with the info.</param>
    /// <returns>A populated <see cref="ChainInfo" /> instance.</returns>
    internal static ChainInfo CreateChainInfo(Blockchain blockchain, string message = "ChainInfo")
    {
        return new ChainInfo
        {
            Message = message,
            ChainID = blockchain.ChainId,
            BlockCount = blockchain.Chain.Count,
            LastHash = blockchain.Chain.Last().Hash,
            FirstHash = blockchain.Chain.First().Hash,
            LastDate = blockchain.Chain.Last().Timestamp,
            NodeAddress = Config.Default.NodeAddress
        };
    }

    /// <summary>
    ///     Serializes the chain information to a formatted JSON string.
    /// </summary>
    /// <returns>A JSON representation of the current chain information.</returns>
    public override string ToString()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    ///     Creates a <see cref="ChainInfo" /> instance from a JSON payload.
    /// </summary>
    /// <param name="json">The JSON representation of the chain info.</param>
    /// <returns>The deserialized <see cref="ChainInfo" /> instance.</returns>
    public static ChainInfo FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ChainInfo>(json) ?? new ChainInfo();
        }
        catch (JsonException)
        {
            return new ChainInfo(); // Return an empty object if the JSON is invalid
        }
    }
}