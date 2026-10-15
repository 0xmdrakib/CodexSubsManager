using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexAccounts.Core;

public sealed class CodexRpc : IAsyncDisposable
{
    readonly Process process;
    readonly OwnedProcessJob job;
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim writer = new(1, 1);
    readonly Task reader, errors;
    readonly TaskCompletionSource<bool> login = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int serial;
    CodexRpc(Process process)
    {
        this.process = process;
        try { job = new OwnedProcessJob(process); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); throw; }
        reader = ReadLoop();
        errors = DrainErrors();
    }
    public static async Task<CodexRpc> Start(string cli, string home, bool isolated, CancellationToken ct)
    {
        var info = new ProcessStartInfo(cli)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = home
        };
        info.ArgumentList.Add("app-server");
        if (isolated) { info.ArgumentList.Add("-c"); info.ArgumentList.Add("cli_auth_credentials_store=\"file\""); }
        info.Environment["CODEX_HOME"] = home;
        foreach (var key in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "CODEX_CLI_PATH" }) info.Environment.Remove(key);
        var p = Process.Start(info) ?? throw new IOException("Codex could not start.");
        var rpc = new CodexRpc(p);
        try
        {
            await rpc.Call("initialize", new { clientInfo = new { name = "codex-accounts", version = "1.0.0" } }, ct);
            await rpc.Send(new { method = "initialized" }, ct);
            return rpc;
        }
        catch { await rpc.DisposeAsync(); throw; }
    }
    public async Task<JsonElement> Call(string method, object? parameters, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref serial);
        var promise = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = promise;
        try
        {
            await Send(new { id, method, @params = parameters ?? new { } }, ct);
            return await promise.Task.WaitAsync(ct);
        }
        finally { pending.TryRemove(id, out _); }
    }
    public Task WaitForLogin(CancellationToken ct) => login.Task.WaitAsync(ct);
    async Task Send(object message, CancellationToken ct)
    {
        await writer.WaitAsync(ct);
        try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct); await process.StandardInput.FlushAsync(ct); }
        finally { writer.Release(); }
    }
    async Task ReadLoop()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                if (line.Length > 2 * 1024 * 1024) throw new IOException("Unexpected Codex response size.");
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("id", out var id) && id.TryGetInt32(out var n) && pending.TryRemove(n, out var promise))
                {
                    if (root.TryGetProperty("result", out var result)) promise.TrySetResult(result.Clone());
                    else promise.TrySetException(new IOException("Codex could not complete the request. Check your sign-in and connection."));
                }
                else if (JsonText.Get(root, "method") == "account/login/completed" && root.TryGetProperty("params", out var p))
                {
                    if (p.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True) login.TrySetResult(true);
                    else login.TrySetException(new IOException("Sign-in was not completed. Your other accounts are unchanged."));
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or JsonException) { }
        finally
        {
            foreach (var promise in pending.Values) promise.TrySetException(new IOException("Codex disconnected."));
            login.TrySetException(new IOException("The sign-in window disconnected."));
        }
    }
    async Task DrainErrors()
    {
        try { while (await process.StandardError.ReadLineAsync(lifetime.Token) is not null) { } }
        catch (Exception e) when (e is OperationCanceledException or IOException) { }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
        try
        {
            using var grace = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            await process.WaitForExitAsync(grace.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync();
        }
        await Task.WhenAll(reader, errors);
        job.Dispose();
        process.Dispose(); lifetime.Dispose(); writer.Dispose();
    }
}

public static class BrowserLogin
{
    public static void Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            (uri.Host != "auth.openai.com" && uri.Host != "chatgpt.com" && !uri.Host.EndsWith(".openai.com", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Codex returned an unexpected sign-in address.");
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
