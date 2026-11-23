using System.IO;
using System.Text.Json;

namespace FlowState;

public class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4F8CFF";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // senkron için (UTC)

    /// <summary>"Genel": derse bağlı olmayan çalışmalar için sabit kimlikli, silinemeyen ders (her cihazda aynı kimlik).</summary>
    public const string GeneralId = "00000000000000000000000000000001";
}

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N"); // senkron için kalıcı kimlik
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;     // senkron için (UTC)
    public string CourseId { get; set; } = "";
    public DateTime Start { get; set; }
    public int Seconds { get; set; }
}

public class ActiveSession
{
    public string CourseId { get; set; } = "";
    public DateTime Start { get; set; }
    public int Seconds { get; set; }
}

// Silinen kaydın izi: senkronda diğer cihaz kaydı geri getirmesin diye saklanır.
public class Tombstone
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = ""; // "course" | "session"
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
}

public class AppData
{
    public List<Course> Courses { get; set; } = new();
    public List<Session> Sessions { get; set; } = new();
    public List<Tombstone> Tombstones { get; set; } = new();
    public List<OwnedAnimal> OwnedAnimals { get; set; } = new(); // hayvanat bahçesi satın alımları

    public void DeleteSession(Session s)
    {
        if (Sessions.Remove(s)) Tombstones.Add(new Tombstone { Id = s.Id, Kind = "session" });
    }

    public void DeleteSessions(Predicate<Session> match)
    {
        foreach (var s in Sessions.Where(x => match(x)).ToList()) DeleteSession(s);
    }

    /// <summary>Derssiz çalışma için "Genel" dersi yoksa başa ekler.</summary>
    public void EnsureGeneral()
    {
        if (Courses.Any(c => c.Id == Course.GeneralId)) return;
        Courses.Insert(0, new Course { Id = Course.GeneralId, Name = "Genel", Color = "#8791B5" });
    }

    public void DeleteCourse(Course c)
    {
        if (c.Id == Course.GeneralId) return;
        DeleteSessions(s => s.CourseId == c.Id);
        if (Courses.Remove(c)) Tombstones.Add(new Tombstone { Id = c.Id, Kind = "course" });
    }

    public LiveState Live { get; set; } = new();

    /// <summary>Paylaşılan canlı seans durumunu şimdi güncellenmiş olarak yazar (senkronda en yeni kazanır).</summary>
    public void SetLive(bool active, string runId, string courseId, DateTime start, double accumulated, DateTime? runningSince)
    {
        Live = new LiveState
        {
            Active = active, RunId = runId, CourseId = courseId, Start = start,
            Accumulated = accumulated, RunningSince = runningSince, UpdatedAt = DateTime.UtcNow
        };
    }

    public ActiveSession? Active { get; set; }
    public double DailyGoalHours { get; set; } = 2;
    public string WidgetColor { get; set; } = "#5B8CFF"; // widget tema rengi (#RRGGBB)

    // Çalış modu (yalnız bu cihazda): kronometre ya da geri sayım.
    public bool Countdown { get; set; }
    public int CountdownMinutes { get; set; } = 25;
    // Geri sayımla başlatılmış süren seansın kimliği ve hedefi (yeniden açılışta hedef kaybolmasın).
    public string CountdownRunId { get; set; } = "";
    public int CountdownSeconds { get; set; }
}

public static class Store
{
    /// <summary>Veri klasörü. Windows'ta %APPDATA%\FlowState; Android uygulaması başlangıçta değiştirir.</summary>
    public static string Dir { get; set; } = DefaultDir();

    static string DefaultDir()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(root, "FlowState");
        var legacy = Path.Combine(root, "StudyAssistant"); // uygulamanın eski adı
        try
        {
            // Tek seferlik taşıma: eski ad altındaki kayıtlar ve senkron girişi korunur.
            if (!Directory.Exists(dir) && Directory.Exists(legacy)) Directory.Move(legacy, dir);
        }
        catch { /* taşınamazsa yeni klasörle devam; eski veri yerinde kalır */ }
        return dir;
    }
    static string FilePath => Path.Combine(Dir, "data.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static AppData Load()
    {
        var d = LoadCore();
        d.EnsureGeneral();
        return d;
    }

    static AppData LoadCore()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppData();
            var json = File.ReadAllText(FilePath);
            var d = JsonSerializer.Deserialize<AppData>(json) ?? new AppData();
            // Tek seferlik geçiş: eski dosyada Id/UpdatedAt yok. Id'ler yeni üretildi; kalıcı olsun diye
            // hemen kaydedilir. Seansların UpdatedAt'i bitiş anına çekilir.
            if (!json.Contains("\"Tombstones\""))
            {
                foreach (var s in d.Sessions) s.UpdatedAt = s.Start.ToUniversalTime().AddSeconds(s.Seconds);
                Save(d);
            }
            return d;
        }
        catch
        {
            try { File.Move(FilePath, Path.Combine(Dir, $"data.bozuk-{DateTime.Now:yyyyMMdd-HHmmss}.json")); } catch { }
            return new AppData();
        }
    }

    /// <summary>true iken Save hiçbir şey yazmaz (ekran görüntüsü modu örnek veriyi diske dökmesin).</summary>
    public static bool ReadOnly { get; set; }

    public static void Save(AppData data)
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Opt));
            File.Move(tmp, FilePath, true);
        }
        catch { /* disk hatasında uygulama çalışmaya devam etsin */ }
    }
}

/// <summary>
/// Cihazlar arası paylaşılan "şu an çalışılan seans" durumu. Zaman damgalarından hesaplanır
/// (geçen süre = Accumulated + şimdi − RunningSince), bu yüzden her cihaz aynı süreyi görür.
/// En yeni UpdatedAt kazanır.
/// </summary>
public class LiveState
{
    public bool Active { get; set; }
    public string RunId { get; set; } = "";       // seansın kalıcı kimliği; bitince Session.Id olur
    public string CourseId { get; set; } = "";
    public DateTime Start { get; set; }           // yerel saat (Session.Start gibi)
    public double Accumulated { get; set; }       // duraklatmalardan önce biriken saniye
    public DateTime? RunningSince { get; set; }   // UTC; null = duraklatılmış
    public DateTime UpdatedAt { get; set; } = DateTime.MinValue; // UTC; MinValue = hiç ayarlanmadı

    public double Elapsed => Accumulated + (RunningSince is { } r ? Math.Max(0, (DateTime.UtcNow - r).TotalSeconds) : 0);
}

/// <summary>Stopwatch yerine zaman damgalı sayaç: başka cihazdan gelen durumu doğrudan alabilir.</summary>
public sealed class ClockTimer
{
    double acc;
    DateTime? since; // UTC

    public bool IsRunning => since != null;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(acc + (since is { } s ? Math.Max(0, (DateTime.UtcNow - s).TotalSeconds) : 0));
    public double Accumulated => acc;
    public DateTime? RunningSince => since;

    public void Start() { since ??= DateTime.UtcNow; }
    public void Stop() { acc = Elapsed.TotalSeconds; since = null; }
    public void Reset() { acc = 0; since = null; }
    public void Restart() { acc = 0; since = DateTime.UtcNow; }
    public void Set(double accumulated, DateTime? runningSince) { acc = accumulated; since = runningSince; }
}
