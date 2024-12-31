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
    ///     Initializes the blockchain with a specified difficulty and miner address.
    ///     Creates a genesis block as the starting point of the blockchain.
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

    [JsonInclude] internal string MinerAdress { get; }

    [JsonInclude] public List<Block> Chain { get; private set; }

    [JsonInclude] internal List<Transaction> PendingTransactions { get; private set; }

    [JsonInclude] internal static double CurrentNetworkLoad { get; private set; } = .5d;

    public IReadOnlyDictionary<string, SmartContract> SmartContracts
    {
        get
        {
            return Chain
                .SelectMany(block => block.SmartContracts)
                .ToDictionary(contract => contract.Name, contract => contract);
        }
    }

    /// <summary>
    ///     Creates the genesis block, which serves as the initial block in the blockchain.
    /// </summary>
    private Block CreateGenesisBlock()
    {
        return new Block(new List<Transaction>(), "0");
    }

    /// <summary>
    ///     Adds a block to the blockchain with optional parameters for locking the chain and mining the block.
    /// </summary>
    internal bool AddBlock(Block block, bool lockChain = true, bool mineBlock = true, int? index = null)
    {
        if (!block.ValidateTimestamp())
        {
            Logger.LogMessage("Invalid timestamp. Block rejected.");
            return false;
        }

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
        else if (index.HasValue && index.Value <= Chain.Count)
        {
            lock (Chain)
            {
                if (index.Value < 0)
                {
                    Logger.LogMessage("Invalid index for adding block.");
                    return false;
                }

                if (index.Value > 0 && Chain[index.Value - 1].Hash != block.PreviousHash)
                {
                    Logger.LogMessage("Block not added to chain -- invalid previous block for given index.");
                    return false;
                }

                if (index.Value < Chain.Count && Chain[index.Value].PreviousHash != block.Hash)
                {
                    Logger.LogMessage("Block not added to chain -- would break chain consistency.");
                    return false;
                }

                Chain.Insert(index.Value, block);
            }
        }
        else
        {
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

        // Optimize: Index transactions for quick lookup
        IndexTransactions(block);

        return true;
    }

    /// <summary>
    ///     Simulates the payment of gas fees by a transaction sender.
    /// </summary>
    private static void PayGas(string type, string payer, int gas)
    {
        if (gas > 0 && payer != SystemAddress)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Logger.LogMessage($"[Gas] {payer} has to pay {gas} for {type}");
            Console.ResetColor();
        }
    }

    /// <summary>
    ///     Adds a transaction to the list of pending transactions.
    ///     Ensures that the transaction is signed and valid.
    /// </summary>
    internal bool AddTransaction(Transaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.Sender))
        {
            Logger.LogMessage($"Transaction {transaction.Name} not added. Sender is missing");
            return false;
        }

        if (string.IsNullOrEmpty(transaction.Recipient))
        {
            Logger.LogMessage($"Transaction {transaction.Name} not added. Recipient is missing");
            return false;
        }

        transaction.SignTransaction(Crypt.Default.PrivateKey);
        var gas = transaction.Gas;

        if (transaction.Sender == SystemAddress) gas = 0;

        if (gas > 0)
            PayGas("Transaction", transaction.Sender, gas);

        lock (PendingTransactions)
        {
            PendingTransactions.Add(transaction);
        }

        return true;
    }

    /// <summary>
    ///     Adds a smart contract to the blockchain after reaching consensus.
    ///     Ensures the contract name is valid and unique.
    /// </summary>
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

    /// <summary>
    ///     Reaches consensus for a specific block using selected validators.
    /// </summary>
    /// <param name="block">The block requiring consensus.</param>
    /// <returns>
    ///     A tuple where the first value indicates if consensus was reached, and the second is a list of reward
    ///     addresses.
    /// </returns>
    private async Task<(bool, List<string>)> ReachConsensus(Block block)
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

    /// <summary>
    ///     Sends a vote request to a target validator for a specific block.
    /// </summary>
    /// <param name="targetValidator">The address of the target validator.</param>
    /// <param name="block">The block to be validated.</param>
    /// <returns>
    ///     A tuple where the first value indicates if the request was successful, and the second contains the response
    ///     message.
    /// </returns>
    private async Task<(bool, string)> SendVoteRequestAsync(string targetValidator, Block block)
    {
        try
        {
            var message = $"Vote:{block.Base64Encoded}";
            if (block.Verify(message))
            {
                Logger.LogMessage($"Sending vote request to: {targetValidator}");
                var response = await SocketManager.GetInstance(targetValidator).SendMessageAsync(message);
                if (Config.Default.Debug) 
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

    /// <summary>
    ///     Executes the specified smart contract with the provided inputs.
    ///     Updates and serializes the state of the contract after execution.
    /// </summary>
    /// <param name="contractName">The name of the smart contract to execute.</param>
    /// <param name="inputs">An array of input strings for the smart contract.</param>
    /// <returns>A tuple containing the execution result and the updated serialized state.</returns>
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

            Console.WriteLine($"Smart Contract {contractName} Result: {result}");
            return (result, updatedSerializedState);
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error executing Smart Contract '{contractName}': {ex.Message}");
            return ("Execution failed", currentSerializedState);
        }
    }

    /// <summary>
    ///     Modifies the input array to include serialized state management logic for smart contract execution.
    /// </summary>
    /// <param name="inputs">An array of input strings for the smart contract.</param>
    /// <returns>An updated array of input strings with added state management logic.</returns>
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

    /// <summary>
    ///     Extracts the class name from a constructor line in the input.
    /// </summary>
    /// <param name="line">The constructor line containing the class name.</param>
    /// <returns>The extracted class name as a string.</returns>
    private string ExtractClassName(string line)
    {
        const string tokenStart = "new ";
        var startIndex = line.IndexOf(tokenStart) + tokenStart.Length;
        var endIndex = line.IndexOf('(', startIndex);
        if (startIndex < 0 || endIndex < 0)
            throw new ArgumentException("Invalid format of the constructor line.");

        return line.Substring(startIndex, endIndex - startIndex);
    }

    /// <summary>
    ///     Writes the serialized state of a smart contract to the blockchain.
    ///     Creates a state transaction and mines it to finalize the state update.
    /// </summary>
    /// <param name="contract">The smart contract whose state is being updated.</param>
    /// <param name="serializedState">The serialized state to be written to the blockchain.</param>
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

    /// <summary>
    ///     Retrieves the serialized state of a specified smart contract from the blockchain.
    ///     If no state exists, initializes and writes a new state to the blockchain.
    /// </summary>
    /// <param name="contract">The smart contract whose state is to be retrieved.</param>
    /// <returns>A task representing the asynchronous operation, with the serialized state as the result.</returns>
    private async Task<string> GetContractState(SmartContract contract)
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

    /// <summary>
    ///     Mines all pending transactions and adds them to the blockchain.
    ///     Includes consensus validation and reward distribution for miners and validators.
    /// </summary>
    /// <param name="minerAddress">The address of the miner receiving the reward.</param>
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

        var block = new Block(transactionsToMine, Chain.Last().Hash);

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

            var rewardTransaction = new RewardTransaction(this, Config.Default.MinerAddress);
            AddTransaction(rewardTransaction);

            Logger.LogMessage($"Miner reward: Reward {rewardTransaction.Reward} to miner {minerAddress}");

            foreach (var address in rewardAddresses)
            {
                if (address == minerAddress)
                    continue;

                rewardTransaction = new RewardTransaction(this, address, true);
                AddTransaction(rewardTransaction);
                Logger.LogMessage($"Validator reward: Reward {rewardTransaction.Reward} to validator {address}");
            }
        }
        else
        {
            Logger.LogMessage("Block rejected");
        }
    }

    /// <summary>
    ///     Attempts to reach consensus for the provided smart contract across the network.
    /// </summary>
    /// <param name="contract">The smart contract to validate and reach consensus on.</param>
    /// <returns>True if consensus is reached; otherwise, false.</returns>
    private async Task<bool> ReachCodeConsensus(SmartContract contract)
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

    /// <summary>
    ///     Sends the serialized code of a smart contract to a server for verification.
    /// </summary>
    /// <param name="serverAddress">The address of the server for verification.</param>
    /// <param name="contract">The smart contract to be verified.</param>
    /// <returns>A boolean indicating whether the code verification was successful.</returns>
    private async Task<bool> SendCodeForVerificationAsync(string serverAddress, SmartContract contract)
    {
        try
        {
            var message = $"VerifyCode:{contract.SerializedContractCode}";
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync(message);
            if (Config.Default.Debug)
            {
                Logger.LogMessage($"Code {contract.Name} sent to {serverAddress} for verification.");
                Logger.LogMessage($"Response from server for code {contract.Name}: {response}", false);
            } 
            return response == "ok";
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error sending code {contract.Name} to {serverAddress}: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Retrieves the latest block in the blockchain.
    /// </summary>
    public Block GetLatestBlock()
    {
        lock (Chain)
        {
            return Chain.Last();
        }
    }

    /// <summary>
    ///     Retrieves a smart contract from the blockchain by its name.
    /// </summary>
    /// <param name="contractName">The name of the smart contract to retrieve.</param>
    /// <returns>The smart contract with the specified name, or null if not found.</returns>
    public SmartContract? GetContractByName(string contractName)
    {
        return Chain.SelectMany(block => block.SmartContracts)
            .FirstOrDefault(contract => contract.Name == contractName);
    }

    /// <summary>
    ///     Retrieves a list of smart contracts from the blockchain.
    /// </summary>
    /// <returns>A list of smart contracts</returns>
    public IEnumerable<SmartContract> GetContracts()
    {
        return Chain?.SelectMany(block => block.SmartContracts) ?? Enumerable.Empty<SmartContract>();
    }

    /// <summary>
    ///     Validates the entire blockchain by checking the hashes and links between blocks.
    /// </summary>
    /// <returns>True if the blockchain is valid; otherwise, false.</returns>
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

    /// <summary>
    ///     Prints the blocks and transactions in the blockchain to the logger.
    /// </summary>
    /// <param name="fromBlock">Starting block index (inclusive).</param>
    /// <param name="toBlock">Ending block index (inclusive). If -1, prints all blocks.</param>
    /// <param name="showTransactions">If true, prints the transactions within each block.</param>
    public void PrintAllBlocksAndTransactions(int fromBlock = 0, int toBlock = -1, bool showTransactions = true)
    {
        if (toBlock == -1 || toBlock >= Chain.Count) toBlock = Chain.Count - 1;

        Logger.LogMessage("------------------- CHAIN INFO -------------------");
        Logger.LogMessage("Listing all blocks and their transactions:");

        for (var i = fromBlock; i <= toBlock; i++)
        {
            var block = Chain[i];
            Logger.LogMessage("------------------- BLOCK -------------------");
            Logger.LogMessage($"Block Index: {i}");
            Logger.LogMessage($"Timestamp: {block.Timestamp}");
            Logger.LogMessage($"Hash: {block.Hash}");
            Logger.LogMessage($"Previous Hash: {block.PreviousHash}");
            Logger.LogMessage($"Nonce: {block.Nonce}");

            if (showTransactions)
                foreach (var transaction in block.Transactions)
                {
                    Logger.LogMessage("---------------- TRANSACTION ----------------");
                    Logger.LogMessage($"Sender: {transaction.Sender}");
                    Logger.LogMessage($"Recipient: {transaction.Recipient}");
                    if (transaction.Amount > 0)
                        Logger.LogMessage($"Amount: {transaction.Amount}");
                    if (!string.IsNullOrEmpty(transaction.Data))
                        Logger.LogMessage($"Data: {transaction.Data}");
                    if (!string.IsNullOrEmpty(transaction.Info))
                        Logger.LogMessage($"Info: {transaction.Info}");
                    if (transaction.Gas > 0)
                        Logger.LogMessage($"Gas: {transaction.Gas}");
                    Logger.LogMessage($"Timestamp: {transaction.Timestamp}");
                    Logger.LogMessage($"Signature: {transaction.Signature}");
                    Logger.LogMessage($"Version: {transaction.Version}");
                    Logger.LogMessage($"Transaction Date: {transaction.Timestamp}");
                }
        }

        Logger.LogMessage("------------------- CHAIN INFO -------------------");
    }

    /// <summary>
    ///     Retrieves all account balances from the blockchain by aggregating transaction data.
    /// </summary>
    /// <returns>A dictionary mapping addresses to their respective balances.</returns>
    public Dictionary<string, double> GetAllBalancesFromChain()
    {
        var balances = new Dictionary<string, double>();
        try
        {
            Transaction.UpdateBalancesFromChain(this);

            var sortedBlocks = Chain.OrderByDescending(block => block.Timestamp);

            foreach (var block in Chain)
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

    /// <summary>
    ///     Loads a blockchain instance from a file.
    /// </summary>
    /// <param name="tmpFile">The file path to load the blockchain from.</param>
    /// <returns>A Blockchain object if successfully loaded; otherwise, null.</returns>
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

    /// <summary>
    ///     Deserializes a blockchain instance from a compressed byte array.
    /// </summary>
    /// <param name="compressedData">The compressed byte array representing the blockchain.</param>
    /// <returns>A Blockchain object if deserialization is successful; otherwise, null.</returns>
    public static Blockchain? FromBytes(byte[] compressedData)
    {
        try
        {
            var jsonString = Compress.DecompressString(compressedData);

            var jsonDocument = JsonDocument.Parse(jsonString);
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("Chain", out var chainElement))
                throw new InvalidOperationException("Chain property is missing.");

            if (!chainElement.TryGetProperty("Chain", out var innerChainElement))
                throw new InvalidOperationException("Inner Chain property is missing.");

            var difficulty = chainElement.GetProperty("_difficulty").GetInt32();
            var minerAddress = chainElement.GetProperty("MinerAdress").GetString();

            var blockchain = new Blockchain(difficulty, minerAddress);

            blockchain.Chain = JsonSerializer.Deserialize<List<Block>>(innerChainElement.GetRawText());

            blockchain.PendingTransactions =
                JsonSerializer.Deserialize<List<Transaction>>(root.GetProperty("PendingTransactions").GetRawText());

            if (root.TryGetProperty("Balances", out var balancesJson))
            {
                var balances = JsonSerializer.Deserialize<Dictionary<string, double>>(balancesJson.GetRawText());
                foreach (var balance in balances) Transaction.Balances[balance.Key] = balance.Value;
            }

            if (root.TryGetProperty("Allowances", out var allowancesJson))
            {
                var allowances =
                    JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(
                        allowancesJson.GetRawText());
                foreach (var allowance in allowances) Transaction.Allowances[allowance.Key] = allowance.Value;
            }

            if (root.TryGetProperty("AuthenticatedUsers", out var authenticatedUsersJson))
            {
                var authenticatedUsers =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(authenticatedUsersJson.GetRawText());
                foreach (var user in authenticatedUsers) Transaction.AuthenticatedUsers[user.Key] = user.Value;
            }

            Logger.LogMessage("Blockchain loaded successfully.");
            return blockchain;
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error loading the blockchain: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    ///     Serializes the blockchain to a Base64 string for storage or transmission.
    /// </summary>
    /// <returns>A Base64 string representation of the blockchain.</returns>
    public string ToBase64()
    {
        return Convert.ToBase64String(GetBytes());
    }

    /// <summary>
    ///     Creates a Blockchain object from a base64-encoded string representation.
    /// </summary>
    /// <param name="base64">The base64 string representing the blockchain data.</param>
    /// <returns>A Blockchain object if decoding is successful; otherwise, null.</returns>
    public static Blockchain? FromBase64(string base64)
    {
        var compressedData = Convert.FromBase64String(base64);
        return FromBytes(compressedData);
    }

    /// <summary>
    ///     Serializes the blockchain to a byte array for storage or transmission.
    /// </summary>
    /// <returns>A compressed byte array representing the blockchain.</returns>
    public byte[] GetBytes()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true
        };
        var jsonData = new
        {
            Chain = this,
            PendingTransactions = new List<Transaction>(),
            Transaction.Balances,
            Transaction.Allowances,
            Transaction.AuthenticatedUsers
        };

        var jsonString = JsonSerializer.Serialize(jsonData, options);
        var compressedData = Compress.CompressString(jsonString);
        return compressedData;
    }

    /// <summary>
    ///     Saves the blockchain to a file.
    /// </summary>
    /// <param name="file">The file path to save the blockchain.</param>
    /// <returns>true if chain was saved successfully, otherwise false.</returns>
    public bool Save(string file)
    {
        try
        {
            File.WriteAllBytes(file, GetBytes());
            Logger.LogMessage($"Blockchain saved to: {file}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error saving the blockchain: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Verifies the integrity of the blockchain by checking hashes and previous hashes.
    /// </summary>
    /// <returns>True if the blockchain is valid, otherwise false.</returns>
    internal bool IsValid()
    {
        var isValid = true;
        Parallel.For(1, Chain.Count, (i, state) =>
        {
            var currentBlock = Chain[i];
            var previousBlock = Chain[i - 1];

            // Check if the current block's hash is valid
            if (currentBlock.Hash != currentBlock.CalculateHash())
            {
                Logger.LogMessage($"Block {i} has an invalid hash.");
                isValid = false;
                state.Stop(); // Stop further iterations if invalid
            }

            // Check if the previous hash in the current block matches the hash of the previous block
            if (currentBlock.PreviousHash != previousBlock.Hash)
            {
                Logger.LogMessage($"Block {i} has an invalid previous hash.");
                isValid = false;
                state.Stop(); // Stop further iterations if invalid
            }
        });

        if (isValid) Logger.LogMessage("Blockchain integrity verified successfully.");

        return isValid;
    }

    #region Index

    private readonly Dictionary<string, List<Transaction>> _transactionIndex = new();
    private readonly int MaxTransactionsPerAddress = 1000;
    private readonly string ArchiveFilePath = "transaction_archive.json";

    /// <summary>
    ///     Indexes all transactions from a given block for efficient lookup.
    /// </summary>
    /// <param name="block">The block containing transactions to index.</param>
    private void IndexTransactions(Block block)
    {
        foreach (var transaction in block.Transactions) IndexTransaction(transaction);

        Logger.LogMessage("Transactions indexed for block: " + block.Hash);
    }

    /// <summary>
    ///     Indexes a single transaction by updating the internal transaction index.
    ///     Ensures the sender and recipient addresses are included in the index.
    /// </summary>
    /// <param name="transaction">The transaction to be indexed.</param>
    private void IndexTransaction(Transaction transaction)
    {
        void AddToIndex(string key, Transaction tx)
        {
            if (!_transactionIndex.ContainsKey(key)) _transactionIndex[key] = new List<Transaction>();

            _transactionIndex[key].Add(tx);

            // Maintain the max size limit for the index
            if (_transactionIndex[key].Count > MaxTransactionsPerAddress) ArchiveOldTransactions(key);
        }

        AddToIndex(transaction.Sender, transaction);
        AddToIndex(transaction.Recipient, transaction);
    }

    // <summary>
    /// Archives old transactions for a specific address to maintain the size of the transaction index.
    /// </summary>
    /// <param name="address">The address whose transactions need to be archived.</param>
    private void ArchiveOldTransactions(string address)
    {
        var transactions = _transactionIndex[address];
        var transactionsToArchive = transactions.Take(transactions.Count - MaxTransactionsPerAddress).ToList();
        transactions.RemoveRange(0, transactionsToArchive.Count);

        try
        {
            var archiveData = File.Exists(ArchiveFilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, List<Transaction>>>(File.ReadAllText(ArchiveFilePath))
                : new Dictionary<string, List<Transaction>>();

            if (!archiveData.ContainsKey(address)) archiveData[address] = new List<Transaction>();

            archiveData[address].AddRange(transactionsToArchive);

            File.WriteAllText(ArchiveFilePath, JsonSerializer.Serialize(archiveData));
            Logger.LogMessage($"Archived {transactionsToArchive.Count} transactions for address {address}.");
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error archiving transactions for {address}: {ex.Message}");
        }
    }

    /// <summary>
    ///     Retrieves a list of transactions associated with a specific address from the blockchain.
    /// </summary>
    /// <param name="address">The address for which to retrieve transactions.</param>
    /// <returns>A list of transactions involving the specified address.</returns>
    public List<Transaction> GetTransactionsByAddress(string address)
    {
        var transactions = new List<Transaction>();

        if (_transactionIndex.TryGetValue(address, out var indexedTransactions))
            transactions.AddRange(indexedTransactions);

        try
        {
            if (File.Exists(ArchiveFilePath))
            {
                var archiveData =
                    JsonSerializer.Deserialize<Dictionary<string, List<Transaction>>>(
                        File.ReadAllText(ArchiveFilePath));
                if (archiveData != null && archiveData.TryGetValue(address, out var archivedTransactions))
                    transactions.AddRange(archivedTransactions);
            }
        }
        catch (Exception ex)
        {
            Logger.LogMessage($"Error loading archived transactions for {address}: {ex.Message}");
        }

        return transactions;
    }

    #endregion
}