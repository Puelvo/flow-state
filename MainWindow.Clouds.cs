using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowState;

// Çalış gökyüzündeki bulutlar: büyük, tombul piksel bulutlar. Uzaktakiler küçük, soluk ve yavaş; yakındakiler büyük ve hızlı.
// Konumları sahne genişliğinin oranı olarak tutulur; pencere büyüyünce gökyüzüne yeniden yayılırlar.
public partial class MainWindow
{
    sealed class SkyCloud { public Image Img = null!; public BitmapSource Base = null!; public double U, Y, Speed; }
    readonly List<SkyCloud> skyClouds = new();

    static (int Kind, double U, double Y, double Speed, double Alpha)[] CloudSet => PixelArt.CloudSet;
    const double CloudMax = 380;      // en geniş buluttan büyük: sahneden tam çıkmadan başa dönmez

    Canvas SkyClouds()
    {
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false, VerticalAlignment = System.Windows.VerticalAlignment.Top };
        skyClouds.Clear();
        for (int i = 0; i < CloudSet.Length; i++)
        {
            var (kind, u, y, speed, alpha) = CloudSet[i];
            var bmp = BigCloud(kind);
            var img = new Image { Source = bmp, Width = bmp.PixelWidth * PixelScale, Height = bmp.PixelHeight * PixelScale, Opacity = alpha };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            layer.Children.Add(img);
            skyClouds.Add(new SkyCloud { Img = img, Base = bmp, U = u, Y = y, Speed = speed });
        }
        return layer;
    }

    /// <summary>Bulutları ilerletir ve gökyüzüne yerleştirir (dt = 0: yalnızca yerleştirir).</summary>
    void TickClouds(double dt)
    {
        double w = SunScene.ActualWidth, sky = horizonY;
        if (w <= 0 || sky <= 0) return;
        foreach (var c in skyClouds)
        {
            double span = w + CloudMax;
            c.U = (c.U + c.Speed * dt / span) % 1;
            Canvas.SetLeft(c.Img, Math.Round(c.U * span - CloudMax));
            Canvas.SetTop(c.Img, Math.Round(c.Y * Math.Max(0, sky * 0.86 - c.Img.Height)));
        }
    }


    static BitmapSource BigCloud(int kind) => B(PixelArt.BigCloud(kind));
}
