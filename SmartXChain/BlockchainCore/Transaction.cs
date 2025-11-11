using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NBitcoin;
using SmartXChain.Utils;
using Account = Nethereum.Web3.Accounts.Account;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     Represents a SmartXChain transaction including metadata, participants, amounts, and helper utilities.
/// </summary>
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
        Data,
        Server,
        GasConfiguration,
        Founder,
        Export,
        Import
    }

    private string _data = string.Empty;
    private string _info = string.Empty;
    private string _sender = string.Empty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Transaction" /> class with default SmartXChain values.
    /// </summary>
    public Transaction()
    {
        const ulong initialSupply = 10000000000;
        var owner = Blockchain.SystemAddress;
        Name = "SmartXchain";
        Symbol = "SXC";
        Decimals = 18;
        ID = Guid.NewGuid();
        TotalSupply = initialSupply;
        TransactionType = TransactionTypes.NotDefined;

        // Assign initial supply to the owner's balance
        if (!Balances.ContainsKey(owner)) Balances.TryAdd(owner, initialSupply);

        Version = "1.0.0";
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Transaction unique ID
    /// </summary>
    [JsonInclude]
    public Guid ID { get; internal set; }

    /// <summary>
    ///     Represents the parent Blocks
    /// </summary>
    [JsonInclude]
    internal string ParentBlock { get; set; } = string.Empty;

    /// <summary>
    ///     Holds the allowances for transactions between accounts.
    /// </summary>
    [JsonInclude]
    internal static Dictionary<string, Dictionary<string, double>> Allowances { get; } = new();

    /// <summary>
    ///     Stores TransactionType.
    /// </summary>
    [JsonInclude]
    internal TransactionTypes TransactionType { get; set; }


    /// <summary>
    ///     Holds the balance of each account.
    /// </summary>
    [JsonInclude]
    internal static ConcurrentDictionary<string, decimal> Balances { get; } = new();

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
            _sender = value ?? string.Empty;
            RecalculateGas();
        }
    }


    /// <summary>
    ///     Represents the recipient's address for the transaction.
    /// </summary>
    [JsonInclude]
    internal string Recipient { get; set; } = string.Empty;

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
    internal string Signature { get; private set; } = string.Empty;

    /// <summary>
    ///     The gas required to execute the transaction.
    ///     Automatically recalculated when relevant properties are updated.
    /// </summary>
    [JsonInclude]
    internal decimal Gas { get; set; }

    /// <summary>
    ///     The name of the blockchain system associated with the transaction.
    ///     Default value is "SmartXchain".
    /// </summary>
    [JsonInclude]
    internal string Name { get; private set; } = string.Empty;

    /// <summary>
    ///     The symbol of the blockchain's currency.
    ///     Default value is "SXC".
    /// </summary>
    [JsonInclude]
    internal string Symbol { get; private set; } = string.Empty;

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
    internal string Version { get; private set; } = string.Empty;

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
            _data = value ?? string.Empty;
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
            _info = value ?? string.Empty;
            RecalculateGas();
        }
    }


    /// <summary>
    ///     Recalculates the gas required for the transaction based on its properties.
    /// </summary>
    private void RecalculateGas()
    {
        if (Sender == Blockchain.SystemAddress)
        {
            Gas = 0;
        }
        else
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
    }

    /// <summary>
    ///     Signs the transaction using the provided private key.
    /// </summary>
    public void SignTransaction(string privateKey)
    {
        try
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ecdsa.ImportECPrivateKey(Convert.FromBase64String(privateKey), out _);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ToString()));
            var signature = ecdsa.SignHash(hash);
            Signature = Convert.ToBase64String(signature) + "|" + Crypt.AssemblyFingerprint;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to import private key.", ex);
        }
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
    ///     Verifies the private key of a user against the stored address.
    /// </summary>
    private static bool VerifyPrivateKey(string? privateKeyWif, string storedAddress, string prefix = "smartX")
    {
        try
        {
            if (string.IsNullOrWhiteSpace(privateKeyWif))
                return false;

            var privateKey = Key.Parse(privateKeyWif, Network.Main);
            var account = new Account(privateKey.ToHex());
            var generatedAddress = prefix + account.Address.Substring(2);
            return generatedAddress.Equals(storedAddress, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "VerifyPrivateKey failed");
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
            Logger.LogError($"Transfer failed: Insufficient balance in account '{sender}'.");
            return false;
        }

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(recipient)) Balances[recipient] = 0;
        Balances[recipient] += amount;

        Logger.Log($"Transfer successful: {amount} tokens from {sender} to {recipient}.");
        return true;
    }

    /// <summary>
    ///     Transfers tokens with blockchain integration and optional metadata.
    /// </summary>
    public static async Task<(bool, string)> Transfer(Blockchain? chain, string sender, string recipient,
        decimal amount,
        string privateKey,
        string info = "", string data = "")
    {
        if (chain != null)
        {
            await chain.SettleFounder(sender);

            var message = "";
            if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
            {
                message = $"Transfer failed: Insufficient balance in account '{sender}'.";
                Logger.LogError(message);
                return (false, message);
            }

            var transferTransaction = new Transaction
            {
                Sender = sender,
                Recipient = recipient,
                Amount = amount,
                Timestamp = DateTime.UtcNow,
                Info = info,
                Data = data,
                TransactionType = TransactionTypes.NativeTransfer
            };

            var success = await chain.AddTransaction(transferTransaction);
            if (success)
            {
                Balances[sender] -= amount;
                Balances.TryAdd(recipient, 0);
                Balances[recipient] += amount;

                message = $"Transfer successful: {amount} tokens from {sender} to {recipient}.";
                Logger.Log(message);
                return (true, message);
            }

            return (false, "Error: AddTransaction failed");
        }

        return (false, "no blockchain");
    }


    /// <summary>
    ///     Performs a transfer of tokens from the sender's account to a file.
    ///     The transfer generates a public/private key pair for the file and records the transaction
    ///     on the blockchain. The transaction is serialized into a file format.
    /// </summary>
    /// <param name="chain">The blockchain instance to which the transaction will be added.</param>
    /// <param name="sender">The address of the sender initiating the transfer.</param>
    /// <param name="amount">The amount of tokens to transfer.</param>
    /// <param name="privateKey">The private key of the sender for authentication.</param>
    /// <returns>
    ///     A tuple containing:
    ///     - success (bool): Indicates whether the transfer was successful.
    ///     - message (string): A message describing the result of the transfer.
    ///     - privateKeyForFile (string): The private key generated for the file.
    ///     - fileContent (string): The serialized content of the transaction.
    /// </returns>
    public static async Task<(bool success, string message, string fileContent)>
        TransferToFile(Blockchain? chain, string sender, decimal amount, string privateKey)
    {
        if (chain != null)
        {
            await chain.SettleFounder(sender);

            var message = "";
            if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
            {
                message = $"Transfer failed: Insufficient balance in account '{sender}'.";
                Logger.LogError(message);
                return (false, message, "");
            }

            // Generate keys
            using var rsa = new RSACryptoServiceProvider(2048);

            var filePrivateKey = Convert.ToBase64String(rsa.ExportRSAPrivateKey());
            var filePublicKey = Convert.ToBase64String(rsa.ExportRSAPublicKey());

            var id = Guid.NewGuid();
            var data = Encoding.ASCII.GetBytes($"{amount}-{filePrivateKey}-{id}");

            byte[] signature;

            // Sign data using the private key
            using (var rsaSigner = new RSACryptoServiceProvider())
            {
                rsaSigner.ImportRSAPrivateKey(Convert.FromBase64String(filePrivateKey), out _);
                signature = rsaSigner.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            var transferTransaction = new Transaction
            {
                Sender = sender,
                Recipient = Blockchain.UnknownAddress,
                Amount = amount,
                Timestamp = DateTime.UtcNow,
                Info = filePublicKey,
                Data = Convert.ToBase64String(signature),
                ID = id,
                TransactionType = TransactionTypes.Export
            };

            var success = await chain.AddTransaction(transferTransaction, true);
            if (success)
            {
                Balances[sender] -= amount;
                Balances.TryAdd(Blockchain.UnknownAddress, 0);
                Balances[Blockchain.UnknownAddress] += amount;

                message = $"Transfer successful: {amount} tokens from {sender} to file.";
                Logger.Log(message);

                var fileData = new FileData
                {
                    PrivateKey = filePrivateKey,
                    Transaction = transferTransaction
                };

                var fileContent = JsonSerializer.Serialize(fileData);
                return (true, message, fileContent);
            }

            return (false, "Error: AddTransaction failed", "");
        }

        return (false, "no blockchain", "");
    }


    /// <summary>
    ///     Imports transaction details from a file and applies the transaction to the blockchain.
    /// </summary>
    /// <param name="chain">The blockchain instance to apply the transaction to.</param>
    /// <param name="fileContent">json transaction</param>
    /// <param name="recipient">The recipient account for the imported transaction.</param>
    /// <returns>A tuple containing success status and a message.</returns>
    public static async Task<(bool success, string message)> ImportFromFileToAccount(Blockchain? chain,
        string fileContent, string recipient)
    {
        if (chain == null) return (false, "No blockchain provided.");

        try
        {
            // Deserialize JSON   
            var fileData = JsonSerializer.Deserialize<FileData>(fileContent);
            var transaction = fileData.Transaction;

            if (transaction == null || transaction.Amount == 0)
                return (false, "Invalid transaction file");

            foreach (var t in chain.GetTransactionsByAddress(Blockchain.UnknownAddress))
                if (transaction.ID.ToString().ToUpper() == t.Info.ToUpper() &&
                    transaction.ID.ToString().ToUpper() == t.Data.ToUpper())
                    return (false, "file already imported to blockchain.");

            foreach (var t in chain.GetTransactionsByAddress(Blockchain.UnknownAddress))
                if (t.ID.Equals(transaction.ID) &&
                    t.Info == transaction.Info &&
                    t.Amount == transaction.Amount && t.TransactionType == TransactionTypes.Export)
                {
                    var data = Encoding.ASCII.GetBytes($"{t.Amount}-{fileData.PrivateKey}-{t.ID}");
                    var signature = Convert.FromBase64String(t.Data);
                    using var rsaVerifier = new RSACryptoServiceProvider();

                    rsaVerifier.ImportRSAPublicKey(Convert.FromBase64String(transaction.Info), out _);
                    if (!rsaVerifier.VerifyData(data, signature, HashAlgorithmName.SHA256,
                            RSASignaturePadding.Pkcs1)) continue;

                    //signature ok, transfer amount to recipient
                    // Create transfer transaction
                    var transferTransaction = new Transaction
                    {
                        Sender = Blockchain.UnknownAddress,
                        Recipient = recipient,
                        Amount = t.Amount,
                        Timestamp = DateTime.UtcNow,
                        Info = t.ID.ToString(),
                        Data = t.ID.ToString(),
                        TransactionType = TransactionTypes.Import
                    };

                    var success = await chain.AddTransaction(transferTransaction, true);
                    if (success)
                    {
                        Logger.Log(
                            $"Import successful: {transaction.Amount} tokens transferred from file to {recipient}.");
                        return (true, "Import successful.");
                    }
                }

            return (false, "Failed to add transaction to blockchain.");
        }
        catch (Exception ex)
        {
            Logger.LogError("Error reading file content: " + ex.Message);
            return (false, "Error processing the file content.");
        }
    }

    /// <summary>
    ///     Updates account balances from the blockchain's transaction history.
    /// </summary>
    internal static void UpdateBalancesFromChain(Blockchain? chain)
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


        Logger.Log("Balances updated successfully from the blockchain.");
    }

    /// <summary>
    ///     Hashes the provided key using SHA-256.
    /// </summary>
    private static string HashKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));

        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hashedBytes);
    }


    /// <summary>
    ///     Computes the hash of the transaction for integrity verification.
    /// </summary>
    public string CalculateHash()
    {
        using var sha256 = SHA256.Create();
        var rawData = $"{ID}{Sender}{Recipient}{Data}{Info}{Amount}{Name}{Version}";
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

    /// <summary>
    ///     Converts the transaction details into a JSON string format.
    ///     Excludes empty properties from the output.
    /// </summary>
    public override string ToString()
    {
        // Create a dictionary to hold the non-empty properties
        var transactionDetails = new Dictionary<string, object>();

        // Helper function to add non-empty properties to the dictionary
        void AddProperty<T>(string key, T value, Func<T, bool> isEmpty)
        {
            if (!isEmpty(value)) transactionDetails[key] = value;
        }

        // Add non-empty properties
        AddProperty("Name", Name, string.IsNullOrEmpty);
        AddProperty("TransactionType", TransactionType.ToString(), string.IsNullOrEmpty);
        AddProperty("Sender", Sender, string.IsNullOrEmpty);
        AddProperty("Recipient", Recipient, string.IsNullOrEmpty);
        AddProperty("Amount", Amount, v => v == 0);
        AddProperty("Gas", Gas, v => v == 0);
        AddProperty("ChainInfo", Data, string.IsNullOrEmpty);
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

    private record FileData
    {
        public Transaction Transaction { get; set; }
        public string PrivateKey { get; set; }
    }
}