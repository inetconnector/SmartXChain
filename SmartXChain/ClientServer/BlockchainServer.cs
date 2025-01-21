using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EmbedIO;
using EmbedIO.BearerToken;
using EmbedIO.WebApi;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using Swan.Logging;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.Server;

/// <summary>
///     The BlockchainServer class handles blockchain operations such as node registration, synchronization,
///     and serving API endpoints for blockchain interaction.
/// </summary>
public partial class BlockchainServer
{
    private const int NodeTimeoutSeconds = 120; // Maximum time before a node is considered inactive 
    private WebServer _server;

    /// <summary>
    ///     Initializes a new instance of the BlockchainServer class with specified external and internal IP addresses.
    /// </summary>
    public BlockchainServer(string url)
    {
        Logger.Log($"Starting server at {Config.Default.URL}...");
    }

    /// <summary>
    ///     Gets the webserver certificate
    /// </summary>
    public static X509Certificate2 WebserverCertificate { get; set; } = null;

    /// <summary>
    ///     Represents the startup state of the blockchain node.
    /// </summary>
    internal static NodeStartupResult Startup { get; private set; }

    public void StartMainServer()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var str = Path.Combine(Config.AppDirectory(), "wwwroot");
        FileSystem.CopyDirectory(Path.Combine(baseDirectory, "wwwroot"), str);
        var path = Path.Combine(str, "index.html");
        File.WriteAllText(path, ReplaceBody(File.ReadAllText(path)));

        if (!Config.Default.SSL)
            _server = (WebServer)new WebServer(Configure).WithCors()
                .WithLocalSessionManager()
                .WithWebApi("/api", m => m.WithController<ApiController>())
                .WithStaticFolder("/", str, true);
        else
            _server = (WebServer)new WebServer(Configure).WithCors()
                .WithBearerToken("/api", Crypt.AssemblyFingerprint.Substring(0, 40),
                    new BasicAuthorizationServerProvider())
                .WithLocalSessionManager()
                .WithWebApi("/api", m => m.WithController<ApiController>())
                .WithStaticFolder("/", str, true);

        //if (!Config.Default.Debug)
        {
            Swan.Logging.Logger.UnregisterLogger<ConsoleLogger>();
            //Terminal.Settings.DefaultColor = ConsoleColor.Green;
        }
        _server.RunAsync();

        Logger.Log($"Server started at {Config.Default.URL}");

        _server.StateChanged += (s, e) =>
            $"WebServer New State - {e.NewState}".Info();
    }

    private string ReplaceBody(string html)
    {
        var stringBuilder = new StringBuilder();

        stringBuilder.AppendLine($"Chain-ID: {Config.Default.ChainId}<br>");
        stringBuilder.AppendLine($"Miner: {Config.Default.MinerAddress}");

        return html.Replace("@body", stringBuilder.ToString());
    }

    private void Configure(WebServerOptions o)
    {
        var port = Config.Default.URL.Split(':')[2];

        o.WithMode(HttpListenerMode.EmbedIO);

        if (Config.Default.SSL && WebserverCertificate != null)
        {
            // Force the application to use TLS 1.2
            if (Config.Default.SecurityProtocol == "Tls11")
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11;
            else if (Config.Default.SecurityProtocol == "Tls12")
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;
            else if (Config.Default.SecurityProtocol == "Tls13")
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;

            // HTTPS configuration
            o.WithAutoLoadCertificate(true);
            o.WithCertificate(WebserverCertificate);
            o.WithUrlPrefix($"https://*:{port}/");
        }
        else
        {
            // HTTP configuration
            o.WithUrlPrefix($"http://*:{port}/");
        }
    }

    public void StopServer()
    {
        _server?.Dispose();
        Console.WriteLine("Server stopped.");
    }

    /// <summary>
    ///     Starts the server asynchronously.
    /// </summary>
    public static async Task<(BlockchainServer?, NodeStartupResult?)> StartServerAsync(bool loadExisting = true)
    {
        NodeStartupResult? result = null;

        // Initialize and start the node
        await Task.Run(async () => { result = await StartNode(Config.Default.MinerAddress); });

        if (result is null or { Blockchain: null })
            throw new InvalidOperationException("Failed to initialize the blockchain node.");

        BlockchainServer? server = null;

        // Initialize and start the server
        await Task.Run(() =>
        {
            try
            {
                server = new BlockchainServer(Config.Default.URL);
                server.Start();

                Logger.Log(
                    $"Server node for blockchain '{Config.Default.ChainId}' started at {Config.Default.URL}");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "starting server");
            }
        });
        Startup = result;


        if (loadExisting)
        {
            var chainPath = "";
            try
            {
                chainPath = Path.Combine(Config.Default.BlockchainPath, "chain-" + result!.Node.ChainId);
                if (File.Exists(chainPath))
                {
                    result!.Blockchain = Blockchain.Load(chainPath);
                }
                else
                {
                    Logger.Log($"No existing chain found in {chainPath}");
                    Logger.Log("Waiting for synchronization...");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"loading existing chain from {chainPath}");
                Logger.LogError($"{ex.Message}");
            }
        }

        return (server, result);
    }

    /// <summary>
    ///     Validates a node's signature using HMACSHA256 with the server's secret key.
    /// </summary>
    /// <param name="nodeAddress">The node address being validated.</param>
    /// <param name="signature">The provided signature to validate.</param>
    /// <returns>True if the signature is valid; otherwise, false.</returns>
    private bool ValidateSignature(string nodeAddress, string signature)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Config.Default.ChainId)))
        {
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(nodeAddress));
            var computedSignature = Convert.ToBase64String(computedHash);

            return computedSignature == signature;
        }
    }

    /// <summary>
    ///     Retrieves a list of active nodes, removing any that are inactive based on heartbeat timestamps.
    /// </summary>
    /// <param name="message">A dummy message for compatibility (not used).</param>
    /// <returns>A comma-separated list of active node addresses.</returns>
    private static string HandleNodes(string message)
    {
        RemoveInactiveNodes();

        if (Node.CurrentNodeIPs.Count > 0)
        {
            var nodes = string.Join(",", Node.CurrentNodeIPs.Where(node => !string.IsNullOrWhiteSpace(node)));
            return nodes.TrimEnd(',');
        }

        return "";
    }

    /// <summary>
    ///     Removes inactive nodes that have exceeded the heartbeat timeout from the registry.
    /// </summary>
    private static void RemoveInactiveNodes()
    {
        var now = DateTime.UtcNow;

        // Identify nodes that have exceeded the heartbeat timeout
        var inactiveNodes = Node.CurrentNodeIP_LastActive
            .Where(kvp => (now - kvp.Value).TotalSeconds > NodeTimeoutSeconds)
            .Select(kvp => kvp.Key)
            .ToList();

        // Remove inactive nodes from the registry
        foreach (var node in inactiveNodes)
        {
            Node.RemoveNodeIP(node);
            Logger.Log($"Node removed: {node} (Inactive)");
        }
    }

    /// <summary>
    ///     Processes a vote message, validating its block and returning a response if successful.
    /// </summary>
    /// <param name="message">The vote message containing block data in Base64 format.</param>
    /// <returns>"ok" with miner address if the vote is valid, or an error message otherwise.</returns>
    private string HandleVote(string message)
    {
        const string prefix = "Vote:";
        if (!message.StartsWith(prefix))
        {
            Logger.Log("Invalid Vote message received.");
            return "";
        }

        try
        {
            var base64 = message.Substring(prefix.Length);
            var block = Block.FromBase64(base64);
            if (block != null)
            {
                var hash = block.Hash;
                var calculatedHash = block.CalculateHash();
                if (calculatedHash == hash) return "ok#" + Config.Default.MinerAddress;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"Invalid Vote message received. {e.Message}");
        }

        return "";
    }

    /// <summary>
    ///     Verifies code by decompressing and validating it against security rules.
    /// </summary>
    /// <param name="message">The verification message containing compressed Base64 code.</param>
    /// <returns>"ok" if the code is safe, or an error message if validation fails.</returns>
    private static string HandleVerifyCode(string message)
    {
        const string prefix = "VerifyCode:";
        if (!message.StartsWith(prefix))
        {
            Logger.Log("Invalid verification request received.");
            return "";
        }

        var compressedBase64Data = message.Substring(prefix.Length);
        var code = Compress.DecompressString(Convert.FromBase64String(compressedBase64Data));

        var codecheck = "";
        var isCodeSafe = CodeSecurityAnalyzer.IsCodeSafe(code, ref codecheck);

        return isCodeSafe ? "ok" : $"failed: {codecheck}";
    }
}