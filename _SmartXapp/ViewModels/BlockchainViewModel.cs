using System.Windows.Input;
using Xamarin.Forms;
using SmartXChain.BlockchainCore;

namespace XamarinBlockchainApp.ViewModels
{
    public class BlockchainViewModel : BindableObject
    {
        private string _commandInput;
        private string _statusMessage;

        public string CommandInput
        {
            get => _commandInput;
            set
            {
                _commandInput = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public ICommand ExecuteCommand { get; }

        public BlockchainViewModel()
        {
            ExecuteCommand = new Command(OnExecute);
        }

        private void OnExecute()
        {
            if (CommandInput == "2")
            {
                StartBlockchain();
            }
            else
            {
                StatusMessage = "Invalid command. Enter '2' to start the blockchain.";
            }
        }

        private void StartBlockchain()
        {
            try
            {
                // Example database path and chain ID
                string blockchainPath = "/path/to/blockchain";
                string chainId = "main";

                // Retrieve miner address and balance from SQLite
                string minerAddress = BlockchainStorage.GetMinerAddress(blockchainPath, chainId);
                decimal minerBalance = BlockchainStorage.GetMinerBalance(blockchainPath, chainId, minerAddress);

                // Log the miner details
                StatusMessage = $"Blockchain started! Miner Address: {minerAddress}, Balance: {minerBalance}";
                
                // Start the blockchain in the background
                Task.Run(() => BlockchainCore.StartBlockchain(blockchainPath, chainId, minerAddress));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error starting blockchain: {ex.Message}";
            }
        }
    }
}
