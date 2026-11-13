using System.Globalization;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record AgentReminder(
    string Id, string Text, DateTimeOffset? At, string? DailyTime, string? TimeZone,
    bool AgentSpeech, bool Enabled, DateTimeOffset? NextDue, DateTimeOffset? LastFired);

/// <summary>Durable user reminders. The running pet delivers due items to the bubble.</summary>
public sealed class AgentReminderStore
{
    private readonly object gate = new();
    private readonly string path;
    private readonly List<AgentReminder> reminders;

    public AgentReminderStore(string root)
    {
        path = Path.Combine(Path.GetFullPath(root), "reminders.json");
        try { reminders = JsonSerializer.Deserialize<List<AgentReminder>>(File.ReadAllText(path)) ?? []; }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { reminders = []; }
        foreach (var reminder in reminders.Where(r => r.Enabled && r.NextDue == null))
        {
            int index = reminders.IndexOf(reminder);
            reminders[index] = reminder with { NextDue = NextAfter(reminder, DateTimeOffset.Now) };
        }
    }

    public AgentReminder[] List() { lock (gate) return reminders.OrderBy(r => r.NextDue ?? DateTimeOffset.MaxValue).ToArray(); }

    public AgentReminder Create(string text, DateTimeOffset? at, string? dailyTime, string? timeZone, bool agentSpeech, DateTimeOffset now)
    {
        text = (text ?? "").Trim();
        if (text.Length is < 1 or > 200) throw new ArgumentException("提醒内容必须为1–200个文字。");
        if ((at == null) == string.IsNullOrWhiteSpace(dailyTime)) throw new ArgumentException("一次性提醒使用 at，每日提醒使用 daily_time 和 time_zone，二者只能选一个。");
        if (dailyTime != null && !TimeOnly.TryParseExact(dailyTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("daily_time 必须为 HH:mm。");
        TimeZoneInfo? zone = null;
        if (dailyTime != null)
        {
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? ""); }
            catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new ArgumentException("时区无效。"); }
        }
        var reminder = new AgentReminder(Guid.NewGuid().ToString("N"), text, at, dailyTime, zone?.Id, agentSpeech, true, null, null);
        reminder = reminder with { NextDue = NextAfter(reminder, now) };
        lock (gate) { reminders.Add(reminder); Save(); }
        return reminder;
    }

    public bool Cancel(string id)
    {
        lock (gate)
        {
            bool removed = reminders.RemoveAll(r => r.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    public AgentReminder[] Due(DateTimeOffset now)
        => List().Where(r => r.Enabled && r.NextDue is { } due && due <= now).ToArray();

    public void Acknowledge(AgentReminder reminder, DateTimeOffset now)
    {
        lock (gate)
        {
            int index = reminders.FindIndex(r => r.Id == reminder.Id);
            if (index < 0) return;
            var current = reminders[index];
            reminders[index] = current.At != null
                ? current with { Enabled = false, NextDue = null, LastFired = now }
                : current with { NextDue = NextAfter(current, now), LastFired = now };
            Save();
        }
    }

    private static DateTimeOffset? NextAfter(AgentReminder reminder, DateTimeOffset now)
    {
        if (reminder.At is { } at) return at;
        if (reminder.DailyTime == null || reminder.TimeZone == null) return null;
        if (!TimeOnly.TryParseExact(reminder.DailyTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(reminder.TimeZone); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        for (int day = 0; day < 8; day++)
        {
            var local = DateTime.SpecifyKind(localNow.Date.AddDays(day) + time.ToTimeSpan(), DateTimeKind.Unspecified);
            if (day == 0 && local <= localNow.DateTime) continue;
            if (zone.IsInvalidTime(local)) continue;
            try
            {
                var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
                var candidate = new DateTimeOffset(local, offset);
                if (candidate > now) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    private void Save() => AtomicText.Write(path, JsonSerializer.Serialize(reminders, new JsonSerializerOptions { WriteIndented = true }));
}
