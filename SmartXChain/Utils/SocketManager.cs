using System.Collections.Concurrent;
using System.Text;

namespace SmartXChain.Utils;

/// <summary>
///     Manages asynchronous communication with a server using HTTP requests, implementing a message queue for processing.
/// </summary>
public class SocketManager : IDisposable
{
    private static readonly ConcurrentDictionary<string, SocketManager> _instances = new();
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
        var keyToRemove = _instances.FirstOrDefault(kv => kv.Value == this).Key;
        if (keyToRemove != null) _instances.TryRemove(keyToRemove, out _);

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
    ///     Adds a message to the processing queue and waits asynchronously for the response.
    /// </summary>
    /// <param name="message">The message to send to the server.</param>
    /// <returns>A task that resolves to the server's response.</returns>
    public Task<string> SendMessageAsync(string message)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageQueue.Add((message, tcs));

        // Apply a timeout for the processing task
        Task.Delay(5000).ContinueWith(_ =>
        {
            if (!tcs.Task.IsCompleted) tcs.TrySetResult("ERROR: Timeout");
        });

        return tcs.Task;
    }

    /// <summary>
    ///     Processes messages from the queue and sends them to the server asynchronously.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    private async Task ProcessQueue(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(_serverAddress) };
            Logger.Log($"HTTP client initialized with server: {_serverAddress}");

            foreach (var (message, tcs) in _messageQueue.GetConsumingEnumerable(cancellationToken))
                lock (_sendLock)
                {
                    try
                    {
                        var content = new StringContent(message, Encoding.UTF8, "application/json");
                        if (Config.Default.Debug)
                            Logger.Log($"Sending queued message to server: {message}");

                        // Send message to the server's REST endpoint
                        var response = httpClient.PostAsync("/api/" + message.Split(':')[0], content).Result;

                        if (response.IsSuccessStatusCode)
                        {
                            var responseString = response.Content.ReadAsStringAsync().Result;
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
                        // Handle exceptions during message processing
                        Logger.Log($"Error processing message: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                }
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Processing queue canceled.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Critical error in ProcessQueue: {ex.Message}");
        }
    }
}