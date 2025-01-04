using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;
using System.Text.RegularExpressions;

namespace SmartXChain.Contracts;

public class CodeSecurityAnalyzer
{
    private static readonly string[] AllowedNamespaces =
    {
        Blockchain.SystemAddress,
        "System.Collections.Generic",
        "System.Security.Cryptography",
        "System.Text",
        "System.Text.Json",
        "System.IO.Compression",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Threading.Tasks",
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

    private static string _safeContractCode = "";
    public static bool IsCodeSafe(string code, ref string message)
    {
        if (_safeContractCode=="")
        {
            var contractBaseFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Contracts", "Contract.cs");

            if (!File.Exists(contractBaseFile))
            {
                Logger.Log($"ERROR: Contract.cs not found at {contractBaseFile}.");
                return false;
            }
            var contractBaseContent = File.ReadAllText(contractBaseFile);
            if (!contractBaseContent.Contains("public Contract()"))
            {
                Logger.Log($"ERROR: {contractBaseFile} is invalid.");
                return false;
            }

            var usingRegex = new Regex(@"^using\s+[^;]+;", RegexOptions.Multiline);
            string code1WithoutUsings = usingRegex.Replace(contractBaseContent, "").Trim();

             _safeContractCode = code1WithoutUsings;
        }

        var tree = CSharpSyntaxTree.ParseText(code.Replace(_safeContractCode,""));
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

    public static bool AreCommandsSafe(string[] codeCommands, ref List<string> messages)
    {
        messages = new List<string>();

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
}