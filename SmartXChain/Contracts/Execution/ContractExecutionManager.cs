using System.Threading;

namespace SmartXChain.Contracts.Execution;

/// <summary>
///     Provides access to the globally configured <see cref="IContractExecutor" /> that should be used for contract
///     execution within the node. Consumers can override the executor for testing purposes.
/// </summary>
public static class ContractExecutionManager
{
    private static IContractExecutor? _executor;

    /// <summary>
    ///     Gets or sets the global contract executor. If not explicitly configured a sandboxed executor is created on
    ///     first access.
    /// </summary>
    public static IContractExecutor Executor
    {
        get
        {
            if (_executor != null)
                return _executor;

            Interlocked.CompareExchange(ref _executor, new Sandbox.SandboxedContractExecutor(), null);
            return _executor!;
        }
        set => _executor = value ?? throw new ArgumentNullException(nameof(value));
    }
}
