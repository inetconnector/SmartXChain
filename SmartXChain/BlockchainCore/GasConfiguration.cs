using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     Configuration class for gas and reward parameters in a smart contract.
///     This class defines key values and rules for gas calculations and reward mechanisms.
/// </summary>
public sealed class GasConfiguration
{
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

    private static readonly Lazy<GasConfiguration> _instance = new(() => new GasConfiguration());

    /// <summary>
    ///     Gets the singleton <see cref="GasConfiguration" /> instance used throughout the application.
    /// </summary>
    public static GasConfiguration Instance => _instance.Value;

    /// <summary>
    ///     Gets or sets the base gas amount required for a regular transaction.
    /// </summary>
    [JsonInclude]
    public decimal BaseGasTransaction { get; set; } = 5;

    /// <summary>
    ///     Gets or sets the base gas amount required for deploying a contract.
    /// </summary>
    [JsonInclude]
    public decimal BaseGasContract { get; set; } = 10;

    /// <summary>
    ///     Gets or sets the additional gas cost per character of contract data.
    /// </summary>
    [JsonInclude]
    public decimal GasPerCharacter { get; set; } = 2;

    /// <summary>
    ///     Gets or sets the initial reward distributed to miners.
    /// </summary>
    [JsonInclude]
    public decimal MinerInitialReward { get; set; } = 0.1m;

    /// <summary>
    ///     Gets or sets the initial reward distributed to validators.
    /// </summary>
    [JsonInclude]
    public decimal ValidatorInitialReward { get; set; } = 0.05m;

    /// <summary>
    ///     Gets or sets the factor by which miner rewards decay over time.
    /// </summary>
    [JsonInclude]
    public decimal MinerDecayFactor { get; set; } = 0.98m;

    /// <summary>
    ///     Gets or sets the factor by which validator rewards decay over time.
    /// </summary>
    [JsonInclude]
    public decimal ValidatorDecayFactor { get; set; } = 0.99m;

    /// <summary>
    ///     Gets or sets the minimum reward value for miners.
    /// </summary>
    [JsonInclude]
    public decimal MinerMinimumReward { get; set; } = 0.01m;

    /// <summary>
    ///     Gets or sets the minimum reward value for validators.
    /// </summary>
    [JsonInclude]
    public decimal ValidatorMinimumReward { get; set; } = 0.005m;

    /// <summary>
    ///     Gets or sets the global multiplier applied to calculated gas values.
    /// </summary>
    [JsonInclude]
    public decimal GasFactor { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the threshold at which the network is considered under heavy load.
    /// </summary>
    [JsonInclude]
    public decimal CurrentNetworkLoadGT { get; set; } = 0.75m;

    /// <summary>
    ///     Gets or sets the threshold at which the network is considered under light load.
    /// </summary>
    [JsonInclude]
    public decimal CurrentNetworkLoadLT { get; set; } = 0.25m;

    /// <summary>
    ///     Gets or sets the multiplier applied to gas when the network load exceeds <see cref="CurrentNetworkLoadGT" />.
    /// </summary>
    [JsonInclude]
    public decimal CurrentNetworkLoadGTMultiply { get; set; } = 1.2m;

    /// <summary>
    ///     Gets or sets the multiplier applied to gas when the network load falls below <see cref="CurrentNetworkLoadLT" />.
    /// </summary>
    [JsonInclude]
    public decimal CurrentNetworkLoadLTMultiply { get; set; } = 0.8m;

    /// <summary>
    ///     Gets or sets the minimum size of contract data before additional gas adjustments are applied.
    /// </summary>
    [JsonInclude]
    public decimal ContractDataLengthMin { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the factor used to adjust gas consumption based on contract data length.
    /// </summary>
    [JsonInclude]
    public decimal ContractDataLengthGasFactor { get; set; } = 0.8m;

    /// <summary>
    ///     Prints the current gas and reward configuration to the console.
    /// </summary>
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
        sb.AppendLine($"ValidatorDecayFactor: {ValidatorDecayFactor} - Decay rate for validator rewards over time.");
        sb.AppendLine($"MinerMinimumReward: {MinerMinimumReward} - Minimum reward for miners.");
        sb.AppendLine($"ValidatorMinimumReward: {ValidatorMinimumReward} - Minimum reward for validators.");
        sb.AppendLine($"GasFactor: {GasFactor} - Scaling factor to adjust gas calculations.");
        sb.AppendLine($"CurrentNetworkLoadGT: {CurrentNetworkLoadGT} - Threshold for high network load.");
        sb.AppendLine($"CurrentNetworkLoadLT: {CurrentNetworkLoadLT} - Threshold for low network load.");
        sb.AppendLine(
            $"CurrentNetworkLoadGTMultiply: {CurrentNetworkLoadGTMultiply} - Multiplier for gas during high network load.");
        sb.AppendLine(
            $"CurrentNetworkLoadLTMultiply: {CurrentNetworkLoadLTMultiply} - Multiplier for gas during low network load.");
        sb.AppendLine($"ContractDataLengthMin: {ContractDataLengthMin} - Minimum data length threshold for contracts.");
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
    public void UpdateParameter(GasConfigParameter parameter, decimal newValue)
    {
        switch (parameter)
        {
            case GasConfigParameter.BaseGasTransaction:
                BaseGasTransaction = newValue;
                break;
            case GasConfigParameter.BaseGasContract:
                BaseGasContract = newValue;
                break;
            case GasConfigParameter.GasPerCharacter:
                GasPerCharacter = newValue;
                break;
            case GasConfigParameter.MinerInitialReward:
                MinerInitialReward = newValue;
                break;
            case GasConfigParameter.ValidatorInitialReward:
                ValidatorInitialReward = newValue;
                break;
            case GasConfigParameter.MinerDecayFactor:
                MinerDecayFactor = newValue;
                break;
            case GasConfigParameter.ValidatorDecayFactor:
                ValidatorDecayFactor = newValue;
                break;
            case GasConfigParameter.MinerMinimumReward:
                MinerMinimumReward = newValue;
                break;
            case GasConfigParameter.ValidatorMinimumReward:
                ValidatorMinimumReward = newValue;
                break;
            case GasConfigParameter.GasFactor:
                GasFactor = newValue;
                break;
            case GasConfigParameter.CurrentNetworkLoadGT:
                CurrentNetworkLoadGT = newValue;
                break;
            case GasConfigParameter.CurrentNetworkLoadLT:
                CurrentNetworkLoadLT = newValue;
                break;
            case GasConfigParameter.CurrentNetworkLoadGTMultiply:
                CurrentNetworkLoadGTMultiply = newValue;
                break;
            case GasConfigParameter.CurrentNetworkLoadLTMultiply:
                CurrentNetworkLoadLTMultiply = newValue;
                break;
            case GasConfigParameter.ContractDataLengthMin:
                ContractDataLengthMin = newValue;
                break;
            case GasConfigParameter.ContractDataLengthGasFactor:
                ContractDataLengthGasFactor = newValue;
                break;
            default:
                throw new ArgumentException("Invalid parameter specified.");
        }
    }

    /// <summary>
    ///     Serializes the current configuration to a Base64 string.
    /// </summary>
    /// <returns>A Base64-encoded string representing the configuration.</returns>
    public string ToBase64String()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(this, options);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    ///     Deserializes a configuration from a Base64 string.
    /// </summary>
    /// <param name="base64String">The Base64 string to deserialize from.</param>
    /// <returns>A GasConfiguration instance deserialized from the Base64 string.</returns>
    public static GasConfiguration FromBase64String(string base64String)
    {
        var bytes = Convert.FromBase64String(base64String);
        var json = Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<GasConfiguration>(json);
    }

    /// <summary>
    ///     Serializes the current configuration to a JSON file.
    /// </summary>
    /// <param name="filePath">The path to the file where the configuration will be saved.</param>
    public static void SaveToFile(string filePath, GasConfiguration config)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    ///     Loads a configuration from a JSON file.
    /// </summary>
    /// <param name="filePath">The path to the file from which the configuration will be loaded.</param>
    /// <returns>A GasConfiguration instance loaded from the file.</returns>
    public static GasConfiguration LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<GasConfiguration>(json);
    }
}