namespace SmartXChain.BlockchainCore;

public class GasAndRewardCalculator
{
    // Constants for base gas values and rewards
    private const int BaseGasTransaction = 5; // Base gas consumption for a transaction
    private const int BaseGasContract = 10; // Base gas consumption for a smart contract
    private const int GasPerCharacter = 2; // Gas consumption per character in data
    private const double MinerInitialReward = 0.1; // Initial reward for miners
    private const double ValidatorInitialReward = 0.05; // Initial reward for validators
    private const double MinerDecayFactor = 0.98; // Decay rate for miner rewards
    private const double ValidatorDecayFactor = 0.99; // Decay rate for validator rewards
    private const double MinerMinimumReward = 0.01; // Minimum reward for miners
    private const double ValidatorMinimumReward = 0.005; // Minimum reward for validators

    /// <summary>
    ///     Gets the gas factor used to scale gas calculations.
    /// </summary>
    public static int GasFactor { get; } = 1000;

    /// <summary>
    ///     Gets or sets the calculated gas value.
    /// </summary>
    public int Gas { get; private set; }

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

        Gas = BaseGasTransaction + (dataLength + infoLength + senderLength) * GasPerCharacter / GasFactor;

        if (Blockchain.CurrentNetworkLoad > 0.75)
            Gas = (int)(Gas * 1.2);
        else if (Blockchain.CurrentNetworkLoad < 0.25)
            Gas = (int)(Gas * 0.8);
         
    }

    /// <summary>
    ///     Calculates the gas required for a smart contract transaction based on the length
    ///     of the serialized contract code, with adjustments for code size and network load.
    /// </summary>
    public void CalculateGasForContract()
    {
        var code = SerializedContractCode;
        var dataLength = string.IsNullOrEmpty(code) ? 0 : code.Length;

        Gas = BaseGasContract + dataLength * GasPerCharacter / GasFactor;

        if (dataLength > 1000)
            Gas = (int)(Gas * 1.5);

        if (Blockchain.CurrentNetworkLoad > 0.75)
            Gas = (int)(Gas * 1.2);
        else if (Blockchain.CurrentNetworkLoad < 0.25)
            Gas = (int)(Gas * 0.8);
    }

    /// <summary>
    ///     Calculates the reward for miners based on the wallet count and decay factor.
    ///     Ensures the reward does not fall below the minimum value.
    /// </summary>
    /// <param name="walletCount">The number of wallets to consider for the decay calculation.</param>
    /// <param name="address">The address of the miner.</param>
    /// <returns>The calculated reward for the miner.</returns>
    public double CalculateMinerReward(int walletCount, string address)
    {
        if (Transaction.Balances.ContainsKey(address) && Transaction.Balances[address] == 0)
            return MinerInitialReward;

        var reward = MinerInitialReward * Math.Pow(MinerDecayFactor, walletCount);
        return Math.Max(reward, MinerMinimumReward);
    }

    /// <summary>
    ///     Calculates the reward for validators based on the wallet count and decay factor.
    ///     Ensures the reward does not fall below the minimum value.
    /// </summary>
    /// <param name="walletCount">The number of wallets to consider for the decay calculation.</param>
    /// <returns>The calculated reward for the validator.</returns>
    public double CalculateValidatorReward(int walletCount)
    {
        var reward = ValidatorInitialReward * Math.Pow(ValidatorDecayFactor, walletCount);
        return Math.Max(reward, ValidatorMinimumReward);
    }
}