using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class RewardTransaction : Transaction
{
    internal RewardTransaction(string recipient, bool validator = false, string sender = Blockchain.SystemAddress)
    {
        Sender = sender;
        if (Balances.Count < 50000 && Sender == Blockchain.SystemAddress)
        {
            double reward = 0;
            var calculator = new GasAndRewardCalculator();
            var minerReward = calculator.CalculateMinerReward(Balances.Count, Config.Default.MinerAddress);
            var validatorReward = calculator.CalculateValidatorReward(Balances.Count);
            if (validator)
                reward = validatorReward;
            else
                reward = minerReward + reward;

            if (Transfer(Blockchain.SystemAddress, recipient, reward)) Reward = reward;
        }
    }

    public double Reward { get; internal set; }

    private bool Transfer(string sender, string recipient, double amount)
    {
        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{sender}'.");
            return false;
        }

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(recipient)) Balances[recipient] = 0;
        Balances[recipient] += amount;

        Log($"Transfer successful: {amount} tokens from {sender} to {recipient}.");
        return true;
    }
}