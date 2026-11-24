using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowState;

// Vaktin ışığı: sahnenin önüne saydam örtü serilmez (renkleri soldurur); her görsel ışık rengiyle ÇARPILIR.
// Çarpım kontrastı ve doygunluğu korur: gece çimen koyu zümrüt olur, gri bir perde inmez.
public partial class MainWindow
{
    /// <summary>Palet ışığının (renk + güç) çarpan hâli; gündüz beyaz. Geçişte gereksiz üretim olmasın diye kademelidir.</summary>
    static Color LightMul(uint light) => C(PixelArt.LightMul(light));

    Color lightMul = Colors.White;

    static uint Multiply(uint c, Color m) => PixelArt.Multiply(c, 0xFF000000u | (uint)m.R << 16 | (uint)m.G << 8 | m.B);

    static Color Multiply(Color c, Color m) => Color.FromArgb(c.A, (byte)(c.R * m.R / 255), (byte)(c.G * m.G / 255), (byte)(c.B * m.B / 255));

    /// <summary>Görseli verilen renkle çarpar.</summary>
    static BitmapSource TintBmp(BitmapSource src, Color m)
    {
        if (m == Colors.White) return src;
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new uint[w * h];
        conv.CopyPixels(px, w * 4, 0);
        for (int i = 0; i < px.Length; i++) if (px[i] >> 24 != 0) px[i] = Multiply(px[i], m);
        var bmp = BitmapSource.Create(w, h, src.DpiX, src.DpiY, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    // Işıklı kopyalar: ışık değişince boşaltılır. litBase: kopyadan aslına.
    readonly Dictionary<(BitmapSource, bool), BitmapSource> litCache = new();
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<BitmapSource, BitmapSource> litBase = new();

    /// <param name="soft">Canlılar (hayvan, gezgin, jeep) ışıktan daha az etkilenir; geceleri de canlı dururlar.</param>
    BitmapSource Lit(BitmapSource src, bool soft)
    {
        if (lightMul == Colors.White) return src;
        if (litCache.TryGetValue((src, soft), out var lit)) return lit;
        lit = TintBmp(src, soft ? Mix(Colors.White, lightMul, 0.38) : lightMul);
        litBase.AddOrUpdate(lit, src);
        return litCache[(src, soft)] = lit;
    }

    string relightKey = "";

    /// <summary>Süsleri ve ağaçları vaktin ışığına boyar (ışık ya da süsler değişince).</summary>
    void Relight()
    {
        if (sceneAnimals == null) return;
        static int Id(List<System.Windows.UIElement> l) => l.Count == 0 ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(l[^1]);
        var key = $"{lightMul}|{decorEls.Count}|{Id(decorEls)}|{growthEls.Count}|{Id(growthEls)}|{flowerImgs.Count}";
        if (key == relightKey) return;
        relightKey = key;
        foreach (var e in decorEls.Concat(growthEls).Concat(flowerImgs.Select(f => (System.Windows.UIElement)f.Img))) Walk(e);
        void Walk(System.Windows.UIElement e)
        {
            if (e is Panel p) { foreach (System.Windows.UIElement c in p.Children) Walk(c); return; }
            if (e is not Image { Source: BitmapSource s } img || img == owlImg || img == campfireImg) return;
            img.Source = Lit(litBase.TryGetValue(s, out var b) ? b : s, soft: false);
        }
        if (roadLine != null) roadLine.Stroke = new SolidColorBrush(Multiply(Color.FromArgb(0x8C, 0xE6, 0xD3, 0x9C), lightMul));
        if (roadMidLine != null) roadMidLine.Stroke = new SolidColorBrush(Multiply(Color.FromArgb(0x50, 0xF4, 0xEA, 0xC8), lightMul));
    }

    /// <summary>Palet değişince: çimen ve bulutları ışığa göre yeniden boyar.</summary>
    void ApplyLight()
    {
        var m = LightMul(sunPalette.Light);
        if (m != lightMul)
        {
            lightMul = m;
            litCache.Clear();          // litBase kalır: eski kopyaların aslını bulmak için gerekir
        }
        var key = $"{m}|{sunPalette.SkyBottom:X}|{sunPalette.Moon:F2}";
        if (key == lightKey || grassLayer == null) return;
        lightKey = key;
        var grass = GrassBrush();
        if (m == Colors.White) grassLayer.Background = grass;
        else
        {
            var b = grass.Clone();
            b.ImageSource = TintBmp((BitmapSource)grass.ImageSource, m);
            RenderOptions.SetBitmapScalingMode(b, BitmapScalingMode.NearestNeighbor);
            b.Freeze();
            grassLayer.Background = b;
        }
        // Bulutlar: gündüz gökyüzünün ufuk rengini hafifçe alır, gece ay ışığında lacivert.
        var sky = C(sunPalette.SkyBottom);
        var cloud = Mix(Mix(Colors.White, sky, 0.22), Color.FromRgb(0x5A, 0x64, 0xA8), Math.Clamp(sunPalette.Moon, 0, 1));
        var tinted = new Dictionary<BitmapSource, BitmapSource>();
        foreach (var c in skyClouds)
        {
            if (!tinted.TryGetValue(c.Base, out var t)) tinted[c.Base] = t = TintBmp(c.Base, cloud);
            c.Img.Source = t;
        }
        Relight();
    }

    string lightKey = "";
    System.Windows.Shapes.Polyline? roadLine, roadMidLine;
}
