namespace FlowState;

/// <summary>Piksel resim: satır satır 0xAARRGGBB.</summary>
public sealed class PixImg
{
    public readonly int W, H;
    public readonly uint[] Px;
    public PixImg(uint[] px, int w, int h) { Px = px; W = w; H = h; }
}

/// <summary>
/// Kodla üretilen bütün piksel resimler: süsler, jeep, bulutlar, güneş saat, tepeler, çimen. Arayüzden bağımsızdır;
/// masaüstü (WPF) ve Android aynı resimleri kullanır.
/// </summary>
public static partial class PixelArt
{
    public const int JeepW = 58, JeepH = 33;
    static PixImg? treeBmp, pondBmp, pondShine, holeBmp, meerkatBmp, leafBmp, fenceBmp, signBmp, tentBmp, swingIconBmp, jeepBackBmp, jeepFrontBmp;
    static PixImg[]? owlBmp, bushBmp, rockBmp, tuftBmp, birdBmp, fireBmp, jeepWheels;

    /// <summary>Satır satır harflerle çizilmiş piksel resim. '.' saydam; diğer harfler paletten.</summary>
    public static PixImg Rows(string[] rows, Dictionary<char, uint> pal)
    {
        int h = rows.Length, w = rows.Max(r => r.Length);
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (pal.TryGetValue(rows[y][x], out var c)) px[y * w + x] = c;
        return new PixImg(px, w, h);
    }

    /// <summary>Büyük akasya: yassı şemsiye taç (üstü açık, altı koyu), V dallar, ince gövde.</summary>
    public static PixImg Tree()
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
        return treeBmp = new PixImg(px, W, H);
    }

    /// <summary>Gölet: kumlu kenar, açık-koyu su halkaları, kenarda sazlar.</summary>
    public static PixImg Pond()
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
        return pondBmp = new PixImg(px, W, H);
    }

    /// <summary>Göletteki parıltılar (ayrı katman; yavaşça yanıp söner).</summary>
    public static PixImg PondShine()
    {
        if (pondShine != null) return pondShine;
        const int W = 48, H = 16;
        var px = new uint[W * H];
        foreach (var (x, y, n) in new[] { (15, 7, 4), (27, 9, 3), (21, 11, 2), (31, 6, 2) })
            for (int i = 0; i < n; i++) px[y * W + x + i] = 0xE6FFFFFFu;
        return pondShine = new PixImg(px, W, H);
    }

    public static PixImg Bush(int k)
    {
        bushBmp ??= new[]
        {
            Rows(new[] {
                "....hhhh.......",
                "..hhGGGGhh.hh..",
                ".hGGGGGGGGhGGh.",
                "hGGGpGGGGGGGGGh",
                "GGGGGGGGGGpGGGG",
                "DGGGGGGGGGGGGGD",
                ".DDDDDDDDDDDDD." }, BushPal),
            Rows(new[] {
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

    public static PixImg Rock(int k)
    {
        var pal = new Dictionary<char, uint> { ['l'] = 0xFFE2DDD2, ['g'] = 0xFFC3BCAE, ['d'] = 0xFF948D80 };
        rockBmp ??= new[]
        {
            Rows(new[] { "..llll...", ".lggggl..", "lggggggg.", "gggggggdd", ".dddddd.." }, pal),
            Rows(new[] { ".ll...", "lggg..", "gggd..", ".dd..." }, pal),
        };
        return rockBmp[k % rockBmp.Length];
    }

    /// <summary>Ön plandaki uzun çim tutamı.</summary>
    public static PixImg Tuft(int k)
    {
        var pal = new Dictionary<char, uint> { ['a'] = 0xFF6FAE4C, ['b'] = 0xFF8CCB63, ['c'] = 0xFFA9DB7E };
        tuftBmp ??= new[]
        {
            Rows(new[] {
                "..c.....b.",
                "..c..b..b.",
                ".bc..b.ab.",
                ".bc.ab.a..",
                "ab.bab.a.c",
                "abbaab.aac",
                "aabaaabaac" }, pal),
            Rows(new[] {
                "...b...",
                ".c.b.a.",
                ".cab.a.",
                "acabaab",
                "aaabaaa" }, pal),
        };
        return tuftBmp[k % tuftBmp.Length];
    }

    public static PixImg Hole() => holeBmp ??= Rows(new[] {
        "..rrrrrr..",
        ".rkkkkkkr.",
        "rkkkkkkkkr",
        ".rrrrrrrr." }, new() { ['r'] = 0xFFB98E58, ['k'] = 0xFF5A3D25 });

    public static PixImg MeerkatBmp() => meerkatBmp ??= Rows(new[] {
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

    public static PixImg[] Owl() => owlBmp ??= new[]
    {
        Rows(new[] { "o.....o", "ooooooo", "oEYoYEo", "ooobooo", "oLLLLLo", ".oLLLo.", "..t.t.." },
            new() { ['o'] = 0xFF8A6A52, ['E'] = 0xFFFFE27A, ['Y'] = 0xFF3B2C25, ['b'] = 0xFFF5B731, ['L'] = 0xFFC9A889, ['t'] = 0xFFF5B731 }),
        Rows(new[] { "o.....o", "ooooooo", "oCCoCCo", "ooobooo", "oLLLLLo", ".oLLLo.", "..t.t.." },
            new() { ['o'] = 0xFF8A6A52, ['C'] = 0xFF6E5442, ['b'] = 0xFFF5B731, ['L'] = 0xFFC9A889, ['t'] = 0xFFF5B731 }),
    };

    public static PixImg Leaf() => leafBmp ??= Rows(new[] { ".gg", "gg." }, new() { ['g'] = 0xFF86C25E });

    public static readonly uint[] ButterflyColors = { 0xFFFFB4D2, 0xFFFFE27A, 0xFFB9C8FF, 0xFFFFC09A };

    static readonly Dictionary<uint, PixImg[]> butterflyCache = new();

    public static PixImg[] Butterfly(uint wing)
    {
        if (butterflyCache.TryGetValue(wing, out var f)) return f;
        var pal = new Dictionary<char, uint> { ['W'] = wing, ['b'] = 0xFF3B2C25 };
        return butterflyCache[wing] = new[]
        {
            Rows(new[] { "WW.WW", "WWbWW", ".WbW.", "..b.." }, pal),
            Rows(new[] { ".W.W.", "..b..", ".WbW.", "..b.." }, pal),
        };
    }

    public static PixImg[] Bird() => birdBmp ??= new[]
    {
        Rows(new[] { "b...b", ".b.b.", "..b.." }, new() { ['b'] = 0xFF4A3D58 }),
        Rows(new[] { ".....", "bb.bb", "..b.." }, new() { ['b'] = 0xFF4A3D58 }),
    };

    static readonly uint[] FlowerColors = { 0xFFFFB4D2, 0xFFFAF8F2, 0xFFFFE27A, 0xFFC9B6FF, 0xFFFF9F8A };

    static readonly Dictionary<int, PixImg> flowerCache = new();

    static uint Shade(uint c) => 0xFF000000u | (uint)(((c >> 16) & 255) * 0.82) << 16 | (uint)(((c >> 8) & 255) * 0.82) << 8 | (uint)((c & 255) * 0.82);

    public static PixImg Flower(int i)
    {
        int k = i % FlowerColors.Length;
        if (flowerCache.TryGetValue(k, out var b)) return b;
        uint petal = FlowerColors[k], heart = k == 2 ? 0xFFFFFFFF : 0xFFF5B731;
        // 9×12: beş taç yaprak, ortada göbek, iki yapraklı sap.
        return flowerCache[k] = Rows(new[] {
            "...pp....",
            ".pppppp..",
            "ppppPpppp",
            ".ppcccpp.",
            "pppcccppp",
            ".pppPppp.",
            "..pp.pp..",
            "....g....",
            ".ll.g....",
            "..llg.ll.",
            "....gll..",
            "....g...." },
            new() { ['p'] = petal, ['P'] = Shade(petal), ['c'] = heart, ['g'] = 0xFF5E9A45, ['l'] = 0xFF7DBA58 });
    }

    static readonly Dictionary<(int, bool), PixImg> treeCache = new();

    /// <summary>Seri ağacı: evreye göre boy; yapraksız (bare) hâlde yalnız gövde ve dallar, dibinde dökülmüş yapraklar.</summary>
    public static PixImg StreakTree(int stage, bool bare)
    {
        if (treeCache.TryGetValue((stage, bare), out var b)) return b;
        (int W, int H, int crown, int trunkH) = stage switch
        {
            0 => (7, 4, 0, 0), 1 => (7, 7, 0, 3), 2 => (11, 15, 4, 7), 3 => (17, 23, 6, 10), 4 => (23, 31, 9, 13), _ => (27, 35, 11, 14)
        };
        var px = new uint[W * H];
        void Set(int x, int y, uint c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c; }
        int cx = W / 2;
        const uint soil = 0xFFB98E58, bark = 0xFF8A6644, barkD = 0xFF6E4F33, leaf = 0xFF7DBA58, leafL = 0xFFA9DB7E, leafD = 0xFF5E9A45, fruit = 0xFFFF6B6B, dead = 0xFFD9A35A;
        // toprak tümseği
        for (int x = cx - 3; x <= cx + 3; x++) Set(x, H - 1, soil);
        for (int x = cx - 2; x <= cx + 2; x++) Set(x, H - 2, soil);
        if (stage == 0) { Set(cx, H - 3, leaf); }
        else if (stage == 1)
        {
            for (int y = H - 3; y >= H - 5; y--) Set(cx, y, leaf);
            if (!bare) { Set(cx - 1, H - 5, leafL); Set(cx - 2, H - 6, leafL); Set(cx + 1, H - 6, leaf); Set(cx + 2, H - 7, leaf); }
        }
        else
        {
            int trunkTop = H - 2 - trunkH;
            for (int y = H - 3; y >= trunkTop; y--) { Set(cx, y, bark); if (stage >= 4) Set(cx + 1, y, barkD); }
            // dallar
            for (int k = 1; k <= crown / 2; k++) { Set(cx - k, trunkTop + 2 - k / 2, bark); Set(cx + k + (stage >= 4 ? 1 : 0), trunkTop + 3 - k / 2, bark); }
            if (!bare)
            {
                int ccy = trunkTop - crown / 2 + 1;
                for (int y = ccy - crown; y <= ccy + crown; y++)
                    for (int x = cx - crown - 1; x <= cx + crown + 1; x++)
                    {
                        double d = Math.Pow((x - cx) / (crown + 1.0), 2) + Math.Pow((y - ccy) / (double)crown, 2);
                        if (d > 1) continue;
                        uint c = d > 0.72 ? leafD : (y < ccy - crown / 3 && (x + y) % 3 == 0) ? leafL : leaf;
                        Set(x, y, c);
                    }
                if (stage == 5)
                    foreach (var (fx, fy) in new[] { (-5, 0), (3, -3), (6, 2), (-2, 4), (0, -6), (-7, -4), (4, 6) })
                    { Set(cx + fx, ccy + fy, fruit); Set(cx + fx + 1, ccy + fy, 0xFFFF9B9B); }
            }
            else
                foreach (var dx in new[] { -4, -2, 3, 5 }) Set(cx + dx, H - 2, dead);              // dökülmüş yapraklar
        }
        return treeCache[(stage, bare)] = new PixImg(px, W, H);
    }

    public static PixImg Fence() => fenceBmp ??= Rows(new[] {
        ".o....o....o....o....o....o..",
        "ooo..ooo..ooo..ooo..ooo..ooo.",
        "wwwwwwwwwwwwwwwwwwwwwwwwwwwww",
        "dodddddoddddodddddoddddoddddd",
        ".o....o....o....o....o....o..",
        "wwwwwwwwwwwwwwwwwwwwwwwwwwwww",
        "dodddddoddddodddddoddddoddddd",
        ".o....o....o....o....o....o..",
        ".o....o....o....o....o....o.." },
        new() { ['w'] = 0xFFD9B27A, ['d'] = 0xFFB98E58, ['o'] = 0xFFA57C4E });

    public static PixImg Sign() => signBmp ??= Rows(new[] {
        "wwwwwwwwwwwwww",
        "wLLLLLLLLLLLLw",
        "wLttLtttLttLLw",
        "wLLLLLLLLLLLLw",
        "wLtttLttLtttLw",
        "wLLLLLLLLLLLLw",
        "wwwwwwwwwwwwww",
        "......pp......",
        "......pp......",
        "......pp......",
        "......pp......" },
        new() { ['w'] = 0xFFA57C4E, ['L'] = 0xFFE9CF9E, ['t'] = 0xFF8A6644, ['p'] = 0xFF8A6644 });

    public static PixImg[] Campfire() => fireBmp ??= new[]
    {
        Rows(new[] {
            "........y.........",
            ".......yy.........",
            "......yooy....y...",
            ".....yoory...yo...",
            "....yorrroy..yo...",
            "....orrRrroyyoo...",
            "...yorRRRrroorry..",
            "...orrRRRRrrrrro..",
            "....orrRRRrrrro...",
            ".....orrrrrrro....",
            "..ss..ooooooo..s..",
            ".bbBbbBbbbBbbBbb..",
            "bBbbBbbBbbBbBbbBb.",
            ".sss.bbbBbbbb.sss." }, FirePal),
        Rows(new[] {
            "..........y.......",
            ".........yy.......",
            "...y.....yooy.....",
            "...oy...yoory.....",
            "...oy..yorrroy....",
            "...ooyyorrRrro....",
            "..yrroorrRRRroy...",
            "..orrrrrRRRRrro...",
            "...orrrrRRRrro....",
            "....orrrrrrro.....",
            "..s..ooooooo..ss..",
            ".bbBbbBbbbBbbBbb..",
            "bBbbBbbBbbBbBbbBb.",
            ".sss.bbbBbbbb.sss." }, FirePal),
    };

    static readonly Dictionary<char, uint> FirePal = new()
    { ['y'] = 0xFFFFE27A, ['o'] = 0xFFFFA94D, ['r'] = 0xFFFF6B4A, ['R'] = 0xFFFFD27A, ['b'] = 0xFF8A6644, ['B'] = 0xFF6E4F33, ['s'] = 0xFFB8B2A6 };

    public static PixImg Tent()
    {
        if (tentBmp != null) return tentBmp;
        const int W = 26, H = 17;
        var px = new uint[W * H];
        for (int y = 0; y < H; y++)
        {
            int half = (int)Math.Round(y * 12.0 / (H - 1));
            for (int x = 13 - half; x <= 13 + half; x++)
            {
                uint c = x < 13 ? 0xFFF7B267 : 0xFFE8955A;                         // ışıklı / gölgeli yüz
                if (x == 13 - half || x == 13 + half) c = 0xFFB86B3A;
                if (y > 7 && Math.Abs(x - 13) <= (y - 7) / 2) c = 0xFF6E4F33;      // kapı
                px[y * W + x] = c;
            }
        }
        px[0 * W + 13] = 0xFF8A6644;
        return tentBmp = new PixImg(px, W, H);
    }

    public static PixImg SwingIcon() => swingIconBmp ??= Rows(new[] {
        "bbbbbbbbbb",
        ".r......r.",
        ".r......r.",
        ".r......r.",
        ".r......r.",
        ".r......r.",
        "sssssssss.",
        ".d.....d.." },
        new() { ['b'] = 0xFF8A6644, ['r'] = 0xFFD9C9A0, ['s'] = 0xFFC98A4E, ['d'] = 0xFF8A6644 });

    public static readonly (double X, double Y)[] JeepWheelAt = { (14, 26), (45, 26) };

    /// <summary>Piksel tuval: dolgu ve otomatik koyu kontur (sprite'larla aynı dil).</summary>
    sealed class JeepPx
    {
        public readonly uint[] Px = new uint[JeepW * JeepH];
        public void Set(int x, int y, uint c) { if (x >= 0 && x < JeepW && y >= 0 && y < JeepH) Px[y * JeepW + x] = c; }
        public uint Get(int x, int y) => x >= 0 && x < JeepW && y >= 0 && y < JeepH ? Px[y * JeepW + x] : 0;
        public void Rect(int x0, int y0, int x1, int y1, uint c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c); }
        public PixImg Outlined(uint line)
        {
            var o = (uint[])Px.Clone();
            for (int y = 0; y < JeepH; y++)
            for (int x = 0; x < JeepW; x++)
                if (Get(x, y) == 0 && (Get(x - 1, y) != 0 || Get(x + 1, y) != 0 || Get(x, y - 1) != 0 || Get(x, y + 1) != 0)) o[y * JeepW + x] = line;
            return new PixImg(o, JeepW, JeepH);
        }
    }

    const uint JeepRed = 0xFFE5383B, JeepLight = 0xFFFF6B62, JeepDark = 0xFFB3242C, JeepLine = 0xFF3A1216, JeepSteel = 0xFF44464F;

    /// <summary>Arka katman (sürücünün arkası): koltuk, iç, devrilme demiri, bagajdaki çanta.</summary>
    public static PixImg JeepBackBmp()
    {
        if (jeepBackBmp != null) return jeepBackBmp;
        var p = new JeepPx();
        p.Rect(13, 14, 36, 18, 0xFF5E1A1E);                                    // iç
        p.Rect(14, 9, 18, 16, 0xFF8A5A3B); p.Rect(14, 9, 18, 10, 0xFFA8734C);  // koltuk
        p.Rect(10, 3, 11, 15, JeepSteel); p.Rect(10, 3, 14, 4, JeepSteel);     // devrilme demiri
        p.Rect(4, 8, 9, 13, 0xFFC59A63); p.Rect(4, 10, 9, 10, 0xFF8A6644); p.Rect(6, 8, 6, 13, 0xFF8A6644);   // çanta
        return jeepBackBmp = p.Outlined(JeepLine);
    }

    /// <summary>Ön katman: gövde, kaput, kapı, ön cam, far, tampon, stepne (tekerlekler ayrı döner).</summary>
    public static PixImg JeepFrontBmp()
    {
        if (jeepFrontBmp != null) return jeepFrontBmp;
        var p = new JeepPx();
        p.Rect(3, 17, 54, 25, JeepRed);                                         // alt gövde
        p.Rect(3, 14, 14, 17, JeepRed);                                         // arka çamurluk
        p.Rect(37, 14, 53, 17, JeepRed); p.Rect(38, 13, 51, 13, JeepRed);       // kaput
        p.Rect(15, 16, 36, 17, JeepRed);                                        // kapı üstü (alçak)
        for (int y = 5; y <= 14; y++) { int x = 35 + (y - 5) / 4; p.Rect(x, y, x + 1, y, JeepSteel); }          // ön cam çerçevesi
        for (int y = 6; y <= 13; y++) p.Set(34 + (y - 5) / 4, y, 0xB0BFE6F5);                                      // cam
        // Işık ve gölge
        for (int x = 0; x < JeepW; x++)
            for (int y = 1; y < JeepH; y++)
                if (p.Get(x, y) == JeepRed && p.Get(x, y - 1) == 0) p.Set(x, y, JeepLight);
        for (int y = 23; y <= 25; y++) for (int x = 0; x < JeepW; x++) if (p.Get(x, y) == JeepRed) p.Set(x, y, JeepDark);
        for (int x = 4; x <= 53; x++) if (p.Get(x, 19) == JeepRed && x % 1 == 0) p.Set(x, 19, 0xFFF4F0E6);        // krem şerit
        foreach (int x in new[] { 18, 34 }) p.Rect(x, 17, x, 24, JeepDark);                                       // kapı çizgileri
        p.Rect(30, 21, 32, 21, 0xFFF4F0E6);                                                                       // kapı kolu
        p.Rect(52, 16, 54, 18, 0xFFFFE79A); p.Set(54, 16, 0xFFFFFFFF);                                            // far
        p.Rect(53, 22, 56, 24, 0xFF8D9099); p.Rect(1, 22, 3, 24, 0xFF8D9099);                                     // tamponlar
        p.Rect(41, 14, 46, 14, JeepDark);                                                                         // kaput ızgarası
        // Stepne
        for (int y = 11; y <= 21; y++) for (int x = 0; x <= 4; x++)
            if ((x - 1) * (x - 1) * 2.2 + (y - 16) * (y - 16) <= 22) p.Set(x, y, (x - 1) * (x - 1) * 2.2 + (y - 16) * (y - 16) <= 5 ? 0xFFC9CBD2 : 0xFF2B2B31);
        // Tekerlek davlumbazları
        foreach (var (cx, cy) in JeepWheelAt)
            for (int y = 0; y < JeepH; y++)
            for (int x = 0; x < JeepW; x++)
            {
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                if (d < 6.6) p.Set(x, y, y > cy ? 0 : JeepLine);
                else if (d < 8 && y < cy && p.Get(x, y) != 0) p.Set(x, y, JeepDark);
            }
        return jeepFrontBmp = p.Outlined(JeepLine);
    }

    /// <summary>Tekerlek: 13×13, göbeği dönen dört kare.</summary>
    public static PixImg[] JeepWheelBmps()
    {
        if (jeepWheels != null) return jeepWheels;
        var list = new PixImg[4];
        for (int f = 0; f < 4; f++)
        {
            const int N = 13;
            var px = new uint[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                double dx = x - 6, dy = y - 6, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 6.2) continue;
                uint c = d > 5.4 ? 0xFF15151Au : d > 3.3 ? 0xFF33333Bu : d > 1.1 ? 0xFFC9CBD2u : 0xFF7C7F88u;
                double a = (Math.Atan2(dy, dx) / (Math.PI / 2) - f / 4.0 + 8) % 1;       // dört kol
                if (d > 1.1 && d <= 3.3 && a < 0.34) c = 0xFF7C7F88;
                px[y * N + x] = c;
            }
            list[f] = new PixImg(px, N, N);
        }
        return jeepWheels = list;
    }

    static readonly PixImg?[] bigClouds = new PixImg?[5];

    /// <summary>Üst üste binen yuvarlaklardan tombul bulut: beyaz tepe, her yuvarlağın altında ve tabanda açık mavi gölge.</summary>
    public static PixImg BigCloud(int kind)
    {
        if (bigClouds[kind] is { } cached) return cached;
        var blobs = kind switch
        {
            0 => new (int X, int Y, int R)[] { (14, 30, 11), (92, 31, 10), (76, 25, 15), (32, 25, 15), (54, 19, 18) },
            1 => new (int X, int Y, int R)[] { (10, 22, 9), (61, 23, 8), (46, 18, 12), (27, 15, 13) },
            2 => new (int X, int Y, int R)[] { (12, 34, 10), (114, 35, 8), (98, 29, 14), (31, 29, 14), (76, 21, 17), (51, 20, 18) },
            3 => new (int X, int Y, int R)[] { (8, 16, 7), (45, 17, 6), (36, 13, 9), (22, 11, 10) },
            _ => new (int X, int Y, int R)[] { (6, 10, 5), (27, 10, 5), (16, 8, 7) },
        };
        int w = blobs.Max(b => b.X + b.R) + 1, bottom = blobs[0].Y + blobs[0].R * 2 / 3;
        int h = bottom + 1;
        bool In(int x, int y) => y <= bottom && blobs.Any(b => (x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y) <= b.R * b.R);
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (!In(x, y)) continue;
            // Öndeki yuvarlak (listede en son) tonu belirler.
            var f = blobs.Last(b => (x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y) <= b.R * b.R);
            double low = (y - f.Y + (x - f.X) * 0.35) / f.R + (((x + y) & 1) == 0 ? 0.05 : -0.05);
            uint c = low > 0.62 ? 0xFFDDEBFAu : low > 0.28 ? 0xFFEFF6FDu : 0xFFFFFFFFu;
            if (y >= bottom - 1 || !In(x, y + 2)) c = 0xFFC9DDF3u;
            else if (y >= bottom - 3) c = 0xFFDDEBFAu;
            px[y * w + x] = c;
        }
        return bigClouds[kind] = new PixImg(px, w, h);
    }

    /// <summary>
    /// (çeşit, yatay başlangıç oranı, gökyüzündeki yükseklik oranı, hız px/sn, saydamlık). Uzaktakiler önce (arkada).
    /// Aynı katmandakiler aynı hızdadır; aralıkları hiç kapanmaz, bulutlar yığılmaz.
    /// </summary>
    public static readonly (int Kind, double U, double Y, double Speed, double Alpha)[] CloudSet =
    {
        // uzak
        (3, 0.16, 0.58, 3, 0.62), (4, 0.36, 0.95, 3, 0.62), (3, 0.56, 0.40, 3, 0.62), (4, 0.76, 0.08, 3, 0.62), (3, 0.97, 0.88, 3, 0.62),
        // orta
        (1, 0.08, 0.05, 6, 0.88), (3, 0.31, 0.22, 6, 0.88), (1, 0.64, 0.90, 6, 0.88), (1, 0.88, 0.45, 6, 0.88),
        // yakın
        (0, 0.24, 0.66, 9, 1.00), (2, 0.50, 0.04, 9, 1.00), (0, 0.80, 0.78, 9, 1.00), (2, 0.03, 0.34, 9, 1.00)
    };
}
