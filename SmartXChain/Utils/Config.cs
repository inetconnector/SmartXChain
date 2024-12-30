using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SmartXChain.Utils;

/// <summary>
///     Represents the configuration management for the SmartXChain application,
///     including server, miner, and peer settings.
/// </summary>
public class Config
{
    private static readonly Lazy<Config> _defaultInstance = new(() =>
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configFilePath = Path.Combine(appDirectory, "config.txt");
        return new Config(configFilePath);
    });

    /// <summary>
    ///     Initializes a new instance of the <see cref="Config" /> class and loads the configuration from a file.
    /// </summary>
    /// <param name="filePath">The file path to the configuration file.</param>
    public Config(string filePath)
    {
        Peers = new List<string>();
        LoadConfig(filePath);
    }

    /// <summary>
    ///     Gets the SmartXChain network identifier.
    /// </summary>
    public string SmartXchain { get; private set; }

    /// <summary>
    ///     Gets the miner's wallet address.
    /// </summary>
    public string MinerAddress { get; private set; }

    /// <summary>
    ///     Gets the mnemonic phrase associated with the miner.
    /// </summary>
    public string Mnemonic { get; private set; }

    /// <summary>
    ///     Gets the list of known peer addresses.
    /// </summary>
    public List<string> Peers { get; }

    /// <summary>
    ///     Gets the port number for the server.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    ///     Gets the IP address of the server.
    /// </summary>
    public string IP { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether debugging is enabled.
    /// </summary>
    public bool Debug { get; private set; }

    /// <summary>
    ///     Gets the public key for the server.
    /// </summary>
    public string PublicKey { get; private set; }

    /// <summary>
    ///     Gets the private key for the server.
    /// </summary>
    public string PrivateKey { get; private set; }

    /// <summary>
    ///     Gets the default configuration instance.
    /// </summary>
    public static Config Default => _defaultInstance.Value;

    /// <summary>
    ///     Reloads the configuration from the configuration file.
    /// </summary>
    public void ReloadConfig()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configFilePath = Path.Combine(appDirectory, "config.txt");
        if (!File.Exists(configFilePath))
        {
            Logger.LogMessage("Config file not found during reload.");
            return;
        }

        // Clear the existing configuration
        Peers.Clear();
        SmartXchain = null;
        MinerAddress = null;
        Mnemonic = null;
        Port = 0;
        PublicKey = null;
        PrivateKey = null;

        // Reload the configuration from the file
        LoadConfig(configFilePath);
        Logger.LogMessage("Configuration reloaded successfully.");
    }

    /// <summary>
    ///     Sets the miner's address, mnemonic, and private key in the configuration file.
    /// </summary>
    /// <param name="minerAddress">The miner's address.</param>
    /// <param name="mnemonic">The mnemonic phrase.</param>
    /// <param name="privatekey">The private key.</param>
    public void SetMinerAddress(string minerAddress, string mnemonic, string privatekey)
    {
        MinerAddress = minerAddress;
        Mnemonic = mnemonic;
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

        if (!File.Exists(filePath)) File.WriteAllText(filePath, "[Miner]\n");

        var lines = File.ReadAllLines(filePath).ToList();
        var minerSectionIndex = lines.FindIndex(l => l.Trim().Equals("[Miner]", StringComparison.OrdinalIgnoreCase));

        if (minerSectionIndex >= 0)
        {
            for (var i = minerSectionIndex + 1; i < lines.Count; i++)
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("["))
                {
                    lines.Insert(i, $"MinerAddress={minerAddress}");
                    lines.Insert(i + 1, $"Mnemonic={mnemonic}");
                    lines.Insert(i + 1, $"PrivateKey={privatekey}");
                    break;
                }
        }
        else
        {
            lines.Add("");
            lines.Add("[Miner]");
            lines.Add($"MinerAddress={minerAddress}");
            lines.Add($"Mnemonic={mnemonic}");
            lines.Add($"PrivateKey={privatekey}");
        }

        File.WriteAllLines(filePath, lines);
        Logger.LogMessage("Miner address, private key, and mnemonic saved to config.");
    }

    /// <summary>
    ///     Loads the configuration data from a file and populates the properties.
    /// </summary>
    /// <param name="filePath">The file path to the configuration file.</param>
    private void LoadConfig(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Config file not found: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var isPeerSection = false;
        var isMinerSection = false;
        var isConfigSection = false;
        var isServerSection = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                continue; // Skip empty lines and comments

            if (trimmedLine.Equals("[Config]", StringComparison.OrdinalIgnoreCase))
            {
                isConfigSection = true;
                isPeerSection = false;
                isMinerSection = false;
                isServerSection = false;
            }
            else if (trimmedLine.Equals("[Peers]", StringComparison.OrdinalIgnoreCase))
            {
                isPeerSection = true;
                isConfigSection = false;
                isMinerSection = false;
                isServerSection = false;
            }
            else if (trimmedLine.Equals("[Miner]", StringComparison.OrdinalIgnoreCase))
            {
                isMinerSection = true;
                isConfigSection = false;
                isPeerSection = false;
                isServerSection = false;
            }
            else if (trimmedLine.Equals("[Server]", StringComparison.OrdinalIgnoreCase))
            {
                isServerSection = true;
                isConfigSection = false;
                isPeerSection = false;
                isMinerSection = false;
            }
            else if (isPeerSection && Regex.IsMatch(trimmedLine, @"^http://[a-zA-Z0-9\-.]+:\d+$"))
            {
                Peers.Add(trimmedLine);
            }
            else if (isConfigSection)
            {
                var parts = trimmedLine.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key.Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var port))
                        Port = port;
                    if (key.Equals("IP", StringComparison.OrdinalIgnoreCase))
                        IP = value;
                    if (key.Equals("Debug", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var debug))
                        Debug = debug;
                }
            }
            else if (isMinerSection)
            {
                var parts = trimmedLine.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key.Equals("SmartXchain", StringComparison.OrdinalIgnoreCase))
                        SmartXchain = value;
                    else if (key.Equals("MinerAddress", StringComparison.OrdinalIgnoreCase))
                        MinerAddress = value;
                    else if (key.Equals("Mnemonic", StringComparison.OrdinalIgnoreCase)) Mnemonic = value;
                }
            }
            else if (isServerSection)
            {
                var parts = trimmedLine.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key.Equals("PublicKey", StringComparison.OrdinalIgnoreCase))
                        PublicKey = value;
                    else if (key.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase))
                        PrivateKey = value;
                }
            }
        }
    }

    /// <summary>
    ///     Generates RSA public and private keys for the server and saves them in the configuration file.
    /// </summary>
    public void GenerateServerKeys()
    {
        using var rsa = RSA.Create(2048);
        PublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        PrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
        if (!File.Exists(filePath)) File.WriteAllText(filePath, "[Server]\n");

        var lines = File.ReadAllLines(filePath).ToList();
        var serverSectionIndex = lines.FindIndex(l => l.Trim().Equals("[Server]", StringComparison.OrdinalIgnoreCase));

        if (serverSectionIndex >= 0)
        {
            for (var i = serverSectionIndex + 1; i < lines.Count; i++)
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("["))
                {
                    lines.Insert(i, $"PublicKey={PublicKey}");
                    lines.Insert(i + 1, $"PrivateKey={PrivateKey}");
                    break;
                }
        }
        else
        {
            lines.Add("");
            lines.Add("[Server]");
            lines.Add($"PublicKey={PublicKey}");
            lines.Add($"PrivateKey={PrivateKey}");
        }

        File.WriteAllLines(filePath, lines);
        Logger.LogMessage("Keys generated and saved to config.");
    }

    /// <summary>
    ///     Verifies the RSA keys stored in the configuration file.
    /// </summary>
    /// <param name="storedPrivateKey">The private key to verify.</param>
    /// <param name="storedPublicKey">The public key to verify.</param>
    /// <returns>True if the keys are valid; otherwise, false.</returns>
    public bool VerifyServerKeys(string storedPrivateKey, string storedPublicKey)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportRSAPrivateKey(Convert.FromBase64String(storedPrivateKey), out _);

            var derivedPublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());

            return derivedPublicKey == storedPublicKey;
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error verifying server keys: {ex.Message}");
            return false;
        }
    }
}