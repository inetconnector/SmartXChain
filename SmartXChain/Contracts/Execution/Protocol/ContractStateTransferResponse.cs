using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

public sealed class ContractStateTransferResponse
{
    [JsonPropertyName("success")] public required bool Success { get; init; }

    [JsonPropertyName("state")] public string? SerializedState { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }
}
