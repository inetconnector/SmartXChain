using System.Text.RegularExpressions;
using SmartXChain;
using SmartXChain.BlockchainCore;
using SmartXChain.ClientServer;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using static SmartXChain.Utils.Config;
using FileSystem = Microsoft.Maui.Storage.FileSystem;

namespace SmartXapp;

public static class BlockchainHelper
{
    private static BlockchainServer.NodeStartupResult? _startup;

    public static string PrivateKey => Default.PrivateKey;


    public static async Task InitializeApplicationAsync()
    {
        Logger.LogLine("Initializing application...");

        ChainName = ChainNames.SmartXChain_Testnet;
        var port = 5556;
        SmartXChain.Utils.FileSystem.CreateBackup();

        Logger.Log($"ChainName has been set to '{ChainName}'.");
        var configFile = SmartXChain.Utils.FileSystem.ConfigFile;
        var configContent = "";

        await CopyFileFromAssets("ERC20.cs", SmartXChain.Utils.FileSystem.ContractsDir);
        await CopyFileFromAssets("ERC20Extended.cs", SmartXChain.Utils.FileSystem.ContractsDir);
        await CopyFileFromAssets("GoldCoin.cs", SmartXChain.Utils.FileSystem.ContractsDir);

        //copy defaults
        if (!File.Exists(configFile) || TestNet)
        {
            if (ChainName == ChainNames.SmartXChain_Testnet)
                configContent = await GetFileFromAssets("config.testnet.txt");
            else if (ChainName == ChainNames.SmartXChain)
                configContent = await GetFileFromAssets("config.txt");

            File.WriteAllText(configFile, configContent);
        }

        Default.ReloadConfig();


        //htm
        await CopyFileFromAssets("index.html", SmartXChain.Utils.FileSystem.AppDirectory);
        var indexhtmSrc = Path.Combine(SmartXChain.Utils.FileSystem.AppDirectory, "index.html");
        var indexhtmDest = Path.Combine(SmartXChain.Utils.FileSystem.WWWRoot, "index.html");
        if (File.Exists(indexhtmSrc))
        {
            var html = Functions.ReplaceBody(File.ReadAllText(indexhtmSrc));
            File.WriteAllText(indexhtmDest, html);
        }

        //get public IP
        var publicIP = await NetworkUtils.GetPublicIPAsync(debug: true);
        Default.SetProperty(ConfigKey.NodeAddress, $"http://{publicIP}:{port}");

        Logger.Log($"Current Node: {ConfigKey.NodeAddress}");
        foreach (var peer in Default.SignalHubs)
            Logger.Log($"Current Peer: {peer}");

        Logger.Log($"Current Chain: {Default.ChainId}");

        //generate wallet
        if (string.IsNullOrEmpty(Default.MinerAddress))
        {
            SmartXWallet.GenerateWallet();
            Default.ReloadConfig();
        }

        //generate server keys
        if (string.IsNullOrEmpty(Default.PublicKey)) Default.GenerateServerKeys();

        await Task.CompletedTask;
    }

    private static async Task CopyFileFromAssets(string fileName, string targetDirectory)
    {
        await using var s = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var r = new StreamReader(s);
        var file = r.ReadToEnd();
        File.WriteAllText(Path.Combine(targetDirectory, fileName), file);
    }

    private static async Task<string> GetFileFromAssets(string fileName)
    {
        await using var s = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var r = new StreamReader(s);
        var file = r.ReadToEnd();
        return file;
    }

    public static async Task<BlockchainServer.NodeStartupResult?> StartServerAsync()
    {
        Logger.LogLine("Starting blockchain server...");
        _startup = await BlockchainServer.StartServerAsync();
        return _startup;
    }

    public static async Task<bool> ImportAmountFromFile(string recipient, string fileContent)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            Logger.Log("Recipient is invalid.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(fileContent))
        {
            Logger.Log("File content is empty.");
            return false;
        }

        try
        {
            if (_startup?.Blockchain != null)
            {
                var (success, message) =
                    await Transaction.ImportFromFileToAccount(_startup.Blockchain, fileContent, recipient);
                Logger.Log(success ? $"Import successful: {message}" : $"Import failed: {message}");
                return success;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error importing tokens: {ex.Message}");
        }

        return false;
    }

    public static async Task<bool> SendNativeTokens(string recipient, decimal amount, string? data = null)
    {
        if (_startup?.Blockchain == null)
        {
            Logger.Log("Blockchain not initialized.");
            return false;
        }

        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        var sender = walletAddresses.Count > 0 ? walletAddresses[0] : null;

        if (sender == null)
        {
            Logger.Log("Sender address not found.");
            return false;
        }

        var success = await Transaction.Transfer(
            _startup.Blockchain, sender, recipient, amount, PrivateKey, "native transfer", data ?? string.Empty);

        Logger.Log(success.Item1 ? "Transfer successful." : $"Transfer failed: {success.Item2}");
        return success.Item1;
    }

    public static Task<string> GetBlockchainState()
    {
        if (_startup?.Blockchain?.Chain == null)
            return Task.FromResult("Blockchain not initialized.");

        var state = string.Join(Environment.NewLine, _startup.Blockchain.Chain);
        return Task.FromResult($"Blockchain State:\n{state}");
    }

    public static Task<string> GetWalletBalances()
    {
        if (_startup?.Blockchain == null)
            return Task.FromResult("Blockchain not initialized.");

        var balances = _startup.Blockchain.GetAllBalancesFromChain();
        var result = string.Join(Environment.NewLine, balances);
        return Task.FromResult($"Wallet Balances:\n{result}");
    }

    public static async Task<bool> UploadSmartContract(string fileName, string contractCode)
    {
        if (Path.GetExtension(fileName)?.ToLower() != ".cs")
        {
            Logger.Log("Invalid file specified.");
            return false;
        }

        var match = Regex.Match(contractCode, @"\bclass\s+(\w+)");
        var contractName = match.Success ? match.Groups[1].Value : "UnnamedContract";

        if (_startup?.Blockchain == null)
        {
            Logger.Log("Blockchain not initialized.");
            return false;
        }

        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        var ownerAddress = walletAddresses.Count > 0 ? walletAddresses[0] : null;

        if (ownerAddress == null)
        {
            Logger.Log("Owner address not found.");
            return false;
        }

        var (contract, created) =
            await SmartContract.Create(contractName, _startup.Blockchain, ownerAddress, contractCode);
        return created;
    }

    public static async Task RunSmartContractDemoAsync()
    {
        Logger.LogLine("Running Smart Contract Demo...");
        if (_startup?.Blockchain == null)
        {
            Logger.Log("Blockchain not initialized.");
            return;
        }

        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        if (walletAddresses.Count == 0)
        {
            Logger.Log("No wallet addresses found.");
            return;
        }

        //send 1000 scx transaction  
        await Transaction.Transfer(_startup.Blockchain, walletAddresses[0], walletAddresses[1], 1000,
            PrivateKey,
            "Smart Contract Demo");


        var ERC20 = Path.Combine(FileSystem.AppDataDirectory,
            "ERC20.cs");

        if (File.Exists(ERC20))
        {
            var csText = File.ReadAllText(ERC20);
            Logger.Log(csText, false);
        }

        Logger.Log("Smart Contract Demo completed successfully.");
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
                //send 1000 scx transaction
                await NativeSCXTransfer(
                    node.Blockchain,
                    sender,
                    recipient,
                    sendAmount,
                    "49.83278, 9.88167",
                    PrivateKey);

                await node.Blockchain.MinePendingTransactions(walletAddresses[0]);


                // Add demonstration tasks for ERC20, ERC20Extendedand and GoldCoin smart contracts
                //ERC20Example(sender, walletAddresses, node.Blockchain);
                //ERC20ExtendedExample(sender, walletAddresses, node.Blockchain);
                //GoldCoinExample(sender, walletAddresses, SmartXWallet.LoadWalletAdresses(), node.Blockchain);


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
    //#region Contracts


    //private static async Task ERC20Example(string ownerAddress, List<string> walletAddresses, Blockchain? blockchain)
    //{
    //    // Deploy and interact with an ERC20 token contract
    //    var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
    //    var contractCode = File.ReadAllText(contractFile);
    //    var (contract, created) = await SmartContract.Create("ERC20Token", blockchain, ownerAddress, contractCode);
    //    if (!created) Logger.Log($"Contract {contract.Name} could not be created.");

    //    string[] inputs =
    //    [
    //        $"var token = new ERC20Token(\"ERC20Token\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
    //        $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
    //        $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
    //        $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
    //        "Logger.Log(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
    //        "Logger.LogLine(\"End ERC20 Demo 1\");"
    //    ];
    //    var result = await ExecuteSmartContract(blockchain, contract, inputs);


    //    //Save and Reload Test
    //    //var tmpFile = Path.GetTempFileName();
    //    //blockchain.Save(tmpFile);
    //    //blockchain = Blockchain.Load(tmpFile);

    //    inputs =
    //    [
    //        $"var token = new ERC20Token(\"ERC20Token\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
    //        $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
    //        $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
    //        $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
    //        $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
    //        $"Logger.Log(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
    //        "Logger.Log(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
    //        "Logger.LogLine(\"End ERC20 Demo 2\");"
    //    ];

    //    result = await ExecuteSmartContract(blockchain, contract, inputs);
    //}


    //private static async Task ERC20ExtendedExample(string ownerAddress, List<string> walletAddresses,
    //    Blockchain? blockchain)
    //{
    //    // Deploy and interact with an ERC20 token contract
    //    var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20Extended.cs");
    //    var contractCode = File.ReadAllText(contractFile);
    //    var (contract, created) = await SmartContract.Create("ERC20Extended", blockchain, ownerAddress, contractCode);
    //    if (!created) Logger.Log($"Contract {contract} could not be created.");

    //    string[] inputs =
    //    [
    //        $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
    //        $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
    //        $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{PrivateKey}\");",
    //        $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{PrivateKey}\");",
    //        "Logger.Log(\"[ERC20Extended] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
    //        "Logger.LogLine(\"End ERC20Extended Demo 1\");"
    //    ];

    //    var result = await ExecuteSmartContract(blockchain, contract, inputs);

    //    //Save and Reload Test
    //    //var tmpFile = Path.GetTempFileName();
    //    //blockchain.Save(tmpFile);
    //    //blockchain = Blockchain.Load(tmpFile);

    //    inputs =
    //    [
    //        $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
    //        $"token.RegisterUser(\"{ownerAddress}\", \"{PrivateKey}\");",
    //        $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
    //        $"token.Burn(5000000000, \"{ownerAddress}\", \"{PrivateKey}\");",
    //        $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
    //        $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
    //        $"Logger.Log(\"[ERC20Extended] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
    //        "Logger.Log(\"[ERC20Extended] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
    //        "Logger.LogLine(\"End ERC20Extended Demo 2\");"
    //    ];

    //    result = await ExecuteSmartContract(blockchain, contract, inputs);

    //    inputs =
    //    [
    //        $"var token = new ERC20Extended(\"ERC20Extended\", \"SXE\", 18, 10000000000, \"{ownerAddress}\");",
    //        $"token.RegisterUser(\"{ownerAddress}\", \"WRONGKEY\");",
    //        $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{PrivateKey}\");",
    //        "Logger.LogLine(\"End ERC20Extended Failure\");"
    //    ];

    //    result = await ExecuteSmartContract(blockchain, contract, inputs);
    //}

    //private static async Task GoldCoinExample(string minerAddress, List<string> wallet1Addresses,
    //    List<string> wallet2Addresses, Blockchain? blockchain)
    //{
    //    // Deploy and interact with a GoldCoin token contract
    //    var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "GoldCoin.cs");
    //    var contractCode = File.ReadAllText(contractFile);
    //    var (contract, created) = await SmartContract.Create("GoldCoin", blockchain, minerAddress, contractCode);
    //    if (!created) Logger.Log($"Contract {contract} could not be created");

    //    string[] inputs =
    //    [
    //        $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
    //        "Logger.Log(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
    //        "Logger.LogLine(\"End GoldCoin Demo 1\");"
    //    ];

    //    var result = await ExecuteSmartContract(blockchain, contract, inputs);

    //    string[] transferInputs =
    //    [
    //        $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
    //        $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
    //        $"token.Transfer(\"{minerAddress}\", \"{wallet1Addresses[1]}\", 50000, \"{PrivateKey}\");",
    //        $"token.Transfer(\"{wallet1Addresses[1]}\", \"{wallet1Addresses[2]}\", 25000, \"{PrivateKey}\");",
    //        "Logger.LogLine(\"End GoldCoin Demo 2\");"
    //    ];

    //    result = await ExecuteSmartContract(blockchain, contract, transferInputs);

    //    string[] approvalInputs =
    //    [
    //        $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
    //        $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
    //        $"token.Approve(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\", 20000, \"{PrivateKey}\");",
    //        $"var allowance = token.Allowance(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\");",
    //        "Logger.Log($\"[GoldCoin] Allowance: {allowance}\");",
    //        "Logger.LogLine(\"End GoldCoin Demo 3\");"
    //    ];

    //    result = await ExecuteSmartContract(blockchain, contract, approvalInputs);

    //    string[] transferFromInputs =
    //    [
    //        $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
    //        $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
    //        $"token.TransferFrom(\"{wallet2Addresses[0]}\", \"{wallet1Addresses[1]}\", \"{wallet1Addresses[3]}\", 15000, \"{PrivateKey}\");",
    //        "Logger.Log(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));",
    //        "Logger.LogLine(\"End GoldCoin Demo 4\");"
    //    ];

    //    result = await ExecuteSmartContract(blockchain, contract, transferFromInputs);

    //    // RPC DEMO
    //    var notifyMint = "https://www.netregservice.com/smartx/rpc_collector.php";
    //    var notifyBurn = "https://www.netregservice.com/smartx/rpc_collector.php";

    //    string[] rpcHandlerCode =
    //    {
    //        $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
    //        "",
    //        "// Register User",
    //        $"token.RegisterUser(\"{minerAddress}\", \"{PrivateKey}\");",
    //        "",
    //        "// Register RPC handlers",
    //        $"token.RegisterHandler(\"Mint\", \"{notifyMint}\", \"{minerAddress}\");",
    //        $"token.RegisterHandler(\"Burn\", \"{notifyBurn}\", \"{minerAddress}\");",
    //        "",
    //        "// Subscribe to token events",
    //        "token.OnMint += async (address, amount) => await token.TriggerHandlers(\"Mint\", $\"{{\\\"address\\\":\\\"{address}\\\",\\\"amount\\\":{amount}}}\");",
    //        "token.OnBurn += async (address, amount) => await token.TriggerHandlers(\"Burn\", $\"{{\\\"address\\\":\\\"{address}\\\",\\\"amount\\\":{amount}}}\");",
    //        "",
    //        "// Trigger events",
    //        $"token.Mint(1000, \"{minerAddress}\", \"{minerAddress}\", \"{PrivateKey}\");",
    //        $"token.Burn(500, \"{minerAddress}\", \"{PrivateKey}\");"
    //    };

    //    result = await ExecuteSmartContract(blockchain, contract, rpcHandlerCode);


    //    Logger.Log("\nGoldCoin Smart Contract finished");
    //}

    //private static async Task<(string result, string updatedSerializedState)> ExecuteSmartContract(
    //    Blockchain? blockchain, SmartContract contract, string[] inputs, bool debug = false)
    //{
    //    // Execute the given smart contract and handle exceptions
    //    (string result, string updatedSerializedState) executionResult = (null, null);

    //    try
    //    {
    //        if (blockchain != null) executionResult = await blockchain.ExecuteSmartContract(contract.Name, inputs);

    //        if (debug)
    //        {
    //            Logger.Log("Smart Contract Execution Completed.");
    //            Logger.Log("Execution Result:");
    //            Logger.Log($"Result: {executionResult.result}");
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        if (!string.IsNullOrEmpty(executionResult.result) ||
    //            !string.IsNullOrEmpty(executionResult.updatedSerializedState))
    //            Logger.LogException(ex, $"{executionResult.result}");
    //        else
    //            Logger.LogException(ex);
    //    }

    //    return executionResult;
    //}

    //#endregion
}