using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

public sealed class ContractCompilationRequest
{
    [JsonPropertyName("code")] public required string Code { get; init; }
}
