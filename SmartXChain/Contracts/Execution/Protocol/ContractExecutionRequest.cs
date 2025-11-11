using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Describes a request to execute a smart contract within the sandbox.
/// </summary>
public sealed class ContractExecutionRequest
{
    /// <summary>
    ///     Gets the input parameters provided to the contract entry point.
    /// </summary>
    [JsonPropertyName("inputs")]
    public required string[] Inputs { get; init; }

    /// <summary>
    ///     Gets the serialized representation of the current contract state.
    /// </summary>
    [JsonPropertyName("state")]
    public required string SerializedState { get; init; }
}