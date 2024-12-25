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


    public async Task<bool> ReachConsensus(Block block)
    {
        Console.WriteLine($"Starting Snowman-Consensus for block: {block.Hash}");

        var requiredVotes = Node.CurrentNodeIPs.Count / 2 + 1;

        var voteTasks = new List<Task<(bool, string)>>();

        foreach (var validator in Node.CurrentNodeIPs) voteTasks.Add(_node.SendVoteRequestAsync(validator, block));
        var results = await Task.WhenAll(voteTasks);

        var positiveVotes = results.Count(result => result.Item1 && result.Item2 == "OK");

        Console.WriteLine(positiveVotes >= requiredVotes
            ? "consensus reached. Block validated."
            : "No consensus reached. Block discharge.");

        return positiveVotes >= requiredVotes;
    }
}