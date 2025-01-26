
using System.Windows.Input;
using Xamarin.Forms;

namespace XamarinBlockchainApp.ViewModels
{
    public class ThemeViewModel : BindableObject
    {
        public ICommand SwitchToLightThemeCommand { get; }
        public ICommand SwitchToDarkThemeCommand { get; }

        public ThemeViewModel()
        {
            SwitchToLightThemeCommand = new Command(SwitchToLightTheme);
            SwitchToDarkThemeCommand = new Command(SwitchToDarkTheme);
        }

        private void SwitchToLightTheme()
        {
            Application.Current.Resources["BackgroundColor"] = Color.White;
            Application.Current.Resources["TextColor"] = Color.Black;
        }

        private void SwitchToDarkTheme()
        {
            Application.Current.Resources["BackgroundColor"] = Color.Black;
            Application.Current.Resources["TextColor"] = Color.White;
        }
    }
}
