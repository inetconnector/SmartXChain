using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartXChain.Utils;

public static class Functions
{
    /// <summary>
    ///     Restarts the application
    /// </summary>
    public static void RestartApplication()
    {
        var executablePath = Process.GetCurrentProcess().MainModule.FileName;

        // Prepare the arguments
        var arguments = Config.TestNet ? "/testnet" : string.Empty;

        // Start the process with the arguments
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false
        });

        // Exit the current process
        Environment.Exit(0);
    }


    /// <summary>
    ///     Generates a new short GUID as a string.
    /// </summary>
    /// <returns>A short GUID string without dashes or braces, in uppercase.</returns>
    public static string NewGuid()
    {
        return Guid.NewGuid().ToString().ToUpper().Replace("-", "").Replace("{", "").Replace("}", "");
    }

    public static string AllowOnlyAlphanumeric(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var regex = new Regex("[^a-zA-Z0-9]");
        return regex.Replace(input, "");
    }
}