using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BigBlueFish;

internal sealed partial class LinuxPetWindow
{
    static string AutostartFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart/bigbluefish.desktop");
    void SetStartup(bool enabled)
    {
        if (!enabled) { File.Delete(AutostartFile); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
        string executable = Path.Combine(AppContext.BaseDirectory, "bigbluefish")
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        File.WriteAllText(AutostartFile, $"[Desktop Entry]\nType=Application\nName=蓝色大肥鱼\nComment=Ubuntu 桌面动画伴侣\nExec=\"{executable}\"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
    }
    void OpenSettings()
    {
        if (exitRequested || closed) return;
        CancelGestureInput();
        if (settings != null) { settings.WindowState = WindowState.Normal; settings.Activate(); return; }
        var body = new StackPanel { Spacing = 10 };
        void Heading(string text) => body.Children.Add(new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        void Toggle(string text, bool value, Action<bool> set)
        {
            var box = new CheckBox { Content = text, IsChecked = value };
            box.IsCheckedChanged += (_, _) => { set(box.IsChecked == true); Save(); };
            body.Children.Add(box);
        }
        void Slider(string text, double value, double min, double max, double step, Action<double> set)
        {
            var label = new TextBlock { Text = $"{text}：{value:0.##}" };
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, _) => { label.Text = $"{text}：{slider.Value:0.##}"; set(slider.Value); Save(); };
            body.Children.Add(label); body.Children.Add(slider);
        }
        Heading("启动");
        var startupStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray };
        var startup = new CheckBox { Content = "开机自启动（登录桌面后自动运行鱼鱼）", IsChecked = StartupEnabled };
        startup.Click += (_, _) =>
        {
            try { SetStartup(startup.IsChecked == true); Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { startup.IsChecked = StartupEnabled; startupStatus.Text = "设置失败：" + error.Message; return; }
            startupStatus.Text = StartupEnabled ? "已启用：登录桌面后自动运行。" : "已关闭。";
        };
        startupStatus.Text = StartupEnabled ? "已启用：登录桌面后自动运行。" : "已关闭。";
        body.Children.Add(startup); body.Children.Add(startupStatus);
        refreshStartupSetting = () => { startup.IsChecked = StartupEnabled; startupStatus.Text = StartupEnabled ? "已启用：登录桌面后自动运行。" : "已关闭。"; };
        Heading("桌宠与显示");
        Slider("桌宠大小（%）", Preferences.Scale * 100, 50, 200, 5, value => { Preferences.Scale = value / 100; Rescale(); Clamp(); });
        Slider("气泡大小（%）", Preferences.BubbleScale * 100, 50, 200, 5, value => { Preferences.BubbleScale = value / 100; MoveBubble(); });
        Toggle("全屏时自动隐藏", Preferences.HideOnFullscreen, value =>
        {
            Preferences.HideOnFullscreen = value;
            if (!value && fullscreenHidden) { fullscreenHidden = false; if (!hidden) { Show(); player.Paused = false; ResumeAutomaticIfIdle(); } }
        });
        body.Children.Add(new TextBlock { Text = "气泡停留时间" });
        var duration = new ComboBox { ItemsSource = CompanionPreferences.BubbleDurationLabels, SelectedIndex = Preferences.BubbleDurationIndex, HorizontalAlignment = HorizontalAlignment.Stretch };
        duration.SelectionChanged += (_, _) => { if (duration.SelectedIndex < 0) return; Preferences.BubbleDurationIndex = duration.SelectedIndex; ResetDwell(); Save(); };
        body.Children.Add(duration);
        body.Children.Add(new TextBlock { Text = "软件解码画质" });
        var quality = new ComboBox
        {
            ItemsSource = new[] { "低清（640×360，24 帧）", "高清（1280×720，60 帧）" },
            SelectedIndex = Preferences.SoftwareHighDefinition ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        quality.SelectionChanged += (_, _) =>
        {
            if (quality.SelectedIndex < 0) return;
            Preferences.SoftwareHighDefinition = quality.SelectedIndex == 1;
            Save();
        };
        body.Children.Add(quality);
        body.Children.Add(new TextBlock
        {
            Text = "下一个动作起生效。高清软件解码会增加 CPU 占用；硬件解码始终使用高清素材。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.SlateGray
        });
        Heading("鼠标互动");
        Toggle("开启趣味鼠标互动", Preferences.MouseInteractions, value =>
        {
            Preferences.MouseInteractions = value; gestures = new(); dragReaction = null;
        });
        body.Children.Add(new TextBlock
        {
            Text = "慢慢摸头、快速挠尾巴、连续戳戳、长按、拎着不放或来回甩动，都能得到不同回应。生气后可以摸头安抚。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.SlateGray
        });
        Heading("动作与作息");
        Toggle("低功耗动画（播放后暂停解码）", Preferences.LowPowerAnimation, value => { Preferences.LowPowerAnimation = value; if (!value) { CancelLowPowerHold(); ResumeAutomaticIfIdle(); } });
        Slider("低功耗静止时长（秒）", Preferences.LowPowerIdleSeconds, 3, 120, 1, value => Preferences.LowPowerIdleSeconds = (int)value);
        Slider("早餐时间（时）", Preferences.BreakfastHour, 0, 23, 1, value => Preferences.BreakfastHour = (int)value);
        Slider("午餐时间（时）", Preferences.LunchHour, 0, 23, 1, value => Preferences.LunchHour = (int)value);
        Slider("晚餐时间（时）", Preferences.DinnerHour, 0, 23, 1, value => Preferences.DinnerHour = (int)value);
        BuildAgentSettings(body);
        BuildMemoryReminderSettings(body);
        Heading("关于");
        body.Children.Add(new TextBlock { Text = "蓝色大肥鱼 " + typeof(LinuxPetWindow).Assembly.GetName().Version!.ToString(3) + "\nUbuntu 桌面动画伴侣", TextWrapping = TextWrapping.Wrap });
        settings = new Window
        {
            Title = "蓝色大肥鱼 · 设置",
            Width = 560,
            Height = 720,
            MinWidth = 440,
            MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Noto Sans CJK SC"),
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)),
            Content = new ScrollViewer { Content = new Border { Padding = new Thickness(28), Child = body } }
        };
        settings.Closed += (_, _) => { refreshAgentSettings = null; refreshStartupSetting = null; settings = null; Save(); ResumeAutomaticIfIdle(); };
        settings.Show(); settings.Activate();
    }
}
