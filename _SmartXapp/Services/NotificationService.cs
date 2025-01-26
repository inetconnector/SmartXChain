using System.Collections.Generic;
using XamarinBlockchainApp.Resources;

namespace XamarinBlockchainApp.Services
{
    public class NotificationService
    {
        public IEnumerable<string> GetNotifications()
        {
            var notifications = new List<string>
            {
                string.Format(AppResources.NewTransaction, "SC3 2.5"),
                AppResources.PasswordUpdated
            };

            return notifications;
        }
    }
}
