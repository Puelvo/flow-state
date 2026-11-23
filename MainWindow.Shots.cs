using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FlowState;

// Ekran görüntüsü modu: FLOWSTATE_SHOTS=<klasör> ile açılınca örnek veriyle her sayfanın PNG'sini kaydeder ve kapanır.
// Tasarımı Windows'u olmayan ortamlardan (CI) görmek için. Örnek veri diske yazılmaz; senkron başlatılmaz.
public partial class MainWindow
{
    static readonly string? ShotsDir = Environment.GetEnvironmentVariable("FLOWSTATE_SHOTS") is { Length: > 0 } d ? d : null;
    static bool ShotMode => ShotsDir != null;

    /// <summary>Gerçek verinin yerine örnek dersler, ~4 aylık oturumlar ve birkaç hayvan koyar.</summary>
    void SeedSample()
    {
        var rnd = new Random(42);
        data.Courses.Clear(); data.Sessions.Clear(); data.OwnedAnimals.Clear(); data.Tombstones.Clear();
        data.Active = null; data.Live = new LiveState();
        data.EnsureGeneral();
        var courses = new[]
        {
            new Course { Name = "Matematik", Color = "#FF7A6B" },
            new Course { Name = "Fizik", Color = "#4CB8F5" },
            new Course { Name = "Kimya", Color = "#3CCB8E" },
            new Course { Name = "İngilizce", Color = "#A78BFA" },
            new Course { Name = "Tarih", Color = "#FFA94D" },
        };
        data.Courses.AddRange(courses);

        for (int back = 125; back >= 1; back--)
        {
            var day = Today.AddDays(-back);
            double chance = back < 30 ? 0.85 : back < 70 ? 0.6 : 0.4;
            if (back <= 9) chance = 1; // güncel seri
            if (rnd.NextDouble() > chance) continue;
            int n = 1 + rnd.Next(3);
            for (int k = 0; k < n; k++)
            {
                var c = courses[rnd.Next(courses.Length)];
                var start = day.AddHours(9 + rnd.Next(12)).AddMinutes(rnd.Next(60));
                data.Sessions.Add(new Session { CourseId = c.Id, Start = start, Seconds = 1200 + rnd.Next(5400) });
            }
        }
        data.Sessions.Add(new Session { CourseId = courses[0].Id, Start = Today.AddHours(9.5), Seconds = 5100 });
        data.Sessions.Add(new Session { CourseId = courses[1].Id, Start = Today.AddHours(11.2), Seconds = 2700 });

        // Hayvanlar geçmişte alınmış olsun: uzun seanslar nadir dost (altın / yavru) getirebilsin.
        foreach (var (id, daysAgo) in new[] { ("zebra", 124), ("giraffe", 90), ("lion", 40) })
            data.OwnedAnimals.Add(new OwnedAnimal { AnimalId = id, Price = ZooCatalog.Find(id)!.Price, BoughtAt = DateTime.UtcNow.AddDays(-daysAgo) });
        // Market'te hem alınabilen (gergedan, 80) hem kilitli (fil, 120) kart görünsün: bakiye ~100.
        // Oturumlara dokunmadan aslanın "ödenen" fiyatı ayarlanır (yalnızca örnek veri).
        var lion = data.OwnedAnimals.Last();
        lion.Price = Math.Max(lion.Price, lion.Price + Zoo.Balance(data) - 100);
    }

    async Task TakeShots()
    {
        Directory.CreateDirectory(ShotsDir!);
        try { await TakeShotsCore(); }
        catch (Exception ex) { File.WriteAllText(Path.Combine(ShotsDir!, "hata.txt"), ex.ToString()); }
        finally { Application.Current.Shutdown(); }
    }

    async Task TakeShotsCore()
    {
        Width = 1180; Height = 860;
        Left = 0; Top = 0;
        RefreshCourses(); UpdateAll();
        await Task.Delay(1500);

        if (CourseList.Items.Count > 1) CourseList.SelectedIndex = 1;

        // Güneş saat: dört vakit (sabit tema) ve otomatik.
        foreach (var (theme, name) in new[] { ("day", "01-calis-gunduz"), ("dawn", "01b-calis-safak"), ("sunset", "01c-calis-gunbatimi"), ("night", "01d-calis-gece") })
        {
            data.GardenTheme = theme;
            ApplySunPalette(force: true);
            await Shot(name, 2200);
        }
        data.GardenTheme = "day";
        ApplySunPalette(force: true);

        // Çalışırken: 2 sa 25 dk'lık süren bir oturum (yıldızlar ve halka dolu, gezgin yürüyor, hayvanlar sakin).
        ApplyLiveLocal(new LiveState
        {
            Active = true, RunId = "shot", CourseId = SelectedCourse?.Id ?? Course.GeneralId,
            Start = DateTime.Now.AddMinutes(-150), Accumulated = 2 * 3600 + 25 * 60, RunningSince = DateTime.UtcNow
        });
        UpdateAll();
        await Shot("02-calis-calisirken", 3000);
        data.GardenTheme = "night";
        ApplySunPalette(force: true);
        await Shot("02b-calis-calisirken-gece", 1500);
        data.GardenTheme = "day";
        ApplySunPalette(force: true);

        NavStats.IsChecked = true;
        await Shot("03-istatistik", 1200, StatsView);
        NavHistory.IsChecked = true;
        await Shot("04-gecmis", 1000, HistoryPanel);
        NavZoo.IsChecked = true;
        await Shot("05-dostlar", 1500, ZooView);
        GoZoo(ZooScreen.Shop);
        await Shot("06-market", 1500, ZooView);
        zooBoughtId = "rhino";
        GoZoo(ZooScreen.Bought);
        await Shot("07-kutlama", 1600);
        zooBoughtId = null;
        GoZoo(ZooScreen.Garden);
        NavSocial.IsChecked = true;
        await Shot("08-sosyal", 1200);
        NavSettings.IsChecked = true;
        await Shot("09-ayarlar", 1000, SettingsView);

        var dlg = ConfirmDialog.Preview(this, "Oturumu sil", "Bu çalışma kaydı silinecek. Bu işlem geri alınamaz.", "Sil", true);
        dlg.Show();
        await Task.Delay(900);
        SaveFull((FrameworkElement)dlg.Content, Background, "10-onay");
        dlg.Close();

        NavTimer.IsChecked = true;
        SetWidgetMode(true);
        await Shot("11-widget", 1600);
        SetWidgetMode(false);

        // Jeton ödülü: 1,5 saatlik bir oturumun bitişi (kart açılmış, jetonlar havada / sekmeye varmış).
        Commit();
        explorerVisitPending = true;
        UpdateAll();
        await Task.Delay(800);
        // Örnek veride nadir dost getirmiş bir seans varsa ödül kartında "Sürpriz!" de görünsün.
        var sp = Specials().LastOrDefault();
        var rs = sp != null ? data.Sessions.First(x => x.Id == sp.SessionId)
            : new Session { Id = "shot-reward", CourseId = SelectedCourse?.Id ?? Course.GeneralId, Start = DateTime.Now.AddMinutes(-95), Seconds = 5400 };
        ShowCoinReward(rs, false, sp);
        await Shot("12-jeton", 650);
        await Shot("13-jeton-ucus", 900);
        // Oturum bitince hayvanlar gezginin etrafına toplanır.
        await Shot("13b-kutlama-bahcede", 4500);
        await Task.Delay(1500);

        // Geri sayım: boşta (süre seçici) ve çalışırken (45 dk hedefin 17 dk'sı geçmiş).
        ModeCountdown.IsChecked = true;
        data.CountdownMinutes = 45;
        UpdateAll();
        OpenDial();
        SetDial(45);
        await Shot("14-geri-sayim-kadran", 900);
        CloseDial();
        await Shot("14-geri-sayim", 600);
        data.CountdownRunId = "shot-cd"; data.CountdownSeconds = 45 * 60;
        ApplyLiveLocal(new LiveState
        {
            Active = true, RunId = "shot-cd", CourseId = SelectedCourse?.Id ?? Course.GeneralId,
            Start = DateTime.Now.AddMinutes(-17), Accumulated = 17 * 60 + 12, RunningSince = DateTime.UtcNow
        });
        UpdateAll();
        await Shot("15-geri-sayim-calisirken", 1400);
    }

    /// <summary>Pencerenin görüntüsünü, istenirse bir kaydırma alanının tam boyunu da kaydeder.</summary>
    async Task Shot(string name, int waitMs, FrameworkElement? full = null)
    {
        await Task.Delay(waitMs);
        UpdateLayout();
        Save(this, (FrameworkElement)Content, Background, name);
        if (full == null) return;
        var inner = full is ScrollViewer sv ? sv.Content as FrameworkElement ?? full : full;
        SaveFull(inner, Background, name + "-tam");
    }

    /// <summary>Öğeyi, taşan gölgeleri dahil tüm çizim sınırıyla kaydeder (kaydırma alanının tam boyu için).</summary>
    static void SaveFull(FrameworkElement el, Brush bg, string name)
    {
        const double scale = 1.5, pad = 24;
        var b = VisualTreeHelper.GetDescendantBounds(el);
        if (b.IsEmpty || b.Width < 2 || b.Height < 2) return;
        double cw = Math.Ceiling(b.Width + pad * 2), ch = Math.Ceiling(b.Height + pad * 2);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(bg ?? Brushes.White, null, new Rect(0, 0, cw, ch));
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.Fill }, null, new Rect(pad, pad, b.Width, b.Height));
        }
        Write(dv, cw, ch, scale, name);
    }

    static void Write(DrawingVisual dv, double cw, double ch, double scale, string name)
    {
        var rtb = new RenderTargetBitmap((int)(cw * scale), (int)(ch * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(ShotsDir!, name + ".png"));
        enc.Save(fs);
    }

    static void Save(Window w, FrameworkElement el, Brush bg, string name, double pad = 0)
    {
        const double scale = 1.5;
        double cw = Math.Ceiling(el.ActualWidth + pad * 2), ch = Math.Ceiling(el.ActualHeight + pad * 2);
        if (cw < 2 || ch < 2) return;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(bg ?? Brushes.White, null, new Rect(0, 0, cw, ch));
            var vb = new VisualBrush(el)
            {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(-pad, -pad, cw, ch),
                ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, cw, ch)
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, cw, ch));
        }
        var rtb = new RenderTargetBitmap((int)(cw * scale), (int)(ch * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(ShotsDir!, name + ".png"));
        enc.Save(fs);
    }
}
