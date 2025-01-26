
using System.Windows.Input;
using Xamarin.Forms;

namespace XamarinBlockchainApp.ViewModels
{
    public class WithdrawViewModel : BindableObject
    {
        private string _walletAddress;
        private string _amount;

        public string WalletAddress
        {
            get => _walletAddress;
            set
            {
                _walletAddress = value;
                OnPropertyChanged();
            }
        }

        public string Amount
        {
            get => _amount;
            set
            {
                _amount = value;
                OnPropertyChanged();
            }
        }

        public ICommand WithdrawCommand { get; }

        public WithdrawViewModel()
        {
            WithdrawCommand = new Command(OnWithdraw);
        }

        private async void OnWithdraw()
        {
            if (string.IsNullOrWhiteSpace(WalletAddress) || string.IsNullOrWhiteSpace(Amount))
            {
                await Application.Current.MainPage.DisplayAlert("Error", "Wallet address and amount are required.", "OK");
                return;
            }

            if (!decimal.TryParse(Amount, out var parsedAmount) || parsedAmount <= 0)
            {
                await Application.Current.MainPage.DisplayAlert("Error", "Invalid amount.", "OK");
                return;
            }

            await Application.Current.MainPage.DisplayAlert("Success", $"Withdrawal of {Amount} SC3 initiated!", "OK");
        }
    }
}
