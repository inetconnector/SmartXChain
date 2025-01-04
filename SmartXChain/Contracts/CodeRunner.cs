using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace SmartXChain.Contracts;

/// <summary>
///     The CodeRunner class enables the secure execution of user-defined C# scripts with custom inputs
///     and state management. It integrates safety checks and supports serialization for data handling.
/// </summary>
public class CodeRunner
{
    /// <summary>
    ///     Contains additional imports needed for serializer functionality.
    /// </summary>
    private const string InjectImports =
        @"
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.IO;
";

    /// <summary>
    ///     Script options for executing user-defined scripts. Includes default assemblies and common namespaces.
    /// </summary>
    public static ScriptOptions ScriptOptions = ScriptOptions.Default
        .AddReferences(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
        .AddImports("System", "System.Linq", "System.Collections.Generic", "System.Text");
     
    /// <summary>
    ///     Executes a C# script asynchronously with the provided inputs and state.
    /// </summary>
    /// <param name="code">The C# code to execute.</param>
    /// <param name="inputs">Array of input variables to be injected into the code.</param>
    /// <param name="currentState">The current state to be used during execution.</param>
    /// <param name="ct">Cancellation token to handle task cancellation.</param>
    /// <returns>A tuple containing the execution result and updated state.</returns>
    public async Task<(string, string)> RunScriptAsync(string code, string[] inputs, string currentState,
        CancellationToken ct)
    {
        var message = ""; 
        if (!CodeSecurityAnalyzer.IsCodeSafe(code, ref message))
            return ($"The code contains forbidden constructs and was not executed. Details: {message}", currentState);

        var messages = new List<string>();
        if (!CodeSecurityAnalyzer.AreCommandsSafe(inputs, ref messages))
            return (
                $"The inputs contain forbidden constructs and the code was not executed. Details: {string.Join(", ", messages)}",
                currentState);

        // Modify code to include inputs after imports
        code = InjectInputsAfterImports(code, inputs);

        // Add Serializer class to the provided code
        code = InjectCode(code);

        var globals = new Globals
        {
            CurrentState = currentState,
            Output = null
        };

        try
        {
            // Execute the script asynchronously with globals
            var script = CSharpScript.Create(code, ScriptOptions, typeof(Globals));
            var state = await script.RunAsync(globals, ct);

            // Retrieve the output from the globals
            if (!string.IsNullOrEmpty(globals.Output + ""))
                return ("ok", globals.Output + "");
            return ("Execution completed with no result.", currentState);
        }
        catch (CompilationErrorException ex)
        {
            return ($"Execution failed with compilation errors: {string.Join(Environment.NewLine, ex.Diagnostics)}",
                currentState);
        }
        catch (Exception ex)
        {
            return ($"Execution failed: {ex.Message}", currentState);
        }
    }

    /// <summary>
    ///     Injects input declarations into the user-provided code after existing import statements.
    /// </summary>
    /// <param name="code">The original code to modify.</param>
    /// <param name="inputs">Array of input declarations.</param>
    /// <returns>Modified code with injected inputs.</returns>
    private string InjectInputsAfterImports(string code, string[] inputs)
    {
        var inputDeclarations = string.Join(Environment.NewLine, inputs);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToList();
        if (usingDirectives.Any())
        {
            var lastUsing = usingDirectives.Last();
            var position = lastUsing.FullSpan.End;
            var codeInserted = code.Insert(position, $"\n\n{inputDeclarations}\n");

            return InjectImports + codeInserted;
        }

        // If no using directives, inject at the start of the code
        return $"\n\n{InjectImports}\n\n{inputDeclarations}\n\n{code}";
    }

    /// <summary>
    ///     Adds a static code to inject a class to the provided code.
    /// </summary>
    /// <param name="code">The original code to modify.</param>
    /// <returns>Modified code with the injected class appended.</returns>
    private string InjectCode(string code)
    {
        const string injectCode = @"";

        return $"{code}\n\n{injectCode}";
    }

    /// <summary>
    ///     Defines global variables accessible during script execution.
    /// </summary>
    public class Globals
    {
        /// <summary>
        ///     Current state of the contracts.
        /// </summary>
        public string CurrentState { get; set; }

        /// <summary>
        ///     Output of the script execution.
        /// </summary>
        public object Output { get; set; }
    }
}