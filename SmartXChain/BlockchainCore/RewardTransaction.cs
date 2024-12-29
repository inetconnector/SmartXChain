using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class RewardTransaction : Transaction
{
    internal RewardTransaction(Blockchain chain, string recipient, bool validator = false, string sender = Blockchain.SystemAddress)
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

            if (Transfer(chain, Blockchain.SystemAddress, recipient, reward)) Reward = reward;
        }
    }

    public double Reward { get; internal set; }

    private bool Transfer(Blockchain chain, string sender, string recipient, double amount)
    {
        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{sender}'.");
            return false;
        }

        UpdateBalancesFromChain(chain);

        var transferTransaction = new Transaction
        {
            Sender = recipient,
            Recipient = sender,
            Amount = amount,
            Timestamp = DateTime.UtcNow
        };

        chain.AddTransaction(transferTransaction);

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(recipient)) Balances[recipient] = 0;
        Balances[recipient] += amount;

        Log($"Transfer successful: {amount} tokens from {sender} to {recipient}.");
        return true;
    }
}