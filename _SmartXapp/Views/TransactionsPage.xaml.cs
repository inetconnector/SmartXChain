using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class TransactionsPage : ContentPage
    {
        public TransactionsPage()
        {
            InitializeComponent();
            BindingContext = new TransactionsViewModel();
        }
    }
}
