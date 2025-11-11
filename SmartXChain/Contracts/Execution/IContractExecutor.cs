namespace SmartXChain.Contracts.Execution;

/// <summary>
///     Abstraction for executing smart contracts in a sandboxed environment. Implementations encapsulate
///     compilation, state transfer and lifecycle management of the underlying runtime.
/// </summary>
public interface IContractExecutor
{
    /// <summary>
    ///     Compiles the provided contract code and returns an execution session that can be used for subsequent
    ///     state transfer and runtime control.
    /// </summary>
    /// <param name="contractCode">Source code of the contract that should be compiled.</param>
    /// <param name="cancellationToken">Cancellation token to abort the compilation.</param>
    /// <returns>An execution session representing the compiled contract.</returns>
    Task<IContractExecutionSession> CompileAsync(string contractCode, CancellationToken cancellationToken);

    /// <summary>
    ///     Transfers the serialized state into the sandbox and returns the normalized state representation that
    ///     should be used for execution.
    /// </summary>
    /// <param name="session">The execution session created during compilation.</param>
    /// <param name="serializedState">The serialized state of the contract.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The normalized serialized state that should be used for execution.</returns>
    Task<string> TransferStateAsync(IContractExecutionSession session, string serializedState,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Executes the compiled contract using the provided inputs and state.
    /// </summary>
    /// <param name="session">Execution session of the compiled contract.</param>
    /// <param name="inputs">Inputs to be passed to the contract.</param>
    /// <param name="serializedState">The serialized state that should be used for execution.</param>
    /// <param name="cancellationToken">Cancellation token controlling runtime duration.</param>
    /// <returns>The execution result and the updated serialized state.</returns>
    Task<ContractExecutionResult> ExecuteAsync(IContractExecutionSession session, string[] inputs,
        string serializedState, CancellationToken cancellationToken);
}