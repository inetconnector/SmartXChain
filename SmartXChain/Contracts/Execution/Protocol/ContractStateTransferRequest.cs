using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

public sealed class ContractStateTransferRequest
{
    [JsonPropertyName("state")] public required string SerializedState { get; init; }
}
