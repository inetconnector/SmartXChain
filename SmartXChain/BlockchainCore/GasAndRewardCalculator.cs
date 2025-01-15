using System.Text.Json.Serialization;

namespace SmartXChain.BlockchainCore;

internal sealed class GasAndRewardCalculator
{ 
    /// <summary>
    ///     Gets or sets the calculated gas value.
    /// </summary>
    public decimal Gas { get; private set; }

    /// <summary>
    ///     Gets or sets the transaction data.
    /// </summary>
    public string Data { get; set; }

    /// <summary>
    ///     Gets or sets additional information for the transaction.
    /// </summary>
    public string Info { get; set; }

    /// <summary>
    ///     Gets or sets the serialized code of a smart contract.
    /// </summary>
    public string SerializedContractCode { get; set; }

    /// <summary>
    ///     Gets or sets the address of the transaction sender.
    /// </summary>
    public string Sender { get; set; }

    /// <summary>
    ///     Calculates the gas required for a standard transaction based on the lengths of data,
    ///     info, and sender fields, and adjusts it based on network load.
    /// </summary>
    public void CalculateGas()
    {
        var dataLength = string.IsNullOrEmpty(Data) ? 0 : Data.Length;
        var infoLength = string.IsNullOrEmpty(Info) ? 0 : Info.Length;
        var senderLength = string.IsNullOrEmpty(Sender) ? 0 : Sender.Length;

        if (Sender == Blockchain.SystemAddress)
            senderLength = 0;

        Gas = GasConfiguration.Instance.BaseGasTransaction + (dataLength + infoLength + senderLength) *
            GasConfiguration.Instance.GasPerCharacter / GasConfiguration.Instance.GasFactor;

        if (Blockchain.CurrentNetworkLoad > CurrentNetworkLoadGT)
            Gas = (int)(Gas * CurrentNetworkLoadGTMultiply);
        else if (Blockchain.CurrentNetworkLoad < CurrentNetworkLoadLT)
            Gas = (int)(Gas * CurrentNetworkLoadLTMultiply);
    }

    internal static decimal CurrentNetworkLoadGT = (decimal)0.75; 
    internal static decimal CurrentNetworkLoadLT = (decimal)0.25;
    internal static decimal CurrentNetworkLoadGTMultiply = (decimal)1.2;
    internal static decimal CurrentNetworkLoadLTMultiply = (decimal)0.8;
    internal static decimal ContractDataLengthMin = (decimal)1000;
    internal static decimal ContractDataLengthGasFactor = (decimal)1.5;
  
    /// <summary>
    ///     Calculates the gas required for a smart contract transaction based on the length
    ///     of the serialized contract code, with adjustments for code size and network load.
    /// </summary>
    public void CalculateGasForContract()
    {
        var code = SerializedContractCode;
        var dataLength = string.IsNullOrEmpty(code) ? 0 : code.Length;

        Gas = GasConfiguration.Instance.BaseGasContract + dataLength * 
            GasConfiguration.Instance.GasPerCharacter / GasConfiguration.Instance.GasFactor;

        if (dataLength > ContractDataLengthMin)
            Gas = (int)(Gas * ContractDataLengthGasFactor);

        if (Blockchain.CurrentNetworkLoad > CurrentNetworkLoadGT)
            Gas = (int)(Gas * CurrentNetworkLoadGTMultiply);
        else if (Blockchain.CurrentNetworkLoad < CurrentNetworkLoadLT)
            Gas = (int)(Gas * CurrentNetworkLoadLTMultiply);
    }

    /// <summary>
    ///     Calculates the reward for miners based on the wallet count and decay factor.
    ///     Ensures the reward does not fall below the minimum value.
    /// </summary>
    /// <param name="walletCount">The number of wallets to consider for the decay calculation.</param>
    /// <param name="address">The address of the miner.</param>
    /// <returns>The calculated reward for the miner.</returns>
    public decimal CalculateMinerReward(int walletCount, string address)
    {
        if (Transaction.Balances.ContainsKey(address) && Transaction.Balances[address] == 0)
            return GasConfiguration.Instance.MinerInitialReward;

        var reward = GasConfiguration.Instance.MinerInitialReward * (decimal)Math.Pow((double)GasConfiguration.Instance.MinerDecayFactor, walletCount);
        return Math.Max(reward, GasConfiguration.Instance.MinerMinimumReward);
    }

    /// <summary>
    ///     Calculates the reward for validators based on the wallet count and decay factor.
    ///     Ensures the reward does not fall below the minimum value.
    /// </summary>
    /// <param name="walletCount">The number of wallets to consider for the decay calculation.</param>
    /// <returns>The calculated reward for the validator.</returns>
    public decimal CalculateValidatorReward(int walletCount)
    {
        var reward = GasConfiguration.Instance.ValidatorInitialReward * (decimal)Math.Pow((double)GasConfiguration.Instance.ValidatorDecayFactor, walletCount);
        return Math.Max(reward, GasConfiguration.Instance.ValidatorMinimumReward);
    }
}