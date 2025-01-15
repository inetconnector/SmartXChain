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

            FileSystem.CreateBackup();
        }

        // Initialize application and start the blockchain server
        await InitializeApplicationAsync();

        var (blockchainServer, startup) = await BlockchainServer.StartServerAsync();

        await RunConsoleMenuAsync(startup);
    }

    private static Task InitializeApplicationAsync()
    {
        Logger.LogLine("Application start");

        // ensure wallet and keys are set up

        if (string.IsNullOrEmpty(Config.Default.MinerAddress))
        {
            SmartXWallet.GenerateWallet();
            Config.Default.ReloadConfig();
        }

        //create server keys
        if (string.IsNullOrEmpty(Config.Default.PublicKey))
            Config.Default.GenerateServerKeys();

        SetWebserverCertificate();
        return Task.CompletedTask;
    }

    private static void SetWebserverCertificate()
    {
        if (Config.Default.URL.ToLower().StartsWith("https"))
        {
            if (!string.IsNullOrEmpty(Config.Default.SSLCertificate) && File.Exists(Config.Default.SSLCertificate))
            {
                //assign certificate for https
                BlockchainServer.WebserverCertificate =
                    CertificateManager.GetCertificate(Config.Default.SSLCertificate);
            }
            else
            {
                //create webserver certificate
                var name = Config.ChainName;
                var certManager = new CertificateManager(name,
                    Config.AppDirectory(),
                    name + ".pfx",
                    name);

                var certPath = certManager.GenerateCertificate(Config.Default.URL);
                if (!certManager.IsCertificateInstalled()) certManager.InstallCertificate(certPath);

                //assign certificate for https
                BlockchainServer.WebserverCertificate = certManager.GetCertificate();
            }
        }
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
                        Config.Default.SetProperty(Config.ConfigKey.Debug,
                            (!Config.Default.Debug).ToString());
                        break;
                    case 'e':
                        EraseWallet(startup);
                        return;
                    case 'r':
                        if (Config.TestNet && RebootChains(startup))
                        {
                            Thread.Sleep(5000);
                            Functions.RestartApplication();
                        } 
                        break;
                    case 'n':
                        DisplayNodes(startup);
                        break;
                    case 's':
                        await SendNativeTokens(startup);
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
        Logger.Log("s: Send SCX Tokens");
        Logger.Log("c: Clear screen");
        Logger.Log("e: Erase wallet and local chain");
        if (Config.TestNet) 
            Logger.Log("r: Reboot nodes");
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

    private static async Task SendNativeTokens(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.LogLine("s: Send SCX Tokens");
        var walletAddresses = SmartXWallet.LoadWalletAdresses();

        Logger.Log("Enter recipient");
        var recipient = Console.ReadLine();

        Logger.Log("Enter SCX amount (i.e. 0.01)");
        var amount = Convert.ToDecimal(Console.ReadLine());

        Logger.Log("Enter info");
        var info = Console.ReadLine();

        if (amount > 0)
        {
            Logger.Log($"Ready to send {amount} to {recipient} ? (y/n)");
            if (Console.ReadLine() == "y")
            {
                var success = await NativeSCXTransfer(startup.Blockchain, walletAddresses[0], recipient, amount,
                    info, PrivateKey);
                Logger.Log("Success: " + success);
            }
        }
    }

    private static void DisplayNodes(BlockchainServer.NodeStartupResult? startup)
    {
        Logger.LogLine("n: Show nodes");
        if (startup != null && startup.Node != null)
        {
            if (Node.CurrentNodeIPs.Count == 0)
                Logger.LogError("no nodes found!");
            else
                foreach (var ip in Node.CurrentNodeIPs)
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
        var seedFile = Path.Combine(Config.AppDirectory(), "seed.txt");
        var seed = File.ReadAllText(seedFile);


        //Get GasContract
        Logger.LogLine("Get GasContract");
        var gas = new GasConfiguration();  

        Logger.LogLine("Gas configuration:");
        foreach (var gasInfo in gas.ToString().Split(Environment.NewLine))
            Logger.Log(gasInfo, false);

        Logger.LogLine("Updated Gas configuration:");
        gas.UpdateParameter( GasConfiguration.GasConfigParameter.BaseGasTransaction,
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
        //get Wallet
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        if (walletAddresses.Count == 0)
        {
            Logger.LogError("SmartXWallet adresses are empty. SmartContractDemo cancelled.");
            return;
        }

        // Native SXC transfer for Gas  
        if (PrivateKey != null && node != null)
        {
            await NativeSCXTransfer(node.Blockchain,
                walletAddresses[0],
                walletAddresses[1],
                (decimal)1000.0,
                "49.83278, 9.88167",
                PrivateKey);


            await NativeSCXTransfer(node.Blockchain,
                walletAddresses[0],
                walletAddresses[2],
                (decimal)1000.0,
                "49.83278, 9.88167",
                PrivateKey);
        }
         
        DisplayWalletBalances(node);

        Logger.LogLine("2: SmartContract Demo");
         
        if (node == null)
        {
            Logger.LogError("Node is empty. SmartContractDemo cancelled.");
            return;
        }


        // Demonstrate ERC20 and GoldCoin smart contracts  
        await ERC20Example(walletAddresses[0], walletAddresses, node.Blockchain);
        await ERC20ExtendedExample(walletAddresses[0], walletAddresses, node.Blockchain);
        await GoldCoinExample(walletAddresses[0], walletAddresses, SmartXWallet.LoadWalletAdresses(),
            node.Blockchain);
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
                 
                if (chain != null)
                    await chain.MinePendingTransactions(sender);

                
                if (transferred)
                    Logger.Log($"Transferred SCX from {sender} to {recipient}", false);
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
            Logger.LogException(ex,$"Transferring SCX from {sender} to {recipient} failed");
        }

        return false;
    }

    private static void DisplayBlockchainState(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("3: Blockchain state");

        // Show current state of the blockchain
        if (node != null && node.Blockchain != null && node.Blockchain.Chain != null)
            foreach (var block in node.Blockchain.Chain)
                Logger.Log($"Block {node.Blockchain.Chain.IndexOf(block)}: {block.Hash}");
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

    private static void EraseWallet(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("e: Delete wallet and local chain");

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
            catch (Exception ex)
            {
                Logger.LogException(ex, $"deleting file {file}"); 
            }

        Logger.Log("Press any key to end");
        Console.ReadKey();
    }

    private static bool RebootChains(BlockchainServer.NodeStartupResult? node)
    {
        Logger.LogLine("r: Reboot chains on all servers. Clears all chains in testnet");
        Logger.Log("Are you sure to reboot and clear testnet chains on all servers?");
        Logger.Log("This action cannot be undone. (yes/no):");

        var confirmation = Console.ReadLine();

        if (confirmation == null || !confirmation.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log("Chain reboot canceled.");
            return false;
        }

        if (node != null) _ = node.Node.RebootChainsAsync();
        return true;
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
        if (!created) Logger.Log($"Contract {contract.Name} could not be created.");

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

    private static async Task ERC20ExtendedExample(string ownerAddress, List<string> walletAddresses,
        Blockchain? blockchain)
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