using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

/// <summary>
///     Represents a basic implementation of an ERC20 token contract with functionality for transfers, approvals,
///     allowances, and user registration.
/// </summary>
public class ERC20Token : Contract
{
    /// <summary>
    ///     Default constructor. Initializes the token contract with default values for balances, allowances,
    ///     authenticated users, and version information.
    /// </summary>
    public ERC20Token()
    {
        Balances = new Dictionary<string, decimal>();
        Allowances = new Dictionary<string, Dictionary<string, decimal>>(); 
    }

    /// <summary>
    ///     Parameterized constructor. Sets up the token with specific parameters and assigns the initial supply
    ///     to the owner's balance.
    /// </summary>
    /// <param name="name">Name of the token.</param>
    /// <param name="symbol">Symbol of the token (e.g., "GLD").</param>
    /// <param name="decimals">Number of decimals for the token.</param>
    /// <param name="initialSupply">Initial supply of the token.</param>
    /// <param name="owner">Address of the token owner.</param>
    public ERC20Token(string name, string symbol, uint decimals, decimal initialSupply, string owner)
    {
        Name = name;
        Symbol = symbol;
        Decimals = decimals;
        TotalSupply = initialSupply;
        Balances = new Dictionary<string, decimal>();
        Allowances = new Dictionary<string, Dictionary<string, decimal>>();
         
        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply; 
    }
      

    /// <summary>
    ///     Symbol of the token (e.g., "GLD").
    /// </summary>
    [JsonInclude]
    public string Symbol { get; private protected set; }

    /// <summary>
    ///     Number of decimal places for the token.
    /// </summary>
    [JsonInclude]
    public uint Decimals { get; private protected set; }

    /// <summary>
    ///     Total supply of tokens in the contract.
    /// </summary>
    [JsonInclude]
    public decimal TotalSupply { get; private protected set; }

    /// <summary>
    ///     Internal dictionary storing the balance of each account.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, decimal> Balances { get; private protected set; }

    /// <summary>
    ///     Internal dictionary managing allowances where a spender can spend on behalf of an owner.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, Dictionary<string, decimal>> Allowances { get; private protected set; }



    /// <summary>
    ///     Exposes a read-only view of account balances.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, decimal> GetBalances => Balances;

    /// <summary>
    ///     Exposes a read-only view of allowances between accounts.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> GetAllowances =>
        Allowances.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, decimal>)v.Value);

    /// <summary>
    ///     Event triggered when a transfer between accounts occurs.
    /// </summary>
    public event Action<string, string, decimal> TransferEvent;

    /// <summary>
    ///     Event triggered when a transfer is executed on behalf of another account using an allowance.
    /// </summary>
    public event Action<string, string, string, decimal> TransferFromEvent;

    /// <summary>
    ///     Event triggered when an allowance is set or updated.
    /// </summary>
    public event Action<string, string, decimal> ApprovalEvent;

    /// <summary>
    ///     Retrieves the balance of a specific account.
    /// </summary>
    /// <param name="account">The address of the account to query.</param>
    /// <returns>The balance of the account.</returns>
    public decimal BalanceOf(string account)
    {
        return Balances.ContainsKey(account) ? Balances[account] : 0;
    }

    /// <summary>
    ///     Retrieves the allowance set by an owner for a specific spender.
    /// </summary>
    /// <param name="owner">The address of the owner.</param>
    /// <param name="spender">The address of the spender.</param>
    /// <returns>The remaining allowance.</returns>
    public decimal Allowance(string owner, string spender)
    {
        if (Allowances.ContainsKey(owner) && Allowances[owner].ContainsKey(spender)) return Allowances[owner][spender];
        return 0;
    }
     

    /// <summary>
    ///     Transfers tokens from one account to another, ensuring proper authentication and balance checks.
    /// </summary>
    public bool Transfer(string from, string to, decimal amount, string privateKey)
    {
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

        TransferEvent?.Invoke(from, to, amount);
        return true;
    }

    /// <summary>
    ///     Approves a spender to spend a specified amount on behalf of the owner.
    /// </summary>
    public bool Approve(string owner, string spender, decimal amount, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey))
        {
            Log($"Approval failed: Unauthorized action by '{owner}'.");
            return false;
        }

        if (!Balances.ContainsKey(owner) || Balances[owner] < amount)
        {
            Log($"Approval failed: Insufficient balance in account '{owner}'.");
            return false;
        }

        if (!Allowances.ContainsKey(owner)) Allowances[owner] = new Dictionary<string, decimal>();
        Allowances[owner][spender] = amount;

        Log($"Approval successful: {spender} can spend {amount} tokens from {owner}.");
        ApprovalEvent?.Invoke(owner, spender, amount); // Trigger event
        return true;
    }

    /// <summary>
    ///     Executes a transfer on behalf of another account, deducting from the spender's allowance.
    /// </summary>
    public bool TransferFrom(string spender, string from, string to, decimal amount, string spenderKey)
    {
        if (!IsAuthenticated(spender, spenderKey))
        {
            Log($"TransferFrom failed: Unauthorized action by '{spender}'.");
            return false;
        }

        var allowedAmount = Allowance(from, spender);
        if (allowedAmount < amount)
        {
            Log($"TransferFrom failed: Allowance of {spender} insufficient for {amount} tokens.");
            return false;
        }

        if (!Transfer(from, to, amount, AuthenticatedUsers[from])) return false;

        Allowances[from][spender] -= amount;
        Log($"TransferFrom successful: {spender} transferred {amount} tokens from {from} to {to}.");

        TransferFromEvent?.Invoke(spender, from, to, amount);
        return true;
    }
}