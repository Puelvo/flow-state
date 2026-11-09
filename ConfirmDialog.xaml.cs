using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace StudyAssistant;

/// <summary>Uygulamanın tasarım diline uyan onay / bilgi penceresi (MessageBox yerine).</summary>
public partial class ConfirmDialog : Window
{
    ConfirmDialog(string title, string message, string okText, string? cancelText, bool danger)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        OkBtn.Content = okText;

        var tone = (Color)ColorConverter.ConvertFromString(danger ? "#E58A8A" : "#7C9CFF");
        IconBg.Fill = new SolidColorBrush(Color.FromArgb(0x33, tone.R, tone.G, tone.B));
        IconText.Foreground = new SolidColorBrush(tone);
        IconText.Text = danger ? "!" : "i";
        OkBtn.Background = new SolidColorBrush(danger ? Color.FromRgb(0xE5, 0x8A, 0x8A) : tone);

        if (cancelText == null) CancelBtn.Visibility = Visibility.Collapsed;
        else CancelBtn.Content = cancelText;

        Loaded += (_, _) => (cancelText == null ? OkBtn : CancelBtn).Focus();
    }

    /// <summary>Onay sorusu. Kullanıcı onaylarsa true döner.</summary>
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
