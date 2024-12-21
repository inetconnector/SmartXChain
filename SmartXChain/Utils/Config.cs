using System.Text.RegularExpressions;

public class Config
{
    private static readonly Lazy<Config> _defaultInstance = new(() =>
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configFilePath = Path.Combine(appDirectory, "config.txt");
        return new Config(configFilePath);
    });

    public Config(string filePath)
    {
        Peers = new List<string>();
        LoadConfig(filePath);
    }

    public string SmartXchain { get; private set; }
    public string MinerAddress { get; private set; }
    public string Mnemonic { get; private set; }
    public List<string> Peers { get; }
    public int Port { get; private set; }
    public string IP { get; private set; }
    public bool Debug { get; private set; }
    public static Config Default => _defaultInstance.Value;

    public void ReloadConfig()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var configFilePath = Path.Combine(appDirectory, "config.txt");
        if (!File.Exists(configFilePath))
        {
            Console.WriteLine("Config file not found during reload.");
            return;
        }

        // Clear the existing configuration
        Peers.Clear();
        SmartXchain = null;
        MinerAddress = null;
        Mnemonic = null;
        Port = 0;

        // Reload the configuration from the file
        LoadConfig(configFilePath);

        Console.WriteLine("Configuration reloaded successfully.");
    }

    private void LoadConfig(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException($"Config file not found: {filePath}");

        var lines = File.ReadAllLines(filePath);
        var isPeerSection = false;
        var isMinerSection = false;
        var isConfigSection = false;

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
            }
            else if (trimmedLine.Equals("[Peers]", StringComparison.OrdinalIgnoreCase))
            {
                isPeerSection = true;
                isConfigSection = false;
                isMinerSection = false;
            }
            else if (trimmedLine.Equals("[Miner]", StringComparison.OrdinalIgnoreCase))
            {
                isMinerSection = true;
                isConfigSection = false;
                isPeerSection = false;
            }
            else if (isPeerSection && Regex.IsMatch(trimmedLine, @"^tcp://[a-zA-Z0-9\-.]+:\d+$"))
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
                else
                {
                    Console.WriteLine($"Invalid configuration line: {trimmedLine}");
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
                    Console.WriteLine($"Invalid miner line: {trimmedLine}");
                }
            }
        }
    }

    public void SetMinerAddress(string minerAddress, string mnemonic)
    {
        MinerAddress = minerAddress;
        Mnemonic = mnemonic;
        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

        if (!File.Exists(filePath)) File.WriteAllText(filePath, "[Miner]\n");

        var lines = File.ReadAllLines(filePath).ToList();
        var minerSectionIndex = lines.FindIndex(l => l.Trim().Equals("[Miner]", StringComparison.OrdinalIgnoreCase));

        if (minerSectionIndex >= 0)
        {
            // Update existing Miner section
            for (var i = minerSectionIndex + 1; i < lines.Count; i++)
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].StartsWith("["))
                {
                    lines.Insert(i, $"MinerAddress={minerAddress}");
                    lines.Insert(i + 1, $"Mnemonic={mnemonic}");
                    break;
                }
        }
        else
        {
            // Append new Miner section
            lines.Add("");
            lines.Add("[Miner]");
            lines.Add($"MinerAddress={minerAddress}");
            lines.Add($"Mnemonic={mnemonic}");
        }

        File.WriteAllLines(filePath, lines);
        Console.WriteLine("Miner address and mnemonic saved to config.");
    }
}