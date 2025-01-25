using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartXChain.Utils;
using System.Threading.Tasks;
using System.Net.Http;

/// ---------BEGIN BASE CLASSES----------
public class Contract:Authenticate
{  
    [JsonInclude] public string Name { get; protected set; }
   
    private readonly Dictionary<string, List<(string RpcUrl, string Owner)>> _eventSubscriptions;

    public Contract()
    {
        _eventSubscriptions = new Dictionary<string, List<(string RpcUrl, string Owner)>>();
    }

    public void RegisterHandler(string eventName, string rpcUrl, string owner)
    {
        if (!_eventSubscriptions.ContainsKey(eventName))
            _eventSubscriptions[eventName] = new List<(string RpcUrl, string Owner)>();

        _eventSubscriptions[eventName].Add((rpcUrl, owner));
        Log($"Registered {rpcUrl} for event {eventName} by owner {owner}");
    }

    public void UnregisterHandler(string eventName, string rpcUrl, string requester)
    {
        if (_eventSubscriptions.ContainsKey(eventName))
        {
            var subscription = _eventSubscriptions[eventName].Find(s => s.RpcUrl == rpcUrl);
            if (subscription.Owner == requester)
            {
                _eventSubscriptions[eventName].Remove(subscription);
                Log($"Unregistered {rpcUrl} from event {eventName} by owner {requester}");
            }
            else
            {
                Log($"Unregister failed: Only the owner ({subscription.Owner}) can unregister this URL.");
            }
        }
    }

    public async Task TriggerHandlers(string eventName, string eventData, string bearerToken="")
    {
        if (_eventSubscriptions.ContainsKey(eventName))
            foreach (var (url, _) in _eventSubscriptions[eventName])
                await SendEvent(url, eventData, bearerToken);
    }


    private async Task SendEvent(string url, string data, string bearerToken = "")
    {
        try
        {
            using var client = new HttpClient();
            if (bearerToken!="")
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);
            var content = new StringContent(data, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            Log($"Sent event to {url}, response: {response.StatusCode}",false);
        }
        catch (Exception ex)
        {
            LogException(ex, $"Send failed: {url}");
        }
    }

    public bool IsValidAddress(string address)
    {
        return !string.IsNullOrWhiteSpace(address) && address.Length >= 5 && address.StartsWith("smartX");
    }

    private string HashKey(string key)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hashedBytes);
    }

    public static string SerializeToBase64<T>(T instance)
    {
        var json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });

        using (var memoryStream = new MemoryStream())
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
            gzipStream.Close();
            return Convert.ToBase64String(memoryStream.ToArray());
        }
    }

    public static T DeserializeFromBase64<T>(string base64Data) where T : class
    {
        var compressedData = Convert.FromBase64String(base64Data);

        using (var memoryStream = new MemoryStream(compressedData))
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
        {
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<T>(json);
        }
    }

    public void Log(string message = "", bool trim = true)
    {
        Logger.Log($"[{Name}] " + message);
    }

    public void LogException(Exception ex, string message = "", bool trim = false)
    {
        Logger.LogException(ex, $"[{Name}] {message}");
    } 
}
/// <summary>
///     Authentication for Contracts
/// </summary>
public class Authenticate : Logger
{
    public Authenticate()
    {
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
        AuthenticatedUsers = new Dictionary<string, string>(); 
        DeploymentDate = DateTime.UtcNow; 
    }

    /// <summary>
    ///     Contract owner
    /// </summary>
    [JsonInclude]
    public string Owner { get; protected internal set; }

    /// <summary>
    ///     Version of the contract.
    /// </summary>
    [JsonInclude]
    public string Version { get; private protected set; }

    /// <summary>
    ///     Timestamp of the contract deployment.
    /// </summary>
    [JsonInclude]
    public DateTime DeploymentDate { get; private protected set; }

    /// <summary>
    ///     Dictionary storing authenticated users and their hashed private keys.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, string> AuthenticatedUsers { get; private protected set; }

    /// <summary>
    ///     Registers a new user by linking their address with a hashed private key.
    /// </summary>
    /// <param name="address">The address of the user to register.</param>
    /// <param name="privateKey">The private key to authenticate the user.</param>
    /// <returns>True if registration is successful; otherwise, false.</returns>
    public bool RegisterUser(string address, string privateKey)
    {
        if (!IsValidAddress(address))
        {
            Log("Registration failed: Invalid address format.");
            return false;
        }

        if (AuthenticatedUsers.ContainsKey(address))
        {
            Log($"Registration failed: Address '{address}' is already registered.");
            return false;
        }

        AuthenticatedUsers[address] = HashKey(privateKey);
        Log($"User {address} registered successfully.");
        return true;
    }

    /// <summary>
    ///     Validates the format of an address.
    /// </summary>
    /// <param name="address">The address to validate.</param>
    /// <returns>True if the address is valid; otherwise, false.</returns>
    private bool IsValidAddress(string address)
    {
        return !string.IsNullOrWhiteSpace(address) && address.Length >= 5 && address.StartsWith("smartX");
    }

    /// <summary>
    ///     Hashes a private key using SHA-256.
    /// </summary>
    /// <param name="key">The key to hash.</param>
    /// <returns>The hashed key as a base64 string.</returns>
    private string HashKey(string key)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Convert.ToBase64String(hashedBytes);
        }
    } 

    /// <summary>
    ///     Authenticates a user by comparing their hashed private key with the stored hash.
    /// </summary>
    /// <param name="address">The user's address.</param>
    /// <param name="privateKey">The private key for authentication.</param>
    /// <returns>True if authentication is successful; otherwise, false.</returns>
    protected bool IsAuthenticated(string address, string privateKey)
    {
        var hashedKey = HashKey(privateKey);
        return AuthenticatedUsers.ContainsKey(address) && AuthenticatedUsers[address] == hashedKey;
    }
}

/// <summary>
///     Logger class
/// </summary>
public class Logger
{
    public static event Action<string>? OnLog;
    /// <summary>
    ///     Logs a message to the console with a timestamp, excluding specific messages based on predefined filters.
    /// </summary>
    /// <param name="message">The message to log. Defaults to an empty string.</param>
    /// <param name="trim">Trims output to 110 chars</param>
    public static void Log(string message = "", bool trim = true)
    {
        // Format the message with a timestamp
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var formattedMessage = $"{timestamp} - {message}";

        formattedMessage= formattedMessage.Replace("smartX0000000000000000000000000000000000000000","System")
                                          .Replace("smartXFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF","Offline");

        // Print the message to the console, truncating if it exceeds 100 characters
        if (formattedMessage.Length > 110 && trim)
            Console.WriteLine(formattedMessage.Substring(0, 110) + "...");
        else
            Console.WriteLine(formattedMessage);

        OnLog?.Invoke(formattedMessage);
    }

    /// <summary>
    ///     Logs an Error message to the console with a timestamp, excluding specific messages based on predefined filters.
    /// </summary>
    /// <param name="message">The message to log. Defaults to an empty string.</param>
    /// <param name="trim">Trims output to 110 chars</param>
    public static void LogError(string message = "", bool trim = true)
    {
        Log("[Error]: " +message,trim);
    }

    /// <summary>
    ///     Logs an Error message to the console with a timestamp, excluding specific messages based on predefined filters.
    /// </summary>
    /// <param name="message">The message to log. Defaults to an empty string.</param>
    /// <param name="trim">Trims output to 110 chars</param>
    public static void LogWarning(string message = "", bool trim = true)
    {
        Log("[Warning]: " + message, trim);
    }

    /// <summary>
    /// Logs a message to the console with a line
    /// </summary>
    /// <param name="message">message to log</param>
    /// <param name="totalWidth">total with of the line. Maximum is 200</param>
    public static void LogLine(string message="", int totalWidth=80)
    {
        if (totalWidth > 200)
#if ANDROID
            totalWidth = 80;
#else            
            totalWidth = 200;
#endif

            // Trim the message to handle cases with only whitespace
            message = message.Trim();

        if (string.IsNullOrEmpty(message))
        {
            // If the message is empty or whitespace, return a full line of dashes
            Console.WriteLine(new string('-', totalWidth));
        }
        else
        {
            int messageLength = message.Length;

            if (messageLength >= totalWidth - 2)
            {
                // If the message is too long, truncate it and add ellipsis
                message = message.Substring(0, totalWidth - 5) + "...";
                messageLength = message.Length;
            }

            int padding = (totalWidth - messageLength - 2) / 2; // Calculate padding
            string line = new string('-', padding) + " " + message.ToUpper() + " " + new string('-', totalWidth - messageLength - padding - 2);

            Logger.Log();
            Logger.Log(line,false);
        }
    }


    /// <summary>
    ///     Logs an error message to the console with a timestamp, excluding specific messages based on predefined filters.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="ex"></param>
    public static void LogException(Exception ex, string message="")
    {
        var prefix = "[ERROR] ";
        // Check if the message starts with "error" or "ERROR:" and modify accordingly
        if (message.StartsWith("error:", StringComparison.OrdinalIgnoreCase) || message.StartsWith("ERROR:"))
            message = prefix + message.Substring(message.IndexOf(":") + 1).Trim();

        // Handle exceptions during message processing
        Log($"{message}");
        Log($"{prefix}{ex.Message}", false);

        if (ex.InnerException != null)
        {
            Log($"{prefix}{ex.InnerException.Message}", false);
            if (ex.InnerException.InnerException != null)
                Log($"{prefix}{ex.InnerException.InnerException.Message}", false);
        }
    }
}
/// ---------END BASE CLASSES----------