using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     Represents a reward transaction in the blockchain, used for distributing rewards
///     to miners or validators.
/// </summary>
public class RewardTransaction : Transaction
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RewardTransaction" /> class,
    ///     calculating and transferring the reward to the recipient.
    /// </summary>
    /// <param name="chain">The blockchain instance.</param>
    /// <param name="recipient">The address of the reward recipient.</param>
    /// <param name="validator">
    ///     A flag indicating whether the reward is for a validator.
    ///     If false, the reward is for a miner.
    /// </param>
    /// <param name="sender">The sender of the transaction (default is the system address).</param>
    internal RewardTransaction(Blockchain? chain, string recipient, bool validator = false,
        string sender = Blockchain.SystemAddress)
    {
        Sender = sender;
        Recipient = recipient;
        TransactionType = validator ? TransactionTypes.ValidatorReward : TransactionTypes.MinerReward;

        // Rewards are only distributed if the balance count is below 50,000
        // and the sender is the system address.
        if (Balances.Count < 50000 && Sender == Blockchain.SystemAddress)
        {
            decimal reward = 0;
            var calculator = new GasAndRewardCalculator();

            // Calculate rewards for miner and validator
            var minerReward = calculator.CalculateMinerReward(Balances.Count, Config.Default.MinerAddress);
            var validatorReward = calculator.CalculateValidatorReward(Balances.Count);

            // Determine the reward based on the recipient type (validator or miner)
            reward = validator ? validatorReward : minerReward + reward;

            // Settle Founders - Ensure the first 10 founders get 10,000,000 each
            var foundersReward = 10_000_000;
            if (Balances[Blockchain.SystemAddress] > TotalSupply - 10 * foundersReward)
                reward = foundersReward;

            if (Balances[Blockchain.SystemAddress] - reward > 0)
                // Perform the transfer and update the reward property
                if (Transfer(chain, Blockchain.SystemAddress, recipient, reward))
                    Reward = reward;
        }
    }

    /// <summary>
    ///     Gets the reward amount distributed in this transaction.
    /// </summary>
    public decimal Reward { get; internal set; }

    /// <summary>
    ///     Transfers the specified amount from the sender to the recipient within the blockchain.
    /// </summary>
    /// <param name="chain">The blockchain instance.</param>
    /// <param name="sender">The address of the sender.</param>
    /// <param name="recipient">The address of the recipient.</param>
    /// <param name="amount">The amount to be transferred.</param>
    /// <returns>True if the transfer is successful; otherwise, false.</returns>
    private bool Transfer(Blockchain? chain, string sender, string recipient, decimal amount)
    {
        UpdateBalancesFromChain(chain);

        // Check if the sender has sufficient balance
        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{sender}'.");
            return false;
        }

        // Create a new transfer transaction
        var transferTransaction = new Transaction
        {
            Sender = sender,
            Recipient = recipient,
            Amount = amount,
            Timestamp = DateTime.UtcNow,
            TransactionType = TransactionTypes.MinerReward
        };
        if (chain != null) chain.AddTransaction(transferTransaction);

        // Update balances
        Balances[sender] -= amount;
        Balances.TryAdd(recipient, 0);
        Balances[recipient] += amount;

        Log($"Transfer successful: {amount} tokens from {sender} to {recipient}.");
        return true;
    }
}