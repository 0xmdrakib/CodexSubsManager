using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexAccounts.Core;

public sealed class ManagerState
{
    public int Version { get; set; } = 1;
    public bool Attached { get; set; } = true;
    public string? ActiveId { get; set; }
    public List<Account> Accounts { get; set; } = [];
    public Preferences Preferences { get; set; } = new();
    public DateTimeOffset? LastSwitch { get; set; }
}

public sealed class Preferences
{
    public bool AutoSwitch { get; set; }
    public int ThresholdPercent { get; set; }
    public int RefreshSeconds { get; set; } = 30;
    public int RefreshPolicyVersion { get; set; }
    public int CooldownSeconds { get; set; } = 120;
    public bool StartWithWindows { get; set; } = true;
    public int StartupPolicyVersion { get; set; }
    public bool Hotkey { get; set; } = true;
    public bool LightTheme { get; set; } = true;
    public string? CliPath { get; set; }
    public string? DesktopPath { get; set; }
    public string CodexHome { get; set; } = Environment.GetEnvironmentVariable("CODEX_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
}

public static class RefreshPolicy
{
    public static bool Apply(Preferences preferences)
    {
        if (preferences.RefreshPolicyVersion >= 1) return false;
        // Replace the old default once; retain customized intervals and all later choices.
        if (preferences.RefreshSeconds == 120) preferences.RefreshSeconds = 30;
        preferences.RefreshPolicyVersion = 1;
        return true;
    }
}

public sealed record AccountUsage(Usage Usage, string? Plan = null, string? Email = null);

public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Identity { get; set; } = "";
    public string Name { get; set; } = "Account";
    public string Email { get; set; } = "";
    public string Plan { get; set; } = "unknown";
    public bool Enabled { get; set; } = true;
    public Usage? Usage { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset Added { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? Verified { get; set; }
}

public sealed class Usage
{
    public DateTimeOffset Captured { get; set; } = DateTimeOffset.UtcNow;
    public bool? OrdinaryUsageAllowed { get; set; }
    public List<QuotaWindow> Windows { get; set; } = [];
    public bool Reached { get; set; }
    public string? Plan { get; set; }
    public bool Fresh(DateTimeOffset now) => Captured <= now.AddMinutes(1) && now - Captured < TimeSpan.FromMinutes(5);
    [JsonIgnore] public List<QuotaWindow> CodingWindows => Windows.Where(w => w.Bucket == "codex").ToList();
    [JsonIgnore] public double? Remaining => CodingWindows.Count == 0 ? null : CodingWindows.Min(x => x.Remaining);

    public static Usage Parse(JsonElement root)
    {
        var usage = new Usage();
        if (root.TryGetProperty("ordinaryUsageAllowed", out var allowed) && allowed.ValueKind is JsonValueKind.True or JsonValueKind.False)
            usage.OrdinaryUsageAllowed = allowed.GetBoolean();
        if (root.TryGetProperty("rateLimitsByLimitId", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject()) usage.AddBucket(entry.Name, entry.Value);
        }
        else if (root.TryGetProperty("rateLimits", out var bucket) && bucket.ValueKind == JsonValueKind.Object)
            usage.AddBucket("codex", bucket);
        return usage;
    }

    void AddBucket(string id, JsonElement bucket)
    {
        Plan ??= JsonText.Get(bucket, "planType");
        if (id == "codex" && bucket.TryGetProperty("rateLimitReachedType", out var reached) && reached.ValueKind == JsonValueKind.String)
            Reached = true;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!w.TryGetProperty("usedPercent", out var pct) || pct.ValueKind != JsonValueKind.Number || !pct.TryGetDouble(out var used) || !double.IsFinite(used)) continue;
            int? duration = w.TryGetProperty("windowDurationMins", out var mins) && mins.ValueKind == JsonValueKind.Number && mins.TryGetInt32(out var m) ? m : null;
            DateTimeOffset? reset = null;
            if (w.TryGetProperty("resetsAt", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var seconds))
                try { reset = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { }
            Windows.Add(new QuotaWindow { Bucket = id, Minutes = duration, Used = Math.Clamp(used, 0, 100), Resets = reset });
        }
    }
}

public sealed class QuotaWindow
{
    public string Bucket { get; set; } = "codex";
    public int? Minutes { get; set; }
    public double Used { get; set; }
    public DateTimeOffset? Resets { get; set; }
    [JsonIgnore] public double Remaining => 100 - Used;
    [JsonIgnore] public string Label => Minutes switch
    {
        300 => "5 hours", 10080 => "Weekly", >= 1440 => $"{Minutes / 1440} days",
        >= 60 => $"{Minutes / 60} hours", > 0 => $"{Minutes} minutes", _ => "Usage window"
    };
}

public static class Selection
{
    public static bool Exhausted(Usage? usage, int threshold, DateTimeOffset now)
    {
        if (usage is null || !usage.Fresh(now)) return false;
        return usage.OrdinaryUsageAllowed == false || usage.Reached || usage.CodingWindows.Any(w => w.Remaining <= threshold);
    }

    public static Account? Next(ManagerState state, DateTimeOffset now)
    {
        var active = state.Accounts.Find(a => a.Id == state.ActiveId);
        if (!state.Attached || !state.Preferences.AutoSwitch || active is null ||
            !Exhausted(active.Usage, state.Preferences.ThresholdPercent, now) ||
            (state.LastSwitch is { } last && now - last < TimeSpan.FromSeconds(state.Preferences.CooldownSeconds))) return null;
        return state.Accounts.FirstOrDefault(a => a.Id != active.Id && a.Enabled && a.Status is null &&
            a.Usage is { } u && u.Fresh(now) && u.OrdinaryUsageAllowed != false && !u.Reached &&
            (u.CodingWindows.Count > 0 || u.OrdinaryUsageAllowed == true) &&
            u.CodingWindows.All(w => w.Remaining > state.Preferences.ThresholdPercent && (w.Resets is null || w.Resets > now)));
    }
}

public sealed record AuthInfo(string Identity, string Email, string Plan, string WorkspaceId)
{
    public static AuthInfo Parse(byte[] auth)
    {
        if (auth.Length > 1024 * 1024) throw new InvalidDataException("This credential file is too large.");
        using var doc = JsonDocument.Parse(auth);
        var root = doc.RootElement;
        if (JsonText.Get(root, "OPENAI_API_KEY") is { Length: > 0 } || JsonText.Get(root, "auth_mode") == "apikey")
            throw new InvalidDataException("Use a ChatGPT subscription sign-in. API-key authorization is kept separate.");
        if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object ||
            JsonText.Get(tokens, "access_token") is not { Length: > 0 })
            throw new InvalidDataException("A ChatGPT Codex sign-in is required. API-key profiles are not subscription accounts.");
        var account = JsonText.Get(tokens, "account_id");
        var jwt = JsonText.Get(tokens, "id_token") ?? JsonText.Get(tokens, "access_token")!;
        JsonDocument? claims = null;
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length >= 2)
            {
                var b64 = parts[1].Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight((b64.Length + 3) / 4 * 4, '=');
                claims = JsonDocument.Parse(Convert.FromBase64String(b64));
            }
            var c = claims?.RootElement ?? default;
            var email = JsonText.Get(c, "email") ?? "";
            var subject = JsonText.Get(c, "sub") ?? email;
            var details = c.ValueKind == JsonValueKind.Object && c.TryGetProperty("https://api.openai.com/auth", out var d) ? d : default;
            account ??= JsonText.Get(details, "chatgpt_account_id");
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(subject))
                throw new InvalidDataException("The sign-in does not contain a stable account identity. Sign in again with Codex.");
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject + "|" + account))).ToLowerInvariant();
            return new AuthInfo(identity, email, JsonText.Get(details, "chatgpt_plan_type") ?? "unknown", account);
        }
        catch (FormatException) { throw new InvalidDataException("This is not a valid Codex sign-in file."); }
        finally { claims?.Dispose(); }
    }
}

public static class JsonText
{
    public static string? Get(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
