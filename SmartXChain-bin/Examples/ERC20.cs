using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

public class ERC20Token
{
    public ERC20Token()
    {
        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();
        AuthenticatedUsers = new Dictionary<string, string>();
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
    }

    public ERC20Token(string name, string symbol, uint decimals, ulong initialSupply, string owner)
    {
        Name = name;
        Symbol = symbol;
        Decimals = decimals;
        TotalSupply = initialSupply;
        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();
        AuthenticatedUsers = new Dictionary<string, string>();

        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply;
        Version = "1.0.0";
        DeploymentDate = DateTime.UtcNow;
    }

    [JsonInclude] public string Name { get; private set; }
    [JsonInclude] public string Symbol { get; private set; }
    [JsonInclude] public uint Decimals { get; private set; }
    [JsonInclude] public ulong TotalSupply { get; private set; }
    [JsonInclude] private Dictionary<string, ulong> Balances { get; set; }
    [JsonInclude] private Dictionary<string, Dictionary<string, ulong>> Allowances { get; set; }
    [JsonInclude] private Dictionary<string, string> AuthenticatedUsers { get; set; } // Authentifizierungsspeicher
    [JsonInclude] public string Version { get; private set; }
    [JsonInclude] public DateTime DeploymentDate { get; private set; }

    // Exposed read-only versions
    [JsonIgnore] public IReadOnlyDictionary<string, ulong> GetBalances => Balances;

    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ulong>> GetAllowances =>
        Allowances.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, ulong>)v.Value);

    public ulong BalanceOf(string account)
    {
        return Balances.ContainsKey(account) ? Balances[account] : 0;
    }

    public ulong Allowance(string owner, string spender)
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

    private void Log(string message)
    {
        Console.WriteLine($"[Contract] {message}");
    }

    public bool Transfer(string from, string to, ulong amount, string privateKey)
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
        return true;
    }

    public bool Approve(string owner, string spender, ulong amount, string privateKey)
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

        if (!Allowances.ContainsKey(owner)) Allowances[owner] = new Dictionary<string, ulong>();
        Allowances[owner][spender] = amount;
        Log($"Approval successful: {spender} can spend {amount} tokens from {owner}.");
        return true;
    }

    public bool TransferFrom(string spender, string from, string to, ulong amount, string spenderKey)
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
        return true;
    }
}
