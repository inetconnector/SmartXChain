using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class QRCodePage : ContentPage
    {
        public QRCodePage()
        {
            InitializeComponent();
            BindingContext = new QRCodeViewModel();
        }
    }
}
