using Xamarin.Forms;
using XamarinBlockchainApp.ViewModels;

namespace XamarinBlockchainApp.Views
{
    public partial class EditProfilePage : ContentPage
    {
        public EditProfilePage()
        {
            InitializeComponent();
            BindingContext = new EditProfileViewModel();
        }
    }
}
