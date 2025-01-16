using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SmartXChain.Utils;
using SmartXChain.Validators;

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
        var validPeers = new ConcurrentBag<string>();

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
            Logger.LogException(ex, $"processing static peers"); 
        }
    }

    /// <summary>
    ///     Continuously synchronizes with peer servers to update the list of active nodes.
    ///     Secure communication is ensured using encryption and signing.
    /// </summary>
    private async Task SynchronizeWithPeers()
    {
        while (true)
        {
            foreach (var peer in Node.CurrentNodeIPs)
            {
                if (peer == Config.Default.URL)
                    continue;
                try
                {
                    // Fetch the peer's public key (with caching)
                    var bobSharedKey = FetchPeerPublicKey(peer);

                    // Prepare secure payload
                    if (bobSharedKey != null)
                    {
                        var (encryptedMessage, iv, hmac) = SecurePeer.GetAlice(bobSharedKey)
                            .EncryptAndSign("");
                        var payload = new
                        {
                            SharedKey = Convert.ToBase64String(SecurePeer.Alice.GetPublicKey()),
                            EncryptedMessage = Convert.ToBase64String(encryptedMessage),
                            IV = Convert.ToBase64String(iv),
                            HMAC = Convert.ToBase64String(hmac)
                        };

                        // Initialize HTTP client for communication with the peer
                        using var client = new HttpClient();
                        client.BaseAddress = new Uri(peer);
                        if (Config.Default.SSL)
                            client.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());

                        // Send an empty secure request to the /api/Nodes endpoint
                        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8,
                            "application/json");
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
                            Logger.LogError($"Synchronizing with peer {peer} failed with {response.StatusCode} ");
                        }
                    }
                    else
                    {
                        Logger.LogError($"Synchronizing with peer {peer} failed");
                        Logger.LogError($"Could not retrieve public key from: {peer}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"Synchronizing with peer {peer} failed");
                }

            }

            // Wait for 20 seconds before the next synchronization cycle
            await Task.Delay(20000);
        }
    }
     
    /// <summary>
    ///     Broadcasts a message to a list of peer servers, targeting a specific API endpoint command.
    ///     Messages are securely encrypted and signed. Fetches the public key for each peer dynamically.
    /// </summary>
    /// <param name="serversList">List of peer server URLs to send the message to.</param>
    /// <param name="command">The API command to invoke on each peer server.</param>
    /// <param name="message">The message content to be sent to the peers.</param>
    public static async Task BroadcastToPeers(ConcurrentBag<string> serversList, string command, string message)
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
                    var bobSharedKey = FetchPeerPublicKey(peer);

                    // Encrypt and sign the message
                    if (bobSharedKey != null)
                    {
                        var (encryptedMessage, iv, hmac) =
                            SecurePeer.GetAlice(bobSharedKey).EncryptAndSign(message);

                        // Serialize message for transport
                        var payload = new
                        {
                            SharedKey = Convert.ToBase64String(SecurePeer.Alice.GetPublicKey()),
                            EncryptedMessage = Convert.ToBase64String(encryptedMessage),
                            IV = Convert.ToBase64String(iv),
                            HMAC = Convert.ToBase64String(hmac)
                        };

                        // Initialize HTTP client for communication with the peer
                        using var client = new HttpClient { BaseAddress = new Uri(peer) };
                        if (Config.Default.SSL)
                            client.DefaultRequestHeaders.Authorization =
                                new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());

                        // Prepare the message content to send to the specified API endpoint
                        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8,
                            "application/json");
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
                            Logger.LogError($"BroadcastToPeers {response.StatusCode} - {response.ReasonPhrase}");
                        }
                    }
                    else
                    {
                        Logger.LogError($"Could not retrieve public key from: {peer}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex, $"BroadcastToPeers {url} failed");
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
    ///     Fetches the public key of a peer for establishing secure communication.
    ///     Checks the cache first to avoid redundant fetching.
    /// </summary>
    /// <param name="peer">The URL of the peer.</param>
    /// <returns>The public key of the peer as a byte array, or null if retrieval fails.</returns>
    public static byte[]? FetchPeerPublicKey(string peer)
    {
        // Check the cache first
        if (PublicKeyCache.TryGetValue(peer, out var cachedKey))
            return cachedKey;

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(peer) };
            var response = client.GetAsync("/api/GetPublicKey").Result;

            if (response.IsSuccessStatusCode)
            {
                var responseBase64 = response.Content.ReadAsStringAsync().Result;
                var responseJson = Encoding.UTF8.GetString(Convert.FromBase64String(responseBase64));

                // Deserialize the JSON structure
                var responseObject = JsonSerializer.Deserialize<ResponseObject>(responseJson);
                if (responseObject == null)
                    throw new Exception("Invalid response structure");

                // Extract the values
                var publicKey = Convert.FromBase64String(responseObject.PublicKey);
                var dllFingerprint = responseObject.DllFingerprint;
                var chainId = responseObject.ChainID;

                if (dllFingerprint != Crypt.GenerateFileFingerprint(Assembly.GetExecutingAssembly().Location))
                {
                    if (!Config.ChainName.ToLower().Contains("test"))
                    {
                        Logger.LogError($"Failed to fetch public key (DLL fingerprint mismatch): {peer}");
                        return null;
                    }
                }

                if (chainId != Config.Default.ChainId)
                {
                    Logger.LogError($"ChainId mismatch: {peer}");
                    return null;
                } 

                // Cache the fetched public key
                PublicKeyCache[peer] = publicKey;

                return publicKey;
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Failed to fetch public key from peer: {peer}");
        }

        RemoveInactiveNodes();
        return null;
    }

    private class ResponseObject
    {
        public string PublicKey { get; set; } = string.Empty;
        public string DllFingerprint { get; set; } = string.Empty;
        public string ChainID { get; set; } = string.Empty;
    }
}