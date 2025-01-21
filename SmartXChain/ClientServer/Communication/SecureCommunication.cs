using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartXChain.Utils;

namespace SmartXChain.Server;

public partial class BlockchainServer
{
    /// <summary>
    ///     Sends a secure message to a peer's API endpoint.
    ///     Handles encryption, signing, and HTTP communication.
    /// </summary>
    /// <param name="peer">The URL of the peer server.</param>
    /// <param name="endpoint">The API endpoint to send the message to.</param>
    /// <param name="message">The plaintext message to send.</param>
    /// <returns>A tuple indicating success or failure and the response content (if any).</returns>
    public static async Task<(bool Success, string? Response)> SendSecureMessage(string peer, string endpoint,
        string message)
    {
        try
        {
            // Fetch the peer's public key
            var bobSharedKey = PeerCommunication.FetchPeerPublicKey(peer);

            if (bobSharedKey == null)
            {
                Logger.LogError($"Failed to retrieve public key from: {peer}");
                return (false, null);
            }

            // Encrypt and sign the message
            var (encryptedMessage, iv, hmac) = SecurePeer.GetAlice(bobSharedKey).EncryptAndSign(message);

            // Prepare the payload
            var payload = new
            {
                SharedKey = Convert.ToBase64String(SecurePeer.Alice.GetPublicKey()),
                EncryptedMessage = Convert.ToBase64String(encryptedMessage),
                IV = Convert.ToBase64String(iv),
                HMAC = Convert.ToBase64String(hmac)
            };

            // Initialize HTTP client
            using var client = new HttpClient { BaseAddress = new Uri(peer) };
            if (Config.Default.SSL)
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            // Send the request
            var response = await client.PostAsync(endpoint, content);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();

                if (Config.Default.Debug)
                {
                    Logger.Log($"Successfully sent secure message to {peer}{endpoint}");
                    Logger.Log($"Response: {responseString}");
                }

                // Deserialize and decrypt the response
                var responsePayload =
                    JsonSerializer.Deserialize<ApiController.SecurePayload>(responseString);
                var decryptedResponse = string.Empty;

                if (responsePayload != null)
                {
                    var alice = SecurePeer.GetAlice(Convert.FromBase64String(responsePayload.SharedKey));
                    decryptedResponse = alice.DecryptAndVerify(
                        Convert.FromBase64String(responsePayload.EncryptedMessage),
                        Convert.FromBase64String(responsePayload.IV),
                        Convert.FromBase64String(responsePayload.HMAC)
                    );
                }

                return (true, decryptedResponse);
            }

            Logger.LogError(
                $"Failed to send secure message to {peer}{endpoint}: {response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            Logger.LogException(ex, $"Error while sending secure message to {peer}{endpoint}");
        }

        return (false, null);
    }
}