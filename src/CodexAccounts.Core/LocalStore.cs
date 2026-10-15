using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace CodexAccounts.Core;

public interface IProtector { byte[] Protect(byte[] value); byte[] Unprotect(byte[] value); }

public sealed class WindowsProtector : IProtector
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr value);
    public byte[] Protect(byte[] value) => Transform(value, true);
    public byte[] Unprotect(byte[] value) => Transform(value, false);
    static byte[] Transform(byte[] value, bool protect)
    {
        var pin = GCHandle.Alloc(value, GCHandleType.Pinned);
        var input = new Blob { Length = value.Length, Data = pin.AddrOfPinnedObject() };
        Blob output = default;
        try
        {
            var ok = protect ? CryptProtectData(ref input, "Codex Accounts", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not unlock the local account vault.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (output.Data != IntPtr.Zero)
            {
                unsafe { CryptographicOperations.ZeroMemory(new Span<byte>((void*)output.Data, output.Length)); }
                LocalFree(output.Data);
            }
            pin.Free();
        }
    }
}

public static class AtomicFile
{
    public static void Write(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Linked credential files cannot be replaced safely.");
        var temp = path + ".ca-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class LocalStore
{
    public const string Owner = "codex-accounts:8228ca2d-38bb-4125-902a-cc205901d961";
    public string Root { get; }
    readonly IProtector protector;
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public LocalStore(string root, IProtector? protector = null)
    {
        Root = Path.GetFullPath(root);
        this.protector = protector ?? new WindowsProtector();
        if (Directory.Exists(Root) && File.GetAttributes(Root).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("The account vault must be a regular local directory.");
        if (Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any() && !File.Exists(Path.Combine(Root, "owner.txt")))
            throw new IOException("This folder belongs to another application. Choose a new data folder.");
        if (File.Exists(Path.Combine(Root, "owner.txt")) && File.ReadAllText(Path.Combine(Root, "owner.txt")) != Owner)
            throw new IOException("This data folder does not belong to Codex Accounts.");
        PrivateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "owner.txt"), Owner);
        PrivateDirectory(Path.Combine(Root, "accounts"));
        PrivateDirectory(Path.Combine(Root, "staging"));
    }
    public static void PrivateDirectory(string path)
    {
        if (Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Linked vault folders are not supported.");
        Directory.CreateDirectory(path);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new IOException("Windows user identity is unavailable.");
        var acl = new DirectorySecurity();
        acl.SetOwner(sid);
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }
    public ManagerState Load()
    {
        var p = Path.Combine(Root, "settings.json");
        if (!File.Exists(p)) return new();
        return JsonSerializer.Deserialize<ManagerState>(File.ReadAllBytes(p), Json) ?? throw new InvalidDataException("Settings could not be read.");
    }
    public void Save(ManagerState state) => AtomicFile.Write(Path.Combine(Root, "settings.json"), JsonSerializer.SerializeToUtf8Bytes(state, Json));
    public bool HasSecret(string name) => File.Exists(SecretPath(name));
    string SecretPath(string name)
    {
        if (name is "baseline" or "pending") return Path.Combine(Root, name + ".vault");
        if (!Guid.TryParseExact(name, "N", out _)) throw new InvalidDataException("Invalid account identifier.");
        return Path.Combine(Root, "accounts", name + ".vault");
    }
    public void PutSecret(string name, byte[] value) => AtomicFile.Write(SecretPath(name), protector.Protect(value));
    public void PutSecretObject<T>(string name, T value)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try { PutSecret(name, plain); } finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public byte[] GetSecret(string name)
    {
        var path = SecretPath(name);
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Linked vault files are not supported.");
        return protector.Unprotect(File.ReadAllBytes(path));
    }
    public void DeleteSecret(string name) { var p = SecretPath(name); if (File.Exists(p)) File.Delete(p); }
    public string Stage()
    {
        var path = Path.Combine(Root, "staging", Guid.NewGuid().ToString("N"));
        PrivateDirectory(path);
        return path;
    }
    public void DeleteStage(string stage)
    {
        var full = Path.GetFullPath(stage);
        var expected = Path.Combine(Root, "staging") + Path.DirectorySeparatorChar;
        if (!full.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe staging path.");
        for (int attempt = 0; ; attempt++)
        {
            try { DeletePrivateTree(full); return; }
            catch (IOException) when (attempt < 10) { Thread.Sleep(100); }
        }
    }
    public void CleanStages()
    {
        foreach (var p in Directory.EnumerateDirectories(Path.Combine(Root, "staging"))) DeleteStage(p);
    }
    public void RemoveOwnData()
    {
        if (File.ReadAllText(Path.Combine(Root, "owner.txt")) != Owner) throw new IOException("The data folder ownership check failed.");
        DeletePrivateTree(Root);
    }
    static void DeletePrivateTree(string path)
    {
        if (!Directory.Exists(path)) return;
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Refusing to remove a linked folder.");
        var entries = Directory.EnumerateFileSystemEntries(path).ToArray();
        foreach (var e in entries)
            if (File.GetAttributes(e).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Refusing to remove linked vault content.");
        foreach (var e in entries)
        { if (Directory.Exists(e)) DeletePrivateTree(e); else File.Delete(e); }
        Directory.Delete(path);
    }
}

public static class PortableVault
{
    public static byte[] Seal(byte[] plain, string password)
    {
        if (password.Length < 12) throw new InvalidDataException("Use an export password with at least 12 characters.");
        byte[] salt = RandomNumberGenerator.GetBytes(32), nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], cipher = new byte[plain.Length];
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, "CodexAccounts/1"u8);
            return JsonSerializer.SerializeToUtf8Bytes(new { format = "CodexAccounts/1", salt, nonce, tag, cipher });
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static byte[] Open(byte[] envelope, string password)
    {
        if (envelope.Length > 20 * 1024 * 1024) throw new InvalidDataException("The export is too large.");
        using var doc = JsonDocument.Parse(envelope);
        var r = doc.RootElement;
        if (JsonText.Get(r, "format") != "CodexAccounts/1") throw new InvalidDataException("Unsupported vault format.");
        var salt = r.GetProperty("salt").GetBytesFromBase64();
        var nonce = r.GetProperty("nonce").GetBytesFromBase64();
        var tag = r.GetProperty("tag").GetBytesFromBase64();
        var cipher = r.GetProperty("cipher").GetBytesFromBase64();
        if (salt.Length != 32 || nonce.Length != 12 || tag.Length != 16) throw new InvalidDataException("Invalid vault envelope.");
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210_000, HashAlgorithmName.SHA256, 32);
        var plain = new byte[cipher.Length];
        try { using var aes = new AesGcm(key, 16); aes.Decrypt(nonce, cipher, tag, plain, "CodexAccounts/1"u8); return plain; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
