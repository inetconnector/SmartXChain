namespace SmartXChain.Utils;

/// <summary>
///     Provides a utility for logging messages to the console with formatting and filtering capabilities.
/// </summary>
public static class Logger
{
    /// <summary>
    ///     Logs a message to the console with a timestamp, excluding specific messages based on predefined filters.
    /// </summary>
    /// <param name="message">The message to log. Defaults to an empty string.</param>
    public static void LogMessage(string message = "")
    {
        // Skip logging messages containing "GetNodes" or "Heartbeat"
        if (message.Contains("GetNodes") || message.Contains("Heartbeat"))
            return;

        // Format the message with a timestamp
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var formattedMessage = timestamp + " - " + message;

        // Print the message to the console, truncating if it exceeds 100 characters
        if (formattedMessage.Length > 110)
            Console.WriteLine(formattedMessage.Substring(0, 110) + "...");
        else
            Console.WriteLine(formattedMessage);
    }
}