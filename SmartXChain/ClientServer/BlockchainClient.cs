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
                {
                    if (peer == Config.Default.URL) continue;
                    if (!Node.CurrentNodeIPs.Contains(peer))
                    {
                        Node.CurrentNodeIPs.Add(peer);
                        validPeers.Add(peer);
                    }
                }

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
            foreach (var peer in Node.CurrentNodeIPs)
            {
                if (peer == Config.Default.URL)
                    continue;

                var (success, response) = await SendSecureMessage(peer, "/api/Nodes", "");

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
                                if (chain.Chain != null &&
                                    (responseObject.BlockCount < chain.Chain.Count ||
                                     (responseObject.BlockCount == chain.Chain.Count &&
                                      responseObject.LastHash != chain.Chain.Last().Hash &&
                                      responseObject.FirstHash != chain.Chain.First().Hash &&
                                      responseObject.LastDate > chain.Chain.Last().Timestamp)
                                    ))
                                    await chain.SendChainInChunks(peer, responseObject);
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

            await Task.Delay(30000);
        }
    }

    /// <summary>
    ///     Refactored BroadcastBlockToPeers to utilize SecureCommunication and log responses.
    /// </summary>
    public static async Task BroadcastBlockToPeers(ConcurrentList<string> serversList, string message,
        Blockchain blockchain)
    {
        var semaphore = new SemaphoreSlim(Config.Default.MaxParallelConnections);

        var tasks = serversList.Select(async peer =>
        {
            if (peer.Contains(Config.Default.URL))
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

                            else if (responseObject.Message.ToLower().Contains("error"))
                                if (blockchain.Chain != null &&
                                    (responseObject.BlockCount < blockchain.Chain.Count ||
                                     (responseObject.BlockCount == blockchain.Chain.Count &&
                                      responseObject.LastHash != blockchain.Chain.Last().Hash &&
                                      responseObject.LastDate > blockchain.Chain.Last().Timestamp)
                                    ))
                                    await blockchain.SendChainInChunks(peer, responseObject);
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


    /// <summary>
    ///     Retrieves the list of registered nodes from a specific server.
    /// </summary>
    /// <param name="serverAddress">The address of the server to query for registered nodes.</param>
    /// <returns>A list of node addresses retrieved from the server.</returns>
    public static async Task<List<string>> GetRegisteredNodesAsync(string serverAddress)
    {
        var ret = new List<string>();
        if (serverAddress.Contains(Config.Default.URL))
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
    ///     Reboot client chains
    /// </summary>
    /// <returns></returns>
    public static async Task RebootChainsAsync()
    {
        if (Config.TestNet)
            foreach (var serverAddress in Node.CurrentNodeIPs)
            {
                if (serverAddress == Config.Default.URL)
                    continue;

                try
                {
                    var response = await SocketManager.GetInstance(serverAddress)
                        .SendMessageAsync($"RebootChain:{serverAddress}");

                    if (Config.Default.Debug)
                    {
                        Logger.Log($"RebootChain sent to {serverAddress}");
                        Logger.Log($"Response from server {serverAddress}: {response}");
                    }

                    if (!string.IsNullOrEmpty(response))
                        Logger.LogError($"No response received from {serverAddress}");
                    else
                        Logger.Log($"Shutdown initiated {serverAddress}");
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"RebootChainsAsync: sending Shutdown command to {serverAddress} failed");
                }
            }
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