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

            Logger.Log($"Static peers discovered: {string.Join(", ", validPeers)}");
        }
        catch (Exception ex)
        {
            Logger.Log($"Error processing static peers: {ex.Message}");
        }
    }

    /// <summary>
    ///     Removes inactive nodes that have exceeded the heartbeat timeout from the registry.
    /// </summary>
    private void RemoveInactiveNodes()
    {
        var now = DateTime.UtcNow;

        // Identify nodes that have exceeded the heartbeat timeout
        var inactiveNodes = Node.CurrentNodeIP_LastActive
            .Where(kvp => (now - kvp.Value).TotalSeconds > HeartbeatTimeoutSeconds)
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
    ///     Continuously synchronizes with peer servers to update the list of active nodes.
    /// </summary>
    private async void SynchronizeWithPeers()
    {
        while (true)
        {
            foreach (var peer in _peerServers)
                try
                {
                    if (Config.Default.SecurityProtocol == "Tls11")
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11;
                    else if (Config.Default.SecurityProtocol == "Tls12")
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;
                    else if (Config.Default.SecurityProtocol == "Tls13")
                        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;

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
        foreach (var peer in serversList)
        {
            if (peer.Contains(Config.Default.URL))
                continue;

            await Task.Run(async () =>
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
                        var responseString = response.Content.ReadAsStringAsync().Result;
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
            });
        }
    }
}