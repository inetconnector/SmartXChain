using System.Reflection;
using System.Text;
using System.Text.Json;
using SmartXChain.BlockchainCore;
using SmartXChain.Utils;

namespace SmartXChain.Server;

public partial class BlockchainServer
{
    /// <summary>
    ///     Utility class for peer-specific communication tasks.
    /// </summary>
    public static class PeerCommunication
    {
        /// <summary>
        ///     Fetches the public key of a peer for secure communication.
        /// </summary>
        /// <param name="peer">The URL of the peer.</param>
        /// <returns>The public key of the peer as a byte array, or null if retrieval fails.</returns>
        public static byte[]? FetchPeerPublicKey(string peer)
        {
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

                    var responseObject = JsonSerializer.Deserialize<ChainInfo>(responseJson);
                    if (responseObject == null)
                        throw new Exception("Invalid response structure");

                    var publicKey = Convert.FromBase64String(responseObject.PublicKey);

                    if (responseObject.DllFingerprint !=
                        Crypt.GenerateFileFingerprint(Assembly.GetExecutingAssembly().Location) &&
                        !Config.ChainName.ToString().ToLower().Contains("test"))
                    {
                        Logger.LogError($"DLL fingerprint mismatch: {peer}");
                        return null;
                    }

                    if (responseObject.ChainID != Config.Default.ChainId)
                    {
                        Logger.LogError($"ChainId mismatch: '{peer}' %  '{Config.Default.ChainId}'");
                        return null;
                    }

                    PublicKeyCache[peer] = publicKey;
                    return publicKey;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, $"Failed to fetch public key from {peer}");
            }

            return null;
        }
    }
}