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

        try
        {
            const string smartX = "smartX";

            // 1. Generate a 12-word Mnemonic phrase (BIP-39)
            mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            Logger.LogLine("Your Mnemonic Phrase (Keep it secure!)");
            Logger.Log(mnemonic.ToString(), false);

            // 2. Derive the seed from the Mnemonic (BIP-39)
            var seed = mnemonic.DeriveSeed();

            // Securely save the mnemonic and seed
            var secretWords = mnemonic.ToString();
            SaveToFile("mnemonic.txt", secretWords);
            SaveToFile("seed.txt", seed.ToHex());

            // 3. Create the master key from the seed
            var masterKey = ExtKey.CreateFromSeed(seed);

            // 4. Derive the Ethereum account path (BIP-44)
            var ethKey = masterKey.Derive(new KeyPath("m/44'/60'/0'/0/0"));
            privateKey = ethKey.PrivateKey;
            SaveToFile("privatekey.txt", privateKey.ToString(Network.Main));

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
        Config.Default.SetProperty(Config.ConfigKey.Mnemonic, mnemonic.ToString());
        Config.Default.SetProperty(Config.ConfigKey.WalletPrivateKey, privateKey.ToString(Network.Main));

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
    ///     Saves sensitive data to a specified file securely.
    /// </summary>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="content">The content to save in the file.</param>
    private static void SaveToFile(string fileName, string content)
    {
        try
        {
            Directory.CreateDirectory(FileSystem.AppDirectory);
            var path = Path.Combine(FileSystem.AppDirectory, fileName);

            // Write the content to the file securely
            File.WriteAllText(path, content);

            Logger.Log($"[INFO] Securely saved: {path}");
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

                Logger.Log($"[INFO] Securely loaded: {fileName}");
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
}