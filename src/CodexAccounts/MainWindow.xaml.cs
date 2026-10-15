using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexAccounts.Core;
using Forms = System.Windows.Forms;

namespace CodexAccounts;

public partial class MainWindow : Window
{
    readonly AccountManager manager;
    readonly bool preview, smoke;
    readonly string? capture, settingsCapture;
    readonly DispatcherTimer timer = new();
    Forms.NotifyIcon? tray;
    System.Drawing.Icon? trayIcon;
    CancellationTokenSource? operation;
    bool busy, quitting, hotkey;
    bool initializing;
    bool initializationStarted;
    int dialogDepth;
    string? pendingCommand;
    bool InteractionBlocked => busy || manager.Busy || initializing || dialogDepth > 0;
    IntPtr hwnd;
    string? lastNotice;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [StructLayout(LayoutKind.Sequential)] struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    public MainWindow(AccountManager manager, bool preview, string? capture, bool smoke, string? settingsCapture)
    {
        InitializeComponent(); this.manager = manager; this.preview = preview; this.capture = capture; this.smoke = smoke; this.settingsCapture = settingsCapture;
        initializing = !preview;
        var work = SystemParameters.WorkArea;
        MaxHeight = Math.Max(440, work.Height - 24); MinHeight = Math.Min(610, MaxHeight); Height = Math.Min(820, MaxHeight);
        MaxWidth = Math.Max(360, work.Width - 24); MinWidth = Math.Min(480, MaxWidth); Width = Math.Min(560, MaxWidth);
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            if (!preview) UpdateHotkey();
        };
        Loaded += OnLoaded;
        timer.Tick += async (_, _) => await Poll();
        if (preview && !Environment.GetCommandLineArgs().Contains("--empty")) AddSamples(); else if (!preview) SetupTray();
        RenderAccounts();
    }
    async void OnLoaded(object sender, RoutedEventArgs e) => await InitializeAsync();
    public Task InitializeHiddenAsync() => InitializeAsync();
    async Task InitializeAsync()
    {
        if (initializationStarted) return;
        initializationStarted = true;
        if (preview)
        {
            if (smoke)
            {
                Search.Text = "Studio";
                if (AccountsList.Items.Count != 1) throw new InvalidOperationException("Account search smoke check failed.");
                Search.Text = "";
                VerifyQuotaCards();
                if (AccountsList.Items.Count != 3) throw new InvalidOperationException("Account list smoke check failed.");
                Search.Text = "no-matching-account";
                if (AccountsList.Items.Count != 0 || EmptyTitle.Text != "No matching accounts") throw new InvalidOperationException("Empty search check failed.");
                Search.Text = "";
            }
            await Task.Delay(200);
            if (smoke)
            {
                UpdateLayout();
                var limits = VisualDescendants(this).OfType<System.Windows.Controls.ItemsControl>()
                    .Where(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Reported usage limits").ToArray();
                if (!limits.Select(c => c.Items.Count).SequenceEqual(new[] { 1, 2, 2 }) ||
                    limits.Sum(c => VisualDescendants(c).OfType<System.Windows.Controls.Border>().Count(b => b.Height == 4)) != 5)
                    throw new InvalidOperationException("Rendered quota bar binding check failed.");
                var nameBottom = CurrentName.TranslatePoint(new Point(0, CurrentName.ActualHeight), this).Y;
                var detailTop = CurrentDetail.TranslatePoint(new Point(0, 0), this).Y;
                if (Math.Abs(detailTop - nameBottom) > 1) throw new InvalidOperationException("Active-account text spacing check failed.");
            }
            if (capture is not null) Capture(this, capture);
            if (settingsCapture is not null)
            {
                var settings = new SettingsWindow(manager, true) { Owner = this };
                settings.Show(); await Task.Delay(150); if (smoke) settings.VerifyInteractions(); Capture(settings, settingsCapture); settings.Close();
            }
            var args = Environment.GetCommandLineArgs(); var removalIndex = Array.IndexOf(args, "--capture-removal");
            var quotaIndex = Array.IndexOf(args, "--capture-quotas");
            if (quotaIndex >= 0 && quotaIndex + 1 < args.Length)
            {
                Search.Text = "Studio"; UpdateLayout(); Capture(this, args[quotaIndex + 1]); Search.Text = "";
            }
            if (removalIndex >= 0 && removalIndex + 1 < args.Length)
            {
                var removal = new RemovalDialog(true) { Owner = this };
                removal.Show(); await Task.Delay(150); if (smoke) removal.VerifyInteractions(); Capture(removal, args[removalIndex + 1]); removal.Close();
            }
            var accountIndex = Array.IndexOf(args, "--capture-account");
            if (accountIndex >= 0 && accountIndex + 1 < args.Length && manager.State.Accounts.Count > 1)
            {
                var account = new AccountWindow(manager, manager.State.Accounts[1], true) { Owner = this };
                account.Show(); await Task.Delay(150); Capture(account, args[accountIndex + 1]); account.Close();
            }
            var aboutIndex = Array.IndexOf(args, "--capture-about");
            if (aboutIndex >= 0 && aboutIndex + 1 < args.Length)
            {
                EmbeddedDocuments.Verify(); var about = new AboutWindow { Owner = this };
                about.Show(); await Task.Delay(150); Capture(about, args[aboutIndex + 1]); about.Close();
            }
            var iconsIndex = Array.IndexOf(args, "--capture-icons");
            if (iconsIndex >= 0 && iconsIndex + 1 < args.Length) IconCatalog.Capture(args[iconsIndex + 1]);
            if (capture is not null || smoke) { quitting = true; System.Windows.Application.Current.Shutdown(); }
            return;
        }
        try { await manager.Recover(CancellationToken.None); if (manager.State.Accounts.Count > 0) manager.SyncCurrent(); }
        catch (Exception ex) { SetStatus(ex.Message); }
        finally { initializing = false; }
        RenderAccounts();
        if (manager.RecoveryNotice is { } notice) SetStatus(notice);
        timer.Interval = TimeSpan.FromSeconds(Math.Clamp(manager.State.Preferences.RefreshSeconds, 30, 3600));
        if (Environment.GetCommandLineArgs().Contains("--native-smoke"))
        {
            if (manager.State.Accounts.Count != 0) throw new InvalidOperationException("Native UI diagnostics require an empty account vault.");
            try { await VerifyNativeLifecycle(); }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(manager.Store.Root, "ui-smoke-error.txt"), ex.ToString());
                quitting = true; CleanupDesktopIntegration(); System.Windows.Application.Current.Shutdown(1);
            }
            return;
        }
        try
        {
            if (StartupPolicy.Apply(manager.State)) manager.Save();
            StartupRegistration.Set(manager.State.Preferences.StartWithWindows && manager.State.Attached);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { SetStatus("Windows startup could not be updated. Open Preferences to try again."); }
        var initial = pendingCommand ?? (Environment.GetCommandLineArgs().Contains("--remove") ? "remove" : Environment.GetCommandLineArgs().Contains("--about") ? "about" : null);
        pendingCommand = null;
        if (initial is not null) HandleCommand(initial);
        if (!quitting) { timer.Start(); await Poll(); }
    }
    void AddSamples()
    {
        var now = DateTimeOffset.UtcNow;
        manager.State.Accounts = [
            new() { Id="preview-1", Name="Personal Pro", Email="personal@example.com", Plan="pro", Usage=new() { Captured=now, Windows=[new(){Minutes=10080,Used=18,Resets=now.AddDays(3)}]} },
            new() { Id="preview-2", Name="Studio", Email="studio@example.com", Plan="plus", Usage=new() { Captured=now, Windows=[new(){Minutes=300,Used=39,Resets=now.AddHours(2)},new(){Minutes=10080,Used=25,Resets=now.AddDays(2)}]} },
            new() { Id="preview-3", Name="Backup", Email="backup@example.com", Plan="plus", Usage=new() { Captured=now, Windows=[new(){Minutes=300,Used=100,Resets=now.AddMinutes(46)},new(){Minutes=10080,Used=52,Resets=now.AddDays(4)}]} }
        ];
        manager.State.ActiveId = "preview-1";
        SetStatus("UI preview · sample accounts · no credentials loaded");
    }
    void VerifyQuotaCards()
    {
        var rows = AccountsList.Items.Cast<AccountCard>().ToArray();
        if (!rows.Select(a => a.Quotas.Length).SequenceEqual(new[] { 1, 2, 2 }) ||
            rows[0].Quotas[0].Label != "Weekly remaining" ||
            rows[1].Quotas[0].Label != "5-hour remaining" || rows[1].Quotas[1].Label != "Weekly remaining")
            throw new InvalidOperationException("Reported usage window presentation check failed.");
        var unknown = new AccountCard(new Account(), 1, false, true);
        if (unknown.Quotas.Length != 0 || unknown.EmptyUsageVisibility != Visibility.Visible)
            throw new InvalidOperationException("Unknown usage must not create a limit bar.");
        var extra = new AccountCard(new Account { Plan = "plus", Usage = new() { Windows = [new() { Minutes = 10080, Used = 24 }, new() { Bucket = "code-review", Minutes = 300, Used = 100 }] } }, 1, false, true);
        if (extra.Quotas.Length != 2 || extra.Quotas[0].Label != "Weekly remaining" || !extra.Quotas[1].Label.StartsWith("Code Review · "))
            throw new InvalidOperationException("Additional usage pools must remain visible and distinct.");
    }
    static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in VisualDescendants(child)) yield return descendant;
        }
    }
    public static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path); encoder.Save(file);
    }
    void RenderAccounts()
    {
        if (AccountsList is null) return;
        var state = manager.State;
        var current = state.Accounts.Find(a => a.Id == state.ActiveId);
        CurrentName.Text = current?.Name ?? "Official Codex";
        CurrentDetail.Text = current is null ? "Import your existing sign-in to get started" : current.Email;
        var query = Search.Text.Trim();
        var rows = state.Accounts.Select((a, i) => new AccountCard(a, i + 1, a.Id == state.ActiveId, !InteractionBlocked && state.Attached))
            .Where(a => query.Length == 0 || (a.Name + " " + a.Email + " " + a.Plan).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        AccountsList.ItemsSource = rows;
        EmptyState.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = state.Accounts.Count == 0 ? "Add your first account" : "No matching accounts";
        EmptyDetail.Text = state.Accounts.Count == 0 ? "Import your Codex sign-in or add another account.\nYour workspace and settings stay in place." : "Try another name, email, or subscription plan.";
        ModeLabel.Text = !state.Attached ? "Account management paused" : state.Preferences.AutoSwitch ? "Automatic switching by priority" : "Manual switching";
        ModeButton.Content = state.Attached ? "Preferences" : "Resume";
        AddButton.IsEnabled = ImportButton.IsEnabled = !InteractionBlocked;
        UpdateTray();
    }
    void SetStatus(string text) => StatusText.Text = text;
    async Task Run(Func<CancellationToken, Task> action, string message, bool quiet = false)
    {
        if (InteractionBlocked) return;
        if (preview) { SetStatus("This preview uses sample accounts. Open the normal app to manage your sign-ins."); return; }
        timer.Stop();
        busy = true; operation = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        CancelButton.Visibility = Visibility.Visible; SetStatus(message); RenderAccounts();
        try { await action(operation.Token); SetStatus("Ready. Your Codex configuration stays unchanged."); }
        catch (OperationCanceledException) { SetStatus("Operation cancelled. Your saved accounts are safe."); }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            if (!quiet) MessageBox.Show(this, ex.Message, "Codex Accounts", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            operation.Dispose(); operation = null; busy = false; CancelButton.Visibility = Visibility.Collapsed;
            if (!quitting) { RenderAccounts(); if (!preview && dialogDepth == 0) timer.Start(); }
        }
    }
    async Task Poll()
    {
        if (InteractionBlocked || preview || !manager.State.Attached || manager.State.Accounts.Count == 0) return;
        string? notice = null;
        await Run(async ct =>
        {
            await manager.Refresh(ct, RenderAccounts);
            string? selected;
            try { selected = await manager.AutoSwitch(ct); }
            catch (IOException ex) { notice = ex.Message; return; }
            if (selected is not null) Notify("Account switched", "Codex is now using " + selected + ".");
            else if (manager.State.Preferences.AutoSwitch && manager.State.Accounts.Find(a => a.Id == manager.State.ActiveId) is { } a &&
                     Selection.Exhausted(a.Usage, manager.State.Preferences.ThresholdPercent, DateTimeOffset.UtcNow))
                notice = "No eligible account is ready. Waiting for quota to reset or an account to become available.";
        }, "Refreshing account usage…", true);
        if (notice is not null)
        {
            SetStatus(notice);
            if (lastNotice != notice) { Notify("Account switching is waiting", notice); lastNotice = notice; }
        }
        else lastNotice = null;
    }
    async void ImportClick(object sender, RoutedEventArgs e) => await Run(async ct =>
    {
        var account = manager.ImportCurrent();
        RenderAccounts(); SetStatus("Account saved. Checking usage…");
        await manager.RefreshAccount(account.Id, ct, RenderAccounts);
    }, "Importing your current Codex sign-in…");
    async void AddClick(object sender, RoutedEventArgs e)
    {
        if (InteractionBlocked) return;
        var dialog = new InputDialog("Add account", "Choose a name for this account, then continue to the official Codex sign-in page.", "Account name", false) { Owner = this };
        if (ShowManagedDialog(dialog) != true) return;
        await Run(async ct =>
        {
            var account = await manager.Login(dialog.Value, text => Dispatcher.Invoke(() => SetStatus(text)), ct);
            RenderAccounts(); SetStatus("Account added. Checking usage…");
            await manager.RefreshAccount(account.Id, ct, RenderAccounts);
        }, "Opening secure browser sign-in…");
    }
    async void RefreshClick(object sender, RoutedEventArgs e) => await Run(ct => manager.Refresh(ct, RenderAccounts), "Refreshing quota windows and reset times…");
    async void SwitchClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string id }) await Run(ct => manager.Switch(id, false, ct), "Switching sign-in. Codex will reopen if it is running…");
    }
    async void OpenClick(object sender, RoutedEventArgs e) => await Run(ct => manager.OpenDesktop(ct), "Opening official Codex…");
    void SearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    { if (SearchHint is not null) SearchHint.Visibility = string.IsNullOrEmpty(Search.Text) ? Visibility.Visible : Visibility.Collapsed; if (manager is not null) RenderAccounts(); }
    void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    void HideClick(object sender, RoutedEventArgs e) { if (preview) { quitting = true; System.Windows.Application.Current.Shutdown(); } else Hide(); }
    void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        for (var source = e.OriginalSource as DependencyObject; source is Visual; source = VisualTreeHelper.GetParent(source))
            if (source is System.Windows.Controls.Button) return;
        DragMove();
    }
    void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (InteractionBlocked) return;
        if (!manager.State.Attached && !preview) { manager.State.Attached = true; manager.Save(); }
        var settings = new SettingsWindow(manager, preview, RenderAccounts) { Owner = this };
        ShowManagedDialog(settings);
        Theme.Apply(manager.State.Preferences.LightTheme); UpdateHotkey();
        timer.Interval = TimeSpan.FromSeconds(Math.Clamp(manager.State.Preferences.RefreshSeconds, 30, 3600));
        if (!preview) timer.Start();
        RenderAccounts();
    }
    void AccountDetails(object sender, RoutedEventArgs e)
    {
        if (InteractionBlocked || sender is not System.Windows.Controls.Button { Tag: string id }) return;
        var account = manager.State.Accounts.Find(a => a.Id == id);
        if (account is null) return;
        ShowManagedDialog(new AccountWindow(manager, account, preview) { Owner = this });
    }
    void DetachClick(object sender, RoutedEventArgs e) => ShowRemoval();
    public async void ShowRemoval()
    {
        if (InteractionBlocked || preview) return;
        var dialog = new RemovalDialog(manager.Store.HasSecret("baseline")) { Owner = this };
        if (ShowManagedDialog(dialog) != true) return;
        timer.Stop();
        await Run(async ct =>
        {
            await manager.Detach(dialog.RestoreOriginal, dialog.DeleteVault, ct);
            quitting = true; CleanupDesktopIntegration();
            MessageBox.Show(this, "Codex can now be used normally with one account. You may delete this app folder. Your projects, chat history, and Codex settings were not removed.", "Official Codex is ready", MessageBoxButton.OK, MessageBoxImage.Information);
            System.Windows.Application.Current.Shutdown();
        }, "Returning control to official Codex…");
        if (!quitting) timer.Start();
    }
    void SetupTray()
    {
        trayIcon = TrayArtwork.Create();
        tray = new Forms.NotifyIcon { Text = "Codex Accounts", Icon = trayIcon, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ToggleTrayPopup); };
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowPopup);
        UpdateTray();
    }
    void UpdateTray()
    {
        if (tray is null) return;
        var old = tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        var show = menu.Items.Add("Show accounts", null, (_, _) => Dispatcher.Invoke(ShowPopup));
        var hide = menu.Items.Add("Hide accounts", null, (_, _) => Dispatcher.Invoke(() => HandleCommand("hide")));
        var open = menu.Items.Add("Open Codex", null, (_, _) => Dispatcher.Invoke(() => OpenClick(this, new RoutedEventArgs())));
        var preferences = menu.Items.Add("Preferences", null, (_, _) => Dispatcher.Invoke(() => { ShowPopup(); SettingsClick(this, new RoutedEventArgs()); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var accountItems = new List<Forms.ToolStripItem>();
        foreach (var a in manager.State.Accounts.Take(15))
        {
            var id = a.Id;
            var item = (Forms.ToolStripMenuItem)menu.Items.Add(a.Name);
            item.Checked = a.Id == manager.State.ActiveId;
            item.Enabled = !InteractionBlocked && a.Id != manager.State.ActiveId && manager.State.Attached;
            accountItems.Add(item);
            item.Click += async (_, _) => await Dispatcher.InvokeAsync(async () => await Run(ct => manager.Switch(id, false, ct), "Switching sign-in…"));
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Open app location", null, (_, _) => Dispatcher.Invoke(() => OpenShell("explorer.exe", "/select,\"" + Environment.ProcessPath + "\"")));
        menu.Items.Add("Windows tray settings", null, (_, _) => Dispatcher.Invoke(() => OpenShell("ms-settings:taskbar")));
        var guide = menu.Items.Add("User guide", null, (_, _) => Dispatcher.Invoke(() => ShowDocument(false)));
        var about = menu.Items.Add("About Codex Accounts", null, (_, _) => Dispatcher.Invoke(ShowAbout));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var disconnect = menu.Items.Add("Return to official Codex", null, (_, _) => Dispatcher.Invoke(() => { ShowPopup(); ShowRemoval(); }));
        var quit = menu.Items.Add("Quit manager", null, (_, _) => Dispatcher.Invoke(() => QuitManager()));
        void ApplyAccess()
        {
            show.Enabled = !IsVisible; hide.Enabled = IsVisible && dialogDepth == 0;
            open.Enabled = preferences.Enabled = guide.Enabled = about.Enabled = disconnect.Enabled = quit.Enabled = !InteractionBlocked;
            quit.ToolTipText = InteractionBlocked ? "Finish or cancel the current operation and close open dialogs before quitting." : "Stop the manager. Your current Codex sign-in is kept.";
            foreach (var item in accountItems) item.Enabled = !InteractionBlocked && manager.State.Attached && item is Forms.ToolStripMenuItem { Checked: false };
        }
        menu.Opening += (_, _) => ApplyAccess(); ApplyAccess();
        var active = manager.State.Accounts.Find(a => a.Id == manager.State.ActiveId);
        var tooltip = active is null ? "Codex Accounts · Click to show or hide" : "Codex Accounts · " + active.Name;
        tray.Text = tooltip[..Math.Min(tooltip.Length, 63)];
        tray.ContextMenuStrip = menu; old?.Dispose();
    }
    bool? ShowManagedDialog(Window dialog)
    {
        dialogDepth++; timer.Stop(); UpdateTray();
        try { return dialog.ShowDialog(); }
        finally { dialogDepth--; if (!preview && !quitting && !initializing) timer.Start(); if (!quitting) RenderAccounts(); }
    }
    public void ShowAbout()
    { if (InteractionBlocked) { ShowPopup(); return; } ShowPopup(); ShowManagedDialog(new AboutWindow { Owner = this }); }
    void ShowDocument(bool licenses)
    { if (InteractionBlocked) return; ShowPopup(); ShowManagedDialog(new EmbeddedDocumentWindow(licenses ? "Software licenses" : "User guide", "Included in the standalone executable.", licenses ? EmbeddedDocuments.Licenses : EmbeddedDocuments.Guide) { Owner = this }); }
    void OpenShell(string file, string arguments = "")
    {
        try { using var p = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException) { SetStatus("Windows could not open this location. " + ex.Message); ShowPopup(); }
    }
    public void HandleCommand(string command)
    {
        if (initializing) { pendingCommand = command; return; }
        switch (command)
        {
            case "show": ShowPopup(); break;
            case "hide": if (dialogDepth == 0) Hide(); else ShowPopup(); break;
            case "about": ShowAbout(); break;
            case "remove": ShowPopup(); ShowRemoval(); break;
            case "quit": QuitManager(); break;
        }
    }
    void ToggleTrayPopup() { if (IsVisible && dialogDepth == 0) Hide(); else ShowPopup(); }
    public void ShowPopup()
    {
        Show(); WindowState = WindowState.Normal; PlaceNearTray(); Activate();
        Window front = this;
        while (front.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible) is { } child) front = child;
        if (front != this) front.Activate();
    }
    void PlaceNearTray()
    {
        if (hwnd == IntPtr.Zero) return;
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        for (int pass = 0; pass < 2; pass++)
        {
            if (!GetWindowRect(hwnd, out var rect)) return;
            SetWindowPos(hwnd, IntPtr.Zero, Math.Max(area.Left, area.Right - (rect.Right - rect.Left) - 8), Math.Max(area.Top, area.Bottom - (rect.Bottom - rect.Top) - 8), 0, 0, 0x15);
            var dpi = VisualTreeHelper.GetDpi(this);
            var maxHeight = Math.Max(360, area.Height / dpi.DpiScaleY - 16); var maxWidth = Math.Max(320, area.Width / dpi.DpiScaleX - 16);
            MinHeight = Math.Min(610, maxHeight); MaxHeight = maxHeight; Height = Math.Min(Height, maxHeight);
            MinWidth = Math.Min(480, maxWidth); MaxWidth = maxWidth; Width = Math.Min(Width, maxWidth); UpdateLayout();
        }
    }
    bool QuitManager()
    {
        if (InteractionBlocked) { SetStatus("Finish or cancel the current operation and close open dialogs before quitting."); ShowPopup(); return false; }
        quitting = true; CleanupDesktopIntegration(); System.Windows.Application.Current.Shutdown(); return true;
    }
    void CleanupDesktopIntegration()
    {
        timer.Stop(); operation?.Cancel();
        if (hotkey) { UnregisterHotKey(hwnd, 1); hotkey = false; }
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); tray = null; }
        trayIcon?.Dispose(); trayIcon = null;
    }
    async Task VerifyNativeLifecycle()
    {
        EmbeddedDocuments.Verify();
        if (timer.Interval != TimeSpan.FromSeconds(30)) throw new InvalidOperationException("Default automatic refresh timer check failed.");
        bool visibilityCorrect = Environment.GetCommandLineArgs().Contains("--tray") ? !IsVisible : IsVisible;
        if (!visibilityCorrect) throw new InvalidOperationException("Initial startup visibility check failed.");
        if (tray is null || !tray.Visible || hwnd == IntPtr.Zero || ShowInTaskbar) throw new InvalidOperationException("Native tray setup check failed.");
        var registered = hotkey;
        Close();
        if (IsVisible || tray is null || !tray.Visible || quitting) throw new InvalidOperationException("Close-to-tray check failed.");
        ToggleTrayPopup(); await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ApplicationIdle);
        if (!IsVisible) throw new InvalidOperationException("Tray show check failed.");
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        if (!GetWindowRect(hwnd, out var rect) || rect.Left < area.Left || rect.Top < area.Top || rect.Right > area.Right || rect.Bottom > area.Bottom)
            throw new InvalidOperationException("Tray popup placement check failed.");
        ToggleTrayPopup(); if (IsVisible) throw new InvalidOperationException("Tray hide check failed.");
        await ForwardDiagnostic("--show");
        if (!IsVisible) throw new InvalidOperationException("Single-instance show forwarding failed.");
        await ForwardDiagnostic("--hide");
        if (IsVisible) throw new InvalidOperationException("Single-instance hide forwarding failed.");
        ShowPopup(); busy = true; UpdateTray();
        try
        {
            if (tray!.ContextMenuStrip!.Items.Cast<Forms.ToolStripItem>().Single(i => i.Text == "Quit manager").Enabled || QuitManager() || quitting)
                throw new InvalidOperationException("Busy operation quit protection failed.");
        }
        finally { busy = false; }
        var modal = new InputDialog("Desktop integration check", "Isolated diagnostic data. No accounts are accessed.", "Account name", false) { Owner = this };
        bool modalProtected = false;
        _ = Dispatcher.BeginInvoke(() =>
        {
            modalProtected = dialogDepth > 0 && !tray!.ContextMenuStrip!.Items.Cast<Forms.ToolStripItem>().Single(i => i.Text == "Quit manager").Enabled;
            modal.Close();
        }, DispatcherPriority.ApplicationIdle);
        ShowManagedDialog(modal);
        if (!modalProtected || dialogDepth != 0) throw new InvalidOperationException("Modal dialog tray protection failed.");
        SetStatus(registered ? "Protected by Windows encryption. Stored on this PC." : "Ctrl + Alt + A is used by another app. You can still open Accounts from the tray.");
        if (capture is not null) Capture(this, capture);
        CleanupDesktopIntegration();
        var clean = tray is null && trayIcon is null && !hotkey && !timer.IsEnabled;
        if (!clean) throw new InvalidOperationException("Desktop integration cleanup failed.");
        File.WriteAllText(Path.Combine(manager.Store.Root, "ui-smoke.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            trayCreated = true, hotkeyRegistered = registered, windowCreated = true, initialVisibilityCorrect = visibilityCorrect, closeToTray = true, trayToggle = true,
            popupWithinWorkArea = true, singleInstanceForwarding = true, busyQuitProtected = true, modalQuitProtected = true,
            embeddedHelpAndLicenses = true, desktopResourcesCleaned = clean, refreshIntervalSeconds = timer.Interval.TotalSeconds
        }));
        QuitManager();
    }
    async Task ForwardDiagnostic(string command)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--native-control"); start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(manager.Store.Root); start.ArgumentList.Add(command);
        using var child = Process.Start(start) ?? throw new IOException("Diagnostic launcher did not start.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { child.Kill(); throw new IOException("Diagnostic launcher did not finish."); }
        if (child.ExitCode != 0) throw new IOException("Diagnostic command forwarding failed.");
        await Task.Delay(100); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    void Notify(string title, string message) { tray?.ShowBalloonTip(4000, title, message, Forms.ToolTipIcon.Info); }
    void UpdateHotkey()
    {
        if (preview || hwnd == IntPtr.Zero) return;
        if (hotkey) { UnregisterHotKey(hwnd, 1); hotkey = false; }
        if (manager.State.Preferences.Hotkey)
        {
            hotkey = RegisterHotKey(hwnd, 1, 0x4000 | 0x0001 | 0x0002, 0x41);
            if (!hotkey) SetStatus("Ctrl + Alt + A is used by another app. You can still open Accounts from the tray.");
        }
    }
    IntPtr WindowMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    { if (msg == 0x0312 && wp == new IntPtr(1)) { ShowPopup(); handled = true; } return IntPtr.Zero; }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!quitting && !preview) { e.Cancel = true; Hide(); }
        else CleanupDesktopIntegration();
        base.OnClosing(e);
    }
}

public sealed class AccountCard
{
    public string Id { get; }
    public string Name { get; }
    public string Email { get; }
    public string Initial { get; }
    public string Plan { get; }
    public string Priority { get; }
    public string ActiveLabel { get; }
    public string Action { get; }
    public bool CanSwitch { get; }
    public Brush Border { get; }
    public Brush AvatarColor { get; }
    public QuotaCard[] Quotas { get; }
    public string EmptyUsageText { get; }
    public Visibility EmptyUsageVisibility => Quotas.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Freshness { get; }
    static Brush Color(string value) => new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    public AccountCard(Account a, int priority, bool active, bool enabled)
    {
        Id = a.Id; Name = a.Name; Email = string.IsNullOrEmpty(a.Email) ? "Saved Codex sign-in" : a.Email;
        Initial = string.Concat(a.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(s => s[0])).ToUpperInvariant();
        Plan = a.Plan == "prolite" ? "Pro Lite" : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(a.Plan); Priority = a.Enabled ? $"Priority {priority}" : "Manual only";
        ActiveLabel = active ? "Active" : ""; Action = active ? "Selected" : "Switch account"; CanSwitch = !active && enabled;
        Border = active ? (Brush)System.Windows.Application.Current.FindResource("Accent") : (Brush)System.Windows.Application.Current.FindResource("Stroke");
        AvatarColor = Color(new[] { "#7868B5", "#5C7BA4", "#96729C", "#56858B", "#92785C" }[(priority - 1) % 5]);
        Quotas = a.Usage?.Windows.OrderBy(w => w.Bucket == "codex" ? 0 : 1)
            .ThenBy(w => w.Bucket, StringComparer.Ordinal).ThenBy(w => w.Minutes ?? int.MaxValue)
            .Select(w => new QuotaCard(w)).ToArray() ?? [];
        EmptyUsageText = a.Status is not null ? "Usage unavailable. Automatic refresh will retry."
            : a.Usage is null ? "Usage not checked yet" : "No usage limits reported";
        bool fresh = a.Usage?.Fresh(DateTimeOffset.UtcNow) == true && a.Status is null;
        var age = a.Usage is { } u ? DateTimeOffset.UtcNow - u.Captured : TimeSpan.Zero;
        Freshness = a.Status is not null && a.Usage is not null ? "Cached usage · retrying automatically" : a.Status ?? (fresh ? age.TotalMinutes < 1 ? "Updated just now" : $"Updated {(int)age.TotalMinutes}m ago" : a.Usage is not null ? "Cached · refresh needed" : "Usage pending");
    }
    public static string Relative(DateTimeOffset reset)
    {
        var delta = reset - DateTimeOffset.UtcNow;
        if (delta <= TimeSpan.Zero) return "pending refresh";
        if (delta.TotalDays >= 1) return $"in {(int)delta.TotalDays}d {delta.Hours}h";
        if (delta.TotalHours >= 1) return $"in {(int)delta.TotalHours}h {delta.Minutes}m";
        return $"in {Math.Max(1, (int)delta.TotalMinutes)}m";
    }
}

public sealed class QuotaCard
{
    public string Label { get; }
    public string Text { get; }
    public string Reset { get; }
    public string ResetDate { get; }
    public string AccessibleSummary => $"{Label}, {Text}, {ResetDate}";
    public Brush Color { get; }
    public GridLength ProgressWidth { get; }
    public GridLength TrackWidth { get; }
    public QuotaCard(QuotaWindow window)
    {
        var remaining = double.IsFinite(window.Remaining) ? Math.Clamp(window.Remaining, 0, 100) : 0;
        var label = window.Minutes == 300 ? "5-hour" : window.Label;
        var bucket = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(window.Bucket.Replace('-', ' ').Replace('_', ' '));
        Label = (window.Bucket == "codex" ? "" : bucket + " · ") + label + " remaining";
        Text = $"{remaining:0.#}% available";
        Color = (Brush)System.Windows.Application.Current.FindResource(remaining <= 10 ? "Danger" : remaining <= 30 ? "Warning" : "Accent");
        ProgressWidth = new GridLength(remaining, GridUnitType.Star);
        TrackWidth = new GridLength(100 - remaining, GridUnitType.Star);
        Reset = window.Resets is { } reset ? "Resets " + AccountCard.Relative(reset) : "Reset time unavailable";
        ResetDate = window.Resets is { } date ? "Resets " + date.ToLocalTime().ToString("ddd, dd MMM · h:mm tt") : Reset;
    }
}

static class TrayArtwork
{
    public static System.Drawing.Icon Create()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/App.ico")).Stream;
        using var icon = new System.Drawing.Icon(stream, 32, 32);
        return (System.Drawing.Icon)icon.Clone();
    }
}
