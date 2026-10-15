using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexAccounts.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("DPAPI round-trip and ciphertext", async () => { var p = new WindowsProtector(); var raw = Auth("first"); var sealedBytes = p.Protect(raw); Check(!Encoding.UTF8.GetString(sealedBytes).Contains("fake-access")); Equal(raw, p.Unprotect(sealedBytes)); await Task.CompletedTask; }),
    ("Same email in two workspaces stays distinct", async () => { using var f = new Fixture(); var a = f.Manager.Import(Auth("a")); var b = f.Manager.Import(Auth("b")); Check(a.Id != b.Id); f.Manager.Import(Auth("a")); Check(f.Manager.State.Accounts.Count == 3); await Task.CompletedTask; }),
    ("Switch preserves config, history and original baseline", async () => { using var f = new Fixture(); var before = f.OtherFiles(); var target = f.Manager.Import(Auth("second")); await f.Manager.Switch(target.Id, false, default); Equal(Auth("second"), f.Auth.Read()!); Check(f.Manager.State.ActiveId == target.Id); Equal(before, f.OtherFiles()); Check(f.Store.HasSecret("baseline") && !f.Store.HasSecret("pending")); }),
    ("Launch failure rolls back sign-in and metadata", async () => { using var f = new Fixture(); f.Desktop.IsRunning = true; f.Desktop.FailLaunches = 1; var previous = f.Manager.State.ActiveId; var target = f.Manager.Import(Auth("second")); await Throws<IOException>(() => f.Manager.Switch(target.Id, false, default)); Equal(Auth("original"), f.Auth.Read()!); Check(f.Manager.State.ActiveId == previous && !f.Store.HasSecret("pending")); }),
    ("Credential write failure rolls back", async () => { using var f = new Fixture(); f.Auth.FailNextWrite = true; var target = f.Manager.Import(Auth("second")); await Throws<IOException>(() => f.Manager.Switch(target.Id, false, default)); Equal(Auth("original"), f.Auth.Read()!); }),
    ("Busy task blocks credential changes", async () => { using var f = new Fixture(); f.Desktop.CurrentActivity = Activity.Busy; var target = f.Manager.Import(Auth("second")); await Throws<IOException>(() => f.Manager.Switch(target.Id, false, default)); Equal(Auth("original"), f.Auth.Read()!); Check(!f.Store.HasSecret("baseline")); }),
    ("Unknown task state blocks automatic switching", async () => { using var f = new Fixture(); f.Desktop.CurrentActivity = Activity.Unknown; var target = f.Manager.Import(Auth("second")); await Throws<IOException>(() => f.Manager.Switch(target.Id, true, default)); Equal(Auth("original"), f.Auth.Read()!); }),
    ("Recovery commits target without reverting rotated tokens", async () => { using var f = new Fixture(); var target = f.Manager.Import(Auth("second")); var journal = new SwitchJournal { Previous=f.Auth.Capture(), PreviousId=f.Manager.State.ActiveId, TargetId=target.Id, TargetIdentity=target.Identity }; f.Store.PutSecret("pending",JsonSerializer.SerializeToUtf8Bytes(journal)); f.Auth.Write(Auth("second","rotated")); var recovered=f.NewManager(); await recovered.Recover(default); Equal(Auth("second","rotated"),f.Auth.Read()!); Check(recovered.State.ActiveId==target.Id && !f.Store.HasSecret("pending")); }),
    ("Recovery preserves an externally changed account", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); f.Store.PutSecret("pending",JsonSerializer.SerializeToUtf8Bytes(new SwitchJournal { Previous=f.Auth.Capture(),TargetId=target.Id,TargetIdentity=target.Identity })); f.Auth.Write(Auth("external")); var recovered=f.NewManager(); await Throws<IOException>(()=>recovered.Recover(default)); Equal(Auth("external"),f.Auth.Read()!); await Throws<IOException>(()=>recovered.Switch(target.Id,false,default)); }),
    ("Detach keeps current official sign-in and disables automation", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); await f.Manager.Switch(target.Id,false,default); f.Manager.State.Preferences.AutoSwitch=true; var before=f.OtherFiles(); await f.Manager.Detach(false,false,default); Equal(Auth("second"),f.Auth.Read()!); Equal(before,f.OtherFiles()); Check(!f.Manager.State.Attached && !f.Manager.State.Preferences.AutoSwitch && f.StartupRemoved); Check(f.Store.HasSecret(target.Id)); }),
    ("Detach can restore exact original sign-in", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); await f.Manager.Switch(target.Id,false,default); await f.Manager.Detach(true,false,default); Equal(Auth("original"),f.Auth.Read()!); }),
    ("Remove vault does not remove Codex state", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); await f.Manager.Switch(target.Id,false,default); var before=f.OtherFiles(); await f.Manager.Detach(false,true,default); Check(!Directory.Exists(f.Store.Root)); Equal(before,f.OtherFiles()); Equal(Auth("second"),f.Auth.Read()!); }),
    ("Reattach reuses saved accounts with no config migration", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); await f.Manager.Detach(false,false,default); var reopened=f.NewManager(); Check(reopened.State.Accounts.Count==2 && !reopened.State.Attached); reopened.State.Attached=true; reopened.Save(); await reopened.Switch(target.Id,false,default); Equal(Auth("second"),f.Auth.Read()!); }),
    ("Deleting active saved profile does not log Codex out", async () => { using var f=new Fixture(); f.Manager.Remove(f.Manager.State.ActiveId!); Equal(Auth("original"),f.Auth.Read()!); await Task.CompletedTask; }),
    ("Portable export rejects wrong password and tampering", async () => { var raw=Auth("a"); var sealedBytes=PortableVault.Seal(raw,"a long test password"); Equal(raw,PortableVault.Open(sealedBytes,"a long test password")); await Throws<CryptographicException>(()=> { PortableVault.Open(sealedBytes,"wrong password"); return Task.CompletedTask; }); using var doc=JsonDocument.Parse(sealedBytes); var dict=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(sealedBytes)!; var cipher=dict["cipher"].GetBytesFromBase64(); cipher[0]^=1; var changed=JsonSerializer.SerializeToUtf8Bytes(new {format="CodexAccounts/1",salt=doc.RootElement.GetProperty("salt").GetBytesFromBase64(),nonce=doc.RootElement.GetProperty("nonce").GetBytesFromBase64(),tag=doc.RootElement.GetProperty("tag").GetBytesFromBase64(),cipher}); await Throws<CryptographicException>(()=> { PortableVault.Open(changed,"a long test password"); return Task.CompletedTask; }); }),
    ("Encrypted account bundle merges and preserves ordering", async () => { using var a=new Fixture(); using var b=new Fixture(); a.Manager.Import(Auth("second"),"Second"); b.Manager.ImportBundle(a.Manager.Export("another good password"),"another good password"); Check(b.Manager.State.Accounts.Count==2 && b.Manager.State.Accounts[1].Name=="Second"); await Task.CompletedTask; }),
    ("Dynamic weekly-only and multiple usage pools parse", async () => { using var d=JsonDocument.Parse("""{"ordinaryUsageAllowed":true,"rateLimitsByLimitId":{"codex":{"primary":null,"secondary":{"usedPercent":20,"windowDurationMins":10080,"resetsAt":1900000000}},"model-special":{"primary":{"usedPercent":45,"windowDurationMins":60,"resetsAt":1900000000}}}}"""); var u=Usage.Parse(d.RootElement); Check(u.Windows.Count==2 && u.Windows[0].Minutes==10080 && u.Windows[1].Minutes==60); await Task.CompletedTask; }),
    ("Priority skips unknown, disabled, and exhausted accounts", async () => { var state=State(); state.Accounts[1].Enabled=false; state.Accounts.Add(new(){Id="unknown",Usage=null}); state.Accounts.Add(new(){Id="next",Usage=Ready(80)}); Check(Selection.Next(state,DateTimeOffset.UtcNow)?.Id=="next"); await Task.CompletedTask; }),
    ("Unknown usage never triggers failover", async () => { var state=State(); state.Accounts[0].Usage=null; Check(Selection.Next(state,DateTimeOffset.UtcNow)==null); await Task.CompletedTask; }),
    ("Expired reset does not imply replenished quota", async () => { var state=State(); state.Accounts[1].Usage!.Windows[0].Resets=DateTimeOffset.UtcNow.AddMinutes(-1); Check(Selection.Next(state,DateTimeOffset.UtcNow)==null); await Task.CompletedTask; }),
    ("Backend denied usage overrides remaining percentage", async () => { var state=State(); state.Accounts[1].Usage!.OrdinaryUsageAllowed=false; Check(Selection.Next(state,DateTimeOffset.UtcNow)==null); await Task.CompletedTask; }),
    ("Cooldown prevents oscillation", async () => { var state=State(); state.LastSwitch=DateTimeOffset.UtcNow; Check(Selection.Next(state,DateTimeOffset.UtcNow)==null); await Task.CompletedTask; }),
    ("Stale quota cannot cause auto-switch", async () => { var state=State(); state.Accounts[0].Usage!.Captured=DateTimeOffset.UtcNow.AddMinutes(-10); Check(Selection.Next(state,DateTimeOffset.UtcNow)==null); await Task.CompletedTask; }),
    ("Local task events detect busy and completion", async () => { using var f=new Fixture(); var file=Path.Combine(f.Home,"sessions","rollout-test.jsonl"); File.WriteAllText(file,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n"); Check(ActivityFiles.Read(f.Home,DateTime.UtcNow.AddMinutes(-1))==Activity.Busy); File.AppendAllText(file,"{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}\n"); Check(ActivityFiles.Read(f.Home,DateTime.UtcNow.AddMinutes(-1))==Activity.Idle); await Task.CompletedTask; }),
    ("Unsupported credential mode leaves config unchanged", async () => { using var f=new Fixture(); File.WriteAllText(Path.Combine(f.Home,"config.toml"),"cli_auth_credentials_store = \"ephemeral\"\n"); var before=f.OtherFiles(); await Throws<InvalidOperationException>(()=> { _=new CodexAuthStore(f.Home); return Task.CompletedTask; }); Equal(before,f.OtherFiles()); }),
    ("Actual Codex RPC initializes in isolated empty home", async () => { using var f=new Fixture(); var cli=CodexLocator.Cli(); if(cli is null) throw new IOException("CLI is required for this integration check."); var stage=f.Store.Stage(); using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(20)); try { await using var rpc=await CodexRpc.Start(cli,stage,true,ct.Token); var response=await rpc.Call("account/read",new{refreshToken=false},ct.Token); Check(response.GetProperty("account").ValueKind==JsonValueKind.Null); } finally { f.Store.DeleteStage(stage); } })
    ,("Ten accounts switch without configuration migration", async () => { using var f=new Fixture(); for(int i=1;i<10;i++) f.Manager.Import(Auth("account-"+i)); Check(f.Manager.State.Accounts.Count==10); var before=f.OtherFiles(); foreach(var a in f.Manager.State.Accounts.ToArray()) await f.Manager.Switch(a.Id,false,default); Equal(before,f.OtherFiles()); })
    ,("Unrelated exhausted model pool does not trigger coding failover", async () => { var state=State(); state.Accounts[0].Usage=Ready(50); state.Accounts[0].Usage!.Windows.Add(new(){Bucket="code-review",Used=100}); Check(Selection.Next(state,DateTimeOffset.UtcNow)==null); await Task.CompletedTask; })
    ,("Backend allowed account without fixed windows is eligible", async () => { var state=State(); state.Accounts[1].Usage=new(){OrdinaryUsageAllowed=true}; Check(Selection.Next(state,DateTimeOffset.UtcNow)?.Id=="next-priority"); await Task.CompletedTask; })
    ,("Exit after conflicted recovery preserves current external sign-in", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); f.Store.PutSecretObject("pending",new SwitchJournal{Previous=f.Auth.Capture(),TargetId=target.Id,TargetIdentity=target.Identity}); f.Auth.Write(Auth("external")); await f.Manager.Detach(false,false,default); Equal(Auth("external"),f.Auth.Read()!); Check(!f.Store.HasSecret("pending") && !f.Manager.State.Attached); })
    ,("Forced workspace constraint is respected without config edits", async () => { using var f=new Fixture(); File.WriteAllText(Path.Combine(f.Home,"config.toml"),"forced_chatgpt_workspace_id = 'required-workspace'\n"); var auth=new CodexAuthStore(f.Home); var before=f.OtherFiles(); await Throws<InvalidOperationException>(()=>{auth.Write(Auth("other-workspace"));return Task.CompletedTask;}); Equal(before,f.OtherFiles()); Equal(Auth("original"),auth.Read()!); })
    ,("Quoted TOML credential setting is detected", async () => { using var f=new Fixture(); File.WriteAllText(Path.Combine(f.Home,"config.toml"),"\"cli_auth_credentials_store\" = 'ephemeral'\n"); await Throws<InvalidOperationException>(()=>{_=new CodexAuthStore(f.Home);return Task.CompletedTask;}); })
    ,("Default encrypted-secrets keyring is rejected without rewriting config", async () => { using var f=new Fixture(); File.WriteAllText(Path.Combine(f.Home,"config.toml"),"cli_auth_credentials_store = 'keyring'\n"); var before=f.OtherFiles(); await Throws<InvalidOperationException>(()=>{_=new CodexAuthStore(f.Home);return Task.CompletedTask;}); Equal(before,f.OtherFiles()); })
    ,("A task starting during validation blocks the switch", async () => { using var f=new Fixture(); var target=f.Manager.Import(Auth("second")); var manager=new AccountManager(f.Store,f.Desktop,()=>f.Auth,()=>{},(_,_)=>{f.Desktop.CurrentActivity=Activity.Busy;return Task.CompletedTask;}); await Throws<IOException>(()=>manager.Switch(target.Id,true,default)); Equal(Auth("original"),f.Auth.Read()!); Check(!f.Store.HasSecret("baseline")); })
    ,("API-key credentials cannot be mixed into subscription profiles", async () => { using var doc=JsonDocument.Parse(Auth("a")); var mixed=JsonSerializer.SerializeToUtf8Bytes(new{OPENAI_API_KEY="fake-api-key",tokens=doc.RootElement.GetProperty("tokens")}); await Throws<InvalidDataException>(()=>{AuthInfo.Parse(mixed);return Task.CompletedTask;}); })
    ,("New preferences start with Windows without enabling auto-switch", async () => { var state=new ManagerState(); Check(state.Preferences.StartWithWindows && !state.Preferences.AutoSwitch); Check(StartupPolicy.Apply(state) && state.Preferences.StartupPolicyVersion==1); await Task.CompletedTask; })
    ,("Legacy startup preference migrates once and retains later opt-out", async () => { var state=JsonSerializer.Deserialize<ManagerState>("{\"Preferences\":{\"StartWithWindows\":false}}")!; Check(StartupPolicy.Apply(state) && state.Preferences.StartWithWindows); state.Preferences.StartWithWindows=false; var reopened=JsonSerializer.Deserialize<ManagerState>(JsonSerializer.Serialize(state))!; Check(!StartupPolicy.Apply(reopened) && !reopened.Preferences.StartWithWindows); await Task.CompletedTask; })
    ,("Disconnected manager stays out of Windows startup", async () => { var state=new ManagerState{Attached=false}; Check(StartupPolicy.Apply(state) && !state.Preferences.StartWithWindows); state.Preferences.StartWithWindows=true; Check(StartupPolicy.Apply(state) && !state.Preferences.StartWithWindows); await Task.CompletedTask; })
    ,("Startup registration quotes spaces and is idempotent", async () => { var entry=new FakeStartupEntry(); const string exe=@"C:\My Apps\CodexAccounts.exe"; StartupRegistration.Set(true,exe,entry); Check(entry.Value=="\""+exe+"\" --tray" && entry.Writes==1); StartupRegistration.Set(true,exe,entry); Check(entry.Writes==1); await Task.CompletedTask; })
    ,("Startup moves to the current EXE without leaving an old entry", async () => { var entry=new FakeStartupEntry{Value="\"C:\\Old App\\CodexAccounts.exe\" --tray"}; StartupRegistration.Set(true,@"C:\New App\CodexAccounts.exe",entry); Check(entry.Value=="\"C:\\New App\\CodexAccounts.exe\" --tray" && entry.Writes==1); await Task.CompletedTask; })
    ,("Disabling startup removes only the owned entry", async () => { const string exe=@"C:\Apps\CodexAccounts.exe"; var entry=new FakeStartupEntry{Value=StartupRegistration.Command(exe)}; StartupRegistration.Set(false,exe,entry); Check(entry.Value is null && entry.Deletes==1); StartupRegistration.Set(false,exe,entry); Check(entry.Deletes==1); await Task.CompletedTask; })
    ,("Foreign startup entry is never overwritten or removed", async () => { const string other="\"C:\\Tools\\Different.exe\" --tray"; var entry=new FakeStartupEntry{Value=other}; await Throws<IOException>(()=>{StartupRegistration.Set(true,@"C:\Apps\CodexAccounts.exe",entry);return Task.CompletedTask;}); StartupRegistration.Set(false,@"C:\Apps\CodexAccounts.exe",entry); Check(entry.Value==other && entry.Writes==0 && entry.Deletes==0); })
    ,("Malformed startup commands are not mistaken for owned entries", async () => { const string exe=@"C:\Apps\CodexAccounts.exe"; foreach(var value in new[]{"CodexAccounts.exe --tray","\"C:\\Apps\\CodexAccounts.exe\" --tray --other","\"C:\\Apps\\CodexAccounts.exe\" --tray\r\nother"}) Check(!StartupRegistration.Owns(value,exe)); await Task.CompletedTask; })
    ,("Unsafe startup executable paths are rejected", async () => { foreach(var path in new[]{"CodexAccounts.exe","C:\\Apps\\bad\"name.exe","C:\\Apps\\bad\nname.exe","C:\\Apps\\file.cmd"}) await Throws<IOException>(()=>{StartupRegistration.Command(path);return Task.CompletedTask;}); })
    ,("Current renamed EXE can remove its exact startup command", async () => { const string exe=@"C:\Apps\My Account Manager.exe"; var entry=new FakeStartupEntry(); StartupRegistration.Set(true,exe,entry); Check(StartupRegistration.Owns(entry.Value!,exe)); StartupRegistration.Set(false,exe,entry); Check(entry.Value is null); await Task.CompletedTask; })
    ,("Refresh default migrates to 30 seconds once", async () => {
        var legacy=JsonSerializer.Deserialize<Preferences>("{\"RefreshSeconds\":120}")!;
        Check(new Preferences().RefreshSeconds==30 && RefreshPolicy.Apply(legacy) && legacy.RefreshSeconds==30);
        legacy.RefreshSeconds=120;
        var reopened=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(legacy))!;
        Check(!RefreshPolicy.Apply(reopened) && reopened.RefreshSeconds==120); await Task.CompletedTask;
    })
    ,("Custom refresh interval survives the default migration", async () => {
        var custom=new Preferences{RefreshSeconds=90}; Check(RefreshPolicy.Apply(custom) && custom.RefreshSeconds==90); await Task.CompletedTask;
    })
    ,("Both five-hour and weekly limits retain independent values and resets", async () => {
        using var d=JsonDocument.Parse("""{"rateLimits":{"planType":"plus","primary":{"usedPercent":35,"windowDurationMins":300,"resetsAt":1900000000},"secondary":{"usedPercent":70,"windowDurationMins":10080,"resetsAt":1900100000}}}""");
        var u=Usage.Parse(d.RootElement); Check(u.Windows.Count==2 && u.Windows[0].Remaining==65 && u.Windows[1].Remaining==30 && u.Windows[0].Resets!=u.Windows[1].Resets && u.Plan=="plus"); await Task.CompletedTask;
    })
    ,("Weekly-only Plus has exactly one reported limit", async () => {
        using var d=JsonDocument.Parse("""{"rateLimits":{"planType":"plus","primary":null,"secondary":{"usedPercent":24,"windowDurationMins":10080,"resetsAt":1900000000}}}""");
        var u=Usage.Parse(d.RootElement); Check(u.Windows.Count==1 && u.Windows[0].Minutes==10080 && u.Windows[0].Remaining==76); await Task.CompletedTask;
    })
    ,("Missing and malformed usage do not invent quota windows", async () => {
        using var d=JsonDocument.Parse("""{"rateLimits":{"primary":{"usedPercent":null},"secondary":{"usedPercent":30,"windowDurationMins":null,"resetsAt":"unknown"}}}""");
        var u=Usage.Parse(d.RootElement); Check(u.Windows.Count==1 && u.Windows[0].Minutes is null && u.Windows[0].Resets is null);
        using var empty=JsonDocument.Parse("{}"); Check(Usage.Parse(empty.RootElement).Windows.Count==0); await Task.CompletedTask;
    })
    ,("New account refresh checks only the imported account and saves rotated tokens", async () => {
        using var f=new Fixture(); var added=f.Manager.Import(Auth("new-account")); int reads=0,updates=0; string? stage=null;
        var m=f.NewManager((home,isolated,ct)=>{
            Check(isolated && home!=f.Home); stage=home; reads++;
            Equal(Auth("new-account"),File.ReadAllBytes(Path.Combine(home,"auth.json")));
            File.WriteAllBytes(Path.Combine(home,"auth.json"),Auth("new-account","rotated"));
            return Task.FromResult(new AccountUsage(Ready(65),"plus"));
        });
        await m.RefreshAccount(added.Id,default,()=>updates++);
        Check(reads==1 && updates==1 && m.State.Accounts.Find(a=>a.Id==added.Id)!.Usage!.Remaining==65 && !Directory.Exists(stage));
        Equal(Auth("new-account","rotated"),f.Store.GetSecret(added.Id)); Equal(Auth("original"),f.Auth.Read()!);
        Check(f.NewManager().State.Accounts.Find(a=>a.Id==added.Id)!.Usage!.Remaining==65);
    })
    ,("Refresh includes manual-only accounts and publishes each result", async () => {
        using var f=new Fixture(); var second=f.Manager.Import(Auth("manual")); second.Enabled=false; f.Manager.Save(); int reads=0,updates=0;
        var m=f.NewManager((home,isolated,ct)=>{reads++; Check(isolated || home==f.Home); return Task.FromResult(new AccountUsage(Ready(80)));});
        await m.Refresh(default,()=>updates++); Check(reads==2 && updates==2 && m.State.Accounts.All(a=>a.Usage!.Remaining==80) && !m.State.Accounts[1].Enabled);
    })
    ,("Usage failure retains imported account, cached values, and rotated credentials", async () => {
        using var f=new Fixture(); var added=f.Manager.Import(Auth("new-account")); added.Usage=Ready(25); f.Manager.Save(); string? stage=null;
        var m=f.NewManager((home,isolated,ct)=>{stage=home;File.WriteAllBytes(Path.Combine(home,"auth.json"),Auth("new-account","rotated"));throw new IOException("Offline");});
        await m.RefreshAccount(added.Id,default); var saved=f.NewManager().State.Accounts.Find(a=>a.Id==added.Id)!;
        Check(saved.Status=="Usage unavailable" && saved.Usage!.Remaining==25 && !Directory.Exists(stage)); Equal(Auth("new-account","rotated"),f.Store.GetSecret(added.Id));
    })
    ,("Cancelled initial refresh preserves the new account and cleans its temporary home", async () => {
        using var f=new Fixture(); var added=f.Manager.Import(Auth("new-account")); using var ct=new CancellationTokenSource(); string? stage=null;
        var m=f.NewManager((home,isolated,token)=>{stage=home;ct.Cancel();return Task.FromCanceled<AccountUsage>(token);});
        await Throws<OperationCanceledException>(()=>m.RefreshAccount(added.Id,ct.Token));
        Check(!m.Busy && f.NewManager().State.Accounts.Any(a=>a.Id==added.Id) && !Directory.Exists(stage)); Equal(Auth("original"),f.Auth.Read()!);
    })
    ,("Concurrent refresh cannot start overlapping quota readers", async () => {
        using var f=new Fixture(); var release=new TaskCompletionSource<AccountUsage>(TaskCreationOptions.RunContinuationsAsynchronously); int reads=0;
        var m=f.NewManager((home,isolated,ct)=>{reads++;return release.Task;}); var first=m.Refresh(default);
        try { await Throws<IOException>(()=>m.Refresh(default)); Check(reads==1 && m.Busy); }
        finally { release.SetResult(new AccountUsage(Ready(70))); await first; }
        Check(!m.Busy);
    })
};
int failed=0;
foreach(var test in tests) try { await test.Run(); Console.WriteLine("PASS " + test.Name); } catch(Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
Console.WriteLine($"\n{tests.Length-failed}/{tests.Length} passed");
Environment.ExitCode=failed==0?0:1;

static void Check(bool ok) { if(!ok) throw new Exception("Assertion failed."); }
static void Equal(byte[] a,byte[] b) => Check(a.AsSpan().SequenceEqual(b));
static async Task Throws<T>(Func<Task> action) where T:Exception { try { await action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static byte[] Auth(string workspace,string token="initial") => TestAuth.Create(workspace,token);
static Usage Ready(double remaining) => new(){Captured=DateTimeOffset.UtcNow,Windows=[new(){Used=100-remaining,Minutes=300,Resets=DateTimeOffset.UtcNow.AddHours(2)}]};
static ManagerState State()=>new(){ActiveId="active",Preferences=new(){AutoSwitch=true},Accounts=[new(){Id="active",Usage=Ready(0)},new(){Id="next-priority",Usage=Ready(60)}]};

static class TestAuth
{
    public static byte[] Create(string workspace,string token="initial")
    {
        var claims=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,object>{{"sub","test-user"},{"email","test@example.invalid"},{"https://api.openai.com/auth",new{chatgpt_account_id=workspace,chatgpt_plan_type="plus"}}})).TrimEnd('=').Replace('+','-').Replace('/','_');
        return JsonSerializer.SerializeToUtf8Bytes(new {tokens=new {id_token="eyJhbGciOiJub25lIn0."+claims+".test",access_token="fake-access-"+token,refresh_token="fake-refresh-"+token,account_id=workspace},last_refresh="2026-10-13T00:00:00Z"});
    }
}
sealed class Fixture:IDisposable
{
    readonly string root=Path.Combine(AppContext.BaseDirectory,"test-data",Guid.NewGuid().ToString("N"));
    public string Home {get;}
    public LocalStore Store {get;}
    public FakeAuth Auth {get;}
    public FakeDesktop Desktop {get;}=new();
    public AccountManager Manager {get;}
    public bool StartupRemoved {get;private set;}
    public Fixture()
    {
        Home=Path.Combine(root,"codex"); Directory.CreateDirectory(Path.Combine(Home,"sessions"));
        File.WriteAllBytes(Path.Combine(Home,"auth.json"),TestAuth.Create("original"));
        File.WriteAllText(Path.Combine(Home,"config.toml"),"model = \"keep-my-model\"\n[plugins]\nuntouched = true\n");
        File.WriteAllText(Path.Combine(Home,"sessions","history.jsonl"),"preserve-my-context");
        Store=new LocalStore(Path.Combine(root,"manager")); Auth=new FakeAuth(Home); Manager=NewManager();
        Manager.State.Preferences.CodexHome=Home; Manager.ImportCurrent();
    }
    public AccountManager NewManager(Func<string,bool,CancellationToken,Task<AccountUsage>>? readUsage=null)=>new(Store,Desktop,()=>Auth,()=>StartupRemoved=true,(_,_)=>Task.CompletedTask,readUsage);
    public byte[] OtherFiles()=>SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Home,"config.toml"))+File.ReadAllText(Path.Combine(Home,"sessions","history.jsonl"))));
    public void Dispose() { if(Directory.Exists(Store.Root))Store.RemoveOwnData(); foreach(var file in Directory.EnumerateFiles(Home,"*",SearchOption.AllDirectories))File.Delete(file); Directory.Delete(Path.Combine(Home,"sessions"));Directory.Delete(Home);Directory.Delete(root); }
}
sealed class FakeAuth(string home):IAuthStore
{
    public bool FailNextWrite {get;set;}
    string PathName=>Path.Combine(home,"auth.json");
    public byte[]? Read()=>File.Exists(PathName)?File.ReadAllBytes(PathName):null;
    public AuthSnapshot Capture()=>new(){Home=home,File=Read()};
    public void Write(byte[] raw) { File.WriteAllBytes(PathName,raw); if(FailNextWrite){FailNextWrite=false;throw new IOException("Injected credential write failure.");} }
    public void Restore(AuthSnapshot snapshot){if(snapshot.File is {} raw)File.WriteAllBytes(PathName,raw);else File.Delete(PathName);}
}
sealed class FakeDesktop:IDesktopRuntime
{
    public bool IsRunning {get;set;}
    public bool Running=>IsRunning;
    public Activity CurrentActivity {get;set;}=CodexAccounts.Core.Activity.Idle;
    public int FailLaunches {get;set;}
    public Activity Activity(string home)=>CurrentActivity;
    public Task Stop(CancellationToken ct){ct.ThrowIfCancellationRequested();IsRunning=false;return Task.CompletedTask;}
    public Task Launch(CancellationToken ct){ct.ThrowIfCancellationRequested();if(FailLaunches-->0)throw new IOException("Injected launch failure.");IsRunning=true;return Task.CompletedTask;}
}
sealed class FakeStartupEntry:IStartupEntry
{
    public string? Value {get;set;}
    public int Writes {get;private set;}
    public int Deletes {get;private set;}
    public string? Read()=>Value;
    public void Write(string command){Value=command;Writes++;}
    public void Delete(){Value=null;Deletes++;}
}
