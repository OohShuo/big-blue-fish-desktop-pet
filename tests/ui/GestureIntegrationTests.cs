using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using System.Net;
using System.Text;
using System.Text.Json;
using BigBlueFish;
using System.Reflection;
using System.Runtime.InteropServices;

var artifacts = Path.GetFullPath("build");
var temp = Path.Combine(Path.GetTempPath(), "fish-gestures-" + Guid.NewGuid());
Directory.CreateDirectory(temp); Environment.CurrentDirectory = temp;
File.WriteAllText("preferences.json", "{\"LowPowerAnimation\":true,\"LowPowerIdleSeconds\":120,\"HideOnFullscreen\":false}");
var builder = AppBuilder.Configure<App>().UseX11().UseSkia().UseHarfBuzz();
builder.AfterSetup(_ => DispatcherTimer.RunOnce(async () =>
{
    var life = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
    var w = life.MainWindow; var f = BindingFlags.NonPublic | BindingFlags.Instance;
    object Get(string name) => w.GetType().GetField(name, f).GetValue(w);
    void Chance(bool yes) { var field = w.GetType().GetField("gestures", f); field.SetValue(w, Activator.CreateInstance(field.FieldType, new object[] { new Func<bool>(() => yes) })); }
    string Text() => (string)Get("fullText");
    void Check(bool ok, string label) { if (!ok) throw new Exception(label + "; text=" + Text()); Console.WriteLine("PASS: " + label); }
    IntPtr display = IntPtr.Zero;
    try
    {
        Chance(false); await Task.Delay(7500);
        var agentPrefs = Get("Preferences");
        agentPrefs.GetType().GetProperty("AgentEnabled")!.SetValue(agentPrefs, true);
        Check((bool)w.GetType().GetMethod("TryOpenMcpInput", f).Invoke(w, null)! && Get("mcpInput") is Window, "double-click input opens for an enabled Agent");
        ((Window)Get("mcpInput")).Close();
        var captureType = w.GetType().Assembly.GetType("BigBlueFish.LinuxCapture")!;
        var captureTask = (Task)captureType.GetMethod("CaptureAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { IntPtr.Zero, 320, true, CancellationToken.None })!;
        await captureTask.WaitAsync(TimeSpan.FromSeconds(10));
        var capture = captureTask.GetType().GetProperty("Result")!.GetValue(captureTask)!;
        Check(((string)capture.GetType().GetProperty("DataUrl")!.GetValue(capture)!).StartsWith("data:image/png;base64,"), "X11 screenshot encodes PNG");
        using (var sayArgs = JsonDocument.Parse("{\"text\":\"工具说话\"}"))
        {
            var say = (Task<object>)w.GetType().GetMethod("ExecuteMcp", f)!.Invoke(w, new object[] { "say", sayArgs.RootElement, CancellationToken.None })!;
            await say.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Check(Text().Contains("工具说话"), "say tool reaches the pet bubble");
        agentPrefs.GetType().GetProperty("AgentEnabled")!.SetValue(agentPrefs, true);
        w.GetType().GetMethod("OpenSettings", f).Invoke(w, null);
        await Task.Delay(500);
        var settingsWindow = (Window)Get("settings");
        var labels = settingsWindow.GetLogicalDescendants().OfType<TextBlock>().Select(x => x.Text ?? "").ToArray();
        var boxes = settingsWindow.GetLogicalDescendants().OfType<CheckBox>().Select(x => x.Content?.ToString() ?? "").ToArray();
        Check(labels.Any(x => x == "连接 Agent"), "Agent settings section is present");
        Check(boxes.Contains("点击时让 Agent 回应") && boxes.Contains("拖动结束时让 Agent 回应"), "Click and drag response switches are present");
        Check(labels.Any(x => x == "固定天气地区") && boxes.Contains("天气变化时让 Agent 提醒"), "Weather settings are present");
        Check(labels.Any(x => x == "记忆与提醒") && labels.Any(x => x == "长期记忆") && labels.Any(x => x == "提醒"), "Memory and reminder settings are present");
        settingsWindow.Close();
        agentPrefs.GetType().GetProperty("AgentEnabled")!.SetValue(agentPrefs, false);
        if (args.Contains("--playback-regression"))
        {
            var prefs = Get("Preferences"); prefs.GetType().GetProperty("LowPowerAnimation").SetValue(prefs, false);
            w.GetType().GetMethod("Play", f).Invoke(w, new object[] { Get("policy").GetType().GetMethod("Find", f | BindingFlags.Public).Invoke(Get("policy"), new object[] { "点击回应-开心跃动" }), "Automatic", false });
            var video = Get("player");
            long Generation() => (long)video.GetType().GetField("generation", f).GetValue(video);
            long before = Generation();
            w.GetType().GetMethod("OpenSettings", f).Invoke(w, null);
            await Task.Delay(12000);
            Check((bool)Get("animationActive") && Generation() > before, "playback continues across EOS while settings stays open");
            ((Window)Get("settings")).Close();
            before = Generation();
            ((CancellationTokenSource)video.GetType().GetField("current", f).GetValue(video)).Cancel();
            ((Action<string>)video.GetType().GetField("Failed", f).GetValue(video))("Injected recoverable decode failure");
            await Task.Delay(3500);
            Check((bool)Get("animationActive") && Generation() > before, "decode failure recovers without user interaction");
            Console.WriteLine("PASS: complete clips and deferred interaction transitions");
            w.GetType().GetMethod("CloseImmediately", f).Invoke(w, null); return;
        }
        display = N.XOpenDisplay(IntPtr.Zero);
        void Move(int x, int y) { N.XTestFakeMotionEvent(display, -1, x, y, 0); N.XFlush(display); }
        void Button(bool down) { N.XTestFakeButtonEvent(display, 1, down ? 1 : 0, 0); N.XFlush(display); }
        PixelPoint Target()
        {
            var image = (Image)Get("sprite"); var bitmap = (WriteableBitmap)image.Source;
            using var b = bitmap.Lock(); int px = bitmap.PixelSize.Width / 2, py = bitmap.PixelSize.Height * 2 / 3;
            for (int radius = 0; radius < 120; radius++) for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                int x = px + dx, y = py + dy; if (x < 0 || y < 0 || x >= bitmap.PixelSize.Width || y >= bitmap.PixelSize.Height) continue;
                if (Marshal.ReadByte(b.Address, y * b.RowBytes + x * 4 + 3) > 200) return w.PointToScreen(new Point(w.Width * x / bitmap.PixelSize.Width, w.Height * y / bitmap.PixelSize.Height));
            }
            throw new Exception("No opaque click target");
        }
        async Task<PixelPoint> Grab()
        {
            // Animated alpha hit regions can move between sampling and the X event.
            for (int i = 0; i < 10; i++)
            {
                var p = Target(); Move(p.X, p.Y); await Task.Delay(40);
                Button(true); await Task.Delay(80);
                if ((bool)Get("dragging")) return p;
                Button(false); await Task.Delay(40);
            }
            throw new Exception("Could not capture animated hit target");
        }
        async Task Click() { await Grab(); Button(false); await Task.Delay(100); }
        // Request feedback during a real playing clip and check that its phase is retained.
        w.GetType().GetMethod("Play", f).Invoke(w, new object[] {
            Get("policy").GetType().GetMethod("Find", f | BindingFlags.Public).Invoke(Get("policy"), new object[] { "点击回应-开心跃动" }), "Automatic", false });
        var gestureType = w.GetType().Assembly.GetType("BigBlueFish.PetGesture");
        void Gesture(string name) => w.GetType().GetMethod("PlayGesture", f).Invoke(w,
            new[] { Enum.Parse(gestureType, name) });
        Gesture("PokedSurprised");
        Check((string)Get("phase") == "Automatic", "feedback waits for current clip to finish");
        Gesture("PokedAngry");
        Check((string)Get("phase") == "Automatic", "repeated feedback does not interrupt current clip");
        async Task WaitFor(Func<bool> condition)
        {
            for (int i = 0; i < 600 && !condition(); i++) await Task.Delay(50);
            Check(condition(), "queued animation completes transition");
        }
        await WaitFor(() => (string)Get("phase") == "Gesture");
        Check(Text().Contains("再戳"), "latest feedback and matching text play at boundary");
        Gesture("HeadPet");
        Check(Text().Contains("再戳"), "queued feedback keeps current animation and text");
        await WaitFor(() => Text().Contains("摸摸头"));
        await WaitFor(() => !(bool)Get("animationActive"));
        Check((string)Get("phase") == "Gesture", "last feedback finishes before low-power hold");
        Gesture("TailTickle");
        await Click();
        Check((string)Get("phase") == "Gesture" && Text().Contains("尾巴"), "real click waits for active gesture");
        var target = await Grab();
        Move(target.X - 25, target.Y); await Task.Delay(250);
        Check((string)Get("phase") == "Gesture" && (bool)Get("dragged"), "drag moves immediately without cutting video");
        Button(false); await Task.Delay(100);
        Check(Get("pendingPlayback") == null, "put-down discards pending suspended feedback");
        Check((string)Get("phase") == "Gesture", "put-down does not interrupt current clip");
        await WaitFor(() => !(bool)Get("animationActive"));
        var reminderStore = w.GetType().GetProperty("Reminders", f | BindingFlags.Public)!.GetValue(w)!;
        var reminderCreate = reminderStore.GetType().GetMethod("Create")!;
        var reminderNow = DateTimeOffset.Now;
        reminderCreate.Invoke(reminderStore, new object[] { "喝水", reminderNow.AddSeconds(-1), null!, null!, false, reminderNow });
        await WaitFor(() => Text().Contains("提醒你：喝水"));
        Check(Text().Contains("提醒你：喝水"), "Due reminder reaches the pet bubble without Puppet");
        Gesture("HeadPet"); w.Close();
        Check((string)Get("phase") == "Gesture" && (bool)Get("exitRequested"), "farewell waits for current clip and locks input");
        var player = Get("player");
        ((Action<string>)player.GetType().GetField("Failed", f).GetValue(player))("Injected decode failure during queued farewell");
        Check((bool)Get("exitReady"), "decode failure during queued farewell still exits");
        Console.WriteLine("PASS: complete clips and deferred interaction transitions");
        life.Shutdown();
    }
    catch (Exception e) { Console.Error.WriteLine(e); life.Shutdown(1); }
    finally { if (display != IntPtr.Zero) N.XCloseDisplay(display); }
}, TimeSpan.FromMilliseconds(100)));
int exit = builder.StartWithClassicDesktopLifetime(args);
Environment.CurrentDirectory = Path.GetTempPath(); Directory.Delete(temp, true); return exit;
static class N
{
    [DllImport("libX11.so.6")] internal static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] internal static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] internal static extern int XCloseDisplay(IntPtr display);
    [DllImport("libXtst.so.6")] internal static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, ulong delay);
    [DllImport("libXtst.so.6")] internal static extern int XTestFakeButtonEvent(IntPtr display, uint button, int down, ulong delay);
}
