using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace CodexAccounts;

public static class EmbeddedDocuments
{
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.4.0";
    public static string Read(string name)
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("The embedded document is missing: " + name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static string Guide => Read("CodexAccounts.Guide.txt");
    public static string Licenses => string.Join("\n\n", typeof(App).Assembly.GetManifestResourceNames()
        .Where(n => n.StartsWith("CodexAccounts.Licenses.", StringComparison.Ordinal)).Order()
        .Select(n => n["CodexAccounts.Licenses.".Length..^4] + "\n\n" + Read(n)));
    public static void Verify()
    {
        if (!Guide.Contains("System tray", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The embedded user guide is incomplete.");
        foreach (var name in new[] { "Application", "Notices", "CodexProfileOverlay-MIT", "Microsoft.NET-LICENSE", "Microsoft.NET-ThirdPartyNotices", "Lucide-ISC" })
            if (Read("CodexAccounts.Licenses." + name + ".txt").Length < 100) throw new InvalidDataException("An embedded license notice is incomplete.");
    }
}

public sealed class AboutWindow : DialogShell
{
    public AboutWindow() : base("About Codex Accounts", "Version " + EmbeddedDocuments.Version + " · Windows x64")
    {
        Body.Children.Add(Heading("One standalone executable"));
        Body.Children.Add(Muted("The interface, icons, help, license notices, and .NET runtime are included in CodexAccounts.exe. No separate SDK, Python, or Node.js installation is needed."));
        Body.Children.Add(Muted("Official Codex must be installed to use your accounts. Your encrypted account vault stays separate from the executable, so replacing the app keeps your saved accounts.", new Thickness(0, 10, 0, 0)));
        Body.Children.Add(Heading("System tray"));
        Body.Children.Add(Muted("Click the supplied icon to show or hide Accounts. Right-click it for Preferences, app location, help, safe disconnection, and Quit manager."));
        Body.Children.Add(Muted("If the icon is hidden, open the Windows tray overflow arrow. Drag the icon onto the taskbar tray to keep it visible. Windows controls which icons are shown.", new Thickness(0, 10, 0, 0)));
        Body.Children.Add(Heading("Windows startup"));
        Body.Children.Add(Muted("Starts quietly after Windows sign-in by default. Turn it off in Preferences to disable future startup. Quit manager exits for this session; saved accounts and settings are kept."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-8, 18, 0, 0) };
        actions.Children.Add(Button("User guide", () => new EmbeddedDocumentWindow("User guide", "Included in the standalone executable.", EmbeddedDocuments.Guide) { Owner = this }.ShowDialog()));
        actions.Children.Add(Button("Software licenses", () => new EmbeddedDocumentWindow("Software licenses", "Notices for bundled application and runtime components.", EmbeddedDocuments.Licenses) { Owner = this }.ShowDialog()));
        Body.Children.Add(actions);
        Footer.Children.Add(Button("Close", Close, true));
    }
}

public sealed class EmbeddedDocumentWindow : DialogShell
{
    public EmbeddedDocumentWindow(string title, string description, string content) : base(title, description)
    {
        // Read-only local documentation. No HTML, remote content, scripts, or links are executed.
        foreach (var line in content.Replace("\r", "").Split('\n'))
        {
            if (line.Length == 0) continue;
            var plain = Regex.Replace(line.TrimStart('#', ' '), @"\[([^\]]+)\]\(([^)]+)\)", "$1 ($2)").Replace("**", "").Replace("`", "");
            Body.Children.Add(line.StartsWith('#') ? Heading(plain) : Muted(plain, new Thickness(0, 3, 0, 6)));
        }
        Footer.Children.Add(Button("Close", Close, true));
    }
}
