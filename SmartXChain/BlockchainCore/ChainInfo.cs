using System.Text.Json; 
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore
{
    /// <summary>
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
        public string NodeAddress { get; set; } = string.Empty; 

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

        public override string ToString()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        }

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
}