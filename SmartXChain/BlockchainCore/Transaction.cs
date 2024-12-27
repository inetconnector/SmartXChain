using System.Security.Cryptography;
using System.Text;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class Transaction
{
    private string _data;
    private string _info;
    private static Dictionary<string, Dictionary<string, double>> Allowances { get; set; } = new Dictionary<string, Dictionary<string, double>>();
    private static Dictionary<string, string> AuthenticatedUsers { get; set; }= new Dictionary<string, string>();
    public static Dictionary<string, double> Balances 
    { 
        get; 
        private set;
    } = new Dictionary<string, double>();

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
            Balances.Add(owner,initialSupply);
        }
     
        Version = "1.0.0";
        TransactionDate = DateTime.UtcNow; 
    }
     
    public string Sender { get; set; } // The address of the sender
    public string Recipient { get; set; } // The address of the recipient

    public string Data
    {
        get => _data;
        set
        {
            _data = value;
            CalculateGas();
        }
    } // Arbitrary data, such as contract state

    public string Info
    {
        get => _info;
        set
        {
            _info = value;
            CalculateGas();
        }
    } // Info field

    public DateTime Timestamp { get; set; } = DateTime.UtcNow; // The time of the transaction
    public string Signature { get; private set; } // Digital signature of the transaction
    public int Gas { get; private set; }

    public string Name { get; private set; }
    public string Symbol { get; private set; }
    public uint Decimals { get; private set; }
    public ulong TotalSupply { get; private set; }
    public string Version { get; private set; }
    public DateTime TransactionDate { get; private set; }

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

    private void CalculateGas()
    {
        var dataLength = string.IsNullOrEmpty(Data) ? 0 : Data.Length;
        var infoLength = string.IsNullOrEmpty(Info) ? 0 : Info.Length;

        // Example formula for gas calculation: baseGas + (dataLength + infoLength) * factor
        const int baseGas = 10; // Base gas cost for any transaction
        const int gasPerCharacter = 2; // Gas cost per character in Data and Info

        Gas = baseGas + (dataLength + infoLength) * gasPerCharacter / Blockchain.GasFactor;
    }

    public bool RegisterUser(string address, string privateKey)
    {
        if (AuthenticatedUsers.ContainsKey(address)) return false;

        AuthenticatedUsers[address] = HashKey(privateKey);
        Balances.TryAdd(address, 0);
        Log($"User {address} registered successfully.");
        return true;
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
    public bool Transfer(string sender, string recipient, ulong amount, string privateKey)
    {
        if (!IsAuthenticated(sender, privateKey))
        {
            Log($"Transfer failed: Unauthorized action by '{sender}'.");
            return false;
        }

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

    public bool Approve(string owner, string spender, double amount, string privateKey)
    {
        if (!IsAuthenticated(owner, privateKey))
        {
            Log($"Approval failed: Unauthorized action by '{owner}'.");
            return false;
        }

        if (!Allowances.ContainsKey(owner)) Allowances[owner] = new Dictionary<string, double>();
        Allowances[owner][spender] = amount;

        Log($"Approval successful: {spender} can spend {amount} tokens from {owner}.");
        return true;
    }

    public bool TransferFrom(string spender, string sender, string recipient, double amount, string spenderKey)
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

        if (!Balances.ContainsKey(sender) || Balances[sender] < amount)
        {
            Log($"TransferFrom failed: Insufficient balance in account '{sender}'.");
            return false;
        }

        Balances[sender] -= amount;
        if (!Balances.ContainsKey(recipient)) Balances[recipient] = 0;
        Balances[recipient] += amount;
        Allowances[sender][spender] -= amount;

        Log($"TransferFrom successful: {spender} transferred {amount} tokens from {sender} to {recipient}.");
        return true;
    }

    private double Allowance(string owner, string spender)
    {
        return Allowances.TryGetValue(owner, out var spenderDict) && spenderDict.TryGetValue(spender, out var allowed)
            ? allowed
            : 0;
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
        Console.WriteLine($"[Transaction] {message}");
    }

    public override string ToString()
    {
        return $"{Sender} -> {Recipient}: {Timestamp}, Gas: {Gas}";
    }
}