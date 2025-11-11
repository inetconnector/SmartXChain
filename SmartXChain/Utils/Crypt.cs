using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SmartXChain.BlockchainCore;

namespace SmartXChain.Utils;

/// <summary>
///     Provides cryptographic helper utilities for key generation, hashing, and assembly fingerprinting.
/// </summary>
public class Crypt
{
    public static readonly Crypt Default = new();

    private static readonly Lazy<string> _assemblyFingerprint = new(GenerateAssemblyFingerprint);

    private static readonly ConcurrentDictionary<string, string> _dllFingerprints = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="Crypt" /> class and generates an ECDSA key pair.
    /// </summary>
    public Crypt()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PrivateKey = Convert.ToBase64String(ecdsa.ExportECPrivateKey());
        PublicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    /// <summary>
    ///     Gets the base64-encoded private key generated for the current instance.
    /// </summary>
    public string PrivateKey { get; }

    /// <summary>
    ///     Gets the base64-encoded public key corresponding to <see cref="PrivateKey" />.
    /// </summary>
    public string PublicKey { get; }

    /// <summary>
    ///     Gets a cached fingerprint representing the assembly that contains the <see cref="Blockchain" /> type.
    /// </summary>
    public static string AssemblyFingerprint => _assemblyFingerprint.Value;

    /// <summary>
    ///     Generates a hash for a DLL file and stores the result in a local cache for reuse.
    /// </summary>
    /// <param name="dllPath">The absolute path of the DLL file to fingerprint.</param>
    /// <returns>A base64-encoded SHA-256 hash of the DLL contents.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the specified DLL cannot be found.</exception>
    public static string GenerateFileFingerprint(string dllPath)
    {
        if (_dllFingerprints.TryGetValue(dllPath, out var binaryFingerprint))
            return binaryFingerprint;

        if (!File.Exists(dllPath)) throw new FileNotFoundException("DLL file not found.", dllPath);

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(dllPath);
        var hash = sha256.ComputeHash(stream);
        var fingerprint = Convert.ToBase64String(hash);
        _dllFingerprints.TryAdd(dllPath, fingerprint);

        return fingerprint;
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

    /// <summary>
    ///     Generates a unique fingerprint for the assembly containing the Blockchain type.
    ///     The fingerprint is calculated as a SHA256 hash of the assembly's manifest resource stream.
    /// </summary>
    /// <returns>A base64-encoded string representing the assembly's fingerprint.</returns>
    public static string GenerateAssemblyFingerprint()
    {
        var assembly = Assembly.GetAssembly(typeof(Blockchain));
        using var sha256 = SHA256.Create();
        using var stream = new MemoryStream();
        if (assembly != null) assembly.GetManifestResourceStream(assembly.ManifestModule.Name)?.CopyTo(stream);
        stream.Position = 0;
        var hash = sha256.ComputeHash(stream);
        return Convert.ToBase64String(hash);
    }
}