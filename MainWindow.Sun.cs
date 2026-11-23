using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FlowState;

// Çalış = bahçe. Arkada gökyüzü (yıldızlar, bulutlar), piksel tepeler, çimen, dolaşan hayvanlar ve gezgin; önde güneş saat.
// Renkler gün döngüsünden gelir (DayCycle): varsayılan olarak bilgisayarın saatine göre, Ayarlar'dan sabit tema da seçilebilir.
public partial class MainWindow
{
    SkyPalette sunPalette = DayCycle.Day;
    string sunPaletteKey = "";
    Rectangle? skyRect, lightRect;
    Image? hillsImg;
    Border? grassLayer;
    Canvas? starLayer, cloudLayer, fireflyLayer;
    Canvas? sceneAnimals;
    TextBlock? sceneCoinText;
    ScaleTransform? sceneCoinPop;
    Border? sceneCoinPill;
    FrameworkElement? emptyHint;
    double horizonY = -1;
    string residentsKey = "";

    GardenTheme Theme => DayCycle.Parse(data.GardenTheme);

    static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>Güneş saatin ilerleme halkasının rengi (vaktin ışığı).</summary>
    Color RingColor() => C(sunPalette.Ray);

    // ---------- kurulum ----------

    void BuildSunScene()
    {
        SunScene.Children.Clear();

        skyRect = new Rectangle { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        starLayer = Stars();
        cloudLayer = SkyClouds();
        hillsImg = new Image { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(hillsImg, BitmapScalingMode.NearestNeighbor);
        grassLayer = new Border { Background = GrassBrush(), IsHitTestVisible = false };
        sceneAnimals = new Canvas();
        sceneAnimals.SizeChanged += (_, e) => { zooWorld.Width = e.NewSize.Width; zooWorld.Height = e.NewSize.Height; };
        lightRect = new Rectangle { IsHitTestVisible = false };
        fireflyLayer = Fireflies();

        // Sahne üstünde jeton hapı (sağ üst). Ödül jetonları buraya uçar.
        sceneCoinText = new TextBlock { FontSize = 17, FontWeight = FontWeights.Black, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 1) };
        var coinRow = new StackPanel { Orientation = Orientation.Horizontal };
        coinRow.Children.Add(CoinGlyph(24));
        coinRow.Children.Add(sceneCoinText);
        sceneCoinPop = new ScaleTransform();
        sceneCoinPill = new Border
        {
            Background = Res("Surface"), CornerRadius = new CornerRadius(20), Padding = new Thickness(6, 5, 14, 5), Child = coinRow,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 20, 0),
            Effect = (Effect)FindResource("SoftShadow"), RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = sceneCoinPop,
            Cursor = Cursors.Hand, ToolTip = "Jetonların. Dostlar sekmesinde yeni dostlar alabilirsin."
        };
        sceneCoinPill.MouseLeftButtonUp += (_, _) => NavZoo.IsChecked = true;

        // Bahçe boşken: çimende küçük bir davet.
        var hint = IconLabel(IconRes("IconPaw"), "Bahçen seni bekliyor. İlk dostunu Dostlar'dan al!", 13, 14, filled: true);
        TextElement_SetForeground(hint, Res("Text"));
        emptyHint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Child = hint, Cursor = Cursors.Hand
        };
        emptyHint.MouseLeftButtonUp += (_, _) => NavZoo.IsChecked = true;

        foreach (var el in new FrameworkElement[] { skyRect, starLayer, cloudLayer, hillsImg, grassLayer, sceneAnimals, lightRect, fireflyLayer, emptyHint, sceneCoinPill })
            SunScene.Children.Add(el);

        zooCanvas = sceneAnimals;
        residentsKey = "";
        SunScene.SizeChanged += (_, _) => LayoutScene();
        RingHost.SizeChanged += (_, _) => LayoutScene();
        PipsBar.SizeChanged += (_, _) => LayoutScene();
        BottomRow.SizeChanged += (_, _) => LayoutScene();
        TopRow.SizeChanged += (_, _) => LayoutScene();
        ApplySunPalette(force: true);
        EnsureZooTimer();
    }

    /// <summary>Ufuk çizgisi güneş saatin hemen altında; gökyüzü, tepeler, çimen ve hayvan alanı buna göre yerleşir.</summary>
    void LayoutScene()
    {
        if (skyRect == null || SunScene.ActualHeight <= 0) return;
        double h = SunScene.ActualHeight, w = SunScene.ActualWidth;
        // Ufuk sabit: güneş tepelerin üstünde, alt kenarı ufka hafifçe değer (yıldız çubuğu açılıp kapanınca sahne oynamaz).
        double bottom = RingHost.TranslatePoint(new Point(0, RingHost.ActualHeight), SunScene).Y;
        double horizon = Math.Clamp(bottom + 4, h * 0.42, h - 190);
        UpdateAvoid(horizon);
        if (Math.Abs(horizon - horizonY) < 1 && hillsImg!.Width >= w) return;
        horizonY = horizon;

        skyRect.Height = horizon + 4;
        starLayer!.Height = horizon;
        cloudLayer!.Margin = new Thickness(0, 10, 0, 0);
        UpdateHills(w);
        hillsImg!.Margin = new Thickness(0, horizon - 41 * PixelScale, 0, 0);
        grassLayer!.Margin = new Thickness(0, horizon - 9, 0, 0);
        sceneAnimals!.Margin = new Thickness(0, horizon - 44, 0, 0);
        lightRect!.Margin = new Thickness(0, horizon - 9, 0, 0);
        fireflyLayer!.Margin = new Thickness(0, horizon, 0, 0);
        emptyHint!.Margin = new Thickness(0, 0, 0, Math.Max(150, (h - horizon) * 0.5));
    }

    /// <summary>Hayvanlar ve gezgin çimendeki düğmelerin (ve yıldız çubuğunun) arkasında durmasın.</summary>
    void UpdateAvoid(double horizon)
    {
        if (sceneAnimals == null || BottomRow.ActualWidth <= 0) return;
        double top = horizon - 44;                            // hayvan tuvalinin üstü
        var tl = BottomRow.TranslatePoint(new Point(0, 0), SunScene);
        // Ayak noktası düğmelerin üstünden ~40 px yukarıdan alta kadar ve iki yanda 50 px pay.
        zooWorld.Avoid = (tl.X - 50, tl.Y - top - 10, tl.X + BottomRow.ActualWidth + 50, double.MaxValue);
    }

    // ---------- her saniye ----------

    /// <summary>Çalış görünümü açıkken: renkleri saate göre günceller, hayvanları ve gezginin havasını eşitler.</summary>
    void UpdateSunScene()
    {
        if (skyRect == null) return;
        ApplySunPalette(force: false);
        SyncResidents();

        int balance = Zoo.Balance(data);
        if (sceneCoinText!.Text != balance.ToString())
        {
            bool first = sceneCoinText.Text.Length == 0;
            sceneCoinText.Text = balance.ToString();
            if (!first && sceneCoinPop != null) Pop(sceneCoinPop, 1.25);
        }

        zooWorld.Mood = !active ? ZooMood.Free : sw.IsRunning ? ZooMood.Calm : ZooMood.Play;
        zooWorld.KeeperWalking = active && sw.IsRunning;
        zooWorld.Night = DayCycle.IsNight(sunPalette);
        emptyHint!.Visibility = zooWorld.Animals.Count == 0 && !widgetMode ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Bahçedeki hayvanları (sahip olunanlar + nadir dostlar) sahne tuvaliyle eşitler.</summary>
    void SyncResidents()
    {
        if (sceneAnimals == null) return;
        var owned = ZooCatalog.InMap(ActiveMapId).Where(a => ZooDemo || Zoo.Owns(data, a.Id)).ToList();
        var specials = ZooDemo ? new List<Special>() : Specials().Where(x => owned.Any(a => a.Id == x.AnimalId)).ToList();
        var key = string.Join(",", owned.Select(a => a.Id)) + "|" + string.Join(",", specials.Select(x => x.Key));
        if (key == residentsKey) return;
        residentsKey = key;

        // Gezgin dışındakiler yeniden kurulur; konumlar dünyada saklı olduğu için kimse yerinden oynamaz.
        foreach (var n in zooNodes) sceneAnimals.Children.Remove(n.Root);
        zooNodes.Clear();
        string? arriving = zooArriving;
        zooArriving = null;
        var keys = owned.Select(a => a.Id).Concat(specials.Select(x => x.Key)).ToHashSet();
        zooWorld.Animals.RemoveAll(w => !keys.Contains(w.Id));
        foreach (var a in owned)
        {
            var w = zooWorld.Animals.FirstOrDefault(x => x.Id == a.Id) ?? zooWorld.Add(a.Id, arriving: a.Id == arriving);
            AddNode(sceneAnimals, a, w, null);
        }
        foreach (var sp in specials)
        {
            var a = ZooCatalog.Find(sp.AnimalId)!;
            var w = zooWorld.Animals.FirstOrDefault(x => x.Id == sp.Key) ?? zooWorld.Add(sp.Key, arriving: sp.Key == arriving);
            if (sp.Kind == SpecialKind.Baby) w.Follow = zooWorld.Animals.FirstOrDefault(x => x.Id == sp.AnimalId);
            AddNode(sceneAnimals, a, w, sp.Kind);
        }
    }

    // ---------- renkler ----------

    /// <summary>Vaktin paletini sahneye ve güneş saate uygular (yalnızca renkler değişince).</summary>
    void ApplySunPalette(bool force)
    {
        var p = DayCycle.At(DateTime.Now, Theme);
        var key = $"{p.SkyTop:X}{p.SkyBottom:X}{p.Light:X}{p.Ray:X}{p.DiscA:X}{p.Stars:F2}{p.Fireflies:F2}";
        if (!force && key == sunPaletteKey) return;
        bool lightChanged = p.Light != sunPalette.Light || force;
        sunPaletteKey = key;
        sunPalette = p;

        if (skyRect != null)
        {
            skyRect.Fill = new LinearGradientBrush(C(p.SkyTop), C(p.SkyBottom), 90);
            lightRect!.Fill = new SolidColorBrush(C(p.Light));
            starLayer!.Opacity = p.Stars;
            fireflyLayer!.Opacity = p.Fireflies;
            fireflyLayer.Visibility = p.Fireflies > 0.02 ? Visibility.Visible : Visibility.Collapsed;
            cloudLayer!.Opacity = p.Clouds;
            if (lightChanged && SunScene.ActualWidth > 0) { hillsFor = ""; UpdateHills(SunScene.ActualWidth); }
        }

        // Güneş saat: disk (güneş ya da ay), hâle, iz halkası ve ilerleme halkası.
        RingFace.Fill = new RadialGradientBrush(C(p.DiscA), C(p.DiscB)) { GradientOrigin = new Point(0.42, 0.36), Center = new Point(0.45, 0.42), RadiusX = 0.62, RadiusY = 0.62 };
        RingFace.Stroke = new SolidColorBrush(Color.FromArgb(0xC0, C(p.Ray).R, C(p.Ray).G, C(p.Ray).B));
        var glow = C(p.Glow);
        SunGlow.Fill = new RadialGradientBrush(
            new GradientStopCollection { new(glow, 0.55), new(Color.FromArgb((byte)(glow.A / 3), glow.R, glow.G, glow.B), 0.78), new(Color.FromArgb(0, glow.R, glow.G, glow.B), 1) });
        SunTrack.Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        if (widgetMode) ApplyWidgetBackground();
        UpdateRing();
    }

    // ---------- katmanlar ----------

    string hillsFor = "";

    /// <summary>Piksel tepeler ve akasyalar (genişliğe göre üretilir); renkleri vaktin ışığıyla karışır.</summary>
    void UpdateHills(double widthPx)
    {
        int w = (int)Math.Ceiling(widthPx / PixelScale / 64.0) * 64 + 64;
        var key = $"{w}|{sunPalette.Light:X}";
        if (key == hillsFor || hillsImg == null) return;
        hillsFor = key;
        hillsImg.Source = HillsBitmap(w, C(sunPalette.Light));
        hillsImg.Width = w * PixelScale;
        hillsImg.Height = 48 * PixelScale;
    }

    static BitmapSource HillsBitmap(int W, Color light)
    {
        const int H = 48;
        var px = new uint[W * H];
        double la = light.A / 255.0;
        uint Tint(uint c)
        {
            double r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            r += (light.R - r) * la; g += (light.G - g) * la; b += (light.B - b) * la;
            return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        }
        void Set(int x, int y, uint c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = Tint(c); }

        for (int x = 0; x < W; x++)
        {
            double far = 27 + 3.5 * Math.Sin(x * 0.035 + 1) + 2 * Math.Sin(x * 0.09);
            double mid = 34 + 3 * Math.Sin(x * 0.05 + 2.2) + 1.5 * Math.Sin(x * 0.13);
            double near = 41 + 2 * Math.Sin(x * 0.07 + 0.5) + 1 * Math.Sin(x * 0.19);
            int yf = (int)Math.Round(far), ym = (int)Math.Round(mid), yn = (int)Math.Round(near);
            for (int y = yf; y < H; y++)
            {
                uint c = y == yf ? 0xFFE0F2CBu : 0xFFCBE8B0u;
                if (y >= ym) c = y == ym ? 0xFFC9E8A9u : 0xFFB2DC8Eu;
                if (y >= yn) { if (y == yn) c = 0xFF9DD271u; else break; }          // altı saydam: çimen dokusu
                Set(x, y, c);
            }
        }
        // Akasyalar (orta tepede), genişliğe yayılmış.
        for (int tx = 52, k = 0; tx < W - 10; tx += 120 + (k * 37) % 70, k++)
        {
            int crown = 9 + (k * 5) % 9;
            int baseY = (int)Math.Round(34 + 3 * Math.Sin(tx * 0.05 + 2.2) + 1.5 * Math.Sin(tx * 0.13));
            int top = baseY - 7;
            for (int y = top; y < baseY + 1; y++) Set(tx, y, 0xFF9C7A54u);
            Set(tx - 1, top + 2, 0xFF9C7A54u); Set(tx + 1, top + 1, 0xFF9C7A54u);
            for (int r = 0; r < 3; r++)
            {
                int half = crown / 2 - (r == 0 ? 2 : r == 2 ? 1 : 0);
                for (int x = tx - half; x <= tx + half; x++) Set(x, top - 2 + r, r == 0 ? 0xFF9FD07Au : 0xFF84BD62u);
            }
        }
        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Gökyüzünde piksel yıldızlar (gece görünür), yavaşça parıldar.</summary>
    Canvas Stars()
    {
        var layer = new Canvas { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top, ClipToBounds = true };
        var rnd = new Random(5);
        for (int i = 0; i < 60; i++)
        {
            bool big = i % 7 == 0;
            FrameworkElement star = big
                ? new Path { Data = Geometry.Parse("M1,0 L2,0 L2,1 L3,1 L3,2 L2,2 L2,3 L1,3 L1,2 L0,2 L0,1 L1,1 Z"), Fill = Brushes.White, Width = 9, Height = 9, Stretch = Stretch.Fill }
                : new Rectangle { Width = 3, Height = 3, Fill = Brushes.White };
            star.Tag = (rnd.NextDouble(), rnd.NextDouble() * 0.85);
            layer.Children.Add(star);
            var tw = new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(1.5 + rnd.NextDouble() * 3))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 4) };
            star.BeginAnimation(OpacityProperty, tw);
        }
        layer.SizeChanged += (_, e) =>
        {
            foreach (FrameworkElement s in layer.Children)
            {
                var (fx, fy) = ((double, double))s.Tag;
                Canvas.SetLeft(s, Math.Round(fx * e.NewSize.Width / 3) * 3);
                Canvas.SetTop(s, Math.Round(fy * e.NewSize.Height / 3) * 3);
            }
        };
        return layer;
    }

    /// <summary>Gökyüzünde yavaşça süzülen piksel bulutlar.</summary>
    Canvas SkyClouds()
    {
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false, VerticalAlignment = VerticalAlignment.Top };
        var list = new (int Kind, double Top, double Seconds, double Phase)[] { (0, 30, 170, 0.1), (1, 90, 210, 0.45), (2, 54, 250, 0.8), (0, 140, 230, 0.62) };
        foreach (var c in list)
        {
            var bmp = CloudBitmap(c.Kind);
            var img = new Image { Source = bmp, Width = bmp.PixelWidth * PixelScale, Height = bmp.PixelHeight * PixelScale };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetTop(img, c.Top);
            layer.Children.Add(img);
            var drift = new DoubleAnimation(-img.Width, 2400, TimeSpan.FromSeconds(c.Seconds * 2))
            { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-c.Seconds * 2 * c.Phase) };
            img.BeginAnimation(Canvas.LeftProperty, drift);
        }
        return layer;
    }

    /// <summary>Çimenin üstünde uçuşan ateş böcekleri (gece).</summary>
    Canvas Fireflies()
    {
        var layer = new Canvas { IsHitTestVisible = false };
        var rnd = new Random(9);
        for (int i = 0; i < 16; i++)
        {
            var glow = new Ellipse
            {
                Width = 18, Height = 18,
                Fill = new RadialGradientBrush(new GradientStopCollection { new(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xC8), 0), new(Color.FromArgb(0xAA, 0xE8, 0xFF, 0x7A), 0.25), new(Color.FromArgb(0, 0xE8, 0xFF, 0x7A), 1) })
            };
            var tt = new TranslateTransform();
            glow.RenderTransform = tt;
            glow.Tag = (rnd.NextDouble(), rnd.NextDouble());
            layer.Children.Add(glow);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-30 - rnd.Next(30), 30 + rnd.Next(30), TimeSpan.FromSeconds(4 + rnd.NextDouble() * 4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 6) });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-20 - rnd.Next(20), 20 + rnd.Next(20), TimeSpan.FromSeconds(3 + rnd.NextDouble() * 3)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 6) });
            glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1 + rnd.NextDouble() * 1.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 3) });
        }
        layer.SizeChanged += (_, e) =>
        {
            foreach (FrameworkElement f in layer.Children)
            {
                var (fx, fy) = ((double, double))f.Tag;
                Canvas.SetLeft(f, fx * e.NewSize.Width);
                Canvas.SetTop(f, 20 + fy * Math.Max(0, e.NewSize.Height - 200));
            }
        };
        return layer;
    }

    // ---------- Ayarlar: bahçe teması ----------

    /// <summary>Ayarlar'daki tema kutuları: Otomatik + dört vakit; her biri küçük bir gökyüzü önizlemesi.</summary>
    void BuildGardenThemeTiles()
    {
        GardenThemePanel.Children.Clear();
        var cur = Theme;
        foreach (var (t, name, sub) in new[] {
            (GardenTheme.Auto, "Otomatik", "Saate göre"), (GardenTheme.Dawn, "Şafak", ""), (GardenTheme.Day, "Gündüz", ""),
            (GardenTheme.Sunset, "Gün batımı", ""), (GardenTheme.Night, "Gece", "") })
        {
            var p = t == GardenTheme.Auto ? DayCycle.At(DateTime.Now, GardenTheme.Auto) : DayCycle.At(DateTime.Now, t);
            bool sel = t == cur;
            var preview = new Grid { Width = 104, Height = 70 };
            preview.Children.Add(new Border { CornerRadius = new CornerRadius(14), Background = new LinearGradientBrush(C(p.SkyTop), C(p.SkyBottom), 90) });
            preview.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(0, 0, 14, 14), Height = 20, VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Mix(Color.FromRgb(0x8D, 0xC9, 0x63), C(p.Light), C(p.Light).A / 255.0))
            });
            var disc = new Ellipse { Width = 24, Height = 24, Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                Fill = new RadialGradientBrush(C(p.DiscA), C(p.DiscB)) };
            preview.Children.Add(disc);
            if (p.Stars > 0.5)
                foreach (var (x, y) in new[] { (16.0, 12.0), (82.0, 18.0), (26.0, 30.0), (74.0, 8.0) })
                    preview.Children.Add(new Rectangle { Width = 3, Height = 3, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0) });
            if (t == GardenTheme.Auto)
            {
                var auto = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 1, 6, 2), Margin = new Thickness(0, 0, 6, 4),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
                    Child = new TextBlock { Text = "A", FontSize = 11, FontWeight = FontWeights.Black, Foreground = Res("Text") } };
                preview.Children.Add(auto);
            }
            var frame = new Border
            {
                CornerRadius = new CornerRadius(18), Padding = new Thickness(4), BorderThickness = new Thickness(3), Child = preview,
                BorderBrush = sel ? Res("Accent") : Brushes.Transparent
            };
            if (sel) frame.SetResourceReference(Border.BorderBrushProperty, "Accent");
            var label = new TextBlock { Text = name, FontSize = 13, FontWeight = sel ? FontWeights.Black : FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res("Text"), Margin = new Thickness(0, 4, 0, 0) };
            var tile = new StackPanel { Margin = new Thickness(0, 0, 10, 8), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            tile.Children.Add(frame);
            tile.Children.Add(label);
            if (sub.Length > 0) tile.Children.Add(new TextBlock { Text = sub, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
            var pick = t;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                data.GardenTheme = DayCycle.Key(pick);
                Store.Save(data);
                ApplySunPalette(force: true);
                BuildGardenThemeTiles();
            };
            GardenThemePanel.Children.Add(tile);
        }
    }
}
