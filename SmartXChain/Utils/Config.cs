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
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "SmartXChain");
        var startupDirectory = AppDomain.CurrentDomain.BaseDirectory;

        // Ensure the AppData directory exists
        Directory.CreateDirectory(appDirectory);

        var configFile = "config.txt";
        var appDataConfigPath = Path.Combine(appDirectory, configFile);
        var startupConfigPath = Path.Combine(startupDirectory, configFile);

        // Check if a config exists in the startup directory and not in AppData
        if (File.Exists(startupConfigPath) && !File.Exists(appDataConfigPath))
        {
            // Copy the startup config to AppData
            File.Copy(startupConfigPath, appDataConfigPath);
            Logger.LogMessage("Initial configuration file copied from startup directory to AppData.");
            Logger.LogMessage(startupConfigPath);
            Logger.LogMessage(appDataConfigPath);
        }

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

        // Set default blockchain path in AppData
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var blockchainDirectory = Path.Combine(appDataPath, "SmartXChain", "Blockchain");
        Directory.CreateDirectory(blockchainDirectory); // Ensure directory exists
        BlockchainPath = blockchainDirectory;

        LoadConfig(filePath);
    }

    public string SmartXchain { get; private set; }
    public string MinerAddress { get; private set; }
    public string Mnemonic { get; private set; }
    public List<string> Peers { get; }
    public int Port { get; private set; }
    public string IP { get; private set; }
    public bool Debug { get; private set; }
    public string BlockchainPath { get; private set; }
    public string PublicKey { get; private set; }
    public string PrivateKey { get; private set; }
    public static Config Default => _defaultInstance.Value;

    public bool Delete()
    {
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDirectory = Path.Combine(appDataPath, "SmartXChain");
            var configFilePath = Path.Combine(appDirectory, "config.txt");
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

    public void ReloadConfig()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "SmartXChain");
        var configFilePath = Path.Combine(appDirectory, "config.txt");
        if (!File.Exists(configFilePath))
        {
            Logger.LogMessage("Config file not found during reload.");
            return;
        }

        Peers.Clear();
        SmartXchain = null;
        MinerAddress = null;
        Mnemonic = null;
        Port = 0;
        PublicKey = null;
        PrivateKey = null;
        BlockchainPath = "";

        LoadConfig(configFilePath);
        Logger.LogMessage("Configuration reloaded successfully.");
    }

    public void SetMinerAddress(string minerAddress, string mnemonic, string privatekey)
    {
        MinerAddress = minerAddress;
        Mnemonic = mnemonic;

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "SmartXChain");
        var filePath = Path.Combine(appDirectory, "config.txt");

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
                continue;

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
                    if (key.Equals("BlockchainPath", StringComparison.OrdinalIgnoreCase))
                        BlockchainPath = value;
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
                else
                {
                    Logger.LogMessage($"Invalid miner line: {trimmedLine}");
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
                else
                {
                    Logger.LogMessage($"Invalid server line: {trimmedLine}");
                }
            }
        }
    }

    public void GenerateServerKeys()
    {
        using var rsa = RSA.Create(2048);
        PublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());
        PrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDirectory = Path.Combine(appDataPath, "SmartXChain");
        var filePath = Path.Combine(appDirectory, "config.txt");

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
}