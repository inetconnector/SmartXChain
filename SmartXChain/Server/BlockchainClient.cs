using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartXChain.Utils;
using SmartXChain.Validators;

namespace SmartXChain.Server;

public partial class BlockchainServer
{
    /// <summary>
    ///     Discovers peers from the configuration and registers them in the peer server list,
    ///     excluding the current server addresses.
    /// </summary>
    private void DiscoverAndRegisterWithPeers()
    {
        var validPeers = new List<string>();

        try
        {
            foreach (var peer in Config.Default.Peers)
                if (!string.IsNullOrEmpty(peer) && peer.StartsWith("http"))
                {
                    if (peer == Config.Default.URL) continue;
                    if (!_peerServers.Contains(peer))
                    {
                        _peerServers.Add(peer);
                        validPeers.Add(peer);
                    }
                }

            Logger.Log($"Static peers discovered: {string.Join(", ", validPeers.Count)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error processing static peers: {ex.Message}");
        }
    }
    /// <summary>
    ///     Continuously synchronizes with peer servers to update the list of active nodes.
    /// </summary>
    private async void SynchronizeWithPeers()
    {
        while (true)
        {
            foreach (var peer in _peerServers)
            {
                try
                {
                    // Initialize HTTP client for communication with the peer
                    using var client = new HttpClient();
                    client.BaseAddress = new Uri(peer);
                    if (Config.Default.SSL)
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());

                    // Send an empty request to the /api/GetNodes endpoint
                    var content = new StringContent(JsonSerializer.Serialize(""), Encoding.UTF8, "application/json");
                    var response = await client.PostAsync("/api/Nodes", content);

                    // If the response is successful, update the list of registered nodes
                    if (response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();

                        foreach (var node in responseBody.Split(','))
                            if (node.Contains("http"))
                                Node.AddNodeIP(node);
                    }
                    else
                    {
                        // Log any error with the response from the peer
                        Logger.Log($"Error synchronizing with peer {peer}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"ERROR: synchronizing with peer {peer} failed");
                }
            }

            // Wait for 20 seconds before the next synchronization cycle
            await Task.Delay(20000);
        }
    }

    /// <summary>
    ///     Broadcasts a message to a list of peer servers, targeting a specific API endpoint command.
    /// </summary>
    /// <param name="serversList">List of peer server URLs to send the message to.</param>
    /// <param name="command">The API command to invoke on each peer server.</param>
    /// <param name="message">The message content to be sent to the peers.</param>
    internal static async void BroadcastToPeers(ConcurrentBag<string> serversList, string command, string message)
    {
        var semaphore = new SemaphoreSlim(Config.Default.MaxParallelConnections);

        var tasks = serversList.Select(async peer =>
        {
            if (peer.Contains(Config.Default.URL))
                return;

            await semaphore.WaitAsync();
            try
            {
                var url = "";
                try
                {
                    // Initialize HTTP client for communication with the peer
                    using var client = new HttpClient { BaseAddress = new Uri(peer) };
                    if (Config.Default.SSL)
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());

                    // Prepare the message content to send to the specified API endpoint
                    var content = new StringContent(message);
                    url = $"/api/{command}";

                    // Log the broadcast request details
                    if (Config.Default.Debug)
                        Logger.Log($"BroadcastToPeers async: {url}\n{content}");
                    var response = await client.PostAsync(url, content);

                    // If the response is successful, log the response content
                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = await response.Content.ReadAsStringAsync();
                        if (Config.Default.Debug)
                            Logger.Log($"BroadcastToPeers response: {responseString}");
                    }
                    else
                    {
                        // Log an error message if the response status indicates failure
                        var error = $"ERROR: BroadcastToPeers {response.StatusCode} - {response.ReasonPhrase}";
                        Logger.Log(error);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"ERROR: BroadcastToPeers {url} failed");
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

}