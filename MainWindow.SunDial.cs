using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowState;

// Güneş saatin yüzü: sahneyle aynı dilde piksel bir güneş (gece ay) ve çevresinde 60 ışın.
// Kronometrede her ışın bir dakikadır, her saat bir tam tur; geri sayımda 60 ışın hedefi paylaşır.
// Her beşinci ışın iri bir üçgen, aradakiler küçük kare noktadır.
// Izgara DialN × DialN hücre; kenarlar her ölçekte temiz kalsın diye hücreler DialUp kat büyütülüp öyle çizilir.
public partial class MainWindow
{
    const int DialN = 106, DialUp = 4;
    const double DiscR = 39, RayIn = 42.6, RayLong = 52.6, DotAt = 45.2;

    string raysKey = "", discKey = "";

    /// <summary>Işınları oturumun ilerlemesine göre günceller (yalnızca bir piksel değişince yeniden çizilir).</summary>
    void UpdateRing()
    {
        double t = active ? sw.Elapsed.TotalSeconds : 0;
        double frac = countdownTarget > 0 ? Math.Min(t / countdownTarget, 1) : t % 3600 / 3600;
        int steps = (int)(frac * 60 * 10);                         // ışın başına 10 adım
        var key = $"{active}|{steps}|{sunPalette.Ray:X}|{sunPalette.Moon:F2}";
        if (key == raysKey) return;
        raysKey = key;
        RingRays.Source = RaysBitmap(active, steps / 10.0);
    }

    /// <summary>Güneş/ay diskini vaktin renkleriyle çizer.</summary>
    void UpdateDisc()
    {
        var key = $"{sunPalette.DiscA:X}{sunPalette.DiscB:X}{sunPalette.Ray:X}{sunPalette.Moon:F2}";
        if (key == discKey) return;
        discKey = key;
        RingFace.Source = DiscBitmap();
    }

    /// <param name="running">Oturum var mı (yoksa ışınlar dinlenir).</param>
    /// <param name="lit">Yanan ışın sayısı (kesirli: sıradaki ışın içeriden dışarı dolar).</param>
    BitmapSource RaysBitmap(bool running, double lit)
    {
        var ray = RingColor();
        double moon = Math.Clamp(sunPalette.Moon, 0, 1);
        // Tur ilerledikçe ışınlar gündüz mercana, gece maviye döner.
        var end = Mix(Color.FromRgb(0xFF, 0x5F, 0x56), Color.FromRgb(0x8C, 0x9E, 0xFF), moon);
        var resting = Mix(ray, Colors.White, 0.25 + 0.15 * moon);
        var px = new uint[DialN * DialN];
        Color Tone(int i, bool on) =>
            !running ? Color.FromArgb(0xE8, resting.R, resting.G, resting.B)
            : on ? Mix(ray, end, i / 59.0)
            : Color.FromArgb((byte)(0x90 * (1 - 0.45 * moon)), 0xFF, 0xFF, 0xFF);

        for (int i = 0; i < 60; i++)
        {
            double a = i * Math.PI / 30, ux = Math.Sin(a), uy = -Math.Cos(a), fill = Math.Clamp(lit - i, 0, 1);
            if (i % 5 != 0)
            {
                // Dakika noktası: 3×3 hücre, ızgaraya oturur; dakikası dolunca yanar.
                int ox = (int)Math.Floor(DialN / 2.0 + ux * DotAt) - 1, oy = (int)Math.Floor(DialN / 2.0 + uy * DotAt) - 1;
                uint c = Argb(Tone(i, fill >= 1));
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++) px[(oy + y) * DialN + ox + x] = c;
                continue;
            }
            // Üçgen ışın: dipten uca incelir, dakikası boyunca dipten uca dolar; ucu parlak.
            for (int y = 0; y < DialN; y++)
            for (int x = 0; x < DialN; x++)
            {
                double dx = x + 0.5 - DialN / 2.0, dy = y + 0.5 - DialN / 2.0;
                double along = dx * ux + dy * uy, across = Math.Abs(dx * uy - dy * ux);
                if (along < RayIn || along > RayLong) continue;
                double k = (along - RayIn) / (RayLong - RayIn);
                if (across > 3.6 - 3.0 * k) continue;
                bool on = fill > 0 && k <= fill;
                var c = Tone(i, on);
                if (running && on && k > 0.72) c = Mix(c, Colors.White, 0.28);
                px[y * DialN + x] = Argb(c);
            }
        }
        return Upscale(px);
    }

    BitmapSource DiscBitmap()
    {
        double moon = Math.Clamp(sunPalette.Moon, 0, 1);
        Color A = C(sunPalette.DiscA), B = C(sunPalette.DiscB), ray = RingColor();
        var rim = Mix(Mix(B, ray, 0.75), Color.FromRgb(0xC4, 0xC1, 0xBC), moon);
        var shade = Mix(Mix(B, ray, 0.35), Color.FromRgb(0xD3, 0xD2, 0xD2), moon);
        double craters = Math.Clamp((moon - 0.15) / 0.7, 0, 1);      // geçişte yarı saydam, gündüz yok
        var px = new uint[DialN * DialN];
        for (int y = 0; y < DialN; y++)
        for (int x = 0; x < DialN; x++)
        {
            double dx = x + 0.5 - DialN / 2.0, dy = y + 0.5 - DialN / 2.0, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > DiscR) continue;
            int bayer = ((x & 1) * 2 + (y & 1) * 3) % 4;              // 2×2 titreme: bantlar piksel piksel karışır
            // Işık sol üstten: beş bant.
            double d = Math.Sqrt((dx + 9) * (dx + 9) + (dy + 11) * (dy + 11)) / (DiscR * 1.32);
            double band = Math.Floor(Math.Clamp(d * 5 + (bayer - 1.5) * 0.10, 0, 4.99)) / 4;
            var c = Mix(A, B, band);
            // Sağ alt kenarda hilal gölge, çevrede ince kenar.
            double sx = dx + 5, sy = dy + 5;
            if (Math.Sqrt(sx * sx + sy * sy) > DiscR - 0.5 + (bayer - 1.5) * 0.5) c = Mix(c, shade, 0.85);
            if (craters > 0)
                foreach (var (cx, cy, cr) in Craters)
                {
                    double kx = dx - (cx - 0.5) * 2 * DiscR, ky = dy - (cy - 0.5) * 2 * DiscR, kr = cr * 2 * DiscR;
                    if (Math.Sqrt(kx * kx + ky * ky) > kr) continue;
                    bool bowl = Math.Sqrt((kx + 0.7) * (kx + 0.7) + (ky + 0.9) * (ky + 0.9)) < kr - 0.9;
                    c = Mix(c, bowl ? Color.FromRgb(0xD0, 0xCD, 0xC5) : Color.FromRgb(0xDE, 0xDB, 0xD2), craters);
                }
            // Sol üstte küçük parıltı (gündüz).
            double deg = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (r is > 32.5 and < 35.2 && deg is > -152 and < -112 && deg is not (> -124 and < -120)) c = Mix(c, Colors.White, 0.85 * (1 - moon));
            if (r > DiscR - 1.15) c = rim;
            px[y * DialN + x] = Argb(c);
        }
        return Upscale(px);
    }

    /// <summary>Ayın kraterleri: (x, y, yarıçap) diskin oranı olarak; yazının arkası boş kalır.</summary>
    static readonly (double X, double Y, double R)[] Craters =
        { (0.27, 0.27, 0.075), (0.72, 0.20, 0.05), (0.81, 0.72, 0.085), (0.30, 0.80, 0.06), (0.55, 0.88, 0.045), (0.11, 0.60, 0.035), (0.90, 0.42, 0.03) };

    static uint Argb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    static BitmapSource Upscale(uint[] px)
    {
        const int W = DialN * DialUp;
        var big = new uint[W * W];
        for (int y = 0; y < W; y++)
        for (int x = 0; x < W; x++) big[y * W + x] = px[y / DialUp * DialN + x / DialUp];
        var bmp = BitmapSource.Create(W, W, 96, 96, PixelFormats.Bgra32, null, big, W * 4);
        bmp.Freeze();
        return bmp;
    }
}
