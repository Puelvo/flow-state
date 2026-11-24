using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace FlowState;

// Bahçenin süsleri ve seri ağacı:
//  - Süsler marketten jetonla alınır (çit, çiçekler, tabela, kamp ateşi, çadır, salıncak); kendiliğinden gelmez.
//  - Seri ağacı: güncel seriyle büyür (filiz → fidan → genç ağaç → ağaç → meyveli ağaç). Seri bozulursa kurumaz;
//    ulaştığı boyda kalır ama yaprakları dökülür, yeniden başlayınca yeşerir.
public partial class MainWindow
{
    const int MaxFlowers = 14;

    static readonly string[] StageNames = { "Tohum", "Filiz", "Fidan", "Genç ağaç", "Ağaç", "Meyveli ağaç" };

    static int StageOf(int days) => days <= 0 ? 0 : days <= 2 ? 1 : days <= 6 ? 2 : days <= 13 ? 3 : days <= 29 ? 4 : 5;

    readonly List<UIElement> growthEls = new();
    string growthKey = "", growthCalcKey = "";
    int growthDays;
    bool growthBare;
    int flowersShown = -1;
    bool flowersBloom;                    // az önce alındı: açılış animasyonu
    Image? campfireImg;
    Ellipse? campGlow;
    readonly List<(double L, double T, double R, double B)> growthBlocks = new();
    readonly List<(Image Img, int Index)> flowerImgs = new();

    /// <summary>Güncel seri; sıfırsa en son biten serinin uzunluğu (ağaç o boyda, yapraksız kalır).</summary>
    (int Days, bool Bare) TreeDays()
    {
        var per = PerDay();
        int streak = Streak(per);
        if (streak > 0) return (streak, false);
        var d = Today.AddDays(-1);
        for (int i = 0; i < 400 && per.GetValueOrDefault(d) < StatsMath.MinDaySeconds; i++) d = d.AddDays(-1);
        int n = 0;
        while (per.GetValueOrDefault(d) >= StatsMath.MinDaySeconds) { n++; d = d.AddDays(-1); }
        return (n, n > 0);
    }

    // ---------- her saniye ----------

    void UpdateGrowth()
    {
        if (sceneAnimals == null || zooWorld.Width <= 0 || decorFor.Length == 0) return;
        // Seri yalnızca oturum listesi, gün ya da (süren oturumda) dakika değişince yeniden hesaplanır.
        var calcKey = $"{data.Sessions.Count}|{Today:yyyyMMdd}|{(active ? ActiveSeconds / 60 : -1)}";
        if (calcKey != growthCalcKey)
        {
            growthCalcKey = calcKey;
            (growthDays, growthBare) = TreeDays();
        }
        var (days, bare) = (growthDays, growthBare);
        var owned = ZooCatalog.Decors.Where(x => Zoo.Owns(data, x.Key)).Select(x => x.Id).ToList();
        int stage = StageOf(days), flowers = owned.Contains("flowers") ? MaxFlowers : 0;
        var key = $"{decorFor}|{Today:yyyyMMdd}|{stage}|{bare}|{string.Join(",", owned)}";
        if (key != growthKey)
        {
            growthKey = key;
            RebuildGrowth(stage, bare, days, owned);
            flowersShown = -1;
        }
        if (flowers != flowersShown) SetFlowers(flowers);
        if (campfireImg != null) campfireImg.Source = Campfire()[(int)(Environment.TickCount64 / 500) % 2];
        if (campGlow != null) campGlow.Opacity = 0.25 + 0.75 * Math.Clamp(sunPalette.Moon, 0, 1);       // gece parlar
    }

    void RebuildGrowth(int stage, bool bare, int days, List<string> unlocked)
    {
        foreach (var e in growthEls) sceneAnimals!.Children.Remove(e);
        growthEls.Clear();
        foreach (var f in flowerImgs) sceneAnimals!.Children.Remove(f.Img);
        flowerImgs.Clear();
        campfireImg = null; campGlow = null;
        foreach (var b in growthBlocks) zooWorld.Blocks.Remove(b);
        growthBlocks.Clear();
        void Block((double L, double T, double R, double B) b) { zooWorld.Blocks.Add(b); growthBlocks.Add(b); }
        double W = zooWorld.Width, top = zooWorld.Top, bottom = zooWorld.Bottom, band = Math.Max(40, bottom - top);

        void Put(UIElement el, double left, double topY, int z)
        {
            Canvas.SetLeft(el, Math.Round(left)); Canvas.SetTop(el, Math.Round(topY)); Panel.SetZIndex(el, z);
            sceneAnimals!.Children.Add(el); growthEls.Add(el);
        }

        // Seri ağacı
        {
            double x = W * 0.60, y = top + 30;
            var img = PixImage(StreakTree(stage, bare));
            img.IsHitTestVisible = true;
            img.ToolTip = stage == 0 ? "Seri ağacı: bugün çalış, filizlensin."
                : bare ? $"Seri ağacı: {StageNames[stage]} · serin bitti, yeniden başlayınca yeşerir"
                : $"Seri ağacı: {StageNames[stage]} · {days} günlük seri";
            Put(img, x - img.Width / 2, y - img.Height, (int)y);
            Block((x - 12, y - 10, x + 12, y + 6));
        }

        // Satın alınmış süsler
        foreach (var id in unlocked)
            switch (id)
            {
                case "fence":
                    var fence = PixImage(Fence());
                    Put(fence, W * 0.04, bottom - fence.Height + 10, (int)bottom + 4);
                    break;
                case "sign":
                    double sx = W * 0.20, sy = top + band - 40;
                    var sign = PixImage(Sign());
                    sign.IsHitTestVisible = true; sign.ToolTip = "Flow State bahçesi";
                    Put(sign, sx - sign.Width / 2, sy - sign.Height, (int)sy);
                    Block((sx - 18, sy - 10, sx + 18, sy + 6));
                    break;
                case "campfire":
                    double cx = W * 0.24, cy = top + band * 0.35;
                    var glow = new Ellipse { Width = 200, Height = 84, IsHitTestVisible = false,
                        Fill = new RadialGradientBrush(Color.FromArgb(0x70, 0xFF, 0xB8, 0x5A), Color.FromArgb(0, 0xFF, 0xB8, 0x5A)) };
                    campGlow = glow;
                    Put(glow, cx - 100, cy - 56, (int)cy - 2);
                    campfireImg = PixImage(Campfire()[0]);
                    Put(campfireImg, cx - campfireImg.Width / 2, cy - campfireImg.Height, (int)cy);
                    Block((cx - 30, cy - 12, cx + 30, cy + 6));
                    break;
                case "tent":
                    double ex = W * 0.71, ey = top + 12;
                    var tent = PixImage(Tent());
                    Put(tent, ex - tent.Width / 2, ey - tent.Height, (int)ey);
                    Block((ex - tent.Width / 2, ey - 14, ex + tent.Width / 2, ey + 6));
                    break;
                case "swing":
                    if (zooWorld.Shade is { } sh)
                    {
                        var swing = SwingVisual();
                        Put(swing, sh.X - 74, sh.Y - 92, (int)sh.Y + 2);
                    }
                    break;
            }
    }

    /// <summary>Çiçekler: yerleri sabit, engellerden uzak. Yeni alınınca sıçrayarak açarlar.</summary>
    void SetFlowers(int count)
    {
        foreach (var f in flowerImgs) sceneAnimals!.Children.Remove(f.Img);
        flowerImgs.Clear();
        var spots = FlowerSpots();
        bool animate = flowersBloom;
        flowersBloom = false;
        for (int i = flowerImgs.Count; i < count && i < spots.Count; i++)
        {
            var (x, y) = spots[i];
            var img = PixImage(Flower(i));
            Canvas.SetLeft(img, Math.Round(x - img.Width / 2)); Canvas.SetTop(img, Math.Round(y - img.Height));
            Panel.SetZIndex(img, (int)y - 3);
            sceneAnimals!.Children.Add(img);
            flowerImgs.Add((img, i));
            if (animate)
            {
                var sc = new ScaleTransform(0, 0, img.Width / 2, img.Height);
                img.RenderTransform = sc;
                var pop = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.6)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4 } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }
        flowersShown = count;
    }

    /// <summary>Sabit çiçek yerleri: engellerden, düğme alanından ve birbirinden uzak.</summary>
    List<(double X, double Y)> FlowerSpots()
    {
        var rnd = new Random(2026);
        var list = new List<(double, double)>();
        double W = zooWorld.Width, top = zooWorld.Top + 10, bottom = zooWorld.Bottom;
        var blocks = zooWorld.Blocks.ToList();
        if (zooWorld.Avoid is { } a) blocks.Add(a);
        for (int tries = 0; tries < 900 && list.Count < MaxFlowers; tries++)
        {
            double x = 20 + rnd.NextDouble() * (W - 40), y = top + rnd.NextDouble() * (bottom - top);
            if (blocks.Any(b => x > b.L - 20 && x < b.R + 20 && y > b.T - 10 && y < b.B + 30)) continue;
            if (zooWorld.PathY is { } py && Math.Abs(py(x) - 4 - y) < 16) continue;            // patikanın üstüne değil
            if (list.Any(p => Math.Abs(p.Item1 - x) < 46 && Math.Abs(p.Item2 - y) < 26)) continue;
            list.Add((x, y));
        }
        return list;
    }

    /// <summary>Oturum sonunda ödül kartına yazılacak haber (seri ağacı büyüdü); yoksa null.</summary>
    string? GrowthNote(Session done)
    {
        var per = PerDay();
        int streak = Streak(per);
        // Bu oturum bugünü ilk kez "çalışılmış gün" yaptıysa seri bir arttı.
        int todayBefore = per.GetValueOrDefault(Today) - done.Seconds;
        if (todayBefore < StatsMath.MinDaySeconds && streak > 0 && StageOf(streak) > StageOf(streak - 1))
            return $"Seri ağacın büyüdü: {StageNames[StageOf(streak)]}!";
        return null;
    }

    // ---------- süsler: Dostlar kartı ve market ----------

    static BitmapSource DecorIcon(string id) => id switch
    { "fence" => Fence(), "flowers" => Flower(0), "sign" => Sign(), "campfire" => Campfire()[0], "tent" => Tent(), _ => SwingIcon() };

    Image DecorImage(string id, double size, double opacity)
    {
        var img = new Image { Source = DecorIcon(id), Width = size, Height = size, Stretch = Stretch.Uniform, Opacity = opacity };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    /// <summary>Dostlar sayfası: bahçedeki süsler; alınmamışlar soluk ve fiyatlı, tıklayınca markete gider.</summary>
    FrameworkElement DecorCard()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "Bahçe süsleri", FontSize = 16, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text") });
        var (days, bare) = TreeDays();
        body.Children.Add(new TextBlock
        {
            Text = $"Süsleri marketten alabilirsin. Seri ağacın: {StageNames[StageOf(days)]}{(bare ? " (yaprakları döküldü)" : "")}.",
            Style = (Style)FindResource("PageSub"), FontSize = 13
        });
        var row = new WrapPanel { Margin = new Thickness(-6, 12, 0, 0) };
        foreach (var x in ZooCatalog.Decors)
        {
            bool have = Zoo.Owns(data, x.Key);
            var stand = new Grid { Width = 74, Height = 74 };
            stand.Children.Add(new Ellipse { Width = 66, Height = 66, Fill = Res(have ? "GoodSoft" : "Panel2") });
            stand.Children.Add(DecorImage(x.Id, 50, have ? 1 : 0.4));
            var tile = new StackPanel { Margin = new Thickness(6, 0, 6, 4), Width = 86, Background = Brushes.Transparent };
            tile.Children.Add(stand);
            tile.Children.Add(new TextBlock { Text = x.Name, FontSize = 12, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res(have ? "Text" : "Muted"), Margin = new Thickness(0, 4, 0, 0) });
            if (have)
                tile.Children.Add(new TextBlock { Text = "bahçende", FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res("Good") });
            else
            {
                tile.Children.Add(PriceRow(x.Price, 13, 12));
                tile.Cursor = System.Windows.Input.Cursors.Hand;
                tile.MouseLeftButtonUp += (_, _) => GoZoo(ZooScreen.Shop);
            }
            row.Children.Add(tile);
        }
        body.Children.Add(row);
        return new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 18, 0, 0), Child = body };
    }

    StackPanel PriceRow(int price, double coin, double font)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        p.Children.Add(CoinGlyph(coin));
        p.Children.Add(new TextBlock { Text = price.ToString(), FontSize = font, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text"), Margin = new Thickness(5, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    /// <summary>Market: süs kartı.</summary>
    FrameworkElement DecorShopCard(GardenDecor x, int balance)
    {
        bool enough = balance >= x.Price;
        var body = new StackPanel();
        var stage = new Grid { Width = 92, Height = 92, HorizontalAlignment = HorizontalAlignment.Center };
        var disc = new Ellipse();
        if (enough) disc.SetResourceReference(Shape.FillProperty, "AccentSoft"); else disc.Fill = Res("Panel2");
        stage.Children.Add(disc);
        stage.Children.Add(DecorImage(x.Id, 60, enough ? 1 : 0.45));
        body.Children.Add(stage);
        body.Children.Add(new TextBlock { Text = x.Name, FontSize = 16, FontWeight = FontWeights.Black, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 2) });
        body.Children.Add(PriceRow(x.Price, 16, 14));
        var buy = new Button
        {
            Content = enough ? "Satın al" : "Biraz daha odaklan", Style = (Style)FindResource(enough ? "Primary" : "Btn"), IsEnabled = enough,
            FontSize = 13.5, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch
        };
        buy.Click += (_, _) =>
        {
            if (Zoo.BuyDecor(data, x) != BuyResult.Ok) return;
            if (x.Id == "flowers") flowersBloom = true;
            Save();
            NavTimer.IsChecked = true;                 // süs bahçeye kondu: görsün
        };
        body.Children.Add(buy);
        return new Border { Style = (Style)FindResource("Card"), Width = 180, Padding = new Thickness(14, 16, 14, 14), Margin = new Thickness(0, 0, 14, 16), Child = body };
    }

    void OpenMarket()
    {
        NavZoo.IsChecked = true;
        GoZoo(ZooScreen.Shop);
    }

    // ---------- sprite'lar ----------



    static BitmapSource Flower(int i) => B(PixelArt.Flower(i));


    static BitmapSource StreakTree(int stage, bool bare) => B(PixelArt.StreakTree(stage, bare));


    static BitmapSource Fence() => B(PixelArt.Fence());

    static BitmapSource Sign() => B(PixelArt.Sign());

    static BitmapSource[] Campfire() => B(PixelArt.Campfire());

    static BitmapSource Tent() => B(PixelArt.Tent());

    static BitmapSource SwingIcon() => B(PixelArt.SwingIcon());

    /// <summary>Akasyanın dalından sarkan, yavaşça sallanan salıncak.</summary>
    FrameworkElement SwingVisual()
    {
        var c = new Canvas { Width = 40, Height = 80, IsHitTestVisible = false };
        var rope = new SolidColorBrush(Color.FromRgb(0xD9, 0xC9, 0xA0));
        c.Children.Add(new Line { X1 = 8, Y1 = 0, X2 = 8, Y2 = 66, Stroke = rope, StrokeThickness = 2 });
        c.Children.Add(new Line { X1 = 32, Y1 = 0, X2 = 32, Y2 = 66, Stroke = rope, StrokeThickness = 2 });
        var seat = new Rectangle { Width = 36, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(0xC9, 0x8A, 0x4E)) };
        Canvas.SetLeft(seat, 2); Canvas.SetTop(seat, 64);
        c.Children.Add(seat);
        var rot = new RotateTransform(0, 20, 0);
        c.RenderTransform = rot;
        rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-7, 7, TimeSpan.FromSeconds(1.8))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        return c;
    }
}
