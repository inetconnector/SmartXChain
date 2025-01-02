using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SmartXChain.Utils;

/// <summary>
///     Represents the configuration management for the SmartXChain application,
///     including server, miner, and peer settings.
/// </summary>
public class Config
{
    public enum ConfigKey
    {
        ChainId,
        BlockchainPath,
        MinerAddress,
        Mnemonic,
        WalletPrivateKey,
        Port,
        IP,
        Debug,
        ServerPublicKey,
        ServerPrivateKey
    }

    private static readonly Lazy<Config> _defaultInstance = new(() =>
    {
        var startupDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Ensure the AppData directory exists
        var appDirectory = AppDirectory();
        Directory.CreateDirectory(appDirectory);

        var appDataConfigPath = Path.Combine(appDirectory, ConfigFileName());
        var startupConfigPath = Path.Combine(startupDirectory, ConfigFileName());

        // Check if a config exists in the startup directory and not in AppData
        if (File.Exists(startupConfigPath) && !File.Exists(appDataConfigPath))
        {
            // Copy the startup config to AppData
            File.Copy(startupConfigPath, appDataConfigPath);
            Logger.LogMessage("Initial configuration file copied from startup directory to AppData.");
        }

        var configFilePath = Path.Combine(appDirectory, ConfigFileName());
        return new Config(configFilePath);
    });

    /// <summary>
    ///     Initializes a new instance of the <see cref="Config" /> class and loads the configuration from a file.
    /// </summary>
    /// <param name="filePath">The file path to the configuration file.</param>
    public Config(string filePath)
    {
        Peers = new List<string>();

        // Set default blockchain path in AppData
        var blockchainDirectory = Path.Combine(AppDirectory(), "Blockchain");
        Directory.CreateDirectory(blockchainDirectory); // Ensure directory exists
        SetBlockchainPath(blockchainDirectory);

        LoadConfig(filePath);
    }

    public string ChainId { get; private set; }
    public string MinerAddress { get; private set; }
    public string Mnemonic { get; private set; }
    public string WalletPrivateKey { get; private set; }
    public List<string> Peers { get; }
    public int Port { get; private set; }
    public string IP { get; private set; }

    public bool Debug { get; private set; }

    public string BlockchainPath { get; private set; }
    public string ServerPublicKey { get; private set; }
    public string ServerPrivateKey { get; private set; }
    public static Config Default => _defaultInstance.Value;

    public static string ChainName { get; set; } = "SmartXChain";

    /// <summary>
    ///     Deletes the configuration file.
    /// </summary>
    /// <returns>True if the file was successfully deleted; otherwise, false.</returns>
    public bool Delete()
    {
        try
        {
            var configFilePath = Path.Combine(AppDirectory(), ConfigFileName());
            if (File.Exists(configFilePath))
            {
                File.Delete(configFilePath);
                Logger.LogMessage("Config file deleted");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error deleting file {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Reloads the configuration file into memory.
    /// </summary>
    public void ReloadConfig()
    {
        var configFilePath = Path.Combine(AppDirectory(), ConfigFileName());
        if (!File.Exists(configFilePath))
        {
            Logger.LogMessage("Config file not found during reload.");
            return;
        }

        Peers.Clear();
        ChainId = null;
        MinerAddress = null;
        Mnemonic = null;
        WalletPrivateKey = null;
        Port = 0;
        ServerPublicKey = null;
        ServerPrivateKey = null;
        BlockchainPath = "";

        LoadConfig(configFilePath);
        Logger.LogMessage("Configuration reloaded successfully.");
    }

    /// <summary>
    ///     Sets a new blockchain path and updates the configuration file.
    /// </summary>
    /// <param name="newPath">The new blockchain path to set.</param>
    public void SetBlockchainPath(string newPath)
    {
        SetProperty(ConfigKey.BlockchainPath, newPath);
        BlockchainPath = newPath;
    }

    /// <summary>
    ///     Sets a property in the configuration file.
    /// </summary>
    /// <param name="key">The key of the property as an enum.</param>
    /// <param name="value">The value to set.</param>
    public void SetProperty(ConfigKey key, string value)
    {
        if (value == null)
        {
            Logger.LogMessage("Invalid value for SetProperty.");
            return;
        }

        var filePath = Path.Combine(AppDirectory(), ConfigFileName());
        var keyName = key.ToString();
        var section = GetSectionForKey(key);

        if (!File.Exists(filePath))
        {
            Logger.LogMessage($"Config file not found: {filePath}");
            File.WriteAllText(filePath, $"[{section}]\n{keyName}={value}\n");
            Logger.LogMessage($"New config file created: {filePath}");
            return;
        }

        var lines = File.ReadAllLines(filePath).ToList();
        var sectionHeader = $"[{section}]";
        var sectionIndex = lines.FindIndex(l => l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex >= 0)
        {
            var keyIndex = lines.FindIndex(sectionIndex + 1, l =>
                l.TrimStart().StartsWith($"{keyName}=", StringComparison.OrdinalIgnoreCase));

            if (keyIndex >= 0)
                lines[keyIndex] = $"{keyName}={value}";
            else
                lines.Insert(sectionIndex + 1, $"{keyName}={value}");
        }
        else
        {
            lines.Add("");
            lines.Add(sectionHeader);
            lines.Add($"{keyName}={value}");
        }

        File.WriteAllLines(filePath, lines);
        Logger.LogMessage($"Property '{keyName}' in section '{section}' set to '{value}'.");
        ReloadConfig();
    }

    /// <summary>
    ///     Retrieves a property value from the configuration file.
    /// </summary>
    /// <param name="key">The key of the property as an enum.</param>
    /// <returns>The value of the property, or null if not found.</returns>
    public string? GetProperty(ConfigKey key)
    {
        var filePath = Path.Combine(AppDirectory(), ConfigFileName());

        if (!File.Exists(filePath))
        {
            Logger.LogMessage($"Config file not found: {filePath}");
            return null;
        }

        var lines = File.ReadAllLines(filePath);
        var keyName = key.ToString();
        var section = GetSectionForKey(key);
        var sectionHeader = $"[{section}]";
        var isInSection = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                continue;

            if (trimmedLine.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                isInSection = true;
                continue;
            }

            if (isInSection && trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                break; // End of section

            if (isInSection)
            {
                var parts = trimmedLine.Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0].Trim().Equals(keyName, StringComparison.OrdinalIgnoreCase))
                    return parts[1].Trim();
            }
        }

        return null; // Key not found
    }

    /// <summary>
    ///     Determines the section for a given key.
    /// </summary>
    /// <param name="key">The key as an enum.</param>
    /// <returns>The section name as a string.</returns>
    private string GetSectionForKey(ConfigKey key)
    {
        return key switch
        {
            ConfigKey.ChainId => "Config",
            ConfigKey.BlockchainPath => "Config",
            ConfigKey.MinerAddress => "Miner",
            ConfigKey.Mnemonic => "Miner",
            ConfigKey.WalletPrivateKey => "Miner",
            ConfigKey.Port => "Config",
            ConfigKey.IP => "Config",
            ConfigKey.Debug => "Config",
            ConfigKey.ServerPublicKey => "Server",
            ConfigKey.ServerPrivateKey => "Server",
            _ => throw new ArgumentException("Invalid key", nameof(key))
        };
    }

    private static string ConfigFileName()
    {
        return ChainName == "SmartXChain" ? "config.txt" : "config.testnet.txt";
    }

    public static string AppDirectory(string chainName = "")
    {
        if (string.IsNullOrWhiteSpace(chainName))
            chainName = ChainName;

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appDataPath, chainName);
    }

    public void GenerateServerKeys()
    {
        using var rsa = RSA.Create(2048);
        ServerPublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        ServerPrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

        var filePath = Path.Combine(AppDirectory(), ConfigFileName());

        if (!File.Exists(filePath)) File.WriteAllText(filePath, "[Server]\n");

        var lines = File.ReadAllLines(filePath).ToList();
        var serverSectionIndex = lines.FindIndex(l => l.Trim().Equals("[Server]", StringComparison.OrdinalIgnoreCase));

        if (serverSectionIndex >= 0)
        {
            for (var i = serverSectionIndex + 1; i < lines.Count; i++)
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("["))
                {
                    lines.Insert(i, $"ServerPublicKey={ServerPublicKey}");
                    lines.Insert(i + 1, $"ServerPrivateKey={ServerPrivateKey}");
                    break;
                }
        }
        else
        {
            lines.Add("");
            lines.Add("[Server]");
            lines.Add($"ServerPublicKey={ServerPublicKey}");
            lines.Add($"ServerPrivateKey={ServerPrivateKey}");
        }

        File.WriteAllLines(filePath, lines);
        Logger.LogMessage("Server keys generated and saved to config.");
    }

    private void LoadConfig(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Config file not found: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var currentSection = string.Empty;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#"))
                continue;

            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                currentSection = trimmedLine;
                continue;
            }

            var parts = trimmedLine.Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();

                switch (currentSection)
                {
                    case "[Config]":
                        if (key.Equals("BlockchainPath", StringComparison.OrdinalIgnoreCase))
                            BlockchainPath = value;
                        if (key.Equals("Port", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var port))
                            Port = port;
                        if (key.Equals("IP", StringComparison.OrdinalIgnoreCase))
                            IP = value;
                        if (key.Equals("Debug", StringComparison.OrdinalIgnoreCase) &&
                            bool.TryParse(value, out var debug))
                            Debug = debug;
                        if (key.Equals("ChainId", StringComparison.OrdinalIgnoreCase))
                            ChainId = value;
                        break;
                    case "[Miner]":
                        if (key.Equals("MinerAddress", StringComparison.OrdinalIgnoreCase))
                            MinerAddress = value;
                        if (key.Equals("Mnemonic", StringComparison.OrdinalIgnoreCase))
                            Mnemonic = value;
                        if (key.Equals("WalletPrivateKey", StringComparison.OrdinalIgnoreCase))
                            WalletPrivateKey = value;
                        break;
                    case "[Server]":
                        if (key.Equals("ServerPublicKey", StringComparison.OrdinalIgnoreCase))
                            ServerPublicKey = value;
                        if (key.Equals("ServerPrivateKey", StringComparison.OrdinalIgnoreCase))
                            ServerPrivateKey = value;
                        break;
                }
            }
            else if (currentSection == "[Peers]")
            {
                var peerValue = trimmedLine;
                if (Regex.IsMatch(peerValue, @"^https?://[\w\-.]+(:\d+)?$"))
                    Peers.Add(peerValue);
                else
                    Console.WriteLine($"Invalid Peer URL: {peerValue}");
            }
        }
    }
}