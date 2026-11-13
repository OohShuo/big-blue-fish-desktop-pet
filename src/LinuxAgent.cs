using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BigBlueFish.Agent;
using BigBlueFish.Control;

namespace BigBlueFish;

internal sealed partial class LinuxPetWindow
{
    ControlServer? mcpServer;
    AgentCoordinator? agent;
    readonly string agentPipe = ControlTools.PipeName + "." + Guid.NewGuid().ToString("N");
    readonly Queue<string> externalActions = new();
    Window? mcpInput;
    Action? refreshAgentSettings;
    DateTime nextAgentSpeech = DateTime.MinValue;
    DateTime nextExternalAt = DateTime.MinValue;
    string? nextExternalText;
    string currentAction = "";
    IntPtr lastForeground;
    long lastClickAt;
    PixelPoint lastClickPoint;
    bool agentTyping;
    readonly Stopwatch agentTypingClock = new();
    int[] agentTextBoundaries = [];
    AgentMemoryStore? memoryStore;
    AgentReminderStore? reminderStore;
    WeatherService? weatherService;
    WeatherSnapshot? lastWeather;
    DateTime nextWeatherCheck = DateTime.MinValue;
    DateTime lastWeatherAlertAt = DateTime.MinValue;
    bool weatherChecking;

    internal string AgentRoot => Path.Combine(Environment.CurrentDirectory, "agent");
    internal AgentMemoryStore Memories => memoryStore ??= new(AgentRoot);
    internal AgentReminderStore Reminders => reminderStore ??= new(AgentRoot);
    internal WeatherService Weather => weatherService ??= new(AgentRoot);
    internal WeatherLocation? CurrentWeatherLocation => Preferences.WeatherLatitude is { } latitude && Preferences.WeatherLongitude is { } longitude
        ? new(Preferences.WeatherLabel.Length > 0 ? Preferences.WeatherLabel : $"{latitude:0.###}, {longitude:0.###}", latitude, longitude) : null;

    internal bool Puppet => Preferences.AgentEnabled && Preferences.PuppetMode;
    internal bool AgentDailyActive => Preferences.AgentEnabled && Preferences.AgentDailySpeech;
    internal bool AgentInputAvailable => Preferences.AgentEnabled;
    internal bool StartupEnabled => File.Exists(AutostartFile);

    static string DefaultAgentWorkspace()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents)) documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
        return Path.Combine(documents, "蓝色大肥鱼工作区");
    }

    static (string Path, string[] Prefix) BridgeCommand()
    {
        string? process = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(process) && Path.GetFileNameWithoutExtension(process) != "dotnet") return (process, []);
        string assembly = typeof(Program).Assembly.Location;
        string dotnet = process ?? HarnessCatalog.FindExecutable("dotnet") ?? "dotnet";
        return (dotnet, [assembly]);
    }

    void ConfigureAgent()
    {
        if (agent == null) return;
        bool daily = Preferences.AgentDailySpeech;
        bool puppet = Preferences.PuppetMode && daily;
        agent.Configure(Preferences.AgentHarnessId, new(true, puppet, daily));
        if (!AgentDailyActive || puppet) nextAgentSpeech = DateTime.MinValue;
    }

    void InitializeMcp()
    {
        if (!Preferences.AgentEnabled || mcpServer != null) return;
        mcpServer = new ControlServer(ExecuteMcp, agentPipe);
        var bridge = BridgeCommand();
        agent = new AgentCoordinator(
            AgentRoot,
            bridge.Path, bridge.Prefix, agentPipe,
            RequestAgentPermission, DefaultAgentWorkspace(), Memories, Reminders);
        if (!HarnessCatalog.All.Any(x => x.Id == Preferences.AgentHarnessId)) Preferences.AgentHarnessId = "codex";
        agent.Changed += () => refreshAgentSettings?.Invoke();
        ConfigureAgent();
        nextAgentSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
    }

    void StopMcp()
    {
        mcpInput?.Close();
        consentWindow?.Close();
        agent?.Dispose(); agent = null;
        mcpServer?.Dispose(); mcpServer = null;
        externalActions.Clear(); nextExternalText = null;
    }

    void TickAgent(DateTime now)
    {
        TickReminders(now);
        TickWeather(now);
        if (agent == null) return;
        agent.Tick(new DateTimeOffset(now), !hidden && !fullscreenHidden && !dragging && !closed && phase != "Exit");
        if (AgentDailyActive && !Puppet && !hidden && !fullscreenHidden && !dragging && settings == null && now >= nextAgentSpeech)
        {
            nextAgentSpeech = now.Add(Preferences.NextSpeechDelay(random));
            agent.DailyCare();
        }
        if (nextExternalText != null && now >= nextExternalAt && !animationActive && !hidden && !fullscreenHidden && !dragging && settings == null)
        {
            string text = nextExternalText; nextExternalText = null;
            SayExternal(text);
        }
    }

    void TickReminders(DateTime now)
    {
        foreach (var reminder in Reminders.Due(new DateTimeOffset(now)))
        {
            bool agentWillSpeak = reminder.AgentSpeech && agent?.ReminderDue(reminder) == true;
            Reminders.Acknowledge(reminder, new DateTimeOffset(now));
            if (!agentWillSpeak) ShowText("主人，提醒你：" + reminder.Text);
        }
    }

    void TickWeather(DateTime now)
    {
        if (!Preferences.AgentEnabled || !Preferences.WeatherAgentAlerts || CurrentWeatherLocation == null || weatherChecking || now < nextWeatherCheck) return;
        nextWeatherCheck = now.AddMinutes(15);
        _ = CheckWeatherAsync();
    }

    async Task CheckWeatherAsync()
    {
        weatherChecking = true;
        try
        {
            var location = CurrentWeatherLocation;
            if (location == null) return;
            var previous = lastWeather ?? Weather.TryReadCache(location);
            var current = await Weather.GetAsync(location, CancellationToken.None, force: true);
            lastWeather = current;
            if (previous == null || agent?.Connected != true || !Preferences.WeatherAgentAlerts) return;
            string? change = WeatherService.DescribeChange(previous, current);
            if (change == null || DateTime.UtcNow < lastWeatherAlertAt.AddMinutes(30)) return;
            if (agent.WeatherChanged(change, current)) lastWeatherAlertAt = DateTime.UtcNow;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or TaskCanceledException or OperationCanceledException or ArgumentException) { }
        finally { weatherChecking = false; }
    }

    void PublishInteraction(string kind, object detail)
    {
        mcpServer?.Publish(kind, detail);
        if (kind == "user_message") return;
        if (kind == "click" && Preferences.AgentRespondToClick || kind == "drag_end" && Preferences.AgentRespondToDrag)
            agent?.Interaction(kind, detail);
    }

    void SetPuppet(bool enabled)
    {
        if (enabled && !Preferences.AgentDailySpeech) throw new InvalidOperationException("请先开启“把日常关心的吐字交给 Agent”。");
        if (Preferences.PuppetMode == enabled) return;
        Preferences.PuppetMode = enabled;
        externalActions.Clear();
        nextExternalText = null;
        if (!enabled) nextAgentSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
        ConfigureAgent();
        if (enabled) RequestPlayback(policy.Find("待机呼吸休闲"), "PuppetIdle", false, null);
        else ResumeAutomaticIfIdle();
        refreshAgentSettings?.Invoke();
    }

    bool TryPlayExternal()
    {
        if (externalActions.Count == 0 || animationActive || hidden || fullscreenHidden || dragging || settings != null || phase == "Exit") return false;
        string action = externalActions.Dequeue();
        if (!policy.Rules.Any(r => r.Id == action)) return TryPlayExternal();
        StartPlayback(policy.Find(action), "External", false, null);
        return true;
    }

    void SayExternal(string text)
    {
        agent?.MarkSpoken();
        ShowTyped(text);
    }

    async Task ManualSpeech()
    {
        if (!AgentDailyActive) { ShowText("请先在设置中开启“把日常关心的吐字交给 Agent”哦~"); return; }
        if (agent?.ManualSpeech() != true) ShowText("鱼鱼还没连接好 Agent，请稍后再试~");
        await Task.CompletedTask;
    }

    void ShowTyped(string text)
    {
        StopTyping();
        fullText = text;
        agentTextBoundaries = StringInfo.ParseCombiningCharacters(text);
        if (agentTextBoundaries.Length == 0) { bubbleText.Text = ""; return; }
        agentTyping = agentTextBoundaries.Length > 1;
        agentTypingClock.Restart();
        bubbleText.Text = agentTyping ? text[..agentTextBoundaries[1]] : text;
        bubbleUntil = DateTime.MaxValue;
        MoveBubble();
        if (!hidden && !fullscreenHidden) ShowBubble();
        if (!agentTyping) ResetDwell();
    }

    void TickTyping()
    {
        if (!agentTyping) return;
        int count = Math.Min(agentTextBoundaries.Length, 1 + (int)(agentTypingClock.Elapsed.TotalSeconds * 28));
        bubbleText.Text = count >= agentTextBoundaries.Length ? fullText : fullText[..agentTextBoundaries[count]];
        if (count == agentTextBoundaries.Length) { agentTyping = false; ResetDwell(); }
    }

    void StopTyping() => agentTyping = false;

    Dictionary<string, object?> CurrentSettings()
    {
        var result = typeof(CompanionPreferences).GetProperties()
            .Where(p => !Attribute.IsDefined(p, typeof(System.Text.Json.Serialization.JsonIgnoreAttribute)))
            .ToDictionary(p => p.Name, p => p.GetValue(Preferences));
        result["StartupEnabled"] = StartupEnabled;
        result["AgentEnabled"] = Preferences.AgentEnabled;
        result["AgentHarnessId"] = Preferences.AgentHarnessId;
        return result;
    }

    async Task<object> ExecuteMcp(string name, JsonElement args, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            if (closed || phase == "Exit" || !Preferences.AgentEnabled) throw new InvalidOperationException("Agent 连接已关闭或桌宠正在退出。");
            switch (name)
            {
                case "get_settings":
                    return ControlTools.Text(new
                    {
                        settings = CurrentSettings(),
                        state = new
                        {
                            visible = IsVisible,
                            hiddenByUser = hidden,
                            fullscreenHidden,
                            action = currentAction,
                            animationPhase = phase,
                            queuedActions = externalActions.ToArray(),
                            queuedSpeech = nextExternalText,
                            connections = mcpServer?.Connections ?? 0,
                            inputOpen = mcpInput?.IsVisible == true,
                            inputAvailable = AgentInputAvailable,
                            agentConnected = agent?.Connected == true,
                            agentStatus = agent?.Status,
                            agentSession = agent?.SessionId,
                            nextWake = agent?.NextWake,
                            puppet = Puppet
                        }
                    });
                case "update_settings":
                    await UpdateAgentSettings(args, token);
                    return ControlTools.Text(new { settings = CurrentSettings() });
                case "list_actions":
                    return ControlTools.Text(policy.Rules.Select(r => new { action = r.Id, pool = r.Pool, weight = r.Weight, duration_seconds = r.Pool == "Greeting" ? 10 / 1.5 : 10 }));
                case "capture_foreground":
                case "capture_screen":
                {
                    var handle = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
                    var identity = name == "capture_foreground" ? CompanionNative.ForegroundIdentity(handle) : "全部显示器组成的完整桌面";
                    var limit = ControlTools.ImageLimit(args, name == "capture_foreground" ? 2560 : 4096);
                    var image = await LinuxCapture.CaptureAsync(handle, limit, name == "capture_screen", token);
                    token.ThrowIfCancellationRequested();
                    if (!Preferences.AgentEnabled) throw new OperationCanceledException();
                    return ControlTools.Image(image.DataUrl, new { target = name, identity, image.Width, image.Height, capturedAt = DateTimeOffset.UtcNow });
                }
                case "play_action":
                case "interrupt_action":
                {
                    string action = ControlTools.RequiredText(args, "action", 200);
                    if (!policy.Rules.Any(r => r.Id == action)) throw new ArgumentException("动作不存在。");
                    if (name == "interrupt_action")
                    {
                        externalActions.Clear(); moveRule = null; pendingPlayback = null;
                        StartPlayback(policy.Find(action), "External", false, null);
                    }
                    else
                    {
                        if (externalActions.Count >= 100) throw new InvalidOperationException("待播放动作队列已满。");
                        externalActions.Enqueue(action);
                        TryPlayExternal();
                    }
                    return ControlTools.Text(new { action, started = name == "interrupt_action", queued = externalActions.Count });
                }
                case "say":
                case "say_next":
                {
                    string text = ControlTools.RequiredText(args, "text");
                    if (name == "say") SayExternal(text);
                    else { nextExternalText = text; nextExternalAt = DateTime.UtcNow; }
                    return ControlTools.Text(new { displayed = name == "say", pending = name == "say_next", text });
                }
                case "get_weather":
                {
                    var location = CurrentWeatherLocation ?? throw new InvalidOperationException("请先在设置中填写固定天气地区。");
                    var snapshot = await Weather.GetAsync(location, token);
                    return ControlTools.Text(new { weather = snapshot, summary = snapshot.Summary() });
                }
                case "get_operation_guide":
                case "get_wakeups":
                case "schedule_wakeup":
                case "cancel_wakeup":
                case "memory_list":
                case "memory_remember":
                case "memory_forget":
                case "reminder_create":
                case "reminder_list":
                case "reminder_cancel":
                    return ControlTools.Text(agent?.ExecuteTool(name, args) ?? throw new InvalidOperationException("Agent 尚未连接。"));
                default: throw new ArgumentException("未知 MCP 工具。");
            }
        });
    }

    async Task UpdateAgentSettings(JsonElement args, CancellationToken token)
    {
        if (!args.TryGetProperty("settings", out var patch) || patch.ValueKind != JsonValueKind.Object) throw new ArgumentException("settings 必须为对象。");
        var values = new Dictionary<string, object?>();
        foreach (var property in patch.EnumerateObject())
        {
            if (!ControlTools.SettingNames.Contains(property.Name)) throw new ArgumentException("未知设置：" + property.Name);
            Type type = property.Name == "StartupEnabled" ? typeof(bool) : typeof(CompanionPreferences).GetProperty(property.Name)!.PropertyType;
            values[property.Name] = JsonSerializer.Deserialize(property.Value.GetRawText(), type);
        }
        foreach (var item in values)
        {
            bool Range(double low, double high) => item.Value is IConvertible c && double.IsFinite(c.ToDouble(null)) && c.ToDouble(null) >= low && c.ToDouble(null) <= high;
            bool valid = item.Key switch
            {
                "Scale" or "BubbleScale" => Range(.5, 2),
                "LowPowerIdleSeconds" => Range(3, 120),
                "BreakfastHour" or "LunchHour" or "DinnerHour" => Range(0, 23),
                "BubbleDurationIndex" => Range(0, 7),
                "SpeechMinMinutes" => item.Value is int minutes && CompanionPreferences.SpeechRanges.Any(r => r.Min == minutes),
                "LastBrushDate" => item.Value == null || item.Value is string date && DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                "Left" or "Top" => item.Value == null || Range(-1000000, 1000000),
                "WeatherLabel" => item.Value is string label && label.Length <= 120,
                "WeatherLatitude" => item.Value == null || Range(-90, 90),
                "WeatherLongitude" => item.Value == null || Range(-180, 180),
                _ => item.Value is bool
            };
            if (!valid) throw new ArgumentException("设置值无效：" + item.Key);
        }
        bool effectiveAlerts = values.TryGetValue("WeatherAgentAlerts", out var alertValue) ? alertValue is true : Preferences.WeatherAgentAlerts;
        double? effectiveLatitude = values.TryGetValue("WeatherLatitude", out var latitudeValue) ? latitudeValue as double? : Preferences.WeatherLatitude;
        double? effectiveLongitude = values.TryGetValue("WeatherLongitude", out var longitudeValue) ? longitudeValue as double? : Preferences.WeatherLongitude;
        if (effectiveAlerts && (effectiveLatitude == null || effectiveLongitude == null))
            throw new ArgumentException("开启天气变化提醒前请先设置固定天气地区。");
        bool turningPuppetOn = values.TryGetValue("PuppetMode", out var puppet) && puppet is true && !Preferences.PuppetMode;
        if (turningPuppetOn)
        {
            if (!Preferences.AgentDailySpeech && !(values.TryGetValue("AgentDailySpeech", out var daily) && daily is true))
                throw new InvalidOperationException("请先开启“把日常关心的吐字交给 Agent”。");
            if (!await Confirm(ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, "了解并开启", token))
                throw new OperationCanceledException("用户取消让 Agent 安排日常关心与动作。");
        }
        token.ThrowIfCancellationRequested();
        if (!Preferences.AgentEnabled) throw new OperationCanceledException();
        foreach (var item in values)
        {
            if (item.Key == "StartupEnabled") SetStartup((bool)item.Value!);
            else if (item.Key != "PuppetMode") typeof(CompanionPreferences).GetProperty(item.Key)!.SetValue(Preferences, item.Value);
        }
        if (Preferences.PuppetMode && !Preferences.AgentDailySpeech) Preferences.PuppetMode = false;
        if ((Preferences.WeatherLatitude == null) != (Preferences.WeatherLongitude == null))
        {
            Preferences.WeatherLatitude = null; Preferences.WeatherLongitude = null; Preferences.WeatherAgentAlerts = false;
        }
        if (values.TryGetValue("PuppetMode", out puppet)) SetPuppet((bool)puppet!);
        var position = Position;
        if (values.TryGetValue("Left", out var left) && left is double x) position = new((int)x, position.Y);
        if (values.TryGetValue("Top", out var top) && top is double y) position = new(position.X, (int)y);
        Rescale(); Clamp(position); MoveBubble(); ResetDwell(); Save();
        ConfigureAgent();
        if (AgentDailyActive && !Puppet) nextAgentSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
        ResumeAutomaticIfIdle(); refreshAgentSettings?.Invoke();
    }

    async Task<string?> RequestAgentPermission(AgentPermission request, CancellationToken token)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (closed || !Preferences.AgentEnabled || token.IsCancellationRequested) return null;
            string heading = request.Title.Length > 80 || request.Title.StartsWith('{') ? "Agent 请求执行操作" : request.Title;
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = heading, FontSize = 22, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new ScrollViewer { Content = Paragraph(request.Detail.Length > 8000 ? request.Detail[..8000] : request.Detail), MaxHeight = 280 });
            var choices = new WrapPanel(); content.Children.Add(choices);
            var window = Dialog(request.Kind == "authentication" ? "Agent 登录认证" : "Agent 请求权限", content, 700, 500);
            string? selected = null;
            var complete = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            foreach (var option in request.Options)
                choices.Children.Add(ActionButton(option.Name, () => { selected = option.Id; window.Close(); }));
            choices.Children.Add(ActionButton("取消", window.Close));
            window.Closed += (_, _) => complete.TrySetResult(token.IsCancellationRequested ? null : selected);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(window.Close));
            if (settings?.IsVisible == true) window.Show(settings); else window.Show();
            return await complete.Task;
        });
    }

    bool TryOpenMcpInput()
    {
        if (!AgentInputAvailable || closed || phase == "Exit") return false;
        if (mcpInput != null) { mcpInput.Show(this); mcpInput.Activate(); return true; }
        const string hint = "通过鱼鱼和已连接的 Agent 说话";
        var text = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000,
            Height = 56, Padding = new Thickness(0, 2, 22, 0), FontSize = 14, Foreground = Hair,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), PlaceholderText = hint
        };
        Avalonia.Automation.AutomationProperties.SetName(text, hint);
        var editor = new Grid(); editor.Children.Add(text);
        var status = new TextBlock { Text = "Ctrl+Enter 发送", Foreground = Brushes.SlateGray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
        var panel = new StackPanel { Spacing = 4 }; panel.Children.Add(editor);
        var frame = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 251, 244)), BorderBrush = Hair, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = panel };
        var dialog = new Window
        {
            Title = "蓝色大肥鱼 · 输入", Width = 354, Height = 120, WindowDecorations = WindowDecorations.None,
            CanResize = false, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Background = Brushes.Transparent, TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Content = frame
        };
        var close = new Button { Content = "×", Width = 20, Height = 20, Padding = new Thickness(0), FontSize = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Foreground = Hair, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        ToolTip.SetTip(close, "收起输入框"); close.Click += (_, _) => dialog.Close(); editor.Children.Add(close);
        void Send()
        {
            if (agent?.Connected != true || string.IsNullOrWhiteSpace(text.Text) || !agent.SendUser(text.Text)) return;
            PublishInteraction("user_message", new { text = text.Text });
            text.Text = ""; status.Text = "已发送给 Agent"; text.Focus();
        }
        var send = ActionButton("发送", Send); send.MinWidth = 58; send.Padding = new Thickness(12, 5); send.FontSize = 12;
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") }; actions.Children.Add(status); actions.Children.Add(send); Grid.SetColumn(send, 1); panel.Children.Add(actions);
        text.TextChanged += (_, _) => send.IsEnabled = agent?.Connected == true && !string.IsNullOrWhiteSpace(text.Text);
        dialog.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); }
            else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control) { e.Handled = true; Send(); }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        bool positioning = false;
        void Follow()
        {
            if (positioning || mcpInput != dialog) return;
            positioning = true;
            try
            {
                if (!IsVisible) { dialog.Hide(); return; }
                double scale = DesktopScaling;
                var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
                dialog.Width = Math.Min(354, Math.Max(160, area.Width / scale - 16));
                int width = (int)Math.Ceiling(dialog.Width * scale), height = (int)Math.Ceiling(dialog.Height * scale), gap = (int)(8 * scale);
                int x = Position.X + (int)(SpriteSize * 208 / 360 * scale) - width - gap;
                if (x < area.X + gap) x = Position.X + (int)(SpriteSize * 428 / 360 * scale) + gap;
                int y = Position.Y + (int)(SpriteSize * 336 / 360 * scale) - height;
                dialog.Position = new(Math.Clamp(x, area.X + gap, Math.Max(area.X + gap, area.Right - width - gap)),
                    Math.Clamp(y, area.Y + gap, Math.Max(area.Y + gap, area.Bottom - height - gap)));
                if (!dialog.IsVisible) dialog.Show(this);
            }
            finally { positioning = false; }
        }
        EventHandler<PixelPointEventArgs> moved = (_, _) => Follow();
        EventHandler<AvaloniaPropertyChangedEventArgs> changed = (_, e) =>
        {
            if (e.Property == IsVisibleProperty || e.Property == WidthProperty || e.Property == HeightProperty) Follow();
        };
        PositionChanged += moved; PropertyChanged += changed;
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        clock.Tick += (_, _) =>
        {
            if (!Preferences.AgentEnabled) { dialog.Close(); return; }
            Follow();
            send.IsEnabled = agent?.Connected == true && !string.IsNullOrWhiteSpace(text.Text);
            if (agent?.Connected != true) status.Text = agent?.Status ?? "Agent 尚未连接";
            else if (status.Text != "已发送给 Agent") status.Text = agent.Busy ? "Agent 正在处理…" : "Ctrl+Enter 发送";
        };
        mcpInput = dialog;
        dialog.Closed += (_, _) => { clock.Stop(); PositionChanged -= moved; PropertyChanged -= changed; mcpInput = null; };
        dialog.Opened += (_, _) => { text.Focus(); send.IsEnabled = false; };
        clock.Start(); Follow(); dialog.Activate(); return true;
    }
}
