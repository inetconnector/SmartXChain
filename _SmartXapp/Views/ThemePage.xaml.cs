using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class ThemePage : ContentPage
    {
        public ThemePage()
        {
            InitializeComponent();
            BindingContext = new ThemeViewModel();
        }
    }
}
