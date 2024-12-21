using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BlockchainProject.Utils;
using BlockchainProject.Validators;
using SmartXChain.Utils;

public class Blockchain
{
    [JsonInclude] private readonly SnowmanConsensus _consensus;

    [JsonInclude] private readonly Dictionary<string, string> _contractStates;

    [JsonInclude] private readonly int _difficulty;
    private readonly object _pendingTransactionsLock = new();

    [JsonInclude] private readonly decimal _reward;

    [JsonInclude] private readonly decimal _reward_validator;

    [JsonInclude] private readonly List<string> _validators;


    /// <summary>
    ///     Start blockchain and sign blocks with privateKey and receive reward at miner address
    /// </summary>
    /// <param name="difficulty"></param>
    /// <param name="reward"></param>
    /// <param name="minerAdress"></param>
    /// <param name="privateKey"></param>
    public Blockchain(int difficulty, decimal reward, string minerAdress, SnowmanConsensus consensus)
    {
        Chain = new List<Block>();
        PendingTransactions = new List<Transaction>();
        SmartContracts = new List<SmartContract>();
        _contractStates = new Dictionary<string, string>();
        _difficulty = difficulty;
        _reward = reward;

        MinerAdress = minerAdress;
        _consensus = consensus;

        AddBlock(CreateGenesisBlock());
    }

    [JsonInclude] public string MinerAdress { get; }

    [JsonInclude] public List<Block> Chain { get; private set; }

    [JsonInclude] public List<Transaction> PendingTransactions { get; private set; }

    [JsonInclude] public List<SmartContract> SmartContracts { get; private set; }

    private Block CreateGenesisBlock()
    {
        return new Block(DateTime.UtcNow, new List<Transaction>(), "0");
    }

    public bool AddBlock(Block block)
    {
        if (Chain.Count > 0) block.PreviousHash = Chain.Last().Hash;
        block.Mine(_difficulty);
        Chain.Add(block);
        return true;
    }

    private void PayGas(string type, string payer, int gas)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[Gas] {payer} has to pay {gas} for {type}");
        Console.ResetColor();
    }

    public bool AddTransaction(Transaction transaction)
    {
        transaction.SignTransaction(Crypt.Default.PrivateKey);

        var payer = "System";
        var gas = transaction.Gas;
        if (transaction.Sender == payer)
        {
            if (transaction.Recipient != payer)
                payer = transaction.Recipient;
            else
                gas = 0;
        }
        else
        {
            payer = transaction.Sender;
        }

        if (gas > 0)
            PayGas("Transaction", payer, gas);

        PendingTransactions.Add(transaction);
        return true;
    }

    public async Task<bool> AddSmartContract(SmartContract contract)
    {
        if (!Regex.IsMatch(contract.Name, @"^[a-zA-Z0-9]+$"))
        {
            Console.WriteLine(
                $"Invalid Smart Contract name '{contract.Name}'. Names can only contain alphanumeric characters (a-z, A-Z, 0-9) without spaces.");
            return false;
        }

        // Check if the contract with the same name already exists
        if (SmartContracts.Exists(sc => sc.Name == contract.Name))
        {
            Console.WriteLine($"A Smart Contract with the name '{contract.Name}' already exists in the blockchain.");
            return false;
        }

        if (await ReachCodeConsensus(contract))
        {
            SmartContracts.Add(contract);

            // Initialize serialized state for the contract
            if (!_contractStates.ContainsKey(contract.Name))
                _contractStates[contract.Name] = Serializer.SerializeToBase64(contract.Name);

            Console.WriteLine($"Smart Contract '{contract.Name}' added to the blockchain.");
            return true;
        }

        Console.WriteLine($"No consensus reached for the Smart Contract '{contract.Name}'");
        return false;
    }

    public async Task<(string result, string updatedSerializedState)> ExecuteSmartContract(string contractName,
        string[] inputs)
    {
        var contract = SmartContracts.FirstOrDefault(c => c.Name == contractName);
        if (contract == null) throw new Exception($"Smart Contract '{contractName}' not found in the blockchain.");

        // Load serialized state from the blockchain
        var currentSerializedState = await GetContractState(contract);

        try
        {
            Console.WriteLine($"Executing Smart Contract '{contractName}'...");

            PayGas("Contract " + contract.Name, contract.Owner, contract.Gas);

            var (result, updatedSerializedState) = await contract.Execute(ModifyInput(inputs), currentSerializedState);

            // Serialize updated state back to the blockchain
            if (result == "Ok")
                await WriteContractStateToBlockchain(contract, updatedSerializedState);

            Console.WriteLine($"Smart Contract {contractName} Result: {result}");
            return (result, updatedSerializedState);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing Smart Contract '{contractName}': {ex.Message}");
            return ("Execution failed", currentSerializedState);
        }
    }

    private string[] ModifyInput(string[] inputs)
    {
        // Extract the first line from the input array
        var firstLine = inputs.FirstOrDefault();
        if (string.IsNullOrEmpty(firstLine))
            throw new ArgumentException("Input array must not be empty.");

        // Assumption: The class name follows "var token = new " and ends before the first parenthesis "("
        var className = ExtractClassName(firstLine);

        // Generate dynamic lines based on the extracted class name
        var secondLine =
            $"if(!string.IsNullOrEmpty(CurrentState)) token = Serializer.DeserializeFromBase64<{className}>(CurrentState);";
        var lastLine = "Output = Serializer.SerializeToBase64(token);";

        // Modify the input array:
        // 1. Start with the first line of the original input
        // 2. Insert the dynamically generated second line
        // 3. Add all remaining lines from the input (excluding the first)
        // 4. Append the dynamically generated last line
        return new[] { inputs[0], secondLine }
            .Concat(inputs.Skip(1)) // Include all lines except the first
            .Concat(new[] { lastLine }) // Add the final output line
            .ToArray();
    }

    private string ExtractClassName(string line)
    {
        const string tokenStart = "new ";
        var startIndex = line.IndexOf(tokenStart) + tokenStart.Length;
        var endIndex = line.IndexOf('(', startIndex);
        if (startIndex < 0 || endIndex < 0)
            throw new ArgumentException("Invalid format of the constructor line.");

        return line.Substring(startIndex, endIndex - startIndex);
    }

    private async Task WriteContractStateToBlockchain(SmartContract contract, string serializedState)
    {
        // Compress the serialized state
        var compressedState = Compress.CompressString(serializedState);

        // Create a transaction with the compressed state
        var stateTransaction = new Transaction
        {
            Sender = contract.Owner,
            Recipient = "System",
            Amount = 0, // No monetary value, just storing state
            Data = Convert.ToBase64String(compressedState), // Store compressed data as Base64 string
            Timestamp = DateTime.UtcNow,
            Info = "$" + contract.Name
        };

        // Add the transaction to pending transactions
        AddTransaction(stateTransaction);
        await MinePendingTransactions(MinerAdress, false);
        Console.WriteLine($"State for contract '{contract.Name}' added to pending transactions.");
    }

    public async Task<string> GetContractState(SmartContract contract)
    {
        for (var i = Chain.Count - 1; i >= 0; i--)
            foreach (var transaction in Chain[i].Transactions)
                if (transaction.Recipient == "System" && transaction.Info == "$" + contract.Name &&
                    !string.IsNullOrEmpty(transaction.Data))
                {
                    // Decompress the data
                    var compressedState = Convert.FromBase64String(transaction.Data);
                    return Compress.DecompressString(compressedState);
                }

        await MinePendingTransactions(MinerAdress, false);

        Console.WriteLine(
            $"No state found for contract '{contract.Name}', initializing new state. Adding contract state to blockchain");
        var newState = ""; // Initialize with an empty serialized state
        await WriteContractStateToBlockchain(contract, newState);
        return newState;
    }

    public async Task MinePendingTransactions(string minerAddress, bool reward = true)
    {
        while (Node.CurrentNodeIPs.Count == 0) Thread.Sleep(10);

        List<Transaction> transactionsToMine;

        lock (_pendingTransactionsLock)
        {
            if (PendingTransactions.Count == 0)
            {
                Console.WriteLine("No transactions to mine.");
                return;
            }

            transactionsToMine = PendingTransactions.ToList();
            PendingTransactions.Clear();
        }

        var block = new Block(DateTime.Now, transactionsToMine, Chain.Last().Hash);

        var consensusReached = await _consensus.ReachConsensus(block);
        if (consensusReached)
        {
            if (ValidateChain())
            {
                Chain.Add(block);
                Console.WriteLine("Block added to chain successfully: " + block.Hash);
            }

            if (reward)
            {
                var rewardTransaction = new Transaction
                {
                    Sender = "System",
                    Recipient = minerAddress,
                    Amount = _reward,
                    Timestamp = DateTime.UtcNow
                };

                AddTransaction(rewardTransaction);
                Console.WriteLine($"Miner reward: reward to {minerAddress}");

                foreach (var address in _validators)
                {
                    if (address == minerAddress)
                        continue;

                    rewardTransaction = new Transaction
                    {
                        Sender = "System",
                        Recipient = address,
                        Amount = _reward,
                        Timestamp = DateTime.UtcNow
                    };

                    AddTransaction(rewardTransaction);
                    Console.WriteLine($"Validator reward: Reward to {address}");
                }
            }
        }
        else
        {
            Console.WriteLine("Block rejected");
        }
    }


    public async Task<bool> ReachCodeConsensus(SmartContract contract)
    {
        try
        {
            Console.WriteLine($"Starting Snowman-Consensus for contract: {contract.Name}");

            var requiredVotes = Node.CurrentNodeIPs.Count / 2 + 1;

            var voteTasks = new List<Task<bool>>();

            foreach (var validator in Node.CurrentNodeIPs)
                voteTasks.Add(SendCodeForVerificationAsync(validator, contract));

            var results = await Task.WhenAll(voteTasks);

            var positiveVotes = results.Count(result => result);

            Console.WriteLine(positiveVotes >= requiredVotes
                ? $"Consensus reached for {contract.Name}. Code is safe."
                : $"No consensus reached for contract {contract.Name}. Code discarded.");

            return positiveVotes >= requiredVotes;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during consensus: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendCodeForVerificationAsync(string serverAddress, SmartContract contract)
    {
        try
        {
            //var compressedData = Compress.CompressString(contract.ContractCode);
            //var compressedBase64Data = Convert.ToBase64String(compressedData);

            var message = $"VerifyCode:{contract.SerializedContractCode}";
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync(message);

            Console.WriteLine($"Code {contract.Name} sent to {serverAddress} for verification.");
            Console.WriteLine($"Response from server for code {contract.Name}: {response}");

            return response == "OK";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending code {contract.Name} to {serverAddress}: {ex.Message}");
        }

        return false;
    }

    public Block GetLatestBlock()
    {
        lock (Chain)
        {
            return Chain.Last();
        }
    }

    public bool ValidateChain()
    {
        for (var i = 1; i < Chain.Count; i++)
        {
            var current = Chain[i];
            var previous = Chain[i - 1];

            if (current.Hash != current.CalculateHash())
                return false;

            if (current.PreviousHash != previous.Hash)
                return false;
        }

        return true;
    }


    public void Save(string file)
    {
        try
        {
            // serialize the entire blockchain into JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true // serialize private fields also
            };
            var jsonString = JsonSerializer.Serialize(this, options);
            var compressedData = Compress.CompressString(jsonString);

            // Save json in file
            File.WriteAllBytes(file, compressedData);
            Console.WriteLine($"Blockchain saved to: {file}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving the blockchain: {ex.Message}");
            throw;
        }
    }

    public static Blockchain Load(string tmpFile, SnowmanConsensus consensus)
    {
        try
        {
            var compressedData = File.ReadAllBytes(tmpFile);
            var jsonString = Compress.DecompressString(compressedData);

            var jsonDocument = JsonDocument.Parse(jsonString);
            var root = jsonDocument.RootElement;

            // manual initialization
            var difficulty = root.GetProperty("_difficulty").GetInt32();
            var reward = root.GetProperty("_reward").GetDecimal();
            var minerAddress = root.GetProperty("MinerAdress").GetString();

            var blockchain = new Blockchain(difficulty, reward, minerAddress, consensus);

            // Additional data (Chain, PendingTransactions etc.)
            blockchain.Chain = JsonSerializer.Deserialize<List<Block>>(root.GetProperty("Chain").GetRawText());
            blockchain.PendingTransactions =
                JsonSerializer.Deserialize<List<Transaction>>(root.GetProperty("PendingTransactions").GetRawText());
            blockchain.SmartContracts =
                JsonSerializer.Deserialize<List<SmartContract>>(root.GetProperty("SmartContracts").GetRawText());

            Console.WriteLine("Blockchain loaded successfully.");
            return blockchain;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading the blockchain: {ex.Message}");
            throw;
        }
    }

    public bool IsValid()
    {
        for (var i = 1; i < Chain.Count; i++)
        {
            var currentBlock = Chain[i];
            var previousBlock = Chain[i - 1];

            if (currentBlock.PreviousHash != previousBlock.Hash || currentBlock.Hash != currentBlock.CalculateHash())
                return false;
        }

        return true;
    }
}