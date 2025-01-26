using System;
using System.Threading.Tasks;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;
using XamarinBlockchainApp.Database;

namespace XamarinBlockchainApp.Services
{
    public static class BlockchainService
    {
        public static async Task StartBlockchain()
        {
            try
            {
                // Load configuration dynamically
                var config = Config.Default;
                var blockchainPath = config.Get(Config.ConfigKey.BlockchainPath);
                var chainId = config.Get(Config.ConfigKey.ChainId);
                var minerAddress = config.Get(Config.ConfigKey.MinerAddress);

                Console.WriteLine($"Initializing Blockchain with ChainId: {chainId}, MinerAddress: {minerAddress}");

                // Start the blockchain node
                using (var node = BlockchainServer.NodeStartupResult.Create(blockchainPath, chainId, minerAddress))
                {
                    if (node == null)
                    {
                        throw new Exception("Failed to initialize blockchain node.");
                    }
                }

                Console.WriteLine("Blockchain initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing blockchain: {ex.Message}");
            }
        }
    }
}
