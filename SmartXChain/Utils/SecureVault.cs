using System.Security.Cryptography;
using System.Text;

namespace SmartXChain.Utils;

/// <summary>
///     Provides facilities for securely persisting and retrieving secrets in an encrypted vault that
///     is intended to be located on an attached USB storage device.
/// </summary>
public static class SecureVault
{
    private const string VaultEnvironmentVariable = "SMARTX_USB_VAULT_PATH";
    private static readonly object SyncRoot = new();

    /// <summary>
    ///     Stores a secret value in the encrypted vault. The secret is encrypted with the current user's
    ///     data-protection API before being persisted to disk.
    /// </summary>
    /// <param name="name">Logical name of the secret.</param>
    /// <param name="value">Secret value to store.</param>
    /// <returns>The identifier of the stored secret.</returns>
    public static string StoreSecret(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Secret name cannot be empty", nameof(name));
        if (value is null) throw new ArgumentNullException(nameof(value));

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
    /// <returns>The decrypted secret, or null when the secret is not present.</returns>
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
    /// <returns>True when the secret existed and has been deleted; otherwise false.</returns>
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

    private static string NormalizeName(string name)
    {
        return name.Trim().Replace(" ", "_").ToLowerInvariant();
    }

    private static string GetSecretPath(string identifier)
    {
        var vaultDirectory = GetVaultDirectory();
        return Path.Combine(vaultDirectory, $"{identifier}.bin");
    }

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
                $"Environment variable {VaultEnvironmentVariable} not set. Falling back to application data vault at {vaultPath}.");
        }

        Directory.CreateDirectory(vaultPath);
        HardenDirectoryPermissions(vaultPath);
        return vaultPath;
    }

    private static byte[] Protect(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
    }

    private static string Unprotect(byte[] protectedBytes)
    {
        var data = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

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
