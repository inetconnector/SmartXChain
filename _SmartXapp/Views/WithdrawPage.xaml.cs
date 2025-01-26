using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class WithdrawPage : ContentPage
    {
        public WithdrawPage()
        {
            InitializeComponent();
            BindingContext = new WithdrawViewModel();
        }
    }
}
