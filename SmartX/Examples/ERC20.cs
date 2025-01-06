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
        AuthenticatedUsers = new Dictionary<string, string>();
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
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
        AuthenticatedUsers = new Dictionary<string, string>();

        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply;
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
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
    ///     Dictionary storing authenticated users and their hashed private keys.
    /// </summary>
    [JsonInclude]
    public Dictionary<string, string> AuthenticatedUsers { get; private protected set; }

    /// <summary>
    ///     Version of the contract.
    /// </summary>
    [JsonInclude]
    public string Version { get; private protected set; }

    /// <summary>
    ///     Timestamp of the contract deployment.
    /// </summary>
    [JsonInclude]
    public DateTime DeploymentDate { get; private protected set; }

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
    ///     Registers a new user by linking their address with a hashed private key.
    /// </summary>
    /// <param name="address">The address of the user to register.</param>
    /// <param name="privateKey">The private key to authenticate the user.</param>
    /// <returns>True if registration is successful; otherwise, false.</returns>
    public bool RegisterUser(string address, string privateKey)
    {
        if (!IsValidAddress(address))
        {
            Log("Registration failed: Invalid address format.");
            return false;
        }

        if (AuthenticatedUsers.ContainsKey(address))
        {
            Log($"Registration failed: Address '{address}' is already registered.");
            return false;
        }

        AuthenticatedUsers[address] = HashKey(privateKey);
        if (!Balances.ContainsKey(address)) Balances[address] = 0;
        Log($"User {address} registered successfully.");
        return true;
    }

    /// <summary>
    ///     Validates the format of an address.
    /// </summary>
    /// <param name="address">The address to validate.</param>
    /// <returns>True if the address is valid; otherwise, false.</returns>
    private bool IsValidAddress(string address)
    {
        return !string.IsNullOrWhiteSpace(address) && address.Length >= 5 && address.StartsWith("smartX");
    }

    /// <summary>
    ///     Hashes a private key using SHA-256.
    /// </summary>
    /// <param name="key">The key to hash.</param>
    /// <returns>The hashed key as a base64 string.</returns>
    private string HashKey(string key)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    /// <summary>
    ///     Authenticates a user by comparing their hashed private key with the stored hash.
    /// </summary>
    /// <param name="address">The user's address.</param>
    /// <param name="privateKey">The private key for authentication.</param>
    /// <returns>True if authentication is successful; otherwise, false.</returns>
    private bool IsAuthenticated(string address, string privateKey)
    {
        var hashedKey = HashKey(privateKey);
        return AuthenticatedUsers.ContainsKey(address) && AuthenticatedUsers[address] == hashedKey;
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