using System.Text.Json.Serialization;

namespace SmartXChain.Contracts.Execution.Protocol;

/// <summary>
///     Represents a request sent to the contract executor asking to compile the supplied source code.
/// </summary>
public sealed class ContractCompilationRequest
{
    /// <summary>
    ///     Gets the smart contract source code to compile.
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }
}