namespace SmartXChain.BlockchainCore;

public class GasAndRewardCalculator
{
    // Constants
    private const int BaseGasTransaction = 5;
    private const int BaseGasContract = 10;
    private const int GasPerCharacter = 2;
    private const double MinerInitialReward = 0.1;
    private const double ValidatorInitialReward = 0.05;
    private const double MinerDecayFactor = 0.98;
    private const double ValidatorDecayFactor = 0.99;
    private const double MinerMinimumReward = 0.01;
    private const double ValidatorMinimumReward = 0.005;

    public static int GasFactor { get; } = 1000;

    public int Gas { get; private set; }
    public string Data { get; set; }
    public string Info { get; set; }
    public string SerializedContractCode { get; set; }

    public void CalculateGas()
    {
        var dataLength = string.IsNullOrEmpty(Data) ? 0 : Data.Length;
        var infoLength = string.IsNullOrEmpty(Info) ? 0 : Info.Length;

        Gas = BaseGasTransaction + (dataLength + infoLength) * GasPerCharacter / GasFactor;

        if (Blockchain.CurrentNetworkLoad > 0.75)
            Gas = (int)(Gas * 1.2);
        else if (Blockchain.CurrentNetworkLoad < 0.25) Gas = (int)(Gas * 0.8);
    }

    public void CalculateGasForContract()
    {
        var code = SerializedContractCode;
        var dataLength = string.IsNullOrEmpty(code) ? 0 : code.Length;

        Gas = BaseGasContract + dataLength * GasPerCharacter / GasFactor;

        if (dataLength > 1000) Gas = (int)(Gas * 1.5);

        if (Blockchain.CurrentNetworkLoad > 0.75)
            Gas = (int)(Gas * 1.2);
        else if (Blockchain.CurrentNetworkLoad < 0.25) Gas = (int)(Gas * 0.8);
    }

    public double CalculateMinerReward(int walletCount, string address)
    {
        if (Transaction.Balances.ContainsKey(address) && Transaction.Balances[address] == 0) return MinerInitialReward;

        var reward = MinerInitialReward * Math.Pow(MinerDecayFactor, walletCount);
        return Math.Max(reward, MinerMinimumReward);
    }

    public double CalculateValidatorReward(int walletCount)
    {
        var reward = ValidatorInitialReward * Math.Pow(ValidatorDecayFactor, walletCount);
        return Math.Max(reward, ValidatorMinimumReward);
    }
}