using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FlowState;

// Bahçenin ekleri: nadir dostlar (altın renkli ve yavru hâller, koleksiyon kartı) ve oturum sonrası gezgin ziyareti.
public partial class MainWindow
{
    const double BabyScale = 0.62;

    // ---------- nadir dostlar ----------

    string specialsKey = "";
    List<Special> specialsCache = new();

    /// <summary>Kazanılmış nadir dostlar (seans/hayvan listesi değişmedikçe yeniden hesaplanmaz).</summary>
    List<Special> Specials()
    {
        var key = $"{data.Sessions.Count}|{data.OwnedAnimals.Count}|{data.Sessions.LastOrDefault()?.Id}";
        if (key != specialsKey) { specialsKey = key; specialsCache = ZooSpecials.All(data); }
        return specialsCache;
    }

    static readonly Dictionary<BitmapSource, BitmapSource> goldCache = new();

    /// <summary>Sprite'ı altına boyar: parlaklığa göre koyu bakır → altın → açık krem; kontur koyu kalır.</summary>
    static BitmapSource Golden(BitmapSource src)
    {
        if (goldCache.TryGetValue(src, out var g)) return g;
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[h * stride];
        conv.CopyPixels(px, stride, 0);
        for (int i = 0; i < px.Length; i += 4)
        {
            if (px[i + 3] == 0) continue;
            double l = (0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2]) / 255.0;
            (double r, double gg, double b) c = l < 0.16 ? (70, 42, 10)                     // kontur
                : l < 0.45 ? Lerp((150, 92, 14), (226, 160, 30), (l - 0.16) / 0.29)
                : l < 0.75 ? Lerp((226, 160, 30), (255, 214, 86), (l - 0.45) / 0.30)
                : Lerp((255, 214, 86), (255, 246, 200), (l - 0.75) / 0.25);
            px[i] = (byte)c.b; px[i + 1] = (byte)c.gg; px[i + 2] = (byte)c.r;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, stride);
        bmp.Freeze();
        return goldCache[src] = bmp;

        static (double, double, double) Lerp((double, double, double) a, (double, double, double) b, double t)
            => (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t);
    }

    static BitmapSource[] GoldenFrames(string id) => Frames(id).Select(Golden).ToArray();
    static BitmapSource?[] GoldenPoses(string id) => Poses(id).Select(p => p == null ? null : Golden(p)).ToArray();

    /// <summary>Nadir dostun hareketsiz görseli (koleksiyon, ödül kartı). Yavru küçük, altın boyalı.</summary>
    FrameworkElement SpecialImage(string animalId, SpecialKind kind, double size, bool locked)
    {
        BitmapSource src;
        if (locked) src = (BitmapSource)SpriteImage(animalId, size, silhouette: true).Source;
        else
        {
            var p = Poses(animalId)[0]!;
            src = kind == SpecialKind.Golden ? Golden(p) : p;
        }
        double s = kind == SpecialKind.Baby ? size * 0.72 : size;
        var img = new Image { Width = s, Height = s, Source = src, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        var host = new Grid { Width = size, Height = size };
        if (locked) img.Opacity = kind == SpecialKind.Golden ? 0.55 : 0.5;
        host.Children.Add(img);
        if (kind == SpecialKind.Golden && !locked)
        {
            var spark = new Path { Data = (Geometry)FindResource("IconSparkle"), Fill = Res("Coin"), Width = size * 0.2, Height = size * 0.2, Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, RenderTransformOrigin = new Point(0.5, 0.5) };
            var sc = new ScaleTransform(1, 1);
            spark.RenderTransform = sc;
            host.Children.Add(spark);
            Twinkle(spark, sc, 0);
        }
        return host;
    }

    /// <summary>Koleksiyon kartı: haritadaki her hayvanın altın ve yavru hâli; kazanılmamışlar siluet.</summary>
    FrameworkElement CollectionCard()
    {
        var got = Specials().Select(x => x.Key).ToHashSet();
        var animals = ZooCatalog.InMap(ActiveMapId).ToList();
        int total = animals.Count * 2, have = animals.Count(a => got.Contains(Special.Keyed(a.Id, SpecialKind.Golden))) + animals.Count(a => got.Contains(Special.Keyed(a.Id, SpecialKind.Baby)));

        var body = new StackPanel();
        var head = new DockPanel();
        var count = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), VerticalAlignment = VerticalAlignment.Center, Background = Res("WarnSoft") };
        DockPanel.SetDock(count, Dock.Right);
        count.Child = new TextBlock { Text = $"{have} / {total}", FontSize = 13, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0x84, 0x10)) };
        head.Children.Add(count);
        var title = IconLabel(IconRes("IconSparkle"), "Nadir dostlar", 16, 16, filled: true);
        TextElement_SetForeground(title, Res("Text"));
        head.Children.Add(title);
        body.Children.Add(head);
        body.Children.Add(new TextBlock
        {
            Text = have == 0 ? "Uzun oturumlar bazen bir sürpriz getirir: altın bir dost ya da minik bir yavru."
                 : have == total ? "Koleksiyon tamam! Bütün nadir dostlar bahçende." : "Uzun oturumlar bazen bir sürpriz getirir.",
            Style = (Style)FindResource("PageSub"), FontSize = 13
        });

        foreach (var kind in new[] { SpecialKind.Golden, SpecialKind.Baby })
        {
            var row = new WrapPanel { Margin = new Thickness(-6, 12, 0, 0) };
            foreach (var a in animals)
            {
                bool owned = got.Contains(Special.Keyed(a.Id, kind));
                var stand = new Grid { Width = 74, Height = 74 };
                stand.Children.Add(new Ellipse
                {
                    Fill = owned ? (kind == SpecialKind.Golden ? Res("WarnSoft") : Res("AccentSoft")) : Res("Panel2"),
                    Width = 66, Height = 66
                });
                stand.Children.Add(SpecialImage(a.Id, kind, 60, locked: !owned));
                var label = new TextBlock
                {
                    Text = owned ? Special.Title(a.Name, kind) : "???", FontSize = 11.5, FontWeight = FontWeights.ExtraBold,
                    HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res(owned ? "Text" : "Faint"), Margin = new Thickness(0, 4, 0, 0)
                };
                var tile = new StackPanel { Margin = new Thickness(6, 0, 6, 4), Width = 80 };
                tile.Children.Add(stand);
                tile.Children.Add(label);
                if (!owned) tile.ToolTip = kind == SpecialKind.Golden ? "Altın bir dost" : "Minik bir yavru";
                row.Children.Add(tile);
            }
            body.Children.Add(row);
        }
        return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 18, 0, 0), Child = body };
    }

    // ---------- gezgin (bahçede hep var) ----------

    bool explorerVisitPending;            // oturum bitti: bahçe göründüğünde hayvanlar gezgini kutlar
    Canvas? visitorRoot, visitorCanvas;
    Image? visitorImg;
    TranslateTransform? visitorHop;
    ScaleTransform? visitorFlip;
    FrameworkElement? visitorBubble;
    const double VisitorSize = 92;

    /// <summary>Bahçe tikinde: bekleyen kutlamayı başlatır ve gezgini çizer.</summary>
    void TickKeeper()
    {
        if (zooCanvas == null || zooWorld.Width <= 0) return;
        if (explorerVisitPending && zooWorld.Animals.Count > 0)
        {
            explorerVisitPending = false;
            zooWorld.Celebrate();
        }
        if (visitorRoot == null || visitorCanvas != zooCanvas) BuildVisitorNode();
        var v = zooWorld.Keeper;
        if (double.IsNaN(v.X)) { visitorRoot!.Visibility = Visibility.Hidden; return; }
        visitorRoot!.Visibility = Visibility.Visible;

        var frames = ExplorerFrames();
        var frame = v.State == WanderState.Walk && frames.Length > 1 ? frames[1 + (int)(v.WalkTime * 8) % (frames.Length - 1)] : frames[0];
        if (!ReferenceEquals(visitorImg!.Source, frame)) visitorImg.Source = frame;
        visitorFlip!.ScaleX = v.FacingRight ? 1 : -1;
        visitorHop!.Y = Math.Round(v.HopOffset * 0.7);
        Canvas.SetLeft(visitorRoot, Math.Round(v.X - VisitorSize / 2));
        Canvas.SetTop(visitorRoot, Math.Round(v.Y - VisitorSize + 2));
        Panel.SetZIndex(visitorRoot, (int)v.Y);
        visitorBubble!.Visibility = zooWorld.Celebrating > ZooWorld.CelebrateSeconds - 4 ? Visibility.Visible : Visibility.Collapsed;
    }
    void BuildVisitorNode()
    {
        if (visitorRoot != null) (visitorRoot.Parent as Panel)?.Children.Remove(visitorRoot);
        visitorCanvas = zooCanvas;
        var shadow = new Ellipse { Width = 44, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(0x48, 0x2E, 0x5A, 0x1C)) };
        Canvas.SetLeft(shadow, (VisitorSize - 44) / 2); Canvas.SetTop(shadow, VisitorSize - 10);
        visitorFlip = new ScaleTransform(1, 1, VisitorSize / 2, VisitorSize / 2);
        visitorHop = new TranslateTransform();
        visitorImg = new Image { Width = VisitorSize, Height = VisitorSize, IsHitTestVisible = false, RenderTransform = new TransformGroup { Children = { visitorFlip, visitorHop } } };
        RenderOptions.SetBitmapScalingMode(visitorImg, BitmapScalingMode.HighQuality);

        var text = new TextBlock { Text = "Selam dostlar!", FontSize = 12, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text") };
        var face = new Border { Background = Res("Surface"), CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 2, 9, 3), Child = text };
        var tail = new Path { Data = Geometry.Parse("M0,0 L10,0 L5,6 Z"), Fill = Res("Surface"), Width = 10, Height = 6, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -1, 0, 0) };
        var bubble = new StackPanel { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        bubble.Children.Add(face);
        bubble.Children.Add(tail);
        bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(bubble, Math.Round((VisitorSize - bubble.DesiredSize.Width) / 2));
        Canvas.SetTop(bubble, -18);
        visitorBubble = bubble;

        visitorRoot = new Canvas { Width = VisitorSize, Height = VisitorSize, IsHitTestVisible = false };
        visitorRoot.Children.Add(shadow);
        visitorRoot.Children.Add(visitorImg);
        visitorRoot.Children.Add(bubble);
        zooCanvas!.Children.Add(visitorRoot);
    }
}
