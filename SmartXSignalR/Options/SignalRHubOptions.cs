namespace SmartXSignalR.Options;

/// <summary>
///     Configures runtime behaviour for the SmartX SignalR hub.
/// </summary>
public class SignalRHubOptions
{
    /// <summary>
    ///     Maximum seconds to wait for a node to respond with an SDP offer.
    /// </summary>
    public int OfferResponseTimeoutSeconds { get; set; } = 15;

    /// <summary>
    ///     Maximum age in seconds for a cached SDP offer before a refresh is forced.
    /// </summary>
    public int OfferCacheDurationSeconds { get; set; } = 30;

    /// <summary>
    ///     Maximum number of concurrent pending offer requests that will be tracked by the hub.
    /// </summary>
    public int MaxPendingOfferRequests { get; set; } = 2048;
}
