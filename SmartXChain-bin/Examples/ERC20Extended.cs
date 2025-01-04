using System.Text.Json.Serialization;

public class ERC20Extended : ERC20Token
{
    public ERC20Extended()
    {
    }
    public ERC20Extended(string name, string symbol, uint decimals, decimal initialSupply, string owner)
    {
        Name = name;
        Symbol = symbol;
        Decimals = decimals;
        TotalSupply = initialSupply;
        Balances = new Dictionary<string, decimal>();
        Allowances = new Dictionary<string, Dictionary<string, decimal>>();
        AuthenticatedUsers = new Dictionary<string, string>();

        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply;
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
        Owner = owner;
    }

    [JsonInclude] public List<string> FrozenAccounts { get; private protected set; }
    [JsonInclude] public string Owner { get; private protected set; }
    [JsonInclude] public bool TransfersPaused { get; private protected set; }
    public event Action<string, decimal>? OnMint;
    public event Action<string, decimal>? OnBurn;
    public event Action? OnTransfersPaused;
    public event Action? OnTransfersResumed;
    public event Action<string, string, decimal>? OnTransfer;
    public event Action<string>? OnAccountFrozen;
    public event Action<string>? OnAccountUnfrozen;

    // Burn tokens
    public bool Burn(decimal amount, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey))
        {
            Log($"Burn failed: Unauthorized action by '{owner}'.");
            return false;
        }

        if (!Balances.ContainsKey(owner) || Balances[owner] < amount)
        {
            Log($"Burn failed: Insufficient balance in account '{owner}'.");
            return false;
        }

        Balances[owner] -= amount;
        TotalSupply -= amount;

        OnBurn?.Invoke(owner, amount);

        Log($"Burn successful: {amount} tokens burned by {owner}. TotalSupply is now {TotalSupply}.");
        return true;
    }

    // Mint tokens
    public bool Mint(decimal amount, string to, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            Log($"Mint failed: Unauthorized action by '{owner}'. Only the token owner can mint tokens.");
            return false;
        }

        if (!Balances.ContainsKey(to)) Balances[to] = 0;

        Balances[to] += amount;
        TotalSupply += amount;

        OnMint?.Invoke(to, amount);

        Log($"Mint successful: {amount} tokens minted and assigned to {to}. TotalSupply is now {TotalSupply}.");
        return true;
    }

    public void PauseTransfers(string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            Log($"PauseTransfers failed: Unauthorized action by '{owner}'. Only the token owner can pause transfers.");
            return;
        }

        TransfersPaused = true;

        Log("Transfers have been paused.");

        OnTransfersPaused?.Invoke();
    }


    public void ResumeTransfers(string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            Log(
                $"ResumeTransfers failed: Unauthorized action by '{owner}'. Only the token owner can resume transfers.");
            return;
        }

        TransfersPaused = false;
        Log("Transfers have been resumed.");

        OnTransfersResumed?.Invoke();
    }

    public new bool Transfer(string from, string to, decimal amount, string privateKey)
    {
        if (TransfersPaused)
        {
            Log("Transfer failed: Transfers are currently paused.");
            return false;
        }

        if (FrozenAccounts.Contains(from))
        {
            Log($"Transfer failed: Account {from} is frozen.");
            return false;
        }

        if (!IsAuthenticated(from, privateKey))
        {
            Log($"Transfer failed: Unauthorized action by '{from}'.");
            return false;
        }

        if (!Balances.ContainsKey(from) || Balances[from] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{from}'.");
            return false;
        }

        if (from == to)
        {
            Log($"Transfer failed: Cannot transfer to the same account '{from}'.");
            return false;
        }

        Balances[from] -= amount;
        if (!Balances.ContainsKey(to)) Balances[to] = 0;
        Balances[to] += amount;

        Log($"Transfer successful: {amount} tokens from {from} to {to}.");

        OnTransfer?.Invoke(from, to, amount);

        return true;
    }


    // Freeze account
    public void FreezeAccount(string account, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            Log($"FreezeAccount failed: Unauthorized action by '{owner}'. Only the token owner can freeze accounts.");
            return;
        }

        FrozenAccounts.Add(account);
        Log($"Account {account} has been frozen.");

        OnAccountFrozen?.Invoke(account);
    }

    public void UnfreezeAccount(string account, string owner, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey) || owner != Owner)
        {
            Log(
                $"UnfreezeAccount failed: Unauthorized action by '{owner}'. Only the token owner can unfreeze accounts.");
            return;
        }

        FrozenAccounts.Remove(account);
        Log($"Account {account} has been unfrozen.");

        OnAccountUnfrozen?.Invoke(account);
    }


    // Get total token holders
    public int GetTotalTokenHolders()
    {
        var holders = Balances.Count(b => b.Value > 0);
        Log($"Total token holders: {holders}.");
        return holders;
    }

    // Transfer ownership
    public void TransferOwnership(string newOwner, string currentOwner, string privateKey)
    {
        if (!IsAuthenticated(currentOwner, privateKey) || currentOwner != Owner)
        {
            Log("Ownership transfer failed: Unauthorized action.");
            return;
        }

        Owner = newOwner;
        Log($"Ownership transferred successfully to {newOwner}.");
    }
}