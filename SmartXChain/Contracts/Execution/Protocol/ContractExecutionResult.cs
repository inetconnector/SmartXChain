namespace SmartXChain.Contracts.Execution;

/// <summary>
///     Represents the standardized result of a contract execution, including the textual outcome and
///     the serialized state that should be persisted by the node.
/// </summary>
public sealed record ContractExecutionResult(string Result, string SerializedState)
{
    public static ContractExecutionResult Error(string message, string state) => new(message, state);
    public static ContractExecutionResult Success(string state) => new("ok", state);
}
