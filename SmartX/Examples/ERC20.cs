using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

public class ERC20Token : Contract
{
    public ERC20Token()
    {
        Balances = new Dictionary<string, decimal>();
        Allowances = new Dictionary<string, Dictionary<string, decimal>>();
        AuthenticatedUsers = new Dictionary<string, string>();
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
    }

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

    [JsonInclude] public string Symbol { get; private protected set; }
    [JsonInclude] public uint Decimals { get; private protected set; }
    [JsonInclude] public decimal TotalSupply { get; private protected set; }
    [JsonInclude] private protected Dictionary<string, decimal> Balances { get; set; }
    [JsonInclude] private protected Dictionary<string, Dictionary<string, decimal>> Allowances { get; set; }
    [JsonInclude] private protected Dictionary<string, string> AuthenticatedUsers { get; set; }
    [JsonInclude] public string Version { get; private protected set; }
    [JsonInclude] public DateTime DeploymentDate { get; private protected set; }

    // Exposed read-only versions
    [JsonIgnore] public IReadOnlyDictionary<string, decimal> GetBalances => Balances;

    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> GetAllowances =>
        Allowances.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, decimal>)v.Value);

    public event Action<string, string, decimal> TransferEvent;
    public event Action<string, string, string, decimal> TransferFromEvent;
    public event Action<string, string, decimal> ApprovalEvent;

    public decimal BalanceOf(string account)
    {
        return Balances.ContainsKey(account) ? Balances[account] : 0;
    }

    public decimal Allowance(string owner, string spender)
    {
        if (Allowances.ContainsKey(owner) && Allowances[owner].ContainsKey(spender)) return Allowances[owner][spender];
        return 0;
    }

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

    private bool IsValidAddress(string address)
    {
        return !string.IsNullOrWhiteSpace(address) && address.Length >= 5 && address.StartsWith("smartX");
    }

    private string HashKey(string key)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    private bool IsAuthenticated(string address, string privateKey)
    {
        var hashedKey = HashKey(privateKey);
        return AuthenticatedUsers.ContainsKey(address) && AuthenticatedUsers[address] == hashedKey;
    }

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
        ApprovalEvent?.Invoke(owner, spender, amount); // Event auslösen
        return true;
    }

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