namespace SmartXChain.Utils;

public static class Logger
{
    public static void LogMessage(string message = "")
    {
        if (message.Contains("GetNodes") ||
            message.Contains("Heartbeat"))
            return;

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var formattedMessage = timestamp + " - " + message;

        if (formattedMessage.Length > 120)
            Console.WriteLine(formattedMessage.Substring(0, 120) + "...");
        else
            Console.WriteLine(formattedMessage);
    }
}