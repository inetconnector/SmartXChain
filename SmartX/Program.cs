using SmartXChain;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Server;
using SmartXChain.Utils;

namespace SmartX;

internal class Program
{
    private static async Task Main(string[] args)
    {
        // Initialize application and start the blockchain server
        await InitializeApplicationAsync();

        var (blockchainServer, startup) = await BlockchainServer.StartServerAsync();
         
        await RunConsoleMenuAsync(startup);
    }

    private static async Task InitializeApplicationAsync()
    {
        // Retrieve public IP and ensure wallet and keys are set up
        await NetworkUtils.GetPublicIPAsync();

        if (string.IsNullOrEmpty(Config.Default.MinerAddress))
        {
            SmartXWallet.GenerateWallet();
            Config.Default.ReloadConfig();
        }

        if (string.IsNullOrEmpty(Config.Default.PublicKey))
            Config.Default.GenerateServerKeys();
    }

    private static async Task RunConsoleMenuAsync(BlockchainServer.NodeStartupResult? startup)
    {
        // Display and process console menu 

        while (true)
        {
            DisplayMenu();

            var mode = Console.ReadKey().KeyChar;
            Logger.LogMessage();

            switch (mode)
            {
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
                    return;
            }
        }
    }

    private static void DisplayMenu()
    {
        // Show available menu options
        Logger.LogMessage("\nEnter mode:");
        Logger.LogMessage("1: Coin class tester");
        Logger.LogMessage("2: SmartContract Demo");
        Logger.LogMessage("3: Blockchain state");
        Logger.LogMessage("4: Wallet Balances");
        Logger.LogMessage("5: Chain Info");
        Logger.LogMessage("6: Show Contracts");
        Logger.LogMessage("7: Exit");
        Logger.LogMessage();
    }

    private static async Task RunCoinClassTesterAsync()
    {
        // Test ERC20 coin functionalities
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        var seed = File.ReadAllText("seed.txt");

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
        Logger.LogMessage("\nDeserialized Token:");
        Logger.LogMessage($"Name: {token.Name}");
        Logger.LogMessage($"Symbol: {token.Symbol}");
        Logger.LogMessage($"Decimals: {token.Decimals}");
        Logger.LogMessage($"Total Supply: {token.TotalSupply}");

        Logger.LogMessage("\nBalances:");
        foreach (var balance in token.GetBalances)
            Logger.LogMessage($"{balance.Key}: {balance.Value}");

        Logger.LogMessage("\nAllowances:");
        foreach (var allowance in token.GetAllowances)
        {
            Logger.LogMessage($"{allowance.Key}:");
            foreach (var spender in allowance.Value)
                Logger.LogMessage($"  {spender.Key}: {spender.Value}");
        }
    }

    private static async Task RunSmartContractDemoAsync(BlockchainServer.NodeStartupResult? node)
    {
        // Demonstrate ERC20 and GoldCoin smart contracts
        var walletAddresses = SmartXWallet.LoadWalletAdresses();
        await ERC20Example(walletAddresses[0], walletAddresses, node.Blockchain);
        await GoldCoinExample(walletAddresses[0], walletAddresses, SmartXWallet.LoadWalletAdresses(), node.Blockchain);

        PerformNativeTransfer(node.Blockchain, walletAddresses);
    }

    private static void PerformNativeTransfer(Blockchain? chain, List<string> walletAddresses)
    {
        // Perform native token transfer
        var transaction = new Transaction();
        var privateKey = File.ReadAllText("privatekey.txt");

        transaction.RegisterUser(walletAddresses[0], privateKey);
        var transferred = transaction.Transfer(
            chain,
            walletAddresses[0],
            walletAddresses[1],
            0.01d,
            privateKey,
            "native transfer",
            "49.83278, 9.88167");

        chain.MinePendingTransactions(walletAddresses[0]);
        if (transferred)
            Logger.LogMessage($"Transferred SCX from {walletAddresses[0]} to {walletAddresses[1]}");
    }

    private static void DisplayBlockchainState(BlockchainServer.NodeStartupResult? node)
    {
        // Show current state of the blockchain
        Logger.LogMessage("Blockchain State:");
        foreach (var block in node.Blockchain.Chain)
            Logger.LogMessage($"Block {node.Blockchain.Chain.IndexOf(block)}: {block.Hash}");
    }

    private static void DisplayWalletBalances(BlockchainServer.NodeStartupResult? node)
    {
        // Display balances of all wallets
        Logger.LogMessage("\nWallet Balances:");
        foreach (var (address, balance) in node.Blockchain.GetAllBalancesFromChain())
            Logger.LogMessage($"{address}: {balance}");
    }

    private static void DisplayChainInfo(BlockchainServer.NodeStartupResult? node)
    {
        // Print all blocks and transactions
        Logger.LogMessage("\nChain Info:");
        node.Blockchain.PrintAllBlocksAndTransactions();
    }

    private static void DisplayContracts(BlockchainServer.NodeStartupResult? node)
    {
        // List all deployed contracts
        Logger.LogMessage("\nContracts:");
        foreach (var contract in node.Blockchain.GetContracts())
        {
            Logger.LogMessage($"Name: {contract.Name}, Owner: {contract.Owner}, Gas: {contract.Gas}");
            Logger.LogMessage($"Code:\n{Serializer.DeserializeFromBase64<string>(contract.SerializedContractCode)}");
        }
    }

    private static async Task ERC20Example(string ownerAddress, List<string> walletAddresses, Blockchain? blockchain)
    {
        // Deploy and interact with an ERC20 token contract
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
        var contractCode = File.ReadAllText(contractFile);
        var (contract, created) = await SmartContract.Create("SmartXchain", blockchain, ownerAddress, contractCode);
        if (!created) Logger.LogMessage($"Contract {contract} could not be created");

        var seedFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.txt");
        var seed = File.ReadAllText(seedFile);

        string[] inputs =
        {
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{seed}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100, \"{seed}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50, \"{seed}\");",
            "Logger.LogMessage(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        var result = await ExecuteSmartContract(blockchain, contract, inputs);
        
        //Save and Reload Test
        var tmpFile = Path.GetTempFileName();
        blockchain.Save(tmpFile);
        blockchain = Blockchain.Load(tmpFile);

        inputs = new[]
        {
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 10000000000, \"{ownerAddress}\");",
            $"token.RegisterUser(\"{ownerAddress}\", \"{seed}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25, \"{seed}\");",
            $"Logger.LogMessage(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Logger.LogMessage(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Logger.LogMessage(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Logger.LogMessage(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
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

        var seedFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.txt");
        var seed = File.ReadAllText(seedFile);

        string[] inputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            "Logger.LogMessage(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        };

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        string[] transferInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{seed}\");",
            $"token.Transfer(\"{minerAddress}\", \"{wallet1Addresses[1]}\", 50000, \"{seed}\");",
            $"token.Transfer(\"{wallet1Addresses[1]}\", \"{wallet1Addresses[2]}\", 25000, \"{seed}\");"
        };

        result = await ExecuteSmartContract(blockchain, contract, transferInputs);

        string[] approvalInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{seed}\");",
            $"token.Approve(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\", 20000, \"{seed}\");",
            $"var allowance = token.Allowance(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\");",
            "Logger.LogMessage($\"[GoldCoin] Allowance: {allowance}\");"
        };

        result = await ExecuteSmartContract(blockchain, contract, approvalInputs);

        string[] transferFromInputs =
        {
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            $"token.RegisterUser(\"{minerAddress}\", \"{seed}\");",
            $"token.TransferFrom(\"{wallet2Addresses[0]}\", \"{wallet1Addresses[1]}\", \"{wallet1Addresses[3]}\", 15000, \"{seed}\");",
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
            executionResult = await blockchain.ExecuteSmartContract(contract.Name, inputs);

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
                Logger.LogMessage($"Error: {executionResult.result} {e.Message}");
            else
                Logger.LogMessage($"Error: {e.Message}");
        }

        return executionResult;
    }
}