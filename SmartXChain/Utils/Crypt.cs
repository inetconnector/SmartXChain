using System.Reflection;
using System.Security.Cryptography;

namespace SmartXChain.Utils;

public class Crypt
{
    public static readonly Crypt Default = new();

    private static readonly Lazy<string> _executingAssemblyFingerprint = new(GenerateExecutingAssemblyFingerprint);

    public Crypt()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PrivateKey = Convert.ToBase64String(ecdsa.ExportECPrivateKey());
        PublicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public string PrivateKey { get; }
    public string PublicKey { get; }

    public static string GenerateBinaryFingerprint(string dllPath)
    {
        if (!File.Exists(dllPath)) throw new FileNotFoundException("DLL file not found.", dllPath);

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(dllPath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }

    public static string GetExecutingAssemblyFingerprint()
    {
        return _executingAssemblyFingerprint.Value;
    }

    private static string GenerateExecutingAssemblyFingerprint()
    {
        var executingAssembly = Assembly.GetExecutingAssembly();
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();
        executingAssembly.GetManifestResourceStream(executingAssembly.ManifestModule.Name)?.CopyTo(stream);
        stream.Position = 0;
        var hash = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }
}