using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowState;

// Güneş saatin yüzü: sahneyle aynı dilde piksel bir güneş (gece ay) ve çevresinde 60 ışın.
// Kronometrede her ışın bir dakikadır, her saat bir tam tur; geri sayımda 60 ışın hedefi paylaşır.
// Her beşinci ışın iri bir üçgen, aradakiler küçük kare noktadır.
// Izgara DialN × DialN hücre; kenarlar her ölçekte temiz kalsın diye hücreler DialUp kat büyütülüp öyle çizilir.
public partial class MainWindow
{
    const int DialN = PixelArt.DialN, DialUp = 4;

    string raysKey = "", discKey = "";

    /// <summary>Işınları oturumun ilerlemesine göre günceller (yalnızca bir piksel değişince yeniden çizilir).</summary>
    void UpdateRing()
    {
        double t = active ? sw.Elapsed.TotalSeconds : 0;
        double frac = countdownTarget > 0 ? Math.Min(t / countdownTarget, 1) : t % 3600 / 3600;
        int steps = (int)(frac * 60 * 10);                         // ışın başına 10 adım
        var key = $"{active}|{steps}|{sunPalette.Ray:X}|{sunPalette.Moon:F2}";
        if (key == raysKey) return;
        raysKey = key;
        RingRays.Source = Upscale(PixelArt.SunRays(sunPalette, active, steps / 10.0).Px);
    }

    /// <summary>Güneş/ay diskini vaktin renkleriyle çizer.</summary>
    void UpdateDisc()
    {
        var key = $"{sunPalette.DiscA:X}{sunPalette.DiscB:X}{sunPalette.Ray:X}{sunPalette.Moon:F2}";
        if (key == discKey) return;
        discKey = key;
        RingFace.Source = Upscale(PixelArt.SunDisc(sunPalette).Px);
    }

    static BitmapSource Upscale(uint[] px)
    {
        const int W = DialN * DialUp;
        var big = new uint[W * W];
        for (int y = 0; y < W; y++)
        for (int x = 0; x < W; x++) big[y * W + x] = px[y / DialUp * DialN + x / DialUp];
        var bmp = BitmapSource.Create(W, W, 96, 96, PixelFormats.Bgra32, null, big, W * 4);
        bmp.Freeze();
        return bmp;
    }
}
