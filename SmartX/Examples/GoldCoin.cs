using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;
using System.Threading.Tasks;

/// <summary>
///     Represents a GoldCoin token that adjusts its supply dynamically based on the gold price.
///     Inherits from the ERC20Extended class.
/// </summary>
public class GoldCoin : ERC20Extended
{
    private static readonly HttpClient HttpClient = new();

    public GoldCoin()
    {
        CurrentGoldPrice = 0;
        LastGoldPriceUpdate = DateTime.MinValue;
        GoldPriceUpdateInterval = TimeSpan.FromMinutes(10); // Default interval
    }

    /// <summary>
    ///     Parameterized constructor for initializing a GoldCoin instance with specific attributes.
    /// </summary>
    /// <param name="name">The name of the token.</param>
    /// <param name="symbol">The symbol of the token.</param>
    /// <param name="decimals">Number of decimal places for the token.</param>
    /// <param name="initialSupply">Initial supply of tokens.</param>
    /// <param name="owner">Owner's address.</param>
    public GoldCoin(string name, string symbol, uint decimals, decimal initialSupply, string owner)
        : base(name, symbol, decimals, initialSupply, owner)
    {
        CurrentGoldPrice = 0;
        LastGoldPriceUpdate = DateTime.MinValue;
        GoldPriceUpdateInterval = TimeSpan.FromMinutes(10); // Default interval
    }

    [JsonInclude] public DateTime LastGoldPriceUpdate { get; private set; }
    [JsonInclude] public decimal CurrentGoldPrice { get; private set; }
    [JsonInclude] public TimeSpan GoldPriceUpdateInterval { get; private set; }

    public event Action<string, decimal>? OnGoldPriceUpdated;


    /// <summary>
    ///     Updates the interval at which the gold price can be updated.
    ///     Only the owner is authorized to modify this setting.
    /// </summary>
    /// <param name="newInterval">New time interval for updates.</param>
    /// <param name="ownerPrivateKey">Private key of the owner for authentication.</param>
    /// <returns>True if the interval was successfully updated; otherwise, false.</returns>
    public bool UpdateGoldPriceInterval(TimeSpan newInterval, string ownerPrivateKey)
    {
        if (!IsAuthenticated(Owner, ownerPrivateKey))
        {
            Log("Gold price interval update failed: Unauthorized action.");
            return false;
        }

        if (newInterval.TotalMinutes < 1)
        {
            Log("Gold price interval update failed: Interval must be at least 1 minute.");
            return false;
        }

        GoldPriceUpdateInterval = newInterval;
        Log($"Gold price update interval set to {newInterval.TotalMinutes} minutes.");
        return true;
    }


    /// <summary>
    ///     Updates the gold price to a new value and adjusts the token supply accordingly.
    ///     Only the owner is allowed to perform this operation.
    /// </summary>
    /// <param name="newGoldPrice">The new gold price in USD per ounce.</param>
    /// <param name="ownerPrivateKey">Private key of the owner for authentication.</param>
    /// <returns>True if the gold price was successfully updated; otherwise, false.</returns>
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

        var timeSinceLastUpdate = DateTime.UtcNow - LastGoldPriceUpdate;
        if (timeSinceLastUpdate < GoldPriceUpdateInterval)
        {
            Log(
                $"Gold price update failed: Updates are allowed only every {GoldPriceUpdateInterval.TotalMinutes} minutes. Time since last update: {timeSinceLastUpdate.TotalMinutes} minutes.");
            return false;
        }

        var previousPrice = CurrentGoldPrice;
        CurrentGoldPrice = newGoldPrice;
        LastGoldPriceUpdate = DateTime.UtcNow;

        OnGoldPriceUpdated?.Invoke(Owner, newGoldPrice);
        Log($"Gold price updated to {newGoldPrice} USD per ounce.");

        AdjustTokenSupplyBasedOnGoldPrice(previousPrice, newGoldPrice);
        return true;
    }

    /// <summary>
    ///     Adjusts the total token supply based on the change in gold price.
    ///     Tokens are minted or burned proportionally to the price change.
    /// </summary>
    /// <param name="previousPrice">The previous gold price.</param>
    /// <param name="newPrice">The updated gold price.</param>
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
    ///     Fetches the latest gold price from an external API and updates the token accordingly.
    ///     Only the owner can initiate this process.
    /// </summary>
    /// <param name="ownerPrivateKey">Private key of the owner for authentication.</param>
    public async Task FetchAndUpdateGoldPriceAsync(string ownerPrivateKey)
    {
        try
        {
            var apiUrl = "https://www.goldapi.io/api/XAU/USD";
            using var client = new HttpClient();


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
        catch (Exception ex)
        {
            Log($"Error fetching gold price: {ex.Message}");
        }
    }
}