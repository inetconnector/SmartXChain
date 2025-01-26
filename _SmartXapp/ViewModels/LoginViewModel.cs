
using System.Windows.Input;
using Xamarin.Forms;

namespace XamarinBlockchainApp.ViewModels
{
    public class LoginViewModel : BindableObject
    {
        private string _email;
        private string _password;

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
            }
        }

        public ICommand LoginCommand { get; }
        public ICommand NavigateToRegisterCommand { get; }

        public LoginViewModel()
        {
            LoginCommand = new Command(OnLogin);
            NavigateToRegisterCommand = new Command(OnNavigateToRegister);
        }

        private async void OnLogin()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                await Application.Current.MainPage.DisplayAlert("Error", "Email and password are required.", "OK");
                return;
            }

            if (Email == "test@example.com" && Password == "password")
            {
                await Application.Current.MainPage.Navigation.PushAsync(new Views.HomePage());
            }
            else
            {
                await Application.Current.MainPage.DisplayAlert("Error", "Invalid credentials.", "OK");
            }
        }

        private async void OnNavigateToRegister()
        {
            await Application.Current.MainPage.Navigation.PushAsync(new Views.RegisterPage());
        }
    }
}
