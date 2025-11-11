using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SmartXChain.Contracts.Execution;
using SmartXChain.Contracts.Execution.Protocol;

namespace SmartXChain.ContractExecutorHost;

public static class Program
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] AllowedNamespaces =
    {
        "System",
        "System.Linq",
        "System.Collections",
        "System.Collections.Generic",
        "System.Text",
        "System.Text.Json",
        "SmartXChain.Contracts.Execution"
    };

    public static async Task Main()
    {
        var stdin = Console.In;
        var stdout = Console.Out;
        await foreach (var line in ReadLinesAsync(stdin))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var document = JsonDocument.Parse(line);
            var type = document.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "compile":
                    await HandleCompileAsync(document.RootElement.GetProperty("payload"), stdout);
                    break;
                case "state":
                    await HandleStateTransferAsync(document.RootElement.GetProperty("payload"), stdout);
                    break;
                case "execute":
                    await HandleExecuteAsync(document.RootElement.GetProperty("payload"), stdout);
                    break;
                case "shutdown":
                    return;
            }
        }
    }

    private static async Task HandleCompileAsync(JsonElement payload, TextWriter stdout)
    {
        var request = payload.Deserialize<ContractCompilationRequest>(SerializerOptions)!;
        var code = request.Code;

        try
        {
            var compilation = CreateCompilation(code);
            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream, pdbStream);

            ContractRuntime.Instance.Reset();

            if (!emitResult.Success)
            {
                var errors = string.Join(Environment.NewLine,
                    emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                await WriteAsync(stdout, new ContractExecutorEnvelope
                {
                    Type = "compile",
                    Payload = new ContractCompilationResponse
                    {
                        Success = false,
                        Error = errors
                    }
                });
                return;
            }

            peStream.Seek(0, SeekOrigin.Begin);
            pdbStream.Seek(0, SeekOrigin.Begin);

            ContractRuntime.Instance.LoadAssembly(peStream, pdbStream);

            await WriteAsync(stdout, new ContractExecutorEnvelope
            {
                Type = "compile",
                Payload = new ContractCompilationResponse
                {
                    Success = true,
                    SessionId = ContractRuntime.Instance.SessionId
                }
            });
        }
        catch (Exception ex)
        {
            await WriteAsync(stdout, new ContractExecutorEnvelope
            {
                Type = "compile",
                Payload = new ContractCompilationResponse
                {
                    Success = false,
                    Error = ex.Message
                }
            });
        }
    }

    private static async Task HandleStateTransferAsync(JsonElement payload, TextWriter stdout)
    {
        var request = payload.Deserialize<ContractStateTransferRequest>(SerializerOptions)!;
        ContractRuntime.Instance.SetState(request.SerializedState);

        await WriteAsync(stdout, new ContractExecutorEnvelope
        {
            Type = "state",
            Payload = new ContractStateTransferResponse
            {
                Success = true,
                SerializedState = ContractRuntime.Instance.SerializedState
            }
        });
    }

    private static async Task HandleExecuteAsync(JsonElement payload, TextWriter stdout)
    {
        var request = payload.Deserialize<ContractExecutionRequest>(SerializerOptions)!;

        try
        {
            var result = await ContractRuntime.Instance.ExecuteAsync(request.Inputs, request.SerializedState);
            await WriteAsync(stdout, new ContractExecutorEnvelope
            {
                Type = "execute",
                Payload = new ContractExecutionResponse
                {
                    Success = true,
                    Result = result.Result,
                    SerializedState = result.SerializedState
                }
            });
        }
        catch (Exception ex)
        {
            await WriteAsync(stdout, new ContractExecutorEnvelope
            {
                Type = "execute",
                Payload = new ContractExecutionResponse
                {
                    Success = false,
                    Error = ex.Message,
                    SerializedState = request.SerializedState
                }
            });
        }
    }

    private static CSharpCompilation CreateCompilation(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var invalidNamespace = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString())
            .FirstOrDefault(ns => !string.IsNullOrWhiteSpace(ns) &&
                                  AllowedNamespaces.All(allowed =>
                                      !ns.StartsWith(allowed, StringComparison.Ordinal)));

        if (!string.IsNullOrEmpty(invalidNamespace))
            throw new InvalidOperationException(
                $"Using directive '{invalidNamespace}' is not allowed in sandboxed contracts.");

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(JsonSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ContractExecutionResult).Assembly.Location)
        };

        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            allowUnsafe: false);

        return CSharpCompilation.Create($"Contract_{Guid.NewGuid():N}", new[] { tree }, references, compilationOptions);
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(TextReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
            yield return line;
    }

    private static Task WriteAsync(TextWriter writer, ContractExecutorEnvelope envelope)
    {
        return writer.WriteLineAsync(JsonSerializer.Serialize(envelope, SerializerOptions));
    }
}

internal sealed class ContractRuntime
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private Assembly? _assembly;
    private Type? _entryType;
    private MethodInfo? _executeMethod;
    private RestrictedLoadContext _loadContext = new();

    public static ContractRuntime Instance { get; } = new();

    public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");

    public string SerializedState { get; private set; } = string.Empty;

    public void Reset()
    {
        SessionId = Guid.NewGuid().ToString("N");
        SerializedState = string.Empty;
        _assembly = null;
        _entryType = null;
        _executeMethod = null;
        _loadContext = new RestrictedLoadContext();
    }

    public void LoadAssembly(Stream peStream, Stream pdbStream)
    {
        Reset();
        _assembly = _loadContext.LoadFromStream(peStream, pdbStream);
        _entryType = _assembly.GetTypes().FirstOrDefault(IsContractEntryType)
                     ?? throw new InvalidOperationException("No valid contract entry point found.");
        _executeMethod = _entryType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                             .FirstOrDefault(m => m.Name.Equals("Execute", StringComparison.OrdinalIgnoreCase) &&
                                                  IsValidSignature(m))
                         ?? throw new InvalidOperationException("No Execute method with valid signature found.");
    }

    public void SetState(string serializedState)
    {
        SerializedState = serializedState ?? string.Empty;
    }

    public async Task<ContractExecutionResult> ExecuteAsync(string[] inputs, string serializedState)
    {
        if (_assembly == null || _executeMethod == null)
            throw new InvalidOperationException("Contract not compiled.");

        object? instance = null;
        if (!_executeMethod.IsStatic)
            instance = Activator.CreateInstance(_entryType!);

        var parameters = _executeMethod.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter.ParameterType == typeof(string[]))
                args[i] = inputs;
            else if (parameter.ParameterType == typeof(string))
                args[i] = serializedState;
            else if (parameter.ParameterType == typeof(ContractExecutionContext))
                args[i] = new ContractExecutionContext(serializedState, inputs);
            else
                args[i] = null;
        }

        var result = _executeMethod.Invoke(instance, args);

        if (result is Task<ContractExecutionResult> taskResult)
        {
            result = await taskResult.ConfigureAwait(false);
        }
        else if (result is Task task)
        {
            await task.ConfigureAwait(false);
            result = null;
        }

        if (result is ContractExecutionResult executionResult)
        {
            SerializedState = executionResult.SerializedState;
            return executionResult;
        }

        SerializedState = serializedState;
        return new ContractExecutionResult(result?.ToString() ?? "ok", SerializedState);
    }

    private static bool IsContractEntryType(Type type)
    {
        if (!type.IsClass || type.IsAbstract)
            return false;

        if (type.Name.Equals("Contract", StringComparison.OrdinalIgnoreCase) ||
            type.Name.Equals("Authenticate", StringComparison.OrdinalIgnoreCase))
            return false;

        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Any(IsValidSignature);
    }

    private static bool IsValidSignature(MethodInfo method)
    {
        if (!method.Name.Equals("Execute", StringComparison.OrdinalIgnoreCase))
            return false;

        if (method.ReturnType != typeof(ContractExecutionResult) &&
            method.ReturnType != typeof(Task<ContractExecutionResult>) &&
            method.ReturnType != typeof(Task))
            return false;

        var parameters = method.GetParameters();
        return parameters.All(p => p.ParameterType == typeof(string[]) ||
                                   p.ParameterType == typeof(string) ||
                                   p.ParameterType == typeof(ContractExecutionContext));
    }
}

internal sealed class RestrictedLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> ForbiddenAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.IO",
        "System.IO.FileSystem",
        "System.Reflection",
        "System.Reflection.Emit"
    };

    public RestrictedLoadContext() : base(true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (ForbiddenAssemblies.Contains(assemblyName.Name ?? string.Empty))
            throw new InvalidOperationException($"Assembly '{assemblyName.Name}' is not permitted in sandbox context.");
        return null;
    }
}

public readonly record struct ContractExecutionContext(string SerializedState, IReadOnlyList<string> Inputs);