using System;
using Xamarin.Forms;
using SmartXChain.Utils;
using XamarinBlockchainApp.Services;

namespace XamarinBlockchainApp.ViewModels
{
    public class TransactionDetailsViewModel : BindableObject
    {
        private Transaction _transaction;

        public Transaction Transaction
        {
            get => _transaction;
            set
            {
                _transaction = value;
                OnPropertyChanged();
            }
        }

        public TransactionDetailsViewModel(string transactionId)
        {
            // Fetch transaction details dynamically from TransactionService
            var transactionService = new TransactionService();
            Transaction = transactionService.GetTransactionById(transactionId);
        }
    }
}
