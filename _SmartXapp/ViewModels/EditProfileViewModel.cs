
using System.Windows.Input;
using Xamarin.Forms;

namespace XamarinBlockchainApp.ViewModels
{
    public class EditProfileViewModel : BindableObject
    {
        private string _firstName;
        private string _lastName;
        private string _email;

        public string FirstName
        {
            get => _firstName;
            set
            {
                _firstName = value;
                OnPropertyChanged();
            }
        }

        public string LastName
        {
            get => _lastName;
            set
            {
                _lastName = value;
                OnPropertyChanged();
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
            }
        }

        public ICommand UpdateCommand { get; }

        public EditProfileViewModel()
        {
            FirstName = "Daniel";
            LastName = "Frede";
            Email = "frede@inetconnector.com";

            UpdateCommand = new Command(OnUpdateProfile);
        }

        private async void OnUpdateProfile()
        {
            if (string.IsNullOrWhiteSpace(FirstName) || string.IsNullOrWhiteSpace(LastName) || string.IsNullOrWhiteSpace(Email))
            {
                await Application.Current.MainPage.DisplayAlert("Error", "All fields are required.", "OK");
                return;
            }

            await Application.Current.MainPage.DisplayAlert("Success", "Profile updated successfully!", "OK");
        }
    }
}
