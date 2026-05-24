using System.Text.Json;

namespace LearnLink;

public static class AgentDebugLog
{
    private static readonly object SyncRoot = new();

    public static void AppendWithContentRoot(string contentRootPath, string tags, string source, string message, object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
            return;

        try
        {
            var logDirectory = Path.Combine(contentRootPath, "App_Data", "AgentDebugLog");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, "agent-debug.log");
            var entry = new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Tags = tags,
                Source = source,
                Message = message,
                Payload = payload
            };

            var line = JsonSerializer.Serialize(entry);

            lock (SyncRoot)
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }
}