using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Represents the response emitted by the contract executor after attempting to compile code.
/// </summary>
public sealed class ContractCompilationResponse
{
    /// <summary>
    ///     Gets a value indicating whether the compilation was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    /// <summary>
    ///     Gets the identifier for the session created during compilation when successful.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>
    ///     Gets an error message describing why compilation failed, if applicable.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

