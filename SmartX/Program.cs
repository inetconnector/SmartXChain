using System.Text;
using BlockchainProject.Utils;
using BlockchainProject.Validators;
internal class Program
{ 
    private static async Task Main(string[] args)
    {
        Console.Write("Enter your smartXchain address or press enter to get one: ");
        var minerAddress = ""; //Console.ReadLine();
        if (string.IsNullOrEmpty(minerAddress))
        {
            var newWallet = TrustWalletCompatibleWallet.GenerateWallet();
            minerAddress = newWallet.Item1.First();
        }
        else
        {
            if (!NetworkUtils.ValidateAddress(minerAddress))
            {
                Console.WriteLine("Invalid miner address. Exiting.");
                return;
            }
        }

        var wallet1Addresses = TrustWalletCompatibleWallet.LoadWalletAdresses();

        var wallet2Addresses = TrustWalletCompatibleWallet.LoadWalletAdresses();

        var node = await Node.Start();
        //var node = await Node.Start(true); //Registration server running on localhost

        var consensus = new SnowmanConsensus(10, node);


        //show menu
        Console.WriteLine(
            "\nEnter mode; \n1 for coin class tester \n2 for Demo\n3 for Miner\n");

        //var mode = Console.ReadLine();
        var mode = "3";

        if (mode == "1")
        {
            //var token1 = new GoldCoin("GoldXToken", "GLX", 18, 1000000, minerAddress);
             
            // Coin class tester
            var token = new ERC20Token("SmartXchain", "SXC", 18, 1000000, minerAddress);
            token.Transfer(minerAddress, wallet1Addresses[1], 100);
            token.Transfer(wallet1Addresses[1], wallet1Addresses[2], 50);

            // Serialisierung in Base64
            var serializedData = Serializer.SerializeToBase64(token);


            // Deserialisierung aus Base64
            var deserializedToken = Serializer.DeserializeFromBase64<ERC20Token>(serializedData);
            deserializedToken.Transfer(wallet1Addresses[2], wallet1Addresses[3], 25);

            // Ausgabe der deserialisierten Daten
            Console.WriteLine("\nDeserialized Token:");
            Console.WriteLine($"Name: {deserializedToken.Name}");
            Console.WriteLine($"Symbol: {deserializedToken.Symbol}");
            Console.WriteLine($"Decimals: {deserializedToken.Decimals}");
            Console.WriteLine($"Total Supply: {deserializedToken.TotalSupply}");

            // Beispiel für Zugriff auf Balances und Allowances
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
        else if (mode == "2")
        {
            //create blockchain
            var blockchain = new Blockchain(2, 5, minerAddress, consensus);

            //publish serverip
            var publicIP = await NetworkUtils.GetPublicIPAsync();
            if (publicIP == "") publicIP = NetworkUtils.GetLocalIP();
            var nodeTransaction = new Transaction
            {
                Sender = "System",
                Recipient = "System",
                Amount = 0, // No monetary value, just storing state
                Data = Convert.ToBase64String(Encoding.ASCII.GetBytes(publicIP)), // Store  data as Base64 string
                Timestamp = DateTime.UtcNow
            };

            blockchain.AddTransaction(nodeTransaction);

            await ERC20Example(minerAddress, wallet1Addresses, consensus, blockchain);

            await GoldCoinExample(minerAddress, wallet1Addresses, wallet2Addresses, consensus, blockchain);

            // Display Blockchain state 
            Console.WriteLine("Blockchain State:");
            foreach (var block in blockchain.Chain)
                Console.WriteLine($"Block {blockchain.Chain.IndexOf(block)}: {block.Hash}");
        }
        else if (mode == "3")
        {
            //var blockchain = new Blockchain(2, 5, minerAddress, consensus);
            //var miner = new Miner(blockchain, minerAddress);

            //Console.WriteLine("Enter 'start' to start mining or 'stop' to stop mining.");
            //string command;

            //do
            //{
            //    command = Console.ReadLine()?.ToLower();

            //    if (command == "start")
            //    {
            //        miner.StartMining();
            //    }
            //    else if (command == "stop")
            //    {
            //        miner.StopMining();
            //        break;
            //    }
            //} while (command != "exit");

            //Console.WriteLine("Exiting miner mode.");
             
            var blockchainServer = await BlockchainServer.StartServerAsync();
             
            //networkManager.StartMaintenanceTasks();
            //networkManager.ConnectToPeerServers();

            //Console.WriteLine("Press Enter to discover new peers...");
            //Console.ReadLine();
            //networkManager.DiscoverPeers();

            //Console.WriteLine("Press Enter to push the chain to a peer...");
            //Console.ReadLine();
            //networkManager.PushChain("tcp://peer_address:port", "Mocked Blockchain Data");

            //Console.WriteLine("Press Enter to pull the chain from a peer...");
            //Console.ReadLine();
            //var chainData = networkManager.PullChain("tcp://peer_address:port");
            //Console.WriteLine($"Pulled chain: {chainData}");

            //Console.WriteLine("Press Enter to exit...");
            //Console.ReadLine();
        }
        else
        {
            Console.WriteLine("Invalid mode.");
        }

        // Prevent program from exiting immediately
        Console.WriteLine("Press Enter to exit...");
        Console.ReadLine();
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

    private static async Task ERC20Example(string minerAddress, List<string> walletAddresses,
        SnowmanConsensus consensus,
        Blockchain blockchain)
    {
        // Add the smart contract \Examples\ERC20.cs to the blockchain
        var contractFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Examples", "ERC20.cs");
        var contractCode = File.ReadAllText(contractFile);
        var contract = await SmartContract.Create("SmartXchain", blockchain, minerAddress, contractCode);

        // Mint and execute the smart contract
        string[] inputs =
        [
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 1000000, \"{minerAddress}\");",
            $"token.Transfer(\"{minerAddress}\", \"{walletAddresses[1]}\", 100);",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 50);",
            "Console.WriteLine(\"[ERC20Token] \" + JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));"
        ];

        var result = await ExecuteSmartContract(blockchain, contract, inputs);

        //Console.WriteLine($"Smartcontract result: {result.result}");
        //Console.WriteLine($"Smartcontract state: {result.updatedSerializedState}");
        //await blockchain.MinePendingTransactions(minerAddress);

        //save and reload the chain
        var tmpFile = Path.GetTempFileName();
        blockchain.Save(tmpFile);
        blockchain = Blockchain.Load(tmpFile, consensus);

        //Execute the contract again and display balances
        inputs =
        [
            $"var token = new ERC20Token(\"SmartXchain\", \"SXC\", 18, 1000000, \"{minerAddress}\");",
            $"token.Transfer(\"{walletAddresses[1]}\", \"{walletAddresses[2]}\", 25);",
            "Console.WriteLine($\"\"[ERC20Token] \" + Json of {token.Name}\");",
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
            var deserializedState = Serializer.DeserializeFromBase64<ERC20Token>(stateBase64);
            if (debug)
                foreach (var balance in deserializedState.GetBalances)
                    Console.WriteLine($"balance: {balance.Key}: {balance.Value}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"error: {result}");
        }

        return result;
    }
}