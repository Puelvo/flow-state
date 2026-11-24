namespace FlowState;

/// <summary>Sahnenin üstüne yazılacak etiket (isim, kalp, uyku). Yazıyı arayüz çizer; konum tampon pikseli (alt orta nokta).</summary>
public readonly record struct SceneLabel(float X, float Y, string Text, bool Heart, bool Accent);

/// <summary>
/// Bahçe sahnesinin arayüzden bağımsız piksel çizicisi: gökyüzü, bulutlar, güneş saat, tepeler, çimen, süsler, hayvanlar, gezgin ve jeep.
/// Her karede <see cref="Buf"/> tamponuna (0xFFRRGGBB) çizer. Android uygulaması ve ana ekran widget'ı bunu kullanır; davranış ZooWorld'dedir.
/// Dünya birimi masaüstüyle aynıdır (bir resim pikseli 3 birim); ekranda bir resim pikseli <see cref="Z"/> tampon pikselidir.
/// </summary>
public sealed class PixelScene
{
    public readonly ZooWorld World = new();
    public int W, H;
    public uint[] Buf = Array.Empty<uint>();
    public int Z = 2, DialZ = 2;
    public int Horizon, SunX, SunY;
    public double PathAt = 0.58;                  // patikanın çimendeki yüksekliği (0 ufuk, 1 alt)
    public bool ShowClouds = true, ShowSun = true;
    public int CloudZ = 2;

    // Durum (arayüz doldurur)
    public SkyPalette Palette = DayCycle.Day;
    public bool Session, Running;
    public double Lit;                            // yanan ışın (0..60)
    public Func<string, string> NameOf = k => k;
    public readonly List<SceneLabel> Labels = new();

    readonly Func<string, PixImg> load;           // "zebra_walk", "zebra_poses", "explorer_walk"
    readonly Random rnd = new(21);
    double U => Z / 3.0;                          // dünya birimi → tampon pikseli
    int WorldTop => Horizon - (int)(44 * U);      // hayvan tuvalinin üstü

    public PixelScene(Func<string, PixImg> loader) => load = loader;

    // ---------- yerleşim ----------

    public void Layout(int w, int h, int horizon, int sunX, int sunY)
    {
        if (w <= 0 || h <= 0) return;
        if (w == W && h == H && horizon == Horizon && sunX == SunX && sunY == SunY) return;
        (W, H, Horizon, SunX, SunY) = (w, h, horizon, sunX, sunY);
        Buf = new uint[w * h];
        World.Width = w / U;
        World.Height = (h - WorldTop) / U;
        decorKey = groundKey = "";
        glowMap = null;
    }

    /// <summary>Hayvanların durmayacağı alan (düğmeler), tampon pikseli.</summary>
    public void SetAvoid(int l, int t, int r)
    {
        World.Avoid = (l / U - 40, (t - WorldTop) / U - 10, r / U + 40, double.MaxValue);
    }

    // ---------- bahçedekiler ----------

    sealed class Resident { public Wanderer W = null!; public PixImg[] Frames = null!; public PixImg?[] Poses = null!; public double Scale = 1; public bool Golden; }
    readonly List<Resident> residents = new();
    string residentsKey = "";
    List<string> decors = new();
    int treeStage; bool treeBare;

    /// <summary>Hayvanlar, nadir dostlar, süsler ve seri ağacı değiştiyse sahneyi eşitler.</summary>
    public void SetGarden(IEnumerable<string> animals, IEnumerable<Special> specials, IEnumerable<string> ownedDecors, int stage, bool bare, string? arriving)
    {
        (treeStage, treeBare) = (stage, bare);
        decors = ownedDecors.ToList();
        var own = animals.ToList();
        var sp = specials.Where(s => own.Contains(s.AnimalId)).ToList();
        var key = string.Join(",", own) + "|" + string.Join(",", sp.Select(x => x.Key));
        if (key == residentsKey) return;
        residentsKey = key;
        residents.Clear();
        var keys = own.Concat(sp.Select(x => x.Key)).ToHashSet();
        World.Animals.RemoveAll(w => !keys.Contains(w.Id));
        foreach (var id in own) Add(id, id, null);
        foreach (var s in sp) Add(s.Key, s.AnimalId, s.Kind);

        void Add(string k, string id, SpecialKind? kind)
        {
            var w = World.Animals.FirstOrDefault(x => x.Id == k) ?? World.Add(k, arriving: k == arriving);
            var frames = Frames(id); var poses = Poses(id);
            bool gold = kind == SpecialKind.Golden;
            if (gold) { frames = frames.Select(Gold).ToArray(); poses = poses.Select(p => p == null ? null : Gold(p)).ToArray(); }
            if (kind == SpecialKind.Baby) w.Follow = World.Animals.FirstOrDefault(x => x.Id == id);
            w.Has = poses.Select(p => p != null).ToArray();
            residents.Add(new Resident { W = w, Frames = frames, Poses = poses, Scale = kind == SpecialKind.Baby ? 0.62 : 1, Golden = gold });
        }
    }

    const int Cell = 32;
    readonly Dictionary<string, PixImg[]> frameCache = new();
    readonly Dictionary<string, PixImg?[]> poseCache = new();
    readonly Dictionary<PixImg, PixImg> goldCache = new();

    PixImg Gold(PixImg p) => goldCache.TryGetValue(p, out var g) ? g : goldCache[p] = PixelArt.Golden(p);

    static PixImg Crop(PixImg s, int x0, int y0, int w, int h)
    {
        var px = new uint[w * h];
        for (int y = 0; y < h; y++) Array.Copy(s.Px, (y0 + y) * s.W + x0, px, y * w, w);
        return new PixImg(px, w, h);
    }

    public PixImg[] Frames(string id)
    {
        if (frameCache.TryGetValue(id, out var f)) return f;
        var s = load(id + "_walk");
        return frameCache[id] = Enumerable.Range(0, s.W / Cell).Select(i => Crop(s, i * Cell, 0, Cell, Cell)).ToArray();
    }

    /// <summary>Duruş pozları, ZooPose sırasıyla; hayvanda olmayan poz null.</summary>
    public PixImg?[] Poses(string id)
    {
        if (poseCache.TryGetValue(id, out var p)) return p;
        var s = load(id + "_poses");
        return poseCache[id] = Enumerable.Range(0, s.W / Cell).Select(i => Crop(s, i * Cell, 0, Cell, Cell)).Select(c => c.Px.Any(v => v >> 24 != 0) ? c : null).ToArray();
    }

    const double KeeperSize = 92;                 // dünya birimi
    PixImg[]? keeper;
    int keeperFor;

    /// <summary>Gezgin kareleri, sahne ölçeğine alan ortalamasıyla küçültülmüş.</summary>
    PixImg[] Keeper()
    {
        int size = (int)Math.Round(KeeperSize * U);
        if (keeper != null && keeperFor == size) return keeper;
        keeperFor = size;
        var s = load("explorer_walk");
        int n = s.W / s.H, c = s.H;
        return keeper = Enumerable.Range(0, n).Select(i => Shrink(Crop(s, i * c, 0, c, c), size)).ToArray();
    }

    static PixImg Shrink(PixImg s, int size)
    {
        var px = new uint[size * size];
        double k = s.W / (double)size;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            double a = 0, r = 0, g = 0, b = 0; int n = 0;
            for (int yy = (int)(y * k); yy < Math.Min(s.H, Math.Max((int)(y * k) + 1, (int)((y + 1) * k))); yy++)
            for (int xx = (int)(x * k); xx < Math.Min(s.W, Math.Max((int)(x * k) + 1, (int)((x + 1) * k))); xx++)
            {
                uint c = s.Px[yy * s.W + xx]; double ca = c >> 24;
                a += ca; r += ca * (c >> 16 & 255); g += ca * (c >> 8 & 255); b += ca * (c & 255); n++;
            }
            if (n == 0 || a / n < 110) continue;
            px[y * size + x] = PixelArt.Rgb((int)(r / a), (int)(g / a), (int)(b / a));
        }
        return new PixImg(px, size, size);
    }

    // ---------- ışık ----------

    uint light = 0xFFFFFFFF;
    readonly Dictionary<(PixImg, int), PixImg> litCache = new();

    /// <param name="mode">0 ışığın tamamı (süsler), 1 yumuşak (canlılar), 2 bulut.</param>
    PixImg Lit_(PixImg p, int mode)
    {
        uint m = mode == 0 ? light : mode == 1 ? PixelArt.Mix(0xFFFFFFFF, light, 0.38) : cloudTint;
        if (m == 0xFFFFFFFF) return p;
        return litCache.TryGetValue((p, mode), out var l) ? l : litCache[(p, mode)] = PixelArt.Tint(p, m);
    }

    uint cloudTint = 0xFFFFFFFF;
    string paletteKey = "";
    uint[] skyRows = Array.Empty<uint>();
    PixImg? disc, rays;
    string raysKey = "";

    void ApplyPalette()
    {
        var p = Palette;
        var key = $"{p.SkyTop:X}{p.SkyBottom:X}{p.Light:X}{p.Ray:X}{p.DiscA:X}{p.Moon:F2}|{Horizon}|{H}";
        if (key == paletteKey) return;
        paletteKey = key;
        light = PixelArt.LightMul(p.Light);
        cloudTint = PixelArt.Mix(PixelArt.Mix(0xFFFFFFFF, p.SkyBottom, 0.22), 0xFF5A64A8, Math.Clamp(p.Moon, 0, 1));
        litCache.Clear();
        skyRows = new uint[Math.Max(1, Horizon + 4 * Z)];
        for (int y = 0; y < skyRows.Length; y++) skyRows[y] = PixelArt.Mix(p.SkyTop, p.SkyBottom, y / (double)Math.Max(1, skyRows.Length - 1));
        disc = PixelArt.SunDisc(p);
        raysKey = groundKey = "";
        glowMap = null;
    }

    // ---------- yer ve süsler ----------

    sealed record Prop(PixImg Img, double X, double Y, int Order, int Anim = 0);   // dünya birimi: sol üst; Order derinlik
    readonly List<Prop> props = new();
    string decorKey = "", groundKey = "";
    readonly List<(double L, double T, double R, double B)> growthBlocks = new();
    (double X, double Y)? fire, swing;
    (double X, double Y, double W) crown;
    double treeTop;
    sealed class Meerkat { public double X, Y, Timer, Rise; public int Phase; public bool Left; }
    readonly List<Meerkat> meerkats = new();

    void BuildDecor()
    {
        var key = $"{W}|{H}|{Horizon}|{treeStage}|{treeBare}|{string.Join(",", decors)}";
        if (key == decorKey) return;
        decorKey = key; groundKey = "";
        props.Clear(); meerkats.Clear();
        World.Blocks.Clear(); fire = swing = null;
        double ww = World.Width, top = World.Top, bottom = World.Bottom, band = Math.Max(40, bottom - top);
        World.PathY = x => top + band * PathAt + Math.Min(16, band * 0.06) * Math.Sin(x / 160 + 1);

        void Put(PixImg img, double left, double y, double order, int anim = 0) => props.Add(new Prop(img, left, y, (int)order, anim));

        double px = ww * 0.17, py = top + 22;
        Put(PixelArt.Pond(), px - 72, py - 24, py - 10);
        Put(PixelArt.PondShine(), px - 72, py - 24, py - 9, anim: 1);
        World.Pond = (px, py, 58);
        World.Blocks.Add((px - 64, py - 18, px + 64, py + 16));

        double tx = ww * 0.84, ty = top + 40;
        var tree = PixelArt.Tree();
        Put(tree, tx - tree.W * 1.5, ty - tree.H * 3, ty);
        World.Shade = (tx, ty);
        World.Blocks.Add((tx - 14, ty - 10, tx + 14, ty + 6));
        treeTop = ty - tree.H * 3;
        crown = (tx - tree.W * 1.5 + 20, treeTop + 6, tree.W * 3 - 40);

        foreach (var (fx, dy, bush, k) in new[] { (0.05, 46.0, true, 0), (0.50, 6.0, true, 1), (0.29, band - 26, false, 0), (0.66, 70.0, false, 1), (0.95, band - 40, true, 0), (0.40, 18.0, false, 1) })
        {
            double x = ww * fx, y = top + dy;
            var img = bush ? PixelArt.Bush(k) : PixelArt.Rock(k);
            Put(img, x - img.W * 1.5, y - img.H * 3, y);
            World.Blocks.Add((x - img.W * 1.5 - 6, y - 12, x + img.W * 1.5 + 6, y + 6));
        }
        foreach (var (fx, dy) in new[] { (0.37, 64.0), (0.76, band - 18) })
        {
            meerkats.Add(new Meerkat { X = ww * fx, Y = top + dy, Timer = 3 + rnd.NextDouble() * 8 });
            var hole = PixelArt.Hole();
            Put(hole, ww * fx - hole.W * 1.5, top + dy - hole.H * 1.5 - 2, top + dy);
        }
        for (double x = 20, i = 0; x < ww; x += 95 + (i * 37) % 60, i++)
        {
            var t = PixelArt.Tuft((int)i);
            Put(t, x, World.Height - 2 - t.H * 3 + 6, 100000);
        }

        // Seri ağacı ve satın alınmış süsler
        double sx = ww * 0.60, sy = top + 30;
        var st = PixelArt.StreakTree(treeStage, treeBare);
        Put(st, sx - st.W * 1.5, sy - st.H * 3, sy);
        World.Blocks.Add((sx - 12, sy - 10, sx + 12, sy + 6));
        foreach (var id in decors)
            switch (id)
            {
                case "fence": Put(PixelArt.Fence(), ww * 0.04, bottom - 27 + 10, bottom + 4); break;
                case "sign":
                    double gx = ww * 0.20, gy = top + band - 40;
                    var sign = PixelArt.Sign();
                    Put(sign, gx - sign.W * 1.5, gy - sign.H * 3, gy);
                    World.Blocks.Add((gx - 18, gy - 10, gx + 18, gy + 6));
                    break;
                case "campfire":
                    double cx = ww * 0.24, cy = top + band * 0.35;
                    fire = (cx, cy);
                    World.Blocks.Add((cx - 30, cy - 12, cx + 30, cy + 6));
                    break;
                case "tent":
                    double ex = ww * 0.71, ey = top + 12;
                    var tent = PixelArt.Tent();
                    Put(tent, ex - tent.W * 1.5, ey - tent.H * 3, ey);
                    World.Blocks.Add((ex - tent.W * 1.5, ey - 14, ex + tent.W * 1.5, ey + 6));
                    break;
                case "swing": swing = (tx - 56, ty + 2); break;
                case "flowers":
                    var fr = new Random(2026);
                    var spots = new List<(double X, double Y)>();
                    int want = (int)Math.Clamp(ww * band / 26000, 5, 14);
                    for (int tries = 0; tries < 900 && spots.Count < want; tries++)
                    {
                        double x = 20 + fr.NextDouble() * (ww - 40), y = top + 10 + fr.NextDouble() * (bottom - top - 10);
                        if (World.Blocks.Any(b => x > b.L - 20 && x < b.R + 20 && y > b.T - 10 && y < b.B + 30)) continue;
                        if (World.Avoid is { } a && x > a.L && x < a.R && y > a.T) continue;
                        if (Math.Abs(World.PathY(x) - 4 - y) < 18) continue;
                        if (spots.Any(q => Math.Abs(q.X - x) < 46 && Math.Abs(q.Y - y) < 26)) continue;
                        var f = PixelArt.Flower(spots.Count);
                        spots.Add((x, y));
                        Put(f, x - f.W * 1.5, y - f.H * 3, y - 3);
                    }
                    break;
            }
    }

    uint[] ground = Array.Empty<uint>();
    int groundTop;

    /// <summary>Tepeler, çimen ve patika tek seferde çizilir; her karede satır satır kopyalanır.</summary>
    void BuildGround()
    {
        var key = paletteKey + decorKey;
        if (key == groundKey) return;
        groundKey = key;
        groundTop = Math.Max(0, Horizon - 41 * Z);
        ground = new uint[W * Math.Max(0, H - groundTop)];
        void Set(int x, int y, uint c)
        {
            if (x < 0 || x >= W || y < groundTop || y >= H) return;
            ref uint d = ref ground[(y - groundTop) * W + x];
            d = d == 0 || c >> 24 == 255 ? c : Over(d, c);
        }
        var tile = PixelArt.Tint(PixelArt.GrassTile(), light);
        int grassTop = Horizon - 3 * Z;
        for (int y = grassTop; y < H; y++)
        for (int x = 0; x < W; x++) Set(x, y, tile.Px[(y - grassTop) / Z % tile.H * tile.W + x / Z % tile.W]);
        var hills = PixelArt.Hills(W / Z + 2, light);
        for (int y = 0; y < hills.H * Z; y++)
        for (int x = 0; x < W; x++)
        {
            uint c = hills.Px[y / Z * hills.W + x / Z];
            if (c != 0) Set(x, Horizon - 41 * Z + y, c);
        }
        // Patika: piksel kenarlı iki ton.
        if (World.PathY is { } path)
        {
            uint edge = PixelArt.Multiply(0x8CE6D39C, light), mid = PixelArt.Multiply(0x50F4EAC8, light);
            double half = 12 * U, inner = 6 * U;
            for (int x = 0; x < W; x += Z)
            {
                double cy = WorldTop + (path(x / U) - 4) * U;
                for (int y = (int)((cy - half) / Z) * Z; y < cy + half; y += Z)
                {
                    uint c = Math.Abs(y - cy) < inner ? PixelArt.WithAlpha(mid, 0xB8) : edge;
                    for (int yy = 0; yy < Z; yy++) for (int xx = 0; xx < Z; xx++) Set(x + xx, y + yy, c);
                }
            }
        }
        // Ağacın gölgesi
        if (World.Shade is { } sh) EllipseTo(Set, sh.X * U, WorldTop + sh.Y * U, 85 * U, 14 * U, 0x302E5A1C);
    }

    // ---------- çizim ilkelleri ----------

    static uint Over(uint d, uint s)
    {
        uint a = s >> 24;
        if (a == 255) return s;
        if (a == 0) return d;
        uint na = 255 - a;
        uint rb = ((s & 0xFF00FF) * a + (d & 0xFF00FF) * na + 0x800080) >> 8 & 0xFF00FF;
        uint g = ((s >> 8 & 255) * a + (d >> 8 & 255) * na + 128) >> 8 & 255;
        return 0xFF000000u | rb | g << 8;
    }

    void Px(int x, int y, uint c)
    {
        if ((uint)x >= (uint)W || (uint)y >= (uint)H) return;
        ref uint d = ref Buf[y * W + x];
        d = Over(d, c);
    }

    /// <summary>Resmi tamsayı ölçekle çizer.</summary>
    void Blit(PixImg s, int x0, int y0, int scale, bool flip = false, int alpha = 255, int clipBottom = int.MaxValue)
    {
        int yEnd = Math.Min(Math.Min(H, clipBottom), y0 + s.H * scale), xEnd = Math.Min(W, x0 + s.W * scale);
        for (int y = Math.Max(0, y0); y < yEnd; y++)
        {
            int sy = (y - y0) / scale * s.W, dst = y * W;
            for (int x = Math.Max(0, x0); x < xEnd; x++)
            {
                int sx = (x - x0) / scale;
                uint c = s.Px[sy + (flip ? s.W - 1 - sx : sx)];
                if (c == 0) continue;
                if (alpha != 255) c = PixelArt.WithAlpha(c, (int)(c >> 24) * alpha / 255);
                Buf[dst + x] = c >> 24 == 255 ? c : Over(Buf[dst + x], c);
            }
        }
    }

    /// <summary>Resmi verilen boyuta en yakın komşuyla çizer.</summary>
    void BlitTo(PixImg s, int x0, int y0, int w, int h, bool flip)
    {
        for (int y = Math.Max(0, y0); y < Math.Min(H, y0 + h); y++)
        for (int x = Math.Max(0, x0); x < Math.Min(W, x0 + w); x++)
        {
            int sx = (x - x0) * s.W / w, sy = (y - y0) * s.H / h;
            uint c = s.Px[sy * s.W + (flip ? s.W - 1 - sx : sx)];
            if (c != 0) Buf[y * W + x] = Over(Buf[y * W + x], c);
        }
    }

    static void EllipseTo(Action<int, int, uint> set, double cx, double cy, double rx, double ry, uint c)
    {
        for (int y = (int)(cy - ry); y <= cy + ry; y++)
        for (int x = (int)(cx - rx); x <= cx + rx; x++)
            if ((x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1) set(x, y, c);
    }

    void Ellipse(double cx, double cy, double rx, double ry, uint c) => EllipseTo(Px, cx, cy, rx, ry, c);

    void Radial(double cx, double cy, double rx, double ry, uint c, double strength)
    {
        for (int y = (int)(cy - ry); y <= cy + ry; y++)
        for (int x = (int)(cx - rx); x <= cx + rx; x++)
        {
            double d = Math.Sqrt((x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry));
            if (d < 1) Px(x, y, PixelArt.WithAlpha(c, (int)((c >> 24) * (1 - d) * strength)));
        }
    }

    int Sx(double wx) => (int)Math.Round(wx * U);
    int Sy(double wy) => WorldTop + (int)Math.Round(wy * U);

    // ---------- canlılar ve ortam ----------

    readonly double[] cloudU = PixelArt.CloudSet.Select(c => c.U).ToArray();
    sealed class Flyer { public int Kind; public double X, Y, Tx, Ty, Vx, Vy, T, Life; public uint Color; }
    readonly List<Flyer> flyers = new();
    readonly List<(double X, double Y, double Life)> dust = new();
    readonly (double X, double Y, double P, bool Big)[] stars = MakeStars();
    readonly (double X, double Y, double P)[] flies = MakeFlies();
    double time, birdTimer = 5, leafTimer = 4, dustTimer, owlBlink = 3;

    static (double, double, double, bool)[] MakeStars()
    {
        var r = new Random(5);
        return Enumerable.Range(0, 60).Select(i => (r.NextDouble(), r.NextDouble() * 0.85, r.NextDouble() * 6, i % 7 == 0)).ToArray();
    }

    static (double, double, double)[] MakeFlies()
    {
        var r = new Random(9);
        return Enumerable.Range(0, 12).Select(_ => (r.NextDouble(), r.NextDouble(), r.NextDouble() * 6)).ToArray();
    }

    public bool Celebrate { get; set; }           // oturum bitti: ilk karede kutlama başlar

    /// <summary>Dünyayı dt saniye ilerletir.</summary>
    public void Update(double dt, bool noon = false)
    {
        if (W == 0) return;
        ApplyPalette();
        BuildDecor();
        time += dt;
        World.SessionOn = Session;
        World.KeeperWalking = Session && Running;
        World.Mood = !Session ? ZooMood.Free : Running ? ZooMood.Calm : ZooMood.Play;
        World.Night = DayCycle.IsNight(Palette);
        World.Noon = noon && !World.Night;
        World.Update(dt);
        if (Celebrate && World.Animals.Count > 0 && !double.IsNaN(World.Keeper.X)) { Celebrate = false; World.Celebrate(); }

        for (int i = 0; i < cloudU.Length; i++) cloudU[i] = (cloudU[i] + PixelArt.CloudSet[i].Speed * dt / (W / U + 380)) % 1;

        bool night = Palette.Moon >= 0.5, day = Palette.Stars < 0.3 && Palette.Fireflies < 0.2;
        double ww = World.Width, top = World.Top;
        int bugs = day ? 4 : 0;
        while (flyers.Count(f => f.Kind == 0) < bugs)
        {
            int i = flyers.Count(f => f.Kind == 0);
            var f = new Flyer { Kind = 0, X = rnd.NextDouble() * ww, Y = top - 30 + rnd.NextDouble() * 120, T = rnd.NextDouble(), Color = PixelArt.ButterflyColors[i % 4] };
            (f.Tx, f.Ty) = (f.X, f.Y);
            flyers.Add(f);
        }
        if (!day) flyers.RemoveAll(f => f.Kind == 0);
        if (!night && (birdTimer -= dt) <= 0)
        {
            birdTimer = 18 + rnd.NextDouble() * 16;
            double y0 = Horizon * (0.15 + rnd.NextDouble() * 0.5), v = 40 + rnd.NextDouble() * 18;
            for (int i = 0, n = 3 + rnd.Next(3); i < n; i++) flyers.Add(new Flyer { Kind = 1, X = -30 - i * 9 * Z, Y = y0 + (i % 2) * 4 * Z + i * Z, Vx = v, T = i * 0.13 });
        }
        if (!night && crown.W > 0 && (leafTimer -= dt) <= 0)
        {
            leafTimer = 8 + rnd.NextDouble() * 8;
            flyers.Add(new Flyer { Kind = 2, X = crown.X + rnd.NextDouble() * crown.W, Y = crown.Y, Vx = -16 - rnd.NextDouble() * 14, Vy = 20, Life = 6 });
        }
        for (int i = flyers.Count - 1; i >= 0; i--)
        {
            var f = flyers[i];
            f.T += dt;
            if (f.Kind == 0)
            {
                double dx = f.Tx - f.X, dy = f.Ty - f.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d < 6) { f.Tx = Math.Clamp(f.X + (rnd.NextDouble() - 0.5) * 220, 10, ww - 10); f.Ty = Math.Clamp(f.Y + (rnd.NextDouble() - 0.5) * 100, top - 40, top + 200); }
                else { f.X += dx / d * 30 * dt; f.Y += dy / d * 30 * dt; }
            }
            else if (f.Kind == 1) { f.X += f.Vx * dt; if (f.X > W + 60) flyers.RemoveAt(i); }
            else { f.X += (f.Vx + Math.Sin(f.T * 3) * 18) * dt; f.Y += f.Vy * dt; if ((f.Life -= dt) <= 0) flyers.RemoveAt(i); }
        }
        foreach (var m in meerkats)
        {
            // 0 delikte, 1 çıkıyor, 2 bakınıyor, 3 iniyor
            m.Timer -= dt;
            if (m.Phase == 1) m.Rise = Math.Min(1, m.Rise + dt * 3);
            if (m.Phase == 3) m.Rise = Math.Max(0, m.Rise - dt * 4);
            if (m.Timer > 0) continue;
            m.Phase = (m.Phase + 1) % 4;
            if (night && m.Phase == 1) m.Phase = 0;
            m.Timer = m.Phase switch { 0 => 6 + rnd.NextDouble() * 10, 1 => 0.5, 2 => 2.5 + rnd.NextDouble() * 3, _ => 0.4 };
            if (m.Phase == 2) m.Left = rnd.Next(2) == 0;
        }
        if ((owlBlink -= dt) <= 0) owlBlink = 3 + rnd.NextDouble() * 3;
        if (World.JeepV > 22 && (dustTimer -= dt) <= 0)
        {
            dustTimer = 0.16;
            int dir = World.JeepRight ? 1 : -1;
            dust.Add((World.JeepX - dir * 73 + rnd.Next(-4, 5), World.JeepY - 6 + rnd.Next(-3, 4), 0.7));
        }
        for (int i = dust.Count - 1; i >= 0; i--)
        {
            var (x, y, l) = dust[i];
            if ((l -= dt) <= 0) dust.RemoveAt(i); else dust[i] = (x, y - 14 * dt, l);
        }
    }

    // ---------- kare ----------

    byte[]? glowMap;

    public void Render()
    {
        if (W == 0) return;
        ApplyPalette();
        BuildDecor();
        BuildGround();
        Labels.Clear();
        double moon = Math.Clamp(Palette.Moon, 0, 1);

        int skyEnd = Math.Min(H, skyRows.Length);
        for (int y = 0; y < skyEnd; y++) Array.Fill(Buf, skyRows[y], y * W, W);

        if (Palette.Stars > 0.02)
            foreach (var (sx, sy, ph, big) in stars)
            {
                int a = (int)(255 * Palette.Stars * (0.62 + 0.38 * Math.Sin(time * 1.6 + ph)));
                int x = (int)(sx * W) / Z * Z, y = (int)(sy * Horizon) / Z * Z;
                void Dot(int dx, int dy) { for (int yy = 0; yy < Z; yy++) for (int xx = 0; xx < Z; xx++) Px(x + dx * Z + xx, y + dy * Z + yy, PixelArt.WithAlpha(0xFFFFFFFF, a)); }
                Dot(0, 0);
                if (big) { Dot(-1, 0); Dot(1, 0); Dot(0, -1); Dot(0, 1); }
            }
        if (ShowClouds)
            for (int i = 0; i < cloudU.Length; i++)
            {
                var (kind, _, fy, _, alpha) = PixelArt.CloudSet[i];
                var c = Lit_(PixelArt.BigCloud(kind), 2);
                int cw = c.W * CloudZ, span = W + (int)(380 * CloudZ / 3.0);
                Blit(c, (int)(cloudU[i] * span) - (int)(380 * CloudZ / 3.0), (int)(fy * Math.Max(0, Horizon * 0.86 - c.H * CloudZ)), CloudZ, alpha: (int)(255 * alpha * Palette.Clouds));
            }
        foreach (var f in flyers) if (f.Kind == 1) Blit(PixelArt.Bird()[(int)(f.T * 5) % 2], (int)f.X, (int)f.Y, Z);

        // Yer: tepe bandında saydam pikseller var, altı düz kopya.
        int solid = Math.Clamp(Horizon + 10 * Z, groundTop, H);
        for (int i = 0, n = (solid - groundTop) * W, d = groundTop * W; i < n; i++) { uint c = ground[i]; if (c != 0) Buf[d + i] = c; }
        Array.Copy(ground, (solid - groundTop) * W, Buf, solid * W, (H - solid) * W);

        if (ShowSun) DrawSun();
        DrawItems(moon);

        if (Palette.Fireflies > 0.02)
            foreach (var (fx, fy, ph) in flies)
            {
                double x = fx * W + Math.Sin(time * 0.8 + ph) * 14 * Z, y = Horizon + 8 * Z + fy * Math.Max(10, (H - Horizon) * 0.7) + Math.Cos(time + ph * 2) * 9 * Z;
                double g = Palette.Fireflies * (0.55 + 0.45 * Math.Sin(time * 2.2 + ph * 3));
                Radial(x, y, 4 * Z, 4 * Z, 0xAAE8FF7A, g);
                for (int yy = 0; yy < Z; yy++) for (int xx = 0; xx < Z; xx++) Px((int)x + xx, (int)y + yy, PixelArt.WithAlpha(0xFFFFFFC8, (int)(255 * g)));
            }
    }

    void DrawSun()
    {
        int size = PixelArt.DialN * DialZ, r = (int)(size * 0.62);
        // Hâle: bir kez hesaplanan saydamlık haritası
        uint glow = Palette.Glow;
        if (glowMap == null || glowMap.Length != (2 * r + 1) * (2 * r + 1))
        {
            glowMap = new byte[(2 * r + 1) * (2 * r + 1)];
            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                double d = Math.Sqrt(x * x + y * y) / r;
                double a = d <= 0.55 ? 1 : d < 0.78 ? 1 - (d - 0.55) / 0.23 * 0.67 : d < 1 ? 0.33 * (1 - (d - 0.78) / 0.22) : 0;
                glowMap[(y + r) * (2 * r + 1) + x + r] = (byte)((glow >> 24) * a);
            }
        }
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            int a = glowMap[(y + r) * (2 * r + 1) + x + r];
            if (a > 0) Px(SunX + x, SunY + y, PixelArt.WithAlpha(glow, a));
        }
        var key = $"{Session}|{(int)(Lit * 10)}|{paletteKey}";
        if (key != raysKey || rays == null) { raysKey = key; rays = PixelArt.SunRays(Palette, Session, (int)(Lit * 10) / 10.0); }
        Blit(rays, SunX - size / 2, SunY - size / 2, DialZ);
        Blit(disc!, SunX - size / 2, SunY - size / 2, DialZ);
    }

    void DrawItems(double moon)
    {
        var items = new List<(int Order, Action Draw)>(64);
        foreach (var p in props)
        {
            var q = p;
            items.Add((p.Order, () =>
            {
                int a = q.Anim == 1 ? (int)(255 * (0.6 + 0.4 * Math.Sin(time * 2))) : 255;
                Blit(Lit_(q.Img, 0), Sx(q.X), Sy(q.Y), Z, alpha: a);
            }));
        }
        if (moon >= 0.5)
            items.Add(((int)World.Top + 41, () => Blit(PixelArt.Owl()[owlBlink < 0.18 ? 1 : 0], Sx(crown.X + crown.W / 2 + 30), Sy(treeTop - 12), Z)));
        foreach (var m in meerkats)
        {
            if (m.Rise <= 0) continue;
            var k = Lit_(PixelArt.MeerkatBmp(), 0);
            items.Add(((int)m.Y - 1, () => Blit(k, Sx(m.X) - k.W * Z / 2, Sy(m.Y - 2) - (int)(k.H * Z * m.Rise), Z, m.Left, clipBottom: Sy(m.Y - 2))));
        }
        if (fire is { } fr)
            items.Add(((int)fr.Y, () =>
            {
                Radial(Sx(fr.X), Sy(fr.Y - 14), 100 * U, 42 * U, 0x70FFB85A, 0.25 + 0.75 * moon);
                var f = PixelArt.Campfire()[(int)(time * 2) % 2];
                Blit(f, Sx(fr.X) - f.W * Z / 2, Sy(fr.Y) - f.H * Z, Z);
            }));
        if (swing is { } sw)
            items.Add(((int)sw.Y, () =>
            {
                double ang = Math.Sin(time * 1.4) * 0.22;
                int x0 = Sx(sw.X), y0 = Sy(sw.Y - 80), len = (int)(66 * U);
                uint rope = PixelArt.Multiply(0xFFD9C9A0, light), seat = PixelArt.Multiply(0xFFC98A4E, light);
                int ex = (int)(Math.Sin(ang) * len), ey = (int)(Math.Cos(ang) * len), half = (int)(12 * U);
                for (int t = 0; t <= len; t++) foreach (int s in new[] { -half, half }) for (int k = 0; k < Math.Max(1, Z / 2); k++) Px(x0 + s + ex * t / len + k, y0 + ey * t / len, rope);
                for (int y = 0; y < 2 * Z; y++) for (int x = -half - Z; x <= half + Z; x++) Px(x0 + ex + x, y0 + ey + y, seat);
            }));
        foreach (var r in residents) if (!double.IsNaN(r.W.X)) { var q = r; items.Add((q.W.Held ? 200000 : (int)q.W.Y, () => DrawAnimal(q))); }
        if (!double.IsNaN(World.Keeper.X) && !World.Riding) items.Add(((int)World.Keeper.Y, DrawKeeper));
        if (!double.IsNaN(World.JeepX)) items.Add(((int)World.JeepY, () => DrawJeep(moon)));
        foreach (var d in dust)
        {
            var q = d;
            items.Add(((int)World.JeepY + 1, () => { uint c = PixelArt.WithAlpha(PixelArt.Multiply(0xFFE6D6A8, light), (int)(255 * 0.85 * q.Life / 0.7)); for (int y = 0; y < 2 * Z; y++) for (int x = 0; x < 2 * Z; x++) Px(Sx(q.X) + x, Sy(q.Y) + y, c); }));
        }
        foreach (var f in flyers)
        {
            if (f.Kind == 1) continue;
            var q = f;
            items.Add((60000, () => Blit(q.Kind == 0 ? PixelArt.Butterfly(q.Color)[(int)(q.T * 6) % 2] : Lit_(PixelArt.Leaf(), 0), Sx(q.X), Sy(q.Y), Z, q.Kind == 0 && q.Tx < q.X)));
        }
        foreach (var (_, draw) in items.OrderBy(i => i.Order)) draw();
    }

    PixImg SpriteOf(Resident r)
    {
        var w = r.W;
        double fps = r.Frames.Length >= 6 ? 6.5 : 5;
        return w.State == WanderState.Walk ? r.Frames[(int)(w.WalkTime * fps) % r.Frames.Length] : r.Poses[(int)w.Shown] ?? r.Poses[0]!;
    }

    void DrawAnimal(Resident r)
    {
        var w = r.W;
        int size = (int)Math.Round(Cell * Z * r.Scale), x = Sx(w.X) - size / 2, y = Sy(w.Y) - size;
        int hop = (int)Math.Round(w.HopOffset * U * r.Scale);
        Ellipse(Sx(w.X), Sy(w.Y) - size * 0.06, size * 0.31, size * 0.08, 0x482E5A1C);
        var img = Lit_(SpriteOf(r), 1);
        if (r.Scale == 1) Blit(img, x, y + hop, Z, !w.FacingRight); else BlitTo(img, x, y + hop, size, size, !w.FacingRight);
        if (r.Golden)
            foreach (var (fx, fy, ph) in new[] { (0.16, 0.30, 0.0), (0.78, 0.18, 2.0) })
            {
                double g = 0.5 + 0.5 * Math.Sin(time * 3 + ph);
                int gx = x + (int)(size * fx), gy = y + hop + (int)(size * fy);
                for (int k = -2; k <= 2; k++) for (int t = 0; t < Z; t++) { uint c = PixelArt.WithAlpha(0xFFFFE27A, (int)(255 * g * (k == 0 ? 1 : 0.7))); Px(gx + k * Z / 2 * 1 + t, gy, c); Px(gx, gy + k * Z / 2 + t, c); }
            }
        string? label = w.Arriving ? "Yeni!" : w.State == WanderState.Greet ? "" : w.Held || w.NameTimer > 0 ? NameOf(w.Id) : w.State == WanderState.Idle && w.Shown == ZooPose.Sleep ? "z z" : null;
        if (label != null) Labels.Add(new SceneLabel(Sx(w.X), y + hop + size * 0.06f, label, label.Length == 0, w.Arriving));
    }

    void DrawKeeper()
    {
        var k = World.Keeper;
        var f = Keeper();
        var img = Lit_(k.State == WanderState.Walk && f.Length > 1 ? f[1 + (int)(k.WalkTime * 8) % (f.Length - 1)] : f[0], 1);
        int x = Sx(k.X) - img.W / 2, y = Sy(k.Y + 2) - img.H;
        Ellipse(Sx(k.X), Sy(k.Y) - 1, 22 * U, 6 * U, 0x482E5A1C);
        Blit(img, x, y + (int)Math.Round(k.HopOffset * 0.7 * U), 1, !k.FacingRight);
        if (World.Celebrating > ZooWorld.CelebrateSeconds - 4) Labels.Add(new SceneLabel(Sx(k.X), y, "Selam dostlar!", false, false));
    }

    void DrawJeep(double moon)
    {
        var z = World;
        int w = PixelArt.JeepW * Z, h = PixelArt.JeepH * Z, x0 = Sx(z.JeepX) - w / 2, y0 = Sy(z.JeepY + 4) - h;
        bool flip = !z.JeepRight;
        int bounce = z.JeepV > 6 ? (int)Math.Round((Math.Sin(z.JeepRoll / 7) * 0.8 + Math.Sin(z.JeepRoll / 23) * 0.9) * U) : 0;
        Ellipse(Sx(z.JeepX), y0 + h - 1.5 * Z, w / 2.0 - 3 * Z, 2.6 * Z, 0x502E5A1C);
        if (z.Riding && moon > 0.2)
        {
            int dir = flip ? -1 : 1, bx = Sx(z.JeepX) + dir * (w / 2 - 3 * Z), by = y0 + 17 * Z, len = (int)(150 * U);
            for (int i = 0; i < len; i++)
            {
                int half = (int)((3 + 27.0 * i / len * 0.85) * U), a = (int)(0x90 * moon * (1 - i / (double)len));
                for (int y = -half + (int)(i * 0.06); y <= half + (int)(i * 0.12); y++) Px(bx + dir * i, by + y, PixelArt.WithAlpha(0xFFFFF0B0, a));
            }
        }
        void Part(PixImg img, int px, int py, int scale, int dy = 0)
        {
            int pw = img.W * scale;
            Blit(Lit_(img, 1), flip ? x0 + w - px - pw : x0 + px, y0 + py + dy, scale, flip);
        }
        Part(PixelArt.JeepBackBmp(), 0, 0, Z, bounce);
        if (z.Riding)
        {
            var k = Keeper()[0];
            int dh = k.H * 124 / 192;
            driver = driver != null && driver.W == k.W && driver.H == dh ? driver : Crop(k, 0, 0, k.W, dh);
            Part(driver, 25 * Z - k.W / 2, 18 * Z - dh, 1, bounce);
        }
        Part(PixelArt.JeepFrontBmp(), 0, 0, Z, bounce);
        var wheel = PixelArt.JeepWheelBmps()[(int)(z.JeepRoll / 5) % 4];
        foreach (var (wx, wy) in PixelArt.JeepWheelAt) Part(wheel, (int)((wx - 6.5) * Z), (int)((wy - 6.5) * Z), Z);
    }

    PixImg? driver;

    // ---------- dokunma ----------

    /// <summary>Tampon pikselindeki hayvan (öndeki önce); biraz payla.</summary>
    public Wanderer? HitAnimal(double bx, double by)
    {
        foreach (var r in residents.Where(r => !double.IsNaN(r.W.X)).OrderByDescending(r => r.W.Y))
        {
            double size = Cell * Z * r.Scale, x = Sx(r.W.X), y = Sy(r.W.Y);
            if (Math.Abs(bx - x) < size * 0.42 && by > y - size * 0.95 && by < y + size * 0.08) return r.W;
        }
        return null;
    }

    public (double X, double Y) ToWorld(double bx, double by) => (bx / U, (by - WorldTop) / U);
}
