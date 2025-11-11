using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Represents a request to update the sandbox with the current serialized contract state.
/// </summary>
public sealed class ContractStateTransferRequest
{
    /// <summary>
    ///     Gets the serialized state being transferred to the sandbox.
    /// </summary>
    [JsonPropertyName("state")]
    public required string SerializedState { get; init; }
}