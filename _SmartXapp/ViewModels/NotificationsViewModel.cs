using System.Collections.ObjectModel;
using Xamarin.Forms;
using XamarinBlockchainApp.Services;

namespace XamarinBlockchainApp.ViewModels
{
    public class NotificationsViewModel : BindableObject
    {
        public ObservableCollection<string> Notifications { get; }

        public NotificationsViewModel()
        {
            // Fetch notifications from NotificationService
            var service = new NotificationService();
            Notifications = new ObservableCollection<string>(service.GetNotifications());
        }
    }
}
