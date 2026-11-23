using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FlowState;

/// <summary>Uygulamanın tasarım diline uyan onay / bilgi penceresi (MessageBox yerine).</summary>
public partial class ConfirmDialog : Window
{
    ConfirmDialog(string title, string message, string okText, string? cancelText, bool danger)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkBtn.Content = okText;

        if (danger)
        {
            // Tehlikeli işlem: pembe rozet, dolu kırmızı onay düğmesi
            IconBg.Fill = (Brush)FindResource("DangerSoft");
            IconPath.Stroke = (Brush)FindResource("Danger");
            OkBtn.Background = (Brush)FindResource("Danger");
            OkBtn.Foreground = Brushes.White;
        }
        else
        {
            // Bilgi / onay: tema renginde rozet ve Primary düğme
            IconBg.SetResourceReference(Shape.FillProperty, "AccentSoft");
            IconPath.SetResourceReference(Shape.StrokeProperty, "Accent");
            IconPath.Data = Geometry.Parse("M12,6.1 L12,6.2 M12,10.5 L12,18");
        }

        // Açılışta küçük bir "pıt": kart hafifçe büyüyerek belirir
        Loaded += (_, _) =>
        {
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
            var a = new DoubleAnimation(0.9, 1, TimeSpan.FromSeconds(0.3)) { EasingFunction = ease };
            PopScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            PopScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            CardBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.2)));
        };

        if (cancelText == null) CancelBtn.Visibility = Visibility.Collapsed;
        else CancelBtn.Content = cancelText;

        Loaded += (_, _) => (cancelText == null ? OkBtn : CancelBtn).Focus();
    }

    /// <summary>Onay sorusu. Kullanıcı onaylarsa true döner.</summary>
    /// <summary>Ekran görüntüsü modu için: diyaloğu açmadan oluşturur.</summary>
    internal static ConfirmDialog Preview(Window owner, string title, string message, string okText, bool danger)
        => new(title, message, okText, "Vazgeç", danger) { Owner = owner };

    public static bool Ask(Window owner, string title, string message, string okText = "Sil", bool danger = true)
    {
        var d = new ConfirmDialog(title, message, okText, "Vazgeç", danger) { Owner = owner };
        return d.ShowDialog() == true;
    }

    /// <summary>Tek düğmeli bilgi penceresi.</summary>
    public static void Info(Window owner, string title, string message)
    {
        new ConfirmDialog(title, message, "Tamam", null, false) { Owner = owner }.ShowDialog();
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); } catch (InvalidOperationException) { }
    }
}
