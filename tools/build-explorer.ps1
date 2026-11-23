# NOT: Yürüyüş şeridi artık tools/build-explorer-walk.py ile üretiliyor (bacaklar prosedürel çiziliyor).
#      Bu betik yalnızca kaynak görsellerden karakteri yeniden ayıklamak gerekirse kullanılır;
#      çıktısının duruş karesi src/explorer_idle.png, 3. karesi src/explorer_body.png olarak saklanır.
# Gezgin karakterinin yürüme şeridini üretir (odak çemberinde yürüyen karakter).
# Kaynak: Assetsexplorersrcexplorer.jpg (duruş karesi) ve explorer_walk2.png (yürüyüş) — 2 satır × 5 kutuluk sunum görselleri.
# Çıktı:  Assets\explorer\explorer_walk.png — 192x192 kareler yan yana; kare 0 = duruş, 1.. = yürüyüş.
# Çalıştır: powershell -ExecutionPolicy Bypass -File tools\build-explorer.ps1 [önizleme.png]
param([string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public class CutFrame { public Bitmap Image; public int Bottom, Height; public double HeadX; }

public static class Cutter
{
    // Zemin gibi mi: koyu lacivert zemin/ızgara ya da yeşil çim.
    static bool BgLike(int c)
    {
        int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255, mx = Math.Max(r, Math.Max(g, b));
        if (b >= r + 6 && mx >= 30 && mx < 125 && Math.Abs(r - g) < 14) return true;
        if (g > r + 12 && g > b + 12) return true;
        return false;
    }

    static bool NearBg(int c)
    {
        int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
        return Math.Abs(r - 29) < 16 && Math.Abs(g - 30) < 16 && Math.Abs(b - 43) < 18;
    }

    /// Kutudan karakteri ayıklar: kenardan başlayan zemin taşması silinir, en büyük parça kalır.
    public static CutFrame Cut(Bitmap src, Rectangle box)
    {
        int w = box.Width, h = box.Height;
        var px = new int[w * h];
        using (var crop = src.Clone(box, PixelFormat.Format32bppArgb))
        {
            var d = crop.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(d.Scan0, px, 0, w * h);
            crop.UnlockBits(d);
        }

        var outside = new bool[w * h];
        var q = new Queue<int>();
        for (int x = 0; x < w; x++) { q.Enqueue(x); q.Enqueue((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { q.Enqueue(y * w); q.Enqueue(y * w + w - 1); }
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            if (outside[i] || !BgLike(px[i])) continue;
            outside[i] = true;
            int x = i % w, y = i / w;
            if (x > 0) q.Enqueue(i - 1); if (x < w - 1) q.Enqueue(i + 1);
            if (y > 0) q.Enqueue(i - w); if (y < h - 1) q.Enqueue(i + w);
        }

        // Karakterin içinde kapalı kalmış zemin cepleri (kol-gövde arası) de zemindir.
        var mask = new bool[w * h];
        for (int i = 0; i < w * h; i++) mask[i] = !outside[i] && !NearBg(px[i]);

        // En büyük bağlı parça = karakter (çim kırıntıları ve lekeler atılır).
        var label = new int[w * h]; int best = 0, bestSize = 0, next = 0;
        for (int s = 0; s < w * h; s++)
        {
            if (!mask[s] || label[s] != 0) continue;
            next++; int size = 0; q.Enqueue(s); label[s] = next;
            while (q.Count > 0)
            {
                int i = q.Dequeue(); size++;
                int x = i % w, y = i / w;
                foreach (int j in new[] { x > 0 ? i - 1 : -1, x < w - 1 ? i + 1 : -1, y > 0 ? i - w : -1, y < h - 1 ? i + w : -1 })
                    if (j >= 0 && mask[j] && label[j] == 0) { label[j] = next; q.Enqueue(j); }
            }
            if (size > bestSize) { bestSize = size; best = next; }
        }

        int top = h, bottom = 0;
        for (int i = 0; i < w * h; i++)
        {
            if (label[i] != best || !mask[i]) { px[i] = 0; continue; }
            px[i] |= unchecked((int)0xFF000000);
            int y = i / w; if (y < top) top = y; if (y > bottom) bottom = y;
        }
        // Kafa hizası: üst %35'lik dilimin yatay ağırlık merkezi (şapka + yüz sabit kalsın).
        double sum = 0; int n = 0; int headEnd = top + (int)((bottom - top) * 0.35);
        for (int y = top; y <= headEnd; y++) for (int x = 0; x < w; x++) if (px[y * w + x] != 0) { sum += x; n++; }

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, bd.Scan0, w * h);
        bmp.UnlockBits(bd);
        return new CutFrame { Image = bmp, Bottom = bottom, Height = bottom - top + 1, HeadX = n > 0 ? sum / n : w / 2.0 };
    }

    /// Kareleri kafadan ortalayıp ayaklardan hizalayarak yan yana dizer. Her karenin kendi ölçeği vardır
    /// (farklı görsellerden gelen kareler aynı boya getirilebilsin diye).
    public static Bitmap Strip(CutFrame[] frames, double[] scales, int cell)
    {
        var strip = new Bitmap(cell * frames.Length, cell, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(strip))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            for (int i = 0; i < frames.Length; i++)
            {
                var f = frames[i]; double s = scales[i];
                g.SetClip(new Rectangle(i * cell, 0, cell, cell));
                float x = (float)(i * cell + cell / 2.0 - f.HeadX * s), y = (float)(cell - 4 - (f.Bottom + 1) * s);
                g.DrawImage(f.Image, x, y, (float)(f.Image.Width * s), (float)(f.Image.Height * s));
            }
        }
        return strip;
    }
}
'@

$dir = Join-Path $PSScriptRoot "..\Assets\explorer"
$CharHeight = 180   # 192'lik karede karakterin boyu

# --- Duruş karesi: ilk görseldeki (explorer.jpg, 2752x1536) ayakta duran "Frame 1" ---
$idleSrc = New-Object System.Drawing.Bitmap (Join-Path $dir "src\explorer.jpg")
$idle = [Cutter]::Cut($idleSrc, (New-Object System.Drawing.Rectangle 104, 271, 442, 455))

# --- Yürüyüş: ikinci görsel (explorer_walk2.png, 1920x1056), 2 satır × 5 kutu, hepsi yürüyüş karesi ---
# Kutular çerçeve çizgisiyle birlikte alınır; çizgi zemin renginde olduğu için ayıklanırken silinir.
$walkSrc = New-Object System.Drawing.Bitmap (Join-Path $dir "src\explorer_walk2.png")
$xs = 62, 424, 792, 1158, 1526
$rows = @(@(176, 334), @(595, 322))        # @(üst, yükseklik)
$walk = @()
foreach ($r in $rows) { foreach ($x in $xs) { $walk += [Cutter]::Cut($walkSrc, (New-Object System.Drawing.Rectangle $x, $r[0], 332, $r[1])) } }

# "Frame 1" atlanır: o karede dürbün çizilmemiş, döngüde dürbün bir an kaybolurdu.
$walk = $walk[1..($walk.Count - 1)]

# İki görselin ölçeği farklı: duruş kendi boyuna, yürüyüş en uzun yürüyüş karesine göre ölçeklenir.
$walkMax = ($walk | ForEach-Object { $_.Height } | Measure-Object -Maximum).Maximum
$frames = @($idle) + $walk
$scales = @($CharHeight / $idle.Height) + ($walk | ForEach-Object { ($CharHeight - 2) / $walkMax })
$strip = [Cutter]::Strip([CutFrame[]]$frames, [double[]]$scales, 192)
$strip.Save((Join-Path $dir "explorer_walk.png"), [System.Drawing.Imaging.ImageFormat]::Png)
"explorer: $($frames.Count) kare (1 duruş + $($walk.Count) yürüyüş), $($strip.Width)x$($strip.Height); yürüyüş boyları: $(($walk | ForEach-Object { $_.Height }) -join ',')"

if ($Preview) {
  $p = New-Object System.Drawing.Bitmap $strip.Width, ($strip.Height * 2)
  $g = [System.Drawing.Graphics]::FromImage($p)
  $g.Clear([System.Drawing.Color]::FromArgb(255, 18, 22, 40))
  $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 235, 235, 240))), 0, $strip.Height, $strip.Width, $strip.Height)
  $g.DrawImage($strip, 0, 0); $g.DrawImage($strip, 0, $strip.Height)
  $g.Dispose(); $p.Save($Preview); $p.Dispose()
}
$strip.Dispose(); $idleSrc.Dispose(); $walkSrc.Dispose()
