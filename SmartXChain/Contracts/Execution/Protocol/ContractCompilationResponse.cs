using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

public sealed class ContractCompilationResponse
{
    [JsonPropertyName("success")] public required bool Success { get; init; }

    [JsonPropertyName("sessionId")] public string? SessionId { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }
}
