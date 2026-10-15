using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using CodexAccounts.Core;
using Microsoft.Win32;

namespace CodexAccounts;

public class DialogShell : Window
{
    protected StackPanel Body { get; } = new() { Margin = new Thickness(26, 0, 26, 12) };
    protected StackPanel Footer { get; } = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(26, 12, 26, 20) };
    public DialogShell(string title, string description)
    {
        Title = title; Width = Math.Min(520, SystemParameters.WorkArea.Width - 24); MinWidth = Math.Min(450, Width); MaxHeight = Math.Max(440, Math.Min(820, SystemParameters.WorkArea.Height - 24)); SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "Text");
        Icon = new BitmapImage(new Uri("pack://application:,,,/Assets/App.ico"));
        UseLayoutRounding = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel { Margin = new Thickness(26, 16, 26, 12) };
        var caption = new DockPanel { LastChildFill = false };
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            for (var source = e.OriginalSource as DependencyObject; source is Visual; source = VisualTreeHelper.GetParent(source))
                if (source is Button) return;
            DragMove();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        var close = new Button { Content = new Glyph { Kind = "Close", Width = 15, Height = 15 }, Style = (Style)System.Windows.Application.Current.FindResource("Quiet"), Padding = new Thickness(7), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Close" };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close dialog"); close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); caption.Children.Add(close);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/Assets/Brand.png")), Width = 24, Height = 24 });
        brand.Children.Add(Muted("Codex Accounts", new Thickness(8, 2, 0, 0))); caption.Children.Add(brand); header.Children.Add(caption);
        header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
        header.Children.Add(Muted(description, new Thickness(0, 7, 0, 0)));
        grid.Children.Add(header);
        var scroll = new ScrollViewer { Content = Body, MaxHeight = 560 }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll);
        Grid.SetRow(Footer, 2); grid.Children.Add(Footer);
        var shell = new Border { CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1), Margin = new Thickness(10), Child = grid };
        shell.SetResourceReference(Border.BackgroundProperty, "Canvas"); shell.SetResourceReference(Border.BorderBrushProperty, "Highlight");
        shell.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(21, 25, 47), BlurRadius = 18, ShadowDepth = 3, Opacity = 0.22 };
        Content = shell;
    }
    protected static TextBlock Muted(string text, Thickness? margin = null) => new()
    { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Foreground = (Brush)System.Windows.Application.Current.FindResource("Muted"), Margin = margin ?? new Thickness(0) };
    protected static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 12, 0, 6) };
    protected static TextBox Field(Panel panel, string label, string value, bool readOnly = false)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5), FontSize = 11 });
        var input = new TextBox { Text = value, IsReadOnly = readOnly, MinHeight = 36 };
        System.Windows.Automation.AutomationProperties.SetName(input, label); panel.Children.Add(input); return input;
    }
    protected static CheckBox Check(Panel panel, string label, bool value)
    { var c = new CheckBox { Content = label, IsChecked = value }; panel.Children.Add(c); return c; }
    protected static Button Button(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0), MinWidth = 86, IsCancel = label == "Cancel" };
        if (primary) b.Style = (Style)System.Windows.Application.Current.FindResource("Primary");
        b.Click += (_, _) => action(); return b;
    }
    protected void Error(Exception ex) => MessageBox.Show(this, ex is CryptographicException ? "The password is incorrect, or the encrypted file was changed." : ex.Message, "Codex Accounts", MessageBoxButton.OK, MessageBoxImage.Information);
}

public sealed class InputDialog : DialogShell
{
    readonly TextBox? text;
    readonly PasswordBox? password;
    public string Value => password?.Password ?? text?.Text.Trim() ?? "";
    public InputDialog(string title, string description, string label, bool secret) : base(title, description)
    {
        if (secret)
        {
            Body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });
            password = new PasswordBox(); Body.Children.Add(password);
            Loaded += (_, _) => password.Focus();
        }
        else { text = Field(Body, label, ""); text.MaxLength = 64; Loaded += (_, _) => text.Focus(); }
        Footer.Children.Add(Button("Cancel", () => DialogResult = false));
        var ok = Button("Continue", () => { if (Value.Length == 0) return; DialogResult = true; }, true); ok.IsDefault = true; Footer.Children.Add(ok);
    }
}

public sealed class SettingsWindow : DialogShell
{
    readonly AccountManager manager;
    readonly bool preview;
    readonly CheckBox auto, start, hotkey, light;
    readonly TextBox threshold, refresh, cooldown, cli, desktop;
    readonly Action? accountsUpdated;
    readonly TextBlock importStatus;
    CancellationTokenSource? importRefresh;
    public SettingsWindow(AccountManager manager, bool preview, Action? accountsUpdated = null) : base("Preferences", "Manage switching, appearance, and encrypted backups.")
    {
        this.manager = manager; this.preview = preview; this.accountsUpdated = accountsUpdated;
        var p = manager.State.Preferences;
        Body.Children.Add(Heading("Switching"));
        auto = Check(Body, "Automatically switch by account priority", p.AutoSwitch);
        Body.Children.Add(Muted("Waits until Codex is idle. Close Codex if activity cannot be verified."));
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new());
        var l = new StackPanel { Margin = new Thickness(0, 0, 6, 0) }; var r = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        threshold = Field(l, "Remaining quota threshold (%)", p.ThresholdPercent.ToString()); cooldown = Field(r, "Switching cooldown (seconds)", p.CooldownSeconds.ToString());
        row.Children.Add(l); Grid.SetColumn(r, 1); row.Children.Add(r); Body.Children.Add(row);
        Body.Children.Add(Heading("Desktop"));
        hotkey = Check(Body, "Keyboard shortcut: Ctrl + Alt + A", p.Hotkey);
        start = Check(Body, "Start quietly when I sign in to Windows", p.StartWithWindows);
        light = Check(Body, "Light appearance", p.LightTheme);
        var advanced = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        refresh = Field(advanced, "Usage refresh interval in seconds", p.RefreshSeconds.ToString());
        cli = Field(advanced, "Codex CLI executable", p.CliPath ?? "");
        advanced.Children.Add(Button("Choose CLI executable", () => Browse(cli)));
        desktop = Field(advanced, "Codex desktop executable", p.DesktopPath ?? "");
        advanced.Children.Add(Button("Choose desktop executable", () => Browse(desktop)));
        Field(advanced, "Your existing Codex home", p.CodexHome, true);
        advanced.Children.Add(Muted("Paths are optional. Automatic detection is used when blank. Codex configuration files are not rewritten.", new Thickness(0, 10, 0, 0)));
        Body.Children.Add(new Expander { Header = "Advanced settings", Content = advanced, Margin = new Thickness(0, 16, 0, 8) });
        Body.Children.Add(Heading("Encrypted backup"));
        Body.Children.Add(Muted("Password-protected backups can be restored on another PC."));
        var backups = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 10, 0, 0) };
        backups.Children.Add(Button("Export backup", Export)); backups.Children.Add(Button("Import backup", Import)); Body.Children.Add(backups);
        importStatus = Muted("", new Thickness(0, 10, 0, 0)); importStatus.Visibility = Visibility.Collapsed; Body.Children.Add(importStatus);
        Closing += (_, e) =>
        {
            if (importRefresh is null) return;
            e.Cancel = true; importRefresh.Cancel(); importStatus.Text = "Stopping usage refresh. Imported accounts are saved.";
        };
        Footer.Children.Add(Button("Cancel", () => DialogResult = false)); Footer.Children.Add(Button("Save preferences", Save, true));
    }
    void Browse(TextBox field)
    {
        var dialog = new OpenFileDialog { Filter = "Windows executable|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) field.Text = dialog.FileName;
    }
    internal void VerifyInteractions()
    {
        var before = auto.IsChecked;
        var toggle = (IToggleProvider)new CheckBoxAutomationPeer(auto).GetPattern(PatternInterface.Toggle);
        toggle.Toggle();
        if (auto.IsChecked == before) throw new InvalidOperationException("Checkbox interaction check failed.");
        toggle.Toggle();
        var value = threshold.Text;
        threshold.Focus(); threshold.SelectAll(); threshold.SelectedText = "15";
        if (threshold.Text != "15") throw new InvalidOperationException("Text input interaction check failed.");
        threshold.Text = value; threshold.Select(0, 0);
        var advanced = Body.Children.OfType<Expander>().Single();
        var expand = (IExpandCollapseProvider)new ExpanderAutomationPeer(advanced).GetPattern(PatternInterface.ExpandCollapse);
        expand.Expand(); UpdateLayout();
        if (!cli.IsVisible || !desktop.IsVisible) throw new InvalidOperationException("Advanced settings expansion check failed.");
        expand.Collapse(); UpdateLayout(); Keyboard.ClearFocus();
    }
    void Save()
    {
        try
        {
            if (!int.TryParse(threshold.Text, out var t) || t is < 0 or > 95) throw new IOException("Remaining threshold must be from 0 to 95 percent.");
            if (!int.TryParse(refresh.Text, out var r) || r is < 30 or > 3600) throw new IOException("Refresh interval must be from 30 to 3600 seconds.");
            if (!int.TryParse(cooldown.Text, out var c) || c is < 30 or > 3600) throw new IOException("Cooldown must be from 30 to 3600 seconds.");
            string? PathValue(TextBox f)
            { var v = f.Text.Trim().Trim('"'); if (v.Length == 0) return null; if (!File.Exists(v) || !v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an existing .exe file, or leave the path blank."); return Path.GetFullPath(v); }
            var cliPath = PathValue(cli); var desktopPath = PathValue(desktop);
            var p = manager.State.Preferences;
            if (!preview) StartupRegistration.Set(start.IsChecked == true);
            p.AutoSwitch = auto.IsChecked == true; p.StartWithWindows = start.IsChecked == true; p.StartupPolicyVersion = 1;
            p.Hotkey = hotkey.IsChecked == true; p.LightTheme = light.IsChecked == true;
            p.ThresholdPercent = t; p.RefreshSeconds = r; p.RefreshPolicyVersion = 1; p.CooldownSeconds = c; p.CliPath = cliPath; p.DesktopPath = desktopPath;
            if (!preview) manager.Save(); Theme.Apply(p.LightTheme); DialogResult = true;
        }
        catch (Exception ex) { Error(ex); }
    }
    void Export()
    {
        if (preview) return;
        try
        {
            var password = new InputDialog("Protect your export", "Use at least 12 characters. You will need this password to import your accounts.", "Export password", true) { Owner = this };
            if (password.ShowDialog() != true) return;
            var dialog = new SaveFileDialog { FileName = "CodexAccounts.accounts", Filter = "Encrypted account bundle|*.accounts", OverwritePrompt = true };
            if (dialog.ShowDialog(this) == true) File.WriteAllBytes(dialog.FileName, manager.Export(password.Value));
        }
        catch (Exception ex) { Error(ex); }
    }
    async void Import()
    {
        if (preview) return;
        try
        {
            var dialog = new OpenFileDialog { Filter = "Encrypted account bundle|*.accounts|Codex auth file|*.json", CheckFileExists = true };
            if (dialog.ShowDialog(this) != true) return;
            var raw = File.ReadAllBytes(dialog.FileName);
            IReadOnlyList<Account> imported;
            try
            {
                if (Path.GetExtension(dialog.FileName).Equals(".accounts", StringComparison.OrdinalIgnoreCase))
                {
                    var password = new InputDialog("Unlock account backup", "Enter the password used when this bundle was exported.", "Password", true) { Owner = this };
                    if (password.ShowDialog() != true) return;
                    imported = manager.ImportBundle(raw, password.Value);
                }
                else imported = [manager.Import(raw)];
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
            accountsUpdated?.Invoke();
            importRefresh = new CancellationTokenSource(TimeSpan.FromMinutes(6));
            Body.IsEnabled = Footer.IsEnabled = false;
            importStatus.Visibility = Visibility.Visible; importStatus.Text = "Accounts imported. Checking usage…";
            try
            {
                await manager.RefreshAccounts(imported.Select(a => a.Id).Distinct().ToArray(), importRefresh.Token, accountsUpdated);
                importStatus.Text = "Accounts imported. Usage check complete.";
            }
            catch (OperationCanceledException) { importStatus.Text = "Accounts imported. Usage will refresh automatically."; }
            finally
            {
                importRefresh.Dispose(); importRefresh = null;
                Body.IsEnabled = Footer.IsEnabled = true; accountsUpdated?.Invoke();
            }
        }
        catch (Exception ex) { Error(ex); }
    }
}

public sealed class AccountWindow : DialogShell
{
    readonly AccountManager manager;
    readonly Account account;
    readonly bool preview;
    readonly TextBox name;
    readonly CheckBox enabled;
    readonly TextBlock order;
    int priority;
    public AccountWindow(AccountManager manager, Account account, bool preview) : base("Account details", account.Email)
    {
        this.manager = manager; this.account = account; this.preview = preview;
        name = Field(Body, "Display name", account.Name); name.MaxLength = 64;
        enabled = Check(Body, "Include this account in automatic selection", account.Enabled);
        priority = manager.State.Accounts.IndexOf(account);
        Body.Children.Add(Heading("Priority"));
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        order = new TextBlock { Text = $"Position {priority + 1}", VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(order);
        var up = Button("Move up", () => { priority = Math.Max(0, priority - 1); order.Text = $"Position {priority + 1}"; });
        var down = Button("Move down", () => { priority = Math.Min(manager.State.Accounts.Count - 1, priority + 1); order.Text = $"Position {priority + 1}"; });
        Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); row.Children.Add(up); row.Children.Add(down);
        Body.Children.Add(row); Body.Children.Add(Heading("Usage windows"));
        if (account.Usage is { } usage && usage.Windows.Count > 0)
            foreach (var w in usage.Windows)
                Body.Children.Add(Muted($"{(w.Bucket == "codex" ? w.Label : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w.Bucket) + " · " + w.Label)} · {w.Remaining:0}% remaining\n{(w.Resets is { } reset ? "Resets " + reset.ToLocalTime().ToString("ddd, dd MMM · h:mm tt") : "Reset time unavailable")}", new Thickness(0, 4, 0, 8)));
        else Body.Children.Add(Muted("Usage has not been checked. Refresh from the main popup."));
        Body.Children.Add(Muted("Codex restarts when you switch a running desktop session. Local chat history and workspace files stay in place.", new Thickness(0, 16, 0, 8)));
        var remove = Button("Remove saved account", () =>
        {
            if (preview) return;
            if (MessageBox.Show(this, "Remove this saved sign-in from the manager? The active official Codex sign-in is kept.", "Remove saved account", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            { try { manager.Remove(account.Id); DialogResult = true; } catch (Exception ex) { Error(ex); } }
        });
        remove.Style = (Style)System.Windows.Application.Current.FindResource("Quiet");
        remove.SetResourceReference(ForegroundProperty, "Danger"); remove.HorizontalAlignment = HorizontalAlignment.Left; remove.Margin = new Thickness(-8, 2, 0, 0); Body.Children.Add(remove);
        Footer.Children.Add(Button("Cancel", () => DialogResult = false));
        Footer.Children.Add(Button("Save account", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            account.Name = name.Text.Trim(); account.Enabled = enabled.IsChecked == true;
            manager.State.Accounts.Remove(account); manager.State.Accounts.Insert(priority, account);
            if (!preview) manager.Save(); DialogResult = true;
        }, true));
    }
}

public sealed class RemovalDialog : DialogShell
{
    readonly RadioButton current;
    readonly RadioButton original;
    readonly CheckBox delete;
    public bool RestoreOriginal => original.IsChecked == true;
    public bool DeleteVault => delete.IsChecked == true;
    public RemovalDialog(bool baseline) : base("Return to official Codex", "Disconnect the manager and continue with one account.")
    {
        Body.Children.Add(Heading("Which sign-in should Codex keep?"));
        current = new RadioButton { Content = "Keep the current account (recommended)", IsChecked = true, GroupName = "return", Margin = new Thickness(0, 8, 0, 8), Foreground = (Brush)System.Windows.Application.Current.FindResource("Text") };
        Body.Children.Add(current);
        original = new RadioButton { Content = "Restore my original sign-in", GroupName = "return", IsEnabled = baseline, Margin = new Thickness(0, 8, 0, 8), Foreground = (Brush)System.Windows.Application.Current.FindResource("Text") };
        Body.Children.Add(original);
        Body.Children.Add(Muted("The original sign-in is saved before your first account switch. You may need to sign in again if it has expired."));
        Body.Children.Add(Heading("Saved accounts"));
        delete = Check(Body, "Permanently delete my saved accounts and settings", false);
        Body.Children.Add(Muted("Leave this unchecked to reuse your encrypted accounts when you return. Startup and automatic switching are disabled either way."));
        var panel = new Border { Background = (Brush)System.Windows.Application.Current.FindResource("AccentSurface"), CornerRadius = new CornerRadius(10), Padding = new Thickness(13), Margin = new Thickness(0, 18, 0, 0), Child = Muted("Your Codex configuration, projects, chat history, plugins, and skills stay in place.") };
        Body.Children.Add(panel);
        Footer.Children.Add(Button("Cancel", () => DialogResult = false)); Footer.Children.Add(Button("Return to Codex", () => DialogResult = true, true));
    }
    internal void VerifyInteractions()
    {
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(original).GetPattern(PatternInterface.SelectionItem)).Select();
        if (!RestoreOriginal || current.IsChecked == true) throw new InvalidOperationException("Return flow selection check failed.");
        ((ISelectionItemProvider)new RadioButtonAutomationPeer(current).GetPattern(PatternInterface.SelectionItem)).Select();
        if (RestoreOriginal) throw new InvalidOperationException("Return flow selection reset failed.");
    }
}
