using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FlowState;

// Bahçe sekmesi üç ekrandan oluşur:
//   Bahçe  → hayvanların serbestçe dolaştığı sahne + alınmamışların siluetleri; "Market" düğmesi
//   Market → alınmamış hayvanlar, her birinin sabit fiyatı
//   Alındı → kutlama; "Bahçeye götür" ile bahçeye dönülür, yeni hayvan soldan yürüyerek girer
// Hareket mantığı ZooWander.cs içinde (arayüzden bağımsız); burada yalnızca çizilir.
// Görünüm "Sevimli safari" tasarım dilinde (docs/TASARIM.md): beyaz kartlar, şeker düğmeler, pastel sahne.
public partial class MainWindow
{
    const string ActiveMapId = "savanna"; // tek harita; harita seçimi ikinci harita gelince

    enum ZooScreen { Garden, Shop, Bought }
    ZooScreen zooScreen = ZooScreen.Garden;
    string? zooBoughtId;   // kutlama ekranındaki hayvan
    string? zooArriving;   // bahçeye ilk kez girecek hayvan (animasyon bir kez oynar)

    string zooKey = "";    // son çizilen durumun anahtarı (her saniye yeniden kurmamak için)
    TextBlock? zooBalance;
    ScaleTransform? zooBalancePop;   // bakiye değişince jeton hapı hafifçe zıplar
    Button? zooMarketBtn;
    Border? zooMarketBadge;          // Market düğmesindeki "alınabilir" sayacı
    TextBlock? zooMarketBadgeText;

    // Sahne ölçüleri (ekran pikseli). Gökyüzü ve tepeler 3× ölçekli piksel resimdir, sprite'larla aynı.
    const int PixelScale = 3;
    const int BackdropW = 320, BackdropH = 48;   // arka plan resminin kendi pikseli (960×144 görünür)
    const double SceneHeight = 440;              // sahne yüksekliği (çerçeve dahil)
    const double SceneFrame = 6;                 // beyaz "fotoğraf" çerçevesi
    const double GroundTop = 44;                 // hayvan tuvalinin üstü; ayaklar en az bunun + 102 px altında, çimende

    /// <summary>Sekmeden çıkınca bir dahaki girişte bahçeden başlansın.</summary>
    void ResetZooScreen()
    {
        if (zooScreen == ZooScreen.Bought && zooBoughtId != null) zooArriving = zooBoughtId; // kutlamayı atladıysa da yürüsün
        zooScreen = ZooScreen.Garden;
    }

    void UpdateZoo()
    {
        int balance = Zoo.Balance(data);
        int affordable = Zoo.AffordableCount(data, ActiveMapId, balance);
        bool canBuy = affordable > 0;

        if (zooBalance != null && zooBalance.Text != balance.ToString())
        {
            zooBalance.Text = balance.ToString();
            if (zooBalancePop != null) Pop(zooBalancePop, 1.18);
        }
        if (zooMarketBtn != null)
        {
            var style = (Style)FindResource(canBuy ? "Primary" : "Btn");
            if (!ReferenceEquals(zooMarketBtn.Style, style)) zooMarketBtn.Style = style;
        }
        if (zooMarketBadge != null && zooMarketBadgeText != null)
        {
            zooMarketBadge.Visibility = canBuy ? Visibility.Visible : Visibility.Collapsed;
            zooMarketBadgeText.Text = affordable.ToString();
        }

        // Düzen yalnızca ekran, sahip olunanlar ya da (markette) alınabilir hayvan sayısı değişince yeniden kurulur.
        var key = $"{zooScreen}|{data.OwnedAnimals.Count}|{Specials().Count}|{(zooScreen == ZooScreen.Shop ? affordable : 0)}";
        if (key == zooKey) return;
        zooKey = key;

        ZooRoot.Children.Clear();
        // Bahçe sahnesi (zooCanvas, zooNodes) artık Çalış'ta; Dostlar sayfası yeniden kurulurken ona dokunulmaz.
        zooMarketBtn = null;
        zooMarketBadge = null;
        zooMarketBadgeText = null;
        switch (zooScreen)
        {
            case ZooScreen.Garden: BuildGarden(balance, affordable); break;
            case ZooScreen.Shop: BuildShop(balance); break;
            case ZooScreen.Bought: BuildBought(); break;
        }
    }

    void GoZoo(ZooScreen s)
    {
        zooScreen = s;
        zooKey = "";
        UpdateZoo();
    }

    // ---------- ekranlar ----------

    void BuildGarden(int balance, int affordable)
    {
        var map = ZooCatalog.Map(ActiveMapId);
        bool canBuy = affordable > 0;

        // Market düğmesi: alınabilecek hayvan varsa tema renginde ve köşesinde küçük bir sayaç.
        zooMarketBtn = new Button
        {
            Content = IconLabel(BagIcon, "Market"), Style = (Style)FindResource(canBuy ? "Primary" : "Btn"),
            FontSize = 15, VerticalAlignment = VerticalAlignment.Center
        };
        zooMarketBtn.Click += (_, _) => GoZoo(ZooScreen.Shop);
        zooMarketBadgeText = new TextBlock
        {
            Text = affordable.ToString(), FontSize = 11, FontWeight = FontWeights.Black, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        zooMarketBadge = new Border
        {
            Background = Res("Danger"), CornerRadius = new CornerRadius(10), MinWidth = 20, Height = 20,
            Padding = new Thickness(5, 0, 5, 1), BorderBrush = Res("Surface"), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -8, -8, 0), IsHitTestVisible = false, Child = zooMarketBadgeText,
            Visibility = canBuy ? Visibility.Visible : Visibility.Collapsed
        };
        var marketHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        marketHost.Children.Add(zooMarketBtn);
        marketHost.Children.Add(zooMarketBadge);

        ZooRoot.Children.Add(Header("Dostlar", "Bahçen Çalış sekmesinde; odaklandıkça jeton biriktir, yeni dostlar kat.", balance, marketHost));

        // Bahçedekiler: sevinç pozunda, pastel kürsülerde. Tıklayınca bahçeye (Çalış) gider.
        var owned = ZooCatalog.InMap(ActiveMapId).Where(a => ZooDemo || Zoo.Owns(data, a.Id)).ToList();
        var ownedBody = new StackPanel();
        var goGarden = new Button { Content = IconLabel(IconRes("IconTimer"), "Bahçeye git", 13, 14), Style = (Style)FindResource("Btn"), Padding = new Thickness(12, 6, 12, 6) };
        goGarden.Click += (_, _) => NavTimer.IsChecked = true;
        DockPanel.SetDock(goGarden, Dock.Right);
        var ownedHead = new DockPanel();
        ownedHead.Children.Add(goGarden);
        ownedHead.Children.Add(new TextBlock { Text = $"{map.Name} bahçende", FontSize = 16, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center });
        ownedBody.Children.Add(ownedHead);
        if (owned.Count == 0)
            ownedBody.Children.Add(new TextBlock { Text = "Henüz kimse yok. Marketten ilk dostunu al, bahçende dolaşmaya başlasın.", Style = (Style)FindResource("PageSub"), FontSize = 13 });
        else
        {
            var row = new WrapPanel { Margin = new Thickness(-6, 12, 0, 0) };
            foreach (var a in owned)
            {
                var stand = new Grid { Width = 74, Height = 74 };
                stand.Children.Add(new Ellipse { Width = 66, Height = 66, Fill = Res("AccentSoft") });
                stand.Children.Add(SpriteImage(a.Id, 60, silhouette: false, ZooPose.Happy));
                var tile = new StackPanel { Margin = new Thickness(6, 0, 6, 4), Width = 80 };
                tile.Children.Add(stand);
                tile.Children.Add(new TextBlock { Text = a.Name, FontSize = 12, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res("Text"), Margin = new Thickness(0, 4, 0, 0) });
                row.Children.Add(tile);
            }
            ownedBody.Children.Add(row);
        }
        ZooRoot.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 20, 0, 0), Child = ownedBody });
        if (owned.Count > 0) ZooRoot.Children.Add(CollectionCard());

        // Henüz alınmamışlar: sahnenin altında, pastel kürsülerde siluet olarak (merak uyandırsın).
        var missing = ZooCatalog.InMap(ActiveMapId).Where(a => !Zoo.Owns(data, a.Id)).ToList();
        if (missing.Count > 0)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Bahçeye katılmayı bekleyenler", FontSize = 16, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text") });
            body.Children.Add(new TextBlock
            {
                Text = missing.Count == 1 ? "Son bir dost kaldı. Kim olduğunu tahmin edebilir misin?" : $"{missing.Count} gizemli dost seni bekliyor.",
                Style = (Style)FindResource("PageSub"), FontSize = 13
            });
            var row = new WrapPanel { Margin = new Thickness(-6, 12, 0, 0) };
            foreach (var a in missing) row.Children.Add(SilhouetteTile(a, 64));
            body.Children.Add(row);
            ZooRoot.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 18, 0, 0), Child = body });
        }
        else
        {
            var done = IconLabel(IconRes("IconSparkle"), $"{map.Name} tamamlandı! Bütün dostlar bahçende.", 15, 18, filled: true);
            TextElement_SetForeground(done, Res("Good"));
            ZooRoot.Children.Add(new Border
            {
                Background = Res("GoodSoft"), CornerRadius = new CornerRadius(20), Padding = new Thickness(20, 12, 20, 12),
                Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center, Child = done
            });
        }
    }

    void BuildShop(int balance)
    {
        var back = new Button
        {
            Content = IconLabel(BackIcon, "Bahçe"), Style = (Style)FindResource("Btn"), FontSize = 15,
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        back.Click += (_, _) => GoZoo(ZooScreen.Garden);
        ZooRoot.Children.Add(Header("Market", "Biriktirdiğin jetonlarla yeni bir dost edin.", balance, back));

        var left = ZooCatalog.InMap(ActiveMapId).Where(a => !Zoo.Owns(data, a.Id)).OrderBy(a => a.Price).ToList();
        if (left.Count == 0)
        {
            ZooRoot.Children.Add(Friendly(IconRes("IconSparkle"), "Hepsi bahçende!", "Bu haritadaki bütün hayvanları topladın. Harikasın.", filledIcon: true));
            return;
        }

        var shop = new WrapPanel { Margin = new Thickness(0, 22, -14, 0) };
        int i = 0;
        foreach (var a in left) shop.Children.Add(ShopCard(a, balance, i++));
        ZooRoot.Children.Add(shop);
    }

    /// <summary>Market kartı: pastel kürsüde hayvan, adı, jetonlu fiyatı ve "Satın al" (yetmiyorsa kilitli).</summary>
    FrameworkElement ShopCard(Animal a, int balance, int index)
    {
        int price = a.Price;
        bool enough = balance >= price;

        var body = new StackPanel();

        // Kürsü: yuvarlak pastel zemin, altında hafif gölge; alınabilirse tema renginin yumuşak tonu.
        var stage = new Grid { Width = 132, Height = 132, HorizontalAlignment = HorizontalAlignment.Center };
        var disc = new Ellipse();
        if (enough) disc.SetResourceReference(Shape.FillProperty, "AccentSoft");
        else disc.Fill = Res("Panel2");
        stage.Children.Add(disc);
        stage.Children.Add(new Ellipse
        {
            Width = 70, Height = 12, Fill = new SolidColorBrush(Color.FromArgb(0x22, 0x50, 0x2A, 0x10)),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 24)
        });
        var sprite = SpriteImage(a.Id, 96, silhouette: !enough);
        sprite.VerticalAlignment = VerticalAlignment.Center;
        sprite.Margin = new Thickness(0, 0, 0, 6);
        if (!enough) sprite.Opacity = 0.28;
        stage.Children.Add(sprite);
        if (!enough)
        {
            // Kilit rozeti: beyaz yuvarlak, içinde kilit
            var lockIcon = IconPath(LockIcon, 16);
            lockIcon.Stroke = Res("Muted");
            stage.Children.Add(new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Background = Res("Surface"),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 6), Effect = (Effect)FindResource("SoftShadow"), Child = lockIcon
            });
        }
        body.Children.Add(stage);

        body.Children.Add(new TextBlock
        {
            Text = a.Name, FontSize = 17, FontWeight = FontWeights.Black, Foreground = Res("Text"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 2)
        });

        var priceRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        priceRow.Children.Add(CoinGlyph(17));
        priceRow.Children.Add(new TextBlock
        {
            Text = price.ToString(), FontSize = 15, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text"),
            Margin = new Thickness(6, 0, 0, 1), VerticalAlignment = VerticalAlignment.Center
        });
        body.Children.Add(priceRow);

        if (!enough)
        {
            // Biriktirme çubuğu: bakiye / fiyat. Ne kadar çalışılacağı söylenmez; jeton kuralı gizli kalır.
            double frac = Math.Clamp(balance / (double)price, 0, 1);
            var track = new Grid { Height = 8, Margin = new Thickness(6, 10, 6, 0) };
            track.Children.Add(new Border { Background = Res("Track"), CornerRadius = new CornerRadius(4) });
            var fill = new Border { Background = Res("Coin"), CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, MinWidth = frac > 0 ? 8 : 0 };
            track.SizeChanged += (_, e) => fill.Width = Math.Max(fill.MinWidth, e.NewSize.Width * frac);
            track.Children.Add(fill);
            body.Children.Add(track);
        }

        var buy = new Button
        {
            Content = enough ? "Satın al" : "Biraz daha odaklan",
            Style = (Style)FindResource(enough ? "Primary" : "Btn"),
            FontSize = 14, Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, enough ? 14 : 10, 0, 0),
            IsEnabled = enough, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var id = a.Id;
        buy.Click += (_, _) => BuyAnimal(id);
        body.Children.Add(buy);

        var card = new Border
        {
            Style = (Style)FindResource("Card"), Width = 196, Padding = new Thickness(16, 18, 16, 16),
            Margin = new Thickness(0, 0, 14, 16), Child = body
        };

        // Beliriş: sırayla, aşağıdan hafifçe yukarı süzülerek. Alınabilir kart üstüne gelince biraz kalkar.
        var lift = new TranslateTransform(0, 14);
        card.RenderTransform = lift;
        card.Opacity = 0;
        card.Loaded += (_, _) =>
        {
            var begin = TimeSpan.FromMilliseconds(70 * index);
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = begin });
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = ease });
        };
        if (enough)
        {
            card.MouseEnter += (_, _) => lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-4, TimeSpan.FromMilliseconds(160)));
            card.MouseLeave += (_, _) => lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
        }
        return card;
    }

    void BuildBought()
    {
        var a = zooBoughtId is string id ? ZooCatalog.Find(id) : null;
        if (a == null) { GoZoo(ZooScreen.Garden); return; }

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Sahne: sıcak bir ışıltı, etrafında parıldayan yıldızcıklar, ortada zıplayarak beliren hayvan.
        var stage = new Grid { Width = 320, Height = 270, HorizontalAlignment = HorizontalAlignment.Center };
        var glow = new Ellipse { Width = 300, Height = 260, IsHitTestVisible = false };
        var glowBrush = new RadialGradientBrush();
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, 0xFF, 0xE0, 0x8A), 0));
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0xFF, 0xD7, 0xA8), 0.5));
        glowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xD7, 0xA8), 1));
        glow.Fill = glowBrush;
        var glowScale = new ScaleTransform(1, 1);
        glow.RenderTransformOrigin = new Point(0.5, 0.5);
        glow.RenderTransform = glowScale;
        stage.Children.Add(glow);
        Breathe(glow, glowScale, 1.0, 1.08, 2.2);

        // Yıldızcıklar: (x, y, boyut, renk anahtarı, gecikme)
        var sparkles = new (double X, double Y, double Size, string Color, double Delay)[]
        {
            (-118, -88, 26, "Coin", 0.0), (112, -70, 20, "Accent", 0.5), (-130, 30, 16, "Accent", 1.1),
            (124, 46, 24, "Coin", 0.8), (-60, -122, 14, "Danger", 1.5), (70, -118, 16, "Coin", 0.3), (0, 112, 12, "Accent", 1.9),
        };
        foreach (var s in sparkles)
        {
            var p = new Path
            {
                Data = (Geometry)FindResource("IconSparkle"), Width = s.Size, Height = s.Size, Stretch = Stretch.Uniform,
                IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5)
            };
            p.SetResourceReference(Shape.FillProperty, s.Color);
            var sc = new ScaleTransform(1, 1);
            p.RenderTransform = new TransformGroup { Children = { sc, new TranslateTransform(s.X, s.Y) } };
            stage.Children.Add(p);
            Twinkle(p, sc, s.Delay);
        }

        // Pist gölgesi + hayvan (varsa sevinç pozunda)
        stage.Children.Add(new Ellipse
        {
            Width = 120, Height = 18, Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xB0, 0x7A, 0x55)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 176, 0, 0)
        });
        var sprite = SpriteImage(a.Id, 192, silhouette: false, pose: ZooPose.Happy);
        sprite.RenderTransformOrigin = new Point(0.5, 0.85);
        sprite.VerticalAlignment = VerticalAlignment.Center;
        var scale = new ScaleTransform(0.2, 0.2);
        sprite.RenderTransform = scale;
        sprite.Loaded += (_, _) =>
        {
            var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        };
        stage.Children.Add(sprite);
        panel.Children.Add(stage);

        var badge = IconLabel(IconRes("IconHeart"), "Yeni dost", 13, 14, filled: true);
        badge.SetResourceReference(TextElement_ForegroundProperty, "AccentDeep");
        var badgeBox = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 6), HorizontalAlignment = HorizontalAlignment.Center, Child = badge, Margin = new Thickness(0, 4, 0, 0) };
        badgeBox.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        panel.Children.Add(badgeBox);

        panel.Children.Add(new TextBlock { Text = $"{a.Name} yolda!", Style = (Style)FindResource("PageTitle"), FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "Yeni yuvasını görmek için sabırsızlanıyor.", Style = (Style)FindResource("PageSub"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });

        var go = new Button
        {
            Content = IconLabel(ArrowIcon, "Bahçeye götür", 17, 18, iconAfter: true), Style = (Style)FindResource("Primary"),
            FontSize = 17, Padding = new Thickness(30, 13, 30, 13),
            Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
        };
        go.Click += (_, _) => { zooArriving = a.Id; zooBoughtId = null; GoZoo(ZooScreen.Garden); NavTimer.IsChecked = true; };
        panel.Children.Add(go);

        // Sıcak, şeftali-krem geçişli büyük kart
        var warm = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        warm.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xF6, 0xE4), 0));
        warm.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.75));
        var card = new Border
        {
            Style = (Style)FindResource("Card"), Background = warm, Padding = new Thickness(30, 20, 30, 34),
            Margin = new Thickness(0, 24, 0, 0), Child = panel
        };
        ZooRoot.Children.Add(card);
    }

    // ---------- dolaşan hayvanlar ----------

    /// <summary>Önizleme: FLOWSTATE_ZOO_DEMO=1 ile açılırsa bahçede tüm hayvanlar dolaşır (veri değişmez).</summary>
    static readonly bool ZooDemo = Environment.GetEnvironmentVariable("FLOWSTATE_ZOO_DEMO") == "1";

    readonly ZooWorld zooWorld = new();
    readonly List<ZooNode> zooNodes = new();
    Canvas? zooCanvas;
    DispatcherTimer? zooTimer;
    readonly Stopwatch zooClock = new();

    /// <summary>Bir hayvanın sahnedeki görseli: gölge + sprite + üstünde isim etiketi (beyaz hap, altında küçük kuyruk).</summary>
    sealed record ZooNode(Wanderer W, Canvas Root, Image Img, ScaleTransform Flip, TranslateTransform Hop,
                          FrameworkElement Bubble, Border BubbleFace, Path BubbleTail, TextBlock BubbleText, Path Heart,
                          BitmapSource[] Frames, BitmapSource?[] Poses, string Name, double Scale);

    /// <param name="kind">null: hayvanın kendisi; Golden: altına boyalı ve parıltılı; Baby: küçük, annesini izler.</param>
    void AddNode(Canvas canvas, Animal a, Wanderer w, SpecialKind? kind)
    {
        const double S = ZooWorld.Sprite;
        bool golden = kind == SpecialKind.Golden;
        double scale = kind == SpecialKind.Baby ? BabyScale : 1;
        var frames = golden ? GoldenFrames(a.Id) : Frames(a.Id);
        var poses = golden ? GoldenPoses(a.Id) : Poses(a.Id);
        w.Has = poses.Select(p => p != null).ToArray();

        var shadow = new Ellipse { Width = S * 0.62, Height = S * 0.16, Fill = new SolidColorBrush(Color.FromArgb(0x48, 0x2E, 0x5A, 0x1C)) };
        Canvas.SetLeft(shadow, S * 0.19); Canvas.SetTop(shadow, S * 0.86);

        var flip = new ScaleTransform(1, 1, S / 2, S / 2);
        var hop = new TranslateTransform();
        var img = new Image { Width = S, Height = S, Source = poses[0], Cursor = Cursors.Hand, RenderTransform = new TransformGroup { Children = { flip, hop } } };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        img.MouseLeftButtonDown += (_, e) => { zooWorld.Poke(w); e.Handled = true; };

        // İsim etiketi: beyaz hap, kakao yazı, altında küçük üçgen kuyruk ve sahte (ucuz) gölge.
        var text = new TextBlock { FontSize = 12, FontWeight = FontWeights.ExtraBold, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center };
        var heart = new Path
        {
            Data = (Geometry)FindResource("IconHeart"), Width = 13, Height = 13, Stretch = Stretch.Uniform,
            Fill = Res("Danger"), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(heart);
        content.Children.Add(text);
        var face = new Border { Background = Res("Surface"), CornerRadius = new CornerRadius(11), Padding = new Thickness(9, 2, 9, 3), Child = content };
        var tail = new Path
        {
            Data = Geometry.Parse("M0,0 L10,0 L5,6 Z"), Fill = Res("Surface"), Width = 10, Height = 6,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -1, 0, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(face);
        stack.Children.Add(tail);
        var bubble = new Grid { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        bubble.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x40, 0x2A, 0x10)), CornerRadius = new CornerRadius(11),
            Margin = new Thickness(0, 2, 0, 4), RenderTransform = new TranslateTransform(0, 2)
        });
        bubble.Children.Add(stack);

        // Gövde (gölge + sprite) yavruda ayakların altından küçültülür; etiket normal boyda kalır.
        var body = new Canvas { Width = S, Height = S, RenderTransform = new ScaleTransform(scale, scale, S / 2, S) };
        body.Children.Add(shadow);
        body.Children.Add(img);
        if (golden)
        {
            // Altın dostun çevresinde parıldayan iki yıldızcık.
            foreach (var (x, y, d, delay) in new[] { (S * 0.16, S * 0.30, 12.0, 0.0), (S * 0.74, S * 0.18, 9.0, 0.7) })
            {
                var sc = new ScaleTransform(1, 1);
                var spark = new Path { Data = (Geometry)FindResource("IconSparkle"), Fill = Res("Coin"), Width = d, Height = d, Stretch = Stretch.Uniform,
                    IsHitTestVisible = false, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = sc };
                Canvas.SetLeft(spark, x); Canvas.SetTop(spark, y);
                body.Children.Add(spark);
                Twinkle(spark, sc, delay);
            }
        }
        var root = new Canvas { Width = S, Height = S };
        root.Children.Add(body);
        root.Children.Add(bubble);
        canvas.Children.Add(root);
        string name = kind is { } k ? Special.Title(a.Name, k) : a.Name;
        zooNodes.Add(new ZooNode(w, root, img, flip, hop, bubble, face, tail, text, heart, frames, poses, name, scale));
    }

    void EnsureZooTimer()
    {
        if (zooTimer != null) return;
        zooTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        zooTimer.Tick += (_, _) => TickZoo();
        zooTimer.Start();
    }

    void TickZoo()
    {
        // Bahçe görünmüyorken dünya durur (hayvanlar kaldıkları yerden devam eder).
        if (zooCanvas == null || !TimerView.IsVisible || widgetMode) { zooClock.Reset(); return; }
        double dt = zooClock.IsRunning ? zooClock.Elapsed.TotalSeconds : 0;
        zooClock.Restart();
        zooWorld.Update(dt);
        TickKeeper();
        TickAmbient(dt);

        const double S = ZooWorld.Sprite;
        foreach (var n in zooNodes)
        {
            var w = n.W;
            if (double.IsNaN(w.X)) { n.Root.Visibility = Visibility.Hidden; continue; }
            n.Root.Visibility = Visibility.Visible;
            Canvas.SetLeft(n.Root, Math.Round(w.X - S / 2));
            Canvas.SetTop(n.Root, Math.Round(w.Y - S));
            Panel.SetZIndex(n.Root, (int)w.Y);                       // aşağıdaki öndedir
            n.Flip.ScaleX = w.FacingRight ? 1 : -1;                  // kareler sağa bakar
            n.Hop.Y = Math.Round(w.HopOffset);

            // Uzun (gerçek) yürüyüş döngüleri daha sık kare değiştirir; adım hızı yürüme hızına uysun.
            double fps = n.Frames.Length >= 6 ? 6.5 : 5;
            var frame = w.State == WanderState.Walk
                ? n.Frames[(int)(w.WalkTime * fps) % n.Frames.Length]
                : n.Poses[(int)w.Shown] ?? n.Poses[0]!;                 // dururken: ayakta, oturma, uyuma, yeme, sevinç
            if (!ReferenceEquals(n.Img.Source, frame)) n.Img.Source = frame;

            // Etiket: yeni gelen "Yeni!" (tema renginde), selamlaşırken kalp, tıklanınca adı, uyurken "z z".
            const string HeartLabel = "♥";
            string? label = w.Arriving ? "Yeni!" : w.State == WanderState.Greet ? HeartLabel : w.NameTimer > 0 ? n.Name
                : w.State == WanderState.Idle && w.Shown == ZooPose.Sleep ? "z z" : null;
            if (label == null) { n.Bubble.Visibility = Visibility.Collapsed; continue; }
            if ((string?)n.Bubble.Tag != label)
            {
                n.Bubble.Tag = label;
                bool isHeart = label == HeartLabel, isNew = label == "Yeni!";
                n.Heart.Visibility = isHeart ? Visibility.Visible : Visibility.Collapsed;
                n.BubbleText.Visibility = isHeart ? Visibility.Collapsed : Visibility.Visible;
                n.BubbleText.Text = isHeart ? "" : label;
                n.BubbleText.Foreground = Res(label == "z z" ? "Muted" : "Text");
                if (isNew)
                {
                    n.BubbleFace.SetResourceReference(Border.BackgroundProperty, "Accent");
                    n.BubbleTail.SetResourceReference(Shape.FillProperty, "Accent");
                    n.BubbleText.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
                }
                else
                {
                    n.BubbleFace.Background = Res("Surface");
                    n.BubbleTail.Fill = Res("Surface");
                }
                n.Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
            n.Bubble.Visibility = Visibility.Visible;
            Canvas.SetLeft(n.Bubble, Math.Round((S - n.Bubble.DesiredSize.Width) / 2));
            Canvas.SetTop(n.Bubble, -4 + S * (1 - n.Scale) + n.Hop.Y * n.Scale);
        }
    }

    // ---------- parçalar ----------

    /// <summary>Sayfa başlığı: solda başlık + sıcak bir alt satır, sağda jeton hapı ve eylem düğmesi.</summary>
    DockPanel Header(string title, string sub, int balance, FrameworkElement action)
    {
        var head = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(action, Dock.Right);
        head.Children.Add(action);

        var pill = CoinPill(balance);
        DockPanel.SetDock(pill, Dock.Right);
        head.Children.Add(pill);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        titles.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("PageTitle"), TextTrimming = TextTrimming.CharacterEllipsis });
        titles.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("PageSub") });
        head.Children.Add(titles);
        return head;
    }

    /// <summary>Tombul jeton hapı: yuvarlak jeton + kalın bakiye. Bakiye değişince hafifçe zıplar.</summary>
    Border CoinPill(int balance)
    {
        zooBalance = new TextBlock
        {
            Text = balance.ToString(), FontSize = 22, FontWeight = FontWeights.Black, Foreground = Res("Text"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 1)
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CoinGlyph(28));
        row.Children.Add(zooBalance);
        zooBalancePop = new ScaleTransform(1, 1);
        return new Border
        {
            Background = Res("WarnSoft"), BorderBrush = Res("Surface"), BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(26), Padding = new Thickness(7, 5, 16, 5), VerticalAlignment = VerticalAlignment.Center,
            Effect = (Effect)FindResource("SoftShadow"), Child = row, ToolTip = "Jeton",
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = zooBalancePop
        };
    }

    /// <summary>Elipslerle çizilmiş yuvarlak altın jeton: koyu kenar, parlak yüz, iç halka ve küçük parıltı.</summary>
    FrameworkElement CoinGlyph(double size)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        g.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xDE, 0x96, 0x16)) });                       // kenar
        g.Children.Add(new Ellipse { Fill = Res("Coin"), Margin = new Thickness(size * 0.07, size * 0.04, size * 0.07, size * 0.12) }); // yüz
        g.Children.Add(new Ellipse
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xDF, 0x7E)), StrokeThickness = Math.Max(1, size * 0.07),
            Margin = new Thickness(size * 0.24, size * 0.2, size * 0.24, size * 0.28)
        });                                                                                                                  // iç halka
        g.Children.Add(new Ellipse
        {
            Fill = Brushes.White, Opacity = 0.75, Width = size * 0.2, Height = size * 0.14,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(size * 0.22, size * 0.16, 0, 0), RenderTransform = new RotateTransform(-30)
        });                                                                                                                  // parıltı
        return g;
    }

    /// <summary>Bahçe sahnesi: beyaz çerçeveli kart; içinde piksel gökyüzü + tepeler, süzülen bulutlar, çimen ve hayvan tuvali.</summary>
    FrameworkElement Scene(Canvas animals, FrameworkElement? overlay)
    {
        var host = new Grid { Height = SceneHeight, Margin = new Thickness(0, 20, 0, 0) };
        // Gölgeyi ayrı, içi boş bir karta veriyoruz: hareketli içerik efektin altında kalıp her karede yeniden işlenmesin.
        host.Children.Add(new Border { Background = Res("Surface"), CornerRadius = new CornerRadius(28), Effect = (Effect)FindResource("LiftShadow") });

        var scene = new Grid { Margin = new Thickness(SceneFrame), Background = GrassBrush() };
        const double R = 28 - SceneFrame;
        scene.SizeChanged += (_, e) => scene.Clip = new RectangleGeometry(new Rect(e.NewSize), R, R);

        var sky = new Image
        {
            Source = Backdrop(), Width = BackdropW * PixelScale, Height = BackdropH * PixelScale, Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(sky, BitmapScalingMode.NearestNeighbor);
        scene.Children.Add(sky);
        scene.Children.Add(Clouds());
        scene.Children.Add(animals);
        if (overlay != null) scene.Children.Add(overlay);
        host.Children.Add(scene);
        return host;
    }

    /// <summary>Gökyüzünde yavaşça süzülen piksel bulutlar (yalnızca süs; tıklanmaz).</summary>
    Canvas Clouds()
    {
        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
        // (bulut, üst konum, bir turun süresi sn, turun ne kadarından başlasın)
        var list = new (int Kind, double Top, double Seconds, double Phase)[] { (0, 14, 150, 0.15), (1, 46, 190, 0.55), (2, 26, 230, 0.85) };
        foreach (var c in list)
        {
            var bmp = CloudBitmap(c.Kind);
            var img = new Image { Source = bmp, Width = bmp.PixelWidth * PixelScale, Height = bmp.PixelHeight * PixelScale, Opacity = 0.95 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            Canvas.SetTop(img, c.Top);
            layer.Children.Add(img);
            var drift = new DoubleAnimation(-img.Width, 1000, TimeSpan.FromSeconds(c.Seconds))
            {
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-c.Seconds * c.Phase)
            };
            Forever(img, Canvas.LeftProperty, drift);
        }
        return layer;
    }

    /// <summary>Boş bahçe: sahnenin ortasında sevimli bir davet kartı.</summary>
    FrameworkElement EmptyGarden()
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var paw = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(29), HorizontalAlignment = HorizontalAlignment.Center };
        paw.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        var pawIcon = new Path { Data = (Geometry)FindResource("IconPaw"), Width = 30, Height = 30, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        pawIcon.SetResourceReference(Shape.FillProperty, "Accent");
        paw.Child = pawIcon;
        p.Children.Add(paw);
        p.Children.Add(new TextBlock { Text = "Bahçen henüz boş", FontSize = 19, FontWeight = FontWeights.Black, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 2) });
        p.Children.Add(new TextBlock
        {
            Text = "Marketten ilk dostunu al,\nburada dolaşmaya başlasın.", Style = (Style)FindResource("PageSub"),
            FontSize = 13.5, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        });
        var go = new Button { Content = IconLabel(BagIcon, "Markete git"), Style = (Style)FindResource("Primary"), FontSize = 14, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        go.Click += (_, _) => GoZoo(ZooScreen.Shop);
        p.Children.Add(go);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)), CornerRadius = new CornerRadius(24),
            Padding = new Thickness(30, 22, 30, 20), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0), Effect = (Effect)FindResource("SoftShadow"), Child = p
        };
        var bob = new TranslateTransform();
        card.RenderTransform = bob;
        Forever(card, null, new DoubleAnimation(0, -5, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        }, bob, TranslateTransform.YProperty);
        return card;
    }

    /// <summary>Kart içinde ortalı, ikonlu dostça bir mesaj (boş durumlar için).</summary>
    Border Friendly(Geometry icon, string title, string sub, bool filledIcon = false)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var circle = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), HorizontalAlignment = HorizontalAlignment.Center };
        circle.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        var path = new Path { Data = icon, Width = 30, Height = 30, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (filledIcon) path.SetResourceReference(Shape.FillProperty, "Accent");
        else { path.SetResourceReference(Shape.StrokeProperty, "Accent"); path.StrokeThickness = 2.4; path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round; path.StrokeLineJoin = PenLineJoin.Round; }
        circle.Child = path;
        p.Children.Add(circle);
        p.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Black, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) });
        p.Children.Add(new TextBlock { Text = sub, Style = (Style)FindResource("PageSub"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        return new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(30, 34, 30, 34), Margin = new Thickness(0, 22, 0, 0), Child = p };
    }

    /// <summary>Alınmamış hayvan: pastel kürsüde koyu silueti, adı gizli.</summary>
    StackPanel SilhouetteTile(Animal a, double size)
    {
        var p = new StackPanel { Margin = new Thickness(6, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var stage = new Grid { Width = size + 22, Height = size + 22 };
        stage.Children.Add(new Ellipse { Fill = Res("Panel2") });
        var shape = SpriteImage(a.Id, size, silhouette: true);
        shape.Opacity = 0.32;
        shape.VerticalAlignment = VerticalAlignment.Center;
        stage.Children.Add(shape);
        p.Children.Add(stage);
        var price = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        price.Children.Add(new TextBlock { Text = "???", FontSize = 13, FontWeight = FontWeights.ExtraBold, Foreground = Res("Faint") });
        p.Children.Add(price);
        return p;
    }

    // ---------- küçük yardımcılar (ikon, animasyon) ----------

    // Tasarım sisteminde olmayan, yalnızca bu sayfada kullanılan çizgi ikonlar (24×24).
    static readonly Geometry BagIcon = Frozen("M5,8.5 L19,8.5 L18,20 L6,20 Z M9,8.5 C9,3.5 15,3.5 15,8.5");
    static readonly Geometry BackIcon = Frozen("M11,5 L4,12 L11,19 M4.5,12 L20,12");
    static readonly Geometry ArrowIcon = Frozen("M4,12 L19.5,12 M13,5 L20,12 L13,19");
    static readonly Geometry LockIcon = Frozen("M5.5,11 L18.5,11 L18.5,20.5 L5.5,20.5 Z M8.5,11 L8.5,8 C8.5,3.3 15.5,3.3 15.5,8 L15.5,11");

    static Geometry Frozen(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    Geometry IconRes(string key) => (Geometry)FindResource(key);

    /// <summary>Icon stilinde çizgi ikon (rengi, içinde bulunduğu denetimin yazı rengidir).</summary>
    Path IconPath(Geometry g, double size)
    {
        var p = new Path { Data = g, Style = (Style)FindResource("Icon"), Width = size, Height = size };
        return p;
    }

    // StackPanel'in yazı rengi: içindeki TextBlock'lar TextElement.Foreground'u miras alır.
    static readonly DependencyProperty TextElement_ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty;
    static void TextElement_SetForeground(DependencyObject d, Brush b) => d.SetValue(TextElement_ForegroundProperty, b);

    /// <summary>İkon + yazı. Düğme içinde ikon düğmenin yazı rengini alır; düğme dışında rengi panelin yazı rengidir.</summary>
    StackPanel IconLabel(Geometry icon, string text, double fontSize = 0, double iconSize = 17, bool filled = false, bool iconAfter = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var path = new Path { Data = icon, Width = iconSize, Height = iconSize, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        // Rengi panelin TextElement.Foreground'undan alır (düğme içinde düğmenin yazı rengi miras gelir).
        var colorBinding = new System.Windows.Data.Binding { Path = new PropertyPath(TextElement_ForegroundProperty), Source = row };
        if (filled) path.SetBinding(Shape.FillProperty, colorBinding);
        else
        {
            path.SetBinding(Shape.StrokeProperty, colorBinding);
            path.StrokeThickness = 2.4;
            path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round;
            path.StrokeLineJoin = PenLineJoin.Round;
        }
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.ExtraBold };
        if (fontSize > 0) label.FontSize = fontSize;
        if (iconAfter)
        {
            label.Margin = new Thickness(0, 0, 8, 1);
            row.Children.Add(label);
            row.Children.Add(path);
        }
        else
        {
            label.Margin = new Thickness(7, 0, 0, 1);
            row.Children.Add(path);
            row.Children.Add(label);
        }
        return row;
    }

    /// <summary>Sonsuz animasyon; öğe ağaçtan çıkınca durdurulur (saat açık kalıp boşuna dönmesin).</summary>
    static void Forever(FrameworkElement owner, DependencyProperty? ownerProp, AnimationTimeline anim,
                        IAnimatable? target = null, DependencyProperty? targetProp = null)
    {
        var t = target ?? owner;
        var prop = targetProp ?? ownerProp!;
        owner.Loaded += (_, _) => t.BeginAnimation(prop, anim);
        owner.Unloaded += (_, _) => t.BeginAnimation(prop, null);
    }

    /// <summary>Kutlamadaki ışıltının yavaş nefes alması.</summary>
    static void Breathe(FrameworkElement owner, ScaleTransform sc, double from, double to, double seconds)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Forever(owner, null, a, sc, ScaleTransform.ScaleXProperty);
        Forever(owner, null, a, sc, ScaleTransform.ScaleYProperty);
    }

    /// <summary>Yıldızcık parıldaması: küçülüp büyür, sönüp parlar.</summary>
    static void Twinkle(FrameworkElement p, ScaleTransform sc, double delay)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var size = new DoubleAnimation(0.55, 1.1, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = ease
        };
        var fade = new DoubleAnimation(0.35, 1, TimeSpan.FromSeconds(0.9))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay), EasingFunction = ease
        };
        p.Opacity = 0.35;
        sc.ScaleX = sc.ScaleY = 0.55;
        Forever(p, null, size, sc, ScaleTransform.ScaleXProperty);
        Forever(p, null, size, sc, ScaleTransform.ScaleYProperty);
        Forever(p, OpacityProperty, fade);
    }

    /// <summary>Kısa "boing": büyür, yaylanarak eski boyuna döner.</summary>
    static void Pop(ScaleTransform sc, double peak)
    {
        var a = new DoubleAnimation(peak, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5 } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    // ---------- sprite'lar (Assets/pixellab; tools/build-sprites.ps1 üretir) ----------

    const int SpriteCell = 32;
    static readonly Dictionary<string, BitmapSource[]> frameCache = new();
    static readonly Dictionary<string, BitmapSource> silCache = new();

    static BitmapImage LoadSprite(string name)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.UriSource = new Uri($"pack://application:,,,/Assets/pixellab/{name}.png");
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.EndInit();
        b.Freeze();
        return b;
    }

    /// <summary>Hayvanın yürüyüş kareleri (sağa bakar).</summary>
    static BitmapSource[] Frames(string id)
    {
        if (frameCache.TryGetValue(id, out var f)) return f;
        var strip = LoadSprite($"{id}_walk");
        f = new BitmapSource[strip.PixelWidth / SpriteCell];
        for (int i = 0; i < f.Length; i++)
        {
            var c = new CroppedBitmap(strip, new Int32Rect(i * SpriteCell, 0, SpriteCell, SpriteCell));
            c.Freeze();
            f[i] = c;
        }
        return frameCache[id] = f;
    }

    static readonly Dictionary<string, BitmapSource?[]> poseCache = new();

    /// <summary>Duruş pozları, ZooPose sırasıyla (duruş, oturma, uyuma, yeme, sevinç). Hayvanda olmayan poz null.</summary>
    static BitmapSource?[] Poses(string id)
    {
        if (poseCache.TryGetValue(id, out var p)) return p;
        var strip = LoadSprite($"{id}_poses");
        p = new BitmapSource?[strip.PixelWidth / SpriteCell];
        var buf = new byte[SpriteCell * SpriteCell * 4];
        for (int i = 0; i < p.Length; i++)
        {
            var c = new CroppedBitmap(strip, new Int32Rect(i * SpriteCell, 0, SpriteCell, SpriteCell));
            var bgra = new FormatConvertedBitmap(c, PixelFormats.Bgra32, null, 0);
            bgra.CopyPixels(buf, SpriteCell * 4, 0);
            bool empty = true;
            for (int k = 3; k < buf.Length; k += 4) if (buf[k] != 0) { empty = false; break; }
            if (empty) continue;                                     // şeritte boş kare: bu hayvanda o poz yok
            c.Freeze();
            p[i] = c;
        }
        return poseCache[id] = p;
    }

    /// <summary>Hareketsiz, piksel keskinliğinde sprite (market, kutlama, siluet). İstenen poz yoksa ayakta duruş.</summary>
    static Image SpriteImage(string id, double size, bool silhouette, ZooPose pose = ZooPose.Stand)
    {
        BitmapSource src;
        if (!silhouette)
        {
            var poses = Poses(id);
            src = ((int)pose < poses.Length ? poses[(int)pose] : null) ?? poses[0]!;
        }
        else if (!silCache.TryGetValue(id, out src!)) src = silCache[id] = LoadSprite($"{id}_sil");
        var img = new Image { Width = size, Height = size, Source = src, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    // ---------- piksel sahne dokuları (kodla üretilir; ek dosya yok) ----------

    static ImageBrush? grassBrush;

    /// <summary>Döşenen piksel çimen dokusu (sprite'larla aynı 3× ölçek): açık pastel yeşil, tutamlar ve minik çiçekler.</summary>
    static ImageBrush GrassBrush()
    {
        if (grassBrush != null) return grassBrush;
        const int N = 64;
        var px = new uint[N * N];
        Array.Fill(px, 0xFF8DC963u);
        var rnd = new Random(11);
        for (int i = 0; i < 170; i++)                                // çim tutamları: 1×2 açık ya da koyu
        {
            int x = rnd.Next(N), y = rnd.Next(N);
            uint c = rnd.Next(3) == 0 ? 0xFF7BB853u : 0xFFA3D77Au;
            px[y * N + x] = c;
            px[(y + 1) % N * N + x] = c;
        }
        uint[] petals = { 0xFFFFFFFFu, 0xFFFFB8CBu, 0xFFFFE27Au };
        for (int i = 0; i < 7; i++)                                  // minik çiçekler: artı biçimli taç, sarı göbek
        {
            int x = rnd.Next(N), y = rnd.Next(N);
            uint c = petals[i % petals.Length];
            px[y * N + (x + 1) % N] = c;
            px[y * N + (x + N - 1) % N] = c;
            px[(y + 1) % N * N + x] = c;
            px[(y + N - 1) % N * N + x] = c;
            px[y * N + x] = c == 0xFFFFE27Au ? 0xFFFFFFFFu : 0xFFF5B731u;
        }
        var bmp = BitmapSource.Create(N, N, 96, 96, PixelFormats.Bgra32, null, px, N * 4);
        bmp.Freeze();
        grassBrush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, N * PixelScale, N * PixelScale), Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(grassBrush, BitmapScalingMode.NearestNeighbor);
        grassBrush.Freeze();
        return grassBrush;
    }

    static BitmapSource? backdrop;

    /// <summary>
    /// Sahnenin üst şeridi, piksel resim: dörtlü dizilimle (Bayer) kademelenen yumuşak gökyüzü, gülümseyen güneş,
    /// iki sıra pastel tepe ve tepelerde iki akasya. Tepelerin altı saydamdır; orada çimen dokusu görünür.
    /// </summary>
    static BitmapSource Backdrop()
    {
        if (backdrop != null) return backdrop;
        const int W = BackdropW, H = BackdropH;
        var px = new uint[W * H];
        int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
        uint[] sky = { 0xFFA9DCFFu, 0xFFBEE5FFu, 0xFFD4EEFFu, 0xFFE9F6FBu, 0xFFFFF3DFu };
        const double SkyRows = 38;

        // Güneş: sağa yakın, hâlesiyle
        const int sx = 232, sy = 12;   // en dar pencerede de görünsün (resim sola dayalı, sağı kırpılır)

        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            double t = Math.Min(1, y / SkyRows) * (sky.Length - 1);
            int band = (int)t;
            double frac = t - band;
            uint c = band + 1 < sky.Length && frac * 16 > bayer[y & 3, x & 3] ? sky[band + 1] : sky[band];

            double d = Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy));
            if (d <= 7.5) c = 0xFFFFD45Au;
            else if (d <= 10.5 && ((x + y) & 1) == 0) c = 0xFFFFEDB0u;          // seyrek noktalı hâle
            else if (d <= 13 && (x & 1) == 0 && (y & 1) == 0) c = 0xFFFFF4D0u;

            // Tepeler: uzak (soluk), orta (biraz daha yeşil), yakın kenar (çimenin başladığı yer)
            double far = 27 + 3.5 * Math.Sin(x * 0.035 + 1) + 2 * Math.Sin(x * 0.09);
            double mid = 34 + 3 * Math.Sin(x * 0.05 + 2.2) + 1.5 * Math.Sin(x * 0.13);
            double near = 41 + 2 * Math.Sin(x * 0.07 + 0.5) + 1 * Math.Sin(x * 0.19);
            int yf = (int)Math.Round(far), ym = (int)Math.Round(mid), yn = (int)Math.Round(near);
            if (y >= yf) c = y == yf ? 0xFFE0F2CBu : 0xFFCBE8B0u;
            if (y >= ym) c = y == ym ? 0xFFC9E8A9u : 0xFFB2DC8Eu;
            if (y >= yn) c = y == yn ? 0xFF9DD271u : 0u;                         // altı saydam: çimen dokusu
            px[y * W + x] = c;
        }

        // Güneşin yüzü: iki göz, pembe yanaklar, küçük gülümseme
        void Set(int x, int y, uint c) { if (x >= 0 && x < W && y >= 0 && y < H) px[y * W + x] = c; }
        Set(sx - 3, sy - 1, 0xFF9A6324u); Set(sx + 2, sy - 1, 0xFF9A6324u);
        Set(sx - 5, sy + 1, 0xFFFFA9A0u); Set(sx + 4, sy + 1, 0xFFFFA9A0u);
        Set(sx - 2, sy + 2, 0xFF9A6324u); Set(sx - 1, sy + 3, 0xFF9A6324u); Set(sx, sy + 3, 0xFF9A6324u); Set(sx + 1, sy + 2, 0xFF9A6324u);
        Set(sx - 3, sy - 4, 0xFFFFE89Au); Set(sx - 2, sy - 5, 0xFFFFE89Au); Set(sx - 1, sy - 5, 0xFFFFE89Au);

        // Akasyalar: ince gövde, yassı şemsiye taç (orta tepenin üstünde)
        foreach (var (tx, crown) in new[] { (52, 13), (176, 17), (104, 9) })
        {
            int baseY = (int)Math.Round(34 + 3 * Math.Sin(tx * 0.05 + 2.2) + 1.5 * Math.Sin(tx * 0.13));
            int top = baseY - 7;
            for (int y = top; y < baseY + 1; y++) Set(tx, y, 0xFF9C7A54u);
            Set(tx - 1, top + 2, 0xFF9C7A54u); Set(tx + 1, top + 1, 0xFF9C7A54u);
            for (int k = 0; k < 3; k++)
            {
                int half = crown / 2 - (k == 0 ? 2 : k == 2 ? 1 : 0);
                for (int x = tx - half; x <= tx + half; x++) Set(x, top - 2 + k, k == 0 ? 0xFF9FD07Au : 0xFF84BD62u);
            }
        }

        var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
        bmp.Freeze();
        return backdrop = bmp;
    }

    static readonly BitmapSource?[] cloudCache = new BitmapSource?[3];

    /// <summary>Piksel bulut: üst üste binen dairelerden; alt kenarı hafif mavi gölgeli.</summary>
    static BitmapSource CloudBitmap(int kind)
    {
        if (cloudCache[kind] is BitmapSource cached) return cached;
        var blobs = kind switch
        {
            0 => new (int X, int Y, int R)[] { (6, 7, 4), (12, 5, 5), (18, 7, 4), (22, 8, 3) },
            1 => new (int X, int Y, int R)[] { (5, 6, 3), (10, 4, 4), (15, 6, 3) },
            _ => new (int X, int Y, int R)[] { (5, 7, 4), (11, 4, 5), (17, 5, 4), (23, 7, 4), (28, 8, 3) },
        };
        int w = blobs.Max(b => b.X + b.R) + 1, h = blobs.Max(b => b.Y + b.R) + 1;
        var px = new uint[w * h];
        int bottom = blobs.Max(b => b.Y) + 1;                    // düz alt kenar
        for (int y = 0; y < Math.Min(h, bottom + 2); y++)
        for (int x = 0; x < w; x++)
        {
            bool inside = blobs.Any(b => (x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y) <= b.R * b.R);
            if (!inside) continue;
            bool shade = !blobs.Any(b => (x - b.X) * (x - b.X) + (y + 1 - b.Y) * (y + 1 - b.Y) <= b.R * b.R) || y >= bottom + 1;
            px[y * w + x] = shade ? 0xFFE1EFFAu : 0xFFFFFFFFu;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return cloudCache[kind] = bmp;
    }

    void BuyAnimal(string id)
    {
        if (Zoo.Buy(data, id) != BuyResult.Ok) return;
        Save();
        zooBoughtId = id;
        GoZoo(ZooScreen.Bought);
    }
}
