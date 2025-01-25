using System.Net;
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

    public static string? ResolveUrlToIp(string url)
    {
        try
        {
            var uri = new Uri(url);

            var addresses = Dns.GetHostAddresses(uri.Host);

            if (addresses.Length > 0) return uri.Scheme + "://" + addresses[0] + ":" + uri.Port;
        }
        catch (Exception ex)
        {
        }

        return url;
    } 
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
                        Console.WriteLine($"Trying {ipServiceUrl}...");

                    // Attempt to retrieve the IP address
                    var publicIP = await httpClient.GetStringAsync(ipServiceUrl);

                    // Trim to ensure no extra whitespace
                    publicIP = publicIP.Trim();

                    Console.WriteLine($"\nPublic IP Address retrieved: {publicIP}");
                    IP = publicIP;
                    return publicIP;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to retrieve IP from {ipServiceUrl}: {ex.Message}");
            }

        // If all services fail, throw an exception
        throw new Exception("Unable to retrieve the public IP address. All services (custom and fallback) failed.");
    }
}