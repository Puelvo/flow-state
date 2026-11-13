using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlowState;

// Bahçe sekmesi üç ekrandan oluşur:
//   Bahçe  → sahip olunan hayvanlar + alınmamışların siluetleri; "Market" düğmesi
//   Market → sıradaki fiyat, alınabilecek hayvanlar
//   Alındı → kutlama; "Bahçeye götür" ile bahçeye dönülür, yeni hayvan yürüyerek girer
// İSKELET: emoji + basit animasyon; pixel art sonradan bunun üzerine giydirilecek.
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

    static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

    /// <summary>Sekmeden çıkınca bir dahaki girişte bahçeden başlansın.</summary>
    void ResetZooScreen()
    {
        if (zooScreen == ZooScreen.Bought && zooBoughtId != null) zooArriving = zooBoughtId; // kutlamayı atladıysa da yürüsün
        zooScreen = ZooScreen.Garden;
    }

    void UpdateZoo()
    {
        int balance = Zoo.Balance(data, ActiveSeconds);
        var next = Zoo.NextPrice(data, ActiveMapId);
        bool canBuy = next is int p && balance >= p;

        if (zooBalance != null) zooBalance.Text = $"🪙 {balance}";
        if (zooMarketBtn != null) zooMarketBtn.Background = canBuy ? Res("Accent") : Res("Panel2");

        // Düzen yalnızca ekran, sahip olunanlar ya da (markette) "alınabilir mi" değişince yeniden kurulur.
        var key = $"{zooScreen}|{data.OwnedAnimals.Count}|{(zooScreen == ZooScreen.Shop ? canBuy : false)}";
        if (key == zooKey) return;
        zooKey = key;

        ZooRoot.Children.Clear();
        zooMarketBtn = null;
        switch (zooScreen)
        {
            case ZooScreen.Garden: BuildGarden(balance, canBuy); break;
            case ZooScreen.Shop: BuildShop(balance, next); break;
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

        // Bahçe: sahip olunanlar renkli, alınmamışlar gri siluet.
        string? arriving = zooArriving;
        zooArriving = null;
        var garden = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in ZooCatalog.InMap(ActiveMapId))
        {
            if (!Zoo.Owns(data, a.Id)) { garden.Children.Add(SilhouetteTile(a, 56)); continue; }
            var tile = AnimalTile(a, 56);
            if (a.Id == arriving) Arrive(tile);
            garden.Children.Add(tile);
        }
        ZooRoot.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0xC8, 0x9B, 0x3C)), // geçici savana tonu
            CornerRadius = new CornerRadius(14), Padding = new Thickness(18), Margin = new Thickness(0, 16, 0, 0),
            MinHeight = 260, ClipToBounds = true, Child = garden
        });

        if (Zoo.MapComplete(data, ActiveMapId))
            ZooRoot.Children.Add(new TextBlock { Text = $"🎉 {map.Name} tamamlandı!", FontSize = 16, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
    }

    void BuildShop(int balance, int? nextPrice)
    {
        var back = new Button { Content = "← Bahçe", Style = (Style)FindResource("Btn"), FontSize = 15, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        back.Click += (_, _) => GoZoo(ZooScreen.Garden);
        ZooRoot.Children.Add(Header("🛒 Market", balance, back));

        if (nextPrice is not int price)
        {
            ZooRoot.Children.Add(new TextBlock { Text = "Bu haritadaki tüm hayvanlar bahçende. 🎉", FontSize = 16, Margin = new Thickness(0, 16, 0, 0) });
            return;
        }
        ZooRoot.Children.Add(new TextBlock { Text = $"Sıradaki hayvan: {price} 🪙", Foreground = Res("Muted"), Margin = new Thickness(0, 10, 0, 12) });

        bool enough = balance >= price;
        var shop = new WrapPanel();
        foreach (var a in ZooCatalog.InMap(ActiveMapId).Where(a => !Zoo.Owns(data, a.Id)))
        {
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
            var card = AnimalTile(a, 44);
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

        var emoji = new TextBlock
        {
            Text = a.Emoji, FontSize = 110, FontFamily = EmojiFont, HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Effect = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD2, 0x5C), ShadowDepth = 0, BlurRadius = 40, Opacity = 0.8 }
        };
        var scale = new ScaleTransform(0.2, 0.2);
        emoji.RenderTransform = scale;
        emoji.Loaded += (_, _) =>
        {
            var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(900)) { EasingFunction = new ElasticEase { Oscillations = 2, Springiness = 4 } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        };
        panel.Children.Add(emoji);

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
        p.Children.Add(new TextBlock { Text = a.Emoji, FontSize = size, FontFamily = EmojiFont, HorizontalAlignment = HorizontalAlignment.Center });
        p.Children.Add(new TextBlock { Text = a.Name, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
        return p;
    }

    /// <summary>Alınmamış hayvan: emojinin şeklinde gri gölge, adı gizli.</summary>
    StackPanel SilhouetteTile(Animal a, double size)
    {
        var glyph = new TextBlock { Text = a.Emoji, FontSize = size, FontFamily = EmojiFont };
        glyph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        glyph.Arrange(new Rect(glyph.DesiredSize));
        var shape = new Rectangle
        {
            Width = glyph.DesiredSize.Width, Height = glyph.DesiredSize.Height,
            Fill = new SolidColorBrush(Color.FromArgb(0x70, 0x00, 0x00, 0x00)),
            OpacityMask = new VisualBrush(glyph) { Stretch = Stretch.None },
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var p = new StackPanel { Margin = new Thickness(12, 6, 12, 6), MinWidth = size + 30, HorizontalAlignment = HorizontalAlignment.Center };
        p.Children.Add(shape);
        p.Children.Add(new TextBlock { Text = "???", FontSize = 13, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center });
        return p;
    }

    /// <summary>Yeni hayvan soldan zıplayarak yürür, yerine oturunca parlar; üstünde "Yeni!" yazar.</summary>
    void Arrive(StackPanel tile)
    {
        tile.Children.Insert(0, new Border
        {
            Background = Res("Accent"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 1, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
            Child = new TextBlock { Text = "Yeni!", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White }
        });

        var move = new TranslateTransform(-700, 0);
        tile.RenderTransform = move;
        var glow = new DropShadowEffect { Color = Color.FromRgb(0xFF, 0xD2, 0x5C), ShadowDepth = 0, BlurRadius = 30, Opacity = 0 };
        tile.Effect = glow;

        tile.Loaded += (_, _) =>
        {
            var walk = TimeSpan.FromMilliseconds(1600);
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-700, 0, walk) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(160))
            {
                AutoReverse = true, RepeatBehavior = new RepeatBehavior(5), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
            {
                BeginTime = walk, AutoReverse = true, RepeatBehavior = new RepeatBehavior(2)
            });
        };
    }

    void BuyAnimal(string id)
    {
        if (Zoo.Buy(data, id, ActiveSeconds) != BuyResult.Ok) return;
        Save();
        zooBoughtId = id;
        GoZoo(ZooScreen.Bought);
    }
}
