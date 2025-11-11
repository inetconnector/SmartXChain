using System.Security.Cryptography;
using System.Text.RegularExpressions;

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

    private static readonly HashSet<ConfigKey> SensitiveConfigKeys = new()
    {
        ConfigKey.Mnemonic,
        ConfigKey.WalletPrivateKey,
        ConfigKey.PrivateKey
    };

    private static readonly Lazy<Config> _defaultInstance = new(() =>
    {
        var configFilePath = FileSystem.ConfigFile;
        var fi = new FileInfo(configFilePath);
        Directory.CreateDirectory(fi.DirectoryName);
        var config = new Config(configFilePath);
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
        RedisConnectionString = string.Empty;
        RedisNamespace = "smartxchain";
        RedisHeartbeatSeconds = 10;
        RedisNodeTtlSeconds = 45;
        MaxParallelConnections = 10;
        SyncChunkSize = 200;
        SyncRequestTimeoutSeconds = 15;
        NodeStaleTimeoutSeconds = 180;
        MaxSyncFailures = 3;
        SyncParallelism = 4;
        LoadConfig(filePath);
        SetBlockchainPath(FileSystem.BlockchainDirectory);
    }

    public string ChainId { get; private set; }
    public string MinerAddress { get; private set; }
    public string Mnemonic { get; private set; }
    public int MaxParallelConnections { get; private set; }
    public string WalletPrivateKey { get; private set; }
    public List<string> SignalHubs { get; }
    public string NodeAddress { get; internal set; }
    public bool Debug { get; private set; }
    public string BlockchainPath { get; private set; }
    public string SSLCertificate { get; private set; }
    public string PublicKey { get; private set; }
    public string PrivateKey { get; private set; }
    public int ResponseTimeoutMilliseconds { get; set; } = 1000;
    public int SyncChunkSize { get; private set; }
    public int SyncRequestTimeoutSeconds { get; private set; }
    public int NodeStaleTimeoutSeconds { get; private set; }
    public int MaxSyncFailures { get; private set; }
    public int SyncParallelism { get; private set; }
    public string RedisConnectionString { get; private set; }
    public string RedisNamespace { get; private set; }
    public int RedisHeartbeatSeconds { get; private set; }
    public int RedisNodeTtlSeconds { get; private set; }
    public bool RedisEnabled => !string.IsNullOrWhiteSpace(RedisConnectionString);

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
        SyncChunkSize = 200;
        SyncRequestTimeoutSeconds = 15;
        NodeStaleTimeoutSeconds = 180;
        MaxSyncFailures = 3;
        SyncParallelism = 4;
        RedisConnectionString = string.Empty;
        RedisNamespace = "smartxchain";
        RedisHeartbeatSeconds = 10;
        RedisNodeTtlSeconds = 45;

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
        var persistedValue = PrepareValueForPersistence(key, value);

        if (!File.Exists(filePath))
        {
            Logger.Log($"Config file not found: {filePath}");
            File.WriteAllText(filePath, $"[{section}]\n{keyName}={persistedValue}\n");
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
                lines[keyIndex] = $"{keyName}={persistedValue}";
            else
                lines.Insert(sectionIndex + 1, $"{keyName}={persistedValue}");
        }
        else
        {
            lines.Add("");
            lines.Add(sectionHeader);
            lines.Add($"{keyName}={persistedValue}");
        }

        File.WriteAllLines(filePath, lines);
        var valueForLog = IsSensitiveKey(key) ? "[redacted]" : value;
        Logger.Log($"Property '{keyName}' in section '{section}' set to '{valueForLog}'.");
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
                {
                    var rawValue = parts[1].Trim();
                    return ResolveStoredValue(key, rawValue);
                }
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

    private string PrepareValueForPersistence(ConfigKey key, string value)
    {
        if (!IsSensitiveKey(key))
            return value;

        if (TryParseVaultReference(value, out var existingIdentifier))
            return $"vault:{existingIdentifier}";

        var identifier = SecureVault.StoreSecret(BuildSecretIdentifier(key), value);
        return $"vault:{identifier}";
    }

    private static bool TryParseVaultReference(string value, out string identifier)
    {
        const string prefix = "vault:";
        if (!string.IsNullOrWhiteSpace(value) &&
            value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            identifier = value[prefix.Length..];
            return !string.IsNullOrWhiteSpace(identifier);
        }

        identifier = string.Empty;
        return false;
    }

    private string? ResolveStoredValue(ConfigKey key, string? persistedValue)
    {
        if (persistedValue == null)
            return null;

        if (!IsSensitiveKey(key))
            return persistedValue;

        if (TryParseVaultReference(persistedValue, out var identifier))
            return SecureVault.RetrieveSecret(identifier);

        if (string.IsNullOrWhiteSpace(persistedValue))
            return null;

        Logger.LogWarning($"Sensitive config value for {key} stored in plaintext. Migrating to secure vault.");
        var newIdentifier = SecureVault.StoreSecret(BuildSecretIdentifier(key), persistedValue);
        UpdateConfigWithVaultReference(key, newIdentifier);
        return SecureVault.RetrieveSecret(newIdentifier);
    }

    private static string BuildSecretIdentifier(ConfigKey key)
    {
        return $"{ChainName.ToString().ToLowerInvariant()}_{key.ToString().ToLowerInvariant()}";
    }

    private void UpdateConfigWithVaultReference(ConfigKey key, string identifier)
    {
        var filePath = FileSystem.ConfigFile;
        if (!File.Exists(filePath))
            return;

        var lines = File.ReadAllLines(filePath);
        var section = GetSectionForKey(key);
        var sectionHeader = $"[{section}]";
        var keyName = key.ToString();
        var isInSection = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmedLine = lines[i].Trim();

            if (trimmedLine.Equals(sectionHeader, StringComparison.OrdinalIgnoreCase))
            {
                isInSection = true;
                continue;
            }

            if (isInSection && trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                break;

            if (isInSection && trimmedLine.StartsWith($"{keyName}=", StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{keyName}=vault:{identifier}";
                File.WriteAllLines(filePath, lines);
                return;
            }
        }
    }

    private static bool IsSensitiveKey(ConfigKey key)
    {
        return SensitiveConfigKeys.Contains(key);
    }

    public void GenerateServerKeys()
    {
        using var rsa = RSA.Create(2048);
        PublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        PrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

        SetProperty(ConfigKey.PublicKey, PublicKey);
        SetProperty(ConfigKey.PrivateKey, PrivateKey);

        if (!string.IsNullOrWhiteSpace(SSLCertificate))
            SetProperty(ConfigKey.SSLCertificate, SSLCertificate);

        Logger.Log("Server keys generated and stored securely.");
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
                        if (key.Equals("SyncChunkSize", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var chunkSize))
                            SyncChunkSize = Math.Max(1, chunkSize);
                        if (key.Equals("SyncRequestTimeoutSeconds", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var timeoutSeconds))
                            SyncRequestTimeoutSeconds = Math.Max(1, timeoutSeconds);
                        if (key.Equals("NodeStaleTimeoutSeconds", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var nodeTimeout))
                            NodeStaleTimeoutSeconds = Math.Max(10, nodeTimeout);
                        if (key.Equals("MaxSyncFailures", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var maxFailures))
                            MaxSyncFailures = Math.Max(1, maxFailures);
                        if (key.Equals("SyncParallelism", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var parallelism))
                            SyncParallelism = Math.Max(1, parallelism);
                        break;

                    case "[Miner]":
                        if (key.Equals("MinerAddress", StringComparison.OrdinalIgnoreCase))
                            MinerAddress = value;
                        if (key.Equals("Mnemonic", StringComparison.OrdinalIgnoreCase))
                            Mnemonic = ResolveStoredValue(ConfigKey.Mnemonic, value);
                        if (key.Equals("WalletPrivateKey", StringComparison.OrdinalIgnoreCase))
                            WalletPrivateKey = ResolveStoredValue(ConfigKey.WalletPrivateKey, value);
                        break;

                    case "[Server]":
                        if (key.Equals("PublicKey", StringComparison.OrdinalIgnoreCase))
                            PublicKey = value;
                        if (key.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase))
                            PrivateKey = ResolveStoredValue(ConfigKey.PrivateKey, value);
                        if (key.Equals("SSLCertificate", StringComparison.OrdinalIgnoreCase))
                            SSLCertificate = value;
                        break;
                    case "[Redis]":
                        if (key.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase))
                            RedisConnectionString = value;
                        if (key.Equals("Namespace", StringComparison.OrdinalIgnoreCase))
                            RedisNamespace = value;
                        if (key.Equals("HeartbeatSeconds", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var heartbeatSeconds))
                            RedisHeartbeatSeconds = Math.Max(1, heartbeatSeconds);
                        if (key.Equals("NodeTtlSeconds", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(value, out var ttlSeconds))
                            RedisNodeTtlSeconds = Math.Max(RedisHeartbeatSeconds + 5, ttlSeconds);
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
        var peersSectionIndex =
            lines.FindIndex(l => l.Trim().Equals("[SignalHubs]", StringComparison.OrdinalIgnoreCase));

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
        var peersSectionIndex =
            lines.FindIndex(l => l.Trim().Equals("[SignalHubs]", StringComparison.OrdinalIgnoreCase));

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