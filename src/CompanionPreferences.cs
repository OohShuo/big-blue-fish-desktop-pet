using System.Text.Json;

namespace BigBlueFish;

internal sealed class CompanionPreferences
{
    public bool AgentEnabled { get; set; }
    public string AgentHarnessId { get; set; } = "codex";
    public bool AgentDailySpeech { get; set; }
    public bool AgentRespondToClick { get; set; }
    public bool AgentRespondToDrag { get; set; }
    public bool PuppetMode { get; set; }
    public bool WeatherAgentAlerts { get; set; }
    public string WeatherLabel { get; set; } = "";
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    public bool MouseInteractions { get; set; } = true;
    public bool HideOnFullscreen { get; set; } = true;
    public double Scale { get; set; } = 1;
    public double BubbleScale { get; set; } = 1;
    public int BubbleDurationIndex { get; set; } = 3;
    public bool SoftwareHighDefinition { get; set; }
    public bool LowPowerAnimation { get; set; }
    public int LowPowerIdleSeconds { get; set; } = 15;
    public int SpeechMinMinutes { get; set; } = 10;
    internal static readonly (int Min, int Max)[] SpeechRanges = [(2, 3), (3, 5), (5, 8), (10, 16), (15, 23), (20, 30)];
    internal int SpeechRangeIndex => Array.FindIndex(SpeechRanges, r => r.Min == NormalizeSpeechMinimum(SpeechMinMinutes));
    internal static int NormalizeSpeechMinimum(int value) => value switch
    {
        2 or 3 or 5 or 10 or 15 or 20 => value,
        <= 2 => 2,
        <= 4 => 3,
        <= 6 => 5,
        <= 12 => 10,
        <= 17 => 15,
        _ => 20
    };
    internal TimeSpan NextSpeechDelay(Random random)
    {
        var range = SpeechRanges[SpeechRangeIndex < 0 ? 3 : SpeechRangeIndex];
        return TimeSpan.FromMinutes(range.Min + random.NextDouble() * (range.Max - range.Min));
    }
    public int BreakfastHour { get; set; } = 8;
    public int LunchHour { get; set; } = 12;
    public int DinnerHour { get; set; } = 18;
    public string? LastBrushDate { get; set; }
    public double? Left { get; set; }
    public double? Top { get; set; }
    internal static readonly int[] BubbleDurations = [5, 10, 15, 30, 60, 120, 180, 0];
    internal static readonly string[] BubbleDurationLabels = ["5秒", "10秒", "15秒", "30秒", "1分钟", "2分钟", "3分钟", "直至关闭"];

    public static CompanionPreferences Read()
    {
        foreach (var path in new[] { "preferences.json", "preferences.json.bak" })
        {
            try
            {
                var value = JsonSerializer.Deserialize<CompanionPreferences>(File.ReadAllText(path));
                if (value == null) continue;
                value.Scale = double.IsFinite(value.Scale) ? Math.Clamp(value.Scale, .5, 2) : 1;
                value.BubbleScale = double.IsFinite(value.BubbleScale) ? Math.Clamp(value.BubbleScale, .5, 2) : 1;
                if (value.Left.HasValue && !double.IsFinite(value.Left.Value)) value.Left = null;
                if (value.Top.HasValue && !double.IsFinite(value.Top.Value)) value.Top = null;
                value.LowPowerIdleSeconds = Math.Clamp(value.LowPowerIdleSeconds, 3, 120);
                value.SpeechMinMinutes = NormalizeSpeechMinimum(value.SpeechMinMinutes);
                if (!HarnessCatalogIds.Contains(value.AgentHarnessId)) value.AgentHarnessId = "codex";
                if (!value.AgentDailySpeech) value.PuppetMode = false;
                if (value.WeatherLatitude.HasValue && (!double.IsFinite(value.WeatherLatitude.Value) || value.WeatherLatitude.Value is < -90 or > 90)) value.WeatherLatitude = null;
                if (value.WeatherLongitude.HasValue && (!double.IsFinite(value.WeatherLongitude.Value) || value.WeatherLongitude.Value is < -180 or > 180)) value.WeatherLongitude = null;
                if (value.WeatherLatitude == null || value.WeatherLongitude == null) { value.WeatherLatitude = null; value.WeatherLongitude = null; value.WeatherAgentAlerts = false; }
                if (value.WeatherLabel.Length > 120) value.WeatherLabel = value.WeatherLabel[..120];
                value.BreakfastHour = Math.Clamp(value.BreakfastHour, 0, 23);
                value.LunchHour = Math.Clamp(value.LunchHour, 0, 23);
                value.DinnerHour = Math.Clamp(value.DinnerHour, 0, 23);
                value.BubbleDurationIndex = Math.Clamp(value.BubbleDurationIndex, 0, BubbleDurations.Length - 1);
                return value;
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return new();
    }

    static readonly string[] HarnessCatalogIds = ["claude", "codex", "opencode"];
    public void Save()
    {
        const string temporary = "preferences.json.tmp", target = "preferences.json";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(target)) File.Replace(temporary, target, "preferences.json.bak");
        else File.Move(temporary, target);
    }
}
