using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Represents the response produced after transferring state to the sandbox.
/// </summary>
public sealed class ContractStateTransferResponse
{
    /// <summary>
    ///     Gets a value indicating whether the state transfer was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>
    ///     Gets the normalized state returned by the sandbox, if available.
    /// </summary>
    [JsonPropertyName("state")]
    public string? SerializedState { get; init; }

    /// <summary>
    ///     Gets the error message returned when the transfer fails.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

