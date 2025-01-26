using System;
using System.Collections.Generic;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;
using XamarinBlockchainApp.Database;

namespace XamarinBlockchainApp.Services
{
    public class TransactionService
    {
        private readonly string _blockchainPath;
        private readonly string _chainId;
        private readonly string _minerAddress;

        public TransactionService()
        {
            var config = Config.Default;
            _blockchainPath = config.Get(Config.ConfigKey.BlockchainPath);
            _chainId = config.Get(Config.ConfigKey.ChainId);
            _minerAddress = config.Get(Config.ConfigKey.MinerAddress);
        }

        public IEnumerable<Transaction> GetAllTransactions()
        {
            var transactions = new List<Transaction>();

            try
            {
                using (var node = BlockchainServer.NodeStartupResult.Create(_blockchainPath, _chainId, _minerAddress))
                {
                    foreach (var block in node.GetAllBlocks())
                    {
                        foreach (var tx in block.Transactions)
                        {
                            transactions.Add(new Transaction
                            {
                                Id = 0,
                                TransactionType = tx.TransactionType,
                                BlockHash = block.Hash,
                                Sender = tx.Sender,
                                Recipient = tx.Recipient,
                                Amount = tx.Amount,
                                Timestamp = tx.Timestamp,
                                Data = tx.Data,
                                Info = tx.Info
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching transactions: {ex.Message}");
            }

            return transactions;
        }

        public void AddTransaction(Transaction transaction)
        {
            try
            {
                using (var node = BlockchainServer.NodeStartupResult.Create(_blockchainPath, _chainId, _minerAddress))
                {
                    var txResult = node.SendNativeSCX(transaction.Sender, transaction.Recipient, transaction.Amount);

                    if (!txResult.IsSuccess)
                    {
                        throw new Exception($"Failed to add transaction: {txResult.ErrorMessage}");
                    }

                    Console.WriteLine($"Transaction added successfully: {txResult.TransactionHash}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding transaction: {ex.Message}");
            }
        }
    }

    public class Transaction
    {
        public int Id { get; set; }
        public int TransactionType { get; set; }
        public string BlockHash { get; set; }
        public string Sender { get; set; }
        public string Recipient { get; set; }
        public decimal Amount { get; set; }
        public DateTime Timestamp { get; set; }
        public string Data { get; set; }
        public string Info { get; set; }
    }
}
