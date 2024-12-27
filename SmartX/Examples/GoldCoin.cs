using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;

public class GoldCoin
{
    private static readonly HttpClient HttpClient = new();

    public GoldCoin()
    {
        CurrentGoldPrice = 0;
        LastGoldPriceUpdate = DateTime.MinValue;

        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();
        AuthenticatedUsers = new Dictionary<string, string>();
    }

    public GoldCoin(string name, string symbol, uint decimals, ulong initialSupply, string owner)
    {
        CurrentGoldPrice = 0;

        Name = name;
        Symbol = symbol;
        Decimals = decimals;
        TotalSupply = initialSupply;
        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();
        AuthenticatedUsers = new Dictionary<string, string>();

        Balances[owner] = initialSupply; 
    }

    [JsonInclude] public DateTime LastGoldPriceUpdate { get; private set; }
    [JsonInclude] public decimal CurrentGoldPrice { get; private set; }
    [JsonInclude] public string Name { get; private set; }
    [JsonInclude] public string Symbol { get; private set; }
    [JsonInclude] public uint Decimals { get; private set; }
    [JsonInclude] public ulong TotalSupply { get; private set; }
    [JsonInclude] private Dictionary<string, ulong> Balances { get; set; }
    [JsonInclude] private Dictionary<string, Dictionary<string, ulong>> Allowances { get; set; }
    [JsonInclude] private Dictionary<string, string> AuthenticatedUsers { get; set; }

    [JsonIgnore] public IReadOnlyDictionary<string, ulong> GetBalances => Balances;

    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ulong>> GetAllowances =>
        Allowances.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, ulong>)v.Value);

    public ulong BalanceOf(string account)
    {
        return Balances.ContainsKey(account) ? Balances[account] : 0;
    }

    public bool RegisterUser(string address, string privateKey)
    {
        if (AuthenticatedUsers.ContainsKey(address)) return false;
        AuthenticatedUsers[address] = HashKey(privateKey);
        if (!Balances.ContainsKey(address)) Balances[address] = 0;
        Log($"User {address} registered successfully.");
        return true;
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
        Console.WriteLine($"[GoldCoin] {message}");
    }

    public bool Transfer(string sender, string to, ulong amount, string privateKey)
    {
        if (!IsAuthenticated(sender, privateKey))
        {
            Log($"Transfer failed: Unauthorized action by '{sender}'.");
            return false;
        }

        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{sender}'. Required: {amount}.");
            return false;
        }

        if (sender == to)
        {
            Log($"Transfer failed: Cannot transfer to the same account '{sender}'.");
            return false;
        }

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(to)) Balances[to] = 0;
        Balances[to] += amount;

        Log($"Transfer successful: {amount} tokens sender {sender} to {to}.");
        Log($"TotalSupply: {TotalSupply}");
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
        Log($"Approval successful: {spender} can spend {amount} tokens sender {owner}.");
        return true;
    }

    public ulong Allowance(string owner, string spender)
    {
        if (Allowances.ContainsKey(owner) && Allowances[owner].ContainsKey(spender))
            return Allowances[owner][spender];
        return 0;
    }

    public bool TransferFrom(string spender, string sender, string to, ulong amount, string spenderKey)
    {
        if (!IsAuthenticated(spender, spenderKey))
        {
            Log($"TransferFrom failed: Unauthorized action by '{spender}'.");
            return false;
        }

        var allowedAmount = Allowance(sender, spender);
        if (allowedAmount < amount)
        {
            Log($"TransferFrom failed: Allowance of {spender} insufficient for {amount} tokens.");
            return false;
        }

        if (!Transfer(sender, to, amount, AuthenticatedUsers[sender])) return false;

        Allowances[sender][spender] -= amount;
        Log($"TransferFrom successful: {spender} transferred {amount} tokens sender {sender} to {to}.");
        return true;
    }

    private async Task EnsureGoldPriceUpdatedAsync()
    {
        try
        {
            var apiUrl = "https://www.goldapi.io/api/XAU/USD";
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-access-token", "goldapi-43irpsm4vsl288-io");
                var response = await client.GetStringAsync(apiUrl);
                var json = JsonDocument.Parse(response);

                if (json.RootElement.TryGetProperty("price", out var priceElement) && priceElement.GetDecimal() > 0)
                {
                    var newGoldPrice = priceElement.GetDecimal();
                    UpdateGoldPrice(newGoldPrice);
                }
                else
                {
                    Log("Failed to fetch or parse gold price from API.");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error fetching gold price: {ex.Message}");
        }
    }

    private void UpdateGoldPrice(decimal newGoldPrice)
    {
        if (newGoldPrice <= 0)
        {
            Log("Update failed: Gold price must be greater than zero.");
            return;
        }

        CurrentGoldPrice = newGoldPrice;
        LastGoldPriceUpdate = DateTime.Now;
        Log($"Gold price updated to {newGoldPrice} USD per ounce.");
    }
}
