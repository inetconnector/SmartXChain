using System;
using System.Collections.ObjectModel;
using Xamarin.Forms;
using SmartXChain.Utils;
using XamarinBlockchainApp.Services;

namespace XamarinBlockchainApp.ViewModels
{
    public class HomeViewModel : BindableObject
    {
        private readonly TransactionService _transactionService;
        private readonly string _minerAddress;
        private decimal _balance;

        public ObservableCollection<Transaction> Transactions { get; }
        public string MinerAddress => _minerAddress;

        public decimal Balance
        {
            get => _balance;
            set
            {
                _balance = value;
                OnPropertyChanged();
            }
        }

        public HomeViewModel()
        {
            var config = Config.Default;
            _minerAddress = config.Get(Config.ConfigKey.MinerAddress);

            _transactionService = new TransactionService();
            Transactions = new ObservableCollection<Transaction>(_transactionService.GetAllTransactions());

            LoadBalance();
        }

        private void LoadBalance()
        {
            try
            {
                // Fetch balance for miner address from blockchain
                Balance = _transactionService.GetBalanceForAddress(MinerAddress);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading balance: {ex.Message}");
                Balance = 0;
            }
        }
    }
}
