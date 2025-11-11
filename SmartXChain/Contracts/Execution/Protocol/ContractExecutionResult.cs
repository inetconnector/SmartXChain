namespace SmartXChain.Contracts.Execution;

/// <summary>
///     Represents the standardized result of a contract execution, including the textual outcome and
///     the serialized state that should be persisted by the node.
/// </summary>
public sealed record ContractExecutionResult(string Result, string SerializedState)
{
    /// <summary>
    ///     Creates a result representing a failed execution while preserving state.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="state">The serialized state to keep.</param>
    /// <returns>A new <see cref="ContractExecutionResult" /> describing the failure.</returns>
    public static ContractExecutionResult Error(string message, string state)
    {
        return new ContractExecutionResult(message, state);
    }

    /// <summary>
    ///     Creates a result representing a successful execution with the specified state.
    /// </summary>
    /// <param name="state">The serialized contract state produced by execution.</param>
    /// <returns>A new <see cref="ContractExecutionResult" /> describing the success.</returns>
    public static ContractExecutionResult Success(string state)
    {
        return new ContractExecutionResult("ok", state);
    }
}