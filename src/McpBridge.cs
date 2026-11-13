using System.IO.Pipes;

namespace BigBlueFish;

// ACP launches this executable with --mcp-bridge --pipe <random>. The bridge only
// relays MCP JSON-RPC between stdio and the pet-owned user-only local socket.
internal static class McpBridge
{
    internal static async Task<int> RunAsync(string pipeName)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000);
            using var cancel = new CancellationTokenSource();
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var toPet = input.CopyToAsync(pipe, cancel.Token);
            var toAgent = pipe.CopyToAsync(output, cancel.Token);
            await Task.WhenAny(toPet, toAgent);
            cancel.Cancel();
            try { await Task.WhenAll(toPet, toAgent); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            return 0;
        }
        catch (Exception error) when (error is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("蓝色大肥鱼内部工具未连接：" + error.Message);
            return 1;
        }
    }
}
