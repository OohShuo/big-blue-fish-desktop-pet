using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using BigBlueFish;
using BigBlueFish.Agent;
using BigBlueFish.Control;

internal static class AgentTests
{
    internal static async Task<int> Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
        string temp = Path.Combine(Path.GetTempPath(), "fish-acp-tests-" + Guid.NewGuid()); Directory.CreateDirectory(temp);
        try
        {
            Check(HarnessCatalog.All.Count == 3 && HarnessCatalog.All.Select(x => x.Id).SequenceEqual(["claude", "codex", "opencode"]), "Only Claude, Codex and OpenCode are offered");
            Check(HarnessCatalog.Get("claude").AdapterPackage != null && HarnessCatalog.Get("codex").AdapterPackage != null && HarnessCatalog.Get("opencode").AdapterPackage == null, "Adapter policy is explicit");
            Check(HarnessCatalog.Get("opencode").Arguments.SequenceEqual(["acp"]), "OpenCode uses native ACP");
            Check(HarnessCatalog.FindExecutable("fish-no-such-cli") == null, "Missing CLI is not invented");

            var legacy = AgentAuthenticationMethod.Parse(JsonSerializer.SerializeToElement(new
            {
                authMethods = new[] { new { id = "login", name = "Kimi login", _meta = new Dictionary<string, object> { ["terminal-auth"] = new { command = "untrusted", args = new[] { "login" }, env = new Dictionary<string, string>(), type = "terminal" } } } }
            }), true);
            Check(legacy.Length == 1 && legacy[0].Type == "terminal" && legacy[0].ReplaceArguments && legacy[0].Arguments.SequenceEqual(["login"]), "Legacy terminal auth normalized");

            var skills = new AgentSkillStore(Path.Combine(temp, "skill"), HarnessCatalog.Get("claude"), Path.Combine(temp, "workspace"));
            skills.Install();
            Check(File.Exists(skills.SkillPath) && File.ReadAllText(skills.SkillPath).Contains("name: big-blue-fish"), "Skill installs in the selected workspace");
            Check(File.Exists(skills.PersonaPath) && File.ReadAllText(skills.PersonaPath).Contains("蓝色大肥鱼"), "Persona installs");
            AtomicText.Write(skills.PersonaPath, "用户自定义人设");
            skills.Install();
            Check(File.ReadAllText(skills.PersonaPath) == "用户自定义人设", "User persona survives reinstall");
            var skillText = File.ReadAllText(skills.SkillPath);
            Check(skillText.Contains("没有本地大模型") && skillText.Contains("AgentRespondToClick") && skillText.Contains("AgentRespondToDrag"), "Skill describes current edition boundaries and response switches");
            Check(skillText.Contains("memory_remember") && skillText.Contains("reminder_create") && skillText.Contains("get_weather"), "Skill advertises memory, reminder and weather tools");

            var jobsRoot = Path.Combine(temp, "jobs"); var jobs = new AgentWakeups(jobsRoot);
            var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(8));
            var one = jobs.Schedule("claude", "s1", "care", 30, null, null, now);
            var repeat = jobs.Schedule("claude", "s1", "repeat", 60, 60, "repeat", now);
            Check(jobs.PeekDue("claude", "s1", now.AddSeconds(29)).Length == 0, "Reservation is not early");
            Check(jobs.PeekDue("codex", "s1", now.AddHours(2)).Length == 0 && jobs.PeekDue("claude", "s2", now.AddHours(2)).Length == 0, "Reservations are isolated by harness and session");
            jobs.Acknowledge(one, now.AddSeconds(30));
            Check(jobs.List("claude", "s1").All(x => x.Id != one.Id), "One-shot reservation delivered once");
            jobs.Acknowledge(repeat, now.AddHours(8));
            Check(jobs.List("claude", "s1").Single().Due == now.AddHours(8).AddSeconds(60), "Missed repeat periods coalesce");
            Check(new AgentWakeups(jobsRoot).List("claude", "s1").Length == 1, "Reservations persist");
            Check(Throws<ArgumentException>(() => jobs.Schedule("claude", "s1", "x", 0, null, null, now)), "Zero-delay reservation rejected");
            jobs.Clear("claude"); Check(jobs.List("claude", "s1").Length == 0, "Disabling care clears reservations");

            var workspaces = new AgentWorkspaceStore(Path.Combine(temp, "workspaces"), Path.Combine(temp, "default"));
            workspaces.EnsureCurrent();
            string created = Path.Combine(temp, "project"); workspaces.Create(temp, "project");
            Check(workspaces.Current == created && workspaces.Recent.Contains(created), "Workspace create and switch");
            Check(workspaces.SessionPath("claude").Contains("sessions"), "Session record path is workspace scoped");
            Check(workspaces.SessionPath("claude") != workspaces.SessionPath("codex"), "Sessions are harness scoped");
            Check(Throws<IOException>(() => workspaces.ValidateClear(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))), "Protected directories cannot be cleared");

            var config = new AgentSessionConfiguration();
            config.Read(JsonSerializer.SerializeToElement(new
            {
                configOptions = new object[]
                {
                    new { id = "model", name = "Model", category = "model", type = "select", currentValue = "fast", options = new object[] { new { value = "fast", name = "Fast" }, new { value = "accurate", name = "Accurate" } } },
                    new { id = "permission", name = "Permission", category = "mode", type = "select", currentValue = "ask", options = new object[] { new { value = "ask", name = "Ask" } } },
                    new { id = "flag", name = "Flag", category = "other", type = "boolean", currentValue = false }
                }
            }));
            Check(config.Options.Length == 3 && config.Model == "fast", "ACP config options are read");
            config.Notification(JsonSerializer.SerializeToElement(new { sessionUpdate = "current_mode_update", currentModeId = "auto" }));
            Check(config.Options.Single(o => o.Id == "permission").CurrentValue == "ask", "Modern config ignores legacy mode update");

            var memoryRoot = Path.Combine(temp, "memory"); Directory.CreateDirectory(memoryRoot);
            var memories = new AgentMemoryStore(memoryRoot);
            memories.Remember("饮食偏好", "主人喜欢大白饭", now);
            Check(memories.List().Single().Text == "主人喜欢大白饭" && memories.PromptText().Contains("大白饭"), "Memory persists and is injected as a bounded snapshot");
            Check(new AgentMemoryStore(memoryRoot).List().Single().Key == "饮食偏好", "Memory survives store reconstruction");
            Check(memories.Forget("饮食偏好") && memories.List().Length == 0, "Memory can be forgotten");

            var reminderRoot = Path.Combine(temp, "reminders"); Directory.CreateDirectory(reminderRoot);
            var reminders = new AgentReminderStore(reminderRoot);
            var once = reminders.Create("喝水", now.AddMinutes(5), null, null, false, now);
            var daily = reminders.Create("休息", null, "21:30", "Asia/Shanghai", true, now);
            Check(reminders.List().Length == 2 && reminders.Due(now.AddMinutes(6)).Single().Id == once.Id, "One-time reminder becomes due");
            reminders.Acknowledge(once, now.AddMinutes(6));
            var fired = reminders.List().Single(r => r.Id == once.Id);
            Check(!fired.Enabled && fired.LastFired != null, "One-time reminder fires once");
            Check(new AgentReminderStore(reminderRoot).List().Any(r => r.Id == daily.Id && r.AgentSpeech), "Daily reminder persists with Agent speech flag");
            Check(reminders.Cancel(daily.Id) && reminders.List().All(r => r.Id != daily.Id), "Reminder can be cancelled");

            var weatherOld = new WeatherSnapshot("测试地区", 31.82, 117.23, now, 20, 20, 60, 0, 0, 5, 0, 26, 15, 10);
            var weatherNew = weatherOld with { WeatherCode = 61, Temperature = 16, Precipitation = 1, PrecipitationProbability = 70 };
            string? weatherChange = WeatherService.DescribeChange(weatherOld, weatherNew);
            Check(weatherChange != null && weatherChange.Contains("天气") && WeatherService.Description(weatherNew.WeatherCode) == "雨", "Weather change detection and WMO description");

            var prefs = new CompanionPreferences();
            Check(!prefs.AgentEnabled && !prefs.AgentDailySpeech && !prefs.PuppetMode, "Agent connection and automatic speech are opt-in");
            Check(prefs.AgentHarnessId == "codex" && prefs.SpeechRangeIndex == 3, "Default harness and speech range");

            var definitionNames = ControlTools.Definitions.Select(x => (string)x.GetType().GetProperty("name")!.GetValue(x)!).ToArray();
            Check(definitionNames.Length == 20 && !definitionNames.Contains("request_local_speech"), "Twenty tools, no local-model speech tool");
            Check(definitionNames.Contains("say") && definitionNames.Contains("capture_foreground") && definitionNames.Contains("schedule_wakeup"), "Core upstream tools are exposed");
            Check(definitionNames.Contains("memory_remember") && definitionNames.Contains("reminder_create") && definitionNames.Contains("get_weather"), "Memory, reminder and weather tools are exposed");
            using (var update = JsonDocument.Parse(JsonSerializer.Serialize(ControlTools.Definitions.Single(x => (string)x.GetType().GetProperty("name")!.GetValue(x)! == "update_settings"))))
                Check(update.RootElement.GetRawText().Contains("SpeechMinMinutes") && update.RootElement.GetRawText().Contains("PuppetMode") && update.RootElement.GetRawText().Contains("WeatherLatitude"), "Settings schema mirrors local preferences");

            string pipe = "BigBlueFish.Test." + Guid.NewGuid().ToString("N");
            using var server = new ControlServer((name, args, _) =>
            {
                if (name == "say") return Task.FromResult(ControlTools.Text(new { displayed = true, text = args.GetProperty("text").GetString() }));
                return Task.FromResult(ControlTools.Text(new { name }));
            }, pipe);
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(3000);
            using var reader = new StreamReader(client); using var writer = new StreamWriter(client) { AutoFlush = true };
            async Task<string> Read() => await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) ?? throw new IOException("MCP EOF");
            await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
            using (var init = JsonDocument.Parse(await Read())) Check(init.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString() == "BigBlueFish", "Control server initializes");
            await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
            using (var list = JsonDocument.Parse(await Read())) Check(list.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength() == 20, "Control server lists twenty tools");
            await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"say\",\"arguments\":{\"text\":\"你好\"}}}");
            using (var call = JsonDocument.Parse(await Read()))
            {
                using var payload = JsonDocument.Parse(call.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);
                Check(payload.RootElement.GetProperty("text").GetString() == "你好", "Control server executes say");
            }

            string fake = Path.Combine(temp, "bin"); Directory.CreateDirectory(fake);
            string fakeCli = Path.Combine(fake, "opencode");
            File.WriteAllText(fakeCli, """
#!/usr/bin/python3
import json, sys, os, subprocess
model = "fast"
mcp_config = []
def reply(i, result=None, error=None):
    msg = {"jsonrpc":"2.0","id":i}
    if error is not None: msg["error"] = error
    else: msg["result"] = result
    print(json.dumps(msg), flush=True)
def config():
    return [{"id":"model","name":"Model","category":"model","type":"select","currentValue":model,"options":[{"value":"fast","name":"Fast"},{"value":"accurate","name":"Accurate"}]},
            {"id":"permission","name":"Permission","category":"mode","type":"select","currentValue":"ask","options":[{"value":"ask","name":"Ask"}]}]
for line in sys.stdin:
    try: msg = json.loads(line)
    except Exception: continue
    if "id" not in msg: continue
    i, method, p = msg["id"], msg.get("method"), msg.get("params", {})
    if method == "initialize":
        reply(i, {"protocolVersion":1,"agentCapabilities":{"loadSession":True,"sessionCapabilities":{"resume":{}}},"agentInfo":{"name":"Fake ACP","version":"1"},"authMethods":[]})
    elif method == "session/new":
        mcp_config = p.get("mcpServers", [])
        reply(i, {"sessionId":"fake-session-1","configOptions":config()})
    elif method in ("session/load", "session/resume"):
        mcp_config = p.get("mcpServers", [])
        reply(i, {"sessionId":p.get("sessionId","fake-session-1"),"configOptions":config()})
    elif method == "session/set_config_option":
        model = p.get("value", model); reply(i, {"configOptions":config()})
    elif method == "session/prompt":
        print(json.dumps({"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"fake-session-1","update":{"sessionUpdate":"tool_call","toolCallId":"x","title":"mcp__big-blue-fish__say"}}}), flush=True)
        if mcp_config:
            try:
                s = mcp_config[0]
                proc = subprocess.Popen([s["command"]] + s.get("args", []), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
                def call(obj):
                    proc.stdin.write(json.dumps(obj) + "\n"); proc.stdin.flush()
                call({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}})
                call({"jsonrpc":"2.0","method":"notifications/initialized"})
                call({"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"say","arguments":{"text":"工具说话"}}})
                for _ in range(2):
                    if not proc.stdout.readline(): break
                proc.stdin.close(); proc.wait(timeout=5)
            except Exception:
                pass
        reply(i, {"stopReason":"end_turn"})
    else:
        reply(i, error={"code":-32601,"message":"unsupported"})
""");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(fakeCli, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string? oldPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", fake + Path.PathSeparator + oldPath);
            try
            {
                string agentPipe = pipe + ".agent";
                var said = new List<string>();
                AgentCoordinator? acpHost = null;
                using var agentServer = new ControlServer((name, args, _) =>
                {
                    if (name == "say")
                    {
                        acpHost?.MarkSpoken();
                        lock (said) said.Add(args.GetProperty("text").GetString()!);
                        return Task.FromResult(ControlTools.Text(new { displayed = true }));
                    }
                    return Task.FromResult(ControlTools.Text(new { name }));
                }, agentPipe);
                string? process = Environment.ProcessPath ?? throw new IOException("Missing process path");
                string[] prefix = Path.GetFileNameWithoutExtension(process) == "dotnet" ? [typeof(AgentTests).Assembly.Location] : [];
                using var host = new AgentCoordinator(Path.Combine(temp, "host"), process, prefix, agentPipe, (_, _) => Task.FromResult<string?>(null), Path.Combine(temp, "agent-workspace"));
                acpHost = host;
                host.Configure("opencode", new(true, false, false));
                await Until(() => host.Connected, "ACP handshake and session/new", count);
                Check(host.Configuration.Options.Length == 2 && host.CurrentModel == "fast", "ACP configuration is exposed");
                await host.SetConfiguration("model", "accurate");
                Check(host.CurrentModel == "accurate", "ACP configuration updates");
                using (var memoryArgs = JsonDocument.Parse("{\"key\":\"偏好\",\"text\":\"主人喜欢大白饭\"}"))
                {
                    host.ExecuteTool("memory_remember", memoryArgs.RootElement);
                    Check(host.Memories.List().Single().Text == "主人喜欢大白饭", "Coordinator memory tool writes the shared store");
                }
                using (var reminderArgs = JsonDocument.Parse("{\"text\":\"喝水\",\"at\":\"" + DateTimeOffset.Now.AddHours(1).ToString("O") + "\",\"agent_speech\":false}"))
                {
                    host.ExecuteTool("reminder_create", reminderArgs.RootElement);
                    var reminder = host.Reminders.List().Single();
                    Check(reminder.Text == "喝水" && !reminder.AgentSpeech, "Coordinator reminder tool creates a one-time reminder");
                    host.ExecuteTool("reminder_cancel", JsonSerializer.SerializeToElement(new { id = reminder.Id }));
                    Check(host.Reminders.List().Length == 0, "Coordinator reminder tool cancels a reminder");
                }
                Check(host.SendUser("hello"), "User message is accepted");
                await Until(() => { lock (said) return said.Count == 1 && host.Detail.Contains("本轮处理完成"); }, "ACP prompt reaches MCP say through the bridge", count);
                Check(host.Detail.Contains("本轮处理完成"), "say marks the turn as spoken");
                Check(host.SessionId == "fake-session-1", "Fish-owned session id retained");
                host.NewConversation();
                await Until(() => host.Connected && !host.Busy, "New conversation reconnects", count);
            }
            finally { Environment.SetEnvironmentVariable("PATH", oldPath); }
        }
        finally { Directory.Delete(temp, true); }
        Console.WriteLine($"PASS: {count} ACP, workspace, wakeup, settings and MCP protocol checks"); return count;
    }

    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static async Task Until(Func<bool> condition, string label, int count)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < limit) await Task.Delay(50);
        if (!condition()) throw new Exception("Timeout: " + label);
    }
}
