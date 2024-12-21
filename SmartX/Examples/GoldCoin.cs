// Name: GoldCoin
// License: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Threading.Tasks;
public class GoldCoin
{
    private static readonly HttpClient HttpClient = new();

    // Constructor: Initializes default values for balances, allowances, and investment percentages.
    public GoldCoin()
    {
        CurrentGoldPrice = 0;
        LastGoldPriceUpdate = DateTime.MinValue;

        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();
    }

    // Constructor: Initializes token properties and assigns the initial supply to the owner's balance.
    public GoldCoin(string name, string symbol, uint decimals, ulong initialSupply, string owner)
    {
        CurrentGoldPrice = 0;

        Name = name;
        Symbol = symbol;
        Decimals = decimals;
        TotalSupply = initialSupply;
        Balances = new Dictionary<string, ulong>();
        Allowances = new Dictionary<string, Dictionary<string, ulong>>();

        // Assign initial supply to the owner's balance
        Balances[owner] = initialSupply;
    }

    [JsonInclude] public DateTime LastGoldPriceUpdate { get; private set; }

    // Property: Token name.
    [JsonInclude] public decimal CurrentGoldPrice { get; private set; }

    // Property: Token name.
    [JsonInclude] public string Name { get; private set; }

    // Property: Token symbol.
    [JsonInclude] public string Symbol { get; private set; }

    // Property: Number of decimals for the token.
    [JsonInclude] public uint Decimals { get; private set; }

    // Property: Total supply of the token.
    [JsonInclude] public ulong TotalSupply { get; private set; }

    // Private: Stores balances of accounts.
    [JsonInclude] private Dictionary<string, ulong> Balances { get; set; }

    // Private: Stores allowances for accounts to spend on behalf of others.
    [JsonInclude] private Dictionary<string, Dictionary<string, ulong>> Allowances { get; set; }

    // Property: Gets the balances as a read-only dictionary.
    [JsonIgnore] public IReadOnlyDictionary<string, ulong> GetBalances => Balances;

    // Property: Gets the allowances as a read-only dictionary.
    [JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ulong>> GetAllowances =>
        Allowances.ToDictionary(k => k.Key, v => (IReadOnlyDictionary<string, ulong>)v.Value);

    // Method: Returns the balance of a specific account.
    public ulong BalanceOf(string account)
    {
        return Balances.ContainsKey(account) ? Balances[account] : 0;
    }

    // Method: Logs a message to the console for the token.
    private void Log(string message)
    {
        Console.WriteLine($"[GoldCoin] {message}");
    }

    public bool Transfer(string sender, string to, ulong amount)
    {
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

        // Ensure the gold price is updated with a timeout
        var goldPriceUpdated = false;
        try
        {
            var task = Task.Run(async () => await EnsureGoldPriceUpdatedAsync());
            goldPriceUpdated = task.Wait(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            Log($"Failed to update gold price: {ex.Message}");
        }

        if (!goldPriceUpdated)
        {
            Log("Transfer failed: Gold price update timed out.");
            return false;
        }

        // Perform the transfer after ensuring the gold price is updated
        Balances[sender] -= amount;
        if (!Balances.ContainsKey(to)) Balances[to] = 0;
        Balances[to] += amount;

        Log($"Transfer successful: {amount} tokens sender {sender} to {to}.");
        Log($"TotalSupply: {TotalSupply}");
        return true;
    }

    // Method: Approves a spender to transfer up to a specified amount of tokens on behalf of the owner.
    public bool Approve(string owner, string spender, ulong amount)
    {
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

    // Method: Returns the remaining allowance for a spender to transfer tokens on behalf of the owner.
    public ulong Allowance(string owner, string spender)
    {
        if (Allowances.ContainsKey(owner) && Allowances[owner].ContainsKey(spender))
            return Allowances[owner][spender];
        return 0;
    }

    // Method: Transfers tokens from a sender to a recipient via a spender's allowance.
    public bool TransferFrom(string spender, string sender, string to, ulong amount)
    {
        var allowedAmount = Allowance(sender, spender);
        if (allowedAmount < amount)
        {
            Log($"TransferFrom failed: Allowance of {spender} insufficient for {amount} tokens.");
            return false;
        }

        if (!Transfer(sender, to, amount)) return false;

        Allowances[sender][spender] -= amount;
        Log($"TransferFrom successful: {spender} transferred {amount} tokens sender {sender} to {to}.");
        return true;
    }


    // Private Method: Reduces the token supply proportionally.
    private void BurnTokens(decimal percentage)
    {
        if (percentage <= 0 || percentage > 100)
        {
            Log("Burn failed: Percentage must be between 0 and 100.");
            return;
        }

        ulong totalBurnAmount = 0;
        const ulong minimumTotalSupply = 1000; // Set a minimum total supply (example: 1000 tokens)

        foreach (var account in Balances.Keys.ToList())
        {
            var burnAmount = (ulong)(Balances[account] * (percentage / 100));
            Balances[account] -= burnAmount;
            totalBurnAmount += burnAmount;
        }

        // Ensure we don't go below the minimum total supply
        if (TotalSupply - totalBurnAmount < minimumTotalSupply)
        {
            Log("Burn operation limited: Total supply cannot drop below the minimum threshold.");
            totalBurnAmount = TotalSupply - minimumTotalSupply;
            foreach (var account in Balances.Keys.ToList())
            {
                // Adjust burn amounts proportionally to enforce the minimum total supply
                var adjustedBurnAmount = (ulong)(Balances[account] * ((decimal)totalBurnAmount / TotalSupply));
                Balances[account] -= adjustedBurnAmount;
            }
        }

        TotalSupply -= totalBurnAmount;
        Log($"Burn successful: {totalBurnAmount} tokens burned proportionally. Total supply: {TotalSupply}");
    }


    // Private Method: Increases the token supply proportionally.
    private void MintTokens(decimal percentage)
    {
        if (percentage <= 0)
        {
            Log("Mint failed: Percentage must be greater than 0.");
            return;
        }

        ulong totalMintAmount = 0;

        foreach (var account in Balances.Keys.ToList())
        {
            var mintAmount = (ulong)(Balances[account] * (percentage / 100));
            Balances[account] += mintAmount;
            totalMintAmount += mintAmount;
        }

        TotalSupply += totalMintAmount;
        Log($"Mint successful: {totalMintAmount} tokens minted proportionally.");
        Log($"TotalSupply: {TotalSupply}");
    }

    // Method: Updates the gold price and adjusts the token supply accordingly.
    private void UpdateGoldPrice(decimal newGoldPrice)
    {
        if (newGoldPrice <= 0)
        {
            Log("Update failed: Gold price must be greater than zero.");
            return;
        }

        if (CurrentGoldPrice == 0)
        {
            // Initial gold price
            CurrentGoldPrice = newGoldPrice;
            LastGoldPriceUpdate = DateTime.Now;
            Log($"Gold price initialized to {newGoldPrice} USD per ounce.");
            return;
        }

        if (newGoldPrice > CurrentGoldPrice)
        {
            // Gold price has increased -> Execute BurnTokens
            var increasePercentage = (newGoldPrice - CurrentGoldPrice) / CurrentGoldPrice * 100;
            BurnTokens(increasePercentage);
        }
        else if (newGoldPrice < CurrentGoldPrice)
        {
            // Gold price has decreased -> Execute MintTokens
            var decreasePercentage = (CurrentGoldPrice - newGoldPrice) / CurrentGoldPrice * 100;
            MintTokens(decreasePercentage);
        }

        // Update the gold price
        CurrentGoldPrice = newGoldPrice;
        LastGoldPriceUpdate = DateTime.Now;
        Log($"Gold price updated to {newGoldPrice} USD per ounce.");
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
}