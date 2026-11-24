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

    public bool Dark { get; private set; }

    public Color this[string key]
    {
        get
        {
            var (light, dark) = ThemeTable.Colors[key];
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
