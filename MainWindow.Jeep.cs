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
    const int JeepW = PixelArt.JeepW, JeepH = PixelArt.JeepH;
    Canvas? jeepRoot, jeepCanvas;
    Image? jeepBack, jeepFront, jeepDriver, jeepWheelA, jeepWheelB;
    ScaleTransform? jeepFlip;
    TranslateTransform? jeepBounce;
    Path? jeepBeam;
    double dustTimer;
    static BitmapSource? jeepDriverBmp;
    readonly List<(Rectangle R, double Life)> dust = new();

    static (double X, double Y)[] JeepWheelAt => PixelArt.JeepWheelAt;



    static BitmapSource JeepBackBmp() => B(PixelArt.JeepBackBmp());

    static BitmapSource JeepFrontBmp() => B(PixelArt.JeepFrontBmp());

    static BitmapSource[] JeepWheelBmps() => B(PixelArt.JeepWheelBmps());

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
