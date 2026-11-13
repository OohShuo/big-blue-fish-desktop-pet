using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BigBlueFish.Agent;
using BigBlueFish.Control;

namespace BigBlueFish;

internal sealed partial class LinuxPetWindow
{
    bool consentOpen;
    Window? consentWindow;

    static Button ActionButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 4, 10, 4), Background = Brushes.White, Foreground = Hair,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 237)), BorderThickness = new Thickness(1)
        };
        button.Click += (_, _) => action(); return button;
    }

    static TextBlock Paragraph(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12),
        Foreground = Brushes.SlateGray
    };

    static Window Dialog(string title, Avalonia.Controls.Control content, double width = 630, double height = 460) => new()
    {
        Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 360,
        FontFamily = new FontFamily("Noto Sans CJK SC"), FontSize = 14,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 251)),
        Content = new Border { Padding = new Thickness(28), Child = content }
    };

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    async Task<bool> Confirm(string title, string message, string accept = "确定", CancellationToken token = default, string reject = "取消")
    {
        if (consentOpen) throw new InvalidOperationException("请先处理已打开的提示窗口。");
        consentOpen = true; bool accepted = false;
        try
        {
            var panel = new StackPanel { Spacing = 15 };
            panel.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold });
            panel.Children.Add(Paragraph(message));
            var dialog = Dialog(title, panel);
            var buttons = new WrapPanel();
            buttons.Children.Add(ActionButton(accept, () => { accepted = true; dialog.Close(); }));
            buttons.Children.Add(ActionButton(reject, dialog.Close));
            panel.Children.Add(buttons);
            using var registration = token.Register(() => Dispatcher.UIThread.Post(() => { accepted = false; dialog.Close(); }));
            var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.Closed += (_, _) => finished.TrySetResult(accepted && !token.IsCancellationRequested);
            consentWindow = dialog;
            if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
            return await finished.Task;
        }
        finally { consentWindow = null; consentOpen = false; }
    }

    void AgentError(string title, Exception error)
    {
        var body = new StackPanel { Spacing = 15 };
        body.Children.Add(Paragraph(error.Message));
        var dialog = Dialog(title, body);
        body.Children.Add(ActionButton("关闭", dialog.Close));
        if (settings?.IsVisible == true) dialog.Show(settings); else dialog.Show();
    }

    static async Task<string?> SelectAgentFolder(Window owner, string title, string initial)
    {
        var suggested = await owner.StorageProvider.TryGetFolderFromPathAsync(initial);
        var found = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = suggested });
        return found.FirstOrDefault()?.TryGetLocalPath();
    }

    void BuildAgentSettings(StackPanel body)
    {
        body.Children.Add(new TextBlock { Text = "连接 Agent", FontSize = 21, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
        var enabled = new CheckBox { Content = "连接本机兼容的 CLI Harness", IsChecked = Preferences.AgentEnabled, Margin = new Thickness(0, 8, 0, 4) };
        body.Children.Add(enabled);
        body.Children.Add(Paragraph("通过已安装的 Claude Code、Codex CLI 或 OpenCode 与鱼鱼对话，并控制鱼鱼的行为。"));
        var extra = new StackPanel { IsVisible = Preferences.AgentEnabled, Spacing = 8 }; body.Children.Add(extra);
        extra.Children.Add(new TextBlock { Text = "选择 Harness", FontWeight = FontWeight.SemiBold });
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var adapters = new AgentAdapterStore(Path.Combine(Environment.CurrentDirectory, "agent"));
        bool refreshing = false;
        void Detect()
        {
            refreshing = true;
            select.ItemsSource = HarnessCatalog.All.OrderByDescending(h => adapters.FindExecutable(h) != null)
                .Select(h => new ComboBoxItem { Content = h.Name + (adapters.FindExecutable(h) == null ? " · 未检测到" : " · 已检测到"), Tag = h.Id }).ToArray();
            select.SelectedItem = select.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string?)x.Tag == Preferences.AgentHarnessId);
            refreshing = false;
        }
        Detect(); extra.Children.Add(select);
        var buttons = new WrapPanel();
        buttons.Children.Add(ActionButton("重新检测", Detect));
        buttons.Children.Add(ActionButton("安装与登录说明", () => OpenUrl(HarnessCatalog.Get(Preferences.AgentHarnessId).Website)));
        extra.Children.Add(buttons);
        var state = Paragraph(""); extra.Children.Add(state);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("重新连接", () => agent?.Reconnect()));
        actions.Children.Add(ActionButton("开始新对话", async () =>
        {
            if (await Confirm("开始新对话", "将建立新的鱼鱼对话，后续使用新的上下文。")) agent?.NewConversation();
        }));
        actions.Children.Add(ActionButton("取消当前请求", () => agent?.Cancel()));
        extra.Children.Add(actions);
        var refreshWorkspace = BuildAgentWorkspaceSettings(extra);
        var refreshConfiguration = BuildAgentConfigurationSettings(extra);
        var daily = new CheckBox { Content = ControlTools.DailySpeechLabel, IsChecked = Preferences.AgentDailySpeech, Margin = new Thickness(0, 12, 0, 4) };
        extra.Children.Add(daily); extra.Children.Add(Paragraph(ControlTools.DailySpeechDescription));
        extra.Children.Add(new TextBlock { Text = "日常关心间隔" });
        var frequency = new ComboBox
        {
            ItemsSource = CompanionPreferences.SpeechRanges.Select(r => $"{r.Min}–{r.Max} 分钟").ToArray(),
            SelectedIndex = Math.Max(0, Preferences.SpeechRangeIndex),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        frequency.SelectionChanged += (_, _) =>
        {
            if (frequency.SelectedIndex < 0) return;
            Preferences.SpeechMinMinutes = CompanionPreferences.SpeechRanges[frequency.SelectedIndex].Min;
            nextAgentSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random)); Save();
        };
        extra.Children.Add(frequency);
        var puppet = new CheckBox { Content = ControlTools.BehaviorControlLabel, IsChecked = Preferences.PuppetMode, Margin = new Thickness(0, 6, 0, 4) };
        extra.Children.Add(puppet); extra.Children.Add(Paragraph(ControlTools.BehaviorControlDescription));
        var wake = Paragraph(""); extra.Children.Add(wake);
        extra.Children.Add(new TextBlock { Text = "鼠标互动响应", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        var clickResponse = new CheckBox { Content = "点击时让 Agent 回应", IsChecked = Preferences.AgentRespondToClick };
        var dragResponse = new CheckBox { Content = "拖动结束时让 Agent 回应", IsChecked = Preferences.AgentRespondToDrag };
        extra.Children.Add(clickResponse); extra.Children.Add(dragResponse);
        extra.Children.Add(Paragraph("开启后，每次点击或拖动结束都会发起一轮 Agent 请求，可能增加延迟和模型费用。关闭时这些事件仍会记录在交互资源中，但不会主动叫醒 Agent。"));
        clickResponse.Click += (_, _) => { Preferences.AgentRespondToClick = clickResponse.IsChecked == true; Save(); };
        dragResponse.Click += (_, _) => { Preferences.AgentRespondToDrag = dragResponse.IsChecked == true; Save(); };
        var details = Paragraph(""); extra.Children.Add(new Expander { Header = "连接详情", Content = details });
        extra.Children.Add(new TextBlock { Text = "固定天气地区", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        extra.Children.Add(Paragraph("桌宠通过 Open-Meteo 获取固定地区的天气，不需要 API Key。Agent 可以调用 get_weather；开启变化提醒后，只有明显变化才会额外发起 Agent 轮次；位置会发送给 Open-Meteo。"));
        var weatherName = new TextBox { Text = Preferences.WeatherLabel, PlaceholderText = "地区名称，例如：合肥" };
        var weatherLatitude = new TextBox { Text = Preferences.WeatherLatitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "", PlaceholderText = "纬度，例如 31.82" };
        var weatherLongitude = new TextBox { Text = Preferences.WeatherLongitude?.ToString("0.####", CultureInfo.InvariantCulture) ?? "", PlaceholderText = "经度，例如 117.23" };
        var weatherStatus = Paragraph(CurrentWeatherLocation == null ? "尚未设置地区；Agent 的 get_weather 会返回错误。" : "当前：" + CurrentWeatherLocation.Label);
        var weatherAlerts = new CheckBox { Content = "天气变化时让 Agent 提醒", IsChecked = Preferences.WeatherAgentAlerts };
        var weatherButtons = new WrapPanel();
        weatherButtons.Children.Add(ActionButton("查询城市", async () =>
        {
            try
            {
                var results = await Weather.SearchAsync(weatherName.Text ?? "", CancellationToken.None);
                if (results.Length == 0) { weatherStatus.Text = "没有找到匹配地区，请尝试更完整的城市名。"; return; }
                var first = results[0];
                weatherName.Text = first.Label; weatherLatitude.Text = first.Latitude.ToString("0.####", CultureInfo.InvariantCulture);
                weatherLongitude.Text = first.Longitude.ToString("0.####", CultureInfo.InvariantCulture);
                Preferences.WeatherLabel = first.Label; Preferences.WeatherLatitude = first.Latitude; Preferences.WeatherLongitude = first.Longitude;
                Save(); weatherStatus.Text = "已选择：" + first.Label; Refresh();
            }
            catch (Exception error) when (error is IOException or HttpRequestException or TaskCanceledException or ArgumentException)
            { weatherStatus.Text = "查询失败：" + error.Message; }
        }));
        weatherButtons.Children.Add(ActionButton("保存地区", () =>
        {
            bool hasLat = double.TryParse(weatherLatitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude);
            bool hasLon = double.TryParse(weatherLongitude.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude);
            if (!hasLat || !hasLon || latitude is < -90 or > 90 || longitude is < -180 or > 180)
            { weatherStatus.Text = "纬度和经度必须是有效数字：纬度 -90–90，经度 -180–180。"; return; }
            Preferences.WeatherLabel = (weatherName.Text ?? "").Trim();
            Preferences.WeatherLatitude = latitude; Preferences.WeatherLongitude = longitude;
            Save(); weatherStatus.Text = "已保存：" + (Preferences.WeatherLabel.Length > 0 ? Preferences.WeatherLabel : "固定地区"); Refresh();
        }));
        extra.Children.Add(weatherName); extra.Children.Add(weatherLatitude); extra.Children.Add(weatherLongitude);
        extra.Children.Add(weatherButtons); extra.Children.Add(weatherAlerts); extra.Children.Add(weatherStatus);
        weatherAlerts.Click += (_, _) => { Preferences.WeatherAgentAlerts = weatherAlerts.IsChecked == true && CurrentWeatherLocation != null; Save(); };
        extra.Children.Add(ActionButton("编辑人设", () =>
        {
            if (agent == null) return;
            var skills = agent.Skills; skills.Install();
            var editor = new TextBox { Text = File.ReadAllText(skills.PersonaPath), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 320 };
            var content = new StackPanel { Spacing = 15 };
            content.Children.Add(Paragraph("保存后在下一轮消息中应用，可修改或清空。"));
            content.Children.Add(editor);
            var window = Dialog("鱼鱼人设", content, 700, 560);
            content.Children.Add(ActionButton("保存", () => { AtomicText.Write(skills.PersonaPath, editor.Text ?? ""); window.Close(); }));
            window.Show(settings!);
        }));
        void Refresh()
        {
            refreshWorkspace(); refreshConfiguration();
            state.Text = (agent?.Status ?? "尚未连接") + "\n" + (agent?.Detail ?? "选择后连接，双击鱼鱼即可对话。");
            details.Text = $"程序：{agent?.ExecutablePath ?? "尚未启动"}\n版本：{agent?.Version}\n模型：{agent?.CurrentModel}\n对话编号：{agent?.SessionId}\n内部工具连接：{mcpServer?.Connections ?? 0}\n点击响应：{Preferences.AgentRespondToClick}；拖动响应：{Preferences.AgentRespondToDrag}（可由 Agent 调整）";
            wake.Text = Preferences.PuppetMode ? "下一次日常关心：" + (agent?.NextWake ?? "等待 Agent 安排")
                : Preferences.AgentDailySpeech ? "鱼鱼按说话间隔触发日常关心，由 Agent 编写。" : "自动日常关心已关闭；双击鱼鱼仍可对话。";
            if (Preferences.PuppetMode && agent?.Connected != true) wake.Text += "\nAgent 未连接，鱼鱼保持待机；关闭接管可恢复鱼鱼自己的动作安排。";
            daily.IsChecked = Preferences.AgentDailySpeech;
            puppet.IsChecked = Preferences.PuppetMode;
            puppet.IsEnabled = Preferences.AgentDailySpeech;
            clickResponse.IsChecked = Preferences.AgentRespondToClick;
            dragResponse.IsChecked = Preferences.AgentRespondToDrag;
            weatherAlerts.IsChecked = Preferences.WeatherAgentAlerts;
            weatherAlerts.IsEnabled = CurrentWeatherLocation != null;
            frequency.IsEnabled = Preferences.AgentDailySpeech && !Preferences.PuppetMode;
        }
        select.SelectionChanged += (_, _) =>
        {
            if (refreshing || select.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            Preferences.AgentHarnessId = id; ConfigureAgent(); Save(); Refresh();
        };
        daily.Click += (_, _) =>
        {
            Preferences.AgentDailySpeech = daily.IsChecked == true;
            if (!Preferences.AgentDailySpeech && Preferences.PuppetMode) SetPuppet(false);
            nextAgentSpeech = DateTime.UtcNow.Add(Preferences.NextSpeechDelay(random));
            ConfigureAgent(); Save(); Refresh();
        };
        puppet.Click += async (_, _) =>
        {
            bool on = puppet.IsChecked == true;
            try
            {
                if (on && !await Confirm(ControlTools.BehaviorControlTitle, ControlTools.PuppetWarning, "了解并开启")) { puppet.IsChecked = false; return; }
                SetPuppet(on); Save(); Refresh();
            }
            catch (InvalidOperationException error) { puppet.IsChecked = false; AgentError("无法接管", error); }
        };
        enabled.Click += async (_, _) =>
        {
            enabled.IsEnabled = false;
            try
            {
                bool on = enabled.IsChecked == true;
                if (on && !await Confirm("连接本机 Agent", ControlTools.PrivacyWarning, "了解并开启")) { enabled.IsChecked = false; return; }
                if (!on) SetPuppet(false);
                Preferences.AgentEnabled = on;
                if (on) InitializeMcp(); else StopMcp();
                extra.IsVisible = on;
                puppet.IsChecked = Preferences.PuppetMode;
                Save(); Refresh();
            }
            finally { enabled.IsEnabled = true; }
        };
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        clock.Tick += (_, _) => Refresh();
        extra.DetachedFromVisualTree += (_, _) => clock.Stop();
        refreshAgentSettings = Refresh;
        Refresh(); clock.Start();
    }

    Action BuildAgentWorkspaceSettings(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "工作区", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        var path = Paragraph(""); panel.Children.Add(path);
        panel.Children.Add(Paragraph("文件由各 Harness 共享；新建对话和项目仍使用当前目录。切换工作区会结束当前请求。"));
        var buttons = new WrapPanel(); panel.Children.Add(buttons); bool operating = false;
        async Task Run(Func<Task> action)
        {
            if (operating) return; operating = true;
            try { await action(); } catch (OperationCanceledException) { }
            catch (Exception error) { AgentError("工作区操作失败", error); }
            finally { operating = false; }
        }
        buttons.Children.Add(ActionButton("新建工作区", () =>
        {
            if (agent == null || settings == null) return;
            var current = agent; string parent = Directory.GetParent(current.Workspaces.Current)?.FullName ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(Paragraph("创建独立文件夹，并将鱼鱼切换到这里工作。"));
            var name = new TextBox { Text = "新工作区", PlaceholderText = "文件夹名称" }; body.Children.Add(name);
            var pathText = Paragraph(parent); body.Children.Add(pathText);
            var dialog = Dialog("新建工作区", body, 650, 440);
            body.Children.Add(ActionButton("选择位置", async () => { var selected = await SelectAgentFolder(dialog, "选择新工作区的存放位置", parent); if (selected != null) { parent = selected; pathText.Text = parent; } }));
            var actions = new WrapPanel(); body.Children.Add(actions);
            actions.Children.Add(ActionButton("创建并打开", async () => await Run(async () => { await current.CreateWorkspace(parent, name.Text ?? ""); dialog.Close(); })));
            actions.Children.Add(ActionButton("取消", dialog.Close)); dialog.Show(settings);
        }));
        buttons.Children.Add(ActionButton("打开工作区", async () =>
        {
            if (agent == null || settings == null) return;
            var current = agent; var selected = await SelectAgentFolder(settings, "选择已有工作区", current.Workspaces.Current);
            if (selected != null) await Run(() => current.OpenWorkspace(selected));
        }));
        buttons.Children.Add(ActionButton("查看文件夹", () =>
        {
            try { if (agent != null) { agent.Workspaces.EnsureCurrent(); OpenUrl(agent.Workspaces.Current); } }
            catch (Exception error) { AgentError("无法打开文件夹", error); }
        }));
        buttons.Children.Add(ActionButton("清空工作区", async () =>
        {
            if (agent == null) return; var current = agent; string expected = current.Workspaces.Current;
            await Run(async () =>
            {
                current.Workspaces.ValidateClear(expected);
                if (await Confirm("清空工作区", $"将删除以下工作区内的全部文件和子文件夹：\n\n{expected}\n\n清空后重新开始鱼鱼对话，并重新准备默认操作技能与人设。", "清空")) await current.ClearWorkspace(expected);
            });
        }));
        return () => { path.Text = agent?.Workspaces.Current ?? "连接后准备默认工作区。"; buttons.IsEnabled = agent != null && !operating && !agent.ChangingWorkspace; };
    }

    Action BuildAgentConfigurationSettings(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "模型与权限", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        var rows = new StackPanel { Spacing = 8 }; panel.Children.Add(rows);
        panel.Children.Add(Paragraph("选项及作用范围由当前 Harness 提供；权限／工作模式决定它如何执行任务与请求确认。"));
        AgentCoordinator? rendered = null; int revision = -1; bool applying = false; Action? refresh = null;
        async Task Apply(AgentCoordinator current, string id, string value)
        {
            if (applying) return; applying = true; rows.IsEnabled = false;
            try { await current.SetConfiguration(id, value); }
            catch (OperationCanceledException) { }
            catch (Exception error) { AgentError("Agent 配置未更改", error); }
            finally { applying = false; revision = -1; refresh?.Invoke(); }
        }
        refresh = () =>
        {
            var current = agent;
            rows.IsEnabled = !applying && current?.Connected == true && !current.Busy && !current.ChangingWorkspace;
            if (applying || ReferenceEquals(rendered, current) && revision == (current?.Configuration.Revision ?? 0)) return;
            rendered = current; revision = current?.Configuration.Revision ?? 0; rows.Children.Clear();
            var options = current?.Configuration.Options ?? [];
            if (options.Length == 0)
            {
                rows.Children.Add(Paragraph(current?.Connected == true ? "模型、权限和工作模式由 Harness 配置；此连接尚未提供可调整的选项。" : "连接后显示 Agent 提供的模型、权限和思考选项。"));
                return;
            }
            if (!options.Any(o => o.Category == "model")) rows.Children.Add(Paragraph("当前模型：" + current!.CurrentModel));
            foreach (var option in options)
            {
                string label = option.Category switch { "model" => "模型 · ", "mode" => "权限／工作模式 · ", "thought_level" => "思考深度 · ", _ => "" };
                rows.Children.Add(Paragraph(label + option.Name));
                if (option.Description.Length > 0) rows.Children.Add(Paragraph(option.Description));
                if (option.Method.Length == 0) { rows.Children.Add(Paragraph(option.CurrentValue + " · 由 Harness 配置")); continue; }
                if (option.Type == "boolean")
                {
                    var toggle = new CheckBox { Content = option.Name, IsChecked = option.CurrentValue == "true" };
                    toggle.Click += async (_, _) => await Apply(current!, option.Id, toggle.IsChecked == true ? "true" : "false");
                    rows.Children.Add(toggle);
                }
                else
                {
                    var selector = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                    var choices = option.Choices.Select(c =>
                    {
                        var item = new ComboBoxItem { Content = (c.Group.Length > 0 ? c.Group + " · " : "") + c.Name, Tag = c.Value };
                        ToolTip.SetTip(item, c.Description); return item;
                    }).ToList();
                    var selected = choices.FirstOrDefault(i => (string?)i.Tag == option.CurrentValue);
                    if (selected == null) { selected = new ComboBoxItem { Content = option.CurrentValue.Length > 0 ? option.CurrentValue : "由 Harness 配置", Tag = option.CurrentValue, IsEnabled = false }; choices.Add(selected); }
                    selector.ItemsSource = choices; selector.SelectedItem = selected;
                    selector.SelectionChanged += async (_, _) => { if (selector.SelectedItem is ComboBoxItem { Tag: string value } && value != option.CurrentValue) await Apply(current!, option.Id, value); };
                    rows.Children.Add(selector);
                }
            }
        };
        return refresh;
    }

    void BuildMemoryReminderSettings(StackPanel body)
    {
        body.Children.Add(new TextBlock { Text = "记忆与提醒", FontSize = 21, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 18, 0, 4) });
        body.Children.Add(Paragraph("Agent 可以通过 memory_* 工具保存长期记忆。每轮对话会把有上限的记忆快照附加给 Agent；这里可以查看和删除。提醒通过 reminder_* 工具创建，桌宠运行到点后显示气泡，也可以让 Agent 组织语言。数据保存在 " + AgentRoot + "。"));
        var memoryRows = new StackPanel { Spacing = 8 };
        var reminderRows = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "长期记忆", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        body.Children.Add(memoryRows);
        body.Children.Add(new TextBlock { Text = "提醒", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
        body.Children.Add(reminderRows);
        var status = Paragraph("");
        void RefreshRows()
        {
            memoryRows.Children.Clear(); reminderRows.Children.Clear();
            foreach (var memory in Memories.List())
            {
                var remove = ActionButton("删除", () => { Memories.Forget(memory.Key); RefreshRows(); status.Text = "已删除记忆：" + memory.Key; });
                memoryRows.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 237)), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
                    Child = new StackPanel { Spacing = 4, Children = {
                        new TextBlock { Text = memory.Key, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = memory.Text, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = "更新于 " + memory.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), Foreground = Brushes.SlateGray, FontSize = 12 },
                        remove } }
                });
            }
            if (memoryRows.Children.Count == 0) memoryRows.Children.Add(Paragraph("还没有长期记忆。让 Agent 记住一条偏好即可。"));
            foreach (var reminder in Reminders.List())
            {
                string schedule = reminder.DailyTime != null
                    ? $"每日 {reminder.DailyTime} · {reminder.TimeZone}"
                    : reminder.At?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "";
                string state = !reminder.Enabled ? "已触发或停用"
                    : "下次 " + (reminder.NextDue?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "等待计算");
                string speech = reminder.AgentSpeech ? "由 Agent 组织语言" : "直接显示提醒文字";
                var cancel = ActionButton("取消提醒", () => { Reminders.Cancel(reminder.Id); RefreshRows(); status.Text = "已取消提醒：" + reminder.Text; });
                reminderRows.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 237)), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
                    Child = new StackPanel { Spacing = 4, Children = {
                        new TextBlock { Text = reminder.Text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold },
                        new TextBlock { Text = schedule + " · " + state + " · " + speech, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, FontSize = 12 },
                        new TextBlock { Text = "编号：" + reminder.Id, Foreground = Brushes.SlateGray, FontSize = 11 },
                        cancel } }
                });
            }
            if (reminderRows.Children.Count == 0) reminderRows.Children.Add(Paragraph("还没有提醒。让 Agent 创建一次性或每日提醒即可。"));
        }
        body.Children.Add(ActionButton("刷新记忆与提醒", () => { status.Text = ""; RefreshRows(); }));
        body.Children.Add(status);
        RefreshRows();
    }
}
