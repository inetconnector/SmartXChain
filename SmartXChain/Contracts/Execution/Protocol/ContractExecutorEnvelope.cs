using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Envelope message exchanged with the sandboxed host process. Each message contains a type discriminator
///     and an arbitrary payload serialized as JSON.
/// </summary>
public sealed class ContractExecutorEnvelope
{
    /// <summary>
    ///     Gets the discriminator describing the payload content.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    ///     Gets the serialized payload delivered to or from the sandbox.
    /// </summary>
    [JsonPropertyName("payload")]
    public required object Payload { get; init; }
}