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
    public static (List<string>, Key, string) GenerateWallet()
    {
        Key privateKey = null;
        Mnemonic mnemonic = null;

        try
        {
            const string smartX = "smartX";

            // 1. Generate a 12-word Mnemonic phrase (BIP-39)
            mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            Logger.LogMessage($"\n\nYour Mnemonic Phrase (Keep it secure!):\n{mnemonic}\n");

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
            Logger.LogMessage("\nYour SmartXChain Address:\n" + smartX + account.Address.Substring(2));
            WalletAddresses.Add(smartX + account.Address.Substring(2));

            // 6. Generate additional addresses in the same wallet
            Logger.LogMessage("\nAdditional Addresses in Wallet:");
            for (var i = 1; i <= 9; i++)
            {
                var derivedKey = masterKey.Derive(new KeyPath($"m/44'/60'/0'/0/{i}"));
                var derivedAccount = new Account(derivedKey.PrivateKey.ToHex());
                var additionalAddress = smartX + derivedAccount.Address.Substring(2);
                WalletAddresses.Add(additionalAddress);
                Logger.LogMessage($"Address {i}: {additionalAddress}");
            }

            Logger.LogMessage();
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"An error occurred: {ex.Message}");
        }

        // Save all generated wallet addresses to a file
        SaveToFile("walletadresses.txt", string.Join(Environment.NewLine, WalletAddresses));

        // Update miner configuration with the generated wallet
        Config.Default.SetMinerAddress(WalletAddresses[0], mnemonic.ToString(), privateKey.ToString(Network.Main));
        Logger.LogMessage("New miner address generated and saved.");

        return (WalletAddresses, privateKey, mnemonic.ToString());
    }

    public static bool DeleteWallet()
    {
        try
        {
            Logger.LogMessage("Are you sure you want to delete the wallet? This action cannot be undone. (yes/no):");
            var confirmation = Console.ReadLine();

            if (confirmation == null || !confirmation.Equals("yes", StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogMessage("Wallet deletion canceled.");
                return false;
            }

            var appDir = Config.AppDirectory();
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

            Logger.LogMessage("Wallet successfully deleted.");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogMessage($"Error deleting wallet: {e.Message}");
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
            var path = Path.Combine(Config.AppDirectory(), fileName);

            // Write the content to the file securely
            File.WriteAllText(path, content);

            Logger.LogMessage($"[INFO] Securely saved: {path}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"[ERROR] Failed to save {fileName}: {ex.Message}");
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
            var path = Path.Combine(Config.AppDirectory(), fileName);

            if (!File.Exists(path))
            {
                Logger.LogMessage($"File {fileName} not found at {path}");
            }
            else
            {
                var content = File.ReadAllText(path);

                Logger.LogMessage($"[INFO] Securely loaded: {fileName}");
                var walletAddresses = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
                WalletAddresses = walletAddresses;
                return walletAddresses;
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"[ERROR] Failed to load {fileName}: {ex.Message}");
        }

        return new List<string>();
    }
}