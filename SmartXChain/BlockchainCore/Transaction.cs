using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using NBitcoin;
using Nethereum.Web3.Accounts; 
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class Transaction
{
    private string _data;
    private string _info;
    private string _sender;
     
    public Transaction()
    {
        const int initialSupply = 1000000000;
        var owner = Blockchain.SystemAddress;
        Name = "SmartXchain";
        Symbol = "SXC";
        Decimals = 18;
        TotalSupply = initialSupply;

        // Assign initial supply to the owner's balance
        if (!Balances.ContainsKey(owner))
        {
            Balances.Add(owner, initialSupply);
        }

        Version = "1.0.0";
        TransactionDate = DateTime.UtcNow;
    }

    [JsonInclude] internal static Dictionary<string, Dictionary<string, double>> Allowances { get; } = new();
    [JsonInclude] internal static Dictionary<string, string> AuthenticatedUsers { get; } = new();
    [JsonInclude] internal static Dictionary<string, double> Balances { get; } = new();

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

    [JsonInclude] internal string Recipient { get; set; }
    [JsonInclude] internal double Amount { get; set; }
    [JsonInclude] internal DateTime Timestamp { get; set; } = DateTime.UtcNow;
    [JsonInclude] internal string Signature { get; private set; }
    [JsonInclude] internal int Gas { get; private set; }
    [JsonInclude] internal string Name { get; private set; }
    [JsonInclude] internal string Symbol { get; private set; }
    [JsonInclude] internal uint Decimals { get; private set; }
    [JsonInclude] internal static ulong TotalSupply { get; private set; }
    [JsonInclude] internal string Version { get; private set; }
    [JsonInclude] internal DateTime TransactionDate { get; private set; }

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

    private void RecalculateGas()
    {
        var calculator = new GasAndRewardCalculator
        {
            Data = Data,
            Info = Info,
            Sender = Sender,
        };
        calculator.CalculateGas();
        Gas = calculator.Gas;
    }

    public void SignTransaction(string privateKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportECPrivateKey(Convert.FromBase64String(privateKey), out _);

        var transactionData = $"{Sender}{Recipient}{Data}{Info}{Timestamp}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(transactionData));
        var signature = ecdsa.SignHash(hash);
        Signature = Convert.ToBase64String(signature) + "|" + Crypt.AssemblyFingerprint;
    }

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


    public bool RegisterUser(string address, string privateKey)
    {
        try
        {
            // Check if the user is already registered
            if (AuthenticatedUsers.ContainsKey(address))
            {
                return false;
            }

            // Validate the public and private key pair
            if (VerifyPrivateKey(privateKey, address))
            {
                // Add the user to the system
                AuthenticatedUsers[address] = HashKey(privateKey);
                Balances.TryAdd(address, 0);
                Log($"User {address} registered successfully.");
                return true;
            }
        }
        catch (Exception ex)
        {
            Log($"Error registering user: {ex.Message}");
        }
        return false;
    }  
    private static bool VerifyPrivateKey(string privateKeyWif, string storedAddress, string prefix = "smartX")
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
            Logger.LogMessage($"Error in VerifyPrivateKey: {ex.Message}");
            return false;
        }
    }

    public double BalanceOf(string account)
    {
        return Balances.TryGetValue(account, out var balance) ? balance : 0;
    }

    private bool Transfer(string sender, string recipient, double amount)
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

    public bool Transfer(Blockchain chain, string sender, string recipient, double amount, string privateKey, string info="", string data = "")
    {
        if (!IsAuthenticated(sender, privateKey))
        {
            Log($"Transfer failed: Unauthorized action by '{sender}'.");
            return false;
        }

        UpdateBalancesFromChain(chain);

        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"Transfer failed: Insufficient balance in account '{sender}'.");
            return false;
        }
         
        var transferTransaction = new Transaction
        {
            Sender = recipient,
            Recipient = sender,
            Amount = amount,
            Timestamp = DateTime.UtcNow,
            Info = info,
            Data=data
        };

        chain.AddTransaction(transferTransaction);

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(recipient)) Balances[recipient] = 0;
        Balances[recipient] += amount;

        Log($"Transfer successful: {amount} tokens from {sender} to {recipient}.");
        return true;
    }

    internal static void UpdateBalancesFromChain(Blockchain chain)
    { 
        return;
        lock (Transaction.Balances)
        { 
            Transaction.Balances.Clear();
             
            foreach (var block in chain.Chain)
            { 
                foreach (var transaction in block.Transactions)
                {
                    // Skip invalid transactions or system-specific transactions
                    if (string.IsNullOrEmpty(transaction.Sender) || string.IsNullOrEmpty(transaction.Recipient) || transaction.Amount <= 0)
                        continue;

                    // Deduct the amount from the sender's balance
                    if (Transaction.Balances.ContainsKey(transaction.Sender))
                        Transaction.Balances[transaction.Sender] -= transaction.Amount;
                    else
                        Transaction.Balances[transaction.Sender] = -transaction.Amount;

                    // Add the amount to the recipient's balance
                    if (Transaction.Balances.ContainsKey(transaction.Recipient))
                        Transaction.Balances[transaction.Recipient] += transaction.Amount;
                    else
                        Transaction.Balances[transaction.Recipient] = transaction.Amount;
                }
            }

            // Ensure no balance is negative (optional, based on blockchain rules)
            foreach (var account in Transaction.Balances.Keys.ToList())
            {
                if (Transaction.Balances[account] < 0)
                    Transaction.Balances[account] = 0;
            }
        }

        Logger.LogMessage("Balances updated successfully from the blockchain.");
    }


    private bool IsAuthenticated(string address, string privateKey)
    {
        return AuthenticatedUsers.TryGetValue(address, out var storedKey) && storedKey == HashKey(privateKey);
    }

    private string HashKey(string key)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hashedBytes);
    }

    private protected void Log(string message)
    {
        Logger.LogMessage($"[Transaction] {message}");
    }

    public override string ToString()
    {
        return $"{Sender} -> {Recipient}: {Timestamp}, Gas: {Gas}";
    }

    public string CalculateHash()
    {
        using (var sha256 = SHA256.Create())
        {
            var rawData = $"{Sender}{Recipient}{Data}{Info}{Timestamp}";
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
    }
}