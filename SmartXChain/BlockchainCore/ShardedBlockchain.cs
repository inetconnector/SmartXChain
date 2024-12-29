using System.IO.Compression;
using System.Text.Json;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

public class ShardedBlockchain : Blockchain
{
    private const int ShardCount = 4;
    private readonly List<Blockchain>[] _shards;

    public ShardedBlockchain(int difficulty, string minerAddress)
        : base(difficulty, minerAddress)
    {
        _shards = new List<Blockchain>[ShardCount];
        for (var i = 0; i < ShardCount; i++) _shards[i] = new List<Blockchain> { new(difficulty, minerAddress) };
    }

    public bool AddTransaction(Transaction transaction)
    {
        var shardIndex = GetShardIndex(transaction);
        return _shards[shardIndex][0].AddTransaction(transaction);
    }

    public List<Block> GetBlocksFromShard(int shardIndex)
    {
        if (shardIndex < 0 || shardIndex >= ShardCount)
            throw new ArgumentException("Invalid shard index");

        return _shards[shardIndex][0].Chain;
    }

    private int GetShardIndex(Transaction transaction)
    {
        var hash = transaction.CalculateHash();
        return Math.Abs(hash.GetHashCode()) % ShardCount;
    }

// Archive Support for Large Blockchain
    public bool ValidateChainWithArchive(string archivePath)
    {
        Block? previousBlock = null;

        foreach (var block in Chain)
        {
            if (previousBlock != null && block.PreviousHash != previousBlock.Hash)
            {
                Logger.LogMessage(
                    $"Validation failed: Block {block.Hash} does not match previous block {previousBlock.Hash}.");
                return false;
            }

            if (block.Hash != block.CalculateHash())
            {
                Logger.LogMessage($"Validation failed: Block {block.Hash} hash is invalid.");
                return false;
            }

            previousBlock = block;
        }

        if (Directory.Exists(archivePath))
        {
            var archivedFiles = Directory.GetFiles(archivePath, "*.gz").OrderBy(f => f);
            foreach (var filePath in archivedFiles)
            {
                var archivedBlock = LoadArchivedBlock(filePath);

                if (previousBlock != null && archivedBlock.PreviousHash != previousBlock.Hash)
                {
                    Logger.LogMessage(
                        $"Validation failed: Archived block {archivedBlock.Hash} does not match previous block {previousBlock.Hash}.");
                    return false;
                }

                if (archivedBlock.Hash != archivedBlock.CalculateHash())
                {
                    Logger.LogMessage($"Validation failed: Archived block {archivedBlock.Hash} hash is invalid.");
                    return false;
                }

                previousBlock = archivedBlock;
            }
        }

        Logger.LogMessage("Blockchain validated successfully.");
        return true;
    }

    private Block LoadArchivedBlock(string filePath)
    {
        using (var fileStream = new FileStream(filePath, FileMode.Open))
        using (var decompressionStream = new GZipStream(fileStream, CompressionMode.Decompress))
        using (var reader = new StreamReader(decompressionStream))
        {
            var blockData = reader.ReadToEnd();
            return JsonSerializer.Deserialize<Block>(blockData);
        }
    }
}