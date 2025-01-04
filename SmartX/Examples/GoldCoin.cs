using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

public class GoldCoin : ERC20Extended
{
    private static readonly HttpClient HttpClient = new();

    public GoldCoin()
    {
        CurrentGoldPrice = 0;
        LastGoldPriceUpdate = DateTime.MinValue;
    }

    /// <summary>
    /// Initializes a new instance of the GoldCoin class with specified parameters.
    /// 
    /// Example:
    /// <code>
    /// var goldCoin = new GoldCoin("GoldCoin", "GLD", 18, 1000m, "smartXOwner123", "ownerPrivateKey");
    /// 
    /// // Owner updates the gold price manually
    /// await goldCoin.UpdateGoldPriceAsync(2000m, "ownerPrivateKey");
    /// 
    /// // Or fetch and update the price from an ext. API
    /// await goldCoin.FetchAndUpdateGoldPriceAsync("ownerPrivateKey");
    /// </code>
    /// </summary>
    /// <param name="name">The name of the token.</param>
    /// <param name="symbol">The symbol of the token.</param>
    /// <param name="decimals">The number of decimals for the token.</param>
    /// <param name="initialSupply">The initial token supply.</param>
    /// <param name="owner">The address of the owner.</param>
    /// <param name="ownerPrivateKey">The private key of the owner.</param>
    public GoldCoin(string name, string symbol, uint decimals, decimal initialSupply, string owner,
        string ownerPrivateKey)
        : base(name, symbol, decimals, initialSupply, owner)
    {
        CurrentGoldPrice = 0;
        LastGoldPriceUpdate = DateTime.MinValue;
        Owner = owner;

        // Register the owner
        RegisterUser(owner, ownerPrivateKey);
    }

    [JsonInclude] public DateTime LastGoldPriceUpdate { get; private set; }
    [JsonInclude] public decimal CurrentGoldPrice { get; private set; }

    /// <summary>
    ///     Updates the current gold price and adjusts the token supply accordingly.
    ///     Only the owner can update the price.
    /// </summary>
    public async Task<bool> UpdateGoldPriceAsync(decimal newGoldPrice, string ownerPrivateKey)
    {
        if (!IsAuthenticated(Owner, ownerPrivateKey))
        {
            Log("Gold price update failed: Unauthorized action.");
            return false;
        }

        if (newGoldPrice <= 0)
        {
            Log("Gold price update failed: Price must be greater than zero.");
            return false;
        }

        var previousPrice = CurrentGoldPrice;
        CurrentGoldPrice = newGoldPrice;
        LastGoldPriceUpdate = DateTime.UtcNow;

        Log($"Gold price updated to {newGoldPrice} USD per ounce.");

        AdjustTokenSupplyBasedOnGoldPrice(previousPrice, newGoldPrice);
        return true;
    }

    private void AdjustTokenSupplyBasedOnGoldPrice(decimal previousPrice, decimal newPrice)
    {
        if (previousPrice <= 0)
        {
            Log("No token supply adjustment needed: This is the first gold price update.");
            return;
        }

        var adjustmentFactor = newPrice / previousPrice;
        var currentTotalSupply = TotalSupply;

        if (adjustmentFactor > 1) // Mint additional tokens
        {
            var mintAmount = currentTotalSupply * (adjustmentFactor - 1);
            Mint(mintAmount, Owner, Owner, AuthenticatedUsers[Owner]);

            Log($"Minted {mintAmount} tokens to align with the updated gold price.");
        }
        else if (adjustmentFactor < 1) // Burn excess tokens
        {
            var burnAmount = currentTotalSupply * (1 - adjustmentFactor);
            Burn(burnAmount, Owner, AuthenticatedUsers[Owner]);

            Log($"Burned {burnAmount} tokens to align with the updated gold price.");
        }
        else
        {
            Log("No token supply adjustment needed: Gold price remained unchanged.");
        }
    }

    /// <summary>
    ///     Fetches the gold price from an ext. API and updates it.
    /// </summary>
    public async Task FetchAndUpdateGoldPriceAsync(string ownerPrivateKey)
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
                    await UpdateGoldPriceAsync(newGoldPrice, ownerPrivateKey);
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