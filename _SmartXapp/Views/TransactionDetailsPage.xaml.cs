using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class TransactionDetailsPage : ContentPage
    {
        public TransactionDetailsPage(string transactionId)
        {
            InitializeComponent();
            BindingContext = new TransactionDetailsViewModel(transactionId);
        }
    }
}
