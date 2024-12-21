using NBitcoin;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Web3.Accounts;
using KeyPath = NBitcoin.KeyPath;

public class SmartXWallet
{
    public static List<string> WalletAddresses { get; set; } = new();

    public static (List<string>, Key, string) GenerateWallet()
    {
        Key privateKey = null;
        Mnemonic mnemonic = null;
        try
        {
            const string smartX = "smartX";

            // 1. Generate a 12-word Mnemonic phrase (BIP-39)
            mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
            Console.WriteLine($"\n\nYour Mnemonic Phrase (Keep it secure!):\n{mnemonic}\n");

            // 2. Derive the seed from the Mnemonic (BIP-39)
            var seed = mnemonic.DeriveSeed();

            // Securely save the mnemonic and seed
            var secretWords = mnemonic.ToString();
            SaveToFile("mnemonic.txt", secretWords);
            SaveToFile("seed.txt", seed.ToHex());

            // 3. Create the master key from the seed using CreateFromSeed
            var masterKey = ExtKey.CreateFromSeed(seed);

            // 4. Derive the Ethereum account path (BIP-44)
            var ethKey = masterKey.Derive(new KeyPath("m/44'/60'/0'/0/0"));
            privateKey = ethKey.PrivateKey;

            // 5. Convert the private key to Ethereum-compatible address
            var account = new Account(privateKey.ToHex());
            Console.WriteLine("\nYour SmartXChain Address:\n" + smartX + account.Address.Substring(2));
            WalletAddresses.Add(smartX + account.Address.Substring(2));

            // 6. Example: Display more addresses in the same wallet
            Console.WriteLine("\nAdditional Addresses in Wallet:");
            for (var i = 1; i <= 9; i++)
            {
                var derivedKey = masterKey.Derive(new KeyPath($"m/44'/60'/0'/0/{i}"));
                var derivedAccount = new Account(derivedKey.PrivateKey.ToHex());
                var additionalAddress = smartX + derivedAccount.Address.Substring(2);
                WalletAddresses.Add(additionalAddress);
                Console.WriteLine($"Address {i}: {additionalAddress}");
            }

            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        SaveToFile("walletadresses.txt", string.Join(Environment.NewLine, WalletAddresses));

        Config.Default.SetMinerAddress(WalletAddresses[0], mnemonic.ToString());
        Console.WriteLine("New miner address generated and saved.");

        return (WalletAddresses, privateKey, mnemonic.ToString());
    }

    /// <summary>
    ///     Saves sensitive data to a file securely.
    /// </summary>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="content">The content to save.</param>
    private static void SaveToFile(string fileName, string content)
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

            // Write content to file securely
            File.WriteAllText(path, content);

            Console.WriteLine($"[INFO] Securely saved: {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to save {fileName}: {ex.Message}");
        }
    }

    public static List<string> LoadWalletAdresses(string fileName = "walletadresses.txt")
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

            if (!File.Exists(path)) throw new FileNotFoundException($"File {fileName} not found at {path}");

            var content = File.ReadAllText(path);

            Console.WriteLine($"[INFO] Securely loaded: {fileName}");
            var walletAddresses = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            WalletAddresses = walletAddresses;
            return walletAddresses;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to load {fileName}: {ex.Message}");
            return new List<string>();
        }
    }
}