using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexAccounts.Core;

public sealed class AuthSnapshot
{
    public byte[]? File { get; set; }
    public byte[]? Credential { get; set; }
    public string? CredentialTarget { get; set; }
    public string Mode { get; set; } = "file";
    public string Home { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => Hash(JsonSerializer.SerializeToUtf8Bytes(this));
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public interface IAuthStore
{
    AuthSnapshot Capture();
    byte[]? Read();
    void Write(byte[] auth);
    void Restore(AuthSnapshot snapshot);
}

public sealed class CodexAuthStore : IAuthStore
{
    public string Home { get; }
    public string Mode { get; }
    readonly string target;
    readonly string? requiredWorkspace;
    public CodexAuthStore(string home)
    {
        Home = Path.GetFullPath(home);
        if (!Directory.Exists(Home)) throw new DirectoryNotFoundException("Open official Codex and sign in once, then try again.");
        if (File.GetAttributes(Home).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("A linked Codex home is not supported for switching.");
        var config = Path.Combine(Home, "config.toml");
        var text = File.Exists(config) ? File.ReadAllText(config) : "";
        // Read only root-level settings. Project tables must never override these values accidentally.
        text = Regex.Split(text, @"(?m)^\s*\[")[0];
        Mode = ReadSetting(text, "cli_auth_credentials_store") ?? "file";
        requiredWorkspace = ReadSetting(text, "forced_chatgpt_workspace_id");
        if (ReadSetting(text, "forced_login_method") == "api")
            throw new InvalidOperationException("Your Codex configuration requires API-key login. Subscription switching will not change that configuration.");
        if (Mode is not ("file" or "auto" or "keyring"))
            throw new InvalidOperationException("This Codex credential mode cannot be switched persistently. Your Codex configuration has not been changed.");
        if (Mode != "file" && (ReadSetting(text, "auth_keyring_backend") ?? "secrets") != "direct")
            throw new InvalidOperationException("Codex is using the encrypted secrets keyring backend. This build supports file storage and explicit direct keyring storage, and will leave your existing setup unchanged.");
        var canonical = CanonicalDirectory(Home);
        var key = "cli|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()[..16];
        target = key + ".Codex Auth";
    }
    static string? ReadSetting(string text, string key)
    {
        var escaped = Regex.Escape(key);
        var m = Regex.Match(text, "(?m)^\\s*(?:" + escaped + "|\"" + escaped + "\"|'" + escaped + "')\\s*=\\s*[\"']([^\"']+)[\"']");
        return m.Success ? m.Groups[1].Value : null;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenDirectory(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint FinalPath(IntPtr handle, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static string CanonicalDirectory(string path)
    {
        var h = OpenDirectory(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (h == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var buffer = new StringBuilder(32768);
            var length = FinalPath(h, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity) throw new IOException("The Codex credential location could not be resolved.");
            return buffer.ToString();
        }
        finally { CloseHandle(h); }
    }
    string AuthPath => Path.Combine(Home, "auth.json");
    public AuthSnapshot Capture() => new()
    {
        Home = Home, Mode = Mode, File = ReadFile(),
        Credential = Mode == "file" ? null : WindowsCredential.Read(target), CredentialTarget = Mode == "file" ? null : target
    };
    byte[]? ReadFile()
    {
        if (!File.Exists(AuthPath)) return null;
        if (File.GetAttributes(AuthPath).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked auth files are not supported.");
        if (new FileInfo(AuthPath).Length > 1024 * 1024) throw new InvalidDataException("The Codex credential file is unexpectedly large.");
        return File.ReadAllBytes(AuthPath);
    }
    public byte[]? Read()
    {
        if (Mode == "file") return ReadFile();
        var credential = WindowsCredential.Read(target);
        if (credential is not null)
        {
            try { return Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(credential)); }
            finally { CryptographicOperations.ZeroMemory(credential); }
        }
        if (Mode == "keyring") return null;
        return ReadFile();
    }
    public void Write(byte[] auth)
    {
        var info = AuthInfo.Parse(auth);
        if (requiredWorkspace is not null && info.WorkspaceId != requiredWorkspace)
            throw new InvalidOperationException("Your Codex configuration requires a different workspace. This account cannot be activated without changing that requirement.");
        bool hasCredential = false;
        if (Mode == "auto")
        {
            var current = WindowsCredential.Read(target);
            hasCredential = current is not null;
            if (current is not null) CryptographicOperations.ZeroMemory(current);
        }
        if (Mode == "file" || (Mode == "auto" && !hasCredential)) AtomicFile.Write(AuthPath, auth);
        else
        {
            var bytes = Encoding.Unicode.GetBytes(Encoding.UTF8.GetString(auth));
            try { WindowsCredential.Write(target, bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
    }
    public void Restore(AuthSnapshot snapshot)
    {
        if (!string.Equals(Path.GetFullPath(snapshot.Home), Home, StringComparison.OrdinalIgnoreCase) || snapshot.Mode != Mode)
            throw new IOException("The credential location or storage mode changed. Restore was stopped to protect your current configuration.");
        if (snapshot.CredentialTarget is not null)
        {
            if (snapshot.CredentialTarget != target) throw new IOException("Credential target mismatch.");
            if (snapshot.Credential is { } c) WindowsCredential.Write(target, c); else WindowsCredential.Delete(target);
        }
        if (snapshot.File is { } file) AtomicFile.Write(AuthPath, file);
        else if (File.Exists(AuthPath))
        {
            if (File.GetAttributes(AuthPath).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked auth file cannot be removed.");
            File.Delete(AuthPath);
        }
    }
}

static class WindowsCredential
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct Credential
    {
        public int Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int BlobSize;
        public IntPtr Blob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr value);
    public static byte[]? Read(string target)
    {
        if (!CredRead(target, 1, 0, out var ptr))
        { var error = Marshal.GetLastWin32Error(); if (error == 1168) return null; throw new Win32Exception(error); }
        try { var c = Marshal.PtrToStructure<Credential>(ptr); var bytes = new byte[c.BlobSize]; Marshal.Copy(c.Blob, bytes, 0, bytes.Length); return bytes; }
        finally { CredFree(ptr); }
    }
    public static void Write(string target, byte[] bytes)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var c = new Credential { Type = 1, TargetName = target, UserName = target[..target.IndexOf(".Codex Auth", StringComparison.Ordinal)],
                Persist = 2, BlobSize = bytes.Length, Blob = pin.AddrOfPinnedObject() };
            if (!CredWrite(ref c, 0)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not save this Codex credential. The previous sign-in will be restored.");
        }
        finally { pin.Free(); }
    }
    public static void Delete(string target)
    { if (!CredDelete(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}
