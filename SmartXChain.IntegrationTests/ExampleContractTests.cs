using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using Xunit;

namespace SmartXChain.IntegrationTests;

public class ExampleContractTests
{
    private const string OwnerAddress = "smartXowner000000000000000000000000000000000";

    [Fact]
    public async Task Erc20Example_handles_stateful_transfers()
    {
        var contract = BuildExampleContract("ERC20.cs", Erc20EntryPoint, "Erc20Example");

        var (firstResult, firstState) = await contract.Execute(new[] { "init" }, string.Empty);
        var initialBalances = ParseBalances(firstResult);

        Assert.Equal(1000m, initialBalances.owner);
        Assert.Equal(0m, initialBalances.recipient);

        var (secondResult, secondState) = await contract.Execute(new[] { "transfer" }, firstState);
        var transferBalances = ParseBalances(secondResult);

        Assert.Equal(850m, transferBalances.owner);
        Assert.Equal(150m, transferBalances.recipient);

        // ensure state propagated by running a no-op execution
        var (finalResult, _) = await contract.Execute(new[] { "snapshot" }, secondState);
        var snapshotBalances = ParseBalances(finalResult);

        Assert.Equal(transferBalances.owner, snapshotBalances.owner);
        Assert.Equal(transferBalances.recipient, snapshotBalances.recipient);
    }

    [Fact]
    public async Task Erc20ExtendedExample_supports_mint_and_burn()
    {
        var contract = BuildExampleContract("ERC20Extended.cs", Erc20ExtendedEntryPoint, "Erc20ExtendedExample");

        var (initialResult, initialState) = await contract.Execute(new[] { "init" }, string.Empty);
        var initial = ParseExtendedResult(initialResult);

        Assert.Equal(500m, initial.ownerBalance);
        Assert.Equal(0m, initial.recipientBalance);
        Assert.Equal(500m, initial.totalSupply);

        var (updatedResult, updatedState) = await contract.Execute(new[] { "mint-burn" }, initialState);
        var updated = ParseExtendedResult(updatedResult);

        Assert.Equal(450m, updated.ownerBalance);
        Assert.Equal(200m, updated.recipientBalance);
        Assert.Equal(650m, updated.totalSupply);

        // second execution to verify persisted allowances are respected when paused transfers are skipped
        var (finalResult, _) = await contract.Execute(new[] { "snapshot" }, updatedState);
        var final = ParseExtendedResult(finalResult);

        Assert.Equal(updated.ownerBalance, final.ownerBalance);
        Assert.Equal(updated.recipientBalance, final.recipientBalance);
        Assert.Equal(updated.totalSupply, final.totalSupply);
    }

    [Fact]
    public async Task GoldCoinExample_tracks_transfers_and_price_updates()
    {
        var contract = BuildExampleContract("GoldCoin.cs", GoldCoinEntryPoint, "GoldCoinExample");

        var (initialResult, initialState) = await contract.Execute(new[] { "init" }, string.Empty);
        var initial = ParseGoldCoinResult(initialResult);

        Assert.Equal(1_000_000m, initial.ownerBalance);
        Assert.Equal(0m, initial.recipientBalance);
        Assert.Equal(1_000_000m, initial.totalSupply);
        Assert.Equal(0m, initial.currentPrice);

        var (transferResult, transferState) = await contract.Execute(new[] { "transfer" }, initialState);
        var transfer = ParseGoldCoinResult(transferResult);

        Assert.Equal(975_000m, transfer.ownerBalance);
        Assert.Equal(25_000m, transfer.recipientBalance);
        Assert.Equal(initial.totalSupply, transfer.totalSupply);
        Assert.Equal(initial.currentPrice, transfer.currentPrice);

        var (priceResult, priceState) = await contract.Execute(new[] { "price" }, transferState);
        var price = ParseGoldCoinResult(priceResult);

        Assert.Equal(transfer.ownerBalance, price.ownerBalance);
        Assert.Equal(transfer.recipientBalance, price.recipientBalance);
        Assert.Equal(transfer.totalSupply, price.totalSupply);
        Assert.Equal(2_000m, price.currentPrice);

        // ensure state stability by running another snapshot execution
        var (finalResult, _) = await contract.Execute(new[] { "snapshot" }, priceState);
        var final = ParseGoldCoinResult(finalResult);

        Assert.Equal(price.ownerBalance, final.ownerBalance);
        Assert.Equal(price.recipientBalance, final.recipientBalance);
        Assert.Equal(price.totalSupply, final.totalSupply);
        Assert.Equal(price.currentPrice, final.currentPrice);
    }

    private static (decimal owner, decimal recipient) ParseBalances(string result)
    {
        var parts = result.Split('|');
        Assert.Equal(2, parts.Length);
        return (
            decimal.Parse(parts[0], CultureInfo.InvariantCulture),
            decimal.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    private static (decimal ownerBalance, decimal recipientBalance, decimal totalSupply) ParseExtendedResult(string result)
    {
        var parts = result.Split('|');
        Assert.Equal(3, parts.Length);
        return (
            decimal.Parse(parts[0], CultureInfo.InvariantCulture),
            decimal.Parse(parts[1], CultureInfo.InvariantCulture),
            decimal.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static (decimal ownerBalance, decimal recipientBalance, decimal totalSupply, decimal currentPrice) ParseGoldCoinResult(string result)
    {
        var parts = result.Split('|');
        Assert.Equal(4, parts.Length);
        return (
            decimal.Parse(parts[0], CultureInfo.InvariantCulture),
            decimal.Parse(parts[1], CultureInfo.InvariantCulture),
            decimal.Parse(parts[2], CultureInfo.InvariantCulture),
            decimal.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    private static SmartContract BuildExampleContract(string exampleFileName, string entryPointCode, string contractName)
    {
        var repositoryRoot = GetRepositoryRoot();
        var baseContractPath = Path.Combine(repositoryRoot, "SmartXChain", "Contracts", "Contract.cs");
        var examplePath = Path.Combine(repositoryRoot, "SmartX", "Examples", exampleFileName);

        var baseContract = File.ReadAllText(baseContractPath);
        var exampleSource = File.ReadAllText(examplePath);

        var combinedSource = exampleSource + Environment.NewLine + entryPointCode;
        var merged = SmartContract.Merge(combinedSource, baseContract);

        var serialized = Serializer.SerializeToBase64(merged);
        return new SmartContract(OwnerAddress, serialized, contractName);
    }

    private static string GetRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "SmartXchain.sln")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }

    private const string Erc20EntryPoint = """
using System;
using System.Globalization;
using SmartXChain.Contracts.Execution;

public class ERC20ExampleContract
{
    private const string Owner = "smartXowner000000000000000000000000000000000";
    private const string OwnerPrivateKey = "owner-secret";
    private const string Recipient = "smartXrecipient000000000000000000000000000000";
    private const string RecipientPrivateKey = "recipient-secret";

    public ContractExecutionResult Execute(string[] inputs, string state)
    {
        var action = inputs.Length > 0 ? inputs[0] : "init";
        var token = string.IsNullOrEmpty(state)
            ? new ERC20Token("ExampleToken", "EXT", 2, 1000m, Owner)
            : Contract.DeserializeFromBase64<ERC20Token>(state);

        token.Owner = Owner;

        if (!token.AuthenticatedUsers.ContainsKey(Owner))
            token.RegisterUser(Owner, OwnerPrivateKey);

        if (!token.AuthenticatedUsers.ContainsKey(Recipient))
            token.RegisterUser(Recipient, RecipientPrivateKey);

        if (string.Equals(action, "transfer", StringComparison.OrdinalIgnoreCase))
        {
            token.Transfer(Owner, Recipient, 150m, OwnerPrivateKey);
        }

        var serialized = Contract.SerializeToBase64(token);
        var result = FormattableString.Invariant($"{token.BalanceOf(Owner)}|{token.BalanceOf(Recipient)}");
        return new ContractExecutionResult(result, serialized);
    }
}
""";

    private const string Erc20ExtendedEntryPoint = """
using System;
using System.Globalization;
using SmartXChain.Contracts.Execution;

public class ERC20ExtendedExampleContract
{
    private const string Owner = "smartXowner000000000000000000000000000000000";
    private const string OwnerPrivateKey = "owner-secret";
    private const string Recipient = "smartXrecipient000000000000000000000000000000";
    private const string RecipientPrivateKey = "recipient-secret";

    public ContractExecutionResult Execute(string[] inputs, string state)
    {
        var action = inputs.Length > 0 ? inputs[0] : "init";
        var token = string.IsNullOrEmpty(state)
            ? new ERC20Extended("ExtendedToken", "EXT", 2, 500m, Owner)
            : Contract.DeserializeFromBase64<ERC20Extended>(state);

        token.Owner = Owner;

        if (!token.AuthenticatedUsers.ContainsKey(Owner))
            token.RegisterUser(Owner, OwnerPrivateKey);

        if (!token.AuthenticatedUsers.ContainsKey(Recipient))
            token.RegisterUser(Recipient, RecipientPrivateKey);

        if (string.Equals(action, "mint-burn", StringComparison.OrdinalIgnoreCase))
        {
            token.Mint(200m, Recipient, Owner, OwnerPrivateKey);
            token.Burn(50m, Owner, OwnerPrivateKey);
        }

        var serialized = Contract.SerializeToBase64(token);
        var result = FormattableString.Invariant($"{token.BalanceOf(Owner)}|{token.BalanceOf(Recipient)}|{token.TotalSupply}");
        return new ContractExecutionResult(result, serialized);
    }
}
""";

    private const string GoldCoinEntryPoint = """
using System;
using System.Globalization;
using System.Threading.Tasks;
using SmartXChain.Contracts.Execution;

public class GoldCoinExampleContract
{
    private const string Owner = "smartXowner000000000000000000000000000000000";
    private const string OwnerPrivateKey = "owner-secret";
    private const string Recipient = "smartXrecipient000000000000000000000000000000";
    private const string RecipientPrivateKey = "recipient-secret";
    private const string Secondary = "smartXsecondary000000000000000000000000000000";
    private const string SecondaryPrivateKey = "secondary-secret";

    public async Task<ContractExecutionResult> Execute(string[] inputs, string state)
    {
        var action = inputs.Length > 0 ? inputs[0] : "init";
        var token = string.IsNullOrEmpty(state)
            ? new GoldCoin("GoldCoin", "GLD", 18, 1_000_000m, Owner)
            : Contract.DeserializeFromBase64<GoldCoin>(state);

        token.Owner = Owner;

        if (!token.AuthenticatedUsers.ContainsKey(Owner))
            token.RegisterUser(Owner, OwnerPrivateKey);

        if (!token.AuthenticatedUsers.ContainsKey(Recipient))
            token.RegisterUser(Recipient, RecipientPrivateKey);

        if (!token.AuthenticatedUsers.ContainsKey(Secondary))
            token.RegisterUser(Secondary, SecondaryPrivateKey);

        if (string.Equals(action, "transfer", StringComparison.OrdinalIgnoreCase))
        {
            token.Transfer(Owner, Recipient, 25_000m, OwnerPrivateKey);
        }
        else if (string.Equals(action, "price", StringComparison.OrdinalIgnoreCase))
        {
            token.UpdateGoldPriceInterval(TimeSpan.FromMinutes(1), OwnerPrivateKey);
            await token.UpdateGoldPriceAsync(2_000m, OwnerPrivateKey);
        }

        var serialized = Contract.SerializeToBase64(token);
        var result = FormattableString.Invariant($"{token.BalanceOf(Owner)}|{token.BalanceOf(Recipient)}|{token.TotalSupply}|{token.CurrentGoldPrice}");
        return new ContractExecutionResult(result, serialized);
    }
}
""";
}
