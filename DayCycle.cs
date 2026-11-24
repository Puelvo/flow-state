namespace FlowState;

/// <summary>Bahçe teması: otomatik (günün saatine göre) ya da sabit bir vakit.</summary>
public enum GardenTheme { Auto, Dawn, Day, Sunset, Night }

/// <summary>Bir vaktin renkleri. Renkler 0xAARRGGBB; oranlar 0..1.</summary>
public readonly record struct SkyPalette(
    uint SkyTop, uint SkyBottom,      // gökyüzü geçişi (üst → ufuk)
    uint Light,                       // yere (tepeler, çimen, süsler) vuran ışık: renk ve güç; görsellerle çarpılır
    uint Ray,                         // güneş saatin ilerleme halkası
    uint DiscA, uint DiscB,           // güneş/ay diskinin ortası ve kenarı
    uint Glow,                        // diskin çevresindeki hâle
    double Stars, double Fireflies, double Clouds, double Moon);

/// <summary>
/// Gün döngüsü: vakitlerin renk paletleri ve saate göre yumuşak geçiş. Arayüzden bağımsızdır.
/// Otomatik temada palet, saatin düştüğü iki anahtar vakit arasında doğrusal karışımdır.
/// </summary>
public static class DayCycle
{
    public static readonly SkyPalette Night = new(
        0xFF0E1540, 0xFF34458F, 0xA02E6FC0, 0xFFC9D3FF, 0xFFF4F1E8, 0xFFEAE6DA, 0x38C9D3FF, 1, 1, 0.72, 1);
    public static readonly SkyPalette Dawn = new(
        0xFF8FA8E8, 0xFFFFC6B4, 0x1FFF9C8A, 0xFFFF8FA3, 0xFFFFF2D8, 0xFFFFC796, 0x66FFB8A0, 0.25, 0, 0.9, 0);
    public static readonly SkyPalette Day = new(
        0xFF7EC4FF, 0xFFD9F1FF, 0x00FFFFFF, 0xFFFFAA2E, 0xFFFFF7CC, 0xFFFFD45A, 0x77FFE38A, 0, 0, 0.95, 0);
    public static readonly SkyPalette Afternoon = new(
        0xFF86BEF2, 0xFFFFE2B0, 0x1AFFB347, 0xFFFF9A30, 0xFFFFF1C2, 0xFFFFC048, 0x77FFD080, 0, 0, 0.9, 0);
    public static readonly SkyPalette Sunset = new(
        0xFF5B4FA8, 0xFFFF9466, 0x33FF6F4A, 0xFFFF7A5C, 0xFFFFE3B4, 0xFFFF9A5A, 0x77FF9A6A, 0.35, 0.3, 0.8, 0);

    // Otomatik tema: (saat, palet). Aradaki saatlerde iki palet karışır.
    static readonly (double Hour, SkyPalette P)[] Keys =
    {
        (0, Night), (5.0, Night), (6.5, Dawn), (8.5, Day), (14.5, Day), (16.5, Afternoon),
        (18.3, Sunset), (19.6, Sunset), (21.0, Night), (24, Night)
    };

    public static SkyPalette At(DateTime now, GardenTheme theme) => theme switch
    {
        GardenTheme.Dawn => Dawn,
        GardenTheme.Day => Day,
        GardenTheme.Sunset => Sunset,
        GardenTheme.Night => Night,
        _ => Auto(now.TimeOfDay.TotalHours),
    };

    public static SkyPalette Auto(double hour)
    {
        for (int i = 0; i < Keys.Length - 1; i++)
        {
            var (h0, p0) = Keys[i];
            var (h1, p1) = Keys[i + 1];
            if (hour < h0 || hour > h1) continue;
            double t = h1 <= h0 ? 0 : (hour - h0) / (h1 - h0);
            return Mix(p0, p1, t);
        }
        return Night;
    }

    /// <summary>Gece mi (hayvanlar uykucu, ay görünür)? Karışık vakitlerde ay oranına bakılır.</summary>
    public static bool IsNight(SkyPalette p) => p.Moon >= 0.5;

    public static SkyPalette Mix(SkyPalette a, SkyPalette b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new SkyPalette(
            Lerp(a.SkyTop, b.SkyTop, t), Lerp(a.SkyBottom, b.SkyBottom, t), Lerp(a.Light, b.Light, t), Lerp(a.Ray, b.Ray, t),
            Lerp(a.DiscA, b.DiscA, t), Lerp(a.DiscB, b.DiscB, t), Lerp(a.Glow, b.Glow, t),
            a.Stars + (b.Stars - a.Stars) * t, a.Fireflies + (b.Fireflies - a.Fireflies) * t,
            a.Clouds + (b.Clouds - a.Clouds) * t, a.Moon + (b.Moon - a.Moon) * t);
    }

    static uint Lerp(uint a, uint b, double t)
    {
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) + ((((b >> shift) & 0xFF) - (double)((a >> shift) & 0xFF)) * t)) & 0xFF;
        return (Ch(24) << 24) | (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    public static GardenTheme Parse(string? s) => s switch
    {
        "dawn" => GardenTheme.Dawn, "day" => GardenTheme.Day, "sunset" => GardenTheme.Sunset, "night" => GardenTheme.Night, _ => GardenTheme.Auto
    };

    public static string Key(GardenTheme t) => t switch
    {
        GardenTheme.Dawn => "dawn", GardenTheme.Day => "day", GardenTheme.Sunset => "sunset", GardenTheme.Night => "night", _ => "auto"
    };
}
