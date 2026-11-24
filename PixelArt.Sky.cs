namespace FlowState;

// Renk işlemleri ve vakte bağlı resimler: güneş saat (disk + 60 ışın), tepeler, çimen, altın boya. Renkler 0xAARRGGBB.
public static partial class PixelArt
{
    // ---------- renk ----------

    public static uint Rgb(int r, int g, int b) => 0xFF000000u | (uint)r << 16 | (uint)g << 8 | (uint)b;
    public static uint WithAlpha(uint c, int a) => (c & 0x00FFFFFFu) | (uint)a << 24;

    /// <summary>İki rengin karışımı (opak).</summary>
    public static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        int Ch(int s) { double x = (a >> s) & 255, y = (b >> s) & 255; return (int)(x + (y - x) * t); }
        return Rgb(Ch(16), Ch(8), Ch(0));
    }

    public static uint Multiply(uint c, uint m) =>
        (c & 0xFF000000u) | ((c >> 16 & 255) * (m >> 16 & 255) / 255) << 16 | ((c >> 8 & 255) * (m >> 8 & 255) / 255) << 8 | (c & 255) * (m & 255) / 255;

    /// <summary>Palet ışığının (renk + güç) çarpan hâli; gündüz beyaz. Geçişte gereksiz üretim olmasın diye kademelidir.</summary>
    public static uint LightMul(uint light)
    {
        uint m = Mix(0xFFFFFFFF, light, (light >> 24) / 255.0);
        if (m == 0xFFFFFFFF) return m;
        static int Q(uint v) => (int)Math.Min(255, (v + 2) / 4 * 4);
        return Rgb(Q(m >> 16 & 255), Q(m >> 8 & 255), Q(m & 255));
    }

    public static PixImg Tint(PixImg src, uint m)
    {
        if (m == 0xFFFFFFFF) return src;
        var px = new uint[src.Px.Length];
        for (int i = 0; i < px.Length; i++) px[i] = src.Px[i] >> 24 == 0 ? 0 : Multiply(src.Px[i], m);
        return new PixImg(px, src.W, src.H);
    }

    /// <summary>Altına boyar: parlaklığa göre koyu bakır → altın → açık krem; kontur koyu kalır.</summary>
    public static PixImg Golden(PixImg src)
    {
        var px = new uint[src.Px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            uint c = src.Px[i];
            if (c >> 24 == 0) continue;
            double l = (0.114 * (c & 255) + 0.587 * (c >> 8 & 255) + 0.299 * (c >> 16 & 255)) / 255.0;
            uint g = l < 0.16 ? Rgb(70, 42, 10)
                : l < 0.45 ? Mix(Rgb(150, 92, 14), Rgb(226, 160, 30), (l - 0.16) / 0.29)
                : l < 0.75 ? Mix(Rgb(226, 160, 30), Rgb(255, 214, 86), (l - 0.45) / 0.30)
                : Mix(Rgb(255, 214, 86), Rgb(255, 246, 200), (l - 0.75) / 0.25);
            px[i] = (g & 0x00FFFFFFu) | (c & 0xFF000000u);
        }
        return new PixImg(px, src.W, src.H);
    }

    // ---------- güneş saat ----------

    public const int DialN = 106;
    const double DiscR = 39, RayIn = 42.6, RayLong = 52.6, DotAt = 45.2;

    /// <summary>60 ışın: her beşincisi iri üçgen, aradakiler kare dakika noktası.</summary>
    /// <param name="running">Oturum var mı (yoksa ışınlar dinlenir).</param>
    /// <param name="lit">Yanan ışın sayısı (kesirli: sıradaki üçgen dipten uca dolar).</param>
    public static PixImg SunRays(SkyPalette p, bool running, double lit)
    {
        uint ray = p.Ray | 0xFF000000u;
        double moon = Math.Clamp(p.Moon, 0, 1);
        // Tur ilerledikçe ışınlar gündüz mercana, gece maviye döner.
        uint end = Mix(0xFFFF5F56, 0xFF8C9EFF, moon), resting = Mix(ray, 0xFFFFFFFF, 0.25 + 0.15 * moon);
        var px = new uint[DialN * DialN];
        uint Tone(int i, bool on) =>
            !running ? WithAlpha(resting, 0xE8) : on ? Mix(ray, end, i / 59.0) : WithAlpha(0xFFFFFFFF, (int)(0x90 * (1 - 0.45 * moon)));

        for (int i = 0; i < 60; i++)
        {
            double a = i * Math.PI / 30, ux = Math.Sin(a), uy = -Math.Cos(a), fill = Math.Clamp(lit - i, 0, 1);
            if (i % 5 != 0)
            {
                int ox = (int)Math.Floor(DialN / 2.0 + ux * DotAt) - 1, oy = (int)Math.Floor(DialN / 2.0 + uy * DotAt) - 1;
                uint c = Tone(i, fill >= 1);
                for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++) px[(oy + y) * DialN + ox + x] = c;
                continue;
            }
            for (int y = 0; y < DialN; y++)
            for (int x = 0; x < DialN; x++)
            {
                double dx = x + 0.5 - DialN / 2.0, dy = y + 0.5 - DialN / 2.0;
                double along = dx * ux + dy * uy, across = Math.Abs(dx * uy - dy * ux);
                if (along < RayIn || along > RayLong) continue;
                double k = (along - RayIn) / (RayLong - RayIn);
                if (across > 3.6 - 3.0 * k) continue;
                bool on = fill > 0 && k <= fill;
                uint c = Tone(i, on);
                if (running && on && k > 0.72) c = Mix(c, 0xFFFFFFFF, 0.28);
                px[y * DialN + x] = c;
            }
        }
        return new PixImg(px, DialN, DialN);
    }

    /// <summary>Disk: gündüz sol üstten ışık alan güneş, gece kraterli ay.</summary>
    public static PixImg SunDisc(SkyPalette p)
    {
        double moon = Math.Clamp(p.Moon, 0, 1);
        uint A = p.DiscA | 0xFF000000u, B = p.DiscB | 0xFF000000u, ray = p.Ray | 0xFF000000u;
        uint rim = Mix(Mix(B, ray, 0.75), 0xFFC4C1BC, moon), shade = Mix(Mix(B, ray, 0.35), 0xFFD3D2D2, moon);
        double craters = Math.Clamp((moon - 0.15) / 0.7, 0, 1);      // geçişte yarı saydam, gündüz yok
        var px = new uint[DialN * DialN];
        for (int y = 0; y < DialN; y++)
        for (int x = 0; x < DialN; x++)
        {
            double dx = x + 0.5 - DialN / 2.0, dy = y + 0.5 - DialN / 2.0, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > DiscR) continue;
            int bayer = ((x & 1) * 2 + (y & 1) * 3) % 4;              // 2×2 titreme: bantlar piksel piksel karışır
            double d = Math.Sqrt((dx + 9) * (dx + 9) + (dy + 11) * (dy + 11)) / (DiscR * 1.32);
            uint c = Mix(A, B, Math.Floor(Math.Clamp(d * 5 + (bayer - 1.5) * 0.10, 0, 4.99)) / 4);
            double sx = dx + 5, sy = dy + 5;                           // sağ alt kenarda hilal gölge
            if (Math.Sqrt(sx * sx + sy * sy) > DiscR - 0.5 + (bayer - 1.5) * 0.5) c = Mix(c, shade, 0.85);
            if (craters > 0)
                foreach (var (cx, cy, cr) in Craters)
                {
                    double kx = dx - (cx - 0.5) * 2 * DiscR, ky = dy - (cy - 0.5) * 2 * DiscR, kr = cr * 2 * DiscR;
                    if (Math.Sqrt(kx * kx + ky * ky) > kr) continue;
                    bool bowl = Math.Sqrt((kx + 0.7) * (kx + 0.7) + (ky + 0.9) * (ky + 0.9)) < kr - 0.9;
                    c = Mix(c, bowl ? 0xFFD0CDC5 : 0xFFDEDBD2, craters);
                }
            double deg = Math.Atan2(dy, dx) * 180 / Math.PI;           // sol üstte küçük parıltı (gündüz)
            if (r is > 32.5 and < 35.2 && deg is > -152 and < -112 && deg is not (> -124 and < -120)) c = Mix(c, 0xFFFFFFFF, 0.85 * (1 - moon));
            if (r > DiscR - 1.15) c = rim;
            px[y * DialN + x] = c;
        }
        return new PixImg(px, DialN, DialN);
    }

    /// <summary>Ayın kraterleri: (x, y, yarıçap) diskin oranı olarak; yazının arkası boş kalır.</summary>
    static readonly (double X, double Y, double R)[] Craters =
        { (0.27, 0.27, 0.075), (0.72, 0.20, 0.05), (0.81, 0.72, 0.085), (0.30, 0.80, 0.06), (0.55, 0.88, 0.045), (0.11, 0.60, 0.035), (0.90, 0.42, 0.03) };

    // ---------- yer ----------

    public const int HillsH = 48;

    /// <summary>Piksel tepeler ve akasyalar; ışıkla çarpılmış. Altı saydam (çimen dokusu görünür).</summary>
    public static PixImg Hills(int W, uint light)
    {
        const int H = HillsH;
        var px = new uint[W * H];
        void Set(int x, int y, uint c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = Multiply(c, light); }
        for (int x = 0; x < W; x++)
        {
            double far = 27 + 3.5 * Math.Sin(x * 0.035 + 1) + 2 * Math.Sin(x * 0.09);
            double mid = 34 + 3 * Math.Sin(x * 0.05 + 2.2) + 1.5 * Math.Sin(x * 0.13);
            double near = 41 + 2 * Math.Sin(x * 0.07 + 0.5) + 1 * Math.Sin(x * 0.19);
            int yf = (int)Math.Round(far), ym = (int)Math.Round(mid), yn = (int)Math.Round(near);
            for (int y = yf; y < H; y++)
            {
                uint c = y == yf ? 0xFFE0F2CBu : 0xFFCBE8B0u;
                if (y >= ym) c = y == ym ? 0xFFC9E8A9u : 0xFFB2DC8Eu;
                if (y >= yn) { if (y == yn) c = 0xFF9DD271u; else break; }
                Set(x, y, c);
            }
        }
        for (int tx = 52, k = 0; tx < W - 10; tx += 120 + (k * 37) % 70, k++)      // orta tepede akasyalar
        {
            int crown = 9 + (k * 5) % 9;
            int baseY = (int)Math.Round(34 + 3 * Math.Sin(tx * 0.05 + 2.2) + 1.5 * Math.Sin(tx * 0.13));
            int top = baseY - 7;
            for (int y = top; y < baseY + 1; y++) Set(tx, y, 0xFF9C7A54u);
            Set(tx - 1, top + 2, 0xFF9C7A54u); Set(tx + 1, top + 1, 0xFF9C7A54u);
            for (int r = 0; r < 3; r++)
            {
                int half = crown / 2 - (r == 0 ? 2 : r == 2 ? 1 : 0);
                for (int x = tx - half; x <= tx + half; x++) Set(x, top - 2 + r, r == 0 ? 0xFF9FD07Au : 0xFF84BD62u);
            }
        }
        return new PixImg(px, W, H);
    }

    static PixImg? grassTile;

    /// <summary>64×64 döşenebilir çimen: tutamlar ve minik çiçekler.</summary>
    public static PixImg GrassTile()
    {
        if (grassTile != null) return grassTile;
        const int N = 64;
        var px = new uint[N * N];
        Array.Fill(px, 0xFF8DC963u);
        var rnd = new Random(11);
        for (int i = 0; i < 170; i++)                                // çim tutamları: 1×2 açık ya da koyu
        {
            int x = rnd.Next(N), y = rnd.Next(N);
            uint c = rnd.Next(3) == 0 ? 0xFF7BB853u : 0xFFA3D77Au;
            px[y * N + x] = c;
            px[(y + 1) % N * N + x] = c;
        }
        uint[] petals = { 0xFFFFFFFFu, 0xFFFFB8CBu, 0xFFFFE27Au };
        for (int i = 0; i < 7; i++)                                  // minik çiçekler: artı biçimli taç, sarı göbek
        {
            int x = rnd.Next(N), y = rnd.Next(N);
            uint c = petals[i % petals.Length];
            px[y * N + (x + 1) % N] = c;
            px[y * N + (x + N - 1) % N] = c;
            px[(y + 1) % N * N + x] = c;
            px[(y + N - 1) % N * N + x] = c;
            px[y * N + x] = c == 0xFFFFE27Au ? 0xFFFFFFFFu : 0xFFF5B731u;
        }
        return grassTile = new PixImg(px, N, N);
    }
}
