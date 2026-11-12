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
foreach (var pool in new[] { "Idle", "Click", "Event" })
{
    var context = new CompanionAnimationContext(new DateTime(2026, month, 15, hour, 20, 0));
    var chosen = policy.Choose(pool, context, random);
    Check(chosen.Pool == pool && policy.Weight(chosen, context) > 0, "Valid weighted selection");
}
var meal = new CompanionAnimationRule { Meal = "Breakfast", MealBoost = 50, Weight = 10 };
Check(policy.Weight(meal, new(new DateTime(2026, 1, 1, 8, 0, 0))) == 60, "Breakfast boost");
Check(policy.Weight(meal, new(new DateTime(2026, 1, 1, 15, 0, 0))) == 10, "Outside meal");
var night = new CompanionAnimationRule { NightOnly = true };
Check(policy.Weight(night, new(new DateTime(2026, 1, 1, 12, 0, 0))) == 0, "No daytime sleep");

var original = Environment.CurrentDirectory;
var temporary = Path.Combine(Path.GetTempPath(), "bigbluefish-tests-" + Guid.NewGuid());
Directory.CreateDirectory(temporary);
try
{
    Environment.CurrentDirectory = temporary;
    Check(CompanionPreferences.Read().Scale == 1, "Default settings");
    File.WriteAllText("preferences.json", "{\"Scale\":99,\"LowPowerIdleSeconds\":-5,\"BreakfastHour\":40,\"ModelEnabled\":true}");
    var preferences = CompanionPreferences.Read();
    Check(preferences.Scale == 2 && preferences.LowPowerIdleSeconds == 3 && preferences.BreakfastHour == 23, "Clamp legacy settings");
    preferences.LowPowerAnimation = true;
    preferences.Save(); preferences.Save();
    Check(!File.ReadAllText("preferences.json").Contains("Model"), "Legacy fields dropped on save");
    File.WriteAllText("preferences.json", "broken");
    Check(CompanionPreferences.Read().LowPowerAnimation, "Backup recovery");
    File.WriteAllText("preferences.json.bak", "broken");
    Check(CompanionPreferences.Read().Scale == 1, "Corrupt settings fallback");
}
finally { Environment.CurrentDirectory = original; Directory.Delete(temporary, true); }

// Exercise the real distribution's software pipeline, including alpha and seek.
var clip = Path.Combine(assets, "compat", policy.Find("女仆屈膝礼仪").File);
using (var stream = new CompanionMediaStream(clip, false, 640, 360, 1.5, 2.0, 1))
{
    stream.SetPaused(false);
    int frames = 0; bool transparent = false, opaque = false;
    var limit = DateTime.UtcNow.AddSeconds(15);
    while (!stream.Ended && DateTime.UtcNow < limit)
    {
        using var frame = stream.Read();
        if (frame == null) continue;
        frames++;
        Check(frame.Stride >= 640 * 4 && frame.Seconds >= 1.4, "Frame dimensions and seek");
        var row = new byte[frame.Stride * frame.Height];
        System.Runtime.InteropServices.Marshal.Copy(frame.Data, row, 0, row.Length);
        for (int i = 3; i < row.Length; i += 4) { transparent |= row[i] == 0; opaque |= row[i] > 200; }
    }
    Check(frames > 0 && stream.Ended, "Pipeline completion");
    Check(transparent && opaque, "Alpha preserved");
}
Console.WriteLine($"PASS: {checks} checks (calendar, selection, settings recovery, real GStreamer decode).");
