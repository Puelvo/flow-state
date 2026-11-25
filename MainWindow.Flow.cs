using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace FlowState;

// Geri sayımın sonu: "Akışta Kal". Süre dolunca sayaç durmaz; sakin bir ses çalar ve iki seçenek çıkar.
//  - Akışta Kal: oturum kesilmeden kronometreye döner, artı süre aynı oturuma eklenir.
//  - Bitir ve Kutla: oturum hedef süreyle kaydedilir.
// Cevap gelmezse FlowRules.GraceSeconds sonra hedef süreyle kendiliğinden biter (masadan kalkan fazladan süre kazanmaz).
public partial class MainWindow
{
    /// <summary>Geri sayım doldu, seçim bekleniyor.</summary>
    bool TimeUp => active && countdownTarget > 0 && ActiveSeconds >= countdownTarget;

    string timeUpRun = "";                // sesi çalınmış oturum (bir kez çalar)
    Border? choiceCard;                   // tam pencerede altta duran kart
    StackPanel? widgetChoice;             // küçük widget'ta düğmelerin yerine

    /// <summary>Saniye tikinde: süre yeni dolduysa haber verir. Oturum burada bittiyse true.</summary>
    bool TickTimeUp(int s)
    {
        if (!TimeUp || !sw.IsRunning) return false;
        if (s - countdownTarget >= FlowRules.GraceSeconds)
        {
            FinishSession(timeUp: true);
            return true;
        }
        if (timeUpRun == activeRunId) return false;
        timeUpRun = activeRunId;
        Chime();
        FlashTaskbar();
        return false;
    }

    void StayInFlow()
    {
        if (!TimeUp) return;
        countdownTarget = 0;
        data.CountdownRunId = "";
        Store.Save(data);
        UpdateAll();
    }

    void BuildFlowChoice()
    {
        choiceCard = new Border
        {
            Style = (Style)FindResource("Card"), Padding = new Thickness(16, 10, 12, 10), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 34), Visibility = Visibility.Collapsed
        };
        // Tek satır: bahçeyi kapatmasın.
        var row = ChoiceButtons(14);
        row.Children.Insert(0, new TextBlock { Text = "Süre doldu", FontSize = 16, FontWeight = FontWeights.Black, Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 2) });
        choiceCard.Child = row;
        ChoiceLayer.Children.Add(choiceCard);

        widgetChoice = ChoiceButtons(17);
        widgetChoice.Visibility = Visibility.Collapsed;
        widgetChoice.Margin = new Thickness(0, 10, 0, 0);
        BottomRow.Children.Add(widgetChoice);
    }

    StackPanel ChoiceButtons(double font)
    {
        var stay = new Button { Content = "Akışta Kal", Style = (Style)FindResource("Primary"), FontSize = font, Padding = new Thickness(18, 9, 18, 9), Focusable = false };
        var finish = new Button { Content = "Bitir ve Kutla", Style = (Style)FindResource("Btn"), FontSize = font, Padding = new Thickness(18, 9, 18, 9), Margin = new Thickness(10, 0, 0, 0), Focusable = false };
        stay.Click += (_, _) => StayInFlow();
        finish.Click += (_, _) => FinishSession(timeUp: true);
        return new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { stay, finish } };
    }

    void UpdateFlowChoice()
    {
        if (choiceCard == null || widgetChoice == null) return;
        bool up = TimeUp;
        choiceCard.Visibility = up && !widgetMode ? Visibility.Visible : Visibility.Collapsed;
        widgetChoice.Visibility = up && widgetMode ? Visibility.Visible : Visibility.Collapsed;
        BtnRow.Visibility = !up ? Visibility.Visible : widgetMode ? Visibility.Collapsed : Visibility.Hidden;     // pencerede yeri korunur, sahne oynamaz
    }

    // ---------- sakin haber verme ----------

    static byte[]? chimeWav;

    /// <summary>Yumuşak iki notalı çan (kodla üretilir; sistem uyarı sesi gibi irkiltmez).</summary>
    static void Chime()
    {
        try { new SoundPlayer(new MemoryStream(chimeWav ??= BuildChime())).Play(); } catch { }
    }

    static byte[] BuildChime()
    {
        const int rate = 22050;
        int n = (int)(rate * 1.9);
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)rate, v = Note(t, 523.25) + Note(t - 0.28, 783.99);       // do, sol
            w.Write((short)(Math.Clamp(v, -1, 1) * 9000));
        }
        return ms.ToArray();

        static double Note(double t, double f) => t < 0 ? 0
            : Math.Min(1, t / 0.02) * Math.Exp(-t * 2.6) * (Math.Sin(2 * Math.PI * f * t) + 0.25 * Math.Sin(4 * Math.PI * f * t)) * 0.7;
    }

    /// <summary>Pencere arkadaysa görev çubuğu düğmesi sakin biçimde yanar; öne gelmeye zorlanmaz.</summary>
    void FlashTaskbar()
    {
        try
        {
            if (IsActive) return;
            var f = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Hwnd = new WindowInteropHelper(this).Handle, Flags = 2 | 12, Count = 3 };
            FlashWindowEx(ref f);
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo { public uint Size; public IntPtr Hwnd; public uint Flags, Count, Timeout; }

    [DllImport("user32.dll")]
    static extern bool FlashWindowEx(ref FlashInfo info);
}
