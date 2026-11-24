using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FlowState;

/// <summary>
/// Açık / koyu tema renkleri. App.xaml'deki fırçalar renklerini buradan bağlar (Binding [Anahtar]); bağlı fırçalar dondurulmaz,
/// bu yüzden tema değişince hem XAML'deki hem kodla kurulmuş öğeler (Res) aynı anda güncellenir.
/// Vurgu rengi (Accent ve türevleri) kullanıcıya bağlıdır; onu MainWindow.ApplyTheme hesaplar.
/// </summary>
public sealed class AppTheme : INotifyPropertyChanged
{
    public static AppTheme Instance { get; } = new();

    static readonly Dictionary<string, (string Light, string Dark)> Table = new()
    {
        ["BgA"] = ("#FFF9F1", "#221E2C"), ["BgB"] = ("#FDEBDD", "#1A1722"), ["Bg"] = ("#FFF7EE", "#1E1B27"),
        ["Surface"] = ("#FFFFFF", "#353042"), ["Panel"] = ("#FFFFFF", "#353042"),
        ["Panel2"] = ("#FBF0E6", "#443D53"), ["Line"] = ("#F1E3D6", "#4D465C"), ["Track"] = ("#F4E7DB", "#494257"),
        ["Text"] = ("#3B2C25", "#F5EEE7"), ["Muted"] = ("#A08C80", "#ADA2B4"), ["Faint"] = ("#CBB9AC", "#766C82"),
        ["Danger"] = ("#EF5D6C", "#FF7683"), ["DangerSoft"] = ("#FDE5E7", "#4F2D38"),
        ["Good"] = ("#2FB67C", "#4FCE97"), ["GoodSoft"] = ("#DDF5EA", "#24443A"),
        ["Warn"] = ("#FFAA3B", "#FFB957"), ["WarnSoft"] = ("#FFF0D6", "#4B3B26"),
        ["Coin"] = ("#F5B731", "#F5B731"), ["CoinInk"] = ("#C98410", "#FFCB5E"),
        ["Glass"] = ("#B8FFFFFF", "#C8353042"),            // gökyüzünde duran yarı saydam haplar
        ["Chip"] = ("#EEFFFFFF", "#EE353042"),             // üst sekme düğmeleri
        ["WarmTop"] = ("#FFF6E4", "#40352E"),              // kutlama kartının sıcak üstü
        ["Shadow"] = ("#B07A55", "#000000"), ["Hover"] = ("#14502A10", "#18FFFFFF"), ["Press"] = ("#22502A10", "#28FFFFFF"),
    };

    public bool Dark { get; private set; }

    public Color this[string key]
    {
        get
        {
            var (light, dark) = Table[key];
            return (Color)ColorConverter.ConvertFromString(Dark ? dark : light);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Set(bool dark)
    {
        if (dark == Dark) return;
        Dark = dark;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(System.Windows.Data.Binding.IndexerName));
    }

    /// <summary>Windows'un başlık çubuğunu da temaya uydurur (desteklemeyen sürümde sessizce atlanır).</summary>
    public static void TitleBar(Window w)
    {
        try
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) return;
            int on = Instance.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(h, 20, ref on, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
