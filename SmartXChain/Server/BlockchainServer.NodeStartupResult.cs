using System.Text;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;
using SmartXChain.Validators;

namespace SmartXChain.Server;

/// <summary>
///     Represents the blockchain server responsible for managing node registration, starting the server,
///     and synchronizing with peers.
/// </summary>
public partial class BlockchainServer
{
    /// <summary>
    ///     Starts the blockchain server by initializing key tasks such as peer discovery, server startup,
    ///     and peer synchronization.
    /// </summary>
    public void Start()
    {
        // 1. Discover and register with peer servers
        Task.Run(() => DiscoverAndRegisterWithPeers());

        // 2. Start the main server to listen for incoming messages
        Task.Run(() => StartMainServer());

        // 3. Background task to synchronize with peer servers
        Task.Run(() => SynchronizeWithPeers());
    }

    /// <summary>
    ///     Starts a new node on the blockchain and initializes the blockchain for that node.
    /// </summary>
    /// <param name="walletAddress">The wallet address to associate with the node.</param>
    /// <returns>A Task that resolves to a <see cref="NodeStartupResult" /> containing the blockchain and node information.</returns>
    public static async Task<NodeStartupResult> StartNode(string walletAddress)
    {
        // Start the node and initialize its configuration
        var node = await Node.Start();

        // Create a new blockchain with the provided wallet address
        var blockchain = new Blockchain(2, walletAddress);

        // Publish the server's IP address in a transaction on the blockchain
        var nodeTransaction = new Transaction
        {
            Sender = Blockchain.SystemAddress,
            Recipient = Blockchain.SystemAddress,
            Data = Convert.ToBase64String(Encoding.ASCII.GetBytes(NetworkUtils.IP)), // Store data as Base64 string
            Timestamp = DateTime.UtcNow
        };

        blockchain.AddTransaction(nodeTransaction);

        // Set up the result containing the blockchain and node
        Startup = new NodeStartupResult(blockchain, node);
        return Startup;
    }

    /// <summary>
    ///     Represents the result of the node startup process, including the blockchain and the node instance.
    /// </summary>
    public class NodeStartupResult
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="NodeStartupResult" /> class.
        /// </summary>
        /// <param name="blockchain">The initialized blockchain instance.</param>
        /// <param name="node">The node associated with the blockchain.</param>
        public NodeStartupResult(Blockchain blockchain, Node node)
        {
            Blockchain = blockchain;
            Node = node;
        }

        /// <summary>
        ///     Gets or sets the blockchain associated with the node.
        /// </summary>
        public Blockchain Blockchain { get; set; }

        /// <summary>
        ///     Gets the node instance associated with the blockchain.
        /// </summary>
        public Node Node { get; private set; }
    }
}