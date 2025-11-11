using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SmartXChain.Contracts.Execution.Protocol;
using SmartXChain.Utils;

namespace SmartXChain.Contracts.Execution.Sandbox;

/// <summary>
///     Implementation of <see cref="IContractExecutor" /> that executes contracts by delegating to the
///     SmartXChain.ContractExecutorHost process. The executor ensures that every interaction happens within a
///     dedicated sandboxed process with strict timeouts and memory monitoring.
/// </summary>
public sealed class SandboxedContractExecutor : IContractExecutor
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const long MemoryLimitBytes = 128 * 1024 * 1024; // 128 MB

    /// <summary>
    ///     Compiles the supplied contract code inside the sandbox host and returns an execution session.
    /// </summary>
    /// <param name="contractCode">The raw C# contract code.</param>
    /// <param name="cancellationToken">Token used to cancel the compilation request.</param>
    /// <returns>An <see cref="IContractExecutionSession" /> bound to the sandbox process.</returns>
    public async Task<IContractExecutionSession> CompileAsync(string contractCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contractCode))
            throw new ArgumentException("Contract code must not be empty", nameof(contractCode));

        if (!IsCodeSafe(contractCode, out var unsafeMessage))
            throw new InvalidOperationException(unsafeMessage);

        var process = StartHostProcess();
        var session = new SandboxedContractExecutionSession(process, MemoryLimitBytes);

        try
        {
            await session.WriteMessageAsync(new ContractExecutorEnvelope
            {
                Type = "compile",
                Payload = new ContractCompilationRequest { Code = contractCode }
            }, cancellationToken).ConfigureAwait(false);

            var response = await session.ReadMessageAsync<ContractCompilationResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (!response.Success)
                throw new InvalidOperationException(response.Error ?? "Compilation failed in sandbox host.");

            session.SessionId = response.SessionId ?? Guid.NewGuid().ToString("N");
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Transfers serialized contract state into the sandbox and returns the normalized representation.
    /// </summary>
    /// <param name="session">The session obtained from <see cref="CompileAsync" />.</param>
    /// <param name="serializedState">The serialized contract state to transfer.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The sanitized serialized state provided by the sandbox.</returns>
    public Task<string> TransferStateAsync(IContractExecutionSession session, string serializedState,
        CancellationToken cancellationToken)
    {
        if (session is not SandboxedContractExecutionSession sandboxSession)
            throw new ArgumentException("Unsupported session type", nameof(session));

        return TransferStateInternalAsync(sandboxSession, serializedState, cancellationToken);
    }

    /// <summary>
    ///     Executes the compiled contract with the supplied inputs and state within the sandbox.
    /// </summary>
    /// <param name="session">The execution session bound to the sandbox.</param>
    /// <param name="inputs">Invocation parameters for the contract.</param>
    /// <param name="serializedState">The serialized state used for execution.</param>
    /// <param name="cancellationToken">Token used to cancel the execution.</param>
    /// <returns>The contract execution result including the updated state.</returns>
    public Task<ContractExecutionResult> ExecuteAsync(IContractExecutionSession session, string[] inputs,
        string serializedState, CancellationToken cancellationToken)
    {
        if (session is not SandboxedContractExecutionSession sandboxSession)
            throw new ArgumentException("Unsupported session type", nameof(session));

        return ExecuteInternalAsync(sandboxSession, inputs, serializedState, cancellationToken);
    }

    private static bool IsCodeSafe(string contractCode, out string message)
    {
        message = string.Empty;
        if (!CodeSecurityAnalyzer.IsCodeSafe(contractCode, ref message))
            return false;
        if (!CodeSecurityAnalyzer.AreAssemblyReferencesSafe(contractCode, ref message))
            return false;
        return true;
    }

    private static Process StartHostProcess()
    {
        var hostPath = ResolveHostPath();
        if (!File.Exists(hostPath))
            throw new FileNotFoundException("Contract executor host not found", hostPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{hostPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.Environment["DOTNET_GCHeapHardLimit"] = MemoryLimitBytes.ToString();
        startInfo.Environment["DOTNET_GCLatencyLevel"] = "1"; // sustained low latency

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Failed to launch sandbox host process");

        return process;
    }

    private static string ResolveHostPath()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.Combine(baseDirectory, "SmartXChain.ContractExecutorHost.dll");
        if (File.Exists(candidate))
            return candidate;

        candidate = Path.Combine(baseDirectory, "SmartXChain.ContractExecutorHost", "SmartXChain.ContractExecutorHost.dll");
        if (File.Exists(candidate))
            return candidate;

        var searchRoot = Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", ".."));
        if (Directory.Exists(searchRoot))
        {
            var discovered = Directory.EnumerateFiles(searchRoot, "SmartXChain.ContractExecutorHost.dll",
                    SearchOption.AllDirectories)
                .OrderBy(File.GetLastWriteTimeUtc)
                .LastOrDefault();
            if (!string.IsNullOrEmpty(discovered))
                return discovered;
        }

        return candidate;
    }

    private static async Task<string> TransferStateInternalAsync(SandboxedContractExecutionSession session,
        string serializedState, CancellationToken cancellationToken)
    {
        await session.WriteMessageAsync(new ContractExecutorEnvelope
        {
            Type = "state",
            Payload = new ContractStateTransferRequest { SerializedState = serializedState }
        }, cancellationToken).ConfigureAwait(false);

        var response = await session.ReadMessageAsync<ContractStateTransferResponse>(cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
            throw new InvalidOperationException(response.Error ?? "State transfer failed");

        return response.SerializedState ?? serializedState;
    }

    private static async Task<ContractExecutionResult> ExecuteInternalAsync(
        SandboxedContractExecutionSession session, string[] inputs, string serializedState,
        CancellationToken cancellationToken)
    {
        await session.WriteMessageAsync(new ContractExecutorEnvelope
        {
            Type = "execute",
            Payload = new ContractExecutionRequest { Inputs = inputs, SerializedState = serializedState }
        }, cancellationToken).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultTimeout);

        try
        {
            var response = await session.ReadMessageAsync<ContractExecutionResponse>(timeoutCts.Token)
                .ConfigureAwait(false);

            if (!response.Success)
                return ContractExecutionResult.Error(response.Error ?? "Execution failed",
                    serializedState);

            return new ContractExecutionResult(response.Result ?? "ok",
                response.SerializedState ?? serializedState);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await session.TerminateAsync().ConfigureAwait(false);
            return ContractExecutionResult.Error("Execution timeout", serializedState);
        }
        catch (Exception ex)
        {
            await session.TerminateAsync().ConfigureAwait(false);
            return ContractExecutionResult.Error($"Execution failed: {ex.Message}", serializedState);
        }
    }

    private sealed class SandboxedContractExecutionSession : IContractExecutionSession
    {
        private readonly JsonSerializerOptions _serializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly StreamReader _stdout;
        private readonly CancellationTokenSource _monitorCts = new();
        private readonly Task _monitorTask;
        private readonly long _memoryLimitBytes;

        public SandboxedContractExecutionSession(Process process, long memoryLimitBytes)
        {
            _process = process;
            _stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            _stdout = new StreamReader(process.StandardOutput.BaseStream, Encoding.UTF8);
            _memoryLimitBytes = memoryLimitBytes;

            _monitorTask = Task.Run(() => MonitorResourceUsageAsync(_monitorCts.Token));
        }

        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

        public async ValueTask DisposeAsync()
        {
            try
            {
                await WriteMessageAsync(new ContractExecutorEnvelope { Type = "shutdown", Payload = new { } },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
            finally
            {
                _monitorCts.Cancel();
                await _monitorTask.ConfigureAwait(false);

                if (!_process.HasExited)
                {
                    try
                    {
                        if (!_process.WaitForExit(200))
                            _process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                _stdin.Dispose();
                _stdout.Dispose();
                _process.Dispose();
            }
        }

        public Task TerminateAsync()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            return Task.CompletedTask;
        }

        public async Task WriteMessageAsync(ContractExecutorEnvelope envelope, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(envelope, _serializerOptions);
            await _stdin.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        public async Task<TResponse> ReadMessageAsync<TResponse>(CancellationToken cancellationToken)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                throw new InvalidOperationException("Unexpected end of stream while waiting for sandbox response");

            try
            {
                return JsonSerializer.Deserialize<TResponse>(line, _serializerOptions)!;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize sandbox response: {ex.Message}");
            }
        }

        private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            var buffer = new char[1];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await _stdout.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                if (read == 0)
                    return sb.Length == 0 ? null : sb.ToString();

                var ch = buffer[0];
                if (ch == '\r')
                    continue;
                if (ch == '\n')
                    return sb.ToString();
                sb.Append(ch);
            }
        }

        private async Task MonitorResourceUsageAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_process.HasExited)
                        return;

                    if (_process.WorkingSet64 > _memoryLimitBytes)
                    {
                        Logger.LogError($"Sandbox session {SessionId} exceeded memory limit. Killing process.");
                        await TerminateAsync().ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // expected on disposal
            }
        }
    }
}

