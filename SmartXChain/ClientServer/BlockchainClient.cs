using System.Collections.Concurrent;
using System.Text.Json;
using SmartXChain.BlockchainCore;
using SmartXChain.Contracts;
using SmartXChain.Utils;
using Node = SmartXChain.Validators.Node;

namespace SmartXChain.Server;

public partial class BlockchainServer
{
    /// <summary>
    ///     Fetches the public key of a peer for establishing secure communication.
    /// </summary>
    /// <param name="peer">The URL of the peer.</param>
    /// <returns>The public key of the peer as a byte array, or null if retrieval fails.</returns>
    private static readonly ConcurrentDictionary<string, byte[]> PublicKeyCache = new();

    /// <summary>
    ///     Discovers peers from the configuration and registers them in the peer server list,
    ///     excluding the current server addresses.
    /// </summary>
    private void DiscoverAndRegisterWithPeers()
    {
        var validPeers = new ConcurrentList<string>();

        try
        {
            foreach (var peer in Config.Default.Peers)
                if (!string.IsNullOrEmpty(peer) && peer.StartsWith("http"))
                    Node.AddNodeIP(peer);

            Logger.Log($"Static peers discovered: {string.Join(", ", validPeers.Count)}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "processing static peers");
        }
    }

    /// <summary>
    ///     Refactored SynchronizeWithPeers to utilize SecureCommunication and process responses.
    /// </summary>
    private async Task SynchronizeWithPeers()
    {
        while (true)
        {
            await Sync();
            await Task.Delay(30000);
        }
    }

    public static async Task Sync()
    {
        foreach (var peer in Node.CurrentNodeIPs)
        {
            if (peer == Config.Default.URL)
                continue;

            var (success, response) = await SendSecureMessage(peer, "/api/Nodes", Config.Default.URL);

            if (success && !string.IsNullOrEmpty(response))
                try
                {
                    var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
                    if (responseObject != null)
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"SynchronizeWithPeers  {peer} Result: {responseObject.Message}");

                        foreach (var node in responseObject.Message.Split(','))
                            Node.AddNodeIP(node);

                        foreach (var chain in Blockchain.Blockchains)
                            if (chain.Chain != null)
                            {
                                if (responseObject.FirstHash == chain.Chain.First().Hash &&
                                    Startup.Blockchain != null && Startup.Blockchain.IsValid())
                                {
                                    if (responseObject.BlockCount > chain.Chain.Count)
                                        await GetRemoteChain(responseObject, chain, chain.Chain.Count);
                                }
                                else
                                {
                                    await GetRemoteChain(responseObject, chain, 0);
                                }
                            }
                    }
                    else
                    {
                        Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"Failed to parse or update nodes from peer {peer}");
                }
            else
                Logger.LogError($"Synchronization with peer {peer} failed or returned no response.");
        }
    }


    /// <summary>
    ///     Refactored BroadcastBlockToPeers to utilize SecureCommunication and log responses.
    /// </summary>
    public static async Task BroadcastBlockToPeers(ConcurrentList<string> serversList, List<Block> blocks,
        Blockchain blockchain)
    {
        var message = JsonSerializer.Serialize(blocks);

        var semaphore = new SemaphoreSlim(Config.Default.MaxParallelConnections);

        var tasks = serversList.Select(async peer =>
        {
            if (peer.Contains(Config.Default.URL) || peer.Contains(Config.Default.ResolvedURL))
                return;

            await semaphore.WaitAsync();
            try
            {
                var compressedMessage = Convert.ToBase64String(Compress.CompressString(message));
                var msg = ApiController.CreateChainInfo(compressedMessage);

                var (success, response) =
                    await SendSecureMessage(peer, "/api/NewBlocks", JsonSerializer.Serialize(msg));

                if (success)
                {
                    if (!string.IsNullOrEmpty(response))
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"Broadcast to {peer} successful. Response: {response}");

                        var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
                        if (responseObject != null)
                        {
                            if (Config.Default.Debug)
                                Logger.Log($"Broadcast to {peer} Result: {responseObject.Message}");
                        }
                        else
                        {
                            Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
                        }
                    }
                    else
                    {
                        Logger.LogError($"Broadcast to {peer} failed. Response is null.");
                    }
                }
                else
                {
                    Logger.LogError($"Broadcast to {peer} failed. SendSecureMessage failed");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static async Task GetRemoteChain(ChainInfo responseObject, Blockchain chain, int fromBlock)
    {
        if (chain.Chain == null)
            return;

        for (var block = fromBlock; block < responseObject.BlockCount; block++)
        {
            var (success, response) =
                await SendSecureMessage(responseObject.URL, "/api/GetBlock/" + block, Config.Default.URL);

            if (success && !string.IsNullOrEmpty(response))
                try
                {
                    var chainInfo = JsonSerializer.Deserialize<ChainInfo>(response);
                    if (chainInfo != null && Startup.Blockchain != null)
                    {
                        if (Config.Default.Debug)
                            Logger.Log($"GetBlock {block} from {responseObject.URL} Result: {responseObject.Message}");

                        if (!string.IsNullOrEmpty(chainInfo.Message))
                        {
                            var newBlock = Block.FromBase64(chainInfo.Message)!;
                            if (newBlock.Nonce == -1)
                                if (Startup.Blockchain.Chain != null)
                                    Startup.Blockchain.Clear();

                            Startup.Blockchain.AddBlock(newBlock, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"Failed to GetBlock {block} from {responseObject.URL} ");
                }
        }
    }


    /// <summary>
    ///     Retrieves the list of registered nodes from a specific server.
    /// </summary>
    /// <param name="serverAddress">The address of the server to query for registered nodes.</param>
    /// <returns>A list of node addresses retrieved from the server.</returns>
    public static async Task<List<string>> GetRegisteredNodesAsync(string serverAddress)
    {
        var ret = new List<string>();
        if (serverAddress.Contains(Config.Default.URL) || serverAddress.Contains(Config.Default.ResolvedURL))
            return ret;

        try
        {
            var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync("Nodes");

            if (response.ToLower().Contains("error"))
            {
                Logger.LogError($"Timeout from server {serverAddress}: {response}");
                Node.RemoveNodeIP(serverAddress);
                return ret;
            }

            var responseObject = JsonSerializer.Deserialize<ChainInfo>(response);
            if (responseObject == null)
            {
                Logger.LogError("ChainInfo Deserialize failed: Invalid response structure");
            }
            else
            {
                if (string.IsNullOrEmpty(responseObject.Message))
                {
                    if (Config.Default.Debug)
                        Logger.Log($"No new nodes received from {serverAddress}");
                    return ret;
                }

                foreach (var nodeAddress in responseObject.Message.Split(','))
                    if (!string.IsNullOrEmpty(nodeAddress))
                        ret.Add(nodeAddress);


                if (Config.Default.Debug)
                    Logger.Log($"Active nodes from server {serverAddress}: {responseObject.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex,
                $"GetRegisteredNodesAsync: retrieving registered nodes from {serverAddress} failed");
        }

        return ret;
    }

     
    /// <summary>
    ///     Sends a vote request to a target validator for a specific block.
    /// </summary>
    /// <param name="targetValidator">The address of the target validator.</param>
    /// <param name="block">The block to be validated.</param>
    /// <returns>
    ///     A tuple where the first value indicates if the request was successful, and the second contains the response
    ///     message.
    /// </returns>
    internal static async Task<(bool, string)> SendVoteRequestAsync(string targetValidator, Block? block)
    {
        try
        {
            if (block != null)
            {
                var verifiedBlock = Block.FromBase64(block.Base64Encoded);
                if (verifiedBlock != null)
                {
                    var hash = block.Hash;
                    var calculatedHash = block.CalculateHash();
                    if (calculatedHash == hash)
                    {
                        var message = $"Vote:{block.Base64Encoded}";
                        Logger.Log($"Sending vote request to: {targetValidator}");
                        var response = await SocketManager.GetInstance(targetValidator).SendMessageAsync(message);
                        if (Config.Default.Debug)
                            Logger.Log($"Response from {targetValidator}: {response}");
                        return (response.Contains("ok"), response);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, "sending vote request");
        }

        Logger.LogError($"block.Verify failed from {targetValidator}");
        return (false, "");
    }


    /// <summary>
    ///     Sends the serialized code of a smart contract to a server for verification.
    /// </summary>
    /// <param name="serverAddress">The address of the server for verification.</param>
    /// <param name="contract">The smart contract to be verified.</param>
    /// <returns>A boolean indicating whether the code verification was successful.</returns>
    internal static async Task<bool> SendCodeForVerificationAsync(string serverAddress, SmartContract? contract)
    {
        try
        {
            if (contract != null)
            {
                var message = $"VerifyCode:{contract.SerializedContractCode}";
                var response = await SocketManager.GetInstance(serverAddress).SendMessageAsync(message);
                if (Config.Default.Debug)
                {
                    Logger.Log($"Code {contract.Name} sent to {serverAddress} for verification.");
                    Logger.Log($"Response from server for code {contract.Name}: {response}", false);
                }

                return response == "ok";
            }

            Logger.LogError($"sending code to {serverAddress} failed: contract is empty");
        }
        catch (Exception ex)
        {
            if (contract != null)
                Logger.LogException(ex, $"sending code {contract.Name} to {serverAddress} failed");
        }

        return false;
    }
}