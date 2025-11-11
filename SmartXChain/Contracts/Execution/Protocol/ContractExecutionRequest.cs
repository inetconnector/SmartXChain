using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

public sealed class ContractExecutionRequest
{
    [JsonPropertyName("inputs")] public required string[] Inputs { get; init; }

    [JsonPropertyName("state")] public required string SerializedState { get; init; }
}
