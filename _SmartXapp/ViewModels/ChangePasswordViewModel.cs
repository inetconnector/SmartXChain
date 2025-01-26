
using System.Windows.Input;
using Xamarin.Forms;

namespace XamarinBlockchainApp.ViewModels
{
    public class ChangePasswordViewModel : BindableObject
    {
        private string _currentPassword;
        private string _newPassword;
        private string _confirmPassword;

        public string CurrentPassword
        {
            get => _currentPassword;
            set
            {
                _currentPassword = value;
                OnPropertyChanged();
            }
        }

        public string NewPassword
        {
            get => _newPassword;
            set
            {
                _newPassword = value;
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

        public ICommand UpdatePasswordCommand { get; }

        public ChangePasswordViewModel()
        {
            UpdatePasswordCommand = new Command(OnUpdatePassword);
        }

        private async void OnUpdatePassword()
        {
            if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword) || string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                await Application.Current.MainPage.DisplayAlert("Error", "All fields are required.", "OK");
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                await Application.Current.MainPage.DisplayAlert("Error", "Passwords do not match.", "OK");
                return;
            }

            await Application.Current.MainPage.DisplayAlert("Success", "Password updated successfully!", "OK");
        }
    }
}
