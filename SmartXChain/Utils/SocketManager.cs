using System.Collections.Concurrent;
using NetMQ;
using NetMQ.Sockets;
using SmartXChain.Utils;

public class SocketManager : IDisposable
{
    private static readonly ConcurrentDictionary<string, SocketManager> _instances = new();
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly BlockingCollection<(string, TaskCompletionSource<string>)> _messageQueue;
    private readonly Task _processingTask;

    private readonly object _sendLock = new();
    private readonly string _serverAddress;

    private SocketManager(string serverAddress)
    {
        _serverAddress = serverAddress;

        _messageQueue = new BlockingCollection<(string, TaskCompletionSource<string>)>();
        _cancellationTokenSource = new CancellationTokenSource();
        _processingTask = Task.Run(() => ProcessQueue(_cancellationTokenSource.Token));
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _messageQueue.CompleteAdding();
        _processingTask.Wait();


        // Entferne die Instanz aus dem Dictionary
        var keyToRemove = _instances.FirstOrDefault(kv => kv.Value == this).Key;
        if (keyToRemove != null) _instances.TryRemove(keyToRemove, out _);

        Console.WriteLine("SocketManager disposed.");
    }

    // Singleton Factory Methode
    public static SocketManager GetInstance(string serverAddress)
    {
        return _instances.GetOrAdd(serverAddress, addr => new SocketManager(addr));
    }

    public Task<string> SendMessageAsync(string message)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messageQueue.Add((message, tcs));

        // Timeout für die Verarbeitung
        Task.Delay(5000).ContinueWith(_ =>
        {
            if (!tcs.Task.IsCompleted) tcs.TrySetResult("ERROR: Timeout");
        });

        return tcs.Task;
    }

    private void ProcessQueue(CancellationToken cancellationToken)
    {
        try
        {
            using var socket = new RequestSocket();
            try
            {
                socket.Connect(_serverAddress);
                Console.WriteLine($"Connected to server: {_serverAddress}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to server {_serverAddress}: {ex.Message}");
                throw;
            }

            foreach (var (message, tcs) in _messageQueue.GetConsumingEnumerable(cancellationToken))
                lock (_sendLock)
                {
                    try
                    {
                        // Nachricht senden
                        socket.SendFrame(Crypt.GetExecutingAssemblyFingerprint() + "#" + message);

                        // Antwort empfangen mit Timeout
                        var response = socket.TryReceiveFrameString(TimeSpan.FromSeconds(5), out var frame)
                            ? frame
                            : "ERROR: Timeout";

                        tcs.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing message: {ex.Message}");
                        tcs.TrySetException(ex);
                    }
                }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Processing queue canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical error in ProcessQueue: {ex.Message}");
        }
    }
}