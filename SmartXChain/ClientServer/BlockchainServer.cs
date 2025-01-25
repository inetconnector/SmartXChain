using System.Net;
using System.Security.Cryptography.X509Certificates;
using EmbedIO;
using EmbedIO.BearerToken;
using EmbedIO.WebApi;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;
using Swan.Logging;

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
        if (!Config.Default.SSL)
            _server = new WebServer(Configure).WithCors()
                .WithLocalSessionManager()
                .WithWebApi("/api", m => m.WithController<ApiController>())
                .WithStaticFolder("/", FileSystem.WWWRoot, true);
        else
            _server = (WebServer)new WebServer(Configure).WithCors()
                .WithBearerToken("/api", Crypt.AssemblyFingerprint.Substring(0, 40),
                    new BasicAuthorizationServerProvider())
                .WithLocalSessionManager()
                .WithWebApi("/api", m => m.WithController<ApiController>())
                .WithStaticFolder("/", FileSystem.WWWRoot, true);

        //if (!Config.Default.Debug)
        {
            try
            {
                Swan.Logging.Logger.UnregisterLogger<ConsoleLogger>();
            }
            catch (Exception e)
            {
            }

            //Terminal.Settings.DefaultColor = ConsoleColor.Green;
        }
        _server.RunAsync();

        Logger.Log($"Server started at {Config.Default.URL}");

        _server.StateChanged += (s, e) =>
            $"WebServer New State - {e.NewState}".Info();
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
}