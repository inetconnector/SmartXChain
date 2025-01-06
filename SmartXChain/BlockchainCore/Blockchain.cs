using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SmartXChain.Contracts;
using SmartXChain.Server;
using SmartXChain.Utils;
using SmartXChain.Validators;
using static SmartXChain.BlockchainCore.Transaction;

namespace SmartXChain.BlockchainCore;

public class Blockchain
{
    public const string SystemAddress = "smartX0000000000000000000000000000000000000000";

    [JsonInclude] private readonly int _difficulty;


    /// <summary>
    ///     Initializes the blockchain with a specified difficulty and miner address.
    ///     Creates a genesis block as the starting point of the blockchain.
    /// </summary>
    public Blockchain(string minerAdress, string chainId, int difficulty = 0)
    {
        Chain = new List<Block>();
        PendingTransactions = new List<Transaction>();
        _difficulty = difficulty;

        ChainId = chainId;
        MinerAdress = minerAdress;

        AddBlock(CreateGenesisBlock());
    }

    [JsonInclude] internal string ChainId { get; }
    [JsonInclude] internal string MinerAdress { get; }

    [JsonInclude] public List<Block>? Chain { get; private set; }

    [JsonInclude] internal List<Transaction>? PendingTransactions { get; private set; }

    [JsonInclude] internal static double CurrentNetworkLoad { get; private set; } = .5d;

    public IReadOnlyDictionary<string, SmartContract?> SmartContracts
    {
        get
        {
            var contracts = new Dictionary<string, SmartContract?>();

            if (Chain != null)
                lock (Chain)
                {
                    foreach (var block in Chain)
                        lock (block)
                        {
                            foreach (var kvp in block.SmartContracts)
                            {
                                contracts.Add(kvp.Key, kvp.Value);
                            }
                        }
                }

            return contracts;
        }
    }

    /// <summary>
    ///     Retrieves the latest block in the blockchain.
    /// </summary>
    public Block? LatestBlock
    {
        get
        {
            if (Chain != null)
                lock (Chain)
                {
                    return Chain.Last();
                }

            return null;
        }
    }

    /// <summary>
    ///     Retrieves the first block in the blockchain.
    /// </summary>
    public Block? FirstBlock
    {
        get
        {
            if (Chain != null)
                lock (Chain)
                {
                    return Chain.First();
                }

            return null;
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
            Logger.Log("Invalid timestamp. Block rejected.");
            return false;
        }

        if (mineBlock)
        {
            if (lockChain)
            {
                if (Chain != null)
                    lock (Chain)
                    {
                        if (Chain.Count > 0)
                            block.PreviousHash = Chain.Last().Hash;
                        block.Mine(_difficulty);
                        Chain.Add(block);
                    }
            }
            else if (Chain != null)
            {
                if (Chain.Count > 0)
                    block.PreviousHash = Chain.Last().Hash;
                block.Mine(_difficulty);
                Chain.Add(block);
            }
        }
        else if (index.HasValue && Chain != null && index.Value <= Chain.Count)
        {
            lock (Chain)
            {
                if (index.Value < 0)
                {
                    Logger.Log("Invalid index for adding block.");
                    return false;
                }

                if (index.Value > 0 && Chain[index.Value - 1].Hash != block.PreviousHash)
                {
                    Logger.Log("Block not added to chain -- invalid previous block for given index.");
                    return false;
                }

                if (index.Value < Chain.Count && Chain[index.Value].PreviousHash != block.Hash)
                {
                    Logger.Log("Block not added to chain -- would break chain consistency.");
                    return false;
                }

                Chain.Insert(index.Value, block);
            }
        }
        else
        {
            if (Chain != null && Chain.Count > 0 && Chain.Last().Hash == block.PreviousHash)
            {
                lock (Chain)
                {
                    Chain.Add(block);
                }
            }
            else
            {
                Logger.Log("Block not added to chain -- invalid previous block");
                return false;
            }
        }

        Logger.Log("Block added to chain successfully: " + block.Hash);

        // Optimize: Index transactions for quick lookup
        IndexTransactions(block);

        return BlockchainStorage.SaveBlock(block, Config.Default.BlockchainPath, ChainId);
    }

    /// <summary>
    ///     Simulates the payment of gas fees by a transaction sender.
    /// </summary>
    private static void PayGas(string type, string payer, int gas)
    {
        if (gas > 0 && payer != SystemAddress)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Logger.Log($"[Gas] {payer} has to pay {gas} for {type}");
            Console.ResetColor();
        }
    }

    /// <summary>
    ///     Adds a transaction to the list of pending transactions.
    ///     Ensures that the transaction is signed and valid.
    /// </summary>
    internal async Task<bool> AddTransaction(Transaction transaction, bool mine = false)
    {
        if (string.IsNullOrEmpty(transaction.Sender))
        {
            Logger.Log($"Transaction {transaction.Name} not added. Sender is missing");
            return false;
        }

        if (string.IsNullOrEmpty(transaction.Recipient))
        {
            Logger.Log($"Transaction {transaction.Name} not added. Recipient is missing");
            return false;
        }

        transaction.SignTransaction(Crypt.Default.PrivateKey);
        var gas = transaction.Gas;

        if (transaction.Sender == SystemAddress) gas = 0;

        if (gas > 0)
            PayGas("Transaction", transaction.Sender, gas);

        if (PendingTransactions != null)
            lock (PendingTransactions)
            {
                PendingTransactions.Add(transaction);
            }

        if (mine) await MinePendingTransactions(transaction.Sender);
        return true;
    }

    /// <summary>
    ///     Adds a smart contract to the blockchain after reaching consensus.
    ///     Ensures the contract name is valid and unique.
    /// </summary>
    public async Task<bool> AddSmartContract(Blockchain? chain, SmartContract? contract)
    {
        if (contract != null && !Regex.IsMatch(contract.Name, @"^[a-zA-Z0-9]+$"))
        {
            Logger.Log(
                $"Invalid Smart Contract name '{contract.Name}'. Names can only contain alphanumeric characters (a-z, A-Z, 0-9) without spaces.");
            return false;
        }

        // Check if the contract with the same name already exists
        if (contract != null && SmartContracts.ContainsKey(contract.Name))
        {
            Logger.Log($"A Smart Contract with the name '{contract.Name}' already exists in the blockchain.");
            return false;
        }

        if (await ReachCodeConsensus(contract))
        {
            //"$$" + contract.Name in info of transaction defines a smart contract in data
            if (contract != null)
            {
                var contractTransaction = new Transaction
                {
                    Sender = contract.Owner,
                    Recipient = SystemAddress,
                    Data = contract.SerializedContractCode,
                    Info = "$$" + contract.Name,
                    Timestamp = DateTime.UtcNow,
                    TransactionType = TransactionTypes.ContractCode
                };

                if (chain != null)
                    if (!await chain.AddTransaction(contractTransaction, true))
                    {
                        Logger.Log($"ERROR: Smart Contract '{contract.Name}' not added to the blockchain.");
                        return false;
                    }
            }

            if (contract != null)
            {
                if (FirstBlock != null)
                    FirstBlock.SmartContracts.TryAdd(contract.Name, contract);

                Logger.Log($"Smart Contract '{contract.Name}' added to the blockchain.");
                return true;
            }
        }
        else
        {
            if (contract != null)
                Logger.Log($"No consensus reached for the Smart Contract '{contract.Name}'");
        }

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
        Logger.Log($"Starting Snowman-Consensus for block: {block.Hash}");

        var selectionSize = Math.Max(1, Node.CurrentNodeIPs.Count / 2); // min 50% of the nodes
        var selectedValidators = Node.CurrentNodeIPs
            .Where(ip => !NetworkUtils.IP.Contains(ip)) // IPs filtern, die in NetworkUtils.IP enthalten sind
            .OrderBy(_ => Guid.NewGuid()) // Zufällige Reihenfolge
            .Take(selectionSize) // N erste Knoten auswählen
            .ToList();

        Logger.Log($"Selected {selectionSize} validators from {Node.CurrentNodeIPs.Count} available nodes.");

        var requiredVotes = selectionSize / 2 + 1;

        var voteTasks = new List<Task<(bool, string)>>();

        foreach (var validator in selectedValidators)
        {
            if (validator.Contains(NetworkUtils.IP))
                continue;
            voteTasks.Add(SendVoteRequestAsync(validator, block));
        }

        var results = await Task.WhenAll(voteTasks);

        var positiveVotes = results.Count(result => result.Item1 && result.Item2.StartsWith("ok#"));

        Logger.Log(positiveVotes >= requiredVotes
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
            var verifiedBlock = Block.FromBase64(block.Base64Encoded);
            if (verifiedBlock != null)
            {
                var hash = block.Hash;
                var calculatedHash = block.CalculateHash();
                if (calculatedHash == hash)
                {
                    var message = $"Vote:{block.Base64Encoded}";
                    Logger.Log($"Sending vote request to: {targetValidator}");
                    var response = await SocketManager.GetInstance(targetValidator).SendMessageAsync(message);
                    if (Config.Default.Debug)
                        Logger.Log($"Response from {targetValidator}: {response}");
                    return (response.Contains("ok"), response);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error sending vote request: {ex.Message}");
        }

        Logger.Log($"ERROR: block.Verify failed from {targetValidator}");
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
        SmartContract? contract = null;
        if (SmartContracts.ContainsKey(contractName)) contract = SmartContracts[contractName];

        if (contract == null)
        {
            //try to find contract in Transactions 
            contract = GetContractFromTransactions(contractName);
            if (contract == null)
                throw new Exception($"Smart Contract '{contractName}' not found in the blockchain.");
            if (Chain != null) Chain[0].SmartContracts.TryAdd(contractName, contract);
        }

        // Load serialized state from the blockchain
        var currentSerializedState = await GetContractState(contract);

        try
        {
            Logger.Log($"Executing Smart Contract '{contractName}'...");

            PayGas("Contract " + contract.Name, contract.Owner, contract.Gas);

            var (result, updatedSerializedState) = await contract.Execute(ModifyInput(inputs), currentSerializedState);

            // Serialize updated state back to the blockchain
            if (result == "ok")
                await WriteContractStateToBlockchain(contract, updatedSerializedState);

            Console.WriteLine($@"Smart Contract {contractName} Result: {result}");
            return (result, updatedSerializedState);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error executing Smart Contract '{contractName}': {ex.Message}");
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
            $"if(!string.IsNullOrEmpty(CurrentState)) token = Contract.DeserializeFromBase64<{className}>(CurrentState);";
        var lastLine = "Output = Contract.SerializeToBase64(token);";

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
        var startIndex = line.IndexOf(tokenStart, StringComparison.Ordinal) + tokenStart.Length;
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
    private async Task WriteContractStateToBlockchain(SmartContract? contract, string serializedState)
    {
        var compressedState = Compress.CompressString(serializedState);

        //"$" + contract.Name in info of transaction defines a contract state
        if (contract != null)
        {
            var stateTransaction = new Transaction
            {
                Sender = contract.Owner,
                Recipient = SystemAddress,
                Data = Convert.ToBase64String(compressedState),
                Timestamp = DateTime.UtcNow,
                Info = "$" + contract.Name,
                TransactionType = TransactionTypes.ContractState
            };

            await AddTransaction(stateTransaction);
        }

        if (contract != null)
            Logger.Log($"State for contract '{contract.Name}' added to pending transactions.");

        await MinePendingTransactions(MinerAdress);
    }

    /// <summary>
    ///     Retrieves the serialized state of a specified smart contract from the blockchain.
    ///     If no state exists, initializes and writes a new state to the blockchain.
    /// </summary>
    /// <param name="contract">The smart contract whose state is to be retrieved.</param>
    /// <returns>A task representing the asynchronous operation, with the serialized state as the result.</returns>
    private async Task<string> GetContractState(SmartContract? contract)
    {
        if (Chain != null)
            for (var i = Chain.Count - 1; i >= 0; i--)
                foreach (var transaction in Chain[i].Transactions)
                    if (transaction.Recipient == SystemAddress &&
                        contract != null &&
                        transaction.Info == "$" + contract.Name &&
                        !string.IsNullOrEmpty(transaction.Data))
                    {
                        var compressedState = Convert.FromBase64String(transaction.Data);
                        return Compress.DecompressString(compressedState);
                    }

        await MinePendingTransactions(MinerAdress);
        var newState = "";
        if (contract != null)
        {
            Logger.Log(
                $"No state found for contract '{contract.Name}', initializing new state. Adding contract state to blockchain");

            await WriteContractStateToBlockchain(contract, newState);
        }

        return newState;
    }

    /// <summary>
    ///     Retrieves all smart contracts from the blockchain and returns them as a dictionary.
    ///     Each entry in the dictionary has the contract name as the key and the SmartContract object as the value.
    ///     Contracts are identified by transactions with the recipient set to SystemAddress and an info field starting with
    ///     "$$".
    /// </summary>
    /// <returns>A dictionary containing all unique smart contracts found in the blockchain.</returns>
    private SmartContract? GetContractFromTransactions(string contractName)
    {
        if (Chain != null)
            lock (Chain)
            {
                foreach (var block in Chain.AsEnumerable().Reverse())
                    lock (block)
                    {
                        var contractTransaction = block.Transactions
                            .FirstOrDefault(transaction =>
                                transaction.Recipient == SystemAddress &&
                                transaction.Info == "$$" + contractName &&
                                !string.IsNullOrEmpty(transaction.Data));

                        if (contractTransaction != null)
                            try
                            {
                                var contractCode = Serializer.DeserializeFromBase64<string>(contractTransaction.Data);
                                return new SmartContract(
                                    contractTransaction.Sender,
                                    Serializer.SerializeToBase64(contractCode),
                                    contractName
                                );
                            }
                            catch (Exception ex)
                            {
                                Logger.Log($"ERROR: Failed to deserialize contract data: {ex.Message}");
                                return null;
                            }
                    }
            }

        Logger.Log($"ERROR: SmartContract '{contractName}' not found in blockchain transactions.");
        return null;
    }
     

    /// <summary>
    ///     Mines all pending transactions and adds them to the blockchain.
    ///     Includes consensus validation and reward distribution for miners and validators.
    /// </summary>
    /// <param name="minerAddress">The address of the miner receiving the reward.</param>
    public async Task MinePendingTransactions(string minerAddress)
    {
        var counter = 0;

        while (Node.CurrentNodeIPs.Count == 0)
        {
            if (counter % 1000 == 0) // All 10 sec (5000 ms / 10 ms = 500 iterations)
                Logger.Log("Waiting for validators...");
            Thread.Sleep(10);
            counter++;
        }

        if (ValidateChain())
        {
            if (Chain != null)
            {
                Block block;
                lock (Chain)
                {
                    var transactionsToMine = new List<Transaction>();
                    if (PendingTransactions != null)
                        lock (PendingTransactions)
                        {
                            if (PendingTransactions.Count == 0)
                            {
                                Logger.Log("No transactions to mine.");
                                return;
                            }

                            transactionsToMine = PendingTransactions.ToList();
                        }

                    block = new Block(transactionsToMine, Chain.Last().Hash);
                }

                //find consensus between validators
                if (block != null)
                {
                    var (consensusReached, rewardAddresses) = await ReachConsensus(block);
                    if (consensusReached)
                    {
                        //add do chain after successful validation 
                        if (AddBlock(block, false))
                        {
                            Broadcast(block);
                            PendingTransactions.Clear();
                        }

                        //send a reward to the miner
                        var rewardTransaction = new RewardTransaction(this, Config.Default.MinerAddress);
                        await AddTransaction(rewardTransaction);

                        Logger.Log(
                            $"Miner reward: MinerReward {rewardTransaction.Reward} to miner {minerAddress}");

                        //send a reward to the validators
                        foreach (var address in rewardAddresses)
                        {
                            if (address == minerAddress)
                                continue;

                            rewardTransaction = new RewardTransaction(this, address, true);
                            await AddTransaction(rewardTransaction);
                            Logger.Log(
                                $"Validator reward: MinerReward {rewardTransaction.Reward} to validator {address}");
                        }
                    }
                    else
                    {
                        Logger.Log("ERROR: Block rejected");
                    }
                }
            }
        }
        else
        {
            Logger.Log("ERROR: The chain is not valid");
        }
    }

    /// <summary>
    ///     Broadcasts a given block to all peers in the network except the current node's IPs.
    ///     Sends two types of broadcasts: one to push the list of servers and another to
    ///     share the new block.
    /// </summary>
    /// <param name="block">The block to broadcast to the network.</param>
    private void Broadcast(Block block)
    {
        BlockchainServer.BroadcastToPeers(Node.CurrentNodeIPs,
            "PushServers", string.Join(",", Node.CurrentNodeIPs).TrimEnd(','));
        BlockchainServer.BroadcastToPeers(Node.CurrentNodeIPs,
            "NewBlock", block.ToBase64());
    }

    /// <summary>
    ///     Attempts to reach consensus for the provided smart contract across the network.
    /// </summary>
    /// <param name="contract">The smart contract to validate and reach consensus on.</param>
    /// <returns>True if consensus is reached; otherwise, false.</returns>
    private async Task<bool> ReachCodeConsensus(SmartContract? contract)
    {
        try
        {
            if (contract != null)
            {
                if (Node.CurrentNodeIPs.Count == 0)
                {
                    Logger.Log($"ERROR: No validator adresses found for: {contract.Name}");
                    return false;
                }

                Logger.Log($"Starting Snowman-Consensus for contract: {contract.Name}");
                var requiredVotes = Node.CurrentNodeIPs.Count / 2 + 1;

                var voteTasks = new List<Task<bool>>();

                foreach (var validator in Node.CurrentNodeIPs)
                    if (!NetworkUtils.IP.Contains(validator))
                        voteTasks.Add(SendCodeForVerificationAsync(validator, contract));

                var results = await Task.WhenAll(voteTasks);

                var positiveVotes = results.Count(result => result);

                Logger.Log(positiveVotes >= requiredVotes
                    ? $"Consensus reached for {contract.Name}. Code is safe."
                    : $"No consensus reached for contract {contract.Name}. Code discarded.");

                return positiveVotes >= requiredVotes;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error during consensus: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Sends the serialized code of a smart contract to a server for verification.
    /// </summary>
    /// <param name="serverAddress">The address of the server for verification.</param>
    /// <param name="contract">The smart contract to be verified.</param>
    /// <returns>A boolean indicating whether the code verification was successful.</returns>
    private async Task<bool> SendCodeForVerificationAsync(string serverAddress, SmartContract? contract)
    {
        try
        {
            if (contract != null)
            {
                var message = $"VerifyCode:{contract.SerializedContractCode}";
                var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync(message);
                if (Config.Default.Debug)
                {
                    Logger.Log($"Code {contract.Name} sent to {serverAddress} for verification.");
                    Logger.Log($"Response from server for code {contract.Name}: {response}", false);
                }

                return response == "ok";
            }

            Logger.Log($"ERROR: sending code to {serverAddress} failed: contract is empty");
        }
        catch (Exception ex)
        {
            if (contract != null)
                Logger.Log($"ERROR: sending code {contract.Name} to {serverAddress} failed: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    ///     Validates the entire blockchain by checking the hashes and links between blocks.
    /// </summary>
    /// <returns>True if the blockchain is valid; otherwise, false.</returns>
    public bool ValidateChain()
    {
        if (Chain != null)
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
        if (Chain==null)
            return;

        if (toBlock == -1 || toBlock >= Chain.Count) toBlock = Chain.Count - 1;

        Logger.Log("------------------- CHAIN INFO -------------------");
        Logger.Log("Listing all blocks and their transactions:");

        for (var i = fromBlock; i <= toBlock; i++)
        {
            var block = Chain[i];
            Logger.Log("------------------- BLOCK -------------------");
            Logger.Log($"Block Index: {i}");
            Logger.Log($"Timestamp: {block.Timestamp}");
            Logger.Log($"Hash: {block.Hash}");
            Logger.Log($"Previous Hash: {block.PreviousHash}");
            Logger.Log($"Nonce: {block.Nonce}");

            if (showTransactions)
                foreach (var transaction in block.Transactions)
                {
                    Logger.Log("---------------- TRANSACTION ----------------");
                    Logger.Log($"Sender: {transaction.Sender}");
                    Logger.Log($"Recipient: {transaction.Recipient}");
                    if (transaction.Amount > 0)
                        Logger.Log($"Amount: {transaction.Amount}");
                    if (!string.IsNullOrEmpty(transaction.Data))
                        Logger.Log($"Data: {transaction.Data}");
                    if (!string.IsNullOrEmpty(transaction.Info))
                        Logger.Log($"Info: {transaction.Info}");
                    if (transaction.Gas > 0)
                        Logger.Log($"Gas: {transaction.Gas}");
                    Logger.Log($"Timestamp: {transaction.Timestamp}");
                    Logger.Log($"Signature: {transaction.Signature}");
                    Logger.Log($"Version: {transaction.Version}");
                    Logger.Log($"TransactionType: {transaction.TransactionType}");
                }
        }

        Logger.Log("------------------- END CHAIN INFO -------------------");
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
            UpdateBalancesFromChain(this);

            if (Chain != null)
                foreach (var unused in Chain)
                foreach (var (address, balance) in Balances)
                    balances.TryAdd(address, balance);
        }
        catch (Exception e)
        {
            Logger.Log($"GetAllBalancesFromChain error: {e.Message}");
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
            Logger.Log($"Error loading the blockchain: {ex.Message}");
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
            var chainId = chainElement.GetProperty("ChainId").GetString();

            if (minerAddress != null && chainId != null)
            {
                var blockchain = new Blockchain(minerAddress, chainId, difficulty);

                blockchain.Chain = JsonSerializer.Deserialize<List<Block>>(innerChainElement.GetRawText());

                blockchain.PendingTransactions =
                    JsonSerializer.Deserialize<List<Transaction>>(root.GetProperty("PendingTransactions").GetRawText());

                if (root.TryGetProperty("Balances", out var balancesJson))
                {
                    var balances = JsonSerializer.Deserialize<Dictionary<string, double>>(balancesJson.GetRawText());
                    if (balances != null)
                        foreach (var balance in balances)
                            Balances[balance.Key] = balance.Value;
                }

                if (root.TryGetProperty("Allowances", out var allowancesJson))
                {
                    var allowances =
                        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(
                            allowancesJson.GetRawText());
                    if (allowances != null)
                        foreach (var allowance in allowances)
                            Allowances[allowance.Key] = allowance.Value;
                }

                if (root.TryGetProperty("AuthenticatedUsers", out var authenticatedUsersJson))
                {
                    var authenticatedUsers =
                        JsonSerializer.Deserialize<Dictionary<string, string>>(authenticatedUsersJson.GetRawText());
                    if (authenticatedUsers != null)
                        foreach (var user in authenticatedUsers)
                            AuthenticatedUsers[user.Key] = user.Value;
                }

                Logger.Log("Blockchain loaded successfully.");
                return blockchain;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error loading the blockchain: {ex.Message}");
            throw;
        }

        return null;
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
            Balances,
            Allowances,
            AuthenticatedUsers
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
            Logger.Log($"Blockchain saved to: {file}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error saving the blockchain: {ex.Message}");
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
        if (Chain != null)
            Parallel.For(1, Chain.Count, (i, state) =>
            {
                var currentBlock = Chain[i];
                var previousBlock = Chain[i - 1];

                // Check if the current block's hash is valid
                if (currentBlock.Hash != currentBlock.CalculateHash())
                {
                    Logger.Log($"ERROR: Block {i} has an invalid hash.");
                    isValid = false;
                    state.Stop(); // Stop further iterations if invalid
                }

                // Check if the previous hash in the current block matches the hash of the previous block
                if (currentBlock.PreviousHash != previousBlock.Hash)
                {
                    Logger.Log($"ERROR: Block {i} has an invalid previous hash.");
                    isValid = false;
                    state.Stop(); // Stop further iterations if invalid
                }
            });

        if (isValid) Logger.Log("Blockchain integrity verified successfully.");

        return isValid;
    }

    #region Index

    private readonly Dictionary<string, List<Transaction>> _transactionIndex = new();
    private const int MaxTransactionsPerAddress = 1000;
    private const string ArchiveFilePath = "transaction_archive.json";

    /// <summary>
    ///     Indexes all transactions from a given block for efficient lookup.
    /// </summary>
    /// <param name="block">The block containing transactions to index.</param>
    private void IndexTransactions(Block block)
    {
        foreach (var transaction in block.Transactions) IndexTransaction(transaction);

        Logger.Log("Transactions indexed for block: " + block.Hash);
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

    /// <summary>
    ///     Archives old transactions for a specific address to maintain the size of the transaction index.
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

            if (archiveData != null)
            {
                if (!archiveData.ContainsKey(address))
                    archiveData[address] = new List<Transaction>();
                archiveData[address].AddRange(transactionsToArchive);

                File.WriteAllText(ArchiveFilePath, JsonSerializer.Serialize(archiveData));
            }

            Logger.Log($"Archived {transactionsToArchive.Count} transactions for address {address}.");
        }
        catch (Exception ex)
        {
            Logger.Log($"ERROR: archiving transactions for {address} failed: {ex.Message}");
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
            Logger.Log($"ERROR: loading archived transactions for {address} failed: {ex.Message}");
        }

        return transactions;
    }

    #endregion
}