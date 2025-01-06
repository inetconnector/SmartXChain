using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

public class Contract
{
    private readonly Dictionary<string, string> _authenticatedUsers = new();
    private readonly Dictionary<string, List<(string RpcUrl, string Owner)>> _eventSubscriptions;

    public Contract()
    {
        _eventSubscriptions = new Dictionary<string, List<(string RpcUrl, string Owner)>>();
    }

    [JsonInclude] public string Name { get; protected set; }

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

    public async Task TriggerHandlers(string eventName, string eventData)
    {
        if (_eventSubscriptions.ContainsKey(eventName))
            foreach (var (url, _) in _eventSubscriptions[eventName])
                await SendEvent(url, eventData);
    }

    private async Task SendEvent(string url, string data)
    {
        using var client = new HttpClient();
        var content = new StringContent(data, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(url, content);

        Log($"Sent event to {url}, response: {response.StatusCode}");
    }

    public bool RegisterUser(string address, string privateKey)
    {
        if (!IsValidAddress(address)) return false;

        if (_authenticatedUsers.ContainsKey(address)) return false;

        _authenticatedUsers[address] = HashKey(privateKey);
        return true;
    }

    public bool IsAuthenticated(string address, string privateKey)
    {
        var hashedKey = HashKey(privateKey);
        return _authenticatedUsers.ContainsKey(address) && _authenticatedUsers[address] == hashedKey;
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
}

public class Logger
{
    public static void Log(string message = "", bool trim = true)
    {
        // Format the message with a timestamp
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var formattedMessage = timestamp + " - " + message;

        // Print the message to the console, truncating if it exceeds 100 characters
        if (formattedMessage.Length > 110 && trim)
            Console.WriteLine(formattedMessage.Substring(0, 110) + "...");
        else
            Console.WriteLine(formattedMessage);
    }
}