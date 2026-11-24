using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace FlowState;

// Oturum sonu jeton ödülü: çemberin üstünde "+N jeton" kartı açılır, sayı sayarak artar;
// jetonlar çemberden saçılıp Bahçe sekmesine uçar, sekme her jetonda zıplar. Hepsi FxLayer'da çizilir.
public partial class MainWindow
{
    Brush CoinText => Res("CoinInk");
    readonly Random fxRnd = new();

    void ShowCoinReward(Session s, bool timeUp, Special? special = null, string? growth = null)
    {
        int coins = Zoo.SessionCoins(s);
        var course = data.Courses.FirstOrDefault(c => c.Id == s.CourseId)?.Name ?? "";
        FxLayer.UpdateLayout();
        var center = RewardOrigin();

        var card = RewardCard(coins, timeUp, $"{course} · {Fmt(s.Seconds)}", special, growth, out var count);
        double zoom = widgetMode ? 0.62 : 1;
        var scale = new ScaleTransform(0.4 * zoom, 0.4 * zoom);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = scale;
        card.Opacity = 0;
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(card, center.X - card.DesiredSize.Width / 2);
        Canvas.SetTop(card, center.Y - card.DesiredSize.Height / 2);
        FxLayer.Children.Add(card);

        // Açılış: yaylanarak büyür.
        var pop = new DoubleAnimation(0.4 * zoom, zoom, TimeSpan.FromSeconds(0.5)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));

        if (count != null) CountUp(count, coins, delayMs: 250, durationMs: 900);
        if (coins > 0) BurstCoins(center, Math.Clamp(coins, 3, 14), coins);

        // Kapanış: biraz bekler, küçülerek kaybolur.
        var hold = TimeSpan.FromSeconds(special != null ? 4.8 : coins > 0 ? 3.2 : 2.6);
        After(hold, () =>
        {
            var shrink = new DoubleAnimation(zoom, 0.85 * zoom, TimeSpan.FromSeconds(0.3)) { EasingFunction = new SineEase() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.3));
            fade.Completed += (_, _) => FxLayer.Children.Remove(card);
            card.BeginAnimation(OpacityProperty, fade);
        });
    }

    /// <summary>Ödülün çıktığı nokta: Çalış görünümündeyse çemberin ortası, değilse pencerenin ortası.</summary>
    Point RewardOrigin()
    {
        if (TimerView.IsVisible && RingHost.IsVisible && RingHost.ActualWidth > 0)
            return RingHost.TranslatePoint(new Point(RingHost.ActualWidth / 2, RingHost.ActualHeight / 2), FxLayer);
        return new Point(FxLayer.ActualWidth / 2, FxLayer.ActualHeight / 2);
    }

    FrameworkElement RewardCard(int coins, bool timeUp, string summary, Special? special, string? growth, out TextBlock? count)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Üst rozet: "Süre doldu!" / "Oturum tamam!"
        var badgeText = new TextBlock { Text = timeUp ? "Süre doldu!" : "Oturum tamam!", FontSize = 13, FontWeight = FontWeights.ExtraBold };
        badgeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeep");
        var badgeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var spark = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("IconSparkle"), Width = 12, Height = 12, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        spark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Accent");
        badgeRow.Children.Add(spark);
        badgeRow.Children.Add(badgeText);
        var badge = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 5), HorizontalAlignment = HorizontalAlignment.Center, Child = badgeRow };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        stack.Children.Add(badge);

        count = null;
        if (coins > 0)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            var glyph = CoinGlyph(46);
            row.Children.Add(glyph);
            count = new TextBlock { Text = "+0", FontSize = 46, FontWeight = FontWeights.Black, Foreground = CoinText, Margin = new Thickness(10, -4, 0, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 70 };
            row.Children.Add(count);
            stack.Children.Add(row);
            // Jeton yavaşça sallanır.
            var rot = new RotateTransform();
            glyph.RenderTransformOrigin = new Point(0.5, 0.5);
            glyph.RenderTransform = rot;
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-12, 12, TimeSpan.FromSeconds(0.5))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            stack.Children.Add(Line("jeton kazandın", 15, FontWeights.ExtraBold, "Text", 2));
        }
        else
        {
            stack.Children.Add(Line("Bu oturum jeton için biraz kısaydı", 16, FontWeights.ExtraBold, "Text", 14));
            stack.Children.Add(Line("Biraz daha uzun odaklan, jetonlar gelsin.", 13, FontWeights.SemiBold, "Muted", 4));
        }
        stack.Children.Add(Line(summary, 13, FontWeights.Bold, "Muted", 6));
        if (growth != null)
        {
            // Bahçede büyüme haberi: yeşil hap.
            var g = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 12, 5), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            g.SetResourceReference(Border.BackgroundProperty, "GoodSoft");
            var gt = new TextBlock { Text = growth, FontSize = 12.5, FontWeight = FontWeights.ExtraBold };
            gt.SetResourceReference(TextBlock.ForegroundProperty, "Good");
            g.Child = gt;
            stack.Children.Add(g);
        }

        if (special != null && ZooCatalog.Find(special.AnimalId) is { } animal)
        {
            // Sürpriz: nadir dost bahçeye katıldı.
            stack.Children.Add(new Border { Height = 2, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, 14, 0, 12), Background = Res("Line") });
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var stand = new Grid { Width = 64, Height = 64 };
            stand.Children.Add(new System.Windows.Shapes.Ellipse { Width = 60, Height = 60, Fill = Res(special.Kind == SpecialKind.Golden ? "WarnSoft" : "AccentSoft") });
            stand.Children.Add(SpecialImage(animal.Id, special.Kind, 56, locked: false));
            row.Children.Add(stand);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var tag = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, Background = Res("WarnSoft"),
                Child = new TextBlock { Text = "Sürpriz!", FontSize = 12, FontWeight = FontWeights.Black, Foreground = CoinText } };
            text.Children.Add(tag);
            var name = new TextBlock { Text = Special.Title(animal.Name, special.Kind), FontSize = 17, FontWeight = FontWeights.Black, Margin = new Thickness(0, 4, 0, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            text.Children.Add(name);
            var sub = new TextBlock { Text = "bahçene katıldı", FontSize = 13, FontWeight = FontWeights.Bold };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            text.Children.Add(sub);
            row.Children.Add(text);
            stack.Children.Add(row);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(26), Padding = new Thickness(30, 18, 30, 20), MinWidth = 240,
            BorderThickness = new Thickness(3), Child = stack, Effect = (Effect)FindResource("LiftShadow")
        };
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE7, 0xA8));
        return card;

        TextBlock Line(string text, double size, FontWeight weight, string fg, double top)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, top, 0, 0) };
            t.SetResourceReference(TextBlock.ForegroundProperty, fg);
            return t;
        }
    }

    /// <summary>Sayı 0'dan hedefe yavaşlayarak sayar; bitince küçük bir zıplama yapar.</summary>
    void CountUp(TextBlock tb, int target, int delayMs, int durationMs)
    {
        var start = DateTime.UtcNow.AddMilliseconds(delayMs);
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        t.Tick += (_, _) =>
        {
            double k = Math.Clamp((DateTime.UtcNow - start).TotalMilliseconds / durationMs, 0, 1);
            double eased = 1 - Math.Pow(1 - k, 3);
            tb.Text = $"+{(int)Math.Round(target * eased)}";
            if (k < 1) return;
            t.Stop();
            var sc = new ScaleTransform();
            tb.RenderTransformOrigin = new Point(0.5, 0.5);
            tb.RenderTransform = sc;
            var bump = new DoubleAnimation(1.25, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
        };
        t.Start();
    }

    /// <summary>Jetonlar merkezden saçılır, kısa bir duraksamadan sonra Bahçe sekmesine uçar.</summary>
    void BurstCoins(Point from, int n, int total)
    {
        // Hedef: Çalış'taki jeton hapı; o görünmüyorsa Dostlar sekmesi; widget'ta yukarı uçup kaybolur.
        FrameworkElement? dest = !widgetMode && sceneCoinPill is { IsVisible: true } ? sceneCoinPill : !widgetMode && NavZoo.IsVisible ? NavZoo : null;
        bool toNav = dest != null;
        Point target = dest != null
            ? dest.TranslatePoint(new Point(dest.ActualWidth / 2, dest.ActualHeight / 2), FxLayer)
            : new Point(from.X, from.Y - 160);
        int arrived = 0;
        double size = widgetMode ? 16 : 24;

        for (int i = 0; i < n; i++)
        {
            var coin = CoinGlyph(size);
            var tt = new TranslateTransform(from.X - size / 2, from.Y - size / 2);
            var sc = new ScaleTransform(0.3, 0.3);
            coin.RenderTransformOrigin = new Point(0.5, 0.5);
            coin.RenderTransform = new TransformGroup { Children = { sc, tt } };
            FxLayer.Children.Insert(0, coin); // kartın arkasında doğar, yazının üstünü kapatmaz

            // 1) Saçılma: kartın çevresine, eşit aralıklı yönlere (biraz rastgele), hafif yukarı ağırlıklı.
            double ang = (i + fxRnd.NextDouble() * 0.6) / n * Math.PI * 2;
            double r = (widgetMode ? 95 : 185) + fxRnd.NextDouble() * (widgetMode ? 25 : 45);
            double bx = from.X + Math.Cos(ang) * r - size / 2, by = from.Y + Math.Sin(ang) * r * 0.75 - 20 - size / 2;
            var burst = TimeSpan.FromSeconds(0.45);
            var outEase = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(bx, burst) { EasingFunction = outEase });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(by, burst) { EasingFunction = outEase });
            var grow = new DoubleAnimation(0.3, 1, burst) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            // 2) Uçuş: X ve Y farklı yumuşatmayla → kavisli yol; yolda küçülür.
            var delay = TimeSpan.FromSeconds(0.75 + i * 0.08);
            var fly = TimeSpan.FromSeconds(0.65);
            var ax = new DoubleAnimation(target.X - size / 2, fly) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            var ay = new DoubleAnimation(target.Y - size / 2, fly) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
            var shrink = new DoubleAnimation(toNav ? 0.45 : 0.6, fly) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            ax.Completed += (_, _) =>
            {
                FxLayer.Children.Remove(coin);
                if (!toNav) return;
                arrived++;
                if (dest == sceneCoinPill && sceneCoinPop != null) Pop(sceneCoinPop, 1.15); else PulseNavZoo();
                if (arrived == n) FloatAtNav(target, $"+{total}");
            };
            if (!toNav) coin.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fly) { BeginTime = delay });
            // Gecikme dolunca uçuş, saçılmanın bıraktığı yerden başlar (aynı özelliğe yeni animasyon).
            After(delay, () =>
            {
                tt.BeginAnimation(TranslateTransform.XProperty, ax);
                tt.BeginAnimation(TranslateTransform.YProperty, ay);
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            });
        }
    }

    ScaleTransform? navZooScale;

    void PulseNavZoo()
    {
        if (navZooScale == null)
        {
            navZooScale = new ScaleTransform();
            NavZoo.RenderTransformOrigin = new Point(0.5, 0.5);
            NavZoo.RenderTransform = navZooScale;
        }
        var bump = new DoubleAnimation(1.18, 1, TimeSpan.FromSeconds(0.3)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        navZooScale.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
        navZooScale.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
    }

    /// <summary>Bahçe sekmesinin altında süzülüp kaybolan "+N" yazısı.</summary>
    void FloatAtNav(Point at, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(CoinGlyph(16));
        row.Children.Add(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.Black, Foreground = CoinText, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var pill = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 4, 10, 4), Child = row, Effect = (Effect)FindResource("SoftShadow") };
        pill.SetResourceReference(Border.BackgroundProperty, "Surface");
        pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tt = new TranslateTransform(at.X - pill.DesiredSize.Width / 2, at.Y + 26);
        pill.RenderTransform = tt;
        FxLayer.Children.Add(pill);
        tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(at.Y + 26, at.Y + 40, TimeSpan.FromSeconds(1.6)) { EasingFunction = new SineEase() });
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5)) { BeginTime = TimeSpan.FromSeconds(1.2) };
        fade.Completed += (_, _) => FxLayer.Children.Remove(pill);
        pill.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Kısa gecikmeyle bir işi UI iş parçacığında çalıştırır.</summary>
    void After(TimeSpan delay, Action act)
    {
        var t = new DispatcherTimer { Interval = delay };
        t.Tick += (_, _) => { t.Stop(); act(); };
        t.Start();
    }
}
