using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FlowState;

// Gezginin kırmızı jeep'i: çalışırken patikada gezer, mola verilince durur, oturum bitince gezgin iner ve jeep yol kenarında bekler.
// Sürüş mantığı ZooWorld'de (JeepX, JeepV, Riding); burada yalnızca çizilir. Piksel resim kodla üretilir (hayvanlarla aynı 3× ölçek).
public partial class MainWindow
{
    const int JeepW = 58, JeepH = 33;
    Canvas? jeepRoot, jeepCanvas;
    Image? jeepBack, jeepFront, jeepDriver, jeepWheelA, jeepWheelB;
    ScaleTransform? jeepFlip;
    TranslateTransform? jeepBounce;
    Path? jeepBeam;
    double dustTimer;
    readonly List<(Rectangle R, double Life)> dust = new();
    static BitmapSource? jeepBackBmp, jeepFrontBmp, jeepDriverBmp;
    static BitmapSource[]? jeepWheels;

    static readonly (double X, double Y)[] JeepWheelAt = { (14, 26), (45, 26) };

    /// <summary>Piksel tuval: dolgu ve otomatik koyu kontur (sprite'larla aynı dil).</summary>
    sealed class JeepPx
    {
        public readonly uint[] Px = new uint[JeepW * JeepH];
        public void Set(int x, int y, uint c) { if (x >= 0 && x < JeepW && y >= 0 && y < JeepH) Px[y * JeepW + x] = c; }
        public uint Get(int x, int y) => x >= 0 && x < JeepW && y >= 0 && y < JeepH ? Px[y * JeepW + x] : 0;
        public void Rect(int x0, int y0, int x1, int y1, uint c) { for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) Set(x, y, c); }
        public BitmapSource Outlined(uint line)
        {
            var o = (uint[])Px.Clone();
            for (int y = 0; y < JeepH; y++)
            for (int x = 0; x < JeepW; x++)
                if (Get(x, y) == 0 && (Get(x - 1, y) != 0 || Get(x + 1, y) != 0 || Get(x, y - 1) != 0 || Get(x, y + 1) != 0)) o[y * JeepW + x] = line;
            return Bmp(o, JeepW, JeepH);
        }
    }

    const uint JeepRed = 0xFFE5383B, JeepLight = 0xFFFF6B62, JeepDark = 0xFFB3242C, JeepLine = 0xFF3A1216, JeepSteel = 0xFF44464F;

    /// <summary>Arka katman (sürücünün arkası): koltuk, iç, devrilme demiri, bagajdaki çanta.</summary>
    static BitmapSource JeepBackBmp()
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
    static BitmapSource JeepFrontBmp()
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
    static BitmapSource[] JeepWheelBmps()
    {
        if (jeepWheels != null) return jeepWheels;
        var list = new BitmapSource[4];
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
            list[f] = Bmp(px, N, N);
        }
        return jeepWheels = list;
    }

    /// <summary>Sürücü: gezginin belden yukarısı.</summary>
    BitmapSource JeepDriverBmp()
    {
        if (jeepDriverBmp != null) return jeepDriverBmp;
        var c = new CroppedBitmap(ExplorerFrames()[0], new Int32Rect(0, 0, ExplorerCell, 124));
        c.Freeze();
        return jeepDriverBmp = c;
    }

    void BuildJeep()
    {
        if (jeepRoot != null) (jeepRoot.Parent as Panel)?.Children.Remove(jeepRoot);
        foreach (var d in dust) (d.R.Parent as Panel)?.Children.Remove(d.R);
        dust.Clear();
        jeepCanvas = zooCanvas;
        double w = JeepW * PixelScale, h = JeepH * PixelScale;
        jeepFlip = new ScaleTransform(1, 1, w / 2, 0);
        jeepBounce = new TranslateTransform();
        var art = new Canvas { Width = w, Height = h, RenderTransform = jeepFlip };
        var shadow = new Ellipse { Width = w - 20, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(0x50, 0x2E, 0x5A, 0x1C)) };
        Canvas.SetLeft(shadow, 10); Canvas.SetTop(shadow, h - 11);
        art.Children.Add(shadow);

        // Gece far ışığı
        var beamFill = new LinearGradientBrush(Color.FromArgb(0x90, 0xFF, 0xF0, 0xB0), Color.FromArgb(0, 0xFF, 0xF0, 0xB0), 0);
        jeepBeam = new Path { Data = Geometry.Parse("M0,0 L150,-16 L150,44 L0,8 Z"), Fill = beamFill, Visibility = Visibility.Collapsed };
        Canvas.SetLeft(jeepBeam, 55 * PixelScale); Canvas.SetTop(jeepBeam, 16 * PixelScale);
        art.Children.Add(jeepBeam);

        var body = new Canvas { Width = w, Height = h, RenderTransform = jeepBounce };
        jeepBack = PixImage(JeepBackBmp());
        jeepDriver = new Image { Width = VisitorSize, Height = VisitorSize * 124 / ExplorerCell, Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(jeepDriver, BitmapScalingMode.HighQuality);
        Canvas.SetLeft(jeepDriver, 25 * PixelScale - VisitorSize / 2); Canvas.SetTop(jeepDriver, 18 * PixelScale - VisitorSize * 124 / ExplorerCell);
        jeepFront = PixImage(JeepFrontBmp());
        body.Children.Add(jeepBack); body.Children.Add(jeepDriver); body.Children.Add(jeepFront);
        art.Children.Add(body);
        Image Wheel((double X, double Y) at)
        {
            var img = PixImage(JeepWheelBmps()[0]);
            Canvas.SetLeft(img, (at.X - 6.5) * PixelScale); Canvas.SetTop(img, (at.Y - 6.5) * PixelScale);
            art.Children.Add(img);
            return img;
        }
        jeepWheelA = Wheel(JeepWheelAt[0]);
        jeepWheelB = Wheel(JeepWheelAt[1]);
        jeepRoot = new Canvas { Width = w, Height = h, IsHitTestVisible = false };
        jeepRoot.Children.Add(art);
        zooCanvas!.Children.Add(jeepRoot);
    }

    void TickJeep(double dt)
    {
        if (zooCanvas == null) return;
        if (jeepRoot == null || jeepCanvas != zooCanvas) BuildJeep();
        var z = zooWorld;
        if (double.IsNaN(z.JeepX)) { jeepRoot!.Visibility = Visibility.Hidden; return; }
        jeepRoot!.Visibility = Visibility.Visible;
        double w = JeepW * PixelScale, h = JeepH * PixelScale;
        Canvas.SetLeft(jeepRoot, Math.Round(z.JeepX - w / 2));
        Canvas.SetTop(jeepRoot, Math.Round(z.JeepY + 4 - h));
        Panel.SetZIndex(jeepRoot, (int)z.JeepY);
        jeepFlip!.ScaleX = z.JeepRight ? 1 : -1;
        jeepBounce!.Y = z.JeepV > 6 ? Math.Round(Math.Sin(z.JeepRoll / 7) * 0.8 + Math.Sin(z.JeepRoll / 23) * 0.9) : 0;
        Set(jeepBack!, JeepBackBmp()); Set(jeepFront!, JeepFrontBmp()); Set(jeepDriver!, JeepDriverBmp());
        var wheel = JeepWheelBmps()[(int)(z.JeepRoll / 5) % 4];
        Set(jeepWheelA!, wheel); Set(jeepWheelB!, wheel);
        jeepDriver!.Visibility = z.Riding ? Visibility.Visible : Visibility.Collapsed;
        double moon = Math.Clamp(sunPalette.Moon, 0, 1);
        jeepBeam!.Visibility = z.Riding && moon > 0.2 ? Visibility.Visible : Visibility.Collapsed;
        jeepBeam.Opacity = moon;

        // Toz: arka tekerin ardından küçük kareler.
        if (z.JeepV > 22 && (dustTimer -= dt) <= 0)
        {
            dustTimer = 0.16;
            int dir = z.JeepRight ? 1 : -1;
            var r = new Rectangle { Width = 6, Height = 6, Fill = new SolidColorBrush(Multiply(Color.FromRgb(0xE6, 0xD6, 0xA8), lightMul)), IsHitTestVisible = false };
            Canvas.SetLeft(r, Math.Round(z.JeepX - dir * (w / 2 - 14) - 3 + amb.Next(-4, 5))); Canvas.SetTop(r, Math.Round(z.JeepY - 8 + amb.Next(-3, 4)));
            Panel.SetZIndex(r, (int)z.JeepY + 1);
            zooCanvas.Children.Add(r);
            dust.Add((r, 0.7));
        }
        for (int i = dust.Count - 1; i >= 0; i--)
        {
            var (r, life) = dust[i];
            life -= dt;
            if (life <= 0) { zooCanvas.Children.Remove(r); dust.RemoveAt(i); continue; }
            r.Opacity = life / 0.7 * 0.85;
            Canvas.SetTop(r, Canvas.GetTop(r) - 14 * dt);
            dust[i] = (r, life);
        }

        void Set(Image img, BitmapSource src)
        {
            var lit = Lit(src, soft: true);
            if (!ReferenceEquals(img.Source, lit)) img.Source = lit;
        }
    }
}
