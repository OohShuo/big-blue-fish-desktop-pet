using System.Text.Json;

namespace BigBlueFish;

internal sealed class CompanionPreferences
{
    public bool HideOnFullscreen { get; set; } = true;
    public double Scale { get; set; } = 1;
    public double BubbleScale { get; set; } = 1;
    public int BubbleDurationIndex { get; set; } = 3;
    public bool SoftwareHighDefinition { get; set; }
    public bool LowPowerAnimation { get; set; }
    public int LowPowerIdleSeconds { get; set; } = 15;
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

    public void Save()
    {
        const string temporary = "preferences.json.tmp", target = "preferences.json";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(target)) File.Replace(temporary, target, "preferences.json.bak");
        else File.Move(temporary, target);
    }
}
