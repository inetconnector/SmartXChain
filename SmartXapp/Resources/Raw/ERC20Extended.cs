using System.Collections.Concurrent;
using System.Text.Json.Serialization;
/// <summary>
///     Extended ERC20 token class with additional features such as minting, burning, pausing transfers,
///     freezing accounts, and transferring ownership.
/// </summary>
public class ERC20Extended : ERC20Token, IERC20Token
{
    /// <summary>
    ///     Default constructor for the ERC20Extended class.
    /// </summary>
    public ERC20Extended()
    {
    }

    /// <summary>
    ///     Initializes a new instance of the ERC20Extended class with specific attributes.
    /// </summary>
    /// <param name="name">The name of the token.</param>
    /// <param name="symbol">The symbol of the token.</param>
    /// <param name="decimals">Number of decimal places for the token.</param>
    /// <param name="initialSupply">Initial supply of tokens.</param>
    /// <param name="owner">Address of the token owner.</param>
    public ERC20Extended(string name, string symbol, uint decimals, decimal initialSupply, string owner) : base(name,
        symbol, decimals, initialSupply, owner)
    {
        Name = name;

        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply;
        Owner = owner;
    }

    /// <summary>
    ///     List of accounts that are frozen and cannot perform transfers.
    /// </summary>
    [JsonInclude]
    public List<string> FrozenAccounts { get; private set; }

    /// <summary>
    ///     Indicates whether transfers are currently paused.
    /// </summary>
    [JsonInclude]
    public bool TransfersPaused { get; private set; }

    /// <summary>
    ///     Total supply of tokens in the contract.
    /// </summary>
    [JsonInclude]
    public new decimal TotalSupply { get; private set; }

    /// <summary>
    ///     Transfers tokens from one account to another.
    /// </summary>
    /// <param name="from">The sender's address.</param>
    /// <param name="to">The recipient's address.</param>
    /// <param name="amount">The amount of tokens to transfer.</param>
    /// <param name="privateKey">The private key of the sender for authentication.</param>
    /// <returns>True if the transfer was successful; otherwise, false.</returns>
    public new bool Transfer(string from, string to, decimal amount, string privateKey)
    {
        if (TransfersPaused)
        {
            LogError("Transfer failed: Transfers are currently paused.");
            return false;
        }

        if (FrozenAccounts != null && FrozenAccounts.Contains(from))
        {
            LogError($"Transfer failed: Account {from} is frozen.");
            return false;
        }

        if (!IsAuthenticated(from, privateKey))
        {
            LogError($"Transfer failed: Unauthorized action by '{from}'.");
            return false;
        }

        if (!Balances.ContainsKey(from) || Balances[from] < amount)
        {
            LogError($"Transfer failed: Insufficient balance in account '{from}'.");
            return false;
        }

        if (from == to)
        {
            LogError($"Transfer failed: Cannot transfer to the same account '{from}'.");
            return false;
        }

        Balances[from] -= amount;
        if (!Balances.ContainsKey(to)) Balances[to] = 0;
        Balances[to] += amount;

        Log($"Transfer successful: {amount} tokens from {from} to {to}.");

        OnTransfer?.Invoke(from, to, amount);

        return true;
    }

    /// <summary>
    ///     Triggered when tokens are minted.
    /// </summary>
    public event Action<string, decimal>? OnMint;

    /// <summary>
    ///     Triggered when tokens are burned.
    /// </summary>
    public event Action<string, decimal>? OnBurn;

    /// <summary>
    ///     Triggered when transfers are paused.
    /// </summary>
    public event Action? OnTransfersPaused;

    /// <summary>
    ///     Triggered when transfers are resumed.
    /// </summary>
    public event Action? OnTransfersResumed;

    /// <summary>
    ///     Triggered when a transfer occurs.
    /// </summary>
    public event Action<string, string, decimal>? OnTransfer;

    /// <summary>
    ///     Triggered when an account is frozen.
    /// </summary>
    public event Action<string>? OnAccountFrozen;

    /// <summary>
    ///     Triggered when an account is unfrozen.
    /// </summary>
    public event Action<string>? OnAccountUnfrozen;

    /// <summary>
    ///     Burns a specified amount of tokens from the owner's balance.
    /// </summary>
    /// <param name="amount">The amount of tokens to burn.</param>
    /// <param name="owner">The address of the owner initiating the burn.</param>
    /// <param name="privateKey">The private key of the owner for authentication.</param>
    /// <returns>True if the burn was successful; otherwise, false.</returns>
    public bool Burn(decimal amount, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey))
        {
            LogError($"Burn failed: Unauthorized action by '{owner}'.");
            return false;
        }

        if (!Balances.ContainsKey(owner) || Balances[owner] < amount)
        {
            LogError($"Burn failed: Insufficient balance in account '{owner}'.");
            return false;
        }

        Balances[owner] -= amount;
        TotalSupply -= amount;

        OnBurn?.Invoke(owner, amount);

        Log($"Burn successful: {amount} tokens burned by {owner}. TotalSupply is now {TotalSupply}.");
        return true;
    }

    /// <summary>
    ///     Mints a specified amount of tokens to a given account.
    /// </summary>
    /// <param name="amount">The amount of tokens to mint.</param>
    /// <param name="to">The address of the recipient account.</param>
    /// <param name="owner">The address of the owner initiating the mint.</param>
    /// <param name="privateKey">The private key of the owner for authentication.</param>
    /// <returns>True if the mint was successful; otherwise, false.</returns>
    public bool Mint(decimal amount, string to, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            LogError($"Mint failed: Unauthorized action by '{owner}'. Only the token owner can mint tokens.");
            return false;
        }

        if (!Balances.ContainsKey(to)) Balances[to] = 0;

        Balances[to] += amount;
        TotalSupply += amount;

        OnMint?.Invoke(to, amount);

        Log($"Mint successful: {amount} tokens minted and assigned to {to}. TotalSupply is now {TotalSupply}.");
        return true;
    }

    /// <summary>
    ///     Pauses all token transfers. Only the owner can initiate this action.
    /// </summary>
    /// <param name="owner">The address of the owner initiating the pause.</param>
    /// <param name="privateKey">The private key of the owner for authentication.</param>
    public void PauseTransfers(string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            LogError(
                $"PauseTransfers failed: Unauthorized action by '{owner}'. Only the token owner can pause transfers.");
            return;
        }

        TransfersPaused = true;

        Log("Transfers have been paused.");

        OnTransfersPaused?.Invoke();
    }

    /// <summary>
    ///     Resumes all token transfers. Only the owner can initiate this action.
    /// </summary>
    /// <param name="owner">The address of the owner initiating the resume.</param>
    /// <param name="privateKey">The private key of the owner for authentication.</param>
    public void ResumeTransfers(string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            LogError(
                $"ResumeTransfers failed: Unauthorized action by '{owner}'. Only the token owner can resume transfers.");
            return;
        }

        TransfersPaused = false;
        Log("Transfers have been resumed.");

        OnTransfersResumed?.Invoke();
    }

    /// <summary>
    ///     Freezes the specified account, preventing it from transferring tokens.
    /// </summary>
    /// <param name="account">The address of the account to freeze.</param>
    /// <param name="owner">The address of the owner initiating the action.</param>
    /// <param name="privateKey">The private key of the owner for authentication.</param>
    public void FreezeAccount(string account, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            LogError(
                $"FreezeAccount failed: Unauthorized action by '{owner}'. Only the token owner can freeze accounts.");
            return;
        }

        FrozenAccounts.Add(account);

        Log($"Account {account} has been frozen.");

        OnAccountFrozen?.Invoke(account);
    }

    /// <summary>
    ///     Unfreezes the specified account, allowing it to transfer tokens.
    /// </summary>
    /// <param name="account">The address of the account to unfreeze.</param>
    /// <param name="owner">The address of the owner initiating the action.</param>
    /// <param name="privateKey">The private key of the owner for authentication.</param>
    public void UnfreezeAccount(string account, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            LogError(
                $"UnfreezeAccount failed: Unauthorized action by '{owner}'. Only the token owner can unfreeze accounts.");
            return;
        }

        FrozenAccounts.Remove(account);
        Log($"Account {account} has been unfrozen.");

        OnAccountUnfrozen?.Invoke(account);
    }

    /// <summary>
    ///     Retrieves the total number of accounts holding tokens.
    /// </summary>
    /// <returns>The number of accounts with a non-zero token balance.</returns>
    public int GetTotalTokenHolders()
    {
        var holders = Balances.Count(b => b.Value > 0);
        Log($"Total token holders: {holders}.");
        return holders;
    }

    /// <summary>
    ///     Transfers ownership of the token to a new owner.
    /// </summary>
    /// <param name="newOwner">The address of the new owner.</param>
    /// <param name="currentOwner">The address of the current owner.</param>
    /// <param name="privateKey">The private key of the current owner for authentication.</param>
    public void TransferOwnership(string newOwner, string currentOwner, string privateKey)
    {
        if (!IsAuthenticated(currentOwner, privateKey) || currentOwner != Owner)
        {
            LogError("Ownership transfer failed: Unauthorized action.");
            return;
        }

        Owner = newOwner;
        Log($"Ownership transferred successfully to {newOwner}.");
    }
}