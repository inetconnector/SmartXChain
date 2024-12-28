using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SmartXChain.Contracts;
using SmartXChain.Server;
using SmartXChain.Utils;
using SmartXChain.Validators;

namespace SmartXChain.BlockchainCore;

public class Blockchain
{
    public const string SystemAddress = "smartX0000000000000000000000000000000000000000";

    [JsonInclude] private readonly Dictionary<string, string> _contractStates;

    [JsonInclude] private readonly int _difficulty;
    private readonly object _pendingTransactionsLock = new();


    /// <summary>
    ///     Start blockchain and sign blocks with privateKey and receive reward at miner address
    /// </summary>
    public Blockchain(int difficulty, string minerAdress)
    {
        Chain = new List<Block>();
        PendingTransactions = new List<Transaction>();
        _contractStates = new Dictionary<string, string>();
        _difficulty = difficulty;

        MinerAdress = minerAdress;

        AddBlock(CreateGenesisBlock());
    }

    [JsonInclude] public string MinerAdress { get; }

    [JsonInclude] public List<Block> Chain { get; private set; }

    [JsonInclude] public List<Transaction> PendingTransactions { get; private set; }

    public static double CurrentNetworkLoad { get; set; } = .5d;

    public IReadOnlyDictionary<string, SmartContract> SmartContracts
    {
        get
        {
            return Chain
                .SelectMany(block => block.SmartContracts)
                .ToDictionary(contract => contract.Name, contract => contract);
        }
    }

    private Block CreateGenesisBlock()
    {
        return new Block(DateTime.UtcNow, new List<Transaction>(), "0");
    }

    public bool AddBlock(Block block, bool lockChain = true, bool mineBlock = true)
    {
        if (mineBlock)
        {
            if (lockChain)
            {
                lock (Chain)
                {
                    if (Chain.Count > 0)
                        block.PreviousHash = Chain.Last().Hash;
                    block.Mine(_difficulty);
                    Chain.Add(block);
                }
            }
            else
            {
                if (Chain.Count > 0)
                    block.PreviousHash = Chain.Last().Hash;
                block.Mine(_difficulty);
                Chain.Add(block);
            }
        }
        else
        {
            //Add only if Block previous hash is last block
            if (Chain.Count > 0 && Chain.Last().Hash == block.PreviousHash)
            {
                lock (Chain)
                {
                    Chain.Add(block);
                }
            }
            else
            {
                Logger.LogMessage("Block not added to chain -- invalid previous block");
                return false;
            }
        }

        Logger.LogMessage("Block added to chain successfully: " + block.Hash);
        return true;
    }

    private static void PayGas(string type, string payer, int gas)
    {
        if (gas > 0 && payer != SystemAddress)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Logger.LogMessage($"[Gas] {payer} has to pay {gas} for {type}");
            Console.ResetColor();
        }
    }

    public bool AddTransaction(Transaction transaction)
    {
        transaction.SignTransaction(Crypt.Default.PrivateKey);

        var payer = SystemAddress;
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
        lock (PendingTransactions)
        {
            PendingTransactions.Add(transaction);
        }

        return true;
    }

    public async Task<bool> AddSmartContract(SmartContract contract)
    {
        if (!Regex.IsMatch(contract.Name, @"^[a-zA-Z0-9]+$"))
        {
            Logger.LogMessage(
                $"Invalid Smart Contract name '{contract.Name}'. Names can only contain alphanumeric characters (a-z, A-Z, 0-9) without spaces.");
            return false;
        }

        // Check if the contract with the same name already exists
        if (Chain.Any(b => b.SmartContracts.Any(sc => sc.Name == contract.Name)))
        {
            Logger.LogMessage($"A Smart Contract with the name '{contract.Name}' already exists in the blockchain.");
            return false;
        }

        if (await ReachCodeConsensus(contract))
        {
            var latestBlock = GetLatestBlock();
            latestBlock.SmartContracts.Add(contract);

            // Initialize serialized state for the contract
            if (!_contractStates.ContainsKey(contract.Name))
                _contractStates[contract.Name] = Serializer.SerializeToBase64(contract.Name);

            Logger.LogMessage($"Smart Contract '{contract.Name}' added to the blockchain.");
            return true;
        }

        Logger.LogMessage($"No consensus reached for the Smart Contract '{contract.Name}'");
        return false;
    }

    public async Task<(bool, List<string>)> ReachConsensus(Block block)
    {
        Logger.LogMessage($"Starting Snowman-Consensus for block: {block.Hash}");

        var selectionSize = Math.Max(1, Node.CurrentNodeIPs.Count / 2); // min 50% of the nodes
        var selectedValidators = Node.CurrentNodeIPs
            .OrderBy(_ => Guid.NewGuid()) // random order
            .Take(selectionSize) // take first N Nodes
            .ToList();

        Logger.LogMessage($"Selected {selectionSize} validators from {Node.CurrentNodeIPs.Count} available nodes.");

        var requiredVotes = selectionSize / 2 + 1;

        var voteTasks = new List<Task<(bool, string)>>();

        foreach (var validator in selectedValidators)
            voteTasks.Add(SendVoteRequestAsync(validator, block));

        var results = await Task.WhenAll(voteTasks);

        var positiveVotes = results.Count(result => result.Item1 && result.Item2.StartsWith("ok#"));

        Logger.LogMessage(positiveVotes >= requiredVotes
            ? "Consensus reached. Block validated."
            : "No consensus reached. Block discharge.");

        var rewardAddresses = new List<string>();
        foreach (var result in results)
        {
            var sp = result.Item2.Split('#');
            if (sp.Length == 2 && sp[0] == "ok")
                if (!rewardAddresses.Contains(sp[1]))
                    rewardAddresses.Add(sp[1]);
        }

        return (positiveVotes >= requiredVotes, rewardAddresses);
    }

    public async Task<(bool, string)> SendVoteRequestAsync(string targetValidator, Block block)
    {
        try
        {
            var message = $"Vote:{block.Base64Encoded}";
            if (block.Verify(message))
            {
                Logger.LogMessage($"Sending vote request to: {targetValidator}");
                var response = await SocketManager.GetInstance(targetValidator).SendMessageAsync(message);
                Logger.LogMessage($"Response from {targetValidator}: {response}");
                return (true, response);
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error sending vote request: {ex.Message}");
        }

        return (false, "");
    }

    public async Task<(string result, string updatedSerializedState)> ExecuteSmartContract(string contractName,
        string[] inputs)
    {
        var contract = Chain.SelectMany(b => b.SmartContracts).FirstOrDefault(c => c.Name == contractName);
        if (contract == null) throw new Exception($"Smart Contract '{contractName}' not found in the blockchain.");

        // Load serialized state from the blockchain
        var currentSerializedState = await GetContractState(contract);

        try
        {
            Logger.LogMessage($"Executing Smart Contract '{contractName}'...");

            PayGas("Contract " + contract.Name, contract.Owner, contract.Gas);

            var (result, updatedSerializedState) = await contract.Execute(ModifyInput(inputs), currentSerializedState);

            // Serialize updated state back to the blockchain
            if (result == "ok")
                await WriteContractStateToBlockchain(contract, updatedSerializedState);

            Logger.LogMessage($"Smart Contract {contractName} Result: {result}");
            return (result, updatedSerializedState);
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error executing Smart Contract '{contractName}': {ex.Message}");
            return ("Execution failed", currentSerializedState);
        }
    }

    private string[] ModifyInput(string[] inputs)
    {
        var firstLine = inputs.FirstOrDefault();
        if (string.IsNullOrEmpty(firstLine))
            throw new ArgumentException("Input array must not be empty.");

        var className = ExtractClassName(firstLine);

        var secondLine =
            $"if(!string.IsNullOrEmpty(CurrentState)) token = Serializer.DeserializeFromBase64<{className}>(CurrentState);";
        var lastLine = "Output = Serializer.SerializeToBase64(token);";

        return new[] { inputs[0], secondLine }
            .Concat(inputs.Skip(1))
            .Concat(new[] { lastLine })
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
        var compressedState = Compress.CompressString(serializedState);

        var stateTransaction = new Transaction
        {
            Sender = contract.Owner,
            Recipient = SystemAddress,
            Data = Convert.ToBase64String(compressedState),
            Timestamp = DateTime.UtcNow,
            Info = "$" + contract.Name
        };

        AddTransaction(stateTransaction);
        await MinePendingTransactions(MinerAdress);
        Logger.LogMessage($"State for contract '{contract.Name}' added to pending transactions.");
    }

    public async Task<string> GetContractState(SmartContract contract)
    {
        for (var i = Chain.Count - 1; i >= 0; i--)
            foreach (var transaction in Chain[i].Transactions)
                if (transaction.Recipient == SystemAddress && transaction.Info == "$" + contract.Name &&
                    !string.IsNullOrEmpty(transaction.Data))
                {
                    var compressedState = Convert.FromBase64String(transaction.Data);
                    return Compress.DecompressString(compressedState);
                }

        await MinePendingTransactions(MinerAdress);

        Logger.LogMessage(
            $"No state found for contract '{contract.Name}', initializing new state. Adding contract state to blockchain");
        var newState = "";
        await WriteContractStateToBlockchain(contract, newState);
        return newState;
    }

    public async Task MinePendingTransactions(string minerAddress)
    {
        while (Node.CurrentNodeIPs.Count == 0) Thread.Sleep(10);

        List<Transaction> transactionsToMine;

        lock (_pendingTransactionsLock)
        {
            if (PendingTransactions.Count == 0)
            {
                Logger.LogMessage("No transactions to mine.");
                return;
            }

            transactionsToMine = PendingTransactions.ToList();
            PendingTransactions.Clear();
        }

        var block = new Block(DateTime.Now, transactionsToMine, Chain.Last().Hash);

        var (consensusReached, rewardAddresses) = await ReachConsensus(block);
        if (consensusReached)
        {
            lock (Chain)
            {
                if (ValidateChain())
                    if (AddBlock(block, false))
                    {
                        BlockchainServer.BroadcastToPeers(Node.CurrentNodeIPs,
                            "PushServers", string.Join(",", Node.CurrentNodeIPs).TrimEnd(','));
                        BlockchainServer.BroadcastToPeers(Node.CurrentNodeIPs,
                            "NewBlock", block.ToBase64());
                    }
            }

            var rewardTransaction = new RewardTransaction(Config.Default.MinerAddress);
            AddTransaction(rewardTransaction);

            Logger.LogMessage($"Miner reward: Reward {rewardTransaction.Reward} to miner {minerAddress}");

            foreach (var address in rewardAddresses)
            {
                if (address == minerAddress)
                    continue;

                rewardTransaction = new RewardTransaction(address, true);
                AddTransaction(rewardTransaction);
                Logger.LogMessage($"Validator reward: Reward {rewardTransaction.Reward} to validator {address}");
            }
        }
        else
        {
            Logger.LogMessage("Block rejected");
        }
    }

    public async Task<bool> ReachCodeConsensus(SmartContract contract)
    {
        try
        {
            Logger.LogMessage($"Starting Snowman-Consensus for contract: {contract.Name}");

            var requiredVotes = Node.CurrentNodeIPs.Count / 2 + 1;

            var voteTasks = new List<Task<bool>>();

            foreach (var validator in Node.CurrentNodeIPs)
                voteTasks.Add(SendCodeForVerificationAsync(validator, contract));

            var results = await Task.WhenAll(voteTasks);

            var positiveVotes = results.Count(result => result);

            Logger.LogMessage(positiveVotes >= requiredVotes
                ? $"Consensus reached for {contract.Name}. Code is safe."
                : $"No consensus reached for contract {contract.Name}. Code discarded.");

            return positiveVotes >= requiredVotes;
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error during consensus: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SendCodeForVerificationAsync(string serverAddress, SmartContract contract)
    {
        try
        {
            var message = $"VerifyCode:{contract.SerializedContractCode}";
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync(message);

            Logger.LogMessage($"Code {contract.Name} sent to {serverAddress} for verification.");
            Logger.LogMessage($"Response from server for code {contract.Name}: {response}");

            return response == "ok";
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error sending code {contract.Name} to {serverAddress}: {ex.Message}");
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


    public Dictionary<string, double> GetAllBalancesFromChain()
    {
        var balances = new Dictionary<string, double>();
        try
        {
            var sortedBlocks = Chain.OrderByDescending(block => block.Timestamp);

            foreach (var block in sortedBlocks)
            {
                var sortedTransactions = block.Transactions.OrderByDescending(transaction => transaction.Timestamp);

                foreach (var (address, balance) in Transaction.Balances)
                    if (!balances.ContainsKey(address))
                        balances[address] = balance;
            }
        }
        catch (Exception e)
        {
            Logger.LogMessage($"GetAllBalancesFromChain error: {e.Message}");
        }

        return balances;
    }


    public static Blockchain? Load(string tmpFile)
    {
        try
        {
            var compressedData = File.ReadAllBytes(tmpFile);
            return FromBytes(compressedData);
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error loading the blockchain: {ex.Message}");
            throw;
        }
    }

    public static Blockchain? FromBytes(byte[] compressedData)
    {
        try
        {
            var jsonString = Compress.DecompressString(compressedData);

            var jsonDocument = JsonDocument.Parse(jsonString);
            var root = jsonDocument.RootElement;

            var difficulty = root.GetProperty("_difficulty").GetInt32();
            var minerAddress = root.GetProperty("MinerAdress").GetString();

            var blockchain = new Blockchain(difficulty, minerAddress);

            blockchain.Chain = JsonSerializer.Deserialize<List<Block>>(root.GetProperty("Chain").GetRawText());
            blockchain.PendingTransactions =
                JsonSerializer.Deserialize<List<Transaction>>(root.GetProperty("PendingTransactions").GetRawText());

            Logger.LogMessage("Blockchain loaded successfully.");
            return blockchain;
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error loading the blockchain: {ex.Message}");
            throw;
        }
    }

    public string ToBase64()
    {
        return Convert.ToBase64String(GetBytes());
    }

    public static Blockchain? FromBase64(string base64)
    {
        var compressedData = Convert.FromBase64String(base64);
        return FromBytes(compressedData);
    }

    public byte[] GetBytes()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true
        };
        var jsonString = JsonSerializer.Serialize(this, options);
        var compressedData = Compress.CompressString(jsonString);
        return compressedData;
    }

    public void Save(string file)
    {
        try
        {
            File.WriteAllBytes(file, GetBytes());
            Logger.LogMessage($"Blockchain saved to: {file}");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error saving the blockchain: {ex.Message}");
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