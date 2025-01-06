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
            Logger.Log($"PrivateKey not found at: {privateKeyFile}. Enter PrivateKey\n");
            _privateKey = Console.ReadLine();
            return _privateKey;
        }
    }

    private static async Task Main(string[] args)
    {
        if (args.Contains("/testnet", StringComparer.OrdinalIgnoreCase))
        {
            Config.ChainName = "SmartXChain_Testnet";
            Logger.Log($"ChainName has been set to '{Config.ChainName}'.");
        }

        // Initialize application and start the blockchain server
        await InitializeApplicationAsync();

        var (blockchainServer, startup) = await BlockchainServer.StartServerAsync();

        await RunConsoleMenuAsync(startup);
    }

    private static async Task InitializeApplicationAsync()
    {
        Logger.Log("Application start");

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
                Logger.Log("----------------------------------------------------");

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
                        Logger.Log("9: Toggle Debug mode");
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
                Logger.Log($"ERROR: {ex.Message}");
                throw;
            }
    }

    private static void DisplayNodes(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.Log("n: Show nodes");
        if (startup != null && startup.Node != null)
        {
            if (Node.CurrentNodeIPs.Count == 0)
                Logger.Log("ERROR: no nodes found!");
            else
                foreach (var ip in Node.CurrentNodeIPs)
                    if (ip == startup.Node.NodeAddress)
                        Logger.Log("*" + ip);
                    else
                        Logger.Log(ip);
        }
    }

    private static void DisplayMenu()
    {
        // Show available menu options
        Logger.Log("----------------------------------------------------");
        Logger.Log("Enter mode:");
        Logger.Log("n: Show nodes");
        Logger.Log("c: Clear screen");
        Logger.Log();
        Logger.Log("1: Coin class tester");
        Logger.Log("2: SmartContract Demo");
        Logger.Log("3: Blockchain state");
        Logger.Log("4: Wallet Balances");
        Logger.Log("5: Chain Info");
        Logger.Log("6: Show Contracts");
        Logger.Log("7: Upload Contract");
        Logger.Log("8: Delete wallet and local chain");
        Logger.Log("9: Toggle Debug mode");
        Logger.Log("0: Exit");
        Logger.Log();
    }

    private static async Task RunCoinClassTesterAsync()
    {
        Logger.Log("1: Coin class tester");

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
        Logger.Log("2: SmartContract Demo");

        // Demonstrate ERC20 and GoldCoin smart contracts
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        if (walletAddresses.Count == 0)
        {
            Logger.Log("ERROR: SmartXWallet adresses are empty. SmartContractDemo cancelled.");
            return;
        }

        if (node == null)
        {
            Logger.Log("ERROR: Node is empty. SmartContractDemo cancelled.");
            return;
        }

        await ERC20Example(walletAddresses[0], walletAddresses, node.Blockchain);
        await ERC20ExtendedExample(walletAddresses[0], walletAddresses, node.Blockchain);
        await GoldCoinExample(walletAddresses[0], walletAddresses, SmartXWallet.LoadWalletAdresses(),
            node.Blockchain);

        PerformNativeTransfer(node.Blockchain, walletAddresses);
    }

    private static async void PerformNativeTransfer(Blockchain? chain, List<string> walletAddresses)
    {
        // Perform native token transfer
        var transaction = new Transaction();

        transaction.RegisterUser(walletAddresses[0], PrivateKey);
        var (transferred,message) = await transaction.Transfer(
            chain,
            walletAddresses[0],
            walletAddresses[1],
            (decimal)0.01,
            PrivateKey,
            "native transfer",
            "49.83278, 9.88167");

        if (chain != null)
            await chain.MinePendingTransactions(walletAddresses[0]);

        if (transferred)
            Logger.Log($"Transferred SCX from {walletAddresses[0]} to {walletAddresses[1]}");
        else
            Logger.Log($"SCX could not be transferred from {walletAddresses[0]} to {walletAddresses[1]}");
    }

    private static void DisplayBlockchainState(BlockchainServer.NodeStartupResult? node)
    {
        Logger.Log("3: Blockchain state");

        // Show current state of the blockchain
        if (node != null)
            foreach (var block in node.Blockchain.Chain)
                Logger.Log($"Block {node.Blockchain.Chain.IndexOf(block)}: {block.Hash}");
    }

    private static void DisplayWalletBalances(BlockchainServer.NodeStartupResult? node)
    {
        Logger.Log("4: Wallet Balances");

        // Display balances of all wallets 
        if (node != null && node.Blockchain != null)
            foreach (var (address, balance) in node.Blockchain.GetAllBalancesFromChain())
                Logger.Log($"{address}: {balance}");
    }

    private static void DisplayChainInfo(BlockchainServer.NodeStartupResult? node)
    {
        Logger.Log("5: Chain Info");
        if (node != null && node.Blockchain != null)
            node.Blockchain.PrintAllBlocksAndTransactions();
    }

    private static void DeleteWallet(BlockchainServer.NodeStartupResult? node)
    {
        Logger.Log("8: Delete wallet and local chain");

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
                Logger.Log($"Deleted {file}");
            }
            catch (Exception e)
            {
                Logger.Log($"Error deleting file {file}: {e.Message}");
            }

        Logger.Log("Press any key to end");
        Console.ReadKey();
    }

    private static void DisplayContracts(BlockchainServer.NodeStartupResult? node)
    {
        Logger.Log("6: Show Contracts");

        // List all deployed contracts and save to File
        var contractsDirectory = Path.Combine(Config.AppDirectory(), "Contracts");
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

        var contractsDirectory = Path.Combine(Config.AppDirectory(), "Contracts");
        Directory.CreateDirectory(contractsDirectory);

        Logger.Log("Enter filename");
        var fileName = Console.ReadLine();

        if (!string.IsNullOrEmpty(fileName))
        {
            var fi = new FileInfo(fileName);
            if (!fi.Exists || fi.Extension.ToLower() != ".cs")
            {
                Logger.Log("ERROR: invalid file specified");
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
                        Logger.Log("ERROR: SmartXWallet adresses are empty. SmartContractDemo cancelled.");
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
                    Logger.Log("ERROR: Chain not available");
                }
            }
        }
        else
        {
            Logger.Log("ERROR: No contract filename specified");
        }
    }

    private static async Task ERC20Example(string ownerAddress, List<string> walletAddresses, Blockchain? blockchain)
    {
        // Deploy and interact with an ERC20 token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("ERC20Token", blockchain, ownerAddress, contractCode);
        if (!created) Logger.Log($"Contract {contract} could not be created.");

        string[] inputs =
        {
            $"var token = new ERC20Token(\"ERC20Token\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
            "Logger.Log(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        //Save and Reload Test
        //var tmpFile = Path.GetTempFileName();
        //blockchain.Save(tmpFile);
        //blockchain = Blockchain.Load(tmpFile);

        inputs = new[]
        {
            $"var token = new ERC20Token(\"ERC20Token\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
            $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Logger.Log(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        result = await ExecuteSmartContract(blockchain, contract, inputs);
    }

    private static async Task ERC20ExtendedExample(string ownerAddress, List<string> walletAddresses, Blockchain? blockchain)
    {
        // Deploy and interact with an ERC20 token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20Extended.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("ERC20Extended", blockchain, ownerAddress, contractCode);
        if (!created) Logger.Log($"Contract {contract} could not be created.");

        string[] inputs =
        {
            $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
            "Logger.Log(\"[ERC20Extended] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        //Save and Reload Test
        //var tmpFile = Path.GetTempFileName();
        //blockchain.Save(tmpFile);
        //blockchain = Blockchain.Load(tmpFile);

        inputs = new[]
        {
            $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
            $"token.Burn(5000000000, \"{ownerAddress}\", \"{PrivateKey}\");",
            $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Logger.Log(\"[ERC20Extended] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
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
        if (!created) Logger.Log($"Contract {contract} could not be created");

        string[] inputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            "Logger.Log(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
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
            "Logger.Log($\"[GoldCoin] Allowance: {allowance}\");"
        };

        result = await ExecuteSmartContract(blockchain, contract, approvalInputs);

        string[] transferFromInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
            $"token.TransferFrom(\"{wallet2Addresses[0]}\", \"{wallet1Addresses[1]}\", \"{wallet1Addresses[3]}\", 15000, \"{PrivateKey}\");",
            "Logger.Log(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

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
            "var rpcHandler = new RpcEventHandler();",
            $"rpcHandler.RegisterHandler(\"Mint\", \"{notifyMint}\", \"{minerAddress}\");",
            $"rpcHandler.RegisterHandler(\"Burn\", \"{notifyBurn}\", \"{minerAddress}\");",
            "",
            "// Subscribe to token events",
            "token.OnMint += async (address, amount) => await rpcHandler.TriggerHandlers(\"Mint\", $\"{{\\\"address\\\":\\\"{address}\\\",\\\"amount\\\":{amount}}}\");",
            "token.OnBurn += async (address, amount) => await rpcHandler.TriggerHandlers(\"Burn\", $\"{{\\\"address\\\":\\\"{address}\\\",\\\"amount\\\":{amount}}}\");",
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
        (string result, string updatedSerializedState) executionResult = (null, null);

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
        catch (Exception e)
        {
            if (!string.IsNullOrEmpty(executionResult.result) ||
                !string.IsNullOrEmpty(executionResult.updatedSerializedState))
                Logger.Log($"ERROR: {executionResult.result} {e.Message}");
            else
                Logger.Log($"ERROR: {e.Message}");
        }

        return executionResult;
    }
}