using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class BlockchainPage : ContentPage
    {
        public BlockchainPage()
        {
            InitializeComponent();
            BindingContext = new BlockchainViewModel();
        }
    }
}
