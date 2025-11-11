using System.Text.RegularExpressions;
using System.IO;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace SmartXChain.Contracts;

public class CodeSecurityAnalyzer
{
    private static readonly string[] AllowedNamespaces =
    {
        Blockchain.SystemAddress,
        "System.Collections.Generic",
        "System.Collections.Concurrent",
        "System.Security.Cryptography",
        "System.Text",
        "System.Text.Json",
        "System.IO.Compression",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks",
        "SmartXChain.Utils",
        "SmartXChain.Contracts.Execution",
        "System.Diagnostics",
        "System.Net.Http",
        "System.Xml",
        "System.Xml.Linq"
    };

    private static readonly string[] ForbiddenClasses =
    {
        "File", "Directory", "Process", "FileInfo", "DirectoryInfo",
        "WebClient", "Socket", "Thread",
        "Assembly", "AppDomain", "Environment", "Marshal", "GCHandle",
        "RegistryKey", "Registry", "TcpClient", "UdpClient", "BinaryFormatter",
        "CryptoStream", "DES", "TripleDES", "RSA", "Aes", "Stream", "FileStream",
        "System.Windows", "Console", "Debugger", "ServiceController", "Win32Exception",
        "Reflection", "Delegate", "MethodInfo", "PropertyInfo", "EventInfo",
        "Kernel32", "DllImportAttribute, GZipStream, MemoryStream"
    };

    private static readonly string[] ForbiddenMethods =
    {
        "Start", "Invoke", "Load", "Delete", "Move", "Copy",
        "ReadAllBytes", "WriteAllBytes", "GetType", "CreateDomain",
        "Execute", "WriteAllText", "ReadAllText",
        "Encrypt", "Decrypt", "OpenSubKey", "CreateSubKey", "Close",
        "Flush", "Bind", "Connect", "Listen", "Send", "Receive",
        "Attach", "Detach", "Kill", "Stop", "Pause", "Resume",
        "LoadFrom", "LoadFile", "LoadModule", "DefineDynamicAssembly",
        "LoadLibrary", "QueueUserWorkItem", "Process.Start", "Console.ReadLine",
        "Console.ReadKey", "Console.Read"
    };


    private static readonly string[] ForbiddenKeywords =
    {
        "unsafe", "dynamic", "dllimport", "extern", "lock",
        "goto", "volatile", "fixed", "stackalloc", "yield", "sealed", "base.", "asm",
        "ref", "partial", "override"
    };

    private static readonly string[] ForbiddenReflectionPatterns =
    {
        "typeof", "Activator.CreateInstance", "MethodInfo", "PropertyInfo", "FieldInfo", "GetType"
    };

    private static readonly HashSet<string> AllowedAssemblyReferences = new(
        new[]
        {
            "System.Runtime",
            "System.Private.CoreLib",
            "System.Collections",
            "System.Collections.Concurrent",
            "System.Linq",
            "System.Text.Json",
            "System.Text",
            "System.Runtime.Extensions"
        },
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ForbiddenPaths = { "C:\\", "/etc/", "%TEMP%", "%APPDATA%" };

    private static readonly string[] ForbiddenActions =
    {
        "Activator.", "[DllImport]", "Marshal.", "Thread.", "Task.", "GC.", "Dispose", "Process.", "Diagnostics.",
        "%TEMP%", "%APPDATA%"
    };

    private static readonly string[] ForbiddenPatterns =
    {
        "\\|\\|", ".*\\$.*", "<script>", "eval\\(", "\\bexec\\b"
    };

    private static string RemoveBaseClassesSection(string code)
    {
        var pattern = @"/// ---------BEGIN BASE CLASSES----------[\s\S]*?/// ---------END BASE CLASSES----------";
        return Regex.Replace(code, pattern, string.Empty, RegexOptions.Singleline);
    }

    public static bool IsCodeSafe(string code, ref string message)
    {
        // Preprocess the code to remove comments 
        code = RemoveBaseClassesSection(code);
        code = RemoveComments(code);

        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // Check for non-whitelisted namespaces
        var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
        foreach (var ud in usingDirectives)
        {
            var ns = ud.Name.ToString();
            if (!AllowedNamespaces.Any(ns.StartsWith))
            {
                message = $"Non-whitelisted namespace detected: {ns}";
                Logger.Log(message);
                return false;
            }
        }

        if (code.Contains("#define") || code.Contains("#if"))
        {
            message = "Forbidden compiler directive detected.";
            Logger.Log(message);
            return false;
        }

        foreach (var pattern in ForbiddenReflectionPatterns)
            if (code.Contains(pattern))
            {
                message = $"Forbidden reflection usage detected: {pattern}";
                Logger.Log(message);
                return false;
            }

        if (code.Contains("../"))
        {
            message = "Path traversal detected.";
            Logger.Log(message);
            return false;
        }

        foreach (var action in ForbiddenActions)
            if (code.Contains(action))
            {
                message = $"Forbidden Action detected: {action}";
                Logger.Log(message);
                return false;
            }

        foreach (var path in ForbiddenPaths)
            if (code.Contains(path))
            {
                message = $"Forbidden path detected: {path}";
                Logger.Log(message);
                return false;
            }

        // Check for unsafe code blocks
        if (root.DescendantNodes().OfType<UnsafeStatementSyntax>().Any())
        {
            message = "Unsafe code block detected.";
            Logger.Log(message);
            return false;
        }

        // Check for forbidden patterns
        foreach (var pattern in ForbiddenPatterns)
            if (code.Contains(pattern))
            {
                message = $"Forbidden pattern detected: '{pattern}'";
                Logger.Log(message);
                return false;
            }

        // Check for forbidden classes
        var objectCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
        foreach (var oc in objectCreations)
        {
            var typeName = oc.Type.ToString();
            if (ForbiddenClasses.Any(f => typeName.Contains(f)))
            {
                message = $"Forbidden class detected: {typeName}";
                Logger.Log(message);
                return false;
            }
        }

        // Check for forbidden method calls
        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
        foreach (var ma in memberAccesses)
        {
            var memberName = ma.Name.ToString();
            if (ForbiddenMethods.Contains(memberName))
            {
                message = $"Forbidden method detected: {memberName}";
                Logger.Log(message);
                return false;
            }
        }

        // Check for dangerous keywords
        foreach (var keyword in ForbiddenKeywords)
            if (code.ToLower().Contains(keyword.ToLower()))
            {
                message = $"Forbidden keyword detected: {keyword}";
                Logger.Log(message);
                return false;
            }

        // Check for dangerous attributes
        var attributes = root.DescendantNodes().OfType<AttributeSyntax>();
        foreach (var attr in attributes)
        {
            var attributeName = attr.Name.ToString().ToLower();
            if (ForbiddenKeywords.Contains(attributeName))
            {
                message = $"Forbidden attribute detected: {attributeName}";
                Logger.Log(message);
                return false;
            }
        }

        // Check for infinite loops
        var loops = root.DescendantNodes().OfType<WhileStatementSyntax>();
        foreach (var loop in loops)
            if (loop.Condition.ToString() == "true")
            {
                message = "Infinite loop detected.";
                Logger.Log(message);
                return false;
            }

        // Check for delay-causing methods
        var delayMethods = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv => inv.Expression.ToString().Contains("Thread.Sleep") ||
                          inv.Expression.ToString().Contains("Task.Delay"));
        if (delayMethods.Any())
        {
            message = "Potential delay-causing method detected.";
            Logger.Log(message);
            return false;
        }

        return true;
    }

    // Utility method to remove comments from the code
    private static string RemoveComments(string code)
    {
        var noSingleLineComments = Regex.Replace(code, @"//.*", ""); // Remove single-line comments
        var noMultiLineComments =
            Regex.Replace(noSingleLineComments, @"/\*.*?\*/", "",
                RegexOptions.Singleline); // Remove multi-line comments
        return noMultiLineComments;
    }

    public static bool AreCommandsSafe(string[] codeCommands, ref ConcurrentList<string> messages)
    {
        messages = new ConcurrentList<string>();

        foreach (var command in codeCommands)
        {
            var message = string.Empty;
            if (!IsCodeSafe(command, ref message))
            {
                messages.Add(message);
                Logger.Log("Unsafe command detected.");
                return false;
            }
        }

        return true;
    }

    public static bool AreAssemblyReferencesSafe(string code, ref string message)
    {
        var referencePattern = new Regex("#r\\s+\"(?<assembly>[^\"]+)\"", RegexOptions.IgnoreCase);
        foreach (Match match in referencePattern.Matches(code))
        {
            var reference = match.Groups["assembly"].Value;
            var assemblyName = Path.GetFileNameWithoutExtension(reference);
            if (!AllowedAssemblyReferences.Contains(assemblyName ?? string.Empty))
            {
                message = $"Assembly reference '{reference}' is not permitted.";
                Logger.Log(message);
                return false;
            }
        }

        return true;
    }
}