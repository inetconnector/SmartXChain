using System;
using System.Threading.Tasks;

namespace SmartXChain.Contracts.Execution;

/// <summary>
///     Represents a compiled contract instance that can be used for state transfer and execution within the
///     sandboxed runtime.
/// </summary>
public interface IContractExecutionSession : IAsyncDisposable
{
    /// <summary>
    ///     Gets an identifier for the session that can be used for logging or diagnostics.
    /// </summary>
    string SessionId { get; }
}
