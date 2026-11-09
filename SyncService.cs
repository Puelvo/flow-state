using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowState;

// Bu cihazın senkron durumu (data.json'dan ayrı; cihaza özel).
public class SyncConfig
{
    public string Email { get; set; } = "";
    public string UserId { get; set; } = "";
    public string RefreshTokenProtected { get; set; } = ""; // DPAPI ile şifreli, base64
    public string PullCoursesAt { get; set; } = "1970-01-01T00:00:00+00:00"; // sunucunun synced_at değeri
    public string PullSessionsAt { get; set; } = "1970-01-01T00:00:00+00:00";
    public DateTime LastPushedUtc { get; set; } = DateTime.MinValue;
}

/// <summary>Oturum anahtarını platforma uygun korur (Windows: DPAPI, Android: uygulamaya özel alan).</summary>
public interface ISecretCodec
{
    string Protect(string plain);
    string Unprotect(string stored);
}

/// <summary>
/// Yerel-öncelikli senkron: uygulama her zaman yerel veriyle çalışır; bu servis arka planda
/// (çek → birleştir → gönder) yapar. Çakışmada kayıt başına "son yazan kazanır" (UpdatedAt).
/// </summary>
public sealed class SyncService
{
    const string BaseUrl = "https://lopwfjomaycoitgwwrwt.supabase.co";
    const string ApiKey = "sb_publishable_VmAehDrlNceCNp-P8QptVQ_Q-qkEZ1P"; // herkese açık anahtar; koruma RLS ile

    static string Dir => Store.Dir;
    static string CfgPath => Path.Combine(Dir, "sync.json");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    readonly AppData data;
    readonly SynchronizationContext ui;
    readonly ISecretCodec codec;
    readonly Action onDataChanged;            // UI thread: yerel veri senkronla değişti
    readonly Func<string, bool> courseInUse;  // UI thread: çalışan seans bu dersi kullanıyor mu
    readonly SemaphoreSlim gate = new(1, 1);
    readonly Timer debounce;
    readonly Timer poll;

    SyncConfig cfg = new();
    string accessToken = "";
    DateTime accessExpiresUtc = DateTime.MinValue;

    public string Status { get; private set; } = "Giriş yapılmadı";
    public bool SignedIn => cfg.RefreshTokenProtected.Length > 0;
    public string Email => cfg.Email;
    public event Action? StatusChanged;

    public SyncService(AppData data, SynchronizationContext ui, ISecretCodec codec,
                       Action onDataChanged, Func<string, bool> courseInUse)
    {
        this.data = data; this.ui = ui; this.codec = codec;
        this.onDataChanged = onDataChanged; this.courseInUse = courseInUse;
        LoadConfig();
        debounce = new Timer(_ => _ = SyncAsync(), null, Timeout.Infinite, Timeout.Infinite);
        poll = new Timer(_ => _ = SyncAsync(), null, Timeout.Infinite, Timeout.Infinite);
        if (SignedIn)
        {
            SetStatus("Senkron bekleniyor…");
            StartPolling();
            _ = SyncAsync();
        }
    }

    void StartPolling() => poll.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

    Task<T> OnUi<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        ui.Post(_ => { try { tcs.SetResult(f()); } catch (Exception e) { tcs.SetException(e); } }, null);
        return tcs.Task;
    }
    Task OnUi(Action a) => OnUi<object?>(() => { a(); return null; });

    /// <summary>Yerel veri değişti: kısa bir bekleme sonrası (kullanımı bölmeden) gönder.</summary>
    public void MarkDirty()
    {
        if (!SignedIn) return;
        debounce.Change(TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
    }

    // ---------- hesap ----------

    public Task<string?> SignInAsync(string email, string password) => AuthAsync("token?grant_type=password", email, password);
    public Task<string?> SignUpAsync(string email, string password) => AuthAsync("signup", email, password);

    async Task<string?> AuthAsync(string path, string email, string password)
    {
        try
        {
            var body = new JsonObject { ["email"] = email.Trim(), ["password"] = password };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/{path}")
            { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.Add("apikey", ApiKey);
            using var res = await Http.SendAsync(req);
            var json = await res.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json) as JsonObject;
            if (!res.IsSuccessStatusCode)
                return TranslateAuthError(node?["msg"]?.ToString() ?? node?["error_description"]?.ToString() ?? node?["message"]?.ToString() ?? json);
            if (node?["access_token"] is null)
                return "Kayıt oluşturuldu ama e-posta doğrulaması gerekiyor. Mailini onaylayıp giriş yap.";

            var newUser = node["user"]?["id"]?.ToString() ?? "";
            if (cfg.UserId != newUser) cfg = new SyncConfig(); // farklı hesap: senkron izlerini sıfırla
            cfg.Email = email.Trim();
            cfg.UserId = newUser;
            ApplyTokens(node);
            SaveConfig();
            StartPolling();
            SetStatus("Giriş yapıldı, senkronlanıyor…");
            _ = SyncAsync();
            return null;
        }
        catch (Exception ex) { return "Bağlanılamadı: " + ex.Message; }
    }

    static string TranslateAuthError(string m)
    {
        if (m.Contains("Invalid login", StringComparison.OrdinalIgnoreCase)) return "E-posta veya şifre hatalı.";
        if (m.Contains("already", StringComparison.OrdinalIgnoreCase)) return "Bu e-posta zaten kayıtlı. Giriş yapmayı dene.";
        if (m.Contains("at least", StringComparison.OrdinalIgnoreCase)) return "Şifre en az 6 karakter olmalı.";
        if (m.Contains("valid email", StringComparison.OrdinalIgnoreCase) || m.Contains("invalid format", StringComparison.OrdinalIgnoreCase)) return "Geçerli bir e-posta gir.";
        if (m.Contains("rate limit", StringComparison.OrdinalIgnoreCase)) return "Çok fazla deneme. Biraz bekleyip tekrar dene.";
        return m;
    }

    public void SignOut()
    {
        poll.Change(Timeout.Infinite, Timeout.Infinite); debounce.Change(Timeout.Infinite, Timeout.Infinite);
        cfg = new SyncConfig(); accessToken = ""; accessExpiresUtc = DateTime.MinValue;
        try { File.Delete(CfgPath); } catch { }
        SetStatus("Giriş yapılmadı");
    }

    void ApplyTokens(JsonObject node)
    {
        accessToken = node["access_token"]!.ToString();
        int exp = node["expires_in"]?.GetValue<int>() ?? 3600;
        accessExpiresUtc = DateTime.UtcNow.AddSeconds(exp - 60);
        if (node["refresh_token"]?.ToString() is { Length: > 0 } rt)
            cfg.RefreshTokenProtected = codec.Protect(rt);
    }

    async Task<bool> EnsureTokenAsync()
    {
        if (accessToken.Length > 0 && DateTime.UtcNow < accessExpiresUtc) return true;
        string rt;
        try { rt = codec.Unprotect(cfg.RefreshTokenProtected); }
        catch { SignOut(); return false; }

        var body = new JsonObject { ["refresh_token"] = rt };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/v1/token?grant_type=refresh_token")
        { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        req.Headers.Add("apikey", ApiKey);
        using var res = await Http.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            if ((int)res.StatusCode is 400 or 401 or 403) { SignOut(); SetStatus("Oturum süresi doldu, yeniden giriş yap."); }
            return false;
        }
        ApplyTokens((JsonObject)JsonNode.Parse(await res.Content.ReadAsStringAsync())!);
        SaveConfig();
        return true;
    }

    // ---------- senkron ----------

    public async Task SyncAsync()
    {
        if (!SignedIn || !await gate.WaitAsync(0)) return;
        try
        {
            SetStatus("Senkronlanıyor…");
            if (!await EnsureTokenAsync()) { if (SignedIn) SetStatus("Bağlantı yok, sonra tekrar denenecek"); return; }

            // 1) çek ve birleştir
            bool changed = false;
            var rows = await PullAsync("courses", cfg.PullCoursesAt);
            if (rows.items.Count > 0) { changed |= await OnUi(() => ApplyCourses(rows.items)); cfg.PullCoursesAt = rows.last; }
            var srows = await PullAsync("sessions", cfg.PullSessionsAt);
            if (srows.items.Count > 0) { changed |= await OnUi(() => ApplySessions(srows.items)); cfg.PullSessionsAt = srows.last; }
            changed |= await OnUi(MergeDuplicateCourses); // aynı adlı dersleri tek derse indir
            if (changed) await OnUi(onDataChanged);

            // 2) gönder (yerelde değişenler)
            var startedUtc = DateTime.UtcNow;
            var (courses, sessions, tombC, tombS) = await OnUi(() => Snapshot(cfg.LastPushedUtc));
            await PushUpsertAsync("courses", courses);
            await PushUpsertAsync("sessions", sessions);
            await PushDeletesAsync("courses", tombC);
            await PushDeletesAsync("sessions", tombS);
            cfg.LastPushedUtc = startedUtc;
            SaveConfig();
            SetStatus($"Senkron tamam · {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex) { SetStatus("Çevrimdışı / hata: " + ex.Message); }
        finally { gate.Release(); }
    }

    static DateTime U(DateTime d) => d.ToUniversalTime();
    static string Iso(DateTime d) => U(d).ToString("o");

    (List<JsonObject>, List<JsonObject>, List<string>, List<string>) Snapshot(DateTime since)
    {
        var uid = cfg.UserId;
        var c = data.Courses.Where(x => U(x.UpdatedAt) > since).Select(x => new JsonObject
        {
            ["user_id"] = uid, ["id"] = x.Id, ["name"] = x.Name, ["color"] = x.Color,
            ["updated_at"] = Iso(x.UpdatedAt), ["deleted"] = false
        }).ToList();
        var s = data.Sessions.Where(x => U(x.UpdatedAt) > since).Select(x => new JsonObject
        {
            ["user_id"] = uid, ["id"] = x.Id, ["course_id"] = x.CourseId, ["started_at"] = Iso(x.Start),
            ["seconds"] = x.Seconds, ["updated_at"] = Iso(x.UpdatedAt), ["deleted"] = false
        }).ToList();
        var tc = data.Tombstones.Where(t => t.Kind == "course" && U(t.DeletedAt) > since).Select(t => t.Id).ToList();
        var ts = data.Tombstones.Where(t => t.Kind == "session" && U(t.DeletedAt) > since).Select(t => t.Id).ToList();
        return (c, s, tc, ts);
    }

    async Task<(List<JsonObject> items, string last)> PullAsync(string table, string since)
    {
        var all = new List<JsonObject>();
        string last = since;
        while (true)
        {
            var url = $"{BaseUrl}/rest/v1/{table}?select=*&synced_at=gt.{Uri.EscapeDataString(last)}&order=synced_at.asc&limit=1000";
            var arr = JsonNode.Parse(await SendAsync(HttpMethod.Get, url, null))!.AsArray();
            foreach (var n in arr) all.Add((JsonObject)n!);
            if (arr.Count > 0) last = arr[^1]!["synced_at"]!.ToString();
            if (arr.Count < 1000) break;
        }
        return (all, last);
    }

    async Task PushUpsertAsync(string table, List<JsonObject> items)
    {
        for (int i = 0; i < items.Count; i += 500)
        {
            var chunk = new JsonArray(items.Skip(i).Take(500).Select(x => (JsonNode)JsonNode.Parse(x.ToJsonString())!).ToArray());
            await SendAsync(HttpMethod.Post, $"{BaseUrl}/rest/v1/{table}?on_conflict=user_id,id", chunk.ToJsonString(),
                "resolution=merge-duplicates,return=minimal");
        }
    }

    async Task PushDeletesAsync(string table, List<string> ids)
    {
        for (int i = 0; i < ids.Count; i += 100)
        {
            var list = string.Join(",", ids.Skip(i).Take(100));
            var body = new JsonObject { ["deleted"] = true, ["updated_at"] = Iso(DateTime.UtcNow) };
            await SendAsync(new HttpMethod("PATCH"), $"{BaseUrl}/rest/v1/{table}?id=in.({list})", body.ToJsonString(), "return=minimal");
        }
    }

    async Task<string> SendAsync(HttpMethod method, string url, string? body, string? prefer = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Add("apikey", ApiKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (prefer != null) req.Headers.Add("Prefer", prefer);
        if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode}: {text}");
        return text;
    }

    // ---------- birleştirme (UI thread) ----------

    static DateTime ParseUtc(string s) => DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;

    bool ApplyCourses(List<JsonObject> rows)
    {
        bool changed = false;
        foreach (var r in rows)
        {
            var id = r["id"]!.ToString();
            var upd = ParseUtc(r["updated_at"]!.ToString());
            var local = data.Courses.FirstOrDefault(c => c.Id == id);
            var tomb = data.Tombstones.FirstOrDefault(t => t.Kind == "course" && t.Id == id);
            if (tomb != null)
            {
                if (U(tomb.DeletedAt) >= upd) continue; // yerel silme daha yeni; gönderimde buluta yansır
                data.Tombstones.Remove(tomb);          // uzak güncelleme daha yeni: geri getir
            }
            if (r["deleted"]!.GetValue<bool>())
            {
                if (local != null && !courseInUse(id))
                {
                    // Başka cihaz bunu aynı adlı derse birleştirdiyse yerel seanslar kaybolmasın, taşınsın.
                    var twin = data.Courses.FirstOrDefault(c => c.Id != id && SameName(c, local));
                    if (twin != null)
                        foreach (var s in data.Sessions.Where(s => s.CourseId == id))
                        { s.CourseId = twin.Id; s.UpdatedAt = DateTime.UtcNow; }
                    data.Sessions.RemoveAll(s => s.CourseId == id);
                    data.Courses.Remove(local);
                    changed = true;
                }
            }
            else if (local == null)
            {
                data.Courses.Add(new Course { Id = id, Name = r["name"]!.ToString(), Color = r["color"]!.ToString(), UpdatedAt = upd });
                changed = true;
            }
            else if (upd > U(local.UpdatedAt))
            {
                local.Name = r["name"]!.ToString(); local.Color = r["color"]!.ToString(); local.UpdatedAt = upd;
                changed = true;
            }
        }
        return changed;
    }

    static bool SameName(Course a, Course b) =>
        string.Equals(a.Name.Trim(), b.Name.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// İki cihazda aynı adla ayrı eklenen dersleri birleştirir. Kazanan deterministik (en küçük Id),
    /// böylece iki cihaz da aynı sonuca varır. Kaybedenin seansları kazanana taşınır, kaybeden silinir.
    /// Çalışan seansın kullandığı ders şimdilik atlanır; seans bitince sonraki senkronda birleşir.
    /// </summary>
    bool MergeDuplicateCourses()
    {
        bool changed = false;
        foreach (var group in data.Courses.Where(c => c.Name.Trim().Length > 0)
                     .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList())
        {
            var winner = group.OrderBy(c => c.Id, StringComparer.Ordinal).First();
            foreach (var loser in group.Where(c => c != winner).ToList())
            {
                if (courseInUse(loser.Id)) continue;
                foreach (var s in data.Sessions.Where(s => s.CourseId == loser.Id))
                { s.CourseId = winner.Id; s.UpdatedAt = DateTime.UtcNow; }
                data.DeleteCourse(loser); // seansları zaten taşındı; ders için silme izi bırakır
                changed = true;
            }
        }
        if (changed) Store.Save(data);
        return changed;
    }

    bool ApplySessions(List<JsonObject> rows)
    {
        bool changed = false;
        foreach (var r in rows)
        {
            var id = r["id"]!.ToString();
            var upd = ParseUtc(r["updated_at"]!.ToString());
            var local = data.Sessions.FirstOrDefault(s => s.Id == id);
            var tomb = data.Tombstones.FirstOrDefault(t => t.Kind == "session" && t.Id == id);
            if (tomb != null)
            {
                if (U(tomb.DeletedAt) >= upd) continue;
                data.Tombstones.Remove(tomb);
            }
            if (r["deleted"]!.GetValue<bool>())
            {
                if (local != null) { data.Sessions.Remove(local); changed = true; }
            }
            else
            {
                var courseId = r["course_id"]!.ToString();
                if (!data.Courses.Any(c => c.Id == courseId)) continue; // ders (henüz) yok/silinmiş: yetim kayıt ekleme
                var start = ParseUtc(r["started_at"]!.ToString()).ToLocalTime();
                int secs = r["seconds"]!.GetValue<int>();
                if (local == null)
                {
                    data.Sessions.Add(new Session { Id = id, CourseId = courseId, Start = start, Seconds = secs, UpdatedAt = upd });
                    changed = true;
                }
                else if (upd > U(local.UpdatedAt))
                {
                    local.CourseId = courseId; local.Start = start; local.Seconds = secs; local.UpdatedAt = upd;
                    changed = true;
                }
            }
        }
        return changed;
    }

    // ---------- yardımcılar ----------

    void SetStatus(string s)
    {
        Status = s;
        ui.Post(_ => StatusChanged?.Invoke(), null);
    }

    void LoadConfig()
    {
        try { if (File.Exists(CfgPath)) cfg = JsonSerializer.Deserialize<SyncConfig>(File.ReadAllText(CfgPath)) ?? new(); }
        catch { cfg = new(); }
    }

    void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = CfgPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg));
            File.Move(tmp, CfgPath, true);
        }
        catch { }
    }
}
