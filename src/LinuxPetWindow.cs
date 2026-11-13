using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace BigBlueFish;

internal sealed partial class LinuxPetWindow : Window
{
    internal readonly CompanionPreferences Preferences = CompanionPreferences.Read();
    readonly CompanionAnimationPolicy policy = CompanionAnimationPolicy.Load(Path.Combine(AppContext.BaseDirectory, "assets/fish"));
    readonly Random random = new();
    readonly Image sprite = new() { Stretch = Stretch.Fill };
    readonly LinuxVideoPlayer player;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Window bubbleWindow;
    readonly TextBlock bubbleText = new() { TextWrapping = TextWrapping.Wrap };
    readonly LinuxBubble bubbleFrame;
    readonly Mutex mutex;
    IPointer? capturedPointer;
    string phase = "Startup", fullText = "";
    DateTime bubbleUntil = DateTime.MaxValue;
    bool closed, exitReady, hidden, fullscreenHidden, dragging, dragged, greetingNoticeActive;
    PixelPoint dragOrigin, windowOrigin, moveOrigin;
    double moveStart, moveEnd;
    CompanionAnimationRule? moveRule;
    bool moveMirrored;
    Window? settings;
    bool animationActive, exitRequested;
    (CompanionAnimationRule Rule, string Phase, bool Mirror, string? Text)? pendingPlayback;
    CancellationTokenSource? lowPowerHold;
    static readonly IBrush Hair = new SolidColorBrush(Color.FromRgb(80, 96, 152));
    double SpriteSize => 280 * Preferences.Scale;

    internal LinuxPetWindow()
    {
        mutex = new Mutex(true, "BigBlueFish.Linux." + Environment.UserName, out var first);
        if (!first) Environment.Exit(0);
        Title = "蓝色大肥鱼"; WindowDecorations = WindowDecorations.None; ShowInTaskbar = false; ShowActivated = false;
        Topmost = true; CanResize = false; Background = Brushes.Transparent; TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        var canvas = new Grid(); canvas.Children.Add(sprite); Content = canvas;
        player = new(sprite); player.FramePresented += MoveWithAnimation;
        PositionChanged += (_, _) => MoveBubble();
        player.PixelsPresented += (pixels, width, height, mirror) => CompanionNative.SetInputShape(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, pixels, width, height, Width * DesktopScaling / width, mirror);
        player.Failed += error => { animationActive = false; Console.Error.WriteLine(error); if (exitRequested || phase == "Exit") CloseImmediately(); };
        bubbleText.FontFamily = new FontFamily("avares://bigbluefish/Assets#LemiXiaoNaiPaoTi");
        bubbleText.Foreground = Hair;
        var close = new Button { Content = "×", Padding = new Thickness(3), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        close.Click += (_, _) => { bubbleWindow!.Hide(); };
        var bubbleGrid = new Grid(); bubbleGrid.Children.Add(bubbleText); bubbleGrid.Children.Add(close); bubbleText.Margin = new Thickness(0, 0, 24, 0);
        bubbleFrame = new LinuxBubble { Child = bubbleGrid, Padding = new Thickness(16, 16, 16, 30) };
        bubbleWindow = new Window
        {
            Title = "蓝色大肥鱼 · 对话",
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            CanResize = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Content = bubbleFrame
        };
        Rescale();
        var screen = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        Position = new((int)(Preferences.Left ?? screen.Right - Width * DesktopScaling), (int)(Preferences.Top ?? screen.Bottom - Height * DesktopScaling)); Clamp();
        Opened += (_, _) =>
        {
            Play(policy.Find("女仆屈膝礼仪"), "Startup"); Welcome();
            InitializeMcp(); InitializeTray(); timer.Start();
        };
        PointerPressed += (_, e) =>
        {
            if (exitRequested || phase == "Exit") { e.Handled = true; return; }
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) { CancelGestureInput(); PublishInteraction("click", new { button = "right", Position.X, Position.Y }); OpenMenu(); e.Handled = true; return; }
            if (!point.Properties.IsLeftButtonPressed) return;
            dragging = true; dragged = false; moveRule = null; windowOrigin = Position;
            var screenPoint = this.PointToScreen(point.Position); dragOrigin = screenPoint;
            dragReaction = null;
            if (Preferences.MouseInteractions) gestures.Press(GestureScreen(screenPoint), Environment.TickCount64);
            capturedPointer = e.Pointer;
            e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (exitRequested || phase == "Exit") return;
            if (!dragging) return;
            var screenPoint = this.PointToScreen(e.GetPosition(this)); int dx = screenPoint.X - dragOrigin.X, dy = screenPoint.Y - dragOrigin.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) > 4 * DesktopScaling)
            {
                dragged = true; PublishInteraction("drag_start", new { Position.X, Position.Y }); if (!Puppet) Play(policy.Find("被鼠标拖拽悬空反馈"), "Drag");
            }
            Position = new(windowOrigin.X + dx, windowOrigin.Y + dy); MoveBubble();
            if (Preferences.MouseInteractions) HandleHeldGesture(gestures.MoveDrag(GestureScreen(screenPoint), Environment.TickCount64, dragged));
        };
        PointerReleased += (_, e) =>
        {
            if (exitRequested || phase == "Exit") return;
            if (!dragging) return;
            if (pendingPlayback?.Phase == "Drag") pendingPlayback = null;
            bool suppressClick = false;
            var feedback = Preferences.MouseInteractions ? gestures.Release(Environment.TickCount64, dragged, out suppressClick) : null;
            var reaction = dragReaction; dragReaction = null;
            var releasedPoint = this.PointToScreen(e.GetPosition(this));
            long clickAt = Environment.TickCount64;
            bool twice = !dragged && Preferences.AgentEnabled && lastClickAt != 0 && clickAt - lastClickAt <= 500
                && Math.Abs(releasedPoint.X - lastClickPoint.X) + Math.Abs(releasedPoint.Y - lastClickPoint.Y) <= 8;
            dragging = false; capturedPointer = null; e.Pointer.Capture(null); Clamp(); Save();
            if (dragged) { lastClickAt = 0; PublishInteraction("drag_end", new { Position.X, Position.Y, cancelled = false }); QueueAutomatic(); }
            else if (twice && TryOpenMcpInput()) { lastClickAt = 0; PublishInteraction("click", new { button = "left", count = 2, Position.X, Position.Y }); }
            else
            {
                PublishInteraction("click", new { button = "left", Position.X, Position.Y });
                lastClickAt = clickAt; lastClickPoint = releasedPoint;
                if (reaction != null) PlayGesture(reaction.Value);
                else if (feedback != null) PlayGesture(feedback.Value);
                else if (!suppressClick && !Puppet) Play(policy.Choose("Click", Context(), random), "Click");
                else if (!animationActive) QueueAutomatic();
            }
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) =>
        {
            // Normal button release already ended the press; keep its poke history.
            if (dragging) { if (dragged) PublishInteraction("drag_end", new { Position.X, Position.Y, cancelled = true }); CancelGestureInput(); QueueAutomatic(); }
        };
        Closing += (_, e) =>
        {
            Save();
            if (exitReady || closed) return;
            e.Cancel = true; if (exitRequested || phase == "Exit") return;
            mcpInput?.Close(); CancelGestureInput(); dragging = false; moveRule = null; settings?.Close(); hidden = false; fullscreenHidden = false; Show(); player.Paused = false;
            RequestPlayback(policy.Find("女仆屈膝礼仪"), "Exit", false, "主人再见~鱼鱼喜欢主人哦");
        };
        Closed += (_, _) => { StopMcp(); CancelLowPowerHold(); closed = true; timer.Stop(); player.Dispose(); bubbleWindow.Close(); settings?.Close(); Save(); mutex.Dispose(); (Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(); };
        timer.Tick += (_, _) => Tick();
    }
    CompanionAnimationContext Context() => new(DateTime.Now, BreakfastHour: Preferences.BreakfastHour, LunchHour: Preferences.LunchHour, DinnerHour: Preferences.DinnerHour);
    void Rescale()
    {
        Width = SpriteSize * 640 / 360; Height = SpriteSize; sprite.Width = Width; sprite.Height = Height;
        MoveBubble();
    }
    PixelPoint Clamp(PixelPoint? requested = null)
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        double s = DesktopScaling, left = SpriteSize * 208 / 360 * s, top = SpriteSize * 58 / 360 * s, right = SpriteSize * 428 / 360 * s, bottom = SpriteSize * 336 / 360 * s;
        var target = requested ?? Position;
        var result = new PixelPoint((int)Math.Clamp(target.X, area.X - left, Math.Max(area.X - left, area.Right - right)), (int)Math.Clamp(target.Y, area.Y - top, Math.Max(area.Y - top, area.Bottom - bottom)));
        Position = result; MoveBubble(); return result;
    }
    void Save() { if (closed) return; Preferences.Left = Position.X; Preferences.Top = Position.Y; Preferences.Save(); }
    void Play(CompanionAnimationRule rule, string nextPhase, bool mirror = false)
        => RequestPlayback(rule, nextPhase, mirror, null);
    void RequestPlayback(CompanionAnimationRule rule, string nextPhase, bool mirror, string? text)
    {
        if (closed || (exitRequested && nextPhase != "Exit")) return;
        if (nextPhase == "Exit") exitRequested = true;
        CancelLowPowerHold();
        if (animationActive || (dragging && dragged && nextPhase is not ("Drag" or "Exit")))
        {
            pendingPlayback = (rule, nextPhase, mirror, text);
            return;
        }
        pendingPlayback = null;
        StartPlayback(rule, nextPhase, mirror, text);
    }
    void StartPlayback(CompanionAnimationRule rule, string nextPhase, bool mirror, string? text)
    {
        if (nextPhase is "Gesture" or "Chat" or "External" or "PuppetIdle" or "Click" or "Drag" or "Exit") moveRule = null;
        animationActive = true; currentAction = rule.Id;
        if (phase == "Startup" && nextPhase != "Startup") EndWelcome();

        phase = nextPhase;
        if (text != null) ShowText(text);
        player.SoftwareHighDefinition = Preferences.SoftwareHighDefinition;
        player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), mirror, Finished,
            rate: rule.Pool == "Greeting" ? 1.5 : 1);
    }
    void QueueAutomatic()
    {
        if (closed || exitRequested || phase == "Exit" || dragging || hidden || fullscreenHidden || settings != null) return;
        if (animationActive) return;
        if (TryPlayExternal() || TryPlayPending()) return;
        if (Puppet) { Play(policy.Find("待机呼吸休闲"), "PuppetIdle"); return; }
        if (!Preferences.LowPowerAnimation) { PlayAutomatic(); return; }
        ScheduleLowPowerPause();
    }
    void PlayAutomatic()
    {
        if (closed || exitRequested || phase == "Exit" || dragging || hidden || fullscreenHidden || settings != null) return;
        var now = DateTime.Now;
        if (now.Hour == 23 && Preferences.LastBrushDate != now.ToString("yyyy-MM-dd"))
        {
            Preferences.LastBrushDate = now.ToString("yyyy-MM-dd"); Save();
            Play(policy.Find("晨间刷牙"), "Daily"); return;
        }
        PlayAutomaticClip(policy.Choose("Automatic", Context(), random));
    }
    void ScheduleLowPowerPause()
    {
        CancelLowPowerHold();
        var hold = new CancellationTokenSource();
        lowPowerHold = hold;
        _ = HoldThenAutomatic(hold);
    }
    async Task HoldThenAutomatic(CancellationTokenSource hold)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Preferences.LowPowerIdleSeconds), hold.Token); }
        catch (OperationCanceledException) { return; }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (lowPowerHold != hold) return;
            lowPowerHold = null; hold.Dispose();
            if (closed || hidden || fullscreenHidden || dragging || settings != null || animationActive || !Preferences.LowPowerAnimation) return;
            PlayAutomatic();
        });
    }
    void CancelLowPowerHold()
    {
        var hold = lowPowerHold;
        lowPowerHold = null;
        if (hold == null) return;
        hold.Cancel(); hold.Dispose();
    }
    void ResumeAutomaticIfIdle()
    {
        if (closed || animationActive || dragging || hidden || fullscreenHidden || settings != null || phase == "Exit") return;
        CancelLowPowerHold();
        if (TryPlayExternal() || TryPlayPending()) return;
        if (Puppet) { Play(policy.Find("待机呼吸休闲"), "PuppetIdle"); return; }
        PlayAutomatic();
    }
    bool TryPlayPending()
    {
        if (pendingPlayback == null || animationActive) return false;
        var next = pendingPlayback.Value;
        if (next.Phase != "Exit" && (hidden || fullscreenHidden || settings != null)) return false;
        if (next.Phase == "Drag" && !dragging) { pendingPlayback = null; return false; }
        if (next.Phase != "Exit" && next.Phase != "Drag" && dragging && dragged) return false;
        pendingPlayback = null;
        StartPlayback(next.Rule, next.Phase, next.Mirror, next.Text);
        return true;
    }
    void Finished()
    {
        animationActive = false;

        if (closed) return;
        if (phase == "Exit") { CloseImmediately(); return; }
        if (phase == "Startup") EndWelcome();
        if (phase == "Drag") { if (!dragging) QueueAutomatic(); return; }
        if (phase == "Outbound" && moveRule != null) { Position = new((int)moveEnd, Position.Y); Play(policy.Choose("Idle", Context(), random), "BetweenMoves"); return; }
        if (phase == "BetweenMoves" && moveRule != null) { moveStart = Position.X; moveEnd = moveOrigin.X; Play(moveRule, "Return", !moveMirrored); return; }
        if (phase == "Return") { Position = new(moveOrigin.X, Position.Y); moveRule = null; }
        if (TryPlayExternal() || TryPlayPending()) return;
        if (dragging || hidden || fullscreenHidden || settings != null) return;
        QueueAutomatic();
    }
    void PlayAutomaticClip(CompanionAnimationRule rule)
    {
        if (rule.MoveKind == null) { Play(rule, "Automatic"); return; }
        moveRule = rule; moveOrigin = Position; moveStart = Position.X;
        Position = new(Position.X - (int)(70 * Preferences.Scale * DesktopScaling), Position.Y); Clamp(); moveEnd = Position.X;
        if (Math.Abs(moveEnd - moveStart) < 15 * DesktopScaling) { Position = new((int)(moveStart + 70 * Preferences.Scale * DesktopScaling), Position.Y); Clamp(); moveEnd = Position.X; }
        Position = moveOrigin; moveMirrored = moveEnd > moveStart; Play(rule, "Outbound", moveMirrored);
    }
    void MoveWithAnimation(double seconds)
    {
        if (moveRule == null || phase is not ("Outbound" or "Return")) return;
        double lead = moveRule.MoveKind == "Run" ? 1.75 : 2, tail = moveRule.MoveKind == "Run" ? 4.8 : 2;
        double fraction = Math.Clamp((seconds - lead) / (10 - lead - tail), 0, 1);
        Position = new((int)(moveStart + (moveEnd - moveStart) * fraction), Position.Y); MoveBubble();
    }
    void Welcome()
    {
        string period = DateTime.Now.Hour switch { >= 5 and < 11 => "早上", >= 11 and < 14 => "中午", >= 14 and < 18 => "下午", _ => "晚上" };
        ShowText($"主人{period}好，鱼鱼给主人请安啦~");
        greetingNoticeActive = true;
    }
    void EndWelcome() { if (greetingNoticeActive) bubbleWindow.Hide(); greetingNoticeActive = false; }
    void ShowText(string text)
    {
        StopTyping();
        greetingNoticeActive = false;
        fullText = text; bubbleText.Text = text;
        ResetDwell();
        MoveBubble(); if (!hidden && !fullscreenHidden) ShowBubble();
    }
    void ShowBubble()
    {
        if (bubbleWindow.IsVisible) return;
        bubbleWindow.Show(this);
        // Some X11 WMs discard ABOVE when a window is withdrawn. Reassert it
        // after mapping, without activating the bubble or raising it per letter.
        bubbleWindow.Topmost = false;
        bubbleWindow.Topmost = true;
        bubbleWindow.ShowInTaskbar = true;
        bubbleWindow.ShowInTaskbar = false;
    }
    void ResetDwell() { int duration = CompanionPreferences.BubbleDurations[Preferences.BubbleDurationIndex]; bubbleUntil = duration == 0 ? DateTime.MaxValue : DateTime.UtcNow.AddSeconds(duration); }
    void MoveBubble()
    {
        if (bubbleFrame == null || bubbleWindow == null) return;
        double scale = Preferences.BubbleScale;
        bubbleText.FontSize = 17 * scale; bubbleText.Margin = new(0, 0, 24 * scale, 0); bubbleText.MaxWidth = 270 * scale;
        bubbleFrame.Padding = new(16 * scale, 16 * scale, 16 * scale, 30 * scale);
        // Measure independently: an already arranged bubble retains its previous
        // window width until the next layout pass, which can clip a new greeting.
        var measure = new TextBlock
        {
            Text = fullText,
            FontFamily = bubbleText.FontFamily,
            FontSize = bubbleText.FontSize,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = bubbleText.MaxWidth
        };
        measure.Measure(new Size(270 * scale, double.PositiveInfinity));
        var size = new Size(measure.DesiredSize.Width + 56 * scale,
            Math.Max(measure.DesiredSize.Height, 28 * scale) + 46 * scale);
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        bubbleWindow.Width = Math.Min(size.Width, area.Width / DesktopScaling - 16); bubbleWindow.Height = Math.Min(size.Height, area.Height / DesktopScaling - 16);
        int width = (int)(bubbleWindow.Width * DesktopScaling), height = (int)(bubbleWindow.Height * DesktopScaling);
        int x = Position.X + (int)(Width * DesktopScaling / 2) - width / 2, y = Position.Y + (int)(SpriteSize * 58 / 360 * DesktopScaling) - height - 10;
        if (y < area.Y) y = Position.Y + (int)(SpriteSize * 336 / 360 * DesktopScaling) + 10;
        bubbleWindow.Position = new(Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - width - 8)), Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - height - 8)));
    }
    void Tick()
    {
        if (closed || exitRequested || phase == "Exit") return;
        lastForeground = CompanionNative.ResolveForeground(CompanionNative.GetForegroundWindow(), lastForeground);
        bool full = Preferences.HideOnFullscreen && CompanionNative.IsFullscreen();
        if (full && !fullscreenHidden && !hidden) { fullscreenHidden = true; CancelGestureInput(); Hide(); bubbleWindow.Hide(); }
        else if (!full && fullscreenHidden) { fullscreenHidden = false; if (!hidden) { Show(); Clamp(); ResumeAutomaticIfIdle(); } }
        player.Paused = hidden || fullscreenHidden;
        if (hidden || fullscreenHidden) return;
        TickAgent(DateTime.Now);
        TickTyping();
        TickGestures();
        if (DateTime.UtcNow >= bubbleUntil) bubbleWindow.Hide();

    }
    void OpenMenu()
    {
        if (exitRequested || closed) return;
        var menu = new ContextMenu();
        MenuItem Item(string title, Action action, bool available = true) { var item = new MenuItem { Header = title, IsEnabled = available }; item.Click += (_, _) => action(); return item; }
        menu.ItemsSource = new[] { Item("聊天", () => TryOpenMcpInput()), Item("吐字测试", () => _ = ManualSpeech()), Item("设置", OpenSettings),
            Item(hidden ? "显示桌宠" : "隐藏桌宠", () => { if (hidden) { hidden = false; player.Paused = false; Show(); Clamp(); ResumeAutomaticIfIdle(); } else { hidden = true; CancelGestureInput(); Hide(); bubbleWindow.Hide(); } }), Item("关闭桌宠", Close) };
        menu.Closed += (_, _) => ResumeAutomaticIfIdle();
        ContextMenu = menu; menu.Open(this);
    }
    void CloseImmediately() { exitReady = true; Close(); }
}
