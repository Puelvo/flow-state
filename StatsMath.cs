namespace FlowState;

/// <summary>Masaüstü ve mobilin ortak kullandığı seri / grafik hesapları.</summary>
public static class StatsMath
{
    /// <summary>Bir günün "çalışılmış" sayılması için gereken en az süre (sn).</summary>
    public const int MinDaySeconds = 60;

    public static int LongestStreak(IDictionary<DateTime, int> perDay)
    {
        var days = perDay.Where(kv => kv.Value >= MinDaySeconds).Select(kv => kv.Key.Date).OrderBy(d => d).ToList();
        int best = 0, run = 0;
        DateTime? prev = null;
        foreach (var d in days)
        {
            run = prev != null && (d - prev.Value).Days == 1 ? run + 1 : 1;
            best = Math.Max(best, run);
            prev = d;
        }
        return best;
    }

    public static int ActiveDays(IDictionary<DateTime, int> perDay) =>
        perDay.Count(kv => kv.Value >= MinDaySeconds);

    public static DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Son <paramref name="weeks"/> haftanın toplam saniyesi; sonuncusu bu hafta.</summary>
    public static int[] Weekly(IDictionary<DateTime, int> perDay, int weeks)
    {
        var res = new int[weeks];
        var thisMon = Monday(DateTime.Today);
        foreach (var kv in perDay)
        {
            int idx = weeks - 1 - (thisMon - Monday(kv.Key)).Days / 7;
            if (idx >= 0 && idx < weeks) res[idx] += kv.Value;
        }
        return res;
    }

    /// <summary>Isı haritası düzeyi: 0 (boş) … 4 (hedefin 1,5 katı ve üstü).</summary>
    public static int Level(int sec, double goalSeconds)
    {
        if (sec < MinDaySeconds) return 0;
        double r = sec / Math.Max(goalSeconds, 60);
        return r < 0.34 ? 1 : r < 0.67 ? 2 : r < 1.0 ? 3 : 4;
    }

    /// <summary>Günün 24 saatine dağılan çalışma süresi (sn). Seans, başlangıçtan itibaren saat saat bölünür.</summary>
    public static double[] Hourly(IEnumerable<(DateTime start, int seconds)> items)
    {
        var h = new double[24];
        foreach (var (start, seconds) in items)
        {
            var t = start;
            int rem = seconds;
            while (rem > 0)
            {
                int toEnd = 3600 - (t.Minute * 60 + t.Second);
                int take = Math.Min(rem, toEnd);
                h[t.Hour] += take;
                rem -= take;
                t = t.AddSeconds(take);
            }
        }
        return h;
    }
}

/// <summary>Çemberin geçişli rengi: tema renginden başlayıp tur boyunca tonu kaydırır.</summary>
public static class RingGradient
{
    /// <param name="t">0 = turun başı, 1 = turun sonu</param>
    public static (byte r, byte g, byte b) At(byte r, byte g, byte b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), d = max - min;
        double h = d == 0 ? 0 : max == rf ? 60 * (((gf - bf) / d % 6 + 6) % 6) : max == gf ? 60 * ((bf - rf) / d + 2) : 60 * ((rf - gf) / d + 4);
        double s = max == 0 ? 0 : d / max, v = max;
        if (s < 0.2) v *= 1 - 0.35 * t;          // gri / beyaz tema: ton yerine koyulaşma
        else h = (h + 80 * t) % 360;             // renkli tema: tonu kaydır
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r1, double g1, double b1) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x)
            : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return ((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }
}
