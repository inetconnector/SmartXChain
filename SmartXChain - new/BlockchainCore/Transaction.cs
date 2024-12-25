using System.Security.Cryptography;
using System.Text;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class Transaction
{
    private string _data;
    private string _info;
    public string Sender { get; set; } // The address of the sender
    public string Recipient { get; set; } // The address of the recipient
    public decimal Amount { get; set; } // The amount of cryptocurrency being transferred

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

    public DateTime Timestamp { get; set; } // The time of the transaction
    public string Signature { get; private set; } // Digital signature of the transaction
    public int Gas { get; private set; }

    public void SignTransaction(string privateKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportECPrivateKey(Convert.FromBase64String(privateKey), out _);

        var transactionData = $"{Sender}{Recipient}{Amount}{Timestamp}";
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

        var transactionData = $"{Sender}{Recipient}{Amount}{Timestamp}";
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

        Gas = baseGas + (dataLength + infoLength) * gasPerCharacter;
    }

    public override string ToString()
    {
        return $"{Sender} -> {Recipient}: {Amount} Tokens (at {Timestamp}, Gas: {Gas})";
    }
}