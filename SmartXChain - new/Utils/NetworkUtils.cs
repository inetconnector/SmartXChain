using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace SmartXChain.Utils;

public class NetworkUtils
{
    public static string IP { get; set; }

    public static bool IsValidIP(string ipString)
    {
        if (string.IsNullOrWhiteSpace(ipString))
            return false;

        return IPAddress.TryParse(ipString, out _);
    }

    public static string GetLocalIP()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());

        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip.ToString();
        throw new Exception("No IPv4 address found.");
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

    public static bool ValidateAddress(string address)
    {
        // Address must start with "smartX" followed by exactly 40 alphanumeric characters
        return Regex.IsMatch(address, "^smartX[a-fA-F0-9]{40}$");
    }
}