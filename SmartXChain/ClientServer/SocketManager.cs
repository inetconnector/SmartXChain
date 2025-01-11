using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartXChain.Utils;

namespace SmartXChain.Server
{
    /// <summary>
    ///     Manages asynchronous communication with a server using HTTP requests, implementing a message queue for processing.
    ///     Secure communication is ensured using encryption and signing via SecurePeer.
    /// </summary>
    public class SocketManager : IDisposable
    {
        private static readonly ConcurrentDictionary<string, SocketManager> _instances = new();
        private static readonly ConcurrentDictionary<string, byte[]> PublicKeyCache = new();

        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly BlockingCollection<(string, TaskCompletionSource<string>)> _messageQueue;
        private readonly Task _processingTask;

        private readonly object _sendLock = new();
        private readonly string _serverAddress;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SocketManager" /> class for a specific server address.
        /// </summary>
        /// <param name="serverAddress">The server address to communicate with.</param>
        private SocketManager(string serverAddress)
        {
            _serverAddress = serverAddress; 

            // Initialize the message queue and processing task
            _messageQueue = new BlockingCollection<(string, TaskCompletionSource<string>)>();
            _cancellationTokenSource = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessQueue(_cancellationTokenSource.Token));
        }

        /// <summary>
        ///     Disposes the current instance by canceling tasks, completing the message queue,
        ///     and removing it from the shared instance dictionary.
        /// </summary>
        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _messageQueue.CompleteAdding();
            _processingTask.Wait();

            // Remove this instance from the dictionary
            RemoveInstance(_serverAddress);

            Logger.Log("SocketManager disposed.");
        }

        /// <summary>
        ///     Retrieves or creates a singleton instance of <see cref="SocketManager" /> for a given server address.
        /// </summary>
        /// <param name="serverAddress">The server address for the instance.</param>
        /// <returns>A <see cref="SocketManager" /> instance associated with the server address.</returns>
        public static SocketManager GetInstance(string serverAddress)
        {
            return _instances.GetOrAdd(serverAddress, addr => new SocketManager(addr));
        }

        /// <summary>
        ///     Removes the instance associated with a specific server address from the shared instance dictionary.
        /// </summary>
        /// <param name="serverAddress">The server address for the instance to be removed.</param>
        public static void RemoveInstance(string serverAddress)
        {
            if (_instances.TryRemove(serverAddress, out var instance))
            {
                instance.Dispose();
                if (Config.Default.Debug)
                    Logger.Log($"SocketManager instance for '{serverAddress}' removed.");
            }
            else
            {
                if (Config.Default.Debug)
                    Logger.Log($"No SocketManager instance found for '{serverAddress}' to remove.");
            }
        }

        /// <summary>
        ///     Adds a message to the processing queue and waits asynchronously for the response.
        ///     The message is encrypted and signed before being sent.
        /// </summary>
        /// <param name="message">The message to send to the server.</param>
        /// <returns>A task that resolves to the server's response.</returns>
        public Task<string> SendMessageAsync(string message)
        {
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _messageQueue.Add((message, tcs));

            // Apply a timeout for the processing task
            Task.Delay(2000).ContinueWith(_ =>
            {
                if (!tcs.Task.IsCompleted) tcs.TrySetResult("ERROR: Timeout");
            });

            return tcs.Task;
        }

        /// <summary>
        ///     Processes messages from the queue and sends them to the server asynchronously.
        ///     Messages are encrypted and signed before being sent.
        /// </summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        private async Task ProcessQueue(CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(_serverAddress) };
                if (Config.Default.SSL)
                {
                    var token = new AuthenticationHeaderValue("Bearer", BearerToken.GetToken());
                    client.DefaultRequestHeaders.Authorization = token;
                }

                Logger.Log($"Connecting to server: {_serverAddress}");

                foreach (var (message, tcs) in _messageQueue.GetConsumingEnumerable(cancellationToken))
                {
                    lock (_sendLock)
                    {
                        var url = "";
                        try
                        {
                            // Fetch the peer's public key (with caching)
                            var bobSharedKey = BlockchainServer.FetchPeerPublicKey(_serverAddress);
                            if (bobSharedKey == null)
                            {
                                Logger.Log($"ERROR: Could not retrieve public key from: {_serverAddress}");
                                tcs.TrySetResult($"ERROR: Could not retrieve public key from {_serverAddress}");
                                continue;
                            } 

                            // Encrypt and sign the message
                            var (encryptedMessage, iv, hmac) = SecurePeer.GetAlice(bobSharedKey)
                                                                                            .EncryptAndSign(message);
  
                            // Serialize the payload
                            var payload = new
                            {
                                SharedKey= Convert.ToBase64String(SecurePeer.Alice.GetPublicKey()),
                                EncryptedMessage = Convert.ToBase64String(encryptedMessage),
                                IV = Convert.ToBase64String(iv),
                                HMAC = Convert.ToBase64String(hmac)
                            };

                            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8,
                                "application/json");
                            if (Config.Default.Debug)
                                Logger.Log($"Sending queued message to server: {message}");

                            url = "/api/" + message.Split(':')[0];

                            // Send message to the server's REST endpoint
                            var response = client.PostAsync(url, content).Result;

                            if (response.IsSuccessStatusCode)
                            {
                                var responseString = response.Content.ReadAsStringAsync().Result;
                                 
                                var bobPayload = JsonSerializer.Deserialize<BlockchainServer.ApiController.SecurePayload>(responseString);
                                if (bobPayload != null)
                                { 
                                    var bob = SecurePeer.GetAlice(bobPayload.SharedKey);

                                    responseString = bob.DecryptAndVerify(
                                        Convert.FromBase64String(bobPayload.EncryptedMessage),
                                        Convert.FromBase64String(bobPayload.IV),
                                        Convert.FromBase64String(bobPayload.HMAC)
                                    );
                                }
                                 

                                if (Config.Default.Debug)
                                    Logger.Log($"Received response: {responseString}");
                                tcs.TrySetResult(responseString);
                            }
                            else
                            {
                                var error = $"ERROR: {response.StatusCode} - {response.ReasonPhrase}";
                                Logger.Log(error);
                                tcs.TrySetResult(error);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException(ex, $"ERROR: send failed: {url}");
                            tcs.TrySetException(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Log("Processing queue canceled.");
            }
            catch (Exception ex)
            {
                Logger.LogException(ex, "ERROR: Critical error in ProcessQueue");
            }
        }
    }
}
