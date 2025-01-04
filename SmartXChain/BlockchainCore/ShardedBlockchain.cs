using System.IO.Compression;
using System.Text.Json;
using SmartXChain.Utils;

namespace SmartXChain.BlockchainCore;

/// <summary>
///     Represents a sharded blockchain implementation, enabling distribution of transactions
///     across multiple shards for scalability.
/// </summary>
public class ShardedBlockchain : Blockchain
{
    private const int ShardCount = 4; // Number of shards in the blockchain
    private readonly List<Blockchain>[] _shards; // Array of blockchain shards

    /// <summary>
    ///     Initializes a new instance of the <see cref="ShardedBlockchain" /> class.
    /// </summary>
    /// <param name="chainId">The chainId of the blockchain.</param>
    /// <param name="difficulty">The difficulty level for mining blocks.</param>
    /// <param name="minerAddress">The address of the miner.</param>
    public ShardedBlockchain(string minerAddress, string chainId, int difficulty = 0)
        : base(minerAddress, chainId, difficulty)
    {
        // Initialize shards with separate blockchains
        _shards = new List<Blockchain>[ShardCount];
        for (var i = 0; i < ShardCount; i++)
            _shards[i] = new List<Blockchain> { new(minerAddress, chainId, difficulty) };
    }

    /// <summary>
    ///     Adds a transaction to the appropriate shard based on its calculated shard index.
    /// </summary>
    /// <param name="transaction">The transaction to be added.</param>
    /// <returns>True if the transaction was successfully added; otherwise, false.</returns>
    public Task<bool> AddTransaction(Transaction transaction)
    {
        var shardIndex = GetShardIndex(transaction);
        return _shards[shardIndex][0].AddTransaction(transaction);
    }

    /// <summary>
    ///     Retrieves all blocks from the specified shard.
    /// </summary>
    /// <param name="shardIndex">The index of the shard to retrieve blocks from.</param>
    /// <returns>A list of blocks from the specified shard.</returns>
    /// <exception cref="ArgumentException">Thrown if the shard index is invalid.</exception>
    public List<Block>? GetBlocksFromShard(int shardIndex)
    {
        if (shardIndex < 0 || shardIndex >= ShardCount)
            throw new ArgumentException("Invalid shard index");

        return _shards[shardIndex][0].Chain;
    }

    /// <summary>
    ///     Validates the integrity of the blockchain, including archived blocks stored in a specified path.
    /// </summary>
    /// <param name="archivePath">The path to the directory containing archived blocks.</param>
    /// <returns>True if the blockchain and archived blocks are valid; otherwise, false.</returns>
    public bool ValidateChainWithArchive(string archivePath)
    {
        Block? previousBlock = null;

        // Validate current chain
        foreach (var block in Chain)
        {
            if (previousBlock != null && block.PreviousHash != previousBlock.Hash)
            {
                Logger.Log(
                    $"Validation failed: Block {block.Hash} does not match previous block {previousBlock.Hash}.");
                return false;
            }

            if (block.Hash != block.CalculateHash())
            {
                Logger.Log($"Validation failed: Block {block.Hash} hash is invalid.");
                return false;
            }

            previousBlock = block;
        }

        // Validate archived blocks if archive path exists
        if (Directory.Exists(archivePath))
        {
            var archivedFiles = Directory.GetFiles(archivePath, "*.gz").OrderBy(f => f);
            foreach (var filePath in archivedFiles)
            {
                var archivedBlock = LoadArchivedBlock(filePath);

                if (previousBlock != null && archivedBlock.PreviousHash != previousBlock.Hash)
                {
                    Logger.Log(
                        $"Validation failed: Archived block {archivedBlock.Hash} does not match previous block {previousBlock.Hash}.");
                    return false;
                }

                if (archivedBlock.Hash != archivedBlock.CalculateHash())
                {
                    Logger.Log($"Validation failed: Archived block {archivedBlock.Hash} hash is invalid.");
                    return false;
                }

                previousBlock = archivedBlock;
            }
        }

        Logger.Log("Blockchain validated successfully.");
        return true;
    }

    /// <summary>
    ///     Calculates the shard index for a transaction using its hash.
    /// </summary>
    /// <param name="transaction">The transaction to calculate the shard index for.</param>
    /// <returns>The shard index for the transaction.</returns>
    private int GetShardIndex(Transaction transaction)
    {
        var hash = transaction.CalculateHash();
        return Math.Abs(hash.GetHashCode()) % ShardCount;
    }

    /// <summary>
    ///     Loads an archived block from a compressed file.
    /// </summary>
    /// <param name="filePath">The path to the compressed file.</param>
    /// <returns>The deserialized <see cref="Block" /> object.</returns>
    private Block? LoadArchivedBlock(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open);
        using var decompressionStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressionStream);
        var blockData = reader.ReadToEnd();
        return JsonSerializer.Deserialize<Block>(blockData);
    }
}