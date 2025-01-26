using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class NotificationsPage : ContentPage
    {
        public NotificationsPage()
        {
            InitializeComponent();
            BindingContext = new NotificationsViewModel();
        }
    }
}
