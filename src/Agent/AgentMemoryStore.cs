using System.Text;
using System.Text.Json;

namespace BigBlueFish.Agent;

public sealed record AgentMemory(string Key, string Text, DateTimeOffset Updated);

/// <summary>Small explicitly-requested memory store. It never records chat transcripts.</summary>
public sealed class AgentMemoryStore
{
    private const int Limit = 200;
    private readonly object gate = new();
    private readonly string path;
    private readonly List<AgentMemory> memories;

    public AgentMemoryStore(string root)
    {
        path = Path.Combine(Path.GetFullPath(root), "memories.json");
        try
        {
            memories = JsonSerializer.Deserialize<List<AgentMemory>>(File.ReadAllText(path)) ?? [];
            memories = memories.Where(m => m != null && !string.IsNullOrWhiteSpace(m.Key) && !string.IsNullOrWhiteSpace(m.Text))
                .TakeLast(Limit).ToList();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { memories = []; }
    }

    public AgentMemory[] List() { lock (gate) return memories.OrderByDescending(m => m.Updated).ToArray(); }

    public AgentMemory Remember(string key, string text, DateTimeOffset now)
    {
        key = (key ?? "").Trim(); text = (text ?? "").Trim();
        if (key.Length is < 1 or > 64 || key.Any(char.IsControl)) throw new ArgumentException("记忆名称必须为1–64个文字。");
        if (text.Length is < 1 or > 1000 || text.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw new ArgumentException("记忆内容必须为1–1000个文字。");
        lock (gate)
        {
            memories.RemoveAll(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var entry = new AgentMemory(key, text, now);
            memories.Add(entry);
            if (memories.Count > Limit) memories.RemoveRange(0, memories.Count - Limit);
            Save();
            return entry;
        }
    }

    public bool Forget(string key)
    {
        lock (gate)
        {
            bool removed = memories.RemoveAll(m => m.Key.Equals((key ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed) Save();
            return removed;
        }
    }

    /// <summary>Bounded snapshot injected into each Agent turn. The Agent decides what is relevant.</summary>
    public string PromptText(int maximum = 4000)
    {
        lock (gate)
        {
            if (memories.Count == 0) return "";
            var text = new StringBuilder();
            foreach (var memory in memories.OrderByDescending(m => m.Updated))
            {
                string line = "- " + memory.Key + "：" + memory.Text.Replace("\r", " ").Replace("\n", " ") + "\n";
                if (text.Length + line.Length > maximum) break;
                text.Append(line);
            }
            return text.ToString();
        }
    }

    private void Save() => AtomicText.Write(path, JsonSerializer.Serialize(memories, new JsonSerializerOptions { WriteIndented = true }));
}
