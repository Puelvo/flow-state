using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlowState;

// Gece güneş saatin yüzü: gerçek dolunaya benzeyen ay. Koyu "denizler" (maria) ve belirgin kraterler, gerçek ay
// haritasındaki yerlerinde (kuzey yukarıda, Dünya'dan görünüş). Abartısız: yumuşak kenarlı, düşük karşıtlıklı;
// ortadaki süre yazısı rahat okunur.
public partial class MainWindow
{
    const double MoonD = 244;

    void BuildMoon()
    {
        MoonDeco.Children.Clear();

        // Kenar kararması: dolunayda kenarlar her yandan biraz koyu.
        MoonDeco.Children.Add(new Ellipse
        {
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x00, 0x8A, 0x8C, 0x96), 0.62),
                new(Color.FromArgb(0x26, 0x8A, 0x8C, 0x96), 0.9),
                new(Color.FromArgb(0x48, 0x7A, 0x7C, 0x88), 1),
            })
        });

        // Denizler: (ad, merkez x, merkez y, yarıçap x, yarıçap y, koyuluk) — 0..1 oranlarla.
        var maria = new Canvas { Width = MoonD, Height = MoonD, Effect = new BlurEffect { Radius = 4 }, CacheMode = new BitmapCache(2) };
        var seas = new (string Name, double X, double Y, double Rx, double Ry, double A)[]
        {
            ("Fırtınalar Okyanusu", 0.22, 0.48, 0.15, 0.24, 0.20),
            ("Yağmurlar Denizi",    0.40, 0.28, 0.13, 0.11, 0.26),
            ("Soğuk Deniz",         0.50, 0.13, 0.20, 0.035, 0.18),
            ("Berraklık Denizi",    0.61, 0.31, 0.07, 0.07, 0.25),
            ("Huzur Denizi",        0.67, 0.47, 0.09, 0.075, 0.26),
            ("Krizler Denizi",      0.86, 0.34, 0.05, 0.065, 0.28),
            ("Bolluk Denizi",       0.79, 0.62, 0.055, 0.08, 0.22),
            ("Nektar Denizi",       0.69, 0.66, 0.04, 0.035, 0.20),
            ("Bulutlar Denizi",     0.44, 0.68, 0.08, 0.06, 0.18),
            ("Nemler Denizi",       0.26, 0.67, 0.045, 0.04, 0.22),
            ("Buharlar Denizi",     0.53, 0.42, 0.05, 0.035, 0.18),
        };
        int seed = 1;
        foreach (var m in seas)
            maria.Children.Add(new Path
            {
                Data = Blob(m.X * MoonD, m.Y * MoonD, m.Rx * MoonD, m.Ry * MoonD, seed++),
                Fill = new SolidColorBrush(Color.FromArgb((byte)(m.A * 255), 0x6E, 0x74, 0x84)),
                ToolTip = m.Name
            });
        MoonDeco.Children.Add(maria);

        var craters = new Canvas { Width = MoonD, Height = MoonD };
        // Tycho: güneyde, çevresine ışın saçan parlak krater.
        double tx = 0.47 * MoonD, ty = 0.86 * MoonD;
        var rays = new Canvas { Effect = new BlurEffect { Radius = 1.5 } };
        var rnd = new Random(7);
        for (int i = 0; i < 14; i++)
        {
            double a = i / 14.0 * Math.PI * 2 + rnd.NextDouble() * 0.3, len = 26 + rnd.NextDouble() * 60;
            rays.Children.Add(new Line
            {
                X1 = tx, Y1 = ty, X2 = tx + Math.Cos(a) * len, Y2 = ty + Math.Sin(a) * len,
                Stroke = new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1.6 + rnd.NextDouble()
            });
        }
        craters.Children.Add(rays);
        Crater(craters, tx, ty, 9, bright: true);
        Crater(craters, 0.37 * MoonD, 0.44 * MoonD, 8, bright: true);     // Kopernik
        Crater(craters, 0.25 * MoonD, 0.43 * MoonD, 5, bright: true);     // Kepler
        Crater(craters, 0.17 * MoonD, 0.35 * MoonD, 4, bright: true);     // Aristarchus (en parlak)
        Crater(craters, 0.45 * MoonD, 0.17 * MoonD, 7, bright: false);    // Platon (koyu tabanlı)
        foreach (var (x, y, r) in new[] { (0.74, 0.80, 5.0), (0.58, 0.76, 4.0), (0.30, 0.80, 6.0), (0.82, 0.48, 4.0), (0.62, 0.20, 3.5), (0.12, 0.62, 4.5), (0.36, 0.92, 3.5) })
            Crater(craters, x * MoonD, y * MoonD, r, bright: false, faint: true);
        MoonDeco.Children.Add(craters);
    }

    /// <summary>Krater: ışığın geldiği yanda (sağ üst) parlak, karşı yanda gölgeli halka.</summary>
    static void Crater(Canvas c, double x, double y, double r, bool bright, bool faint = false)
    {
        byte k = (byte)(faint ? 0x40 : 0x70);
        var floor = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = new SolidColorBrush(bright ? Color.FromArgb(0xB0, 0xFB, 0xFA, 0xF6) : Color.FromArgb((byte)(faint ? 0x22 : 0x50), 0x6E, 0x74, 0x84)),
            Stroke = new LinearGradientBrush(Color.FromArgb(k, 0xFF, 0xFF, 0xFF), Color.FromArgb(k, 0x6E, 0x72, 0x80), new Point(1, 0), new Point(0, 1)),
            StrokeThickness = Math.Max(1, r * 0.28)
        };
        Canvas.SetLeft(floor, x - r); Canvas.SetTop(floor, y - r);
        c.Children.Add(floor);
    }

    /// <summary>Dalgalı kenarlı yumuşak leke (deniz biçimi): elipsin yarıçapı birkaç sinüsle oynatılır.</summary>
    static Geometry Blob(double cx, double cy, double rx, double ry, int seed)
    {
        var rnd = new Random(seed * 31 + 5);
        double p1 = rnd.NextDouble() * 6, p2 = rnd.NextDouble() * 6, p3 = rnd.NextDouble() * 6;
        var fig = new PathFigure { IsClosed = true, IsFilled = true };
        const int N = 48;
        for (int i = 0; i < N; i++)
        {
            double a = i / (double)N * Math.PI * 2;
            double k = 1 + 0.16 * Math.Sin(a * 3 + p1) + 0.09 * Math.Sin(a * 5 + p2) + 0.05 * Math.Sin(a * 9 + p3);
            var pt = new Point(cx + Math.Cos(a) * rx * k, cy + Math.Sin(a) * ry * k);
            if (i == 0) fig.StartPoint = pt; else fig.Segments.Add(new LineSegment(pt, true));
        }
        var g = new PathGeometry { Figures = { fig } };
        g.Freeze();
        return g;
    }
}
