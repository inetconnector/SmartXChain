namespace SmartXChain.BlockchainCore;

// <summary>
/// ChainInfo object which is exchanged between nodes
/// </summary>
public class ChainInfo
{
    public string PublicKey { get; set; } = string.Empty;
    public string DllFingerprint { get; set; } = string.Empty;
    public string ChainID { get; set; } = string.Empty;
    public int BlockCount { get; set; } = 0;
    public string Message { get; set; } = string.Empty;
    public string FirstHash { get; set; } = string.Empty;
    public string LastHash { get; set; } = string.Empty;
    public DateTime LastDate { get; set; } = DateTime.MaxValue;
    public string URL { get; set; } = string.Empty;
}