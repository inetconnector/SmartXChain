namespace SmartXChain.BlockchainCore;

public class Tangle
{
    private readonly List<Block> _blocks;

    public Tangle()
    {
        _blocks = new List<Block>();
    }

    public void AddBlock(Block block)
    {
        // approve new block with 2 existing blocks
        if (_blocks.Count > 1)
        {
            var randomBlocks = _blocks
                .OrderBy(x => Guid.NewGuid())
                .Take(2) // take 2 random blocks
                .ToList();

            foreach (var randomBlock in randomBlocks)
                randomBlock.Approves.Add(randomBlock.CalculateHash());
        }

        _blocks.Add(block);
        Console.WriteLine($"Block added: {block.CalculateHash()}");
        Console.WriteLine($"Approved: {string.Join(", ", block.Approves)}");
    }

    public void PrintTangle()
    {
        Console.WriteLine("\n--- actual tangle ---");
        foreach (var tx in _blocks)
        {
            Console.WriteLine($"ID: {tx.CalculateHash()}");
            Console.WriteLine($"Bestätigt: {string.Join(", ", tx.Approves)}");
            foreach (var transaction in tx.Transactions)
            {
                Console.WriteLine($"Data: {transaction.ToString()}");
            }
       
            Console.WriteLine();
        }
    }
}