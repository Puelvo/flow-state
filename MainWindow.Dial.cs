using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace FlowState;

// Geri sayım süre kadranı: sayfa hafifçe kararır, ortada bir kart açılır. Çemberdeki tutamaç çevrilerek
// süre seçilir (tam tur = 2 saat, 5 dk adım); fare tekerleği ve ok tuşları da çalışır. "Başla" süreyi
// kaydedip oturumu hemen başlatır; "Vazgeç", Esc ya da karartılmış alana tıklamak kapatır.
public partial class MainWindow
{
    const int DialMin = 5, DialMax = 120, DialStep = 5;
    const double DialSize = 250, DialR = 104, DialStroke = 18;

    bool DialOpen => DialLayer.Visibility == Visibility.Visible;

    int dialValue;
    bool dialPrevCountdown;
    Path? dialArc;
    Grid? dialKnob;
    TextBlock? dialNumber, dialUnit;
    ScaleTransform? dialNumberPop;
    UniformGrid? dialChips;

    void OpenDial()
    {
        if (DialOpen) return;
        // Geri sayım bu tıklamayla seçildiyse Vazgeç kronometreye döner; zaten seçiliyse geri sayımda kalır.
        dialPrevCountdown = !countdownJustChecked;
        countdownJustChecked = false;
        dialValue = Math.Clamp(data.CountdownMinutes, DialMin, DialMax);
        BuildDial();
        DialLayer.Visibility = Visibility.Visible;
        UpdateDial(pop: false);
    }

    // Checked olayı Click'ten önce gelir: Mode_Checked bunu işaretler, OpenDial okur.
    bool countdownJustChecked;

    void CloseDial()
    {
        if (!DialOpen) return;
        DialLayer.Visibility = Visibility.Collapsed;
        DialLayer.Children.Clear();
    }

    void CancelDial()
    {
        CloseDial();
        if (!dialPrevCountdown) ModeStopwatch.IsChecked = true; // önceden kronometredeyse ona dön
    }

    void StartFromDial()
    {
        data.CountdownMinutes = dialValue;
        data.Countdown = true;
        Store.Save(data);
        CloseDial();
        if (ModeCountdown.IsChecked != true) ModeCountdown.IsChecked = true;
        if (!active && SelectedCourse != null) ToggleTimer();
        else UpdateAll();
    }

    /// <summary>Kadran açıkken klavye: Esc kapat, Enter/Boşluk başla, oklar ±5 dk.</summary>
    bool DialKey(Key k)
    {
        switch (k)
        {
            case Key.Escape: CancelDial(); return true;
            case Key.Enter: case Key.Space: StartFromDial(); return true;
            case Key.Right: case Key.Up: SetDial(dialValue + DialStep); return true;
            case Key.Left: case Key.Down: SetDial(dialValue - DialStep); return true;
        }
        return false;
    }

    void SetDial(int minutes)
    {
        minutes = Math.Clamp(minutes, DialMin, DialMax);
        if (minutes == dialValue) return;
        dialValue = minutes;
        UpdateDial(pop: true);
    }

    // ---------- çizim ----------

    void BuildDial()
    {
        DialLayer.Children.Clear();

        // Karartılmış zemin: tıklayınca vazgeçer.
        var backdrop = new Border { Background = new SolidColorBrush(Color.FromArgb(0x55, 0x3B, 0x2C, 0x25)) };
        backdrop.MouseLeftButtonDown += (_, e) => { CancelDial(); e.Handled = true; };
        backdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
        DialLayer.Children.Add(backdrop);

        var stack = new StackPanel();
        var title = new TextBlock { Text = "Ne kadar odaklanalım?", FontSize = 20, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var sub = new TextBlock { Text = "Tutamacı çevir; süre dolunca oturum biter.", FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        stack.Children.Add(title);
        stack.Children.Add(sub);
        stack.Children.Add(BuildDialFace());

        // Hızlı seçimler.
        dialChips = new UniformGrid { Columns = 5, Margin = new Thickness(-3, 6, -3, 0) };
        foreach (var m in new[] { 15, 25, 45, 60, 90 })
        {
            var b = new Button { Content = m % 60 == 0 ? $"{m / 60} sa" : $"{m} dk", Margin = new Thickness(3), Padding = new Thickness(0, 6, 0, 6), FontSize = 13, Tag = m };
            int pick = m;
            b.Click += (_, _) => SetDial(pick);
            dialChips.Children.Add(b);
        }
        stack.Children.Add(dialChips);

        // Düğmeler.
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        var cancel = new Button { Content = "Vazgeç", Style = (Style)FindResource("Btn"), Padding = new Thickness(0, 11, 0, 11) };
        cancel.Click += (_, _) => CancelDial();
        var startRow = new StackPanel { Orientation = Orientation.Horizontal };
        var play = new Path { Data = Geometry.Parse("M 3,2 L 13,8 L 3,14 Z"), Width = 12, Height = 12, Stretch = Stretch.Uniform, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        play.SetResourceReference(Shape.FillProperty, "OnAccent");
        play.SetResourceReference(Shape.StrokeProperty, "OnAccent");
        startRow.Children.Add(play);
        startRow.Children.Add(new TextBlock { Text = SelectedCourse != null ? "Başla" : "Kaydet", VerticalAlignment = VerticalAlignment.Center });
        var start = new Button { Content = startRow, Style = (Style)FindResource("Primary"), Padding = new Thickness(0, 11, 0, 11) };
        start.Click += (_, _) => StartFromDial();
        Grid.SetColumn(start, 2);
        row.Children.Add(cancel);
        row.Children.Add(start);
        stack.Children.Add(row);

        var card = new Border
        {
            Style = (Style)FindResource("Card"), Width = 360, Padding = new Thickness(26, 24, 26, 22),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = stack, Effect = (Effect)FindResource("LiftShadow"), CornerRadius = new CornerRadius(30)
        };
        card.MouseLeftButtonDown += (_, e) => e.Handled = true; // kartın içi kapatmasın
        var sc = new ScaleTransform(0.9, 0.9);
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = sc;
        var pop = new DoubleAnimation(0.9, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
        DialLayer.Children.Add(card);
    }

    FrameworkElement BuildDialFace()
    {
        double c = DialSize / 2;
        var host = new Grid { Width = DialSize, Height = DialSize, Margin = new Thickness(0, 16, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand };

        // İz halkası.
        // Ellipse kalınlığı sınırın içine çizer: çizginin ortası DialR'de olsun diye çap 2R + kalınlık.
        var track = new Ellipse { Width = DialR * 2 + DialStroke, Height = DialR * 2 + DialStroke, StrokeThickness = DialStroke };
        track.SetResourceReference(Shape.StrokeProperty, "Track");
        host.Children.Add(track);

        // Saat işaretleri: her 5 dk küçük nokta, her 15 dk daha büyük; 0/30/60/90 etiketli.
        var marks = new Canvas { Width = DialSize, Height = DialSize, IsHitTestVisible = false };
        for (int m = 0; m < DialMax; m += DialStep)
        {
            bool major = m % 15 == 0;
            double a = m / (double)DialMax * 2 * Math.PI, r = DialR - DialStroke / 2 - 10;
            double d = major ? 5 : 3;
            var dot = new Ellipse { Width = d, Height = d };
            dot.SetResourceReference(Shape.FillProperty, major ? "Muted" : "Faint");
            Canvas.SetLeft(dot, c + r * Math.Sin(a) - d / 2);
            Canvas.SetTop(dot, c - r * Math.Cos(a) - d / 2);
            marks.Children.Add(dot);
            if (m % 30 == 0)
            {
                var lab = new TextBlock { Text = m == 0 ? "0" : m.ToString(), FontSize = 11, FontWeight = FontWeights.ExtraBold };
                lab.SetResourceReference(TextBlock.ForegroundProperty, "Faint");
                lab.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double lr = r - 16;
                Canvas.SetLeft(lab, c + lr * Math.Sin(a) - lab.DesiredSize.Width / 2);
                Canvas.SetTop(lab, c - lr * Math.Cos(a) - lab.DesiredSize.Height / 2);
                marks.Children.Add(lab);
            }
        }
        host.Children.Add(marks);

        // Seçilen süre yayı.
        dialArc = new Path { StrokeThickness = DialStroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        dialArc.SetResourceReference(Shape.StrokeProperty, "Accent");
        host.Children.Add(new Canvas { Width = DialSize, Height = DialSize, IsHitTestVisible = false, Children = { dialArc } });

        // Ortadaki sayı.
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        dialNumberPop = new ScaleTransform();
        dialNumber = new TextBlock { FontSize = 56, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = dialNumberPop, Margin = new Thickness(0, -6, 0, -8) };
        dialNumber.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        dialUnit = new TextBlock { FontSize = 14, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center };
        dialUnit.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        center.Children.Add(dialNumber);
        center.Children.Add(dialUnit);
        host.Children.Add(center);

        // Tutamaç: beyaz, tema renginde kenarlı, gölgeli yuvarlak.
        dialKnob = new Grid { Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Effect = (Effect)FindResource("SoftShadow") };
        var knobFace = new Ellipse { StrokeThickness = 4 };
        knobFace.SetResourceReference(Shape.FillProperty, "Surface");
        knobFace.SetResourceReference(Shape.StrokeProperty, "Accent");
        var knobDot = new Ellipse { Width = 8, Height = 8 };
        knobDot.SetResourceReference(Shape.FillProperty, "Accent");
        dialKnob.Children.Add(knobFace);
        dialKnob.Children.Add(knobDot);
        host.Children.Add(dialKnob);

        // Sürükleme ve tekerlek.
        bool dragging = false;
        host.MouseLeftButtonDown += (_, e) => { dragging = true; host.CaptureMouse(); DialFromPoint(e.GetPosition(host)); e.Handled = true; };
        host.MouseMove += (_, e) => { if (dragging) DialFromPoint(e.GetPosition(host)); };
        host.MouseLeftButtonUp += (_, _) => { dragging = false; host.ReleaseMouseCapture(); };
        host.LostMouseCapture += (_, _) => dragging = false;
        host.MouseWheel += (_, e) => { SetDial(dialValue + (e.Delta > 0 ? DialStep : -DialStep)); e.Handled = true; };
        return host;
    }

    /// <summary>Fare konumundan süre: üstten saat yönünde açı → dakika (5'e yuvarlı). Tepeden geçerken uç değere yapışır.</summary>
    void DialFromPoint(Point p)
    {
        double c = DialSize / 2;
        double deg = Math.Atan2(p.X - c, c - p.Y) * 180 / Math.PI;
        if (deg < 0) deg += 360;
        int m = (int)Math.Round(deg / 360 * DialMax / DialStep) * DialStep;
        // Tepe (0 = 120) üzerinden atlamayı engelle: büyükten küçüğe sıçrarsa en büyükte, tersinde en küçükte kal.
        if (dialValue >= DialMax - 20 && m <= 20) m = DialMax;
        else if (dialValue <= 20 && m >= DialMax - 20) m = DialMin;
        if (m == 0) m = dialValue > DialMax / 2 ? DialMax : DialMin;
        SetDial(m);
    }

    void UpdateDial(bool pop)
    {
        if (dialArc == null || dialKnob == null || dialNumber == null || dialUnit == null) return;
        double c = DialSize / 2, frac = dialValue / (double)DialMax;
        double a = frac * 2 * Math.PI;
        var end = new Point(c + DialR * Math.Sin(a), c - DialR * Math.Cos(a));
        if (frac >= 0.999)
            dialArc.Data = new EllipseGeometry(new Point(c, c), DialR, DialR);
        else
        {
            var fig = new PathFigure { StartPoint = new Point(c, c - DialR), IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(DialR, DialR), 0, frac > 0.5, SweepDirection.Clockwise, true));
            dialArc.Data = new PathGeometry { Figures = { fig } };
        }
        dialKnob.Margin = new Thickness(end.X - 17, end.Y - 17, 0, 0);

        dialNumber.Text = dialValue.ToString();
        dialUnit.Text = dialValue >= 60 ? $"dakika · {dialValue / 60} sa{(dialValue % 60 > 0 ? $" {dialValue % 60} dk" : "")}" : "dakika";

        if (dialChips != null)
            foreach (Button b in dialChips.Children)
                b.Style = (Style)FindResource((int)b.Tag == dialValue ? "Primary" : "Btn");

        if (pop && dialNumberPop != null)
        {
            var bump = new DoubleAnimation(1.08, 1, TimeSpan.FromSeconds(0.18));
            dialNumberPop.BeginAnimation(ScaleTransform.ScaleXProperty, bump);
            dialNumberPop.BeginAnimation(ScaleTransform.ScaleYProperty, bump);
        }
    }
}
