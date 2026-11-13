using Avalonia.Controls;

namespace BigBlueFish;

internal sealed partial class LinuxPetWindow
{
    TrayIcon? tray;
    void RestorePet() { hidden = false; Show(); player.Paused = false; Clamp(); ResumeAutomaticIfIdle(); }
    void InitializeTray()
    {
        var menu = new NativeMenu();
        void Item(string title, Action action) { var item = new NativeMenuItem(title); item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("显示桌宠", RestorePet); Item("聊天", () => TryOpenMcpInput()); Item("吐字测试", () => _ = ManualSpeech()); Item("设置", OpenSettings); Item("关闭桌宠", Close);
        tray = new TrayIcon { Icon = new WindowIcon(Path.Combine(AppContext.BaseDirectory, "assets/bigbluefish.ico")), ToolTipText = "蓝色大肥鱼", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => RestorePet();
        TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { tray });
        Closed += (_, _) => tray.Dispose();
    }
}
