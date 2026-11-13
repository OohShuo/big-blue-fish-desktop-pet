using System.Diagnostics;

namespace BigBlueFish.Agent;

public sealed class AgentAdapterStore(string root)
{
    public string? FindExecutable(HarnessDefinition harness) => harness.FindExecutable();

    public async Task<(string Path, string[] Arguments)> Resolve(HarnessDefinition harness, string executable, CancellationToken token, Action<string> status)
    {
        if (harness.AdapterPackage == null) return (executable, harness.Arguments);
        string? existing = HarnessCatalog.FindExecutable(harness.AdapterCommand!);
        if (existing != null) return (existing, []);
        var npm = HarnessCatalog.FindExecutable("npm") ?? throw new IOException("此 Harness 的 ACP 适配器需要 Node.js / npm，请先安装 Node.js。");
        string folder = Path.Combine(root, "adapters", harness.Id);
        string command = Path.Combine(folder, "node_modules", ".bin", harness.AdapterCommand! + (OperatingSystem.IsWindows() ? ".cmd" : ""));
        string receipt = Path.Combine(folder, "fish-adapter.json");
        if (File.Exists(command) && File.Exists(receipt) && File.ReadAllText(receipt) == harness.AdapterPackage) return (command, []);
        Directory.CreateDirectory(folder);
        status("正在安装 " + harness.Name + " 的 ACP 适配器…");
        var start = AgentProcess.StartInfo(npm, ["install", "--prefix", folder, "--no-audit", "--no-fund", "--save-exact", "--registry", "https://registry.npmjs.org", "--cache", Path.Combine(root, "adapter-cache"), harness.AdapterPackage], folder);
        using var process = Process.Start(start) ?? throw new IOException("无法启动适配器安装。");
        using var ownership = new ProcessOwnership(process);
        var stdout = process.StandardOutput.ReadToEndAsync(token); var stderr = process.StandardError.ReadToEndAsync(token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { try { process.Kill(true); } catch (Exception) { } throw; }
        await stdout; string error = await stderr;
        if (process.ExitCode != 0 || !File.Exists(command)) throw new IOException("ACP 适配器安装失败。请检查 npm 网络连接。" + error[^Math.Min(500, error.Length)..]);
        AtomicText.Write(receipt, harness.AdapterPackage);
        return (command, []);
    }
}
