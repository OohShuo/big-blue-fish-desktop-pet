namespace BigBlueFish.Diagnostics;

public enum DiagnosticEvent
{
    AppStarted, AppStopped, SettingsOpened, SettingsClosed, SettingsChanged, VisibilityChanged,
    SessionRecovery, Interaction, AnimationStarted, AnimationCompleted, AnimationFailed, TrayState, AppFailed,
    SpeechRequested, SpeechSkipped, SpeechStarted, SpeechCompleted, SpeechCancelled, SpeechFailed,
    ScreenReadStarted, ScreenReadCompleted, ScreenReadFallback, BubbleShown, BubbleClosed,
    AgentConnecting, AgentConnected, AgentDisconnected, AgentRequest, AgentMaterials, AgentReply,
    AgentCancelled, AgentFailed, AgentPermission, AgentConfiguration, AgentWorkspace, McpTool
}

/// <summary>
/// Privacy-safe diagnostic sink. The ACP/control layer only records event categories and
/// never prompt text, screenshots, credentials or exception messages.
/// </summary>
public static class DiagnosticLog
{
    public static void ConfigureDirectories(string appDirectory, string dataDirectory) { }
    public static void ObserveSettings(object settings) { }
    public static void Write(DiagnosticEvent code, string? kind = null, string? state = null, int? count = null,
        int? chars = null, double? elapsedMs = null, bool? enabled = null, Exception? error = null,
        string? action = null, string? directory = null, IEnumerable<string>? changed = null)
    {
        if (Environment.GetEnvironmentVariable("BIGBLUEFISH_DIAGNOSTICS") != "1") return;
        string fields = string.Join(" ", new[] { kind is null ? null : "kind=" + kind, state is null ? null : "state=" + state, count is null ? null : "count=" + count }.Where(x => x != null));
        Console.Error.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{code}] {fields}");
    }
}
