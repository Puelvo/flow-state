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
public partial class MainWindow
{
    const string ActiveMapId = "savanna"; // tek harita; harita seçimi ikinci harita gelince

    enum ZooScreen { Garden, Shop, Bought }
    ZooScreen zooScreen = ZooScreen.Garden;
    string? zooBoughtId;   // kutlama ekranındaki hayvan
    string? zooArriving;   // bahçeye ilk kez girecek hayvan (animasyon bir kez oynar)

    string zooKey = "";    // son çizilen durumun anahtarı (her saniye yeniden kurmamak için)
    TextBlock? zooBalance;
    Button? zooMarketBtn;

    /// <summary>Sekmeden çıkınca bir dahaki girişte bahçeden başlansın.</summary>
    void ResetZooScreen()
    {
        if (zooScreen == ZooScreen.Bought && zooBoughtId != null) zooArriving = zooBoughtId; // kutlamayı atladıysa da yürüsün
        zooScreen = ZooScreen.Garden;
    }

    void UpdateZoo()
    {
        int balance = Zoo.Balance(data, ActiveSeconds);
        int affordable = Zoo.AffordableCount(data, ActiveMapId, balance);
        bool canBuy = affordable > 0;

        if (zooBalance != null) zooBalance.Text = $"🪙 {balance}";
        if (zooMarketBtn != null) zooMarketBtn.Background = canBuy ? Res("Accent") : Res("Panel2");

        // Düzen yalnızca ekran, sahip olunanlar ya da (markette) alınabilir hayvan sayısı değişince yeniden kurulur.
        var key = $"{zooScreen}|{data.OwnedAnimals.Count}|{(zooScreen == ZooScreen.Shop ? affordable : 0)}";
        if (key == zooKey) return;
        zooKey = key;

        ZooRoot.Children.Clear();
        zooNodes.Clear();
        zooCanvas = null;
        zooMarketBtn = null;
        switch (zooScreen)
        {
            case ZooScreen.Garden: BuildGarden(balance, canBuy); break;
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

    void BuildGarden(int balance, bool canBuy)
    {
        var map = ZooCatalog.Map(ActiveMapId);

        zooMarketBtn = new Button
        {
            Content = "🛒 Market", Style = (Style)FindResource("Btn"), FontSize = 15, Margin = new Thickness(14, 0, 0, 0),
            Background = canBuy ? Res("Accent") : Res("Panel2"), VerticalAlignment = VerticalAlignment.Center
        };
        zooMarketBtn.Click += (_, _) => GoZoo(ZooScreen.Shop);
        ZooRoot.Children.Add(Header($"{map.Emoji} {map.Name}", balance, zooMarketBtn));

        // Sahne: çimen zemin, üstünde dolaşan hayvanlar.
        var canvas = new Canvas();
        var scene = new Border
        {
            Background = GrassBrush(), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 16, 0, 0),
            Height = 380, ClipToBounds = true, Child = canvas
        };
        RenderOptions.SetBitmapScalingMode(scene, BitmapScalingMode.NearestNeighbor);
        canvas.SizeChanged += (_, e) => { zooWorld.Width = e.NewSize.Width; zooWorld.Height = e.NewSize.Height; };
        zooCanvas = canvas;
        ZooRoot.Children.Add(scene);

        // Dünyayı sahip olunanlarla eşitle; konumlar ekran yeniden kurulsa da korunur.
        string? arriving = zooArriving;
        zooArriving = null;
        var owned = ZooCatalog.InMap(ActiveMapId).Where(a => ZooDemo || Zoo.Owns(data, a.Id)).ToList();
        zooWorld.Animals.RemoveAll(w => owned.All(a => a.Id != w.Id));
        foreach (var a in owned)
        {
            var w = zooWorld.Animals.FirstOrDefault(x => x.Id == a.Id) ?? zooWorld.Add(a.Id, arriving: a.Id == arriving);
            AddNode(canvas, a, w);
        }
        if (owned.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = "Bahçen henüz boş.\nMarketten ilk hayvanını al!", TextAlignment = TextAlignment.Center,
                FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Opacity = 0.9
            };
            canvas.SizeChanged += (_, e) =>
            {
                hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(hint, (e.NewSize.Width - hint.DesiredSize.Width) / 2);
                Canvas.SetTop(hint, (e.NewSize.Height - hint.DesiredSize.Height) / 2);
            };
            canvas.Children.Add(hint);
        }
        EnsureZooTimer();

        // Henüz alınmamışlar: sahnenin altında siluet olarak (merak uyandırsın).
        var missing = ZooCatalog.InMap(ActiveMapId).Where(a => !Zoo.Owns(data, a.Id)).ToList();
        if (missing.Count > 0)
        {
            var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var a in missing) row.Children.Add(SilhouetteTile(a, 56));
            ZooRoot.Children.Add(row);
        }
        else
            ZooRoot.Children.Add(new TextBlock { Text = $"🎉 {map.Name} tamamlandı!", FontSize = 16, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
    }

    void BuildShop(int balance)
    {
        var back = new Button { Content = "← Bahçe", Style = (Style)FindResource("Btn"), FontSize = 15, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        back.Click += (_, _) => GoZoo(ZooScreen.Garden);
        ZooRoot.Children.Add(Header("🛒 Market", balance, back));

        var left = ZooCatalog.InMap(ActiveMapId).Where(a => !Zoo.Owns(data, a.Id)).OrderBy(a => a.Price).ToList();
        if (left.Count == 0)
        {
            ZooRoot.Children.Add(new TextBlock { Text = "Bu haritadaki tüm hayvanlar bahçende. 🎉", FontSize = 16, Margin = new Thickness(0, 16, 0, 0) });
            return;
        }

        var shop = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        foreach (var a in left)
        {
            int price = a.Price;
            bool enough = balance >= price;
            var buy = new Button
            {
                // Yetmiyorsa ne kadar çalışılacağı söylenmez; jeton kuralı gizli kalır.
                Content = enough ? $"Al · {price} 🪙" : "Biraz daha çalış",
                Style = (Style)FindResource("Btn"), Padding = new Thickness(14, 6, 14, 6),
                FontSize = 13, Margin = new Thickness(0, 8, 0, 0), IsEnabled = enough
            };
            if (enough) buy.Background = Res("Accent");
            var id = a.Id;
            buy.Click += (_, _) => BuyAnimal(id);
            var card = AnimalTile(a, 80);
            card.Children.Add(buy);
            shop.Children.Add(new Border
            {
                Background = Res("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 14),
                Margin = new Thickness(0, 0, 12, 12), Width = 160, Child = card
            });
        }
        ZooRoot.Children.Add(shop);
    }

    void BuildBought()
    {
        var a = zooBoughtId is string id ? ZooCatalog.Find(id) : null;
        if (a == null) { GoZoo(ZooScreen.Garden); return; }

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 50, 0, 0) };

        var sprite = SpriteImage(a.Id, 192, silhouette: false);
        sprite.RenderTransformOrigin = new Point(0.5, 0.5);
        sprite.Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD2, 0x5C), ShadowDepth = 0, BlurRadius = 40, Opacity = 0.8 };
        var scale = new ScaleTransform(0.2, 0.2);
        sprite.RenderTransform = scale;
        sprite.Loaded += (_, _) =>
        {
            var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        };
        panel.Children.Add(sprite);

        panel.Children.Add(new TextBlock { Text = $"{a.Name} yolda!", FontSize = 30, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 4) });
        panel.Children.Add(new TextBlock { Text = "Yeni yuvasını görmek için sabırsızlanıyor.", Foreground = Res("Muted"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center });

        var go = new Button
        {
            Content = "Bahçeye götür →", Style = (Style)FindResource("Btn"), Background = Res("Accent"),
            FontSize = 17, FontWeight = FontWeights.SemiBold, Padding = new Thickness(28, 12, 28, 12),
            Margin = new Thickness(0, 26, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
        };
        go.Click += (_, _) => { zooArriving = a.Id; zooBoughtId = null; GoZoo(ZooScreen.Garden); };
        panel.Children.Add(go);

        ZooRoot.Children.Add(panel);
    }

    // ---------- dolaşan hayvanlar ----------

    /// <summary>Önizleme: FLOWSTATE_ZOO_DEMO=1 ile açılırsa bahçede tüm hayvanlar dolaşır (veri değişmez).</summary>
    static readonly bool ZooDemo = Environment.GetEnvironmentVariable("FLOWSTATE_ZOO_DEMO") == "1";

    readonly ZooWorld zooWorld = new();
    readonly List<ZooNode> zooNodes = new();
    Canvas? zooCanvas;
    DispatcherTimer? zooTimer;
    readonly Stopwatch zooClock = new();

    /// <summary>Bir hayvanın sahnedeki görseli: gölge + sprite + üstünde balon.</summary>
    sealed record ZooNode(Wanderer W, Canvas Root, Image Img, ScaleTransform Flip, TranslateTransform Hop,
                          Border Bubble, TextBlock BubbleText, BitmapSource[] Frames, BitmapSource?[] Poses, string Name);

    void AddNode(Canvas canvas, Animal a, Wanderer w)
    {
        const double S = ZooWorld.Sprite;
        var frames = Frames(a.Id);
        var poses = Poses(a.Id);
        w.Has = poses.Select(p => p != null).ToArray();

        var shadow = new Ellipse { Width = S * 0.62, Height = S * 0.16, Fill = new SolidColorBrush(Color.FromArgb(0x55, 0x1E, 0x3A, 0x12)) };
        Canvas.SetLeft(shadow, S * 0.19); Canvas.SetTop(shadow, S * 0.86);

        var flip = new ScaleTransform(1, 1, S / 2, S / 2);
        var hop = new TranslateTransform();
        var img = new Image { Width = S, Height = S, Source = poses[0], Cursor = Cursors.Hand, RenderTransform = new TransformGroup { Children = { flip, hop } } };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        img.MouseLeftButtonDown += (_, e) => { zooWorld.Poke(w); e.Handled = true; };

        var text = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x2A, 0x24, 0x36)), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 1, 8, 2), Child = text, Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };

        var root = new Canvas { Width = S, Height = S };
        root.Children.Add(shadow);
        root.Children.Add(img);
        root.Children.Add(bubble);
        canvas.Children.Add(root);
        zooNodes.Add(new ZooNode(w, root, img, flip, hop, bubble, text, frames, poses, a.Name));
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
        if (zooCanvas == null || zooScreen != ZooScreen.Garden || !ZooView.IsVisible) { zooClock.Reset(); return; }
        double dt = zooClock.IsRunning ? zooClock.Elapsed.TotalSeconds : 0;
        zooClock.Restart();
        zooWorld.Update(dt);

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

            string? label = w.Arriving ? "Yeni!" : w.State == WanderState.Greet ? "❤" : w.NameTimer > 0 ? n.Name
                : w.State == WanderState.Idle && w.Shown == ZooPose.Sleep ? "z z" : null;
            if (label == null) { n.Bubble.Visibility = Visibility.Collapsed; continue; }
            if (n.BubbleText.Text != label)
            {
                n.BubbleText.Text = label;
                n.Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            }
            n.Bubble.Visibility = Visibility.Visible;
            Canvas.SetLeft(n.Bubble, (S - n.Bubble.DesiredSize.Width) / 2);
            Canvas.SetTop(n.Bubble, 2 + n.Hop.Y);
        }
    }

    // ---------- parçalar ----------

    DockPanel Header(string title, int balance, Button action)
    {
        var head = new DockPanel();
        DockPanel.SetDock(action, Dock.Right);
        head.Children.Add(action);
        zooBalance = new TextBlock { Text = $"🪙 {balance}", FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Jeton" };
        DockPanel.SetDock(zooBalance, Dock.Right);
        head.Children.Add(zooBalance);
        head.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        return head;
    }

    StackPanel AnimalTile(Animal a, double size)
    {
        var p = new StackPanel { Margin = new Thickness(12, 6, 12, 6), MinWidth = size + 30, HorizontalAlignment = HorizontalAlignment.Center };
        p.Children.Add(SpriteImage(a.Id, size, silhouette: false));
        p.Children.Add(new TextBlock { Text = a.Name, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
        return p;
    }

    /// <summary>Alınmamış hayvan: koyu silueti, adı gizli.</summary>
    StackPanel SilhouetteTile(Animal a, double size)
    {
        var p = new StackPanel { Margin = new Thickness(12, 6, 12, 6), MinWidth = size + 30, HorizontalAlignment = HorizontalAlignment.Center };
        var shape = SpriteImage(a.Id, size, silhouette: true);
        shape.Opacity = 0.45;
        p.Children.Add(shape);
        p.Children.Add(new TextBlock { Text = "???", FontSize = 13, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
        return p;
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

    /// <summary>Hareketsiz, piksel keskinliğinde sprite (market, kutlama, siluet).</summary>
    static Image SpriteImage(string id, double size, bool silhouette)
    {
        BitmapSource src;
        if (!silhouette) src = Poses(id)[0]!;
        else if (!silCache.TryGetValue(id, out src!)) src = silCache[id] = LoadSprite($"{id}_sil");
        var img = new Image { Width = size, Height = size, Source = src, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        return img;
    }

    static ImageBrush? grassBrush;

    /// <summary>Döşenen piksel çimen dokusu (sprite'larla aynı 3× ölçek).</summary>
    static ImageBrush GrassBrush()
    {
        if (grassBrush != null) return grassBrush;
        const int N = 64;
        var px = new uint[N * N];
        Array.Fill(px, 0xFF6DAA47u);
        var rnd = new Random(11);
        for (int i = 0; i < 150; i++)                                // çim tutamları: 1×2 açık ya da koyu
        {
            int x = rnd.Next(N), y = rnd.Next(N);
            uint c = rnd.Next(3) == 0 ? 0xFF5E9A40u : 0xFF82BF58u;
            px[y * N + x] = c;
            px[(y + 1) % N * N + x] = c;
        }
        var bmp = BitmapSource.Create(N, N, 96, 96, PixelFormats.Bgra32, null, px, N * 4);
        bmp.Freeze();
        grassBrush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, N * 3, N * 3), Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapScalingMode(grassBrush, BitmapScalingMode.NearestNeighbor);
        grassBrush.Freeze();
        return grassBrush;
    }

    void BuyAnimal(string id)
    {
        if (Zoo.Buy(data, id, ActiveSeconds) != BuyResult.Ok) return;
        Save();
        zooBoughtId = id;
        GoZoo(ZooScreen.Bought);
    }
}
