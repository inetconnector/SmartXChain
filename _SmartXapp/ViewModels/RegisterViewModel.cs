
using System.Windows.Input;
using Xamarin.Forms;

namespace XamarinBlockchainApp.ViewModels
{
    public class RegisterViewModel : BindableObject
    {
        private string _email;
        private string _password;
        private string _confirmPassword;

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

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                _confirmPassword = value;
                OnPropertyChanged();
            }
        }

        public ICommand RegisterCommand { get; }

        public RegisterViewModel()
        {
            RegisterCommand = new Command(OnRegister);
        }

        private async void OnRegister()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                await Application.Current.MainPage.DisplayAlert("Error", "All fields are required.", "OK");
                return;
            }

            if (Password != ConfirmPassword)
            {
                await Application.Current.MainPage.DisplayAlert("Error", "Passwords do not match.", "OK");
                return;
            }

            await Application.Current.MainPage.DisplayAlert("Success", "Account created successfully!", "OK");
            await Application.Current.MainPage.Navigation.PopAsync();
        }
    }
}
