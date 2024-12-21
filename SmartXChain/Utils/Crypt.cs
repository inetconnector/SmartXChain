using System.Reflection;
using System.Security.Cryptography;

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