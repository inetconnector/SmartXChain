using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Envelope message exchanged with the sandboxed host process. Each message contains a type discriminator
///     and an arbitrary payload serialized as JSON.
/// </summary>
public sealed class ContractExecutorEnvelope
{
    [JsonPropertyName("type")] public required string Type { get; init; }

    [JsonPropertyName("payload")] public required object Payload { get; init; }
}
