using System.Security.Cryptography;
using System.Text.Json;

namespace CodexAccounts.Core;

public sealed class SwitchJournal
{
    public AuthSnapshot Previous { get; set; } = new();
    public string TargetIdentity { get; set; } = "";
    public string? PreviousId { get; set; }
    public string TargetId { get; set; } = "";
}

public sealed class AccountManager
{
    public LocalStore Store { get; }
    public ManagerState State { get; }
    readonly Func<IAuthStore> authFactory;
    readonly IDesktopRuntime desktop;
    readonly Action removeStartup;
    readonly Func<Account, CancellationToken, Task> validateTarget;
    readonly Func<string, bool, CancellationToken, Task<AccountUsage>> readUsage;
    readonly SemaphoreSlim gate = new(1, 1);
    public bool Busy { get; private set; }
    public string? RecoveryNotice { get; private set; }
    public AccountManager(LocalStore store, IDesktopRuntime? desktop = null, Func<IAuthStore>? authFactory = null, Action? removeStartup = null,
        Func<Account, CancellationToken, Task>? validateTarget = null,
        Func<string, bool, CancellationToken, Task<AccountUsage>>? readUsage = null)
    {
        Store = store; State = store.Load();
        this.desktop = desktop ?? new DesktopRuntime(State.Preferences);
        this.authFactory = authFactory ?? (() => new CodexAuthStore(State.Preferences.CodexHome));
        this.removeStartup = removeStartup ?? StartupRegistration.Remove;
        this.validateTarget = validateTarget ?? VerifyTarget;
        this.readUsage = readUsage ?? ReadUsage;
        if (State.Version != 1) throw new InvalidDataException("This account vault was created by a newer application version.");
        if (RefreshPolicy.Apply(State.Preferences)) Save();
        Store.CleanStages();
    }
    public void Save() => Store.Save(State);
    public void SyncCurrent()
    {
        var raw = authFactory().Read();
        if (raw is null) { State.ActiveId = null; return; }
        try
        {
            var identity = AuthInfo.Parse(raw);
            var account = State.Accounts.Find(x => x.Identity == identity.Identity);
            State.ActiveId = account?.Id;
            if (account is not null) Store.PutSecret(account.Id, raw);
            Save();
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
    public Account Import(byte[] raw, string? name = null)
    {
        var info = AuthInfo.Parse(raw);
        var existing = State.Accounts.Find(a => a.Identity == info.Identity);
        var account = existing ?? new Account { Identity = info.Identity, Name = string.IsNullOrWhiteSpace(name) ? (info.Email.Split('@')[0] is { Length: > 0 } e ? e : "Account") : name.Trim() };
        account.Email = info.Email; account.Plan = info.Plan; account.Status = null;
        Store.PutSecret(account.Id, raw);
        if (existing is null) State.Accounts.Add(account);
        Save();
        return account;
    }
    public Account ImportCurrent(string? name = null)
    {
        var raw = authFactory().Read() ?? throw new IOException("No current Codex sign-in was found. Sign in using Add account.");
        try { var account = Import(raw, name); State.ActiveId = account.Id; Save(); return account; }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
    public async Task<Account> Login(string name, Action<string> progress, CancellationToken ct)
    {
        Account? added = null;
        await Exclusive(async () =>
        {
            var stage = Store.Stage();
            try
            {
                var cli = RequireCli();
                await using (var rpc = await CodexRpc.Start(cli, stage, true, ct))
                {
                    var result = await rpc.Call("account/login/start", new { type = "chatgpt" }, ct);
                    var url = JsonText.Get(result, "authUrl") ?? throw new IOException("Codex did not return a sign-in address.");
                    progress("Finish signing in in your browser. Your current Codex account stays open.");
                    BrowserLogin.Open(url);
                    await rpc.WaitForLogin(ct);
                }
                var raw = File.ReadAllBytes(Path.Combine(stage, "auth.json"));
                try { added = Import(raw, name); } finally { CryptographicOperations.ZeroMemory(raw); }
            }
            finally { Store.DeleteStage(stage); }
        }, ct);
        return added!;
    }
    public Task Refresh(CancellationToken ct, Action? updated = null) => RefreshAccounts(null, ct, updated);
    public Task RefreshAccount(string id, CancellationToken ct, Action? updated = null) => RefreshAccounts([id], ct, updated);
    public async Task RefreshAccounts(IReadOnlyCollection<string>? ids, CancellationToken ct, Action? updated = null)
    {
        await Exclusive(async () =>
        {
            SyncCurrent();
            foreach (var account in State.Accounts.ToArray())
            {
                // Manual-only accounts still need visible usage; Enabled controls auto-selection only.
                if (ids is not null && !ids.Contains(account.Id)) continue;
                ct.ThrowIfCancellationRequested();
                string? stage = null;
                try
                {
                    bool active = account.Id == State.ActiveId;
                    var home = active ? State.Preferences.CodexHome : stage = Store.Stage();
                    if (!active)
                    {
                        var raw = Store.GetSecret(account.Id);
                        try { AtomicFile.Write(Path.Combine(home, "auth.json"), raw); }
                        finally { CryptographicOperations.ZeroMemory(raw); }
                    }
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(25));
                    var snapshot = await readUsage(home, !active, timeout.Token);
                    account.Usage = snapshot.Usage;
                    account.Plan = snapshot.Plan ?? snapshot.Usage.Plan ?? account.Plan;
                    account.Email = snapshot.Email ?? account.Email;
                    account.Status = null; account.Verified = DateTimeOffset.UtcNow;
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException or System.ComponentModel.Win32Exception)
                { if (ct.IsCancellationRequested) throw; account.Status = "Usage unavailable"; }
                finally
                {
                    // Persist tokens rotated by official Codex even when the usage request failed.
                    if (stage is not null)
                    {
                        try
                        {
                            var path = Path.Combine(stage, "auth.json");
                            if (File.Exists(path))
                            {
                                var raw = File.ReadAllBytes(path);
                                try { if (AuthInfo.Parse(raw).Identity == account.Identity) Store.PutSecret(account.Id, raw); }
                                finally { CryptographicOperations.ZeroMemory(raw); }
                            }
                        }
                        finally { Store.DeleteStage(stage); }
                    }
                    else if (account.Id == State.ActiveId) SyncCurrent();
                    Save();
                    updated?.Invoke();
                }
            }
        }, ct);
    }
    async Task<AccountUsage> ReadUsage(string home, bool isolated, CancellationToken ct)
    {
        await using var rpc = await CodexRpc.Start(RequireCli(), home, isolated, ct);
        var usage = Usage.Parse(await rpc.Call("account/rateLimits/read", null, ct));
        var details = await rpc.Call("account/read", new { refreshToken = false }, ct);
        var account = details.TryGetProperty("account", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        return new(usage, JsonText.Get(account, "planType"), JsonText.Get(account, "email"));
    }
    public async Task Switch(string id, bool automatic, CancellationToken ct)
    {
        await Exclusive(async () =>
        {
            if (Store.HasSecret("pending")) throw new IOException("Recover the interrupted switch before changing accounts.");
            if (!State.Attached) throw new IOException("Resume the manager before switching accounts.");
            SyncCurrent();
            var account = State.Accounts.Find(a => a.Id == id) ?? throw new IOException("Account no longer exists.");
            if (account.Id == State.ActiveId) return;
            var activity = desktop.Activity(State.Preferences.CodexHome);
            if (activity == Activity.Busy || (automatic && activity != Activity.Idle))
                throw new IOException("Switch is waiting for Codex to finish its current task. You can also close Codex to switch safely.");
            await validateTarget(account, ct);
            activity = desktop.Activity(State.Preferences.CodexHome);
            if (activity == Activity.Busy || (automatic && activity != Activity.Idle))
                throw new IOException("A Codex task started while the account was being checked. The switch is waiting for it to finish.");
            var auth = authFactory();
            var raw = Store.GetSecret(id);
            try
            {
                if (AuthInfo.Parse(raw).Identity != account.Identity) throw new IOException("The saved account identity changed. Sign in again.");
                bool running = desktop.Running;
                await desktop.Stop(ct);
                var snapshot = auth.Capture();
                if (!Store.HasSecret("baseline")) Store.PutSecretObject("baseline", snapshot);
                var journal = new SwitchJournal { Previous = snapshot, PreviousId = State.ActiveId, TargetId = id, TargetIdentity = account.Identity };
                Store.PutSecretObject("pending", journal);
                try
                {
                    auth.Write(raw);
                    var check = auth.Read() ?? throw new IOException("The selected sign-in could not be read back.");
                    try { if (AuthInfo.Parse(check).Identity != account.Identity) throw new IOException("Sign-in verification failed."); }
                    finally { CryptographicOperations.ZeroMemory(check); }
                    if (running) await desktop.Launch(ct);
                    State.ActiveId = id; State.LastSwitch = DateTimeOffset.UtcNow; Save();
                    Store.DeleteSecret("pending");
                }
                catch
                {
                    // Never overwrite credentials after the new client has launched and could rotate them.
                    if (desktop.Running) await desktop.Stop(CancellationToken.None);
                    auth.Restore(snapshot); State.ActiveId = journal.PreviousId; Save();
                    Store.DeleteSecret("pending");
                    if (running) try { await desktop.Launch(CancellationToken.None); } catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { }
                    throw;
                }
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }, ct);
    }
    public async Task Recover(CancellationToken ct)
    {
        if (!Store.HasSecret("pending")) return;
        await Exclusive(async () =>
        {
            var raw = Store.GetSecret("pending");
            SwitchJournal journal;
            try { journal = JsonSerializer.Deserialize<SwitchJournal>(raw)!; }
            finally { CryptographicOperations.ZeroMemory(raw); }
            var auth = authFactory(); var current = auth.Read();
            string? identity = null;
            try { if (current is not null) identity = AuthInfo.Parse(current).Identity; }
            finally { if (current is not null) CryptographicOperations.ZeroMemory(current); }
            if (identity == journal.TargetIdentity)
            { State.ActiveId = journal.TargetId; Save(); Store.DeleteSecret("pending"); RecoveryNotice = "The previous account switch was recovered successfully."; }
            else if (auth.Capture().Fingerprint == journal.Previous.Fingerprint)
            { State.ActiveId = journal.PreviousId; Save(); Store.DeleteSecret("pending"); RecoveryNotice = "Your previous sign-in was preserved after an interrupted switch."; }
            else
                throw new IOException("A previous switch was interrupted and your sign-in has since changed. Current credentials were preserved. Keep the vault and use Return to official Codex before making further changes.");
            await Task.CompletedTask;
        }, ct);
    }
    public async Task<string?> AutoSwitch(CancellationToken ct)
    {
        var next = Selection.Next(State, DateTimeOffset.UtcNow);
        if (next is null) return null;
        await Switch(next.Id, true, ct);
        return next.Name;
    }
    public async Task Detach(bool restoreOriginal, bool removeData, CancellationToken ct)
    {
        await Exclusive(async () =>
        {
            if (Store.HasSecret("pending"))
            {
                if (restoreOriginal) throw new IOException("Keep the current sign-in to exit an interrupted switch safely.");
                Store.DeleteSecret("pending");
            }
            if (restoreOriginal && Store.HasSecret("baseline"))
            {
                if (desktop.Activity(State.Preferences.CodexHome) == Activity.Busy) throw new IOException("Finish the current Codex task before restoring the original sign-in.");
                var raw = Store.GetSecret("baseline");
                try
                {
                    var original = JsonSerializer.Deserialize<AuthSnapshot>(raw)!;
                    var auth = authFactory(); bool running = desktop.Running;
                    await desktop.Stop(ct);
                    var before = auth.Capture();
                    try { auth.Restore(original); if (running) await desktop.Launch(ct); }
                    catch { if (desktop.Running) await desktop.Stop(CancellationToken.None); auth.Restore(before); throw; }
                }
                finally { CryptographicOperations.ZeroMemory(raw); }
            }
            else
            {
                try { SyncCurrent(); }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or JsonException) { }
            }
            State.Preferences.AutoSwitch = false; State.Preferences.StartWithWindows = false; State.Attached = false;
            removeStartup(); Save();
            if (removeData) Store.RemoveOwnData();
        }, ct);
    }
    public void Remove(string id)
    {
        if (Busy) throw new IOException("Wait for the current operation to finish.");
        if (Store.HasSecret("pending")) throw new IOException("Recover the interrupted switch before removing accounts.");
        State.Accounts.RemoveAll(a => a.Id == id); Store.DeleteSecret(id);
        if (State.ActiveId == id) State.ActiveId = null;
        Save();
    }
    public byte[] Export(string password)
    {
        var records = new List<ExportAccount>();
        try
        {
            foreach (var account in State.Accounts) records.Add(new() { Name = account.Name, Enabled = account.Enabled, Auth = Store.GetSecret(account.Id) });
            var plain = JsonSerializer.SerializeToUtf8Bytes(records);
            try { return PortableVault.Seal(plain, password); } finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally { foreach (var r in records) CryptographicOperations.ZeroMemory(r.Auth); }
    }
    public IReadOnlyList<Account> ImportBundle(byte[] envelope, string password)
    {
        if (Busy) throw new IOException("Wait for the current operation to finish.");
        var plain = PortableVault.Open(envelope, password);
        List<ExportAccount>? records = null;
        try
        {
            records = JsonSerializer.Deserialize<List<ExportAccount>>(plain) ?? throw new InvalidDataException("Empty account bundle.");
            if (records.Count > 100) throw new InvalidDataException("The bundle contains too many accounts.");
            foreach (var r in records) _ = AuthInfo.Parse(r.Auth);
            var imported = new List<Account>();
            foreach (var r in records) { var account = Import(r.Auth, r.Name); account.Enabled = r.Enabled; imported.Add(account); }
            Save();
            return imported;
        }
        finally { CryptographicOperations.ZeroMemory(plain); if (records is not null) foreach (var r in records) CryptographicOperations.ZeroMemory(r.Auth); }
    }
    public Task OpenDesktop(CancellationToken ct) => desktop.Launch(ct);
    async Task VerifyTarget(Account account, CancellationToken ct)
    {
        if (account.Verified is { } verified && DateTimeOffset.UtcNow - verified < TimeSpan.FromMinutes(2) && account.Status is null) return;
        var stage = Store.Stage();
        try
        {
            var raw = Store.GetSecret(account.Id);
            try { AtomicFile.Write(Path.Combine(stage, "auth.json"), raw); }
            finally { CryptographicOperations.ZeroMemory(raw); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));
            await using (var rpc = await CodexRpc.Start(RequireCli(), stage, true, timeout.Token))
            {
                var result = await rpc.Call("account/read", new { refreshToken = true }, timeout.Token);
                if (!result.TryGetProperty("account", out var a) || JsonText.Get(a, "type") != "chatgpt")
                    throw new IOException("Sign in to this account again before switching. Your current Codex account is unchanged.");
            }
            var verifiedAuth = File.ReadAllBytes(Path.Combine(stage, "auth.json"));
            try
            {
                if (AuthInfo.Parse(verifiedAuth).Identity != account.Identity)
                    throw new IOException("Codex returned a different account identity. Your current sign-in was preserved.");
            }
            finally { CryptographicOperations.ZeroMemory(verifiedAuth); }
            account.Verified = DateTimeOffset.UtcNow; account.Status = null;
        }
        finally
        {
            try
            {
                var p = Path.Combine(stage, "auth.json");
                if (File.Exists(p))
                {
                    var raw = File.ReadAllBytes(p);
                    try { if (AuthInfo.Parse(raw).Identity == account.Identity) Store.PutSecret(account.Id, raw); }
                    finally { CryptographicOperations.ZeroMemory(raw); }
                }
            }
            finally { Store.DeleteStage(stage); Save(); }
        }
    }
    string RequireCli() => CodexLocator.Cli(State.Preferences.CliPath) ?? throw new FileNotFoundException("Codex CLI was not found. Select codex.exe in Advanced settings.");
    async Task Exclusive(Func<Task> action, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) throw new IOException("Another account operation is in progress.");
        Busy = true;
        try { await action(); } finally { Busy = false; gate.Release(); }
    }
    sealed class ExportAccount { public string Name { get; set; } = "Account"; public bool Enabled { get; set; } = true; public byte[] Auth { get; set; } = []; }
}
