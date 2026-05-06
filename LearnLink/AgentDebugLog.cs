using System.Text.Json;

namespace LearnLink;

/// <summary>Debug-mode NDJSON logger for session b87d95 — remove after verified fix.</summary>
internal static class AgentDebugLog
{
    private const string SessionId = "b87d95";
    private const string LogFileName = "debug-b87d95.log";

    /// <summary>Workspace-level log path: parent of project ContentRoot + debug-b87d95.log</summary>
    public static string GetLogPath(string contentRoot)
        => Path.GetFullPath(Path.Combine(contentRoot, "..", LogFileName));

    /// <summary>Writes one NDJSON line. Never log secrets (keys, connection strings, tokens).</summary>
    public static void AppendWithContentRoot(string contentRoot, string hypothesisId, string location, string message, object? data = null, string runId = "pre-fix")
    {
        // #region agent log
        try
        {
            var path = GetLogPath(contentRoot);
            var payload = new Dictionary<string, object?>
            {
                ["sessionId"] = SessionId,
                ["runId"] = runId,
                ["hypothesisId"] = hypothesisId,
                ["location"] = location,
                ["message"] = message,
                ["data"] = data,
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            File.AppendAllText(path, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // ignore
        }
        // #endregion
    }
}
