using System.Windows;
using System.Windows.Media.Imaging;

namespace FlowState;

// Odak çemberindeki gezgin: oturum çalışırken yerinde yürür, duraklatılınca ya da oturum yokken durur.
// Kareler Assets/explorer/explorer_walk.png içinde (tools/build-explorer.ps1 üretir): kare 0 duruş, 1.. yürüyüş.
public partial class MainWindow
{
    const int ExplorerCell = 192;
    const double ExplorerFps = 8;
    BitmapSource[]? explorerFrames;
    int explorerShown = -1;

    BitmapSource[] ExplorerFrames()
    {
        if (explorerFrames != null) return explorerFrames;
        var strip = new BitmapImage();
        strip.BeginInit();
        strip.UriSource = new Uri("pack://application:,,,/Assets/explorer/explorer_walk.png");
        strip.CacheOption = BitmapCacheOption.OnLoad;
        strip.EndInit();
        strip.Freeze();
        var f = new BitmapSource[strip.PixelWidth / ExplorerCell];
        for (int i = 0; i < f.Length; i++)
        {
            var c = new CroppedBitmap(strip, new Int32Rect(i * ExplorerCell, 0, ExplorerCell, ExplorerCell));
            c.Freeze();
            f[i] = c;
        }
        return explorerFrames = f;
    }

    /// <summary>Her saat tikinde çağrılır; yalnızca kare değişince görseli günceller.</summary>
    void UpdateExplorer()
    {
        var frames = ExplorerFrames();
        bool walking = active && sw.IsRunning;
        int walkCount = frames.Length - 1;
        int frame = walking && walkCount > 0 ? 1 + (int)(sw.Elapsed.TotalSeconds * ExplorerFps) % walkCount : 0;
        if (frame == explorerShown) return;
        explorerShown = frame;
        RingWalker.Source = frames[frame];
    }
}
