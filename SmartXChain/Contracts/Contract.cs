using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartXChain.Utils;

/// ---------BEGIN BASE CLASSES----------
public class Contract:Authenticate
{
    private readonly Dictionary<string, List<(string RpcUrl, string Owner)>> _eventSubscriptions;

    [JsonInclude] public string Name { get; protected set; }
     
    private async Task SendEvent(string url, string data)
    {
        try
        {
            using var client = new HttpClient();
            if (Config.Default.SSL)
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());
            var content = new StringContent(data, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            Log($"Sent event to {url}, response: {response.StatusCode}");
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
///     Configuration class for gas and reward parameters in a smart contract.
///     This class defines key values and rules for gas calculations and reward mechanisms.
/// </summary>
public class GasConfiguration : Authenticate
{
    public GasConfiguration(string owner)
    {
        Owner = owner;
    }
    /// <summary>
    ///     Enum to represent configurable gas and reward parameters.
    /// </summary>
    public enum GasConfigParameter
    {
        BaseGasTransaction,
        BaseGasContract,
        GasPerCharacter,
        MinerInitialReward,
        ValidatorInitialReward,
        MinerDecayFactor,
        ValidatorDecayFactor,
        MinerMinimumReward,
        ValidatorMinimumReward,
        GasFactor,
        CurrentNetworkLoadGT,
        CurrentNetworkLoadLT,
        CurrentNetworkLoadGTMultiply,
        CurrentNetworkLoadLTMultiply,
        ContractDataLengthMin,
        ContractDataLengthGasFactor
    }

    [JsonInclude] public decimal BaseGasTransaction { get; set; } = 5;
    // Base gas cost for a transaction.

    [JsonInclude] public decimal BaseGasContract { get; set; } = 10;
    // Base gas cost for a smart contract.

    [JsonInclude] public decimal GasPerCharacter { get; set; } = 2;
    // Gas cost per character in transmitted data.

    [JsonInclude] public decimal MinerInitialReward { get; set; } = 0.1m;
    // Initial reward for miners.

    [JsonInclude] public decimal ValidatorInitialReward { get; set; } = 0.05m;
    // Initial reward for validators.

    [JsonInclude] public decimal MinerDecayFactor { get; set; } = 0.98m;
    // Decay rate for miner rewards over time.

    [JsonInclude] public decimal ValidatorDecayFactor { get; set; } = 0.99m;
    // Decay rate for validator rewards over time.

    [JsonInclude] public decimal MinerMinimumReward { get; set; } = 0.01m;
    // Minimum reward for miners after decay.

    [JsonInclude] public decimal ValidatorMinimumReward { get; set; } = 0.005m;
    // Minimum reward for validators after decay.

    [JsonInclude] public decimal GasFactor { get; set; } = 1000;
    // Scaling factor to adjust gas calculations.

    [JsonInclude] public decimal CurrentNetworkLoadGT { get; set; } = 0.75m;
    // Threshold for high network load. If exceeded, additional gas is applied.

    [JsonInclude] public decimal CurrentNetworkLoadLT { get; set; } = 0.25m;
    // Threshold for low network load. If below, reduced gas is applied.

    [JsonInclude] public decimal CurrentNetworkLoadGTMultiply { get; set; } = 1.2m;
    // Multiplier for gas calculations during high network load.

    [JsonInclude] public decimal CurrentNetworkLoadLTMultiply { get; set; } = 0.8m;
    // Multiplier for gas calculations during low network load.

    [JsonInclude] public decimal ContractDataLengthMin { get; set; } = 1000;
    // Minimum data length threshold for contracts.

    [JsonInclude] public decimal ContractDataLengthGasFactor { get; set; } = 0.8m;
    // Gas adjustment factor based on contract data length.

    /// <summary>
    ///     Prints the current gas and reward configuration to the console.
    /// </summary>
    /// <summary>
    ///     Overrides the ToString method to provide a description of all constants and their functions.
    /// </summary>
    /// <returns>A string describing the constants and their roles in the gas and reward calculation.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Gas and Reward Configuration:");
        sb.AppendLine($"BaseGasTransaction: {BaseGasTransaction} - Base gas consumption for a transaction.");
        sb.AppendLine($"BaseGasContract: {BaseGasContract} - Base gas consumption for a smart contract.");
        sb.AppendLine($"GasPerCharacter: {GasPerCharacter} - Gas consumption per character in data.");
        sb.AppendLine($"MinerInitialReward: {MinerInitialReward} - Initial reward for miners.");
        sb.AppendLine($"ValidatorInitialReward: {ValidatorInitialReward} - Initial reward for validators.");
        sb.AppendLine($"MinerDecayFactor: {MinerDecayFactor} - Decay rate for miner rewards over time.");
        sb.AppendLine(
            $"ValidatorDecayFactor: {ValidatorDecayFactor} - Decay rate for validator rewards over time.");
        sb.AppendLine($"MinerMinimumReward: {MinerMinimumReward} - Minimum reward for miners.");
        sb.AppendLine($"ValidatorMinimumReward: {ValidatorMinimumReward} - Minimum reward for validators.");
        sb.AppendLine($"GasFactor: {GasFactor} - Scaling factor to adjust gas calculations.");
        sb.AppendLine($"CurrentNetworkLoadGT: {CurrentNetworkLoadGT} - Threshold for high network load.");
        sb.AppendLine($"CurrentNetworkLoadLT: {CurrentNetworkLoadLT} - Threshold for low network load.");
        sb.AppendLine(
            $"CurrentNetworkLoadGTMultiply: {CurrentNetworkLoadGTMultiply} - Multiplier for gas during high network load.");
        sb.AppendLine(
            $"CurrentNetworkLoadLTMultiply: {CurrentNetworkLoadLTMultiply} - Multiplier for gas during low network load.");
        sb.AppendLine(
            $"ContractDataLengthMin: {ContractDataLengthMin} - Minimum data length threshold for contracts.");
        sb.AppendLine(
            $"ContractDataLengthGasFactor: {ContractDataLengthGasFactor} - Adjustment factor for gas based on contract length.");
        return sb.ToString();
    }

    /// <summary>
    ///     Updates a gas configuration parameter based on the provided enum value.
    /// </summary>
    /// <param name="owner">The name of the owner (must be the owner).</param>
    /// <param name="parameter">The parameter to update.</param>
    /// <param name="newValue">The new value for the parameter.</param>
    public void UpdateParameter(string owner, GasConfigParameter parameter, decimal newValue)
    {
        EnsureOwner(owner);

        switch (parameter)
        {
            case GasConfigParameter.BaseGasTransaction:
                BaseGasTransaction = newValue;
                Log($"BaseGasTransaction updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.BaseGasContract:
                BaseGasContract = newValue;
                Log($"BaseGasContract updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.GasPerCharacter:
                GasPerCharacter = newValue;
                Log($"GasPerCharacter updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.MinerInitialReward:
                MinerInitialReward = newValue;
                Log($"MinerInitialReward updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.ValidatorInitialReward:
                ValidatorInitialReward = newValue;
                Log($"ValidatorInitialReward updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.MinerDecayFactor:
                MinerDecayFactor = newValue;
                Log($"MinerDecayFactor updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.ValidatorDecayFactor:
                ValidatorDecayFactor = newValue;
                Log($"ValidatorDecayFactor updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.MinerMinimumReward:
                MinerMinimumReward = newValue;
                Log($"MinerMinimumReward updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.ValidatorMinimumReward:
                ValidatorMinimumReward = newValue;
                Log($"ValidatorMinimumReward updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.GasFactor:
                GasFactor = newValue;
                Log($"GasFactor updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.CurrentNetworkLoadGT:
                CurrentNetworkLoadGT = newValue;
                Log($"CurrentNetworkLoadGT updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.CurrentNetworkLoadLT:
                CurrentNetworkLoadLT = newValue;
                Log($"CurrentNetworkLoadLT updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.CurrentNetworkLoadGTMultiply:
                CurrentNetworkLoadGTMultiply = newValue;
                Log($"CurrentNetworkLoadGTMultiply updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.CurrentNetworkLoadLTMultiply:
                CurrentNetworkLoadLTMultiply = newValue;
                Log($"CurrentNetworkLoadLTMultiply updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.ContractDataLengthMin:
                ContractDataLengthMin = newValue;
                Log($"ContractDataLengthMin updated to {newValue} by {owner}");
                break;
            case GasConfigParameter.ContractDataLengthGasFactor:
                ContractDataLengthGasFactor = newValue;
                Log($"ContractDataLengthGasFactor updated to {newValue} by {owner}");
                break;
            default:
                throw new ArgumentException("Invalid parameter specified.");
        }
    }
     
    private void EnsureOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(Owner))
            throw new InvalidOperationException("Owner is not set.");

        if (Owner != owner)
            throw new UnauthorizedAccessException("Only the contract owner can perform this operation.");
    }
}
/// <summary>
///     Logger class
/// </summary>
public class Logger
{
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

        // Print the message to the console, truncating if it exceeds 100 characters
        if (formattedMessage.Length > 110 && trim)
            Console.WriteLine(formattedMessage.Substring(0, 110) + "...");
        else
            Console.WriteLine(formattedMessage);
    }

    /// <summary>
    ///     Logs an Error message to the console with a timestamp, excluding specific messages based on predefined filters.
    /// </summary>
    /// <param name="message">The message to log. Defaults to an empty string.</param>
    /// <param name="trim">Trims output to 110 chars</param>
    public static void LogError(string message = "", bool trim = true)
    {
        Log("Error: " +message,trim);
    }

    /// <summary>
    /// Logs a message to the console with a line
    /// </summary>
    /// <param name="message">message to log</param>
    /// <param name="totalWidth">total with of the line. Maximum is 200</param>
    public static void LogLine(string message="", int totalWidth=80)
    {
        if (totalWidth > 200)
            totalWidth = 200;

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
            string line = new string('-', padding) + " " + message + " " + new string('-', totalWidth - messageLength - padding - 2);

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