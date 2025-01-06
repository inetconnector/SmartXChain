using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;
using Nethereum.Web3.Accounts;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class Transaction
{
    public enum TransactionTypes
    {
        NotDefined,
        NativeTransfer,
        MinerReward,
        ContractCode,
        ContractState,
        Gas,
        ValidatorReward,
        Data
    }

    private string _data;
    private string _info;
    private string _sender;

    public Transaction()
    {
        const ulong initialSupply = 10000000000;
        var owner = Blockchain.SystemAddress;
        Name = "SmartXchain";
        Symbol = "SXC";
        Decimals = 18;
        TotalSupply = initialSupply;
        TransactionType = TransactionTypes.NotDefined;

        // Assign initial supply to the owner's balance
        if (!Balances.ContainsKey(owner)) Balances.Add(owner, initialSupply);

        Version = "1.0.0";
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Holds the allowances for transactions between accounts.
    /// </summary>
    [JsonInclude]
    internal static Dictionary<string, Dictionary<string, double>> Allowances { get; } = new();

    /// <summary>
    ///     Stores authenticated users' data with hashed private keys.
    /// </summary>
    [JsonInclude]
    internal static Dictionary<string, string> AuthenticatedUsers { get; } = new();

    /// <summary>
    ///     Stores TransactionType.
    /// </summary>
    [JsonInclude]
    internal TransactionTypes TransactionType { get; set; }

    /// <summary>
    ///     Holds the balance of each account.
    /// </summary>
    [JsonInclude]
    internal static Dictionary<string, decimal> Balances { get; } = new();

    /// <summary>
    ///     Stores and retrieves the sender's address for the transaction.
    ///     Recalculates gas whenever the value is updated.
    /// </summary>
    [JsonInclude]
    internal string Sender
    {
        get => _sender;
        set
        {
            _sender = value;
            RecalculateGas();
        }
    }

    /// <summary>
    ///     Represents the recipient's address for the transaction.
    /// </summary>
    [JsonInclude]
    internal string Recipient { get; set; }

    /// <summary>
    ///     The amount of tokens to be transferred in the transaction.
    /// </summary>
    [JsonInclude]
    internal decimal Amount { get; set; }

    /// <summary>
    ///     The timestamp indicating when the transaction was created.
    ///     Defaults to the current UTC time.
    /// </summary>
    [JsonInclude]
    internal DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     The cryptographic signature of the transaction for validation purposes.
    /// </summary>
    [JsonInclude]
    internal string Signature { get; private set; }

    /// <summary>
    ///     The gas required to execute the transaction.
    ///     Automatically recalculated when relevant properties are updated.
    /// </summary>
    [JsonInclude]
    internal int Gas { get; private set; }

    /// <summary>
    ///     The name of the blockchain system associated with the transaction.
    ///     Default value is "SmartXchain".
    /// </summary>
    [JsonInclude]
    internal string Name { get; private set; }

    /// <summary>
    ///     The symbol of the blockchain's currency.
    ///     Default value is "SXC".
    /// </summary>
    [JsonInclude]
    internal string Symbol { get; private set; }

    /// <summary>
    ///     The number of decimals supported by the blockchain's currency.
    ///     Default value is 18.
    /// </summary>
    [JsonInclude]
    internal uint Decimals { get; private set; }

    /// <summary>
    ///     The total supply of the blockchain's currency.
    ///     Initially set to 1,000,000,000.
    /// </summary>
    [JsonInclude]
    internal static decimal TotalSupply { get; private set; }

    /// <summary>
    ///     The version of the transaction system.
    ///     Default value is "1.0.0".
    /// </summary>
    [JsonInclude]
    internal string Version { get; private set; }

    /// <summary>
    ///     Additional data associated with the transaction.
    ///     Recalculates gas whenever the value is updated.
    /// </summary>
    [JsonInclude]
    internal string Data
    {
        get => _data;
        set
        {
            _data = value;
            RecalculateGas();
        }
    }

    /// <summary>
    ///     Additional information associated with the transaction.
    ///     Recalculates gas whenever the value is updated.
    /// </summary>
    [JsonInclude]
    internal string Info
    {
        get => _info;
        set
        {
            _info = value;
            RecalculateGas();
        }
    }

    /// <summary>
    ///     Recalculates the gas required for the transaction based on its properties.
    /// </summary>
    private void RecalculateGas()
    {
        var calculator = new GasAndRewardCalculator
        {
            Data = Data,
            Info = Info,
            Sender = Sender
        };
        calculator.CalculateGas();
        Gas = calculator.Gas;
    }

    /// <summary>
    ///     Signs the transaction using the provided private key.
    /// </summary>
    public void SignTransaction(string privateKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportECPrivateKey(Convert.FromBase64String(privateKey), out _);

        var transactionData = $"{Sender}{Recipient}{Data}{Info}{Timestamp}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(transactionData));
        var signature = ecdsa.SignHash(hash);
        Signature = Convert.ToBase64String(signature) + "|" + Crypt.AssemblyFingerprint;
    }

    /// <summary>
    ///     Verifies the cryptographic signature of the transaction.
    /// </summary>
    public bool VerifySignature(string publicKey)
    {
        if (string.IsNullOrEmpty(Signature))
            throw new InvalidOperationException("Transaction is not signed.");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);

        var transactionData = $"{Sender}{Recipient}{Data}{Info}{Timestamp}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(transactionData));
        var sp = Signature.Split('|');
        var signatureBytes = Convert.FromBase64String(sp[0]);

        return ecdsa.VerifyHash(hash, signatureBytes) && sp[1] == Crypt.AssemblyFingerprint;
    }

    /// <summary>
    ///     Registers a user in the blockchain system using their address and private key.
    /// </summary>
    public bool RegisterUser(string address, string? privateKey)
    {
        try
        {
            if (AuthenticatedUsers.ContainsKey(address)) return false;

            if (VerifyPrivateKey(privateKey, address))
            {
                AuthenticatedUsers[address] = HashKey(privateKey);
                Balances.TryAdd(address, 0);
                Log($"User {address} registered successfully.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR: user registration failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Verifies the private key of a user against the stored address.
    /// </summary>
    private static bool VerifyPrivateKey(string? privateKeyWif, string storedAddress, string prefix = "smartX")
    {
        try
        {
            var privateKey = Key.Parse(privateKeyWif, Network.Main);
            var account = new Account(privateKey.ToHex());
            var generatedAddress = prefix + account.Address.Substring(2);
            return generatedAddress.Equals(storedAddress, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error in VerifyPrivateKey: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Retrieves the balance of a specified account.
    /// </summary>
    public decimal BalanceOf(string account)
    {
        return Balances.TryGetValue(account, out var balance) ? balance : 0;
    }

    /// <summary>
    ///     Transfers tokens from one account to another.
    /// </summary>
    private bool Transfer(string sender, string recipient, decimal amount)
    {
        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{sender}'.");
            return false;
        }

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(recipient)) Balances[recipient] = 0;
        Balances[recipient] += amount;

        Log($"Transfer successful: {amount} tokens from {sender} to {recipient}.");
        return true;
    }

    /// <summary>
    ///     Transfers tokens with blockchain integration and optional metadata.
    /// </summary>
    public async Task<(bool, string)> Transfer(Blockchain? chain, string sender, string recipient, decimal amount,
        string? privateKey,
        string info = "", string data = "")
    {
        var message = "";
        if (!IsAuthenticated(sender, privateKey))
        {
            message = $"Transfer failed: Unauthorized action by '{sender}'."; 
            Log(message);
            return (false, message);
        }

        TransactionType = TransactionTypes.NativeTransfer;
        UpdateBalancesFromChain(chain);

        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            message = $"Transfer failed: Insufficient balance in account '{sender}'."; 
            Log(message);
            return (false,message);
        }

        var transferTransaction = new Transaction
        {
            Sender = recipient,
            Recipient = sender,
            Amount = amount,
            Timestamp = DateTime.UtcNow,
            Info = info,
            Data = data
        };

        if (TransactionType == TransactionTypes.NotDefined)
            TransactionType = TransactionTypes.NativeTransfer;

        if (chain != null)
        {
            var success = await chain.AddTransaction(transferTransaction);
        }

        Balances[sender] -= amount;
        Balances.TryAdd(recipient, 0);
        Balances[recipient] += amount;

        message = $"Transfer successful: {amount} tokens from {sender} to {recipient}.";
        Log(message);
        return (true, message);
    }

    /// <summary>
    ///     Updates account balances from the blockchain's transaction history.
    /// </summary>
    internal static void UpdateBalancesFromChain(Blockchain? chain)
    {
        lock (Balances)
        {
            Balances.Clear();
            Balances[Blockchain.SystemAddress] = TotalSupply;

            if (chain != null && chain.Chain != null)
                foreach (var block in chain.Chain)
                foreach (var transaction in block.Transactions)
                {
                    if (string.IsNullOrEmpty(transaction.Sender) || string.IsNullOrEmpty(transaction.Recipient) ||
                        transaction.Amount <= 0)
                        continue;

                    if (Balances.ContainsKey(transaction.Sender))
                        Balances[transaction.Sender] -= transaction.Amount;
                    else
                        Balances[transaction.Sender] = -transaction.Amount;

                    if (Balances.ContainsKey(transaction.Recipient))
                        Balances[transaction.Recipient] += transaction.Amount;
                    else
                        Balances[transaction.Recipient] = transaction.Amount;
                }

            foreach (var account in Balances.Keys.ToList())
                if (Balances[account] < 0)
                    Balances[account] = 0;
        }

        Logger.Log("Balances updated successfully from the blockchain.");
    }

    /// <summary>
    ///     Checks if a user is authenticated using their private key.
    /// </summary>
    private bool IsAuthenticated(string address, string? privateKey)
    {
        return AuthenticatedUsers.TryGetValue(address, out var storedKey) && storedKey == HashKey(privateKey);
    }

    /// <summary>
    ///     Hashes the provided key using SHA-256.
    /// </summary>
    private string HashKey(string? key)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hashedBytes);
    }

    /// <summary>
    ///     Logs messages related to transactions.
    /// </summary>
    private protected void Log(string message)
    {
        Logger.Log($"[Transaction] {message}");
    }

    /// <summary>
    ///     Computes the hash of the transaction for integrity verification.
    /// </summary>
    public string CalculateHash()
    {
        using var sha256 = SHA256.Create();
        var rawData = $"{Sender}{Recipient}{Data}{Info}{Amount}{Name}{Version}";
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

    /// <summary>
    /// Converts the transaction details into a JSON string format.
    /// Excludes empty properties from the output.
    /// </summary>
    public override string ToString()
    {
        // Create a dictionary to hold the non-empty properties
        var transactionDetails = new Dictionary<string, object>();

        // Helper function to add non-empty properties to the dictionary
        void AddProperty<T>(string key, T value, Func<T, bool> isEmpty)
        {
            if (!isEmpty(value))
            {
                transactionDetails[key] = value;
            }
        }

        // Add non-empty properties
        AddProperty("Name", Name, string.IsNullOrEmpty);
        AddProperty("TransactionType", TransactionType.ToString(), string.IsNullOrEmpty);
        AddProperty("Sender", Sender, string.IsNullOrEmpty);
        AddProperty("Recipient", Recipient, string.IsNullOrEmpty);
        AddProperty("Amount", Amount, v => v == 0);
        AddProperty("Gas", Gas, v => v == 0);
        AddProperty("Data", Data, string.IsNullOrEmpty);
        AddProperty("Info", Info, string.IsNullOrEmpty);
        AddProperty("Timestamp", Timestamp, v => v == default);
        AddProperty("Signature", Signature, string.IsNullOrEmpty);
        AddProperty("Version", Version, string.IsNullOrEmpty);

        // Serialize the dictionary to JSON
        return JsonSerializer.Serialize(transactionDetails, new JsonSerializerOptions
        {
            WriteIndented = true // Pretty print the JSON
        });
    }


}