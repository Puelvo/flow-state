using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlowState;

// "Sessiz Bakış": masaüstü widget'ının ince şerit hâli. Yalnızca patika, jeep ve süre; hayvan, kuş, süs yok.
// Düğmeler yalnızca fare üstündeyken görünür. Sahneyi ortak piksel çizici (PixelScene, Quiet) çizer.
public partial class MainWindow
{
    const double StripW = 480, StripH = 84;
    bool stripMode;
    Rect stripBounds = Rect.Empty;
    PixelScene? stripScene;
    WriteableBitmap? stripBmp;
    DispatcherTimer? stripTimer;
    readonly System.Diagnostics.Stopwatch stripClock = new();
    Border? stripTools, stripInfo;
    StackPanel? stripChoice;
    TextBlock? stripTime, stripLabel;
    Button? stripToggle, stripPin;
    string stripNote = "";
    DateTime stripNoteUntil;

    void StripToggle_Click(object sender, RoutedEventArgs e) => SetStrip(!stripMode, remember: true);

    /// <summary>Widget'ı şerit ya da güneşli kare görünümüne geçirir.</summary>
    void SetStrip(bool on, bool remember)
    {
        if (on == stripMode || (on && !widgetMode)) return;
        if (on) { BuildStrip(); widgetBounds = new Rect(Left, Top, Width, Height); }
        else stripBounds = new Rect(Left, Top, Width, Height);
        stripMode = on;
        if (remember) { data.WidgetStrip = on; Store.Save(data); }

        StripHost.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TimerView.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        WidgetBar.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Width = on ? StripW : WidgetSize;
        Height = on ? StripH : WidgetSize;
        if (on && stripBounds.IsEmpty)
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - StripW - 20;
            Top = wa.Bottom - StripH - 20;
        }
        else if (on) { Left = stripBounds.X; Top = stripBounds.Y; }
        else if (!widgetBounds.IsEmpty) { Left = widgetBounds.X; Top = widgetBounds.Y; }

        stripClock.Reset();
        if (on) { stripTimer!.Start(); TickStrip(); } else stripTimer!.Stop();
        ApplyPinVisual();
        UpdateAll();
    }

    void BuildStrip()
    {
        if (stripScene != null) return;
        stripScene = new PixelScene(_ => StripSprite()) { Z = 2, CloudZ = 1, ShowSun = false, Quiet = true, PathAt = 0.1 };
        int w = (int)StripW, h = (int)StripH;
        stripScene.Layout(w, h, 36, w / 2, 20);
        stripBmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var img = new Image { Source = stripBmp, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        StripHost.Children.Add(img);

        // Sağda süre ve ders; süre dolunca yerine iki küçük düğme gelir.
        stripTime = new TextBlock { FontSize = 25, FontWeight = FontWeights.Black, Foreground = Res("Text"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -3, 0, -3) };
        System.Windows.Documents.Typography.SetNumeralAlignment(stripTime, FontNumeralAlignment.Tabular);
        stripLabel = new TextBlock { FontSize = 12, FontWeight = FontWeights.ExtraBold, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 };
        stripInfo = new Border
        {
            Background = Res("Chip"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 12, 5), Margin = new Thickness(0, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            Child = new StackPanel { Children = { stripTime, stripLabel } }
        };
        StripHost.Children.Add(stripInfo);
        stripChoice = ChoiceButtons(13);
        stripChoice.HorizontalAlignment = HorizontalAlignment.Right;
        stripChoice.VerticalAlignment = VerticalAlignment.Center;
        stripChoice.Margin = new Thickness(0, 0, 10, 0);
        stripChoice.Visibility = Visibility.Collapsed;
        StripHost.Children.Add(stripChoice);

        // Solda araçlar: yalnızca fare şeridin üstündeyken.
        Button Tool(object content, string tip, Action click)
        {
            var b = new Button { Content = content, Style = (Style)FindResource("Ghost"), Padding = new Thickness(7), ToolTip = tip, Focusable = false, Foreground = Res("Text") };
            b.Click += (_, _) => click();
            return b;
        }
        FrameworkElement Icon(string key) => IconPath(IconRes(key), 14);
        stripToggle = Tool("", "Başlat / duraklat", ToggleTimer);
        stripPin = Tool(Icon("IconPin"), "", () => WidgetPin_Click(this, new RoutedEventArgs()));
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(stripToggle);
        tools.Children.Add(Tool(Icon("IconWidget"), "Güneşli widget'a dön", () => SetStrip(false, remember: true)));
        tools.Children.Add(stripPin);
        tools.Children.Add(Tool(Icon("IconExpand"), "Tam görünüme dön (Esc)", () => SetWidgetMode(false)));
        tools.Children.Add(Tool(Icon("IconClose"), "Kapat", Close));
        stripTools = new Border
        {
            Background = Res("Chip"), CornerRadius = new CornerRadius(14), Padding = new Thickness(3), Margin = new Thickness(10, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = tools, Visibility = Visibility.Collapsed
        };
        StripHost.Children.Add(stripTools);
        StripHost.MouseEnter += (_, _) => stripTools.Visibility = Visibility.Visible;
        StripHost.MouseLeave += (_, _) => stripTools.Visibility = Visibility.Collapsed;

        stripTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        stripTimer.Tick += (_, _) => TickStrip();
    }

    /// <summary>Şeritte yalnızca gezgin kullanılır; kareleri kaynaktan okunur.</summary>
    PixImg StripSprite()
    {
        var src = new FormatConvertedBitmap(new BitmapImage(new Uri("pack://application:,,,/Assets/explorer/explorer_walk.png")), PixelFormats.Bgra32, null, 0);
        var px = new uint[src.PixelWidth * src.PixelHeight];
        src.CopyPixels(px, src.PixelWidth * 4, 0);
        for (int i = 0; i < px.Length; i++) if (px[i] >> 24 == 0) px[i] = 0;
        return new PixImg(px, src.PixelWidth, src.PixelHeight);
    }

    void TickStrip()
    {
        if (!stripMode || stripScene is not { } s || stripBmp == null) return;
        double dt = stripClock.IsRunning ? Math.Min(0.1, stripClock.Elapsed.TotalSeconds) : 0;
        stripClock.Restart();
        s.Palette = DayCycle.At(DateTime.Now, Theme);
        s.Session = active;
        s.Running = active && sw.IsRunning;
        s.Update(dt);
        s.Render();
        stripBmp.WritePixels(new Int32Rect(0, 0, s.W, s.H), s.Buf, s.W * 4, 0);
    }

    /// <summary>Saniyede bir ve durum değişince: süre, ders, düğmeler.</summary>
    void UpdateStrip()
    {
        if (!stripMode || stripTime == null) return;
        bool up = TimeUp;
        stripInfo!.Visibility = up ? Visibility.Collapsed : Visibility.Visible;
        stripChoice!.Visibility = up ? Visibility.Visible : Visibility.Collapsed;
        stripTime.Text = ElapsedText.Text;
        stripTime.Opacity = !active ? 0.45 : sw.IsRunning ? 1 : 0.6;
        bool note = DateTime.Now < stripNoteUntil;
        stripLabel!.Text = note ? stripNote : active && !sw.IsRunning ? $"{TimerCourseName.Text} · mola" : TimerCourseName.Text;
        stripLabel.Foreground = Res(note ? "CoinInk" : "Muted");
        stripToggle!.Content = IconPath(active && sw.IsRunning ? Geometry.Parse("M8,5 L8,19 M16,5 L16,19") : Geometry.Parse("M7,4 L19,12 L7,20 Z"), 14);
        stripToggle.IsEnabled = !up && StartBtn.IsEnabled;
        stripPin!.Opacity = widgetOnTop ? 1 : 0.45;
        stripPin.ToolTip = PinBtn.ToolTip;
    }

    /// <summary>Şeritte ödül kartı açılmaz (sığmaz); kazanç birkaç saniye ders adının yerinde görünür.</summary>
    void StripReward(int coins, Special? special)
    {
        stripNote = special != null ? "Sürpriz dost geldi!" : coins > 0 ? $"+{coins} jeton" : "Oturum kaydedildi";
        stripNoteUntil = DateTime.Now.AddSeconds(6);
        UpdateStrip();
    }
}
