//Validator A registriert sich beim Server.
//Node B sendet eine Transaktion an den Validator.
//Validator A validiert die Transaktion und fordert andere Validatoren zum Konsens auf.
//Nach erfolgreichem Konsens fügt Validator A die Transaktion einem neuen Block hinzu.
//Der neue Block wird an die Blockchain der Nodes verteilt.

using SmartXChain.BlockchainCore;

namespace SmartXChain.Validators;

public class SnowmanConsensus
{
    private readonly Node _node;
    private readonly int _reward;

    public SnowmanConsensus(int reward, Node node)
    {
        _reward = reward;
        _node = node;
    }

    public async Task<(bool, List<string>)> ReachConsensus(Block block)
    {
        Console.WriteLine($"Starting Snowman-Consensus for block: {block.Hash}");

        var selectionSize = Math.Max(1, Node.CurrentNodeIPs.Count / 2); // min 50% of the nodes
        var selectedValidators = Node.CurrentNodeIPs
            .OrderBy(_ => Guid.NewGuid()) // random order
            .Take(selectionSize) // take first N Nodes
            .ToList();

        Console.WriteLine($"Selected {selectionSize} validators from {Node.CurrentNodeIPs.Count} available nodes.");

        var requiredVotes = selectionSize / 2 + 1;

        var voteTasks = new List<Task<(bool, string)>>();

        foreach (var validator in selectedValidators)
            voteTasks.Add(_node.SendVoteRequestAsync(validator, block));

        var results = await Task.WhenAll(voteTasks);

        var positiveVotes = results.Count(result => result.Item1 && result.Item2.StartsWith("ok#"));

        Console.WriteLine(positiveVotes >= requiredVotes
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
}