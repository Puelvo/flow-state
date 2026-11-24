using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowState;

// Çalış gökyüzündeki bulutlar: büyük, tombul piksel bulutlar. Uzaktakiler küçük, soluk ve yavaş; yakındakiler büyük ve hızlı.
// Konumları sahne genişliğinin oranı olarak tutulur; pencere büyüyünce gökyüzüne yeniden yayılırlar.
public partial class MainWindow
{
    sealed class SkyCloud { public Image Img = null!; public double U, Y, Speed; }
    readonly List<SkyCloud> skyClouds = new();

    /// <summary>
    /// (çeşit, yatay başlangıç oranı, gökyüzündeki yükseklik oranı, hız px/sn, saydamlık). Uzaktakiler önce (arkada).
    /// Aynı katmandakiler aynı hızdadır; aralıkları hiç kapanmaz, bulutlar yığılmaz.
    /// </summary>
    static readonly (int Kind, double U, double Y, double Speed, double Alpha)[] CloudSet =
    {
        // uzak
        (3, 0.16, 0.58, 3, 0.62), (4, 0.36, 0.95, 3, 0.62), (3, 0.56, 0.40, 3, 0.62), (4, 0.76, 0.08, 3, 0.62), (3, 0.97, 0.88, 3, 0.62),
        // orta
        (1, 0.08, 0.05, 6, 0.88), (3, 0.31, 0.22, 6, 0.88), (1, 0.64, 0.90, 6, 0.88), (1, 0.88, 0.45, 6, 0.88),
        // yakın
        (0, 0.24, 0.66, 9, 1.00), (2, 0.50, 0.04, 9, 1.00), (0, 0.80, 0.78, 9, 1.00), (2, 0.03, 0.34, 9, 1.00)
    };
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
            skyClouds.Add(new SkyCloud { Img = img, U = u, Y = y, Speed = speed });
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

    static readonly BitmapSource?[] bigClouds = new BitmapSource?[5];

    /// <summary>Üst üste binen yuvarlaklardan tombul bulut: beyaz tepe, her yuvarlağın altında ve tabanda açık mavi gölge.</summary>
    static BitmapSource BigCloud(int kind)
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
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bigClouds[kind] = bmp;
    }
}
