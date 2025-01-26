using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class ChangePasswordPage : ContentPage
    {
        public ChangePasswordPage()
        {
            InitializeComponent();
            BindingContext = new ChangePasswordViewModel();
        }
    }
}
