using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace SecureCodeRunnerLib;

public class CodeRunner
{
    private const string serializerClassImports = @"
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.IO;
";

    public static ScriptOptions ScriptOptions = ScriptOptions.Default
        .AddReferences(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
        .AddImports("System", "System.Linq", "System.Collections.Generic", "System.Text");

    public async Task<(string, string)> RunScriptAsync(string code, string[] inputs, string currentState,
        CancellationToken ct)
    {
        if (!CodeSecurityAnalyzer.IsCodeSafe(code))
            return ("The code contains forbidden constructs and was not executed.", currentState);

        if (!CodeSecurityAnalyzer.AreCommandsSafe(inputs))
            return ("The inputs contain forbidden constructs and the code was not executed.", currentState);

        // Modify code to include inputs after imports
        code = InjectInputsAfterImports(code, inputs);

        // Add Serializer class to the provided code
        code = InjectSerializerClass(code);

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
                return ("Ok", globals.Output + "");
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

            return serializerClassImports + codeInserted;
        }

        // If no using directives, inject at the start of the code
        return $"\n\n{serializerClassImports}\n\n{inputDeclarations}\n\n{code}";
    }

    private string InjectSerializerClass(string code)
    {
        const string serializerClass = @"
public static class Serializer
{
    public static string SerializeToBase64<T>(T instance)
    {
        var json = JsonSerializer.Serialize(instance, new JsonSerializerOptions { WriteIndented = true });

        using (var memoryStream = new MemoryStream())
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
            gzipStream.Close();
            return Convert.ToBase64String(memoryStream.ToArray());
        }
    }

    public static T DeserializeFromBase64<T>(string base64Data) where T : class
    {
        var compressedData = Convert.FromBase64String(base64Data);

        using (var memoryStream = new MemoryStream(compressedData))
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
        using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
        {
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}";

        return $"{code}\n\n{serializerClass}";
    }

    public class Globals
    {
        public string CurrentState { get; set; } // Aktueller Zustand des Contracts
        public object Output { get; set; } // Ausgabe des Scripts
    }
}