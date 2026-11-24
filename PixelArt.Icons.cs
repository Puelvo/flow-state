namespace FlowState;

// Arayüzün piksel ikonları (Android: sekmeler, düğmeler, widget, bildirim). 'x' istenen renge boyanır.
public static partial class PixelArt
{
    static readonly Dictionary<(string, uint), PixImg> iconCache = new();

    public static PixImg Icon(string name, uint color)
    {
        if (iconCache.TryGetValue((name, color), out var p)) return p;
        var rows = IconRows.TryGetValue(name, out var r) ? r : IconRows["dot"];
        return iconCache[(name, color)] = Rows(rows, new() { ['x'] = color, ['o'] = WithAlpha(color, (int)(color >> 24) * 110 / 255) });
    }

    static readonly Dictionary<string, string[]> IconRows = new()
    {
        ["dot"] = new[] { "xx", "xx" },
        ["timer"] = new[] {
            "....xxxx....", ".....xx.....", "...xxxxxx.x.", "..xx....xxx.", ".xx..x...xx.", ".x...x....x.",
            ".x...x....x.", ".x...xxx..x.", ".xx......xx.", "..xx....xx..", "...xxxxxx...", "............" },
        ["chart"] = new[] {
            "............", ".....xx.....", ".....xx.....", ".....xx..xx.", ".....xx..xx.", ".xx..xx..xx.",
            ".xx..xx..xx.", ".xx..xx..xx.", ".xx..xx..xx.", ".xx..xx..xx.", ".xx..xx..xx.", "............" },
        ["list"] = new[] {
            "............", ".xx..xxxxxx.", ".xx..xxxxxx.", "............", "............", ".xx..xxxxxx.",
            ".xx..xxxxxx.", "............", "............", ".xx..xxxxxx.", ".xx..xxxxxx.", "............" },
        ["paw"] = new[] {
            "...xx..xx...", "..xxx..xxx..", "..xxx..xxx..", "x..x....x..x", "xxx......xxx", "xxx.xxxx.xxx",
            ".x.xxxxxx.x.", "..xxxxxxxx..", "..xxxxxxxx..", "..xxxxxxxx..", "...xx..xx...", "............" },
        ["people"] = new[] {
            "............", "...xxx......", "..xxxxx.xx..", "..xxxxx.xxx.", "..xxxxx.xxx.", "...xxx..xx..",
            "............", "..xxxxx.xxx.", ".xxxxxxx.xxx", "xxxxxxxx.xxx", "xxxxxxxx.xxx", "............" },
        ["sliders"] = new[] {
            "............", "...xx.......", ".xxxxxxxxxx.", "...xx.......", "............", ".......xx...",
            ".xxxxxxxxxx.", ".......xx...", "............", "....xx......", ".xxxxxxxxxx.", "....xx......" },
        ["bag"] = new[] {
            "....xxxx....", "...x....x...", "...x....x...", ".xxxxxxxxxx.", ".xxxxxxxxxx.", ".xx.xxxx.xx.",
            ".xx.xxxx.xx.", ".xxx....xxx.", ".xxxxxxxxxx.", ".xxxxxxxxxx.", ".xxxxxxxxxx.", "............" },
        ["play"] = new[] {
            "............", "..xxx.......", "..xxxxx.....", "..xxxxxxx...", "..xxxxxxxxx.", "..xxxxxxxxxx",
            "..xxxxxxxxxx", "..xxxxxxxxx.", "..xxxxxxx...", "..xxxxx.....", "..xxx.......", "............" },
        ["pause"] = new[] {
            "............", "..xxx..xxx..", "..xxx..xxx..", "..xxx..xxx..", "..xxx..xxx..", "..xxx..xxx..",
            "..xxx..xxx..", "..xxx..xxx..", "..xxx..xxx..", "..xxx..xxx..", "..xxx..xxx..", "............" },
        ["stop"] = new[] {
            "............", "............", "..xxxxxxxx..", "..xxxxxxxx..", "..xxxxxxxx..", "..xxxxxxxx..",
            "..xxxxxxxx..", "..xxxxxxxx..", "..xxxxxxxx..", "..xxxxxxxx..", "............", "............" },
        ["plus"] = new[] {
            "............", ".....xx.....", ".....xx.....", ".....xx.....", ".....xx.....", ".xxxxxxxxxx.",
            ".xxxxxxxxxx.", ".....xx.....", ".....xx.....", ".....xx.....", ".....xx.....", "............" },
        ["trash"] = new[] {
            "....xxxx....", ".xxxxxxxxxx.", "............", "..xxxxxxxx..", "..x.xx.xx.x.", "..xx.x.x.xx.",
            "..xx.x.x.xx.", "..xx.x.x.xx.", "..xx.x.x.xx.", "..xxxxxxxx..", "..xxxxxxxx..", "............" },
        ["hourglass"] = new[] {
            ".xxxxxxxxxx.", "..x......x..", "..xxxxxxxx..", "...xxxxxx...", "....xxxx....", ".....xx.....",
            ".....xx.....", "....x..x....", "...x.xx.x...", "..x.xxxx.x..", "..xxxxxxxx..", ".xxxxxxxxxx." },
        ["flame"] = new[] {
            "......x.....", ".....xx.....", ".....xxx....", "....xxxx.x..", "..x.xxxxxx..", "..xxxxxxxxx.",
            ".xxxxxxxxxx.", ".xxxxooxxxx.", ".xxxoooxxxx.", ".xxxoooxxx..", "..xxoooxx...", "...xxxxx...." },
        ["heart"] = new[] {
            "............", "..xx....xx..", ".xxxx..xxxx.", ".xxxxxxxxxx.", ".xxxxxxxxxx.", ".xxxxxxxxxx.",
            "..xxxxxxxx..", "...xxxxxx...", "....xxxx....", ".....xx.....", "............", "............" },
        ["star"] = new[] {
            ".....xx.....", ".....xx.....", "....xxxx....", "....xxxx....", "xxxxxxxxxxxx", ".xxxxxxxxxx.",
            "..xxxxxxxx..", "...xxxxxx...", "...xxxxxx...", "..xxxx.xxxx.", "..xx.....xx.", "............" },
        ["staro"] = new[] {
            ".....xx.....", ".....xx.....", "....x..x....", "....x..x....", "xxxxx..xxxxx", ".x........x.",
            "..x......x..", "...x....x...", "...x.xx.x...", "..x.x..x.x..", "..xx....xx..", "............" },
        ["moon"] = new[] {
            "............", "....xxxx....", "..xxxx......", "..xxx.......", ".xxxx.......", ".xxxx.......",
            ".xxxx.......", ".xxxxx....x.", "..xxxxxxxxx.", "..xxxxxxxx..", "....xxxx....", "............" },
        ["sun"] = new[] {
            ".....xx.....", ".x...xx...x.", "..x......x..", "....xxxx....", "...xxxxxx...", "xx.xxxxxx.xx",
            "xx.xxxxxx.xx", "...xxxxxx...", "....xxxx....", "..x......x..", ".x...xx...x.", ".....xx....." },
        ["pen"] = new[] {
            "........xx..", ".......xxxx.", "......x.xxx.", ".....xxx.x..", "....xxxxx...", "...xxxxx....",
            "..xxxxx.....", ".x.xxx......", ".xx.x.......", ".xxx........", "............", "............" },
        ["back"] = new[] {
            "............", "....x.......", "...xx.......", "..xx........", ".xxxxxxxxxx.", ".xxxxxxxxxx.",
            "..xx........", "...xx.......", "....x.......", "............", "............", "............" },
        ["lock"] = new[] {
            "............", "....xxxx....", "...x....x...", "...x....x...", "..xxxxxxxx..", "..xxxxxxxx..",
            "..xxx..xxx..", "..xxx..xxx..", "..xxxxxxxx..", "..xxxxxxxx..", "............", "............" },
        ["cloud"] = new[] {
            "............", "............", "....xxx.....", "...xxxxx....", "..xxxxxxxx..", ".xxxxxxxxxx.",
            "xxxxxxxxxxxx", "xxxxxxxxxxxx", ".xxxxxxxxxx.", "............", "............", "............" },
        ["down"] = new[] {
            ".....xx.....", ".....xx.....", ".....xx.....", ".....xx.....", "..xxxxxxxx..", "...xxxxxx...",
            "....xxxx....", ".....xx.....", "............", ".xxxxxxxxxx.", ".xxxxxxxxxx.", "............" },
        ["reset"] = new[] {
            "............", "...xxxxx..x.", "..xx...xx.x.", ".xx.....xxx.", ".x.....xxxx.", ".x..........",
            ".x........x.", ".xx......xx.", "..xx....xx..", "...xxxxxx...", "............", "............" },
    };

    static PixImg? coinImg;

    /// <summary>Piksel jeton.</summary>
    public static PixImg Coin() => coinImg ??= Rows(new[] {
        "....dddd....", "..ddyyyydd..", ".dywwyyyyyd.", ".dwyyyyyyyd.", "dyyyyllyyyyd", "dyyylyyoyyyd",
        "dyyylyyoyyyd", "dyyyyooyyyyd", ".dyyyyyyyod.", ".dyyyyyyood.", "..ddoooodd..", "....dddd...." },
        new() { ['d'] = 0xFFB87410, ['y'] = 0xFFF5B731, ['w'] = 0xFFFFF0B8, ['l'] = 0xFFFFDF7E, ['o'] = 0xFFDE9616 });

    /// <summary>Piksel şeker düğme: köşeleri yuvarlatılmış yüz, altında koyu dudak, üstte parlak şerit; ortada ikon.</summary>
    public static PixImg Button(int w, int h, uint face, PixImg icon)
    {
        var px = new uint[w * h];
        uint lip = Mix(face, 0xFF2A1810, 0.28), shine = Mix(face, 0xFFFFFFFF, 0.35);
        const int L = 3;                                     // dudak yüksekliği
        bool In(int x, int y, int hh)
        {
            int c = Math.Min(x, w - 1 - x), r = Math.Min(y, hh - 1 - y);
            return x >= 0 && y >= 0 && x < w && y < hh && c + r >= 4 && (c >= 1 || r >= 3) && (r >= 1 || c >= 3);
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (In(x, y - L, h - L)) px[y * w + x] = lip;
            if (In(x, y, h - L)) px[y * w + x] = y == 1 && x > 4 && x < w - 5 ? shine : y <= 2 && x > 3 && x < w - 9 && y == 2 ? Mix(face, shine, 0.4) : face;
        }
        int ox = (w - icon.W) / 2, oy = (h - L - icon.H) / 2;
        for (int y = 0; y < icon.H; y++)
        for (int x = 0; x < icon.W; x++)
        {
            uint c = icon.Px[y * icon.W + x];
            if (c != 0 && x + ox >= 0 && x + ox < w && y + oy >= 0) px[(y + oy) * w + x + ox] = c >> 24 == 255 ? c : Mix(px[(y + oy) * w + x + ox], c | 0xFF000000, (c >> 24) / 255.0);
        }
        return new PixImg(px, w, h);
    }
}
