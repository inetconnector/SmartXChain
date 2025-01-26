using Xamarin.Forms;
using SmartXChain.Utils;

namespace XamarinBlockchainApp.ViewModels
{
    public class SettingsViewModel : BindableObject
    {
        public string MinerAddress { get; }

        public SettingsViewModel()
        {
            // Load miner address from configuration
            var config = Config.Default;
            MinerAddress = config.Get(Config.ConfigKey.MinerAddress);
        }
    }
}
