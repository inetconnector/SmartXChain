using SmartXChain;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Server;
using SmartXChain.Utils;
using SmartXChain.Validators;

namespace SmartX;

internal class Program
{
    private static async Task Main(string[] args)
    {
        await NetworkUtils.GetPublicIPAsync();

        if (string.IsNullOrEmpty(Config.Default.MinerAddress))
        {
            SmartXWallet.GenerateWallet();
            Config.Default.ReloadConfig();
        }

        var result = await BlockchainServer.StartServerAsync();
        var blockchainServer = result.Item1;
        var node = result.Item2;

        //show menu
        Console.WriteLine(
            "\nEnter mode; \n1 Coin class tester \n2 Demo\n3 Exit\n");
        while (true)
        {
            var mode = Console.ReadKey().KeyChar;
            if (mode == '1')
            {
                var wallet1Addresses = SmartXWallet.LoadWalletAdresses();

                // Coin class tester
                //var token1 = new GoldCoin("GoldXToken", "GLX", 18, 1000000, minerAddress);
                var token = new ERC20Token("SmartXchain", "SXC", 18, 1000000, wallet1Addresses[0]);
                token.Transfer(wallet1Addresses[0], wallet1Addresses[1], 100);
                token.Transfer(wallet1Addresses[1], wallet1Addresses[2], 50);

                // Serialization to Base64
                var serializedData = Serializer.SerializeToBase64(token);

                // Deserialization from Base64
                var deserializedToken = Serializer.DeserializeFromBase64<ERC20Token>(serializedData);
               
                // Transfer after serialization
                deserializedToken.Transfer(wallet1Addresses[2], wallet1Addresses[3], 25);
                 
                Console.WriteLine("\nDeserialized Token:");
                Console.WriteLine($"Name: {deserializedToken.Name}");
                Console.WriteLine($"Symbol: {deserializedToken.Symbol}");
                Console.WriteLine($"Decimals: {deserializedToken.Decimals}");
                Console.WriteLine($"Total Supply: {deserializedToken.TotalSupply}");
                 
                Console.WriteLine("\nBalances:");
                foreach (var balance in deserializedToken.GetBalances)
                    Console.WriteLine($"{balance.Key}: {balance.Value}");

                Console.WriteLine("\nAllowances:");
                foreach (var allowance in deserializedToken.GetAllowances)
                {
                    Console.WriteLine($"{allowance.Key}:");
                    foreach (var spender in allowance.Value) Console.WriteLine($"  {spender.Key}: {spender.Value}");
                }
            }
            else if (mode == '2')
            {
                var wallet1Addresses = SmartXWallet.LoadWalletAdresses();
                var wallet2Addresses = SmartXWallet.LoadWalletAdresses();

                await ERC20Example(wallet1Addresses[0], wallet1Addresses, node.Consensus, node.Blockchain);

                await GoldCoinExample(wallet1Addresses[0], wallet1Addresses, wallet2Addresses, node.Consensus,
                    node.Blockchain);

                // Display Blockchain state 
                Console.WriteLine("Blockchain State:");
                var chain = node.Blockchain.Chain;
                foreach (var block in chain)
                    Console.WriteLine($"Block {chain.IndexOf(block)}: {block.Hash}");
            }
            else if (mode == '3')
            {
                break;
            }
            else
            {
                Console.WriteLine("\nInvalid mode.");
            }
        }
    }

    private static async Task ERC20Example(string ownerAddress, List<string> walletAddresses,
        SnowmanConsensus consensus,
        Blockchain blockchain)
    {
        // Add the smart contract \Examples\ERC20.cs to the blockchain
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
        var contractCode = File.ReadAllText(contractFile);
        var contract = await SmartContract.Create("SmartXchain", blockchain, ownerAddress, contractCode);

        // Mint and execute the smart contract
        string[] inputs =
        [
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 1000000, \"{ownerAddress}\");",
            $"token.Transfer(\"{ownerAddress}\", \"{walletAddresses[1]}\", 100);",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50);",
            "Console.WriteLine(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        ];

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        //Console.WriteLine($"Smartcontract result: {result.result}");
        //Console.WriteLine($"Smartcontract state: {result.updatedSerializedState}");
        //await blockchain.MinePendingTransactions(ownerAddress);

        //save and reload the chain
        var tmpFile = Path.GetTempFileName();
        blockchain.Save(tmpFile);
        blockchain = Blockchain.Load(tmpFile, consensus);

        //Execute the contract again and display balances
        inputs =
        [
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 1000000, \"{ownerAddress}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25);",
            "//token.GetBalances[\"hacker\"]=1000;",
            "//token.Balances[\"hacker\"]=1000;",
            "//File.WriteAllText(\"hacker.txt\", \"example.txt\");",
            "//Shell.Execute(\"cmd.exe\")",
            $"Console.WriteLine(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[0]}\"]);",
            $"Console.WriteLine(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[1]}\"]);",
            $"Console.WriteLine(\"[ERC20Token] \" + token.GetBalances[\"{walletAddresses[2]}\"]);",
            "Console.WriteLine(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        ];

        result = await ExecuteSmartContract(blockchain, contract, inputs);
    }

    private static async Task GoldCoinExample(string minerAddress,
        List<string> wallet1Addresses,
        List<string> wallet2Addresses,
        SnowmanConsensus consensus,
        Blockchain blockchain)
    {
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "GoldCoin.cs");
        var contractCode = File.ReadAllText(contractFile);
        var contract = await SmartContract.Create("GoldCoin", blockchain, minerAddress, contractCode);

        // Create the token
        string[] inputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",
            "Console.WriteLine(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        ];
        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        // Transfer examples
        string[] transferInputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",

            $"token.Transfer(\"{minerAddress}\", \"{wallet1Addresses[1]}\", 50000);",
            $"token.Transfer(\"{wallet1Addresses[1]}\", \"{wallet1Addresses[2]}\", 25000);"
        ];
        result = await ExecuteSmartContract(blockchain, contract, transferInputs);

        // Approve and Allowance examples
        string[] approvalInputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",

            $"token.Approve(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\", 20000);",
            $"var allowance = token.Allowance(\"{wallet1Addresses[1]}\", \"{wallet2Addresses[0]}\");",
            "Console.WriteLine($\"[GoldCoin] Allowance: {allowance}\");"
        ];
        result = await ExecuteSmartContract(blockchain, contract, approvalInputs);

        // TransferFrom example
        string[] transferFromInputs =
        [
            $"var token = new GoldCoin(\"GoldCoin\", \"GLD\", 18, 1000000, \"{minerAddress}\");",

            $"token.TransferFrom(\"{wallet2Addresses[0]}\", \"{wallet1Addresses[1]}\", \"{wallet1Addresses[3]}\", 15000);",
            "Console.WriteLine(\"[GoldCoin] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        ];
        result = await ExecuteSmartContract(blockchain, contract, transferFromInputs);

        Console.WriteLine("\nGoldCoin Smart Contract finished");
    }

    private static async Task<(string result, string updatedSerializedState)> ExecuteSmartContract(
        Blockchain blockchain, SmartContract contract, string[] inputs, bool debug = false)
    {
        var result = await blockchain.ExecuteSmartContract(contract.Name, inputs);
        try
        {
            // Show Balances (assuming balances are part of the contract's output)
            if (debug)
            {
                Console.WriteLine("Smart Contract Execution Completed.");
                Console.WriteLine("Result:");
                Console.WriteLine(result);
            }

            // Deserialize the contract state and extract balances
            var stateBase64 = result.updatedSerializedState;

            if (contract.Name == "SmartXchain")
            {
                var deserializedState = Serializer.DeserializeFromBase64<ERC20Token>(stateBase64);
                if (debug)
                    foreach (var balance in deserializedState.GetBalances)
                        Console.WriteLine($"balance: {balance.Key}: {balance.Value}");
            }
            else if (contract.Name == "GoldCoin")
            {
                var deserializedState = Serializer.DeserializeFromBase64<GoldCoin>(stateBase64);
                if (debug)
                    foreach (var balance in deserializedState.GetBalances)
                        Console.WriteLine($"balance: {balance.Key}: {balance.Value}");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"error: {result} {e.Message}");
        }

        return result;
    }
}