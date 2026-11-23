using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FlowState;

// Bahçenin süsleri ve küçük canlıları. Hepsi kodla üretilen piksel sanatı (hayvanlarla aynı 3× ölçek).
//  - Süsler (hayvan tuvalinde, derinliğe göre sıralı): gölet (su içilir), büyük akasya (gölgesinde dinlenilir),
//    çalılar ve kayalar (içine girilmez), toprak patika (gezgin çalışırken burada yürür), ön planda uzun çim tutamları.
//  - Canlılar: gündüz kelebekler, gece dışında kuş sürüleri, ağaçtan savrulan yapraklar, deliklerden çıkan mirketler,
//    gece ağaçta göz kırpan baykuş.
public partial class MainWindow
{
    readonly List<UIElement> decorEls = new();
    Canvas? birdLayer;
    string decorFor = "";
    readonly Random amb = new(21);

    // ---------- piksel yardımcıları ----------

    /// <summary>Satır satır harflerle çizilmiş piksel resim. '.' saydam; diğer harfler paletten.</summary>
    static BitmapSource Pix(string[] rows, Dictionary<char, uint> pal)
    {
        int h = rows.Length, w = rows.Max(r => r.Length);
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (pal.TryGetValue(rows[y][x], out var c)) px[y * w + x] = c;
        return Bmp(px, w, h);
    }

    static BitmapSource Bmp(uint[] px, int w, int h)
    {
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        b.Freeze();
        return b;
    }

    static Image PixImage(BitmapSource src)
    {
        var img = new Image { Source = src, Width = src.PixelWidth * PixelScale, Height = src.PixelHeight * PixelScale, IsHitTestVisible = false, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    // ---------- sprite'lar ----------

    static BitmapSource? treeBmp, pondBmp, pondShine, holeBmp, meerkatBmp, leafBmp;
    static BitmapSource[]? owlBmp, bushBmp, rockBmp, tuftBmp;

    /// <summary>Büyük akasya: yassı şemsiye taç (üstü açık, altı koyu), V dallar, ince gövde.</summary>
    static BitmapSource Tree()
    {
        if (treeBmp != null) return treeBmp;
        const int W = 56, H = 40, cx = 28;
        var px = new uint[W * H];
        void Set(int x, int y, uint c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c; }
        int[] half = { 12, 18, 22, 25, 26, 27, 26, 24, 19 };
        for (int r = 0; r < half.Length; r++)
            for (int x = cx - half[r]; x <= cx + half[r]; x++)
            {
                bool edge = x == cx - half[r] || x == cx + half[r];
                uint c = r <= 1 ? 0xFFA9DB7Eu : r >= 7 ? 0xFF5E9A45u : 0xFF86C25Eu;
                if (r == 2 && (x + r) % 5 == 0) c = 0xFFA9DB7Eu;                       // tepede parlak benekler
                if (r == 6 && (x * 3) % 7 == 0) c = 0xFF6FAA4Du;
                if (edge && r > 1) c = 0xFF5E9A45u;
                Set(x, r, c);
            }
        const uint bark = 0xFF8A6644u, barkDark = 0xFF6E4F33u;
        for (int y = 9; y < H; y++) { Set(cx - 1, y, bark); Set(cx, y, bark); Set(cx + 1, y, barkDark); }
        for (int k = 0; k < 9; k++) { Set(cx - 2 - k, 9 + (8 - k) / 2 + 4, bark); Set(cx + 2 + k, 9 + (8 - k) / 2 + 4, bark); }    // V dallar
        for (int k = 0; k < 6; k++) { Set(cx - 4 - k * 2, 8, 0xFF5E9A45u); }
        Set(cx - 2, H - 1, bark); Set(cx + 2, H - 1, barkDark); Set(cx - 2, H - 2, bark); Set(cx + 2, H - 2, barkDark);
        return treeBmp = Bmp(px, W, H);
    }

    /// <summary>Gölet: kumlu kenar, açık-koyu su halkaları, kenarda sazlar.</summary>
    static BitmapSource Pond()
    {
        if (pondBmp != null) return pondBmp;
        const int W = 48, H = 16;
        var px = new uint[W * H];
        double cx = 23.5, cy = 9, rx = 21, ry = 6;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                double d = Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2);
                double inner = Math.Pow((x - cx) / (rx - 6), 2) + Math.Pow((y - cy - 0.5) / (ry - 2), 2);
                if (d <= 1) px[y * W + x] = inner <= 1 ? 0xFF5FB2DCu : 0xFF85CDEBu;
                else if (d <= 1.22) px[y * W + x] = 0xFFE9D9A6u;                        // kumlu kenar
            }
        // Sazlar (sol kenar): koyu yeşil saplar, kahverengi başlar.
        foreach (var (x, top) in new[] { (3, 2), (5, 0), (7, 3), (42, 3), (44, 1) })
        {
            for (int y = top; y < 10; y++) px[y * W + x] = 0xFF5E9A45u;
            px[top * W + x] = 0xFF8A6644u; px[(top + 1) * W + x] = 0xFF8A6644u;
        }
        return pondBmp = Bmp(px, W, H);
    }

    /// <summary>Göletteki parıltılar (ayrı katman; yavaşça yanıp söner).</summary>
    static BitmapSource PondShine()
    {
        if (pondShine != null) return pondShine;
        const int W = 48, H = 16;
        var px = new uint[W * H];
        foreach (var (x, y, n) in new[] { (15, 7, 4), (27, 9, 3), (21, 11, 2), (31, 6, 2) })
            for (int i = 0; i < n; i++) px[y * W + x + i] = 0xE6FFFFFFu;
        return pondShine = Bmp(px, W, H);
    }

    static BitmapSource Bush(int k)
    {
        bushBmp ??= new[]
        {
            Pix(new[] {
                "....hhhh.......",
                "..hhGGGGhh.hh..",
                ".hGGGGGGGGhGGh.",
                "hGGGpGGGGGGGGGh",
                "GGGGGGGGGGpGGGG",
                "DGGGGGGGGGGGGGD",
                ".DDDDDDDDDDDDD." }, BushPal),
            Pix(new[] {
                "...hhh....",
                ".hhGGGhh..",
                "hGGGGGGGh.",
                "GGGGGGGGGh",
                "DGGGGGGGGD",
                ".DDDDDDDD." }, BushPal),
        };
        return bushBmp[k % bushBmp.Length];
    }

    static readonly Dictionary<char, uint> BushPal = new()
    { ['h'] = 0xFFA9DB7E, ['G'] = 0xFF7DBA58, ['D'] = 0xFF5E9A45, ['p'] = 0xFFFF9BB5 };

    static BitmapSource Rock(int k)
    {
        var pal = new Dictionary<char, uint> { ['l'] = 0xFFE2DDD2, ['g'] = 0xFFC3BCAE, ['d'] = 0xFF948D80 };
        rockBmp ??= new[]
        {
            Pix(new[] { "..llll...", ".lggggl..", "lggggggg.", "gggggggdd", ".dddddd.." }, pal),
            Pix(new[] { ".ll...", "lggg..", "gggd..", ".dd..." }, pal),
        };
        return rockBmp[k % rockBmp.Length];
    }

    /// <summary>Ön plandaki uzun çim tutamı.</summary>
    static BitmapSource Tuft(int k)
    {
        var pal = new Dictionary<char, uint> { ['a'] = 0xFF6FAE4C, ['b'] = 0xFF8CCB63, ['c'] = 0xFFA9DB7E };
        tuftBmp ??= new[]
        {
            Pix(new[] {
                "..c.....b.",
                "..c..b..b.",
                ".bc..b.ab.",
                ".bc.ab.a..",
                "ab.bab.a.c",
                "abbaab.aac",
                "aabaaabaac" }, pal),
            Pix(new[] {
                "...b...",
                ".c.b.a.",
                ".cab.a.",
                "acabaab",
                "aaabaaa" }, pal),
        };
        return tuftBmp[k % tuftBmp.Length];
    }

    static BitmapSource Hole() => holeBmp ??= Pix(new[] {
        "..rrrrrr..",
        ".rkkkkkkr.",
        "rkkkkkkkkr",
        ".rrrrrrrr." }, new() { ['r'] = 0xFFB98E58, ['k'] = 0xFF5A3D25 });

    static BitmapSource MeerkatBmp() => meerkatBmp ??= Pix(new[] {
        "..kk...",
        ".kKKk..",
        ".KeKe..",
        ".KKKKn.",
        "..KK...",
        ".KLLK..",
        ".KLLK..",
        ".KLLK..",
        "kKLLK..",
        ".KKKK..",
        ".KKKK..",
        ".KKKK.." }, new() { ['K'] = 0xFFD9B27A, ['k'] = 0xFFB98E58, ['L'] = 0xFFF0D6A6, ['e'] = 0xFF3B2C25, ['n'] = 0xFF3B2C25 });

    static BitmapSource[] Owl() => owlBmp ??= new[]
    {
        Pix(new[] { "o.....o", "ooooooo", "oEYoYEo", "ooobooo", "oLLLLLo", ".oLLLo.", "..t.t.." },
            new() { ['o'] = 0xFF8A6A52, ['E'] = 0xFFFFE27A, ['Y'] = 0xFF3B2C25, ['b'] = 0xFFF5B731, ['L'] = 0xFFC9A889, ['t'] = 0xFFF5B731 }),
        Pix(new[] { "o.....o", "ooooooo", "oCCoCCo", "ooobooo", "oLLLLLo", ".oLLLo.", "..t.t.." },
            new() { ['o'] = 0xFF8A6A52, ['C'] = 0xFF6E5442, ['b'] = 0xFFF5B731, ['L'] = 0xFFC9A889, ['t'] = 0xFFF5B731 }),
    };

    static BitmapSource Leaf() => leafBmp ??= Pix(new[] { ".gg", "gg." }, new() { ['g'] = 0xFF86C25E });

    static readonly uint[] ButterflyColors = { 0xFFFFB4D2, 0xFFFFE27A, 0xFFB9C8FF, 0xFFFFC09A };
    static readonly Dictionary<uint, BitmapSource[]> butterflyCache = new();

    static BitmapSource[] Butterfly(uint wing)
    {
        if (butterflyCache.TryGetValue(wing, out var f)) return f;
        var pal = new Dictionary<char, uint> { ['W'] = wing, ['b'] = 0xFF3B2C25 };
        return butterflyCache[wing] = new[]
        {
            Pix(new[] { "WW.WW", "WWbWW", ".WbW.", "..b.." }, pal),
            Pix(new[] { ".W.W.", "..b..", ".WbW.", "..b.." }, pal),
        };
    }

    static BitmapSource[]? birdBmp;
    static BitmapSource[] Bird() => birdBmp ??= new[]
    {
        Pix(new[] { "b...b", ".b.b.", "..b.." }, new() { ['b'] = 0xFF4A3D58 }),
        Pix(new[] { ".....", "bb.bb", "..b.." }, new() { ['b'] = 0xFF4A3D58 }),
    };

    // ---------- yerleşim ----------

    /// <summary>Süsleri dünya boyutuna göre yerleştirir; gölet, gölge, engeller ve patika dünyaya bildirilir.</summary>
    void BuildDecor()
    {
        if (sceneAnimals == null || zooWorld.Width <= 0 || zooWorld.Height <= 0) return;
        double W = zooWorld.Width, top = zooWorld.Top, bottom = zooWorld.Bottom;
        var key = $"{Math.Round(W)}|{Math.Round(zooWorld.Height)}";
        if (key == decorFor) return;
        decorFor = key;
        foreach (var e in decorEls) sceneAnimals.Children.Remove(e);
        decorEls.Clear();
        meerkats.Clear();
        zooWorld.Blocks.Clear();

        void Put(UIElement el, double left, double topY, int z)
        {
            Canvas.SetLeft(el, Math.Round(left)); Canvas.SetTop(el, Math.Round(topY)); Panel.SetZIndex(el, z);
            sceneAnimals.Children.Add(el); decorEls.Add(el);
        }

        // Patika: çimenin ortasında hafif kıvrımlı toprak yol (en altta).
        double band = Math.Max(40, bottom - top);
        Func<double, double> pathY = x => top + band * 0.58 + 16 * Math.Sin(x / 160 + 1);
        zooWorld.PathY = pathY;
        var pts = new PointCollection();
        for (double x = -30; x <= W + 30; x += 20) pts.Add(new Point(x, pathY(x) - 4));
        var road = new Polyline { Points = pts, Stroke = new SolidColorBrush(Color.FromArgb(0x8C, 0xE6, 0xD3, 0x9C)), StrokeThickness = 24, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        var roadMid = new Polyline { Points = pts, Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0xF4, 0xEA, 0xC8)), StrokeThickness = 9, StrokeLineJoin = PenLineJoin.Round, StrokeDashArray = new DoubleCollection { 2, 3 }, IsHitTestVisible = false };
        Put(road, 0, 0, -100000);
        Put(roadMid, 0, 0, -99999);

        // Gölet (sol arkada, ufka yakın).
        double px = W * 0.17, py = top + 22;
        var pond = PixImage(Pond());
        Put(pond, px - pond.Width / 2, py - pond.Height / 2, (int)(py - 10));
        var shine = PixImage(PondShine());
        Put(shine, px - shine.Width / 2, py - shine.Height / 2, (int)(py - 9));
        shine.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(1.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        zooWorld.Pond = (px, py, 58);
        zooWorld.Blocks.Add((px - 64, py - 18, px + 64, py + 16));

        // Büyük akasya (sağ arkada) ve gölgesi.
        double tx = W * 0.84, ty = top + 40;
        var shadow = new Ellipse { Width = 170, Height = 28, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x2E, 0x5A, 0x1C)), IsHitTestVisible = false };
        Put(shadow, tx - 85, ty - 14, -90000);
        var tree = PixImage(Tree());
        Put(tree, tx - tree.Width / 2, ty - tree.Height, (int)ty);
        zooWorld.Shade = (tx, ty);
        zooWorld.Blocks.Add((tx - 14, ty - 10, tx + 14, ty + 6));
        owlImg = PixImage(Owl()[0]);
        Put(owlImg, tx + 30, ty - tree.Height - 12, (int)ty + 1);
        treeCrown = (tx - tree.Width / 2 + 20, ty - tree.Height + 6, tree.Width - 40);

        // Çalılar ve kayalar (içlerine girilmez).
        foreach (var (fx, dy, kind, k) in new[] { (0.05, 46.0, 'b', 0), (0.50, 6.0, 'b', 1), (0.29, band - 26, 'r', 0), (0.66, 70.0, 'r', 1), (0.95, band - 40, 'b', 0), (0.40, 18.0, 'r', 1) })
        {
            double x = W * fx, y = top + dy;
            var img = PixImage(kind == 'b' ? Bush(k) : Rock(k));
            Put(img, x - img.Width / 2, y - img.Height, (int)y);
            zooWorld.Blocks.Add((x - img.Width / 2 - 6, y - 12, x + img.Width / 2 + 6, y + 6));
        }

        // Mirket delikleri.
        foreach (var (fx, dy) in new[] { (0.37, 64.0), (0.76, band - 18) })
            AddMeerkat(W * fx, top + dy, Put);

        // Ön planda uzun çim tutamları: hayvanlar arkalarından geçer.
        double groundBottom = zooWorld.Height - 2;
        for (double x = 20, i = 0; x < W; x += 95 + (i * 37) % 60, i++)
        {
            var tuft = PixImage(Tuft((int)i));
            Put(tuft, x, groundBottom - tuft.Height + 6, 100000);
        }

        // Kuşlar gökyüzü katmanında (sahnede, bulutların üstünde).
        if (birdLayer == null && cloudLayer != null)
        {
            birdLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Top };
            SunScene.Children.Insert(SunScene.Children.IndexOf(cloudLayer) + 1, birdLayer);
        }
        if (birdLayer != null) birdLayer.Height = Math.Max(0, horizonY - 60);
    }

    // ---------- canlılar ----------

    Image? owlImg;
    (double X, double Y, double W) treeCrown;
    double owlBlink = 3;

    sealed class Meerkat { public Canvas Clip = null!; public TranslateTransform Rise = null!; public ScaleTransform Flip = null!; public double Timer; public int Phase; }
    readonly List<Meerkat> meerkats = new();

    void AddMeerkat(double x, double y, Action<UIElement, double, double, int> put)
    {
        var kat = PixImage(MeerkatBmp());
        var rise = new TranslateTransform(0, kat.Height + 4);
        var flip = new ScaleTransform(1, 1, kat.Width / 2, 0);
        kat.RenderTransform = new TransformGroup { Children = { flip, rise } };
        // Delik ağzına kadar kırpılmış alan: mirket içinden yükselir.
        var clip = new Canvas { Width = kat.Width, Height = kat.Height, ClipToBounds = true, IsHitTestVisible = false };
        clip.Children.Add(kat);
        put(clip, x - kat.Width / 2, y - kat.Height - 2, (int)y - 1);
        var hole = PixImage(Hole());
        put(hole, x - hole.Width / 2, y - hole.Height / 2 - 2, (int)y);
        meerkats.Add(new Meerkat { Clip = clip, Rise = rise, Flip = flip, Timer = 3 + amb.NextDouble() * 8 });
    }

    sealed class Flyer { public Image Img = null!; public BitmapSource[] Frames = null!; public double X, Y, Vx, Vy, Tx, Ty, T, Life; public int Kind; }
    readonly List<Flyer> flyers = new();
    double birdTimer = 6, leafTimer = 5;

    /// <summary>Bahçe tikinde: kelebekler, kuşlar, yapraklar, mirketler ve baykuş.</summary>
    void TickAmbient(double dt)
    {
        if (sceneAnimals == null) return;
        BuildDecor();
        var p = sunPalette;
        bool night = p.Moon >= 0.5, day = p.Stars < 0.3 && p.Fireflies < 0.2;
        double W = zooWorld.Width, top = zooWorld.Top;

        // Kelebekler: gündüz 4 tane, çimenin üstünde süzülür.
        int wantBugs = day ? 4 : 0, haveBugs = flyers.Count(f => f.Kind == 0);
        for (int i = haveBugs; i < wantBugs; i++)
        {
            var frames = Butterfly(ButterflyColors[i % ButterflyColors.Length]);
            var f = new Flyer { Kind = 0, Frames = frames, Img = PixImage(frames[0]), X = amb.NextDouble() * W, Y = top - 30 + amb.NextDouble() * 120, T = amb.NextDouble() };
            f.Tx = f.X; f.Ty = f.Y;
            Panel.SetZIndex(f.Img, 60000);
            sceneAnimals.Children.Add(f.Img);
            flyers.Add(f);
        }
        if (!day) foreach (var f in flyers.Where(f => f.Kind == 0).ToList()) { sceneAnimals.Children.Remove(f.Img); flyers.Remove(f); }

        // Kuş sürüsü: gece dışında arada bir gökyüzünden geçer.
        if (birdLayer != null && !night && (birdTimer -= dt) <= 0)
        {
            birdTimer = 18 + amb.NextDouble() * 16;
            int n = 3 + amb.Next(3);
            double y0 = 30 + amb.NextDouble() * Math.Max(20, birdLayer.Height - 80), vx = 55 + amb.NextDouble() * 25;
            for (int i = 0; i < n; i++)
            {
                var f = new Flyer { Kind = 1, Frames = Bird(), Img = PixImage(Bird()[0]), X = -40 - i * 26, Y = y0 + (i % 2) * 12 + i * 3, Vx = vx, T = i * 0.13 };
                birdLayer.Children.Add(f.Img);
                flyers.Add(f);
            }
        }

        // Yaprak: ağaçtan savrulur (gece dışında).
        if (!night && treeCrown.W > 0 && (leafTimer -= dt) <= 0)
        {
            leafTimer = 8 + amb.NextDouble() * 8;
            var f = new Flyer { Kind = 2, Frames = new[] { Leaf() }, Img = PixImage(Leaf()), X = treeCrown.X + amb.NextDouble() * treeCrown.W, Y = treeCrown.Y, Vx = -16 - amb.NextDouble() * 14, Vy = 20, Life = 6 };
            Panel.SetZIndex(f.Img, 60000);
            sceneAnimals.Children.Add(f.Img);
            flyers.Add(f);
        }

        foreach (var f in flyers.ToList())
        {
            f.T += dt;
            switch (f.Kind)
            {
                case 0: // kelebek: hedefe süzül, varınca yeni hedef; kanat çırp
                    if (Math.Abs(f.Tx - f.X) < 6 && Math.Abs(f.Ty - f.Y) < 6)
                    { f.Tx = Math.Clamp(f.X + (amb.NextDouble() - 0.5) * 260, 10, W - 10); f.Ty = Math.Clamp(f.Y + (amb.NextDouble() - 0.5) * 90, top - 60, zooWorld.Bottom - 40); }
                    double dx = f.Tx - f.X, dy = f.Ty - f.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                    f.X += dx / d * 32 * dt; f.Y += dy / d * 32 * dt;
                    PlaceFlyer(f, f.X, f.Y + Math.Sin(f.T * 5) * 5, f.T * 8, dx < 0);
                    break;
                case 1: // kuş
                    f.X += f.Vx * dt;
                    PlaceFlyer(f, f.X, f.Y + Math.Sin(f.T * 2.4) * 3, f.T * 5.5, false);
                    if (f.X > (birdLayer?.ActualWidth ?? W) + 40) { birdLayer?.Children.Remove(f.Img); flyers.Remove(f); }
                    break;
                case 2: // yaprak: rüzgarla sola süzülür, sallanır, söner
                    f.X += (f.Vx + Math.Sin(f.T * 3) * 18) * dt; f.Y += f.Vy * dt;
                    f.Img.Opacity = Math.Clamp(f.Life - f.T, 0, 1);
                    PlaceFlyer(f, f.X, f.Y, 0, false);
                    if (f.T >= f.Life) { sceneAnimals.Children.Remove(f.Img); flyers.Remove(f); }
                    break;
            }
        }

        // Mirketler: saklı → çıkar → sağa sola bakar → iner. Gece uyurlar.
        foreach (var m in meerkats)
        {
            double h = m.Clip.Height;
            m.Timer -= dt;
            switch (m.Phase)
            {
                case 0: m.Rise.Y = h + 4; if (m.Timer <= 0 && !night) { m.Phase = 1; m.Timer = 0.45; } break;
                case 1: m.Rise.Y = (h + 4) * Math.Max(0, m.Timer / 0.45); if (m.Timer <= 0) { m.Phase = 2; m.Timer = 3.2; } break;
                case 2: m.Rise.Y = 0; m.Flip.ScaleX = m.Timer > 1.6 ? 1 : -1; if (m.Timer <= 0) { m.Phase = 3; m.Timer = 0.3; } break;
                case 3: m.Rise.Y = (h + 4) * (1 - Math.Max(0, m.Timer / 0.3)); if (m.Timer <= 0) { m.Phase = 0; m.Timer = 7 + amb.NextDouble() * 12; } break;
            }
        }

        // Baykuş: yalnızca gece; arada göz kırpar.
        if (owlImg != null)
        {
            owlImg.Visibility = night ? Visibility.Visible : Visibility.Collapsed;
            owlBlink -= dt;
            if (owlBlink <= 0) owlBlink = 3 + amb.NextDouble() * 3;
            var frame = Owl()[owlBlink < 0.18 ? 1 : 0];
            if (!ReferenceEquals(owlImg.Source, frame)) owlImg.Source = frame;
        }

        static void PlaceFlyer(Flyer f, double x, double y, double frameT, bool flip)
        {
            Canvas.SetLeft(f.Img, Math.Round(x)); Canvas.SetTop(f.Img, Math.Round(y));
            var fr = f.Frames[(int)frameT % f.Frames.Length];
            if (!ReferenceEquals(f.Img.Source, fr)) f.Img.Source = fr;
            if (f.Frames.Length > 1 && f.Kind == 0) f.Img.RenderTransform = flip ? new ScaleTransform(-1, 1, f.Img.Width / 2, 0) : Transform.Identity;
        }
    }
}
