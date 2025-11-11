using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Represents the response returned after executing a contract inside the sandbox.
/// </summary>
public sealed class ContractExecutionResponse
{
    /// <summary>
    ///     Gets a value indicating whether the execution completed successfully.
    /// </summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>
    ///     Gets the textual result returned by the contract.
    /// </summary>
    [JsonPropertyName("result")]
    public string? Result { get; init; }

    /// <summary>
    ///     Gets the serialized state produced by the execution.
    /// </summary>
    [JsonPropertyName("state")]
    public string? SerializedState { get; init; }

    /// <summary>
    ///     Gets details about an error encountered during execution, if any.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

