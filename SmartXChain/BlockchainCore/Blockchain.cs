using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using static SmartXChain.BlockchainCore.Transaction;
using static SmartXChain.Server.BlockchainServer;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     SmartX Blockchain
/// </summary>
public partial class Blockchain
{
    public const string SystemAddress = "smartX0000000000000000000000000000000000000000";
    public const string UnknownAddress = "smartXFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF";

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

        AddBlock(GenesisBlock(minerAdress));
        Blockchains.Add(this);
    }

    public static List<Blockchain> Blockchains { get; } = new();
     
    [JsonInclude] internal string ChainId { get; }
    [JsonInclude] internal string MinerAdress { get; } 
    [JsonInclude] public List<Block>? Chain { get; private set; } 
    [JsonInclude] internal List<Transaction>? PendingTransactions { get; private set; } 
    [JsonInclude] internal static decimal CurrentNetworkLoad { get; private set; } = (decimal).5;

    public IReadOnlyDictionary<string, SmartContract?> SmartContracts
    {
        get
        {
            var contracts = new ConcurrentDictionary<string, SmartContract?>();

            if (Chain != null)
                foreach (var block in Chain)
                foreach (var kvp in block.SmartContracts)
                    contracts.TryAdd(kvp.Key, kvp.Value);

            return contracts;
        }
    }

    /// <summary>
    ///     Creates the genesis block, which serves as the initial block in the blockchain.
    /// </summary>
    private Block? GenesisBlock(string minerAdress)
    {
        ;
        var genesisTransactions = new List<Transaction>();

        // Publish the server's IP address in a transaction on the blockchain
        var serverTransaction = new Transaction
        {
            Sender = SystemAddress,
            Recipient = minerAdress,
            Data = Convert.ToBase64String(Encoding.ASCII.GetBytes(Config.Default.URL)), // Store data as Base64 string
            Info = "IP",
            Timestamp = DateTime.UtcNow,
            TransactionType = TransactionTypes.Server
        };
        genesisTransactions.Add(serverTransaction);

        //publish gas settings in a transaction on the blockchain
        var gasTransaction = new Transaction
        {
            Sender = SystemAddress,
            Recipient = minerAdress,
            Data = GasConfiguration.Instance.ToBase64String(),
            Info = "GasConfiguration",
            Timestamp = DateTime.UtcNow,
            TransactionType = TransactionTypes.GasConfiguration
        };
        genesisTransactions.Add(gasTransaction);

        //settle system gas to blockchain
        var rewardTransaction = new RewardTransaction(this, minerAdress)
        {
            Amount = TotalSupply / 2
        };
        Balances[minerAdress] = rewardTransaction.Amount;
        genesisTransactions.Add(rewardTransaction);

        var genesisBlock = new Block(genesisTransactions, "0") { Nonce = -1 };

        var hash = genesisBlock.CalculateHash();
        genesisBlock.Approves.Add(hash);

        return genesisBlock;
    }

    /// <summary>
    ///     Adds a block to the blockchain with optional parameters for locking the chain and mining the block.
    /// </summary>
    internal bool AddBlock(Block? block, bool mineBlock = true, int? index = null)
    {
        if (block == null)
        {
            Logger.LogError("Block not added to chain. Block is null.");
            return false;
        }

        lock (block)
        {
            if (mineBlock)
            {
                block.Mine(_difficulty);
            }

            if (Chain != null)
                lock (Chain)
                {
                    if (index.HasValue && index.Value <= Chain.Count)
                    {
                        if (index.Value < 0)
                        {
                            Logger.LogError("Invalid index for adding block.");
                            return false;
                        }

                        if (index.Value > 0 && Chain[index.Value - 1].Hash != block.PreviousHash)
                        {
                            Logger.LogError("Block not added to chain -- invalid previous block for given index.");
                            return false;
                        }

                        if (index.Value < Chain.Count && Chain[index.Value].PreviousHash != block.Hash)
                        {
                            Logger.LogError("Block not added to chain -- would break chain consistency.");
                            return false;
                        }

                        Chain.Insert(index.Value, block);
                    }
                    else
                    {
                        
                        if (Chain.Count > 0 && Chain.Last().Hash != block.PreviousHash)
                        {  
                            Logger.LogError("Block not added to chain -- invalid previous block.");
                            return false;
                        }

                        Chain.Add(block);
                    }
                }
        }

        Logger.Log("Block added to chain successfully: " + block.Hash);

        // Optimize: Index transactions for quick lookup
        //IndexTransactions(block);
        return BlockchainStorage.SaveBlock(block, Config.Default.BlockchainPath, ChainId);
    }

    /// <summary>
    ///     pay gas fees by a transaction sender to system
    /// </summary>
    private async Task<bool> PayGas(string type, string sender, decimal gas)
    {
        if (gas > 0 && sender != SystemAddress)
        {
            Balances.TryGetValue(sender, out var balance);
            Logger.Log($"[Gas] {sender} has to pay {gas} for {type}");

            Balances.TryGetValue(sender, out balance);
            if (balance - gas < 0)
            {
                Logger.LogError($"No gas available for {sender} to pay {gas} for {type}");
                return false;
            }

            var payGasTransaction = new Transaction
            {
                Sender = sender,
                Recipient = SystemAddress,
                Amount = gas,
                Timestamp = DateTime.UtcNow,
                Info = type,
                TransactionType = TransactionTypes.Gas
            };

            var success = await AddTransaction(payGasTransaction);
            if (!success)
                Logger.LogError($"Pay {gas} gas failed");

            if (Balances[sender] - gas < 0)
                return false;

            Balances[sender] -= gas;
            Balances.TryAdd(SystemAddress, 0);
            Balances[SystemAddress] += gas;
        }

        return true;
    }


    /// <summary>
    ///     Checks if an address has already received a Founder Reward.
    /// </summary>
    /// <param name="address">The address to verify.</param>
    /// <returns>True if the address has received a Founder Reward; otherwise, false.</returns>
    public bool HasReceivedFounderReward(string address)
    {
        if (Chain == null || string.IsNullOrEmpty(address)) return false;

        foreach (var block in Chain)
        foreach (var transaction in block.Transactions)
            if (transaction.Recipient == address && transaction.TransactionType == TransactionTypes.Founder)
                return true;

        return false;
    }

    internal async Task SettleFounder(string sender)
    {
        // Settle Founders - Ensure rewards are distributed based on founder index
        const int initialReward = 10_000_000;
        const int maxFounders = 300_000_000 / initialReward; // Calculate maximum founders based on total reward pool
        int reward;
        var founderIndex = Balances.Count;

        if (founderIndex == 1)
            // First founder gets the full initial reward
            reward = initialReward;
        else if (founderIndex <= 10)
            // First 10 founders get the full initial reward
            reward = initialReward;
        else if (founderIndex <= maxFounders)
            // Founders beyond the first 10 get rewards decreasing logarithmically
            reward = (int)(initialReward / Math.Log(founderIndex));
        else
            // No reward for founders beyond the maximum
            reward = 0;

        if (reward > 0 && Balances[SystemAddress] >= reward)
        {
            if (!HasReceivedFounderReward(sender))
            {
                var founderTransaction = new Transaction
                {
                    Sender = SystemAddress,
                    Recipient = sender,
                    Amount = reward,
                    Timestamp = DateTime.UtcNow,
                    TransactionType = TransactionTypes.Founder
                };

                var success = await AddTransaction(founderTransaction, true);
                if (!success)
                    Logger.LogError($"Founder {sender} reward of {founderIndex}.{reward} SCX failed");
                else
                    Logger.Log($"Founder {sender} reward of {founderIndex}.{reward} SCX success");
            }
        }
        else if (reward > 0)
        {
            Logger.LogError($"Insufficient balance to reward founder {founderIndex} with {reward} SCX");
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

        if (gas > 0 && transaction.TransactionType != TransactionTypes.Gas &&
            transaction.TransactionType != TransactionTypes.Founder)
            if (!await PayGas("Transaction", transaction.Sender, gas))
                return false;

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
        if (chain == null)
        {
            Logger.LogError("Blockchain instance is null.");
            return false;
        }

        if (contract == null)
        {
            Logger.LogError("SmartContract instance is null.");
            return false;
        }

        if (!Regex.IsMatch(contract.Name, @"^[a-zA-Z0-9]+$"))
        {
            Logger.Log(
                $"Invalid Smart Contract name '{contract.Name}'. Names can only contain alphanumeric characters (a-z, A-Z, 0-9) without spaces.");
            return false;
        }

        // Check if the contract with the same name already exists
        if (SmartContracts.ContainsKey(contract.Name))
        {
            Logger.Log($"A Smart Contract with the name '{contract.Name}' already exists in the blockchain.");
            return false;
        }

        var message = "";
        if (!CodeSecurityAnalyzer.IsCodeSafe(Serializer.DeserializeFromBase64<string>(contract.SerializedContractCode),
                ref message))
        {
            Logger.LogError("The code contains forbidden constructs and was not executed.");
            Logger.LogError($"Details: {message}");
            return false;
        }

        if (await ReachCodeConsensus(contract))
        {
            // "$$" + contract.Name in info of transaction defines a smart contract in data
            var contractTransaction = new Transaction
            {
                Sender = contract.Owner,
                Recipient = SystemAddress,
                Data = contract.SerializedContractCode,
                Info = "$$" + contract.Name,
                Timestamp = DateTime.UtcNow,
                TransactionType = TransactionTypes.ContractCode
            };

            if (!await chain.AddTransaction(contractTransaction, true))
            {
                Logger.LogError($"Smart Contract '{contract.Name}' not added to the blockchain.");
                return false;
            }
             
            Logger.Log($"Smart Contract '{contract.Name}' added to the blockchain.");
            return true;
        }

        Logger.Log($"No consensus reached for the Smart Contract '{contract.Name}'");

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
    private async Task<(bool, ConcurrentList<string>)> ReachConsensus(Block? block)
    {
        if (block != null)
        {
            Logger.Log($"Starting Snowman-Consensus for block: {block.Hash}");

            // Dynamically select the number of validators based on block size
            var selectionSize = block.Transactions.Count > 1000 ? 20 : 10; // Example: More validators for large blocks

            // If there are fewer nodes than the desired number, select all available nodes
            selectionSize = Math.Min(selectionSize, Node.CurrentNodeIPs.Count / 2);

            if (Node.CurrentNodeIPs.Count == 1)
                selectionSize = 1;

            // Select the validators (excluding own server address)
            var selectedValidators = Node.CurrentNodeIPs
                .Where(ip =>
                    !Config.Default.URL.Contains(ip) &&
                    !Config.Default.ResolvedURL.Contains(ip)) // Exclude own server URL
                .OrderBy(_ => Guid.NewGuid()) // Random selection
                .Take(selectionSize) // Select the desired number of validators
                .ToList();

            Logger.Log(
                $"Selected {selectedValidators.Count} validators from {Node.CurrentNodeIPs.Count} available nodes.");

            if (selectedValidators.Count <= 0) return (false, new ConcurrentList<string>());

            // Calculate the required number of positive votes to reach consensus
            var requiredVotes = selectedValidators.Count / 2 + 1;

            // List of tasks for sending vote requests
            var voteTasks = new List<Task<(bool, string)>>();

            foreach (var validator in selectedValidators)
            {
                if (validator.Contains(Config.Default.URL) || validator.Contains(Config.Default.ResolvedURL))
                    continue;
                voteTasks.Add(SendVoteRequestAsync(validator, block));
            }

            // Wait for all the vote results
            var results = await Task.WhenAll(voteTasks);

            // Count the number of positive votes
            var positiveVotes = results.Count(result => result.Item1 && result.Item2.StartsWith("ok#"));

            if (positiveVotes >= requiredVotes)
                Logger.Log("Consensus reached. Block validated.");
            else
                Logger.Log("No consensus reached. Block discharge.");

            // Collect reward addresses from the positive vote results
            var rewardAddresses = new ConcurrentList<string>();
            foreach (var result in results)
            {
                var sp = result.Item2.Split('#');
                if (sp.Length == 2 && sp[0] == "ok")
                    if (!rewardAddresses.Contains(sp[1]))
                        rewardAddresses.Add(sp[1]);
            }

            // Return the consensus status and the list of reward addresses
            var consensus = positiveVotes >= requiredVotes;
            return (consensus, rewardAddresses);
        }

        return (false, new ConcurrentList<string>());
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

            if (!await PayGas("Contract " + contract.Name, contract.Owner, contract.Gas))
                return ("", currentSerializedState);

            var (result, updatedSerializedState) = await contract.Execute(ModifyInput(inputs), currentSerializedState);

            // Serialize updated state back to the blockchain
            if (result == "ok")
                await WriteContractStateToBlockchain(contract, updatedSerializedState);

            Logger.Log($@"Smart Contract {contractName} Result: {result}");
            return (result, updatedSerializedState);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"executing Smart Contract '{contractName}'");
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
                                Logger.LogException(ex, "Failed to deserialize contract data");
                                return null;
                            }
                    }
            }

        Logger.LogError($"SmartContract '{contractName}' not found in blockchain transactions.");
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
            if (counter % 1000 == 0) // Every 10 seconds
                Logger.Log("Waiting for validators...");
            Thread.Sleep(10);
            counter++;
        }

        await Sync();

        if (ValidateChain())
        {
            if (Chain != null)
            {
                var retryCount = 0;
                const int maxRetries = 5;
                bool blockAdded = false;

                do
                {
                    var transactionsToMine = new List<Transaction>();

                    if (PendingTransactions != null)
                    {
                        lock (PendingTransactions)
                        {
                            if (PendingTransactions.Count == 0)
                            {
                                Logger.Log("No transactions to mine.");
                                return;
                            }

                            transactionsToMine = PendingTransactions.ToList();
                        }
                    }

                    var previousHash = Chain.Last().Hash;
                    var block = new Block(transactionsToMine, previousHash);

                    // Find consensus between validators
                    var (consensusReached, rewardAddresses) = await ReachConsensus(block);

                    if (consensusReached)
                    {
                        // Attempt to add block to chain
                        blockAdded = AddBlock(block);

                        if (blockAdded)
                        {
                            var blocks = new List<Block> { block };

                            // Broadcast the new block securely
                            _ = BroadcastBlockToPeers(Node.CurrentNodeIPs, blocks, this);

                            // Clear pending transactions
                            PendingTransactions?.Clear();

                            // Reward the miner
                            var rewardTransaction = new RewardTransaction(this, Config.Default.MinerAddress);
                            await AddTransaction(rewardTransaction);

                            // Reward the validators
                            foreach (var address in rewardAddresses)
                            {
                                if (address == minerAddress)
                                    continue;

                                rewardTransaction = new RewardTransaction(this, address, true);
                                await AddTransaction(rewardTransaction);
                                Logger.Log(
                                    $"Validator reward: {rewardTransaction.Reward} to validator {address}");
                            }

                            Logger.Log($"Block successfully mined and added to the chain. Retries: {retryCount}");
                        }
                        else
                        {
                            Logger.LogWarning("AddBlock failed. Retrying...");
                            await Sync(); // Sync chain to get the latest state
                            retryCount++;
                        }
                    }
                    else
                    {
                        Logger.LogError("Block rejected.");
                        break;
                    }
                } while (!blockAdded && retryCount < maxRetries);

                if (!blockAdded)
                {
                    Logger.LogError($"Failed to add block after {maxRetries} attempts.");
                }
            }
        }
        else
        {
            Logger.LogError("The chain is not valid.");
        }
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
                    Logger.LogError($"No validator adresses found for: {contract.Name}");
                    return false;
                }

                Logger.Log($"Starting Snowman-Consensus for contract: {contract.Name}");
                var requiredVotes = Node.CurrentNodeIPs.Count / 2;
                if (requiredVotes == 0)
                    requiredVotes = 1;

                var voteTasks = new List<Task<bool>>();

                foreach (var validator in Node.CurrentNodeIPs)
                    if (!Config.Default.URL.Contains(validator))
                        voteTasks.Add(SendCodeForVerificationAsync(validator, contract));

                var results = await Task.WhenAll(voteTasks);

                var positiveVotes = results.Count(result => result);

                if (positiveVotes >= requiredVotes)
                {
                    Logger.Log($"Consensus reached for {contract.Name}. Code is safe.");
                    return true;
                }

                Logger.Log($"No consensus reached for contract {contract.Name}. Code discarded.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "during consensus");
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
            lock (Chain)
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
        if (Chain == null)
            return;

        if (toBlock == -1 || toBlock >= Chain.Count) toBlock = Chain.Count - 1;

        Logger.LogLine("CHAIN INFO");
        Logger.Log("Listing all blocks and their transactions:");

        for (var i = fromBlock; i <= toBlock; i++)
        {
            var block = Chain[i];
            Logger.LogLine("BLOCK");
            Logger.Log($"Block Index: {i}");
            Logger.Log($"Timestamp: {block.Timestamp}");
            Logger.Log($"Hash: {block.Hash}");
            Logger.Log($"Previous Hash: {block.PreviousHash}");
            Logger.Log($"Nonce: {block.Nonce}");

            if (showTransactions)
                foreach (var transaction in block.Transactions)
                {
                    Logger.LogLine("TRANSACTION");
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

        Logger.LogLine("END CHAIN INFO");
    }

    /// <summary>
    ///     Retrieves the balance of a specific address from the blockchain by aggregating transaction data.
    /// </summary>
    /// <param name="address">The blockchain address to retrieve the balance for.</param>
    /// <returns>The balance of the address, or null if the address does not exist.</returns>
    public decimal? GetBalanceForAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be null or empty.", nameof(address));

        try
        {
            UpdateBalancesFromChain(this);

            if (Balances.TryGetValue(address, out var balance)) return balance;
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Failed to retrieve balance for address: {address}");
        }

        return null; // Return null if the address does not exist in the balances.
    }


    /// <summary>
    ///     Retrieves all account balances from the blockchain by aggregating transaction data.
    /// </summary>
    /// <returns>A dictionary mapping addresses to their respective balances.</returns>
    public Dictionary<string, decimal> GetAllBalancesFromChain()
    {
        var balances = new Dictionary<string, decimal>();
        try
        {
            UpdateBalancesFromChain(this);

            if (Chain != null)
                foreach (var unused in Chain)
                foreach (var (address, balance) in Balances)
                    balances.TryAdd(address, balance);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "Failed to GetAllBalancesFromChain");
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
            Logger.LogException(ex, "loading the blockchain");
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
                var blockchain = new Blockchain(minerAddress, chainId, difficulty)
                {
                    Chain = JsonSerializer.Deserialize<List<Block>>(innerChainElement.GetRawText()),
                    PendingTransactions =
                        JsonSerializer.Deserialize<List<Transaction>>(root.GetProperty("PendingTransactions")
                            .GetRawText())
                };

                if (root.TryGetProperty("Balances", out var balancesJson))
                {
                    var balances = JsonSerializer.Deserialize<Dictionary<string, decimal>>(balancesJson.GetRawText());
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

                Logger.Log("Blockchain loaded successfully.");
                return blockchain;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "loading the blockchain");
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
    ///     Serializes a range of blocks to a Base64 string for storage or transmission.
    /// </summary>
    /// <param name="fromBlock">The starting block index (inclusive).</param>
    /// <param name="toBlock">The ending block index (inclusive). If -1, includes all blocks from the start index.</param>
    /// <returns>A Base64 string representation of the selected blocks.</returns>
    public string GetBlocksAsBase64(int fromBlock, int toBlock)
    {
        if (Chain == null || Chain.Count == 0)
            throw new InvalidOperationException("The blockchain is empty.");

        if (fromBlock < 0 || fromBlock >= Chain.Count)
            throw new ArgumentOutOfRangeException(nameof(fromBlock), "Invalid starting block index.");

        if (toBlock == -1 || toBlock >= Chain.Count)
            toBlock = Chain.Count - 1;

        if (fromBlock > toBlock)
            throw new ArgumentException("The starting block index cannot be greater than the ending block index.");

        // Select the range of blocks
        var blocks = Chain.GetRange(fromBlock, toBlock - fromBlock + 1);

        // Serialize the selected blocks to JSON
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true
        };
        var jsonData = JsonSerializer.Serialize(blocks, options);

        // Compress and convert to Base64
        return Convert.ToBase64String(Compress.CompressString(jsonData));
    }

    /// <summary>
    ///     Decodes a list<Block>
    /// </summary>
    /// <param name="base64Data"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    public static List<Block> DecodeBlocksFromBase64(string base64Data)
    {
        if (string.IsNullOrWhiteSpace(base64Data))
            throw new ArgumentException("Base64 data cannot be null or empty.", nameof(base64Data));

        // Base64 dekodieren
        var compressedData = Convert.FromBase64String(base64Data);

        // Dekomprimieren
        var jsonData = Compress.DecompressString(compressedData);

        // Deserialisieren
        var options = new JsonSerializerOptions
        {
            IncludeFields = true
        };
        return JsonSerializer.Deserialize<List<Block>>(jsonData, options)
               ?? throw new InvalidOperationException("Failed to deserialize blocks.");
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
            Allowances
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
            Logger.LogException(ex, "saving the blockchain");
        }

        return false;
    }

    /// <summary>
    ///     Verifies the integrity of the blockchain by checking hashes and previous hashes.
    ///     If the chain contains an invalid previous hash, it will be shortened to the last valid block.
    /// </summary>
    /// <returns>True if the blockchain is valid, otherwise false.</returns>
    internal bool IsValid()
    {
        var isValid = true;
        var lastValidIndex = Chain?.Count - 1 ?? -1; // Start with the assumption that the entire chain is valid.

        if (Chain != null)
        {
            Parallel.For(1, Chain.Count, (i, state) =>
            {
                var currentBlock = Chain[i];
                var previousBlock = Chain[i - 1];

                // Check if the current block's hash is valid
                if (currentBlock.Hash != currentBlock.CalculateHash())
                {
                    Logger.LogError($"Block {i} has an invalid hash.");
                    isValid = false;
                    lastValidIndex = Math.Min(lastValidIndex, i - 1); // Update the last valid index
                    state.Stop(); // Stop further iterations if invalid
                }

                // Check if the previous hash in the current block matches the hash of the previous block
                if (currentBlock.PreviousHash != previousBlock.Hash)
                {
                    Logger.LogError($"Block {i} has an invalid previous hash.");
                    isValid = false;
                    lastValidIndex = Math.Min(lastValidIndex, i - 1); // Update the last valid index
                    state.Stop(); // Stop further iterations if invalid
                }
            });

            // If the chain has invalid blocks, truncate it to the last valid block
            if (!isValid && lastValidIndex >= 0)
            {
                Logger.LogWarning($"Truncating blockchain to the last valid block at index {lastValidIndex}.");
                Chain = Chain.Take(lastValidIndex + 1).ToList();
            }
        }

        if (isValid && Config.Default.Debug)
            Logger.Log("Blockchain integrity verified successfully.");

        return isValid;
    }

    /// <summary>
    ///     Clears the blockchain
    /// </summary>
    public void Clear()
    {
        if (Chain != null)
            lock (Chain)
            {
                Chain.Clear();
            }

        BlockchainStorage.ClearDatabase(Config.Default.BlockchainPath, ChainId);
        //_transactionIndex.Clear();
    }

    /// <summary>
    /// Retrieves all transactions associated with a specific address from the blockchain.
    /// </summary>
    /// <param name="address">The blockchain address to filter transactions for.</param>
    /// <returns>An IEnumerable containing all matching transactions.</returns>
    public IEnumerable<Transaction> GetTransactionsByAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Address cannot be null or empty.", nameof(address));

        var transactions = new List<Transaction>();

        if (Chain != null)
        {
            foreach (var block in Chain)
            {
                lock (block)
                {
                    var matchingTransactions = block.Transactions
                        .Where(t => t.Sender == address || t.Recipient == address);

                    transactions.AddRange(matchingTransactions);
                }
            }
        }

        return transactions;
    }
}
