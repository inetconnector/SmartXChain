using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SmartXChain.BlockchainCore;

namespace SmartXChain.Utils;

public class Crypt
{
    public static readonly Crypt Default = new();

    private static readonly Lazy<string> _assemblyFingerprint = new(GenerateAssemblyFingerprint);

    public Crypt()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PrivateKey = Convert.ToBase64String(ecdsa.ExportECPrivateKey());
        PublicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public string PrivateKey { get; }
    public string PublicKey { get; }
    public static string AssemblyFingerprint => _assemblyFingerprint.Value;

    public static string GenerateBinaryFingerprint(string dllPath)
    {
        if (!File.Exists(dllPath)) throw new FileNotFoundException("DLL file not found.", dllPath);

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(dllPath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    ///     Generates an HMAC signature for a message using a secret key.
    /// </summary>
    /// <param name="message">The message to sign.</param>
    /// <param name="secret">The secret key to use for signing.</param>
    /// <returns>The generated signature as a Base64 string.</returns>
    public static string GenerateHMACSignature(string message, string secret)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
        {
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            return Convert.ToBase64String(hash);
        }
    }

    private static string GenerateAssemblyFingerprint()
    {
        var assembly = Assembly.GetAssembly(typeof(Blockchain));
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();
        assembly.GetManifestResourceStream(assembly.ManifestModule.Name)?.CopyTo(stream);
        stream.Position = 0;
        var hash = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }
}