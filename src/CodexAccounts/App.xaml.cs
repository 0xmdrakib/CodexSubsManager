using System.Security.Cryptography;
using System.Text;
using System.IO.Pipes;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using CodexAccounts.Core;

namespace CodexAccounts;

public partial class App : System.Windows.Application
{
    Mutex? instance;
    readonly CancellationTokenSource pipeLifetime = new();
    public static bool Preview { get; private set; }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Codex Accounts", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        try
        {
            if (e.Args.Contains("--register-startup")) { StartupRegistration.Set(true); Shutdown(); return; }
            Preview = e.Args.Contains("--preview") || e.Args.Contains("--smoke");
            string? Value(string flag) { var i = Array.IndexOf(e.Args, flag); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
            var diagnostic = e.Args.Contains("--native-smoke") || e.Args.Contains("--native-control");
            if (diagnostic && Value("--data-dir") is null)
                throw new InvalidOperationException("Native UI diagnostics require an isolated data directory.");
            var root = Value("--data-dir") ?? (Preview ? Path.Combine(AppContext.BaseDirectory, "preview-data") :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexAccounts"));
            if (!Preview)
            {
                var id = "CodexAccounts." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
                if (diagnostic) id += ".Diagnostic." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..16];
                var name = "Local\\" + id;
                instance = new Mutex(true, name, out var created);
                if (!created)
                {
                    try
                    {
                        using var client = new NamedPipeClientStream(".", id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                        client.Connect(1500); using var writer = new StreamWriter(client);
                        writer.WriteLine(e.Args.Contains("--remove") ? "remove" : e.Args.Contains("--quit") ? "quit" : e.Args.Contains("--hide") ? "hide" : e.Args.Contains("--about") ? "about" : "show"); writer.Flush();
                        if (!diagnostic && OlderVersionIsRunning())
                            MessageBox.Show("An older version of Codex Accounts is still running. Choose Quit manager from its system tray menu, then open this EXE again. Your saved accounts and Codex settings are kept.", "Codex Accounts " + EmbeddedDocuments.Version + " is ready", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex) when (ex is IOException or TimeoutException)
                    {
                        if (diagnostic) { Shutdown(2); return; }
                        MessageBox.Show("Codex Accounts is already running. Open its icon from the Windows tray overflow arrow, or press Ctrl + Alt + A.", "Codex Accounts");
                    }
                    Shutdown(); return;
                }
                if (e.Args.Contains("--native-control")) { Shutdown(2); return; }
                if (e.Args.Contains("--quit") || e.Args.Contains("--hide")) { Shutdown(); return; }
                _ = Listen(id);
            }
            var manager = new AccountManager(new LocalStore(root));
            if (Preview && e.Args.Contains("--light")) manager.State.Preferences.LightTheme = true;
            if (Preview && e.Args.Contains("--dark")) manager.State.Preferences.LightTheme = false;
            Theme.Apply(manager.State.Preferences.LightTheme);
            var window = new MainWindow(manager, Preview, Value("--capture"), e.Args.Contains("--smoke"), Value("--capture-settings"));
            MainWindow = window;
            var hidden = e.Args.Contains("--tray") && !Preview;
            if (hidden)
            {
                window.ShowActivated = false;
                new WindowInteropHelper(window).EnsureHandle();
                StartHiddenWindow(window);
            }
            else window.Show();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Codex Accounts could not start", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    static async void StartHiddenWindow(MainWindow window)
    {
        try { await window.InitializeHiddenAsync(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Codex Accounts could not start", MessageBoxButton.OK, MessageBoxImage.Error); System.Windows.Application.Current.Shutdown(1); }
    }
    static bool OlderVersionIsRunning()
    {
        var current = typeof(App).Assembly.GetName().Version;
        foreach (var process in Process.GetProcessesByName("CodexAccounts"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try
                {
                    if (process.MainModule?.FileName is { } path && Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var version) && version < current) return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { }
            }
        }
        return false;
    }
    protected override void OnExit(ExitEventArgs e)
    {
        pipeLifetime.Cancel();
        if (instance is not null) { try { instance.ReleaseMutex(); } catch (ApplicationException) { } instance.Dispose(); }
        base.OnExit(e);
    }
    async Task Listen(string name)
    {
        try
        {
            while (!pipeLifetime.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(pipeLifetime.Token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(pipeLifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(2));
                    using var reader = new StreamReader(server); var buffer = new char[32]; var length = 0;
                    while (length < buffer.Length)
                    {
                        var read = await reader.ReadAsync(buffer.AsMemory(length, 1), timeout.Token);
                        if (read == 0 || buffer[length] == '\n') break;
                        length++;
                    }
                    var command = new string(buffer, 0, length).Trim();
                    if (command is "show" or "hide" or "about" or "remove" or "quit")
                        if (!Dispatcher.HasShutdownStarted) _ = Dispatcher.BeginInvoke(() => { if (MainWindow is MainWindow window) window.HandleCommand(command); });
                }
                catch (OperationCanceledException) when (!pipeLifetime.IsCancellationRequested) { }
                catch (IOException) when (!pipeLifetime.IsCancellationRequested) { }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException) { }
    }
}

public static class Theme
{
    public static void Apply(bool light)
    {
        var colors = light ? new Dictionary<string, string>
        {
            ["Canvas"]="#E9ECF3", ["Surface"]="#EDF0F6", ["SurfaceHover"]="#E2E6F0", ["Inset"]="#E2E6EF", ["Stroke"]="#D3D9E7",
            ["Text"]="#24283F", ["Muted"]="#626B82", ["Accent"]="#6656D9", ["AccentText"]="#FFFFFF", ["AccentSurface"]="#E2DFF5", ["Warning"]="#99600E", ["Danger"]="#B04458", ["Highlight"]="#FFFFFF"
        } : new Dictionary<string, string>
        {
            ["Canvas"]="#252939", ["Surface"]="#2C3043", ["SurfaceHover"]="#373C52", ["Inset"]="#202434", ["Stroke"]="#40465D",
            ["Text"]="#F2F1FA", ["Muted"]="#B0B6CF", ["Accent"]="#B4A6FF", ["AccentText"]="#242039", ["AccentSurface"]="#393451", ["Warning"]="#E8BF78", ["Danger"]="#EF9BAE", ["Highlight"]="#43495F"
        };
        foreach (var (name, color) in colors) System.Windows.Application.Current.Resources[name] =
            new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        System.Windows.Application.Current.Resources["ShadowDark"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(light ? "#B8C1D4" : "#151823");
        System.Windows.Application.Current.Resources["ShadowLight"] = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(light ? "#FFFFFF" : "#444A63");
    }
}
