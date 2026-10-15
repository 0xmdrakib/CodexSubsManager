using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace CodexAccounts.Core;

public enum Activity { Idle, Busy, Unknown }
public interface IDesktopRuntime
{
    bool Running { get; }
    Activity Activity(string home);
    Task Stop(CancellationToken ct);
    Task Launch(CancellationToken ct);
}

public static class CodexLocator
{
    public static string? Cli(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "OpenAI", "Codex", "bin");
        if (Directory.Exists(root))
        {
            var bundled = Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (bundled is not null) return bundled;
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var p = Path.Combine(directory.Trim('"'), "codex.exe"); if (File.Exists(p)) return p; }
            catch (ArgumentException) { }
        }
        return null;
    }
    public static string? Desktop(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        foreach (var p in Process.GetProcessesByName("Codex"))
        {
            using (p) try { if (p.MainWindowHandle != IntPtr.Zero && p.MainModule?.FileName is { } path && IsOfficialDesktopPath(path)) return path; }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "OpenAI", "Codex");
        if (File.Exists(Path.Combine(root, "Codex.exe"))) return Path.Combine(root, "Codex.exe");
        if (Directory.Exists(root))
        {
            var path = Directory.EnumerateDirectories(root, "app-*").OrderDescending()
                .Select(d => Path.Combine(d, "Codex.exe")).FirstOrDefault(File.Exists);
            if (path is not null) return path;
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\Codex.exe");
        return key?.GetValue(null) is string exe && File.Exists(exe) ? exe : null;
    }
    public static bool IsOfficialDesktopPath(string path)
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex") + Path.DirectorySeparatorChar;
        return (path.StartsWith(local, StringComparison.OrdinalIgnoreCase) && !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase)) ||
            path.Contains("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase);
    }
    public static async Task<string?> StoreDesktop(CancellationToken ct)
    {
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Get-AppxPackage -Name '*Codex*' | Select-Object -First 1 -ExpandProperty InstallLocation" }) info.ArgumentList.Add(arg);
        using var p = Process.Start(info);
        if (p is null) return null;
        try
        {
            var output = await p.StandardOutput.ReadToEndAsync(ct);
            await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var root = output.Trim();
            if (!Directory.Exists(root)) return null;
            return Directory.EnumerateFiles(root, "Codex.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        finally { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } }
    }
}

public sealed class DesktopRuntime(Preferences preferences) : IDesktopRuntime, ICodexProcessSession
{
    string? resolved;
    HashSet<int> roots = [];
    public bool ObservedDesktop { get; private set; }
    public bool Running
    {
        get { var found = FindRoots(); try { return found.Count > 0; } finally { foreach (var p in found) p.Dispose(); } }
    }
    List<Process> FindRoots()
    {
        var list = new List<Process>();
        var configured = CodexLocator.Desktop(preferences.DesktopPath) ?? resolved;
        foreach (var p in Process.GetProcessesByName("Codex"))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path is not null && ((p.MainWindowHandle != IntPtr.Zero && CodexLocator.IsOfficialDesktopPath(path)) ||
                    (configured is not null && string.Equals(path, configured, StringComparison.OrdinalIgnoreCase))))
                { list.Add(p); resolved = path; continue; }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            p.Dispose();
        }
        return list;
    }
    public Activity Activity(string home)
    {
        var desktops = FindRoots();
        try
        {
            if (desktops.Count == 0) return Core.Activity.Idle;
            var since = desktops.Select(p => { try { return p.StartTime.ToUniversalTime(); } catch { return DateTime.MinValue; } }).Min();
            return ActivityFiles.Read(home, since);
        }
        finally { foreach (var p in desktops) p.Dispose(); }
    }
    public async Task Stop(CancellationToken ct)
    {
        var processes = FindRoots();
        try { roots = processes.Select(p => p.Id).ToHashSet(); ObservedDesktop = roots.Count > 0; }
        finally { foreach (var p in processes) p.Dispose(); }
        var map = ProcessTree.Parents();
        var ours = ProcessTree.Descendants(Environment.ProcessId, map);
        var desktopChildren = roots.SelectMany(id => ProcessTree.Descendants(id, map)).ToHashSet();
        foreach (var p in Process.GetProcessesByName("codex"))
        {
            using (p)
                if (!roots.Contains(p.Id) && !desktopChildren.Contains(p.Id) && !ours.Contains(p.Id))
                    throw new IOException("A separate Codex terminal or editor session is open. Close it before switching this shared sign-in.");
        }
        if (roots.Count == 0) return;
        roots.UnionWith(desktopChildren);
        await new CodexShutdownCoordinator().CloseAsync(this, TimeSpan.FromSeconds(12), false, () => { }, () => { }, ct);
    }
    public IReadOnlyList<int> FindRunning()
    {
        var found = new HashSet<int>();
        foreach (var id in roots)
            try { using var p = Process.GetProcessById(id); if (!p.HasExited) found.Add(id); } catch (ArgumentException) { }
        var processes = FindRoots();
        try { foreach (var p in processes) { found.Add(p.Id); roots.Add(p.Id); } }
        finally { foreach (var p in processes) p.Dispose(); }
        return found.ToArray();
    }
    public void RequestClose(int id)
    { try { using var p = Process.GetProcessById(id); p.CloseMainWindow(); } catch (ArgumentException) { } }
    public void Terminate(int id) => throw new InvalidOperationException("Automatic force-closing is disabled.");
    public async Task Launch(CancellationToken ct)
    {
        var exe = CodexLocator.Desktop(preferences.DesktopPath) ?? resolved ?? await CodexLocator.StoreDesktop(ct);
        if (exe is null) throw new FileNotFoundException("Codex Desktop was not found. Select its executable in Advanced settings.");
        resolved = exe;
        using var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        if (p is null) throw new IOException("Codex Desktop did not start.");
        await Task.Delay(1200, ct);
        if (p.HasExited && !Running) throw new IOException("Codex Desktop exited during launch.");
    }
}

public static class ActivityFiles
{
    public static Activity Read(string home, DateTime since)
    {
        var sessions = Path.Combine(home, "sessions");
        if (!Directory.Exists(sessions)) return Activity.Unknown;
        bool observed = false;
        try
        {
            var files = Directory.EnumerateFiles(sessions, "rollout-*.jsonl", SearchOption.AllDirectories)
                .Where(f => File.GetLastWriteTimeUtc(f) >= since.AddMinutes(-1)).Take(1001).ToArray();
            if (files.Length > 1000) return Activity.Unknown;
            foreach (var file in files)
            {
                string? last = null;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bool partial = stream.Length > 256 * 1024;
                stream.Seek(Math.Max(0, stream.Length - 256 * 1024), SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                if (partial) reader.ReadLine();
                while (reader.ReadLine() is { } line)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var r = doc.RootElement;
                        if (JsonText.Get(r, "type") != "event_msg" || !r.TryGetProperty("payload", out var p)) continue;
                        var type = JsonText.Get(p, "type");
                        if (type is "task_started" or "task_complete" or "turn_aborted") last = type;
                    }
                    catch (JsonException) { }
                }
                if (last == "task_started") return Activity.Busy;
                observed |= last is not null;
            }
            return observed ? Activity.Idle : Activity.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Activity.Unknown; }
    }
}

static class ProcessTree
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Entry
    {
        public uint Size, Usage, Id; public IntPtr Heap; public uint Module, Threads, Parent;
        public int Priority; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] static extern bool First(IntPtr h, ref Entry e);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] static extern bool Next(IntPtr h, ref Entry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    public static Dictionary<int, int> Parents()
    {
        var result = new Dictionary<int, int>(); var h = CreateToolhelp32Snapshot(2, 0);
        if (h == new IntPtr(-1)) throw new IOException("Windows process activity could not be checked.");
        try { var e = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Name = "" }; if (First(h, ref e)) do { result[(int)e.Id] = (int)e.Parent; } while (Next(h, ref e)); }
        finally { CloseHandle(h); }
        return result;
    }
    public static HashSet<int> Descendants(int id, Dictionary<int, int> map)
    {
        var found = new HashSet<int>(); var queue = new Queue<int>(); queue.Enqueue(id);
        while (queue.TryDequeue(out var current))
            foreach (var child in map.Where(x => x.Value == current).Select(x => x.Key)) if (found.Add(child)) queue.Enqueue(child);
        return found;
    }
}
