using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SmartXChain;
using SmartXChain.BlockchainCore;
using SmartXChain.ClientServer;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using SmartXChain.Validators;
using static SmartXChain.Utils.Config;

namespace SmartX;

internal class Program
{
    private static string? _privateKey = "";

    private static string PrivateKey
    {
        get
        {
            if (!string.IsNullOrEmpty(_privateKey))
                return _privateKey;

            var privateKeyFile = Path.Combine(FileSystem.AppDirectory, "privatekey.txt");
            if (File.Exists(privateKeyFile)) return File.ReadAllText(privateKeyFile);
            Logger.Log($"PrivateKey not found at: {privateKeyFile}. Enter PrivateKey\n");
            _privateKey = Console.ReadLine();
            return _privateKey + "";
        }
    }

    public static async Task Main(string[] args)
    {
        ChainName = ChainNames.SmartXChain;
        var port = 5556;

        if (args.Contains("/testnet", StringComparer.OrdinalIgnoreCase))
        {
            ChainName = ChainNames.SmartXChain_Testnet;
            Logger.Log($"ChainName has been set to '{ChainName}'.");

            FileSystem.CreateBackup();

            var configFile = FileSystem.ConfigFile;
            var configFileName = ChainName == ChainNames.SmartXChain ? "config.txt" : "config.testnet.txt";
            var configInitialPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);

            if (!File.Exists(configFile) || TestNet)
                File.Copy(configInitialPath, configFile, true);
            Default.ReloadConfig();
        }

        var publicIP = await NetworkUtils.GetPublicIPAsync(debug: true);

        //Default.SetProperty(ConfigKey.NodeAddress, $"http://{publicIP}:{port}");

        // Initialize application and start the blockchain server
        await InitializeApplicationAsync();

        var startup = await BlockchainServer.StartServerAsync();

        await RunConsoleMenuAsync(startup);
    }

    private static Task InitializeApplicationAsync()
    {
        Logger.LogLine("Application start");

        // ensure wallet and keys are set up 
        if (string.IsNullOrEmpty(Default.MinerAddress))
        {
            SmartXWallet.GenerateWallet();
            Default.ReloadConfig();
        }

        //create server keys
        if (string.IsNullOrEmpty(Default.PublicKey))
            Default.GenerateServerKeys();
         
        return Task.CompletedTask;
    }
      
    private static async Task RunConsoleMenuAsync(BlockchainServer.NodeStartupResult? startup)
    {
        // Display and process console menu 

        while (true)
            try
            {
                DisplayMenu();

                var mode = Console.ReadKey().KeyChar;
                Logger.Log("Menu");

                switch (mode)
                {
                    case 'c':
                        Console.Clear();
                        break;
                    case 'n':
                        DisplayNodes(startup);
                        break;
                    case 's':
                        await SendNativeTokens(startup);
                        break; 
                    case 'i':
                        ImportAmountFromFile(startup);
                        break;
                    case '1':
                        await RunCoinClassTesterAsync();
                        break;
                    case '2':
                        await RunSmartContractDemoAsync(startup);
                        break;
                    case '3':
                        DisplayBlockchainState(startup);
                        break;
                    case '4':
                        DisplayWalletBalances(startup);
                        break;
                    case '5':
                        DisplayChainInfo(startup);
                        break;
                    case '6':
                        DisplayContracts(startup);
                        break;
                    case '7':
                        await UploadContract(startup);
                        break;
                    case '8':
                        Logger.Log("9: Toggle Debug mode");
                        Default.SetProperty(ConfigKey.Debug,
                            (!Default.Debug).ToString());
                        break; 
                    case '0':
                        return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
    }

    private static void DisplayMenu()
    {
        // Show available menu options
        Logger.LogLine();
        Logger.Log("Enter mode:");
        Logger.Log("n: Show nodes");
        Logger.Log("s: Send SCX Tokens / Export SCX Tokens to file");
        Logger.Log("i: Import SCX Tokens from file");
        Logger.Log("c: Clear screen"); 
        Logger.Log();
        Logger.Log("1: Coin class tester");
        Logger.Log("2: SmartContract Demo");
        Logger.Log("3: Blockchain state");
        Logger.Log("4: Wallet Balances");
        Logger.Log("5: Chain Info");
        Logger.Log("6: Show Contracts");
        Logger.Log("7: Upload Contract");
        Logger.Log("9: Toggle Debug mode");
        Logger.Log("0: Exit");
        Logger.LogLine();
    }

    private static void ImportAmountFromFile(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.LogLine("i: Import SCX tokens from file");

        Logger.Log("Enter recipient address : ");
        var recipient = Console.ReadLine();

        Logger.Log("Enter fileName to read the SCX tokens from: ");
        var fileName = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(fileName))
        {
            Logger.LogError("Recipient address or file name is invalid.");
            return;
        }

        if (!File.Exists(fileName))
        {
            Logger.LogError("The specified file does not exist.");
            return;
        }

        try
        {
            // Read file content
            var fileContent = File.ReadAllText(fileName);

            // Execute ImportFromFileToAccount
            var blockchain = startup?.Blockchain; // Assuming Blockchain is accessible through startup

            if (blockchain == null)
            {
                Logger.LogError("Blockchain is not initialized.");
                return;
            }

            Task.Run(async () =>
            {
                var (success, message) =
                    await Transaction.ImportFromFileToAccount(startup.Blockchain, fileContent, recipient);

                if (success)
                    Logger.LogLine("Import successful: " + message);
                else
                    Logger.LogError("Import failed: " + message);
            }).Wait();
        }
        catch (Exception ex)
        {
            Logger.LogError("Error during import: " + ex.Message);
        }
    }

    private static async Task SendNativeTokens(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.LogLine("s: Send SCX Tokens");
        var walletAddresses = SmartXWallet.LoadWalletAdresses();

        Logger.Log("Enter recipient address or <press enter> to export SCX tokens to file: ");
        var recipient = Console.ReadLine();

        Logger.Log("Enter SCX amount (i.e. 0.01)");
        var scx = Console.ReadLine();
        if (decimal.TryParse(scx, NumberStyles.Number, CultureInfo.CurrentCulture, out var result))
        {
            var amount = Convert.ToDecimal(scx);

            if (amount > 0)
            {
                if (!string.IsNullOrEmpty(recipient))
                {
                    Logger.Log("Enter data (optional): ");
                    var data = Console.ReadLine() + "";

                    //send to recipient
                    Logger.Log($"Ready to send {amount} to {recipient} ? (y/n)");
                    if (Console.ReadLine() == "y")
                    {
                        var success = startup != null && await NativeSCXTransfer(startup.Blockchain, walletAddresses[0],
                            recipient, amount,
                            data, PrivateKey);
                        Logger.Log("Success: " + success);
                    }
                }
                else
                {
                    Logger.Log("Enter filename to save scx tokens: ");
                    var fileName = Console.ReadLine() + "";

                    // Export the amount to the file
                    var success = await NativeSCXExport(startup.Blockchain, walletAddresses[0], fileName, amount,
                        PrivateKey);

                    Logger.Log("Success: " + success);
                }
            }
        }
    }

    private static void DisplayNodes(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.LogLine("n: Show nodes");
        if (startup != null && startup.Node != null)
        {
            if (Node.CurrentNodes.Count == 0)
                Logger.LogError("no nodes found!");
            else
                foreach (var ip in Node.CurrentNodes)
                    if (ip == startup.Node.NodeAddress)
                        Logger.Log("*" + ip);
                    else
                        Logger.Log(ip);
        }
    }

    private static async Task RunCoinClassTesterAsync()
    {
        Logger.LogLine("1: Coin class tester");

        // Get Wallet
        Logger.LogLine("Get Wallet");
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        var seedFile = Path.Combine(FileSystem.AppDirectory, "seed.txt");
        var seed = File.ReadAllText(seedFile);


        //Get GasContract
        Logger.LogLine("Get GasContract");
        var gas = new GasConfiguration();

        Logger.LogLine("Gas configuration:");
        foreach (var gasInfo in gas.ToString().Split(Environment.NewLine))
            Logger.Log(gasInfo, false);

        Logger.LogLine("Updated Gas configuration:");
        gas.UpdateParameter(GasConfiguration.GasConfigParameter.BaseGasTransaction,
            gas.BaseGasContract * (decimal)1.1);
        foreach (var gasInfo in gas.ToString().Split(Environment.NewLine))
            Logger.Log(gasInfo, false);


        // Test ERC20 coin functionalities
        Logger.LogLine("ERC20 coin functionalities");
        var token = new ERC20Token("SmartXchain", "SXC", 18, 10000000000, walletAddresses[0]);
        token.RegisterUser(walletAddresses[0], seed);
        token.Transfer(walletAddresses[0], walletAddresses[1], 100, seed);
        token.Transfer(walletAddresses[1], walletAddresses[2], 50, seed);
        var serializedData = Serializer.SerializeToBase64(token);
        var deserializedToken = Serializer.DeserializeFromBase64<ERC20Token>(serializedData);
        deserializedToken.Transfer(walletAddresses[2], walletAddresses[3], 25, seed);
        DisplayTokenDetails(deserializedToken);

        //Test Goldcoin coin functionalities 
        Logger.LogLine("Goldcoin coin functionalities");
        var goldCoin = new GoldCoin("GoldCoin", "GLD", 18, 10000000000, walletAddresses[0]);
        goldCoin.RegisterUser(walletAddresses[0], seed);
        goldCoin.Transfer(walletAddresses[0], walletAddresses[1], 100, seed);
        await goldCoin.FetchAndUpdateGoldPriceAsync(seed);
        serializedData = Serializer.SerializeToBase64(goldCoin);
        deserializedToken = Serializer.DeserializeFromBase64<GoldCoin>(serializedData);
        DisplayTokenDetails(deserializedToken);
    }

    private static void DisplayTokenDetails(IERC20Token token)
    {
        // Display token details, balances, and allowances
        Logger.Log("Deserialized Token:");
        Logger.Log($"Name: {token.Name}");
        Logger.Log($"Symbol: {token.Symbol}");
        Logger.Log($"Decimals: {token.Decimals}");
        Logger.Log($"Total Supply: {token.TotalSupply}");

        Logger.Log("Balances:");
        foreach (var balance in token.GetBalances)
            Logger.Log($"{balance.Key}: {balance.Value}");

        Logger.Log("Allowances:");
        foreach (var allowance in token.GetAllowances)
        {
            Logger.Log($"{allowance.Key}:");
            foreach (var spender in allowance.Value)
                Logger.Log($"  {spender.Key}: {spender.Value}");
        }
    }

    private static async Task RunSmartContractDemoAsync(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("2: SmartContract Demo");

        // Get Wallet Addresses
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        if (walletAddresses.Count == 0)
        {
            Logger.LogError("SmartXWallet addresses are empty. SmartContractDemo cancelled.");
            return;
        }

        // Ensure PrivateKey and node are not null
        if (PrivateKey != null && node != null)
        {
            var sendAmount = (decimal)1000.0;
            var sender = walletAddresses[0];
            var recipient = walletAddresses[1];

            if (node.Blockchain != null)
            {
                ////send 1000 scx transaction
                //await NativeSCXTransfer(
                //    node.Blockchain,
                //    sender,
                //    recipient,
                //    (decimal)1,
                //    "49.83278, 9.88167",
                //    PrivateKey);

                //await node.Blockchain.MinePendingTransactions(walletAddresses[0]);


                const int threadCount = 1;
                const int transactionsPerThread = 1; // Anzahl der Transaktionen pro Thread
                var tasks = new List<Task>();

                for (var i = 0; i < threadCount; i++)
                    tasks.Add(Task.Run(async () =>
                    {
                        for (var j = 0; j < transactionsPerThread; j++)
                        {
                            //send 1000 scx transaction
                            await NativeSCXTransfer(
                                node.Blockchain,
                                sender,
                                recipient,
                                1,
                                j + "Thread",
                                PrivateKey);

                            await node.Blockchain.MinePendingTransactions(walletAddresses[0]);
                        }
                    }));

                await Task.WhenAll(tasks);

                // Add demonstration tasks for ERC20, ERC20Extendedand and GoldCoin smart contracts
                ERC20Example(sender, walletAddresses, node.Blockchain);
                ERC20ExtendedExample(sender, walletAddresses, node.Blockchain);
                GoldCoinExample(sender, walletAddresses, SmartXWallet.LoadWalletAdresses(), node.Blockchain);


                Logger.LogLine("All operations completed.");

                // Display Wallet Balances
                DisplayWalletBalances(node);
            }
            else
            {
                Logger.LogError("Blockchain is null. SmartContractDemo cancelled.");
            }
        }
        else
        {
            Logger.LogError("PrivateKey or node is null. SmartContractDemo cancelled.");
        }
    }

    private static async Task<bool> NativeSCXTransfer(Blockchain? chain,
        string sender,
        string recipient,
        decimal amount,
        string data,
        string privateKey)
    {
        try
        {
            // Perform native token transfer
            if (!string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(privateKey))
            {
                var (transferred, message) = await Transaction.Transfer(
                    chain,
                    sender,
                    recipient,
                    amount,
                    privateKey,
                    "native transfer",
                    data);


                if (transferred)
                {
                    Logger.Log($"Transferred SCX from {sender} to {recipient}", false);
                }
                else
                {
                    Logger.Log($"SCX could not be transferred from {sender} to {recipient}", false);
                    Logger.Log($"{message}", false);
                }

                return transferred;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Transferring SCX from {sender} to {recipient} failed");
        }

        return false;
    }

    private static async Task<bool> NativeSCXExport(Blockchain? chain,
        string sender,
        string fileName,
        decimal amount,
        string privateKey)
    {
        try
        {
            // Perform native token transfer
            if (!string.IsNullOrEmpty(sender) && !string.IsNullOrEmpty(privateKey))
            {
                var (transferred, message, fileContent) = await Transaction.TransferToFile(
                    chain,
                    sender,
                    amount, PrivateKey);


                if (transferred)
                {
                    File.WriteAllText(fileName, fileContent);
                    Logger.Log($"Transferred SCX from {sender} to {fileName}", false);
                }
                else
                {
                    Logger.Log($"SCX could not be transferred from {sender} to {fileName}", false);
                    Logger.Log($"{message}", false);
                }

                return transferred;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Transferring SCX from {sender} to {fileName} failed");
        }

        return false;
    }

    private static void DisplayBlockchainState(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("3: Blockchain state");

        // Show current state of the blockchain
        if (node != null && node.Blockchain != null && node.Blockchain.Chain != null)
            lock (node.Blockchain.Chain)
            {
                foreach (var block in node.Blockchain.Chain)
                    Logger.Log($"Block {node.Blockchain.Chain.IndexOf(block)}: {block.Hash}");
            }
    }

    private static void DisplayWalletBalances(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("4: Wallet Balances");

        var walletAddresses = SmartXWallet.LoadWalletAdresses();

        // Display balances of all wallets 
        if (node != null && node.Blockchain != null)
            foreach (var (address, balance) in node.Blockchain.GetAllBalancesFromChain())
            {
                var isMine = "";
                if (address == walletAddresses[0])
                    isMine += "*";
                if (walletAddresses.Contains(address))
                    isMine += "*";
                Logger.Log($"{isMine}{address}: {balance}");
            }
    }

    private static void DisplayChainInfo(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("5: Chain Info");
        if (node != null && node.Blockchain != null)
            node.Blockchain.PrintAllBlocksAndTransactions();
    }

    private static void DisplayContracts(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("6: Show Contracts");

        // List all deployed contracts and save to File
        var contractsDirectory = Path.Combine(FileSystem.AppDirectory, "Contracts");
        Directory.CreateDirectory(contractsDirectory);

        if (node != null && node.Blockchain != null)
        {
            Logger.Log("Contracts:");
            foreach (var contract in node.Blockchain.SmartContracts.Values)
                if (contract != null)
                {
                    Logger.Log($"Name: {contract.Name}, Owner: {contract.Owner}, Gas: {contract.Gas}");
                    var contractSrc = Serializer.DeserializeFromBase64<string>(contract.SerializedContractCode);
                    var path = Path.Combine(contractsDirectory, contract.Name);

                    File.WriteAllText(path + ".cs", contractSrc, Encoding.UTF8);
                    Logger.Log($"Contract: {contract.Name} saved to {path}");
                }

            if (Directory.Exists(contractsDirectory) && node.Blockchain.SmartContracts.Values.Any())
                Process.Start("explorer.exe", contractsDirectory);
        }
        else
        {
            Logger.Log("No contracts found.");
        }
    }
     
    private static async Task UploadContract(BlockchainServer.NodeStartupResult? node)
    {
        Logger.Log("7: Upload Contract");
        // Upload contract from file to blochchain

        var contractsDirectory = Path.Combine(FileSystem.AppDirectory, "Contracts");
        Directory.CreateDirectory(contractsDirectory);

        Logger.Log("Enter filename");
        var fileName = Console.ReadLine();

        if (!string.IsNullOrEmpty(fileName))
        {
            var fi = new FileInfo(fileName);
            if (!fi.Exists || fi.Extension.ToLower() != ".cs")
            {
                Logger.LogError("invalid file specified");
            }
            else
            {
                if (node != null && node.Blockchain != null)
                {
                    var contractCode = File.ReadAllText(fileName);

                    var pattern = @"\bclass\s+(\w+)";
                    var match = Regex.Match(contractCode, pattern);
                    var contractName = "";
                    if (match.Success && match.Groups.Count > 1)
                        contractName = match.Groups[1].Value;

                    var walletAddresses = SmartXWallet.LoadWalletAdresses();
                    if (walletAddresses.Count == 0)
                    {
                        Logger.LogError("SmartXWallet adresses are empty. SmartContractDemo cancelled.");
                        return;
                    }

                    var ownerAddress = walletAddresses[0];
                    var (contract, created) =
                        await SmartContract.Create(contractName, node.Blockchain, ownerAddress, contractCode);
                    if (!created)
                        Logger.Log($"Contract {contract} could not be created");
                    else
                        Logger.Log($"Contract {contract} created");
                }
                else
                {
                    Logger.LogError("Chain not available");
                }
            }
        }
        else
        {
            Logger.LogError("No contract filename specified");
        }
    }

    private static async Task ERC20Example(string ownerAddress, List<string> walletAddresses, Blockchain? blockchain)
    {
        // Deploy and interact with an ERC20 token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("ERC20Token", blockchain, ownerAddress, contractCode);
        if (!created)
            Logger.Log($"Contract {contract.Name} could not be created.");

        string[] inputs =
        [
            $"var token = new ERC20Token(\"ERC20Token\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
            "Logger.Log(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
            "Logger.LogLine(\"End ERC20 Demo 1\");"
        ];
        var result = await ExecuteSmartContract(blockchain, contract, inputs);


        //Save and Reload Test
        //var tmpFile = Path.GetTempFileName();
        //blockchain.Save(tmpFile);
        //blockchain = Blockchain.Load(tmpFile);

        inputs =
        [
            $"var token = new ERC20Token(\"ERC20Token\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
            $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Logger.Log(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
            "Logger.LogLine(\"End ERC20 Demo 2\");"
        ];

        result = await ExecuteSmartContract(blockchain, contract, inputs);
    }

    private static async Task ERC20ExtendedExample(string ownerAddress, List<string> walletAddresses,
        Blockchain? blockchain)
    {
        // Deploy and interact with an ERC20 token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20Extended.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("ERC20Extended", blockchain, ownerAddress, contractCode);
        if (!created)
            Logger.Log($"Contract {contract} could not be created.");

        string[] inputs =
        [
            $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
            "Logger.Log(\"[ERC20Extended] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
            "Logger.LogLine(\"End ERC20Extended Demo 1\");"
        ];

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        //Save and Reload Test
        //var tmpFile = Path.GetTempFileName();
        //blockchain.Save(tmpFile);
        //blockchain = Blockchain.Load(tmpFile);

        inputs =
        [
            $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
            $"token.Burn(5000000000, \"{ownerAddress}\", \"{PrivateKey}\");",
            $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Logger.Log(\"[ERC20Extended] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
            "Logger.LogLine(\"End ERC20Extended Demo 2\");"
        ];

        result = await ExecuteSmartContract(blockchain, contract, inputs);

        inputs =
        [
            $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"WRONGKEY\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
            "Logger.LogLine(\"End ERC20Extended Failure\");"
        ];

        result = await ExecuteSmartContract(blockchain, contract, inputs);
    }

    private static async Task GoldCoinExample(string minerAddress, List<string> wallet1Addresses,
        List<string> wallet2Addresses, Blockchain? blockchain)
    {
        // Deploy and interact with a GoldCoin token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "GoldCoin.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("GoldCoin", blockchain, minerAddress, contractCode);
        if (!created)
            Logger.Log($"Contract {contract} could not be created");

        string[] inputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            "Logger.Log(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
            "Logger.LogLine(\"End GoldCoin Demo 1\");"
        ];

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        string[] transferInputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{minerAddress}\", \"{wallet1Addresses[1]}\", 50000, \"{PrivateKey}\");",
            $"token.Transfer(\"{wallet1Addresses[1]}\", \"{wallet1Addresses[2]}\", 25000, \"{PrivateKey}\");",
            "Logger.LogLine(\"End GoldCoin Demo 2\");"
        ];

        result = await ExecuteSmartContract(blockchain, contract, transferInputs);

        string[] approvalInputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.Approve(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\", 20000, \"{PrivateKey}\");",
            $"var allowance = token.Allowance(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\");",
            "Logger.Log($\"[GoldCoin] Allowance: {allowance}\");",
            "Logger.LogLine(\"End GoldCoin Demo 3\");"
        ];

        result = await ExecuteSmartContract(blockchain, contract, approvalInputs);

        string[] transferFromInputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.TransferFrom(\"{wallet2Addresses[0]}\", \"{wallet1Addresses[1]}\", \"{wallet1Addresses[3]}\", 15000, \"{PrivateKey}\");",
            "Logger.Log(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
            "Logger.LogLine(\"End GoldCoin Demo 4\");"
        ];

        result = await ExecuteSmartContract(blockchain, contract, transferFromInputs);

        // RPC DEMO
        var notifyMint = "https://www.netregservice.com/smartx/rpc_collector.php";
        var notifyBurn = "https://www.netregservice.com/smartx/rpc_collector.php";

        string[] rpcHandlerCode =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            "",
            "// Register User",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            "",
            "// Register RPC handlers",
            $"token.RegisterHandler(\"Mint\", \"{notifyMint}\", \"{minerAddress}\");",
            $"token.RegisterHandler(\"Burn\", \"{notifyBurn}\", \"{minerAddress}\");",
            "",
            "// Subscribe to token events",
            "token.OnMint += async (address, amount) => await token.TriggerHandlers(\"Mint\", $\"{{\\\"address\\\":\\\"{address}\\\",\\\"amount\\\":{amount}}}\");",
            "token.OnBurn += async (address, amount) => await token.TriggerHandlers(\"Burn\", $\"{{\\\"address\\\":\\\"{address}\\\",\\\"amount\\\":{amount}}}\");",
            "",
            "// Trigger events",
            $"token.Mint(1000, \"{minerAddress}\", \"{minerAddress}\", \"{PrivateKey}\");",
            $"token.Burn(500, \"{minerAddress}\", \"{PrivateKey}\");"
        };

        result = await ExecuteSmartContract(blockchain, contract, rpcHandlerCode);


        Logger.Log("\nGoldCoin Smart Contract finished");
    }

    private static async Task<(string result, string updatedSerializedState)> ExecuteSmartContract(
        Blockchain? blockchain, SmartContract contract, string[] inputs, bool debug = false)
    {
        // Execute the given smart contract and handle exceptions
        (string result, string updatedSerializedState) executionResult = ("", "");

        try
        {
            if (blockchain != null) executionResult = await blockchain.ExecuteSmartContract(contract.Name, inputs);

            if (debug)
            {
                Logger.Log("Smart Contract Execution Completed.");
                Logger.Log("Execution Result:");
                Logger.Log($"Result: {executionResult.result}");
            }
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(executionResult.result) ||
                !string.IsNullOrEmpty(executionResult.updatedSerializedState))
                Logger.LogException(ex, $"{executionResult.result}");
            else
                Logger.LogException(ex);
        }

        return executionResult;
    }
}