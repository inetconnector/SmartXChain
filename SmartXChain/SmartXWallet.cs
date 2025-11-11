using NBitcoin;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3.Accounts;
using SmartXChain.Utils;
using KeyPath = NBitcoin.KeyPath;

namespace SmartXChain;

/// <summary>
///     Represents the SmartXWallet, which provides functionality for generating, managing,
///     and securely storing cryptocurrency wallets compatible with SmartXChain.
/// </summary>
public class SmartXWallet
{
    /// <summary>
    ///     Stores the list of generated wallet addresses.
    /// </summary>
    public static List<string> WalletAddresses { get; set; } = new();

    /// <summary>
    ///     Generates a new SmartXChain wallet with a 12-word mnemonic phrase and Ethereum-compatible addresses.
    /// </summary>
    /// <returns>
    ///     A tuple containing the list of wallet addresses, the private key, and the mnemonic phrase.
    /// </returns>
    public static string GenerateWallet()
    {
        Key privateKey = null;
        Mnemonic mnemonic = null;
        var mnemonicSecretId = string.Empty;
        var privateKeySecretId = string.Empty;

        try
        {
            const string smartX = "smartX";

            // 1. Generate a 12-word Mnemonic phrase (BIP-39)
            mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            Logger.LogLine("Mnemonic phrase generated");
            Logger.Log("Mnemonic stored securely in the hardware vault. Record it from the vault before continuing.");

            // 2. Derive the seed from the Mnemonic (BIP-39)
            var seed = mnemonic.DeriveSeed();
            var seedSecretId = SecureVault.StoreSecret(BuildVaultKey("wallet_seed"), seed.ToHex());
            Logger.Log($"Seed stored in secure vault reference '{seedSecretId}'.");

            // 3. Create the master key from the seed
            var masterKey = ExtKey.CreateFromSeed(seed);

            // 4. Derive the Ethereum account path (BIP-44)
            var ethKey = masterKey.Derive(new KeyPath("m/44'/60'/0'/0/0"));
            privateKey = ethKey.PrivateKey;
            privateKeySecretId = SecureVault.StoreSecret(BuildVaultKey("wallet_private_key"),
                privateKey.ToString(Network.Main));
            mnemonicSecretId = SecureVault.StoreSecret(BuildVaultKey("wallet_mnemonic"), mnemonic.ToString());
            Logger.Log("Wallet keys stored in secure vault. No secrets were written to plaintext files.");

            // 5. Convert the private key to an Ethereum-compatible address
            var account = new Account(privateKey.ToHex());
            Logger.LogLine("Your SmartXChain Address");
            Logger.Log(smartX + account.Address.Substring(2));
            WalletAddresses.Add(smartX + account.Address.Substring(2));

            // 6. Generate additional addresses in the same wallet
            Logger.LogLine("Additional Addresses in Wallet");
            for (var i = 1; i <= 9; i++)
            {
                var derivedKey = masterKey.Derive(new KeyPath($"m/44'/60'/0'/0/{i}"));
                var derivedAccount = new Account(derivedKey.PrivateKey.ToHex());
                var additionalAddress = smartX + derivedAccount.Address.Substring(2);
                WalletAddresses.Add(additionalAddress);
                Logger.Log($"Address {i}: {additionalAddress}");
            }

            Logger.Log();
        }
        catch (Exception ex)
        {
            Logger.Log($"An error occurred: {ex.Message}");
        }

        // Save all generated wallet addresses to a file
        SaveToFile("walletadresses.txt", string.Join(Environment.NewLine, WalletAddresses));

        // Update miner configuration with the generated wallet 
        Config.Default.SetProperty(Config.ConfigKey.MinerAddress, WalletAddresses[0]);
        if (!string.IsNullOrEmpty(mnemonicSecretId))
            Config.Default.SetProperty(Config.ConfigKey.Mnemonic, $"vault:{mnemonicSecretId}");
        if (!string.IsNullOrEmpty(privateKeySecretId))
            Config.Default.SetProperty(Config.ConfigKey.WalletPrivateKey, $"vault:{privateKeySecretId}");

        Logger.Log("New miner address generated and saved.");
        return WalletAddresses[0];
    }

    public static bool DeleteWallet()
    {
        try
        {
            Logger.Log("Are you sure you want to delete the wallet? This action cannot be undone. (yes/no):");
            var confirmation = Console.ReadLine();

            if (confirmation == null || !confirmation.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log("Wallet deletion canceled.");
                return false;
            }

            var appDir = FileSystem.AppDirectory;
            var file = Path.Combine(appDir, "mnemonic.txt");
            if (File.Exists(file))
                File.Delete(file);

            file = Path.Combine(appDir, "seed.txt");
            if (File.Exists(file))
                File.Delete(file);

            file = Path.Combine(appDir, "privatekey.txt");
            if (File.Exists(file))
                File.Delete(file);

            file = Path.Combine(appDir, "walletadresses.txt");
            if (File.Exists(file))
                File.Delete(file);

            SecureVault.DeleteSecret(BuildVaultKey("wallet_seed"));
            SecureVault.DeleteSecret(BuildVaultKey("wallet_private_key"));
            SecureVault.DeleteSecret(BuildVaultKey("wallet_mnemonic"));

            Config.Default.Delete();

            Logger.Log("Wallet successfully deleted.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Deleting wallet failed");
        }

        return false;
    }


    /// <summary>
    ///     Saves data to a specified file.
    /// </summary>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="content">The content to save in the file.</param>
    private static void SaveToFile(string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(FileSystem.AppDirectory);
            var path = Path.Combine(FileSystem.AppDirectory, fileName);

            File.WriteAllText(path, content);

            Logger.Log($"[INFO] Saved: {path}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[ERROR] Failed to save {fileName}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Loads wallet addresses from a specified file.
    /// </summary>
    /// <param name="fileName">The name of the file containing wallet addresses. Defaults to "walletadresses.txt".</param>
    /// <returns>A list of wallet addresses loaded from the file.</returns>
    public static List<string> LoadWalletAdresses(string fileName = "walletadresses.txt")
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDirectory, fileName);

            if (!File.Exists(path))
            {
                Logger.Log($"File {fileName} not found at {path}");
            }
            else
            {
                var content = File.ReadAllText(path);

                Logger.Log($"[INFO] Loaded: {fileName}");
                var walletAddresses = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
                WalletAddresses = walletAddresses;
                return walletAddresses;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ERROR] Failed to load {fileName}: {ex.Message}");
        }

        return new List<string>();
    }

    private static string BuildVaultKey(string suffix)
    {
        return $"{Config.ChainName.ToString().ToLowerInvariant()}_{suffix}";
    }
}