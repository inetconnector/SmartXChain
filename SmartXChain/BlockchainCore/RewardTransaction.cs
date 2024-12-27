using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class RewardTransaction : Transaction
{
    internal RewardTransaction(string recipient, double reward, string sender=Blockchain.SystemAddress)
    {
        Sender = sender;
        if (Balances.Count < 50000 && Sender == Blockchain.SystemAddress)
        {
            var minerReward = CalculateMinerReward(Balances.Count);
            Transfer(Blockchain.SystemAddress, recipient,  minerReward + (double)reward);
        }
    }

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
    private double CalculateMinerReward(int walletCount)
    {
        const double initialReward = 0.1;
        const double decayFactor = 0.98; // Decrease reward as more wallets join

        // Reward decays as wallet count increases but only if walletCount > 0
        if (Balances.ContainsKey(Config.Default.MinerAddress) && Balances[Config.Default.MinerAddress] == 0)
        {
            return initialReward;
        }

        return (double)(initialReward * Math.Pow(decayFactor, walletCount));
    }

}