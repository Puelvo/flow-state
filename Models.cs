using System.IO;
using System.Text.Json;

namespace FlowState;

public class Course
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4F8CFF";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow; // senkron için (UTC)
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

    public void DeleteSession(Session s)
    {
        if (Sessions.Remove(s)) Tombstones.Add(new Tombstone { Id = s.Id, Kind = "session" });
    }

    public void DeleteSessions(Predicate<Session> match)
    {
        foreach (var s in Sessions.Where(x => match(x)).ToList()) DeleteSession(s);
    }

    public void DeleteCourse(Course c)
    {
        DeleteSessions(s => s.CourseId == c.Id);
        if (Courses.Remove(c)) Tombstones.Add(new Tombstone { Id = c.Id, Kind = "course" });
    }

    public ActiveSession? Active { get; set; }
    public double DailyGoalHours { get; set; } = 2;
    public string WidgetColor { get; set; } = "#5B8CFF"; // widget tema rengi (#RRGGBB)
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

    public static void Save(AppData data)
    {
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
