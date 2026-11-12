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
    string phase = "Startup", fullText = "";
    DateTime bubbleUntil = DateTime.MaxValue;
    bool closed, exitReady, hidden, fullscreenHidden, dragging, dragged, greetingNoticeActive;
    PixelPoint dragOrigin, windowOrigin, moveOrigin;
    double moveStart, moveEnd;
    CompanionAnimationRule? moveRule;
    bool moveMirrored;
    Window? settings;
    bool animationActive;
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
        player.Failed += error => { animationActive = false; Console.Error.WriteLine(error); if (phase == "Exit") CloseImmediately(); };
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
            InitializeTray(); timer.Start();
        };
        PointerPressed += (_, e) =>
        {
            if (phase == "Exit") { e.Handled = true; return; }
            var point = e.GetCurrentPoint(this);
            if (point.Properties.IsRightButtonPressed) { OpenMenu(); e.Handled = true; return; }
            if (!point.Properties.IsLeftButtonPressed) return;
            dragging = true; dragged = false; moveRule = null; windowOrigin = Position;
            var screenPoint = this.PointToScreen(point.Position); dragOrigin = screenPoint;
            e.Pointer.Capture(this); e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (phase == "Exit") return;
            if (!dragging) return;
            var screenPoint = this.PointToScreen(e.GetPosition(this)); int dx = screenPoint.X - dragOrigin.X, dy = screenPoint.Y - dragOrigin.Y;
            if (!dragged && Math.Abs(dx) + Math.Abs(dy) > 4 * DesktopScaling)
            {
                dragged = true; Play(policy.Find("被鼠标拖拽悬空反馈"), "Drag");
            }
            Position = new(windowOrigin.X + dx, windowOrigin.Y + dy); MoveBubble();
        };
        PointerReleased += (_, e) =>
        {
            if (phase == "Exit") return;
            if (!dragging) return; dragging = false; e.Pointer.Capture(null); Clamp(); Save();
            if (dragged) { QueueAutomatic(); }
            else
            {
                Play(policy.Choose("Click", Context(), random), "Click");
            }
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (dragging) { dragging = false; QueueAutomatic(); } };
        Closing += (_, e) =>
        {
            Save();
            if (exitReady || closed) return;
            e.Cancel = true; if (phase == "Exit") return;
            dragging = false; moveRule = null; settings?.Close(); hidden = false; fullscreenHidden = false; Show(); player.Paused = false;
            Play(policy.Find("女仆屈膝礼仪"), "Exit"); ShowText("主人再见~鱼鱼喜欢主人哦");
        };
        Closed += (_, _) => { CancelLowPowerHold(); closed = true; timer.Stop(); player.Dispose(); bubbleWindow.Close(); settings?.Close(); Save(); mutex.Dispose(); (Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(); };
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
    {
        if (closed) return;
        CancelLowPowerHold();
        animationActive = true;
        if (phase == "Startup" && nextPhase != "Startup") EndWelcome();

        phase = nextPhase;
        player.SoftwareHighDefinition = Preferences.SoftwareHighDefinition;
        player.Play(Path.Combine(AppContext.BaseDirectory, "assets/fish", rule.File), mirror, Finished,
            nextPhase == "Drag" ? 1.5 : 0, nextPhase == "Drag" ? 6 : null, rule.Pool == "Greeting" ? 1.5 : 1);
    }
    void QueueAutomatic()
    {
        if (closed || phase == "Exit" || dragging || hidden || fullscreenHidden || settings != null) return;
        if (!Preferences.LowPowerAnimation) { PlayAutomatic(); return; }
        if (animationActive) return;
        ScheduleLowPowerPause();
    }
    void PlayAutomatic()
    {
        if (closed || phase == "Exit" || dragging || hidden || fullscreenHidden || settings != null) return;
        var now = DateTime.Now;
        if (now.Hour == 23 && Preferences.LastBrushDate != now.ToString("yyyy-MM-dd")) {
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
        PlayAutomatic();
    }
    void Finished()
    {
        animationActive = false;

        if (closed) return;
        if (phase == "Exit") { CloseImmediately(); return; }
        if (phase == "Drag") { if (!dragging) QueueAutomatic(); return; }
        if (phase == "Outbound" && moveRule != null) { Position = new((int)moveEnd, Position.Y); Play(policy.Choose("Idle", Context(), random), "BetweenMoves"); return; }
        if (phase == "BetweenMoves" && moveRule != null) { moveStart = Position.X; moveEnd = moveOrigin.X; Play(moveRule, "Return", !moveMirrored); return; }
        if (phase == "Return") { Position = new(moveOrigin.X, Position.Y); moveRule = null; }
        if (phase == "Startup") EndWelcome();
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
        if (closed || phase == "Exit") return;
        bool full = Preferences.HideOnFullscreen && CompanionNative.IsFullscreen();
        if (full && !fullscreenHidden && !hidden) { fullscreenHidden = true; Hide(); bubbleWindow.Hide(); }
        else if (!full && fullscreenHidden) { fullscreenHidden = false; if (!hidden) { Show(); Clamp(); ResumeAutomaticIfIdle(); } }
        player.Paused = hidden || fullscreenHidden;
        if (hidden || fullscreenHidden) return;
        if (DateTime.UtcNow >= bubbleUntil) bubbleWindow.Hide();

    }
    void OpenMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string title, Action action, bool available = true) { var item = new MenuItem { Header = title, IsEnabled = available }; item.Click += (_, _) => action(); return item; }
        menu.ItemsSource = new[] { Item("设置", OpenSettings),
            Item(hidden ? "显示桌宠" : "隐藏桌宠", () => { if (hidden) { hidden = false; player.Paused = false; Show(); Clamp(); ResumeAutomaticIfIdle(); } else { hidden = true; Hide(); bubbleWindow.Hide(); } }), Item("关闭桌宠", Close) };
        ContextMenu = menu; menu.Open(this);
    }
    void CloseImmediately() { exitReady = true; Close(); }
}
