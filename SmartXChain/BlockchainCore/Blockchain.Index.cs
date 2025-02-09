//using System.Collections.Concurrent;
//using System.Text.Json;
//using SmartXChain.Utils;

//namespace SmartXChain.BlockchainCore;

//public partial class Blockchain
//{
//    #region Index

//    private readonly ConcurrentDictionary<string, List<Transaction>> _transactionIndex = new();
//    private const int MaxTransactionsPerAddress = 1000;
//    private readonly string _archiveFilePath = Path.Combine(Config.Default.BlockchainPath, "transaction_archive.json");

//    /// <summary>
//    ///     Indexes all transactions from a given block for efficient lookup.
//    /// </summary>
//    /// <param name="block">The block containing transactions to index.</param>
//    private void IndexTransactions(Blocks? block)
//    {
//        if (block != null)
//        {
//            foreach (var transaction in block.Transactions) IndexTransaction(transaction);

//            Logger.Log("Transactions indexed for block: " + block.Hash);
//        }
//    }

//    /// <summary>
//    ///     Indexes a single transaction by updating the internal transaction index.
//    ///     Ensures the sender and recipient addresses are included in the index.
//    /// </summary>
//    /// <param name="transaction">The transaction to be indexed.</param>
//    private void IndexTransaction(Transaction transaction)
//    {
//        void AddToIndex(string key, Transaction tx)
//        {
//            if (!_transactionIndex.ContainsKey(key)) _transactionIndex[key] = new List<Transaction>();

//            lock (_transactionIndex[key])
//            {
//                _transactionIndex[key].Add(tx);
//            }

//            // Maintain the max size limit for the index
//            if (_transactionIndex[key].Count > MaxTransactionsPerAddress) ArchiveOldTransactions(key);
//        }

//        AddToIndex(transaction.Sender, transaction);
//        AddToIndex(transaction.Recipient, transaction);
//    }

//    /// <summary>
//    ///     Archives old transactions for a specific address to maintain the size of the transaction index.
//    /// </summary>
//    /// <param name="address">The address whose transactions need to be archived.</param>
//    private void ArchiveOldTransactions(string address)
//    {
//        var transactions = _transactionIndex[address];
//        lock (transactions)
//        {
//            var transactionsToArchive = transactions.Take(transactions.Count - MaxTransactionsPerAddress).ToList();
//            transactions.RemoveRange(0, transactionsToArchive.Count);

//            try
//            {
//                var archiveData = File.Exists(_archiveFilePath)
//                    ? JsonSerializer.Deserialize<Dictionary<string, List<Transaction>>>(
//                        File.ReadAllText(_archiveFilePath))
//                    : new Dictionary<string, List<Transaction>>();

//                if (archiveData != null)
//                {
//                    if (!archiveData.ContainsKey(address))
//                        archiveData[address] = new List<Transaction>();
//                    archiveData[address].AddRange(transactionsToArchive);

//                    File.WriteAllText(_archiveFilePath, JsonSerializer.Serialize(archiveData));
//                }

//                Logger.Log($"Archived {transactionsToArchive.Count} transactions for address {address}.");
//            }
//            catch (Exception ex)
//            {
//                Logger.LogException(ex, $"archiving transactions for {address} failed");
//            }
//        }
//    }

//    /// <summary>
//    ///     Retrieves a list of transactions associated with a specific address from the blockchain.
//    /// </summary>
//    /// <param name="address">The address for which to retrieve transactions.</param>
//    /// <returns>A list of transactions involving the specified address.</returns>
//    public List<Transaction> GetTransactionsByAddress(string address)
//    {
//        var transactions = new List<Transaction>();

//        if (_transactionIndex.TryGetValue(address, out var indexedTransactions))
//            transactions.AddRange(indexedTransactions);

//        try
//        {
//            if (File.Exists(_archiveFilePath))
//            {
//                var archiveData =
//                    JsonSerializer.Deserialize<Dictionary<string, List<Transaction>>>(
//                        File.ReadAllText(_archiveFilePath));
//                if (archiveData != null && archiveData.TryGetValue(address, out var archivedTransactions))
//                    transactions.AddRange(archivedTransactions);
//            }
//        }
//        catch (Exception ex)
//        {
//            Logger.LogException(ex, $"loading archived transactions for {address} failed");
//        }

//        return transactions;
//    }

//    #endregion
//}

