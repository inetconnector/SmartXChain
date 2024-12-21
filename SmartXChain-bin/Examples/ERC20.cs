//Name: SmartXchain Token
//License: MIT

using System.Text.Json.Serialization;

public class ERC20Token
{
    public ERC20Token()
    {
        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();
    }

    public ERC20Token(string name, string symbol, uint decimals, ulong initialSupply, string owner)
    {
        Name = name;
        Symbol = symbol;
        Decimals = decimals;
        TotalSupply = initialSupply;
        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();

        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply;
    }


    [JsonInclude] public string Name { get; private set; }

    [JsonInclude] public string Symbol { get; private set; }

    [JsonInclude] public uint Decimals { get; private set; }

    [JsonInclude] public ulong TotalSupply { get; private set; }

    [JsonInclude] private Dictionary<string, ulong> Balances { get; set; }

    [JsonInclude] private Dictionary<string, Dictionary<string, ulong>> Allowances { get; set; }

    // Exposed read-only versions
    [JsonIgnore] // Avoid duplication in serialization
    public IReadOnlyDictionary<string, ulong> GetBalances => Balances;

    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ulong>> GetAllowances =>
        Allowances.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, ulong>)v.Value);

    // Methods
    public ulong BalanceOf(string account)
    {
        return Balances.ContainsKey(account) ? Balances[account] : 0;
    }

    private void Log(string message)
    {
        Console.WriteLine($"[Contract] {message}");
    }

    public bool Transfer(string from, string to, ulong amount)
    {
        if (!Balances.ContainsKey(from) || Balances[from] < amount)
        {
            var currentBalance = Balances.ContainsKey(from) ? Balances[from] : 0;
            Log(
                $"Transfer failed: Insufficient balance in account '{from}'.\nCurrent balance: {currentBalance}, required: {amount}.");
            return false;
        }

        if (from == to)
        {
            Log($"Transfer failed: Cannot transfer to the same account '{from}'.\nCurrent balance: {Balances[from]}.");
            return false;
        }

        Balances[from] -= amount;
        if (!Balances.ContainsKey(to)) Balances[to] = 0;
        Balances[to] += amount;

        Log($"Transfer successful: {amount} tokens from {from} to {to}.");

        return true;
    }

    public bool Approve(string owner, string spender, ulong amount)
    {
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

    public ulong Allowance(string owner, string spender)
    {
        if (Allowances.ContainsKey(owner) && Allowances[owner].ContainsKey(spender)) return Allowances[owner][spender];
        return 0;
    }

    public bool TransferFrom(string spender, string from, string to, ulong amount)
    {
        var allowedAmount = Allowance(from, spender);
        if (allowedAmount < amount)
        {
            Log($"TransferFrom failed: Allowance of {spender} insufficient for {amount} tokens.");
            return false;
        }

        if (!Transfer(from, to, amount)) return false;

        Allowances[from][spender] -= amount;
        Log($"TransferFrom successful: {spender} transferred {amount} tokens from {from} to {to}.");
        return true;
    }
}