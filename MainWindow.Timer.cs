using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FlowState;

// Çalış sayfasının küçük canlılıkları: durum rozeti, dersin renginde pastel çember yüzü,
// oturum sürerken "nefes alan" yüz ve yanıp sönen durum noktası, saat yıldızcıkları.
public partial class MainWindow
{
    // Durum rozeti: hazır (krem, yeşil nokta) / odakta (tema pasteli, tema noktası) / mola (sarı pastel).
    void UpdateStatusPill(Course? course)
    {
        string text; string bg, fg, dot;
        if (!active)
        {
            text = course == null ? "" : "Başlamaya hazır";
            (bg, fg, dot) = ("Panel2", "Muted", "Good");
        }
        else if (!sw.IsRunning)
        {
            text = "Kısa bir mola";
            (bg, fg, dot) = ("WarnSoft", "Text", "Warn");
        }
        else
        {
            text = "Odaklanıyorsun";
            (bg, fg, dot) = ("AccentSoft", "AccentDeep", "Accent");
        }
        TimerStatus.Text = text;
        StatusPill.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusPill.SetResourceReference(Border.BackgroundProperty, bg);
        TimerStatus.SetResourceReference(TextBlock.ForegroundProperty, fg);
        TimerStatusDot.SetResourceReference(Shape.FillProperty, dot);
    }

    // Güneş saatin yüzü artık vaktin güneşi/ayıdır (ApplySunPalette); ders rengi chip'lerde görünür.
    void UpdateRingFace(Course? course) { }

    bool breathing;

    // Oturum çalışırken çember yüzü yavaşça büyüyüp küçülür, durum noktası yumuşakça yanıp söner.
    void SetBreathing(bool on)
    {
        if (on == breathing) return;
        breathing = on;
        if (on)
        {
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var breathe = new DoubleAnimation(1, 0.94, TimeSpan.FromSeconds(2.4))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            RingFaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
            RingFaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);
            TimerStatusDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(1.2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }
        else
        {
            // Animasyonu kaldırınca değer yerel değere (1) döner.
            RingFaceScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            RingFaceScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            TimerStatusDot.BeginAnimation(OpacityProperty, null);
        }
    }

    // Saat yıldızcığı: tamamlanan saat altın dolgulu, süren saat tema renginde boş.
    static UIElement PipStar(bool filled)
    {
        var p = new Path
        {
            Data = (Geometry)Application.Current.FindResource("IconSparkle"),
            Width = 16, Height = 16, Stretch = Stretch.Uniform,
            StrokeThickness = filled ? 0 : 1.8, StrokeLineJoin = PenLineJoin.Round,
            Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1)
        };
        if (filled) p.SetResourceReference(Shape.FillProperty, "Coin");
        else p.SetResourceReference(Shape.StrokeProperty, "Accent");
        return p;
    }

    // Küçük bir "zıp": 0'dan biraz taşarak tam boya.
    static void PopIn(UIElement el)
    {
        if (el.RenderTransform is not ScaleTransform st || st.IsFrozen) return;
        var pop = new DoubleAnimation(0.2, 1, TimeSpan.FromSeconds(0.4))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }
}
