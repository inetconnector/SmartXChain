using Xamarin.Forms;
namespace XamarinBlockchainApp.ViewModels
{
    public class QRCodeViewModel : BindableObject
    {
        private string _walletAddress;

        public string WalletAddress
        {
            get => _walletAddress;
            set
            {
                _walletAddress = value;
                OnPropertyChanged();
            }
        }

        public QRCodeViewModel()
        {
            // Dynamically load the miner address from Config
            var config = SmartXChain.Utils.Config.Default;
            WalletAddress = config.Get(SmartXChain.Utils.Config.ConfigKey.MinerAddress);
        }
    }
}
