using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class HomePage : ContentPage
    {
        public HomePage()
        {
            InitializeComponent();
            BindingContext = new HomeViewModel();
        }
    }
}
