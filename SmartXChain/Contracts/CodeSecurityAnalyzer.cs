using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace SmartXChain.Contracts;

public class CodeSecurityAnalyzer
{
    // Allowed Namespaces
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
        "System.Diagnostics",
        "System.Net.Http",
        "System.Xml",
        "System.Xml.Linq"
    };

    // Forbidden Classes
    private static readonly string[] ForbiddenClasses =
    {
        "File", "Directory", "Process", "FileInfo", "DirectoryInfo",
        "WebClient", "Socket", "Thread", //"Task",
        "Assembly", "AppDomain", "Environment", "Marshal", "GCHandle",
        "RegistryKey", "Registry", "TcpClient", "UdpClient", "BinaryFormatter",
        "CryptoStream", "DES", "TripleDES", "RSA", "Aes", "Stream", "FileStream",
        "System.Windows", "Console", "Debugger", "ServiceController", "Win32Exception",
        "Reflection", "Delegate", "MethodInfo", "PropertyInfo", "EventInfo",
        "Kernel32", "DllImportAttribute"
    };

    // Forbidden Methods
    private static readonly string[] ForbiddenMethods =
    {
        "Start", "Invoke", "Load", "Delete", "Move", "Copy",
        "ReadAllBytes", "WriteAllBytes", "GetType", "CreateDomain",
        "Execute", "WriteAllText", "ReadAllText",
        "Encrypt", "Decrypt", "OpenSubKey", "CreateSubKey", "Close",
        "Flush", "Bind", "Connect", "Listen", "Send", "Receive",
        "Attach", "Detach", "Kill", "Stop", "Pause", "Resume",
        "LoadFrom", "LoadFile", "LoadModule", "DefineDynamicAssembly",
        "LoadLibrary", "QueueUserWorkItem", "Process.Start" //, "Run"
    };

    // Forbidden Keywords
    private static readonly string[] ForbiddenKeywords =
    {
        "unsafe", "dynamic", "DllImport", "extern", "lock",
        "goto", "volatile", "fixed", "stackalloc", "yield", "sealed", "base",
        "ref", "partial", "override" //, "async",  "out" ,"await", 
    };

    public static bool IsCodeSafe(string code, ref string message)
    {
        var tree = CSharpSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        // 1. Check for non-whitelisted namespaces
        var usingDirectives = root.DescendantNodes().OfType<UsingDirectiveSyntax>();
        foreach (var ud in usingDirectives)
        {
            var ns = ud.Name.ToString();
            if (!AllowedNamespaces.Any(ns.StartsWith))
            {
                message = $"Non-whitelisted namespace detected: {ns}";
                Logger.LogMessage(message);
                return false;
            }
        }

        // 2. Check for unsafe code blocks
        if (root.DescendantNodes().OfType<UnsafeStatementSyntax>().Any())
        {
            message = "Unsafe code block detected.";
            Logger.LogMessage(message);
            return false;
        }

        // 3. Check for forbidden classes
        var objectCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
        foreach (var oc in objectCreations)
        {
            var typeName = oc.Type.ToString();
            if (ForbiddenClasses.Any(f => typeName.Contains(f)))
            {
                message = $"Forbidden class detected: {typeName}";
                Logger.LogMessage(message);
                return false;
            }
        }

        // 4. Check for forbidden method calls
        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
        foreach (var ma in memberAccesses)
        {
            var memberName = ma.Name.ToString();
            if (ForbiddenMethods.Contains(memberName))
            {
                message = $"Forbidden method detected: {memberName}";
                Logger.LogMessage(message);
                return false;
            }
        }

        // 5. Check for dangerous keywords
        foreach (var keyword in ForbiddenKeywords)
            if (code.Contains(keyword))
            {
                message = $"Forbidden keyword detected: {keyword}";
                Logger.LogMessage(message);
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
                Logger.LogMessage("Unsafe command detected.");
                return false;
            }
        }

        return true;
    }
}