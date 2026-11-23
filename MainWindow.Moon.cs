using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlowState;

// Gece güneş saatin yüzü: sade ve sevimli ay. Neredeyse düz, sıcak beyaz disk; sol kenarda yumuşak hilal gölgesi;
// düz renkli, iç içe iki daireden kraterler (yazının arkası boş kalır).
public partial class MainWindow
{
    const double MoonD = 244;

    void BuildMoon()
    {
        MoonDeco.Children.Clear();

        // Hilal gölgesi: diskten, sağ üste kaydırılmış bir daire çıkarılır; kalan sol-alt kenar yumuşakça koyulaşır.
        var disc = new EllipseGeometry(new Point(MoonD / 2, MoonD / 2), MoonD / 2, MoonD / 2);
        var lit = new EllipseGeometry(new Point(MoonD * 0.61, MoonD * 0.53), MoonD * 0.47, MoonD * 0.47);
        MoonDeco.Children.Add(new Path
        {
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, disc, lit),
            Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0xC4, 0xC6, 0xD4)),
            Effect = new BlurEffect { Radius = 14 }
        });

        // Kraterler: (x, y, yarıçap) diskin oranı olarak.
        var canvas = new System.Windows.Controls.Canvas { Width = MoonD, Height = MoonD };
        foreach (var (x, y, r) in new[] { (0.27, 0.30, 0.075), (0.72, 0.22, 0.05), (0.80, 0.70, 0.085), (0.30, 0.78, 0.06), (0.55, 0.86, 0.04), (0.12, 0.55, 0.035), (0.88, 0.45, 0.03) })
        {
            double cx = x * MoonD, cy = y * MoonD, R = r * MoonD;
            var rim = new Ellipse { Width = R * 2, Height = R * 2, Fill = new SolidColorBrush(Color.FromRgb(0xDE, 0xDB, 0xD2)) };
            System.Windows.Controls.Canvas.SetLeft(rim, cx - R); System.Windows.Controls.Canvas.SetTop(rim, cy - R);
            var bowl = new Ellipse { Width = R * 1.68, Height = R * 1.55, Fill = new SolidColorBrush(Color.FromRgb(0xD0, 0xCD, 0xC5)) };
            System.Windows.Controls.Canvas.SetLeft(bowl, cx - R * 0.78); System.Windows.Controls.Canvas.SetTop(bowl, cy - R * 0.6);
            canvas.Children.Add(rim);
            canvas.Children.Add(bowl);
        }
        MoonDeco.Children.Add(canvas);
    }
}
