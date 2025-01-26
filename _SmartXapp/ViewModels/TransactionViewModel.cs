using System;
using System.Threading.Tasks;
using Xamarin.Forms;
using ZXing.Net.Mobile.Forms;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace XamarinBlockchainApp.ViewModels
{
    public class TransactionViewModel : BindableObject
    {
        private string _senderAddress;
        private string _recipientAddress;
        private decimal _amount;
        private string _statusMessage;

        public string SenderAddress
        {
            get => _senderAddress;
            set
            {
                _senderAddress = value;
                OnPropertyChanged();
            }
        }

        public string RecipientAddress
        {
            get => _recipientAddress;
            set
            {
                _recipientAddress = value;
                OnPropertyChanged();
            }
        }

        public decimal Amount
        {
            get => _amount;
            set
            {
                _amount = value;
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

        public Command SendTransactionCommand { get; }
        public Command ScanRecipientCommand { get; }

        public TransactionViewModel()
        {
            var config = Config.Default;
            SenderAddress = config.Get(Config.ConfigKey.MinerAddress);

            SendTransactionCommand = new Command(async () => await SendTransaction());
            ScanRecipientCommand = new Command(async () => await ScanRecipient());
        }

        private async Task SendTransaction()
        {
            try
            {
                var blockchainPath = Config.Default.Get(Config.ConfigKey.BlockchainPath);
                var chainId = Config.Default.Get(Config.ConfigKey.ChainId);

                using (var node = BlockchainServer.NodeStartupResult.Create(blockchainPath, chainId, SenderAddress))
                {
                    var txResult = node.SendNativeSCX(SenderAddress, RecipientAddress, Amount);

                    if (!txResult.IsSuccess)
                    {
                        throw new Exception($"Transaction failed: {txResult.ErrorMessage}");
                    }

                    StatusMessage = $"Transaction successful! Hash: {txResult.TransactionHash}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task ScanRecipient()
        {
            try
            {
                var scannerPage = new ZXingScannerPage();
                scannerPage.OnScanResult += (result) =>
                {
                    scannerPage.IsScanning = false;

                    Device.BeginInvokeOnMainThread(() =>
                    {
                        Application.Current.MainPage.Navigation.PopAsync();
                        RecipientAddress = result.Text;
                        StatusMessage = "Recipient address scanned successfully.";
                    });
                };

                await Application.Current.MainPage.Navigation.PushAsync(scannerPage);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning QR Code: {ex.Message}";
            }
        }
    }
}
