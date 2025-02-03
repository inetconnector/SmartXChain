using System.Security.Cryptography;
using System.Text.RegularExpressions;
using WebSocketSharp;

namespace SmartXChain.Utils;

/// <summary>
///     Represents the configuration management for the SmartXChain application,
///     including server, miner, and peer settings.
/// </summary>
public class Config
{
    public enum ChainNames
    {
        SmartXChain_Testnet,
        SmartXChain
    }

    public enum ConfigKey
    {
        ChainId,
        BlockchainPath,
        MinerAddress,
        Mnemonic,
        WalletPrivateKey,
        NodeAddress,
        Debug,
        PublicKey,
        PrivateKey, 
        SSLCertificate,
        MaxParallelConnections
    }

    private static readonly Lazy<Config> _defaultInstance = new(() =>
    {
        var configFilePath = FileSystem.ConfigFile;
        var fi = new FileInfo(configFilePath);
        Directory.CreateDirectory(fi.DirectoryName); 
        var config= new Config(configFilePath);
        if (string.IsNullOrEmpty(config.NodeAddress))
            config.NodeAddress = "";
        return config;
    });

    /// <summary>
    ///     Initializes a new instance of the <see cref="Config" /> class and loads the configuration from a file.
    /// </summary>
    /// <param name="filePath">The file path to the configuration file.</param>
    public Config(string filePath)
    {
        SignalHubs = new List<string>(); 
        LoadConfig(filePath);
        SetBlockchainPath(FileSystem.BlockchainDirectory);
    }

    public string ChainId { get; private set; }
    public string MinerAddress { get; private set; }
    public string Mnemonic { get; private set; }
    public int MaxParallelConnections { get; private set; }
    public string WalletPrivateKey { get; private set; }
    public List<string> SignalHubs { get; } 
    public string NodeAddress {get; internal set;} 
    public bool Debug { get; private set; }
    public string BlockchainPath { get; private set; } 
    public string SSLCertificate { get; private set; }
    public string PublicKey { get; private set; }
    public string PrivateKey { get; private set; }
    public int ResponseTimeoutMilliseconds { get; set; } = 1000;

    public static bool TestNet
    {
        get => ChainName == ChainNames.SmartXChain_Testnet;
        set =>
            ChainName = value ? ChainNames.SmartXChain_Testnet : ChainNames.SmartXChain;
    }

    public static Config Default => _defaultInstance.Value;
    public static ChainNames ChainName { get; set; } = ChainNames.SmartXChain_Testnet;

    /// <summary>
    ///     Deletes the configuration file.
    /// </summary>
    /// <returns>True if the file was successfully deleted; otherwise, false.</returns>
    public bool Delete()
    {
        try
        {
            var configFilePath = FileSystem.ConfigFile;
            if (File.Exists(configFilePath))
            {
                File.Delete(configFilePath);
                Logger.Log("Config file deleted");
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "deleting config file");
        }

        return false;
    }

    /// <summary>
    ///     Reloads the configuration file into memory.
    /// </summary>
    public void ReloadConfig()
    {
        var configFilePath = FileSystem.ConfigFile;
        if (!File.Exists(configFilePath))
        {
            Logger.Log("Config file not found during reload.");
            return;
        }

        SignalHubs.Clear();
        ChainId = null;
        MinerAddress = null;
        Mnemonic = null;
        WalletPrivateKey = null;
        PublicKey = null;
        PrivateKey = null;
        BlockchainPath = "";
        SSLCertificate = ""; 
        MaxParallelConnections = 10; 

        LoadConfig(configFilePath);
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
            Logger.Log("Invalid value for SetProperty.");
            return;
        }

        var filePath = FileSystem.ConfigFile;
        var keyName = key.ToString();
        var section = GetSectionForKey(key);

        if (!File.Exists(filePath))
        {
            Logger.Log($"Config file not found: {filePath}");
            File.WriteAllText(filePath, $"[{section}]\n{keyName}={value}\n");
            Logger.Log($"New config file created: {filePath}");
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
        Logger.Log($"Property '{keyName}' in section '{section}' set to '{value}'.");
        ReloadConfig();
    }

    /// <summary>
    ///     Retrieves a property value from the configuration file.
    /// </summary>
    /// <param name="key">The key of the property as an enum.</param>
    /// <returns>The value of the property, or null if not found.</returns>
    public string? GetProperty(ConfigKey key)
    {
        var filePath = FileSystem.ConfigFile;

        if (!File.Exists(filePath))
        {
            Logger.Log($"Config file not found: {filePath}");
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
            ConfigKey.Debug => "Config",
            ConfigKey.NodeAddress => "Config", 
            ConfigKey.MaxParallelConnections => "Config",

            ConfigKey.MinerAddress => "Miner",
            ConfigKey.Mnemonic => "Miner",
            ConfigKey.WalletPrivateKey => "Miner",

            ConfigKey.PublicKey => "Server",
            ConfigKey.PrivateKey => "Server",
            ConfigKey.SSLCertificate => "Server",

            _ => throw new ArgumentException("Invalid key", nameof(key))
        };
    }

    public void GenerateServerKeys()
    {
        using var rsa = RSA.Create(2048);
        PublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        PrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

        var filePath = FileSystem.ConfigFile;

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
            lines.Add($"SSLCertificate={SSLCertificate}");
        }

        File.WriteAllLines(filePath, lines);
        Logger.Log("Server keys generated and saved to config.");
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
                        if (key.Equals("Debug", StringComparison.OrdinalIgnoreCase) &&
                            bool.TryParse(value, out var debug))
                            Debug = debug;
                        if (key.Equals("ChainId", StringComparison.OrdinalIgnoreCase))
                            ChainId = value;
                        if (key.Equals("NodeAddress", StringComparison.OrdinalIgnoreCase))
                            NodeAddress = value; 
                        if (key.Equals("MaxParallelConnections", StringComparison.OrdinalIgnoreCase))
                            MaxParallelConnections = Convert.ToInt16(value);
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
                        if (key.Equals("PublicKey", StringComparison.OrdinalIgnoreCase))
                            PublicKey = value;
                        if (key.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase))
                            PrivateKey = value;
                        if (key.Equals("SSLCertificate", StringComparison.OrdinalIgnoreCase))
                            SSLCertificate = value;
                        break;
                }
            }
            else if (currentSection == "[SignalHubs]")
            {
                var item = trimmedLine;
                if (Regex.IsMatch(item, @"^https?://[\w\-.]+(:\d+)?(/[\w\-/]*)?$"))
                    SignalHubs.Add(item);
                else
                    Logger.Log($"Invalid SignalHub: {item}");
            }
        }
    }

    /// <summary>
    ///     Adds a new peer to the configuration file if it does not already exist.
    /// </summary>
    /// <param name="signalHubUrl">The NodeAddress of the peer to add.</param>
    /// <returns>True if the peer was successfully added; otherwise, false.</returns>
    public bool AddSignalHub(string signalHubUrl)
    {
        if (string.IsNullOrWhiteSpace(signalHubUrl) || !Regex.IsMatch(signalHubUrl, @"^https?://[\w\-.]+(:\d+)?$"))
        {
            Logger.Log($"Invalid peer SignalHub: {signalHubUrl}");
            return false;
        }

        if (SignalHubs.Contains(signalHubUrl))
        {
            Logger.Log($"SignalHub already exists: {signalHubUrl}");
            return false;
        }

        SignalHubs.Add(signalHubUrl);

        var filePath = FileSystem.ConfigFile;
        if (!File.Exists(filePath))
        {
            Logger.Log($"Config file not found: {filePath}");
            return false;
        }

        var lines = File.ReadAllLines(filePath).ToList();
        var peersSectionIndex = lines.FindIndex(l => l.Trim().Equals("[SignalHubs]", StringComparison.OrdinalIgnoreCase));

        if (peersSectionIndex >= 0)
        {
            lines.Insert(peersSectionIndex + 1, signalHubUrl);
        }
        else
        {
            lines.Add("");
            lines.Add("[SignalHubs]");
            lines.Add(signalHubUrl);
        }

        File.WriteAllLines(filePath, lines);
        Logger.Log($"SignalHub added: {signalHubUrl}");
        return true;
    }

    /// <summary>
    ///     Removes an existing peer from the configuration file if it exists.
    /// </summary>
    /// <param name="signalHub">The NodeAddress of the peer to remove.</param>
    /// <returns>True if the peer was successfully removed; otherwise, false.</returns>
    public bool RemoveSignalHub(string signalHub)
    {
        if (string.IsNullOrWhiteSpace(signalHub))
        {
            Logger.Log("Invalid peer SignalHub provided for removal.");
            return false;
        }

        if (!SignalHubs.Remove(signalHub))
        {
            Logger.Log($"SignalHub not found: {signalHub}");
            return false;
        }

        var filePath = FileSystem.ConfigFile;
        if (!File.Exists(filePath))
        {
            Logger.Log($"Config file not found: {filePath}");
            return false;
        }

        var lines = File.ReadAllLines(filePath).ToList();
        var peersSectionIndex = lines.FindIndex(l => l.Trim().Equals("[SignalHubs]", StringComparison.OrdinalIgnoreCase));

        if (peersSectionIndex >= 0)
        {
            var updatedPeers = lines.Skip(peersSectionIndex + 1)
                .TakeWhile(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("["))
                .Where(l => !l.Equals(signalHub, StringComparison.OrdinalIgnoreCase))
                .ToList();

            lines = lines.Take(peersSectionIndex + 1).Concat(updatedPeers).ToList();
            File.WriteAllLines(filePath, lines);
            Logger.Log($"SignalHub removed: {signalHub}");
            return true;
        }

        Logger.Log("SignalHub section not found in the config file.");
        return false;
    }
}