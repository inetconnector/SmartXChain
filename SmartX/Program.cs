using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using SmartXChain;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Server;
using SmartXChain.Utils;
using SmartXChain.Validators;

namespace SmartX;

internal class Program
{
    private static string? _privateKey = "";

    private static string? PrivateKey
    {
        get
        {
            if (!string.IsNullOrEmpty(_privateKey))
                return _privateKey;

            var privateKeyFile = Path.Combine(Config.AppDirectory(), "privatekey.txt");
            if (File.Exists(privateKeyFile)) return File.ReadAllText(privateKeyFile);
            Logger.LogMessage($"PrivateKey not found at: {privateKeyFile}. Enter PrivateKey\n");
            _privateKey = Console.ReadLine();
            return _privateKey;
        }
    }

    private static async Task Main(string[] args)
    {
        if (args.Contains("/testnet", StringComparer.OrdinalIgnoreCase))
        {
            Config.ChainName = "SmartXChain_Testnet";
            Logger.LogMessage($"ChainName has been set to '{Config.ChainName}'.");
        }

        // Initialize application and start the blockchain server
        await InitializeApplicationAsync();

        var (blockchainServer, startup) = await BlockchainServer.StartServerAsync();

        await RunConsoleMenuAsync(startup);
    }

    private static async Task InitializeApplicationAsync()
    {
        Logger.LogMessage("Application start");

        // Retrieve public IP and ensure wallet and keys are set up
        await NetworkUtils.GetPublicIPAsync();

        if (string.IsNullOrEmpty(Config.Default.MinerAddress))
        {
            SmartXWallet.GenerateWallet();
            Config.Default.ReloadConfig();
        }

        if (string.IsNullOrEmpty(Config.Default.ServerPublicKey))
            Config.Default.GenerateServerKeys();
    }

    private static async Task RunConsoleMenuAsync(BlockchainServer.NodeStartupResult? startup)
    {
        // Display and process console menu 

        while (true)
            try
            {
                DisplayMenu();

                var mode = Console.ReadKey().KeyChar;
                Logger.LogMessage("----------------------------------------------------");

                switch (mode)
                {
                    case 'c':
                        Console.Clear();
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
                        DeleteWallet(startup);
                        return;
                    case '9':
                        Logger.LogMessage("9: Toggle Debug mode");
                        Config.Default.SetProperty(Config.ConfigKey.Debug,
                            (!Config.Default.Debug).ToString());
                        break;
                    case 'n':
                        DisplayNodes(startup);
                        break;
                    case '0':
                        return;
                }
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"ERROR: {ex.Message}");
                throw;
            }
    }

    private static void DisplayNodes(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.LogMessage("n: Show nodes");
        if (startup != null && startup.Node != null)
        {
            if (Node.CurrentNodeIPs.Count == 0)
                Logger.LogMessage("ERROR: no nodes found!");
            else
                foreach (var ip in Node.CurrentNodeIPs)
                    if (ip == startup.Node.NodeAddress)
                        Logger.LogMessage("*" + ip);
                    else
                        Logger.LogMessage(ip);
        }
    }

    private static void DisplayMenu()
    {
        // Show available menu options
        Logger.LogMessage("----------------------------------------------------");
        Logger.LogMessage("Enter mode:");
        Logger.LogMessage("n: Show nodes");
        Logger.LogMessage("c: Clear screen");
        Logger.LogMessage();
        Logger.LogMessage("1: Coin class tester");
        Logger.LogMessage("2: SmartContract Demo");
        Logger.LogMessage("3: Blockchain state");
        Logger.LogMessage("4: Wallet Balances");
        Logger.LogMessage("5: Chain Info");
        Logger.LogMessage("6: Show Contracts");
        Logger.LogMessage("7: Upload Contract");
        Logger.LogMessage("8: Delete wallet and local chain");
        Logger.LogMessage("9: Toggle Debug mode");
        Logger.LogMessage("0: Exit");
        Logger.LogMessage();
    }

    private static async Task RunCoinClassTesterAsync()
    {
        Logger.LogMessage("1: Coin class tester");

        // Test ERC20 coin functionalities
        var walletAddresses = SmartXWallet.LoadWalletAdresses();

        var seedFile = Path.Combine(Config.AppDirectory(), "seed.txt");
        var seed = File.ReadAllText(seedFile);

        var token = new ERC20Token("SmartXchain", "SXC", 18, 10000000000, walletAddresses[0]);
        token.RegisterUser(walletAddresses[0], seed);
        token.Transfer(walletAddresses[0], walletAddresses[1], 100, seed);
        token.Transfer(walletAddresses[1], walletAddresses[2], 50, seed);

        var serializedData = Serializer.SerializeToBase64(token);
        var deserializedToken = Serializer.DeserializeFromBase64<ERC20Token>(serializedData);
        deserializedToken.Transfer(walletAddresses[2], walletAddresses[3], 25, seed);

        DisplayTokenDetails(deserializedToken);
    }

    private static void DisplayTokenDetails(ERC20Token token)
    {
        // Display token details, balances, and allowances
        Logger.LogMessage("Deserialized Token:");
        Logger.LogMessage($"Name: {token.Name}");
        Logger.LogMessage($"Symbol: {token.Symbol}");
        Logger.LogMessage($"Decimals: {token.Decimals}");
        Logger.LogMessage($"Total Supply: {token.TotalSupply}");

        Logger.LogMessage("Balances:");
        foreach (var balance in token.GetBalances)
            Logger.LogMessage($"{balance.Key}: {balance.Value}");

        Logger.LogMessage("Allowances:");
        foreach (var allowance in token.GetAllowances)
        {
            Logger.LogMessage($"{allowance.Key}:");
            foreach (var spender in allowance.Value)
                Logger.LogMessage($"  {spender.Key}: {spender.Value}");
        }
    }

    private static async Task RunSmartContractDemoAsync(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("2: SmartContract Demo");

        // Demonstrate ERC20 and GoldCoin smart contracts
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        if (walletAddresses.Count == 0)
        {
            Logger.LogMessage("ERROR: SmartXWallet adresses are empty. SmartContractDemo cancelled.");
            return;
        }

        if (node == null)
        {
            Logger.LogMessage("ERROR: Node is empty. SmartContractDemo cancelled.");
            return;
        }

        await ERC20Example(walletAddresses[0], walletAddresses, node.Blockchain);
        await GoldCoinExample(walletAddresses[0], walletAddresses, SmartXWallet.LoadWalletAdresses(),
            node.Blockchain);

        PerformNativeTransfer(node.Blockchain, walletAddresses);
    }

    private static async void PerformNativeTransfer(Blockchain? chain, List<string> walletAddresses)
    {
        // Perform native token transfer
        var transaction = new Transaction();

        transaction.RegisterUser(walletAddresses[0], PrivateKey);
        var transferred = await transaction.Transfer(
            chain,
            walletAddresses[0],
            walletAddresses[1],
            0.01d,
            PrivateKey,
            "native transfer",
            "49.83278, 9.88167");

        if (chain != null)
            await chain.MinePendingTransactions(walletAddresses[0]);

        if (transferred)
            Logger.LogMessage($"Transferred SCX from {walletAddresses[0]} to {walletAddresses[1]}");
    }

    private static void DisplayBlockchainState(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("3: Blockchain state");

        // Show current state of the blockchain
        if (node != null)
            foreach (var block in node.Blockchain.Chain)
                Logger.LogMessage($"Block {node.Blockchain.Chain.IndexOf(block)}: {block.Hash}");
    }

    private static void DisplayWalletBalances(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("4: Wallet Balances");

        // Display balances of all wallets 
        if (node != null && node.Blockchain != null)
            foreach (var (address, balance) in node.Blockchain.GetAllBalancesFromChain())
                Logger.LogMessage($"{address}: {balance}");
    }

    private static void DisplayChainInfo(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("5: Chain Info");
        if (node != null && node.Blockchain != null)
            node.Blockchain.PrintAllBlocksAndTransactions();
    }

    private static void DeleteWallet(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("8: Delete wallet and local chain");

        // deletes wallet
        SmartXWallet.DeleteWallet();

        //delete saved contracts
        var contractsPath = Path.Combine(Config.Default.BlockchainPath, "Contracts");
        if (Directory.Exists(contractsPath))
            Directory.Delete(contractsPath, true);

        //delete local chains
        foreach (var file in Directory.GetFiles(Config.Default.BlockchainPath))
            try
            {
                File.Delete(file);
                Logger.LogMessage($"Deleted {file}");
            }
            catch (Exception e)
            {
                Logger.LogMessage($"Error deleting file {file}: {e.Message}");
            }

        Logger.LogMessage("Press any key to end");
        Console.ReadKey();
    }

    private static void DisplayContracts(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("6: Show Contracts");

        // List all deployed contracts and save to File
        var contractsDirectory = Path.Combine(Config.AppDirectory(), "Contracts");
        Directory.CreateDirectory(contractsDirectory);

        if (node != null && node.Blockchain != null)
        {
            Logger.LogMessage("Contracts:");
            foreach (var contract in node.Blockchain.SmartContracts.Values)
                if (contract != null)
                {
                    Logger.LogMessage($"Name: {contract.Name}, Owner: {contract.Owner}, Gas: {contract.Gas}");
                    var contractSrc = Serializer.DeserializeFromBase64<string>(contract.SerializedContractCode);
                    var path = Path.Combine(contractsDirectory, contract.Name);

                    File.WriteAllText(path + ".cs", contractSrc, Encoding.UTF8);
                    Logger.LogMessage($"Contract: {contract.Name} saved to {path}");
                }

            if (Directory.Exists(contractsDirectory) && node.Blockchain.SmartContracts.Values.Any())
                Process.Start("explorer.exe", contractsDirectory);
        }
        else
        {
            Logger.LogMessage("No contracts found.");
        }
    }

    private static async Task UploadContract(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogMessage("7: Upload Contract");
        // Upload contract from file to blochchain

        var contractsDirectory = Path.Combine(Config.AppDirectory(), "Contracts");
        Directory.CreateDirectory(contractsDirectory);

        Logger.LogMessage("Enter filename");
        var fileName = Console.ReadLine();

        if (!string.IsNullOrEmpty(fileName))
        {
            var fi = new FileInfo(fileName);
            if (!fi.Exists || fi.Extension.ToLower() != ".cs")
            {
                Logger.LogMessage("ERROR: invalid file specified");
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
                        Logger.LogMessage("ERROR: SmartXWallet adresses are empty. SmartContractDemo cancelled.");
                        return;
                    }

                    var ownerAddress = walletAddresses[0];
                    var (contract, created) =
                        await SmartContract.Create(contractName, node.Blockchain, ownerAddress, contractCode);
                    if (!created)
                        Logger.LogMessage($"Contract {contract} could not be created");
                    else
                        Logger.LogMessage($"Contract {contract} created");
                }
                else
                {
                    Logger.LogMessage("ERROR: Chain not available");
                }
            }
        }
        else
        {
            Logger.LogMessage("ERROR: No contract filename specified");
        }
    }

    private static async Task ERC20Example(string ownerAddress, List<string> walletAddresses, Blockchain? blockchain)
    {
        // Deploy and interact with an ERC20 token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("SmartXchain", blockchain, ownerAddress, contractCode);
        if (!created) Logger.LogMessage($"Contract {contract} could not be created.");

        string[] inputs =
        {
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
            "Log(JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        //Save and Reload Test
        //var tmpFile = Path.GetTempFileName();
        //blockchain.Save(tmpFile);
        //blockchain = Blockchain.Load(tmpFile);

        inputs = new[]
        {
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
            $"Log(token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Log(token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Log(token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Log(JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        result = await ExecuteSmartContract(blockchain, contract, inputs);
    }

    private static async Task GoldCoinExample(string minerAddress, List<string> wallet1Addresses,
        List<string> wallet2Addresses, Blockchain? blockchain)
    {
        // Deploy and interact with a GoldCoin token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "GoldCoin.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("GoldCoin", blockchain, minerAddress, contractCode);
        if (!created) Logger.LogMessage($"Contract {contract} could not be created");

        string[] inputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            "Logger.LogMessage(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        string[] transferInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{minerAddress}\", \"{wallet1Addresses[1]}\", 50000, \"{PrivateKey}\");",
            $"token.Transfer(\"{wallet1Addresses[1]}\", \"{wallet1Addresses[2]}\", 25000, \"{PrivateKey}\");"
        };

        result = await ExecuteSmartContract(blockchain, contract, transferInputs);

        string[] approvalInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.Approve(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\", 20000, \"{PrivateKey}\");",
            $"var allowance = token.Allowance(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\");",
            "Logger.LogMessage($\"[GoldCoin] Allowance: {allowance}\");"
        };

        result = await ExecuteSmartContract(blockchain, contract, approvalInputs);

        string[] transferFromInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.TransferFrom(\"{wallet2Addresses[0]}\", \"{wallet1Addresses[1]}\", \"{wallet1Addresses[3]}\", 15000, \"{PrivateKey}\");",
            "Logger.LogMessage(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        result = await ExecuteSmartContract(blockchain, contract, transferFromInputs);

        Logger.LogMessage("\nGoldCoin Smart Contract finished");
    }

    private static async Task<(string result, string updatedSerializedState)> ExecuteSmartContract(
        Blockchain? blockchain, SmartContract contract, string[] inputs, bool debug = false)
    {
        // Execute the given smart contract and handle exceptions
        (string result, string updatedSerializedState) executionResult = (null, null);

        try
        {
            if (blockchain != null) executionResult = await blockchain.ExecuteSmartContract(contract.Name, inputs);

            if (debug)
            {
                Logger.LogMessage("Smart Contract Execution Completed.");
                Logger.LogMessage("Execution Result:");
                Logger.LogMessage($"Result: {executionResult.result}");
            }
        }
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(executionResult.result) ||
                !string.IsNullOrEmpty(executionResult.updatedSerializedState))
                Logger.LogMessage($"ERROR: {executionResult.result} {e.Message}");
            else
                Logger.LogMessage($"ERROR: {e.Message}");
        }

        return executionResult;
    }
}