using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace CodexAccounts.Core;

public static class StartupPolicy
{
    public static bool Apply(ManagerState state)
    {
        var p = state.Preferences; bool changed = false;
        // Version 1.3 introduces the user's requested default. A later opt-out is never reset.
        if (p.StartupPolicyVersion < 1)
        { p.StartWithWindows = state.Attached; p.StartupPolicyVersion = 1; changed = true; }
        if (!state.Attached && p.StartWithWindows) { p.StartWithWindows = false; changed = true; }
        return changed;
    }
}

public interface IStartupEntry
{
    string? Read();
    void Write(string command);
    void Delete();
}

public static class StartupRegistration
{
    public static void Set(bool enabled) => Set(enabled, Environment.ProcessPath ?? throw new IOException("The app location is unavailable."), new WindowsStartupEntry());
    public static void Remove() => Set(false);
    public static string Command(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.IndexOfAny(['"', '\r', '\n']) >= 0 || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Windows startup requires an absolute executable path.");
        return "\"" + Path.GetFullPath(executable) + "\" --tray";
    }
    public static bool Owns(string value, string executable)
    {
        if (value.Equals(Command(executable), StringComparison.OrdinalIgnoreCase)) return true;
        var match = Regex.Match(value, "\\A\"(?<path>[^\"\\r\\n]+)\" --tray\\z", RegexOptions.CultureInvariant);
        return match.Success && Path.IsPathFullyQualified(match.Groups["path"].Value) &&
            Path.GetFileName(match.Groups["path"].Value).Equals("CodexAccounts.exe", StringComparison.OrdinalIgnoreCase);
    }
    public static void Set(bool enabled, string executable, IStartupEntry entry)
    {
        var command = Command(executable); var existing = entry.Read();
        if (existing is not null && !Owns(existing, executable))
        {
            if (enabled) throw new IOException("A different program already uses the CodexAccounts startup entry. It was left unchanged.");
            return;
        }
        if (enabled) { if (existing != command) entry.Write(command); }
        else if (existing is not null) entry.Delete();
    }
    sealed class WindowsStartupEntry : IStartupEntry
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "CodexAccounts";
        public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name)?.ToString(); }
        public void Write(string command) { using var key = Registry.CurrentUser.CreateSubKey(Key); key.SetValue(Name, command, RegistryValueKind.String); }
        public void Delete() { using var key = Registry.CurrentUser.OpenSubKey(Key, true); key?.DeleteValue(Name, false); }
    }
}
