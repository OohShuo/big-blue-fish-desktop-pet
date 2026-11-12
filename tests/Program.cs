using BigBlueFish;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var assets = Path.GetFullPath(args[0]);
var policy = CompanionAnimationPolicy.Load(assets);
Check(policy.Rules.Count == 106, "All animations retained");
Check(policy.FestivalDistance(new DateTime(2026, 2, 17), "SpringFestival") == 0, "Lunar New Year");
Check(policy.FestivalDistance(new DateTime(2026, 12, 25), "Christmas") == 0, "Christmas");
Check(CompanionAnimationPolicy.Season(new DateTime(2026, 7, 1)) == "Summer", "Summer");
var random = new Random(42);
foreach (var month in Enumerable.Range(1, 12))
foreach (var hour in new[] { 0, 8, 12, 18, 23 })
foreach (var pool in new[] { "Idle", "Click", "Event", "Automatic" })
{
    var context = new CompanionAnimationContext(new DateTime(2026, month, 15, hour, 20, 0));
    var chosen = policy.Choose(pool, context, random);
    Check((pool == "Automatic" ? chosen.Pool is "Idle" or "Event" : chosen.Pool == pool) && policy.Weight(chosen, context) > 0, "Valid weighted selection");
}
var meal = new CompanionAnimationRule { Meal = "Breakfast", MealBoost = 50, Weight = 10 };
Check(policy.Weight(meal, new(new DateTime(2026, 1, 1, 8, 0, 0))) == 60, "Breakfast boost");
Check(policy.Weight(meal, new(new DateTime(2026, 1, 1, 15, 0, 0))) == 10, "Outside meal");
var night = new CompanionAnimationRule { NightOnly = true };
Check(policy.Weight(night, new(new DateTime(2026, 1, 1, 12, 0, 0))) == 0, "No daytime sleep");

var disabled = policy.Rules.Where(r => r.Pool == "Disabled").Select(r => r.Id).ToHashSet();
Check(disabled.SetEquals(new[] { "余额-分文不剩", "余额-数金皱眉", "余额-袋空如洗", "工作状态-垂头叹气冒汗" }), "Same four clips stay disabled");
Check(policy.Rules.Count(r => r.Pool is "Idle" or "Event") == 94, "94 merged idle/event entries");
var specialProperties = new[] { "NightOnly", "SeasonOnly", "SummerWeight", "WinterWeight", "MorningWeight", "Meal", "Festival", "MoveKind" };
using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "animations.json"))))
{
    foreach (var entry in data.RootElement.EnumerateArray())
    {
        if (entry.GetProperty("Pool").GetString() is not ("Idle" or "Event")) continue;
        if (specialProperties.Any(key => entry.TryGetProperty(key, out _))) continue;
        Check(entry.GetProperty("Weight").GetDouble() == 100, "Every ordinary idle/event action has the same weight");
    }
}
var automaticDraws = Enumerable.Range(0, 2000).Select(_ => policy.Choose("Automatic", new(new DateTime(2026, 11, 12, 12, 20, 0)), random)).ToArray();
Check(automaticDraws.Any(r => r.Pool == "Idle") && automaticDraws.Any(r => r.Pool == "Event"), "Merged pool draws from both groups");
Check(automaticDraws.All(r => !disabled.Contains(r.Id)), "Disabled clips never drawn");

var original = Environment.CurrentDirectory;
var temporary = Path.Combine(Path.GetTempPath(), "bigbluefish-tests-" + Guid.NewGuid());
Directory.CreateDirectory(temporary);
try
{
    Environment.CurrentDirectory = temporary;
    Check(CompanionPreferences.Read().Scale == 1, "Default settings");
    Check(!CompanionPreferences.Read().SoftwareHighDefinition, "Default software quality stays low");
    File.WriteAllText("preferences.json", "{\"Scale\":99,\"LowPowerIdleSeconds\":-5,\"BreakfastHour\":40,\"ModelEnabled\":true}");
    var preferences = CompanionPreferences.Read();
    Check(preferences.Scale == 2 && preferences.LowPowerIdleSeconds == 3, "Clamp legacy settings");
    preferences.SoftwareHighDefinition = true;
    preferences.LowPowerAnimation = true;
    preferences.Save(); preferences.Save();
    Check(!File.ReadAllText("preferences.json").Contains("Model"), "Legacy fields dropped on save");
    Check(CompanionPreferences.Read().BreakfastHour == 23, "Meal settings remain available");
    Check(CompanionPreferences.Read().SoftwareHighDefinition, "Software quality persists");
    File.WriteAllText("preferences.json", "broken");
    Check(CompanionPreferences.Read().LowPowerAnimation, "Backup recovery");
    File.WriteAllText("preferences.json.bak", "broken");
    Check(CompanionPreferences.Read().Scale == 1, "Corrupt settings fallback");
}
finally { Environment.CurrentDirectory = original; Directory.Delete(temporary, true); }

// Exercise the real distribution's software pipeline, including alpha and seek.
foreach (bool highDefinition in new[] { false, true })
{
    var originalClip = Path.Combine(assets, policy.Find("女仆屈膝礼仪").File);
    var source = AnimationSource.Software(originalClip, highDefinition);
    Check(source.Width == (highDefinition ? 1280 : 640), "Software output resolution");
    Check(source.File == (highDefinition ? originalClip : Path.Combine(assets, "compat", Path.GetFileName(originalClip))), "Software source selection");
    using (var stream = new CompanionMediaStream(source.File, false, source.Width, source.Height, 1.5, 2.0, 1))
    {
        stream.SetPaused(false);
        int frames = 0; bool transparent = false, opaque = false;
        var limit = DateTime.UtcNow.AddSeconds(15);
        while (!stream.Ended && DateTime.UtcNow < limit)
        {
            using var frame = stream.Read();
            if (frame == null) continue;
            frames++;
            Check(frame.Stride >= source.Width * 4 && frame.Seconds >= 1.4, "Frame dimensions and seek");
            var row = new byte[frame.Stride * frame.Height];
            System.Runtime.InteropServices.Marshal.Copy(frame.Data, row, 0, row.Length);
            for (int i = 3; i < row.Length; i += 4) { transparent |= row[i] == 0; opaque |= row[i] > 200; }
        }
        Check(frames > 0 && stream.Ended, "Pipeline completion");
        Check(transparent && opaque, "Alpha preserved");
    }
}
Console.WriteLine($"PASS: {checks} checks (calendar, selection, settings recovery, real GStreamer decode).");
