using System.IO;
using SQLite;

namespace XamarinBlockchainApp.Database
{
    public static class SQLiteHelper
    {
        private static readonly string DatabaseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        public static string GetDatabasePath(string databaseName)
        {
            return Path.Combine(DatabaseFolder, databaseName);
        }

        public static SQLiteConnection GetConnection(string databaseName)
        {
            var path = GetDatabasePath(databaseName);
            return new SQLiteConnection(path);
        }
    }
}
