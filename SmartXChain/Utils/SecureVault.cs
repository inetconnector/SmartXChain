using System.Security.Cryptography;
using System.Text;

namespace SmartXChain.Utils;

/// <summary>
///     Provides functionality for securely persisting and retrieving secrets in an encrypted vault.
///     The vault is typically located on an external USB storage device for portability and isolation.
/// </summary>
public static class SecureVault
{
    private const string VaultEnvironmentVariable = "SMARTX_USB_VAULT_PATH";
    private static readonly object SyncRoot = new();

    /// <summary>
    ///     Stores a secret value in the encrypted vault.
    ///     The secret is encrypted using the current user's data protection API before being persisted.
    /// </summary>
    /// <param name="name">Logical name of the secret to store.</param>
    /// <param name="value">The plaintext secret value.</param>
    /// <returns>The normalized identifier (file name) of the stored secret.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name" /> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="value" /> is null.</exception>
    public static string StoreSecret(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Secret name cannot be empty", nameof(name));
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        var identifier = NormalizeName(name);
        var secretPath = GetSecretPath(identifier);
        var protectedBytes = Protect(value);

        lock (SyncRoot)
        {
            File.WriteAllBytes(secretPath, protectedBytes);
            HardenFilePermissions(secretPath);
        }

        return identifier;
    }

    /// <summary>
    ///     Retrieves a secret from the encrypted vault.
    /// </summary>
    /// <param name="name">Logical name of the secret.</param>
    /// <returns>The decrypted secret string, or <c>null</c> if not found.</returns>
    public static string? RetrieveSecret(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var identifier = NormalizeName(name);
        var secretPath = GetSecretPath(identifier);

        if (!File.Exists(secretPath))
            return null;

        var protectedBytes = File.ReadAllBytes(secretPath);
        return Unprotect(protectedBytes);
    }

    /// <summary>
    ///     Deletes a stored secret from the encrypted vault.
    /// </summary>
    /// <param name="name">Logical name of the secret to remove.</param>
    /// <returns><c>true</c> if the secret existed and was deleted; otherwise <c>false</c>.</returns>
    public static bool DeleteSecret(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var identifier = NormalizeName(name);
        var secretPath = GetSecretPath(identifier);

        if (!File.Exists(secretPath))
            return false;

        File.Delete(secretPath);
        return true;
    }

    /// <summary>
    ///     Normalizes a logical secret name into a file-safe identifier.
    /// </summary>
    /// <param name="name">The original logical name.</param>
    /// <returns>A normalized identifier for use in the vault file system.</returns>
    private static string NormalizeName(string name)
    {
        return name.Trim().Replace(" ", "_").ToLowerInvariant();
    }

    /// <summary>
    ///     Builds the full file path for a given secret identifier within the vault directory.
    /// </summary>
    /// <param name="identifier">The normalized secret name.</param>
    /// <returns>Full path to the secret file.</returns>
    private static string GetSecretPath(string identifier)
    {
        var vaultDirectory = GetVaultDirectory();
        return Path.Combine(vaultDirectory, $"{identifier}.bin");
    }

    /// <summary>
    ///     Resolves the vault directory path from environment or default location and ensures it exists.
    /// </summary>
    /// <returns>The absolute path to the vault directory.</returns>
    private static string GetVaultDirectory()
    {
        var envPath = Environment.GetEnvironmentVariable(VaultEnvironmentVariable);
        string vaultPath;

        if (!string.IsNullOrWhiteSpace(envPath))
        {
            vaultPath = envPath;
        }
        else
        {
            vaultPath = Path.Combine(FileSystem.AppDirectory, "Vault");
            Logger.LogWarning(
                $"Environment variable {VaultEnvironmentVariable} not set. Falling back to application vault at {vaultPath}.");
        }

        Directory.CreateDirectory(vaultPath);
        HardenDirectoryPermissions(vaultPath);
        return vaultPath;
    }

    /// <summary>
    ///     Encrypts the specified plaintext string using the current user's data protection scope.
    /// </summary>
    /// <param name="value">The plaintext value to protect.</param>
    /// <returns>Encrypted byte array representing the protected data.</returns>
    private static byte[] Protect(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    /// <summary>
    ///     Decrypts a previously protected byte array back into a plaintext string.
    /// </summary>
    /// <param name="protectedBytes">The encrypted byte array.</param>
    /// <returns>The decrypted plaintext string.</returns>
    private static string Unprotect(byte[] protectedBytes)
    {
        var data = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

    /// <summary>
    ///     Hardens directory permissions to reduce visibility and limit access to the vault directory.
    /// </summary>
    /// <param name="directoryPath">Path to the vault directory.</param>
    private static void HardenDirectoryPermissions(string directoryPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var attributes = File.GetAttributes(directoryPath);
                if (!attributes.HasFlag(FileAttributes.Hidden))
                    File.SetAttributes(directoryPath, attributes | FileAttributes.Hidden);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
#if NET7_0_OR_GREATER
                File.SetUnixFileMode(directoryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
#endif
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Unable to harden vault directory permissions: {ex.Message}");
        }
    }

    /// <summary>
    ///     Hardens file permissions for a secret file, restricting access to the current user.
    /// </summary>
    /// <param name="filePath">Path to the secret file.</param>
    private static void HardenFilePermissions(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var attributes = File.GetAttributes(filePath);
                File.SetAttributes(filePath, attributes | FileAttributes.Hidden);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
#if NET7_0_OR_GREATER
                File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"Unable to harden vault file permissions: {ex.Message}");
        }
    }
}