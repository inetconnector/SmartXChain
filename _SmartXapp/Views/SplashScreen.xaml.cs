using System.Threading.Tasks;
using Xamarin.Forms;

namespace XamarinBlockchainApp.Views
{
    public partial class SplashScreen : ContentPage
    {
        public SplashScreen()
        {
            InitializeComponent();
            StartAnimation();
        }

        private async void StartAnimation()
        {
            // Fade-in animation for the logo
            await LogoImage.FadeTo(1, 2000); // Duration: 2 seconds

            // Wait and navigate to the main page
            await Task.Delay(1000);
            Application.Current.MainPage = new NavigationPage(new LoginPage());
        }
    }
}
