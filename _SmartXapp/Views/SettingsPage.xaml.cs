using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage()
        {
            InitializeComponent();
            BindingContext = new SettingsViewModel();
        }
    }
}
