
using System.Collections.ObjectModel;

namespace XamarinBlockchainApp.ViewModels
{
    public class TransactionsViewModel : BindableObject
    {
        public ObservableCollection<Transaction> Transactions { get; }

        public TransactionsViewModel()
        {
            Transactions = new ObservableCollection<Transaction>
            {
                new Transaction { Admin = "Admin", Date = "Dec 18 2024, 11:27 AM", Amount = "+ SC3 3" },
                new Transaction { Admin = "User1", Date = "Dec 19 2024, 02:15 PM", Amount = "- SC3 1.2" }
            };
        }
    }

    public class Transaction
    {
        public string Admin { get; set; }
        public string Date { get; set; }
        public string Amount { get; set; }
    }
}
