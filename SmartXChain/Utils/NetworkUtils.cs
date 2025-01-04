using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SmartXChain.Utils;

/// <summary>
///     Provides utility methods for network-related operations such as IP validation and retrieval.
/// </summary>
public class NetworkUtils
{
    /// <summary>
    ///     Stores the current IP address of the network.
    /// </summary>
    public static string IP { get; set; }

    /// <summary>
    ///     Validates whether a given string is a valid IP address.
    /// </summary>
    /// <param name="ipString">The IP address string to validate.</param>
    /// <returns>True if the string is a valid IP address; otherwise, false.</returns>
    public static bool IsValidIP(string ipString)
    {
        if (string.IsNullOrWhiteSpace(ipString))
            return false;

        return IPAddress.TryParse(ipString, out _);
    }

    /// <summary>
    ///     Retrieves the local IPv4 address of the current machine.
    /// </summary>
    /// <returns>The local IPv4 address as a string.</returns>
    /// <exception cref="Exception">Thrown if no IPv4 address is found.</exception>
    public static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());

        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();

        throw new Exception("No IPv4 address found.");
    }

    /// <summary>
    ///     Retrieves the public IP address of the current network using external IP services.
    /// </summary>
    /// <param name="customIpServices">An optional list of custom IP service URLs.</param>
    /// <param name="debug">If true, logs debug messages during the operation.</param>
    /// <returns>A Task that resolves to the public IP address as a string.</returns>
    /// <exception cref="Exception">Thrown if unable to retrieve the public IP address.</exception>
    public static async Task<string> GetPublicIPAsync(IEnumerable<string> customIpServices = null, bool debug = false)
    {
        // Default list of public IP services for fallback
        var defaultIpServiceUrls = new[]
        {
            "https://api.ipify.org",
            "https://checkip.amazonaws.com",
            "https://ifconfig.me/ip",
            "https://icanhazip.com"
        };

        // Combine custom services (if any) with the default list
        var ipServiceUrls = customIpServices?.Any() ?? false
            ? customIpServices.Concat(defaultIpServiceUrls)
            : defaultIpServiceUrls;

        foreach (var ipServiceUrl in ipServiceUrls)
            try
            {
                using (var httpClient = new HttpClient())
                {
                    if (debug)
                        Logger.Log($"Trying {ipServiceUrl}...");

                    // Attempt to retrieve the IP address
                    var publicIP = await httpClient.GetStringAsync(ipServiceUrl);

                    // Trim to ensure no extra whitespace
                    publicIP = publicIP.Trim();

                    Logger.Log($"Public IP Address retrieved: {publicIP}");
                    IP = publicIP;
                    return publicIP;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to retrieve IP from {ipServiceUrl}: {ex.Message}");
            }

        // If all services fail, throw an exception
        throw new Exception("Unable to retrieve the public IP address. All services (custom and fallback) failed.");
    }

    /// <summary>
    ///     Validates whether a given address matches the SmartXChain format.
    /// </summary>
    /// <param name="address">The address string to validate.</param>
    /// <returns>True if the address matches the format; otherwise, false.</returns>
    public static bool ValidateAddress(string address)
    {
        // Address must start with "smartX" followed by exactly 40 alphanumeric characters
        return Regex.IsMatch(address, "^smartX[a-fA-F0-9]{40}$");
    }

    /// <summary>
    ///     Validates whether a given address matches url i.e. http://10.1.2.3:5555.
    /// </summary>
    /// <param name="httpIPPort">The address string to validate.</param>
    /// <returns>True if the address matches the format; otherwise, false.</returns>
    public static bool IsValidServer(string httpIPPort)
    {
        if (string.IsNullOrWhiteSpace(httpIPPort)) return false;

        // Regex pattern for validating a server URL
        var pattern = @"^(http|https):\/\/(?:(?:\d{1,3}\.){3}\d{1,3}|(?:[a-zA-Z0-9-]+\.)+[a-zA-Z]{2,})(?::\d{1,5})?$";

        // Match the input string against the regex pattern
        return Regex.IsMatch(httpIPPort, pattern, RegexOptions.IgnoreCase);
    }
}