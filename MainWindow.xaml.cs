using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FlowState;

public partial class MainWindow : Window
{
    static readonly CultureInfo Tr = new("tr-TR");
    static readonly string[] Palette =
        { "#4F8CFF", "#F2994A", "#27AE60", "#BB6BD9", "#EB5757", "#2D9CDB", "#F2C94C", "#56CCF2" };

    readonly AppData data = Store.Load();
    readonly ClockTimer sw = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    bool active;                    // oturum var (çalışıyor ya da duraklatılmış)
    string activeCourseId = "";
    string activeRunId = "";
    DateTime activeStart;
    int lastCheckpoint;
    int lastTickSecond = -1;
    string period = "today";

    bool ready;
    TextBlock[] statCards = null!, streakCards = null!, streakNotes = null!;

    static DateTime Today => DateTime.Today;

    public MainWindow()
    {
        InitializeComponent();
        RingHost.LayoutTransform = NormalRingScale;
        BuildSunScene();
        data.CountdownMinutes = Math.Clamp(data.CountdownMinutes, DialMin, DialMax);
        if (data.Countdown) ModeCountdown.IsChecked = true;

        // Önceki çalışma çökme/kapanma ile yarım kaldıysa kaydet.
        if (data.Active is { } a)
        {
            // Paylaşılan seans hâlâ sürüyorsa (başka cihaz bitirmedi) kaydetme; zamanlayıcı aşağıda canlı durumdan sürer.
            if (a.Seconds >= 5 && !data.Live.Active)
                data.Sessions.Add(new Session { CourseId = a.CourseId, Start = a.Start, Seconds = a.Seconds });
            data.Active = null;
            Store.Save(data);
        }

        statCards = BuildCards(StatsCards, new StatCardSpec[]
        {
            new("Bugün", "IconTimer", "Accent", "AccentSoft"),
            new("Bu hafta", "IconChart", "Good", "GoodSoft"),
            new("Bu ay", "IconPaw", "Warn", "WarnSoft"),
            new("Toplam", "IconSparkle", "Coin", "WarnSoft", Filled: true),
        }, out _);
        streakCards = BuildCards(StreakCards, new StatCardSpec[]
        {
            new("Güncel seri", "IconFlame", "Danger", "DangerSoft", Filled: true),
            new("En uzun seri", "IconHeart", "Danger", "DangerSoft"),
            new("Çalışılan gün", "IconHistory", "Accent", "AccentSoft"),
        }, out streakNotes);

        BuildStars();
        ApplyTheme();
        ready = true;
        RefreshCourses();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        UpdateAll();

        sync = new SyncService(data, new DispatcherSynchronizationContext(Dispatcher), new WindowsSecretCodec(),
            onDataChanged: () => { Store.Save(data); RefreshCourses(); UpdateAll(); },
            courseInUse: id => active && activeCourseId == id,
            onRemoteLive: OnRemoteLive);
        sync.StatusChanged += RefreshSyncUi;
        RefreshSyncUi();
        InitSocialBadge();
        if (data.Live.Active && data.Courses.Any(c => c.Id == data.Live.CourseId)) { ApplyLiveLocal(data.Live); UpdateAll(); }
    }

    // ---------- senkronizasyon ----------

    SyncService? sync;

    void RefreshSyncUi()
    {
        if (sync == null) return;
        SyncStatus.Text = sync.SignedIn ? $"{sync.Email} · {sync.Status}" : sync.Status;
        // Durum hapı: girişliyse yeşil, değilse sade
        SyncPill.Background = Res(sync.SignedIn ? "GoodSoft" : "Panel2");
        SyncDot.Fill = Res(sync.SignedIn ? "Good" : "Faint");
        SyncLoginPanel.Visibility = sync.SignedIn ? Visibility.Collapsed : Visibility.Visible;
        SyncAccountPanel.Visibility = sync.SignedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    async void SyncAuth(bool signUp)
    {
        if (sync == null) return;
        var email = SyncEmail.Text.Trim();
        var pw = SyncPassword.Password;
        if (email.Length == 0 || pw.Length == 0) { ShowSyncMsg("E-posta ve şifre gir."); return; }
        SyncLoginBtn.IsEnabled = SyncSignupBtn.IsEnabled = false;
        ShowSyncMsg(null);
        var err = signUp ? await sync.SignUpAsync(email, pw) : await sync.SignInAsync(email, pw);
        SyncLoginBtn.IsEnabled = SyncSignupBtn.IsEnabled = true;
        if (err != null) ShowSyncMsg(err); else SyncPassword.Clear();
        RefreshSyncUi();
    }

    void ShowSyncMsg(string? m)
    {
        SyncMsg.Text = m ?? "";
        SyncMsg.Visibility = string.IsNullOrEmpty(m) ? Visibility.Collapsed : Visibility.Visible;
    }

    void SyncLogin_Click(object sender, RoutedEventArgs e) => SyncAuth(false);
    void SyncSignup_Click(object sender, RoutedEventArgs e) => SyncAuth(true);
    void SyncPassword_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) SyncAuth(false); }
    void SyncNow_Click(object sender, RoutedEventArgs e) => _ = sync?.SyncAsync();

    void SyncLogout_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDialog.Ask(this, "Çıkış yap",
                "Bu cihazdaki senkron kapatılır. Verilerin bu cihazda ve bulutta kalır.", "Çıkış yap")) return;
        sync?.SignOut();
        RefreshSyncUi();
    }

    // ---------- arka plan ----------

    readonly List<(FrameworkElement e, double fx, double fy)> stars = new();

    static readonly string[] Confetti = { "#FFB4A2", "#FFD58A", "#A8E6CF", "#B5C7FF", "#F7B2D9", "#C9B6FF" };

    void BuildStars()
    {
        var rnd = new Random(7);
        var sparkle = (Geometry)FindResource("IconSparkle");
        for (int i = 0; i < 26; i++)
        {
            var fill = ParseBrush(Confetti[rnd.Next(Confetti.Length)]);
            FrameworkElement deco;
            if (rnd.NextDouble() < 0.35)
            {
                double size = 10 + rnd.NextDouble() * 8;
                deco = new System.Windows.Shapes.Path { Data = sparkle, Fill = fill, Width = size, Height = size, Stretch = Stretch.Uniform };
            }
            else
            {
                double size = 6 + rnd.NextDouble() * 9;
                deco = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = fill };
            }
            deco.Opacity = 0.45 + rnd.NextDouble() * 0.4;
            var tt = new TranslateTransform();
            var rot = new RotateTransform();
            deco.RenderTransformOrigin = new Point(0.5, 0.5);
            deco.RenderTransform = new TransformGroup { Children = { rot, tt } };
            var ease = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(-10 - rnd.NextDouble() * 12, 10 + rnd.NextDouble() * 12,
                TimeSpan.FromSeconds(4 + rnd.NextDouble() * 5))
            {
                AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(-rnd.NextDouble() * 6), EasingFunction = ease
            });
            if (deco is System.Windows.Shapes.Path)
                rot.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(-18, 18,
                    TimeSpan.FromSeconds(3 + rnd.NextDouble() * 3))
                { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = ease });
            stars.Add((deco, rnd.NextDouble(), rnd.NextDouble()));
            StarCanvas.Children.Add(deco);
        }
        StarCanvas.SizeChanged += (_, _) => PlaceStars();
        Loaded += (_, _) => PlaceStars();
    }

    void PlaceStars()
    {
        double w = ActualWidth, h = ActualHeight;
        foreach (var (e, fx, fy) in stars)
        {
            Canvas.SetLeft(e, fx * w);
            Canvas.SetTop(e, fy * h);
        }
    }

    // ---------- yardımcılar ----------

    Brush Res(string key) => (Brush)FindResource(key);
    static Brush ParseBrush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    Course? SelectedCourse => (CourseList.SelectedItem as ListBoxItem)?.Tag as Course;
    int ActiveSeconds => active ? (int)sw.Elapsed.TotalSeconds : 0;

    static string Fmt(double sec)
    {
        int s = (int)sec, h = s / 3600, m = s % 3600 / 60;
        if (h > 0) return $"{h} sa {m:00} dk";
        if (m > 0) return $"{m} dk";
        return $"{s} sn";
    }

    static DateTime WeekStart()
    {
        int diff = ((int)Today.DayOfWeek + 6) % 7; // Pazartesi başlangıç
        return Today.AddDays(-diff);
    }

    DateTime PeriodFrom(string p) => p switch
    {
        "today" => Today,
        "week" => WeekStart(),
        "month" => new DateTime(Today.Year, Today.Month, 1),
        _ => DateTime.MinValue
    };

    double SecondsSince(DateTime from, string? courseId)
    {
        long t = 0;
        foreach (var s in data.Sessions)
            if (s.Start >= from && (courseId == null || s.CourseId == courseId)) t += s.Seconds;
        if (active && activeStart >= from && (courseId == null || activeCourseId == courseId))
            t += ActiveSeconds;
        return t;
    }

    Dictionary<DateTime, int> PerDay()
    {
        var r = new Dictionary<DateTime, int>();
        foreach (var s in data.Sessions)
            r[s.Start.Date] = r.GetValueOrDefault(s.Start.Date) + s.Seconds;
        if (active)
            r[activeStart.Date] = r.GetValueOrDefault(activeStart.Date) + ActiveSeconds;
        return r;
    }

    int Streak(Dictionary<DateTime, int> per)
    {
        var d = Today;
        if (per.GetValueOrDefault(d) < 60) d = d.AddDays(-1);
        int n = 0;
        while (per.GetValueOrDefault(d) >= 60) { n++; d = d.AddDays(-1); }
        return n;
    }

    /// <summary>İstatistik kartı tanımı: başlık, ikon geometrisi, ikon rengi ve rozet zemini (kaynak adları).</summary>
    record StatCardSpec(string Title, string Icon, string Fg, string Bg, bool Filled = false);

    // Beyaz kart: solda renkli yuvarlak ikon rozeti ve başlık, altında büyük kalın değer ve küçük bir not satırı.
    TextBlock[] BuildCards(Panel host, StatCardSpec[] specs, out TextBlock[] notes)
    {
        var values = new TextBlock[specs.Length];
        notes = new TextBlock[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            var sp = specs[i];
            var icon = new System.Windows.Shapes.Path
            {
                Data = (Geometry)FindResource(sp.Icon), Style = (Style)FindResource("Icon"),
                Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center
            };
            icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, sp.Fg);
            if (sp.Filled) icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, sp.Fg);
            var badge = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Child = icon };
            badge.SetResourceReference(Border.BackgroundProperty, sp.Bg);

            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(badge);
            head.Children.Add(new TextBlock
            {
                Text = sp.Title, Foreground = Res("Muted"), FontSize = 13, FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0)
            });

            var val = new TextBlock
            {
                Text = "-", Foreground = Res("Text"), FontSize = 26, FontWeight = FontWeights.Black,
                Margin = new Thickness(2, 12, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
            };
            var note = new TextBlock
            {
                Foreground = Res("Muted"), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 2, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed
            };

            var card = new Border
            {
                Style = (Style)FindResource("Card"),
                Padding = new Thickness(18, 16, 16, 16),
                Margin = new Thickness(0, 0, 14, 0),
                Child = new StackPanel { Children = { head, val, note } }
            };
            host.Children.Add(card);
            values[i] = val;
            notes[i] = note;
        }
        return values;
    }

    // "2 sa 05 dk" gibi bir metni büyük sayı + küçük birim olarak yazar.
    void SetBigValue(TextBlock tb, string text)
    {
        tb.Inlines.Clear();
        var parts = text.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            bool num = p.Length > 0 && char.IsDigit(p[0]);
            var run = new System.Windows.Documents.Run(num ? p : (i > 0 ? " " : "") + p + (i < parts.Length - 1 ? " " : ""));
            if (!num)
            {
                run.FontSize = 14;
                run.FontWeight = FontWeights.ExtraBold;
                run.Foreground = Res("Muted");
            }
            tb.Inlines.Add(run);
        }
    }

    void Save() { Store.Save(data); sync?.MarkDirty(); }

    // ---------- oturum mantığı ----------

    void StartBtn_Click(object sender, RoutedEventArgs e) => ToggleTimer();

    void ToggleTimer()
    {
        if (!active)
        {
            var c = SelectedCourse;
            if (c == null) return;
            active = true;
            activeCourseId = c.Id;
            activeStart = DateTime.Now;
            activeRunId = Guid.NewGuid().ToString("N");
            lastCheckpoint = 0;
            countdownTarget = data.Countdown ? data.CountdownMinutes * 60 : 0;
            data.CountdownRunId = countdownTarget > 0 ? activeRunId : "";
            data.CountdownSeconds = countdownTarget;
            sw.Restart();
        }
        else if (sw.IsRunning) sw.Stop();
        else sw.Start();
        PublishLive();
        UpdateAll();
    }

    static string Clock(int s) => $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";


    void FinishBtn_Click(object sender, RoutedEventArgs e) => FinishSession(timeUp: false);

    /// <summary>Oturumu bitirir ve kazanılan jetonu animasyonla gösterir (Bitir düğmesi / geri sayımın sonu).</summary>
    void FinishSession(bool timeUp)
    {
        var done = Commit();
        Special? special = null;
        string? growth = null;
        if (done != null)
        {
            growth = GrowthNote(done);
            explorerVisitPending = true;                       // gezgin bahçeye doğru yola çıkar
            special = ZooSpecials.From(data, done);
            if (special != null) zooArriving = special.Key;    // nadir dost bahçeye yürüyerek girer
        }
        UpdateAll();
        if (done != null) ShowCoinReward(done, timeUp, special, growth);
    }

    /// <summary>Süren oturumu kaydeder. Kaydedilen oturumu (5 sn'den kısaysa null) döndürür.</summary>
    Session? Commit()
    {
        if (!active) return null;
        sw.Stop();
        int s = (int)sw.Elapsed.TotalSeconds;
        if (countdownTarget > 0) s = Math.Min(s, countdownTarget); // geri sayım hedefi aşılmaz
        Session? saved = null;
        if (s >= 5)
            data.Sessions.Add(saved = new Session { Id = RunIdOrNew(), CourseId = activeCourseId, Start = activeStart, Seconds = s });
        countdownTarget = 0;
        data.CountdownRunId = "";
        active = false;
        sw.Reset();
        data.Active = null;
        PublishLive();
        Save();
        Title = "Flow State";
        return saved;
    }

    // ---------- canlı seans: cihazlar arası paylaşım ----------

    string RunIdOrNew() { if (activeRunId.Length == 0) activeRunId = Guid.NewGuid().ToString("N"); return activeRunId; }

    /// <summary>Yerel zamanlayıcı durumunu paylaşılan canlı duruma yazar ve hemen gönderir.</summary>
    void PublishLive()
    {
        data.SetLive(active, RunIdOrNew(), activeCourseId, activeStart, sw.Accumulated, sw.RunningSince);
        Store.Save(data);
        sync?.RequestLive();
    }

    void ApplyLiveLocal(LiveState l)
    {
        active = l.Active;
        activeCourseId = l.CourseId;
        activeStart = l.Start;
        activeRunId = l.RunId;
        lastCheckpoint = 0;
        lastTickSecond = -1;
        countdownTarget = active && l.RunId == data.CountdownRunId ? data.CountdownSeconds : 0;
        if (active) sw.Set(l.Accumulated, l.RunningSince);
        else { sw.Reset(); data.Active = null; Title = "Flow State"; }
    }

    // UI thread: başka cihazın canlı seansı geldi. Uygulandıysa true.
    bool OnRemoteLive(LiveState r)
    {
        if (r.Active)
        {
            if (!data.Courses.Any(c => c.Id == r.CourseId)) return false; // ders henüz gelmedi; sonra denenir
            if (active && activeRunId != r.RunId)
            {
                // Bu cihazdaki ayrı seansın emeği kaybolmasın: kaydet, sonra paylaşılana geç.
                int s = (int)sw.Elapsed.TotalSeconds;
                if (s >= 5) data.Sessions.Add(new Session { Id = RunIdOrNew(), CourseId = activeCourseId, Start = activeStart, Seconds = s });
                Save();
            }
        }
        else if (active && activeRunId != r.RunId)
        {
            // Uzaktaki bitiş başka bir seansa ait; bizimki sürüyor. Durumumuzu yenileyip tekrar yayınlarız.
            data.SetLive(true, activeRunId, activeCourseId, activeStart, sw.Accumulated, sw.RunningSince);
            Store.Save(data);
            return false;
        }
        ApplyLiveLocal(r);
        data.Live = r;
        Store.Save(data);
        UpdateAll();
        return true;
    }

    // Çember: her saat bir tam tur. İlerleme = saatin kaçta kaçı geçti.
    void UpdateRing()
    {
        double t = active ? sw.Elapsed.TotalSeconds : 0;
        double frac = countdownTarget > 0 ? Math.Min(t / countdownTarget, 1) : t % 3600 / 3600;
        if (frac < 0.002) { RingArc.Visibility = Visibility.Collapsed; ringShown = 0; return; }
        frac = Math.Min(frac, 0.9999);
        EnsureRingSegments();

        // 1°'lik küçük çizgilerden oluşan geçişli yay: görünen parça sayısı ilerlemeye göre değişir.
        double deg = frac * 360;
        int m = (int)Math.Ceiling(deg);
        for (int i = Math.Max(0, Math.Min(ringShown, m) - 2); i < Math.Max(ringShown, m); i++)
        {
            bool vis = i < m;
            ringSegs[i].Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
            if (vis) SetSeg(ringSegs[i], i, i == m - 1 ? deg : i + 1.6);
        }
        ringShown = m;

        // Yuvarlak uçlar: başlangıç ve güncel uç kendi geçiş renklerini alır.
        var acc = RingColor();
        ringCapStart!.Fill = new SolidColorBrush(GradAt(acc, 0));
        ringCapEnd!.Fill = new SolidColorBrush(GradAt(acc, frac));
        var (ex, ey) = RingPoint(deg);
        Canvas.SetLeft(ringCapEnd, ex - 10); Canvas.SetTop(ringCapEnd, ey - 10);
        RingArc.Visibility = Visibility.Visible;
    }

    readonly System.Windows.Shapes.Line[] ringSegs = new System.Windows.Shapes.Line[360];
    System.Windows.Shapes.Ellipse? ringCapStart, ringCapEnd;
    Color ringBuiltFor;
    int ringShown;

    static (double x, double y) RingPoint(double deg)
    {
        double a = deg * Math.PI / 180;
        return (150 + 140 * Math.Sin(a), 150 - 140 * Math.Cos(a));
    }

    static void SetSeg(System.Windows.Shapes.Line l, double fromDeg, double toDeg)
    {
        (l.X1, l.Y1) = RingPoint(fromDeg);
        (l.X2, l.Y2) = RingPoint(toDeg);
    }

    static Color GradAt(Color acc, double t)
    {
        var (r, g, b) = RingGradient.At(acc.R, acc.G, acc.B, t);
        return Color.FromRgb(r, g, b);
    }

    // Tema rengi değişince çizgilerin renkleri yeniden verilir.
    void EnsureRingSegments()
    {
        var acc = RingColor();
        if (ringCapStart != null && ringBuiltFor == acc) return;
        ringBuiltFor = acc;
        if (ringCapStart == null)
        {
            for (int i = 0; i < 360; i++)
            {
                ringSegs[i] = new System.Windows.Shapes.Line { StrokeThickness = 20, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat, Visibility = Visibility.Collapsed };
                RingArc.Children.Add(ringSegs[i]);
            }
            ringCapStart = new System.Windows.Shapes.Ellipse { Width = 20, Height = 20 };
            ringCapEnd = new System.Windows.Shapes.Ellipse { Width = 20, Height = 20 };
            var (sx, sy) = RingPoint(0);
            Canvas.SetLeft(ringCapStart, sx - 10); Canvas.SetTop(ringCapStart, sy - 10);
            RingArc.Children.Add(ringCapStart);
            RingArc.Children.Add(ringCapEnd);
        }
        for (int i = 0; i < 360; i++)
            ringSegs[i].Stroke = new SolidColorBrush(GradAt(acc, (i + 0.8) / 360.0));
    }

    int pipsShown = int.MinValue;

    void UpdatePips()
    {
        if (countdownTarget > 0 || (!active && data.Countdown))
        {
            if (pipsShown != -2) { HourPips.Children.Clear(); pipsShown = -2; }
            PipsLabel.Text = !active ? ""
                : $"{Fmt(countdownTarget)} hedefin %{Math.Min(100, ActiveSeconds * 100 / countdownTarget)}'i tamam";
            return;
        }
        int laps = ActiveSeconds / 3600;
        // Her saniye yeniden kurmamak için yalnızca sayı değişince çizilir.
        int key = active ? laps : -1;
        if (key == pipsShown) return;
        bool grew = pipsShown >= 0 && key > pipsShown;
        pipsShown = key;

        HourPips.Children.Clear();
        int shown = Math.Min(laps, 12);
        for (int i = 0; i < shown; i++)
        {
            var star = PipStar(true);
            HourPips.Children.Add(star);
            if (grew && i == shown - 1) PopIn(star); // yeni kazanılan yıldız küçük bir zıplamayla belirir
        }
        if (laps > 12)
        {
            var more = new TextBlock { Text = $"+{laps - 12}", FontSize = 13, FontWeight = FontWeights.ExtraBold, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
            more.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            HourPips.Children.Add(more);
        }
        if (active) HourPips.Children.Add(PipStar(false));
        PipsLabel.Text = !active ? "" : laps == 0 ? "İlk yıldızın yolda" : $"{laps} saat tamamlandı";
    }

    void Tick()
    {
        UpdateRing();
        UpdateExplorer();
        int s = ActiveSeconds;

        if (s == lastTickSecond) return;
        lastTickSecond = s;

        if (active && countdownTarget > 0 && sw.IsRunning && s >= countdownTarget)
        {
            System.Media.SystemSounds.Asterisk.Play();
            FinishSession(timeUp: true);
            return;
        }

        if (active)
        {
            var name = data.Courses.FirstOrDefault(c => c.Id == activeCourseId)?.Name ?? "";
            Title = $"{(sw.IsRunning ? "▶" : "⏸")} {name} — Flow State";
            if (s - lastCheckpoint >= 15)
            {
                lastCheckpoint = s;
                data.Active = new ActiveSession { CourseId = activeCourseId, Start = activeStart, Seconds = s };
                Store.Save(data); // yalnızca yerel çökme koruması; buluta gönderilmez
            }
        }
        UpdateAll();
    }

    // ---------- dersler ----------

    void RefreshCourses()
    {
        var selId = SelectedCourse?.Id;
        CourseList.Items.Clear();
        foreach (var c in data.Courses)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = ParseBrush(c.Color),
                Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0x50, 0x2A, 0x10)), StrokeThickness = 1,
                Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = c.Name, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 });
            CourseList.Items.Add(new ListBoxItem { Content = row, Tag = c });
        }
        var pick = CourseList.Items.OfType<ListBoxItem>().FirstOrDefault(i => (i.Tag as Course)?.Id == selId)
                   ?? CourseList.Items.OfType<ListBoxItem>().FirstOrDefault();
        if (pick != null) pick.IsSelected = true;
        UpdateAll();
    }

    void CourseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ready) UpdateAll();
    }

    void AddCourse_Click(object sender, RoutedEventArgs e) => AddCourse();

    void ToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        bool show = AddPanel.Visibility != Visibility.Visible;
        AddPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) NewCourseBox.Focus();
    }

    void NewCourseBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddCourse();
        else if (e.Key == Key.Escape) AddPanel.Visibility = Visibility.Collapsed;
    }

    void AddCourse()
    {
        var name = NewCourseBox.Text.Trim();
        if (name.Length == 0) return;
        var existing = data.Courses.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing == null)
        {
            existing = new Course { Name = name, Color = Palette[data.Courses.Count % Palette.Length] };
            data.Courses.Add(existing);
            Save();
        }
        NewCourseBox.Clear();
        AddPanel.Visibility = Visibility.Collapsed;
        RefreshCourses();
        if (!active)
            foreach (var i in CourseList.Items.OfType<ListBoxItem>())
                if ((i.Tag as Course)?.Id == existing.Id) i.IsSelected = true;
    }

    void DeleteCourse_Click(object sender, RoutedEventArgs e)
    {
        var c = SelectedCourse;
        if (c == null || active) return;
        if (!ConfirmDialog.Ask(this, "Dersi sil",
                $"\"{c.Name}\" dersi ve bu derse ait tüm çalışma kayıtları silinecek. Bu işlem geri alınamaz.",
                "Dersi sil")) return;
        data.DeleteCourse(c);
        Save();
        RefreshCourses();
    }

    // ---------- görünümler ----------

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (TimerView == null || StatsView == null || HistoryView == null || SettingsView == null || SocialView == null || ZooView == null || !ready) return;
        if (NavZoo.IsChecked != true && ZooView.Visibility == Visibility.Visible) ResetZooScreen();
        ZooView.Visibility = NavZoo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TimerView.Visibility = NavTimer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StatsView.Visibility = NavStats.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SocialView.Visibility = NavSocial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SocialShown(SocialView.Visibility == Visibility.Visible);
        SettingsView.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsView.Visibility == Visibility.Visible) { BuildSettings(); BuildGardenThemeTiles(); }
        UpdateAll();
    }

    void Seg_Checked(object sender, RoutedEventArgs e)
    {
        period = (string)((RadioButton)sender).Tag;
        if (BreakdownPanel != null && ready) UpdateBreakdown();
    }

    void UpdateAll()
    {
        if (!ready) return;
        UpdateTimerView();
        if (StatsView.Visibility == Visibility.Visible) UpdateStats();
        if (HistoryView.Visibility == Visibility.Visible) UpdateHistory();
        if (ZooView.Visibility == Visibility.Visible) UpdateZoo();
        if (TimerView.Visibility == Visibility.Visible) UpdateSunScene();
    }

    void UpdateTimerView()
    {
        var course = active
            ? data.Courses.FirstOrDefault(c => c.Id == activeCourseId)
            : SelectedCourse;

        TimerCourseName.Text = course?.Name ?? "Bir ders ekle";
        StartBtn.Content = active && sw.IsRunning ? PauseIcon() : PlayIcon();
        FinishBtn.Content = StopIcon();
        StartBtn.IsEnabled = course != null;
        FinishBtn.IsEnabled = active;
        CourseList.IsEnabled = !active;
        // Oturum sürerken ders ekle/sil butonları solmaz; sadece tıklanamaz olur.
        DeleteCourseBtn.IsEnabled = course != null && course.Id != Course.GeneralId;
        CourseTools.IsHitTestVisible = !active;
        if (active) AddPanel.Visibility = Visibility.Collapsed;

        UpdateStatusPill(course);
        UpdateRingFace(course);
        SetBreathing(active && sw.IsRunning);

        // Çember ve başlat butonu ayarlardaki tema rengini alır (StartBtn: DynamicResource Accent).
        // Widget'ta parıltı yok: sakin, sabit görünüm. Açık zeminde hafif, renkli bir hale yeter.
        var tc = RingColor();
        if (widgetMode) RingArc.Effect = null;
        else if (RingArc.Effect is not System.Windows.Media.Effects.DropShadowEffect glow || glow.Color != tc)
            RingArc.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = tc, BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.35 };

        int es = ActiveSeconds;
        ElapsedText.Text = countdownTarget > 0 ? Clock(Math.Max(0, countdownTarget - es))
            : !active && data.Countdown ? Clock(data.CountdownMinutes * 60)
            : Clock(es);
        // Mod seçici yalnızca oturum yokken görünür; widget'ta hiç yok.
        ModePanel.Visibility = widgetMode || active ? Visibility.Collapsed : Visibility.Visible;
        if (active) CloseDial();
        CountdownLabel.Text = data.Countdown ? $"Geri sayım · {data.CountdownMinutes} dk" : "Geri sayım";
        ElapsedText.Opacity = !active ? 0.4 : sw.IsRunning ? 1 : 0.6;

        UpdateRing();
        UpdatePips();
        // Boş yıldız satırı ve boş etiket yer kaplamasın (düğmeler kaydırmadan görünsün).
        if (!widgetMode)
        {
            HourPips.Visibility = HourPips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PipsLabel.Visibility = PipsLabel.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        PipsBar.Visibility = !widgetMode && (HourPips.Children.Count > 0 || PipsLabel.Text.Length > 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateStats()
    {
        var per = PerDay();
        int streak = Streak(per);
        int longest = Math.Max(streak, StatsMath.LongestStreak(per));
        int activeDays = StatsMath.ActiveDays(per);
        SetBigValue(streakCards[0], $"{streak} gün");
        SetBigValue(streakCards[1], $"{longest} gün");
        SetBigValue(streakCards[2], $"{activeDays} gün");

        bool todayDone = per.GetValueOrDefault(Today) >= StatsMath.MinDaySeconds;
        SetNote(streakNotes[0], streak == 0 ? "Bugün yeni bir seri başlat!"
            : todayDone ? "Bugünü de kaptın, harika gidiyorsun!"
            : "Seriyi sürdürmek için bugün biraz çalış.");
        SetNote(streakNotes[1], longest == 0 ? "İlk rekorun seni bekliyor."
            : streak > 0 && streak == longest ? "Şu an rekordasın!"
            : $"Rekora {longest - streak} gün kaldı.");
        SetNote(streakNotes[2], "En az 1 dakika çalıştığın günler.");

        SetBigValue(statCards[0], Fmt(SecondsSince(Today, null)));
        SetBigValue(statCards[1], Fmt(SecondsSince(WeekStart(), null)));
        SetBigValue(statCards[2], Fmt(SecondsSince(PeriodFrom("month"), null)));
        SetBigValue(statCards[3], Fmt(SecondsSince(DateTime.MinValue, null)));
        UpdateBreakdown();
        UpdateChart();
        UpdateHeatmap(per);
        UpdateWeekly(per);
        UpdateHourly();
    }

    static void SetNote(TextBlock tb, string text)
    {
        tb.Text = text;
        tb.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Ders rengi (bozuk ya da eksikse sönük kakao).
    static Color CourseColor(string? hex)
    {
        try { if (!string.IsNullOrWhiteSpace(hex)) return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch (FormatException) { }
        return Color.FromRgb(0xCB, 0xB9, 0xAC);
    }

    // Sevimli boş durum: yuvarlak pastel rozet içinde ikon, kısa başlık ve sıcak bir cümle.
    UIElement EmptyState(string iconKey, string title, string sub)
    {
        var icon = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource(iconKey), Style = (Style)FindResource("Icon"),
            Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Accent");
        var badge = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Child = icon, HorizontalAlignment = HorizontalAlignment.Center };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 18),
            Children =
            {
                badge,
                new TextBlock
                {
                    Text = title, Foreground = Res("Text"), FontSize = 16, FontWeight = FontWeights.ExtraBold,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
                },
                new TextBlock
                {
                    Text = sub, Foreground = Res("Muted"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Margin = new Thickness(0, 4, 0, 0)
                }
            }
        };
    }

    // Küçük hap etiket (saat aralığı, süre, yüzde).
    static Border Pill(string text, Brush? bg, Brush? fg, bool bold = false) => new()
    {
        Background = bg, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, Foreground = fg, FontSize = 12, FontWeight = bold ? FontWeights.ExtraBold : FontWeights.Bold }
    };

    // Isı haritası: sütun = hafta (Pzt..Paz satırları), son 18 hafta. Boş günler Panel2, dolu günler tema renginin tonları.
    void UpdateHeatmap(Dictionary<DateTime, int> per)
    {
        const int weeks = 18;
        HeatGrid.Children.Clear();
        var acc = WidgetColor();
        var empty = Res("Panel2");
        const double goalSec = 2 * 3600; // yoğunluk referansı
        var start = StatsMath.Monday(Today).AddDays(-7 * (weeks - 1));
        for (int row = 0; row < 7; row++)
            for (int col = 0; col < weeks; col++)
            {
                var d = start.AddDays(col * 7 + row);
                int sec = per.GetValueOrDefault(d);
                var sq = new Border
                {
                    Width = 26, Height = 26, Margin = new Thickness(3), CornerRadius = new CornerRadius(8),
                    Background = d > Today ? Brushes.Transparent : HeatBrush(acc, StatsMath.Level(sec, goalSec), empty),
                    ToolTip = d > Today ? null : $"{d.ToString("d MMMM dddd", Tr)}: {Fmt(sec)}"
                };
                if (d == Today)
                {
                    sq.BorderThickness = new Thickness(2.5);
                    sq.SetResourceReference(Border.BorderBrushProperty, "AccentDeep");
                }
                HeatGrid.Children.Add(sq);
            }

        HeatLegend.Children.Clear();
        HeatLegend.Children.Add(new TextBlock { Text = "az", Foreground = Res("Muted"), FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        for (int l = 0; l <= 4; l++)
            HeatLegend.Children.Add(new Border
            {
                Width = 16, Height = 16, Margin = new Thickness(2, 0, 2, 0), CornerRadius = new CornerRadius(5), Background = HeatBrush(acc, l, empty)
            });
        HeatLegend.Children.Add(new TextBlock { Text = "çok", Foreground = Res("Muted"), FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
    }

    static Brush HeatBrush(Color acc, int level, Brush empty) => level == 0
        ? empty
        : new SolidColorBrush(Lighten(acc, level switch { 1 => 0.72, 2 => 0.48, 3 => 0.24, _ => 0 }));

    void UpdateWeekly(Dictionary<DateTime, int> per)
    {
        var w = StatsMath.Weekly(per, 8);
        var items = new List<(string label, int sec, string tip, bool hi)>();
        var thisMon = StatsMath.Monday(Today);
        for (int i = 0; i < 8; i++)
        {
            var mon = thisMon.AddDays(-7 * (7 - i));
            items.Add(($"{mon.Day} {Tr.DateTimeFormat.AbbreviatedMonthNames[mon.Month - 1]}", w[i],
                $"{mon.ToString("d MMM", Tr)} haftası: {Fmt(w[i])}", i == 7));
        }
        BuildBars(WeekGrid, items, 1, true);
        WeekEmpty.Visibility = items.All(x => x.sec == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateHourly()
    {
        var sessions = data.Sessions.Select(s => (s.Start, s.Seconds)).ToList();
        if (active) sessions.Add((activeStart, ActiveSeconds));
        var h = StatsMath.Hourly(sessions);
        var items = new List<(string label, int sec, string tip, bool hi)>();
        for (int i = 0; i < 24; i++)
            items.Add((i % 3 == 0 ? i.ToString("00") : "", (int)h[i], $"{i:00}:00–{i:00}:59 arası toplam: {Fmt(h[i])}", i == DateTime.Now.Hour));
        BuildBars(HourGrid, items, 1, false);
        HourEmpty.Visibility = items.All(x => x.sec == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    // Tek çubuk: üstte (isteğe bağlı) değer, altta tamamen yuvarlak çubuk. Vurgulu (bugün / bu hafta / bu saat) çubuk tema renginde.
    // En yüksek değer 130 px'e ölçeklenir; boş değerler küçük bir nokta olarak görünür.
    StackPanel BarStack(int sec, double max, bool hi, double maxWidth, bool showValue)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        if (showValue && sec > 0)
        {
            var val = new TextBlock
            {
                Text = ShortTime(sec), FontSize = 11, FontWeight = hi ? FontWeights.ExtraBold : FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.NoWrap
            };
            if (hi) val.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeep");
            else val.Foreground = Res("Muted");
            stack.Children.Add(val);
        }
        if (sec > 0)
        {
            var bar = new Border
            {
                Height = Math.Max(sec / max * 130, 8), MaxWidth = maxWidth,
                // Yarıçap kutunun yarısını aşarsa WPF kırpar: kısa çubuklar da hap biçiminde kalır.
                CornerRadius = new CornerRadius(maxWidth / 2)
            };
            bar.SetResourceReference(Border.BackgroundProperty, hi ? "Accent" : "AccentDim");
            stack.Children.Add(bar);
        }
        else
        {
            var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, hi ? "AccentDim" : "Track");
            stack.Children.Add(dot);
        }
        return stack;
    }

    // Çubuğun altındaki etiket; vurgulu olan küçük pastel bir hap içinde.
    UIElement BarLabel(string text, bool hi)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
            FontWeight = hi ? FontWeights.Black : FontWeights.Bold
        };
        if (hi) tb.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeep");
        else tb.Foreground = Res("Muted");
        if (!hi) return new Border { Child = tb, Padding = new Thickness(0, 2, 0, 2), Margin = new Thickness(0, 6, 0, 0) };
        var pill = new Border
        {
            Child = tb, CornerRadius = new CornerRadius(9), Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
        };
        pill.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        return pill;
    }

    // Ortak çubuk grafik (haftalık, saatlik).
    void BuildBars(System.Windows.Controls.Primitives.UniformGrid host, List<(string label, int sec, string tip, bool hi)> items, int minMax, bool showValues)
    {
        host.Children.Clear();
        double max = Math.Max(items.Max(x => x.sec), 1800 * minMax);
        foreach (var (label, sec, tip, hi) in items)
        {
            var cell = new Grid { Margin = new Thickness(showValues ? 4 : 1.5, 0, showValues ? 4 : 1.5, 0), Background = Brushes.Transparent, ToolTip = tip };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.Children.Add(BarStack(sec, max, hi, showValues ? 40 : 14, showValues));
            // Saatlik grafikte etiketsiz sütunlarda hap çizilmez.
            var lb = BarLabel(label, hi && label.Length > 0);
            Grid.SetRow(lb, 1);
            cell.Children.Add(lb);
            host.Children.Add(cell);
        }
    }

    void UpdateBreakdown()
    {
        BreakdownPanel.Children.Clear();
        var from = PeriodFrom(period);
        var rows = data.Courses
            .Select(c => (c, sec: SecondsSince(from, c.Id)))
            .Where(x => x.sec > 0)
            .OrderByDescending(x => x.sec)
            .ToList();
        if (rows.Count == 0)
        {
            string when = period switch { "today" => "Bugün", "week" => "Bu hafta", "month" => "Bu ay", _ => "Şimdiye kadar" };
            BreakdownPanel.Children.Add(EmptyState("IconChart", $"{when} henüz kayıt yok",
                "Bir ders seçip başlat; derslerin burada renkli çubuklarla dizilecek."));
            return;
        }
        double total = rows.Sum(x => x.sec);
        foreach (var (c, sec) in rows)
        {
            double pct = sec / total * 100;
            var col = CourseColor(c.Color);

            var head = new DockPanel();
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock
            {
                Text = Fmt(sec), Foreground = Res("Text"), FontSize = 13, FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            });
            right.Children.Add(Pill($"%{pct:0}", new SolidColorBrush(Lighten(col, 0.82)), new SolidColorBrush(Tint(col, 0.62, 0)), true));
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(col), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
            };
            DockPanel.SetDock(dot, Dock.Left);
            head.Children.Add(dot);
            head.Children.Add(new TextBlock
            {
                Text = c.Name, Foreground = Res("Text"), FontSize = 14, FontWeight = FontWeights.ExtraBold,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            });

            var bar = new Grid { Height = 12, Margin = new Thickness(22, 8, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
            var bg = new Border { Background = Res("Track"), CornerRadius = new CornerRadius(6) };
            Grid.SetColumnSpan(bg, 2);
            bar.Children.Add(bg);
            bar.Children.Add(new Border
            {
                Background = new LinearGradientBrush(Lighten(col, 0.35), col, 0),
                CornerRadius = new CornerRadius(6)
            });

            BreakdownPanel.Children.Add(new StackPanel { Margin = new Thickness(0, 4, 0, 14), Children = { head, bar } });
        }
    }

    static string ShortTime(int sec) =>
        sec >= 3600 ? (sec / 3600.0).ToString("0.#", Tr) + " sa" : $"{sec / 60} dk";

    void UpdateChart()
    {
        ChartGrid.Children.Clear();
        var per = PerDay();
        var days = Enumerable.Range(0, 14).Select(i => Today.AddDays(i - 13)).ToList();
        double max = Math.Max(days.Max(d => per.GetValueOrDefault(d)), 1800);
        foreach (var d in days)
        {
            int sec = per.GetValueOrDefault(d);
            bool today = d == Today;
            var cell = new Grid
            {
                Margin = new Thickness(3, 0, 3, 0),
                Background = Brushes.Transparent,
                ToolTip = $"{d.ToString("d MMMM dddd", Tr)}: {Fmt(sec)}"
            };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cell.Children.Add(BarStack(sec, max, today, 30, true));

            var label = BarLabel($"{Tr.DateTimeFormat.AbbreviatedDayNames[(int)d.DayOfWeek]}\n{d.Day}", today);
            Grid.SetRow(label, 1);
            cell.Children.Add(label);
            ChartGrid.Children.Add(cell);
        }
        ChartEmpty.Visibility = days.All(d => per.GetValueOrDefault(d) == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    // Geçmiş: günlere göre gruplanmış kartlar. Her satırda ders rengi rozeti, saat aralığı ve süre hapları, sil düğmesi.
    void UpdateHistory()
    {
        HistoryPanel.Children.Clear();
        var list = data.Sessions.OrderByDescending(s => s.Start).Take(300).ToList();
        if (list.Count == 0)
        {
            HistoryPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("Card"), Margin = new Thickness(0, 10, 0, 0),
                Child = EmptyState("IconHistory", "Henüz kayıtlı çalışma yok",
                    "İlk oturumunu bitirdiğinde burada görünecek. Haydi, bir ders seç ve başla!")
            });
            return;
        }
        bool first = true;
        foreach (var day in list.GroupBy(s => s.Start.Date))
        {
            var d = day.Key;
            string title = d == Today ? "Bugün"
                : d == Today.AddDays(-1) ? "Dün"
                : d.Year == Today.Year ? d.ToString("d MMMM dddd", Tr)
                : d.ToString("d MMMM yyyy, dddd", Tr);
            int count = day.Count();
            int daySec = day.Sum(s => s.Seconds);

            var head = new DockPanel { Margin = new Thickness(6, first ? 10 : 24, 4, 10) };
            var sum = Pill($"{count} oturum · {Fmt(daySec)}", Res("Surface"), Res("Muted"));
            DockPanel.SetDock(sum, Dock.Right);
            head.Children.Add(sum);
            head.Children.Add(new TextBlock
            {
                Text = title, Foreground = Res("Text"), FontSize = 16, FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center
            });
            HistoryPanel.Children.Add(head);
            first = false;

            var rows = new StackPanel();
            bool firstRow = true;
            foreach (var s in day)
            {
                if (!firstRow)
                    rows.Children.Add(new Border { Height = 1, Background = Res("Line"), Margin = new Thickness(48, 0, 6, 0) });
                firstRow = false;
                rows.Children.Add(HistoryRow(s));
            }
            HistoryPanel.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(14, 6, 10, 6), Child = rows });
        }
    }

    UIElement HistoryRow(Session s)
    {
        var course = data.Courses.FirstOrDefault(c => c.Id == s.CourseId);
        var col = CourseColor(course?.Color);
        var g = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(0, 0, 12, 0),
            Background = new SolidColorBrush(Lighten(col, 0.8)),
            Child = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(col) }
        };
        var name = new TextBlock
        {
            Text = course?.Name ?? "(silinmiş ders)", FontSize = 14, FontWeight = FontWeights.ExtraBold,
            Foreground = course == null ? Res("Muted") : Res("Text"),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        var end = s.Start.AddSeconds(s.Seconds);
        var time = Pill($"{s.Start:HH:mm} – {end:HH:mm}", Res("Panel2"), Res("Muted"));
        time.Margin = new Thickness(10, 0, 8, 0);
        var durText = new TextBlock { Text = Fmt(s.Seconds), FontSize = 12, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Center };
        durText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeep");
        var dur = new Border
        {
            CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4), MinWidth = 76,
            VerticalAlignment = VerticalAlignment.Center, Child = durText
        };
        dur.SetResourceReference(Border.BackgroundProperty, "AccentSoft");

        var del = new Button
        {
            Style = (Style)FindResource("Ghost"), Padding = new Thickness(8), Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Bu kaydı sil", VerticalAlignment = VerticalAlignment.Center,
            Content = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("IconTrash"), Style = (Style)FindResource("Icon"), Width = 16, Height = 16 }
        };
        del.MouseEnter += (_, _) => del.Foreground = Res("Danger");
        del.MouseLeave += (_, _) => del.Foreground = Res("Muted");
        var session = s;
        del.Click += (_, _) => { data.DeleteSession(session); Save(); UpdateAll(); };

        Grid.SetColumn(name, 1); Grid.SetColumn(time, 2); Grid.SetColumn(dur, 3); Grid.SetColumn(del, 4);
        g.Children.Add(badge); g.Children.Add(name); g.Children.Add(time); g.Children.Add(dur); g.Children.Add(del);
        return g;
    }

    // ---------- hedef, dışa aktarma, klavye ----------

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"calisma-kayitlari-{DateTime.Today:yyyy-MM-dd}.csv"
        };
        if (dlg.ShowDialog(this) != true) return;
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("Tarih;Başlangıç;Ders;Süre (dk)\r\n");
        foreach (var s in data.Sessions.OrderBy(x => x.Start))
        {
            var name = (data.Courses.FirstOrDefault(c => c.Id == s.CourseId)?.Name ?? "").Replace(';', ',');
            sb.Append(s.Start.ToString("yyyy-MM-dd", inv)).Append(';')
              .Append(s.Start.ToString("HH:mm", inv)).Append(';')
              .Append(name).Append(';')
              .Append(Math.Round(s.Seconds / 60.0, 1).ToString(Tr)).Append("\r\n");
        }
        try { File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true)); }
        catch (Exception ex) { ConfirmDialog.Info(this, "Dışa aktarılamadı", ex.Message); }
    }

    // ---------- widget modu ----------

    bool widgetMode;

    int countdownTarget; // süren seansın geri sayım hedefi (sn); 0 = kronometre

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (ModeCountdown.IsChecked == true && !data.Countdown) countdownJustChecked = true;
        data.Countdown = ModeCountdown.IsChecked == true;
        Store.Save(data);
        UpdateAll();
    }

    // Geri sayım'a her tıklamada (zaten seçiliyken de) süre kadranı açılır.
    void ModeCountdown_Click(object sender, RoutedEventArgs e)
    {
        if (!active) OpenDial();
    }

    // Büyük pencerede çember biraz küçültülür ki düğmeler varsayılan boyda kaydırmadan görünsün.
    static readonly ScaleTransform NormalRingScale = new(0.88, 0.88);
    Rect normalBounds;
    double normalMinW, normalMinH;
    Rect widgetBounds = Rect.Empty;
    const double WidgetScale = 0.7, WidgetSize = 300;

    void WidgetToggle_Click(object sender, RoutedEventArgs e) => SetWidgetMode(!widgetMode);

    void WidgetClose_Click(object sender, RoutedEventArgs e) => Close();

    void SetWidgetMode(bool on)
    {
        if (on == widgetMode) return;
        widgetMode = on;

        if (on)
        {
            NavTimer.IsChecked = true; // widget yalnızca çalışma görünümünü gösterir
            normalBounds = new Rect(Left, Top, Width, Height);
            normalMinW = MinWidth; normalMinH = MinHeight;
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;

            TopBar.Visibility = Visibility.Collapsed;
            TimerHello.Visibility = Visibility.Collapsed;
            CourseTools.Visibility = Visibility.Collapsed;
            ToolsBar.Visibility = Visibility.Collapsed;
            SunScene.Visibility = Visibility.Collapsed;
            PipsBar.Visibility = Visibility.Collapsed;
            TimerPanel.VerticalAlignment = VerticalAlignment.Center;
            AddPanel.Visibility = Visibility.Collapsed;
            WidgetFrame.Visibility = Visibility.Visible;
            WidgetBar.Visibility = Visibility.Visible;
            BgLayer.Visibility = Visibility.Collapsed; // animasyonlar durur
            ApplyWidgetBackground();
            CourseList.Visibility = Visibility.Collapsed;
            HourPips.Visibility = Visibility.Collapsed;
            PipsLabel.Visibility = Visibility.Collapsed;
            TimerPanel.Margin = new Thickness(0);
            RingHost.Margin = new Thickness(0);
            RingHost.LayoutTransform = Transform.Identity;
            BtnRow.Margin = new Thickness(0, 6, 0, 0);
            StartBtn.Width = StartBtn.Height = 60;
            FinishBtn.Width = FinishBtn.Height = 50;
            TimerCourseName.FontSize = 26;
            TimerStatus.FontSize = 17;
            ElapsedText.FontSize = 46;
            ElapsedText.Margin = new Thickness(0, 0, 0, 2);
            TimerPanel.LayoutTransform = new ScaleTransform(WidgetScale, WidgetScale);

            MinWidth = 0; MinHeight = 0;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            Topmost = widgetOnTop;
            ApplyPinVisual();
            SizeToContent = SizeToContent.Manual;
            Width = WidgetSize; Height = WidgetSize;

            if (widgetBounds.IsEmpty)
            {
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - WidgetSize - 20;
                Top = wa.Top + 20;
            }
            else { Left = widgetBounds.X; Top = widgetBounds.Y; }
        }
        else
        {
            widgetBounds = new Rect(Left, Top, Width, Height);

            SizeToContent = SizeToContent.Manual;
            Topmost = false;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = normalMinW; MinHeight = normalMinH;

            TopBar.Visibility = Visibility.Visible;
            TimerHello.Visibility = Visibility.Visible;
            CourseTools.Visibility = Visibility.Visible;
            ToolsBar.Visibility = Visibility.Visible;
            SunScene.Visibility = Visibility.Visible;
            TimerPanel.VerticalAlignment = VerticalAlignment.Stretch;
            WidgetFrame.Visibility = Visibility.Collapsed;
            WidgetBar.Visibility = Visibility.Collapsed;
            BgLayer.Visibility = Visibility.Visible;
            SetResourceReference(BackgroundProperty, "BgGrad");
            CourseList.Visibility = Visibility.Visible;
            HourPips.Visibility = Visibility.Visible;
            PipsLabel.Visibility = Visibility.Visible;
            TimerPanel.Margin = new Thickness(24, 4, 24, 18);
            RingHost.Margin = new Thickness(0, 6, 0, 0);
            RingHost.LayoutTransform = NormalRingScale;
            BtnRow.Margin = new Thickness(0, 14, 0, 0);
            StartBtn.Width = StartBtn.Height = 92;
            FinishBtn.Width = FinishBtn.Height = 72;
            TimerCourseName.FontSize = 20;
            TimerStatus.FontSize = 13;
            ElapsedText.FontSize = 40;
            ElapsedText.Margin = new Thickness(0, 0, 0, 2);
            TimerPanel.LayoutTransform = Transform.Identity;

            Left = normalBounds.X; Top = normalBounds.Y;
            Width = normalBounds.Width; Height = normalBounds.Height;
        }
        UpdateAll();
    }

    bool widgetOnTop = false; // varsayılan: widget diğer pencerelerin altında kalır (📌 ile üste sabitlenir)

    // Krem zeminde neşeli duran şeker renkleri. Varsayılan mavi (#5B8CFF) mevcut kullanıcılar için listede kalır.
    static readonly string[] SwatchColors =
    {
        "#5B8CFF", // mavi (varsayılan)
        "#4CB8F5", // gök mavisi
        "#22B8B0", // turkuaz
        "#3CCB8E", // nane
        "#F5C33B", // güneş sarısı
        "#FFA94D", // mango
        "#FF7A6B", // mercan
        "#FF7EB6", // pembe
        "#A78BFA", // lavanta
        "#9C6B53", // kakao
    };

    Color WidgetColor()
    {
        try { return (Color)ColorConverter.ConvertFromString(data.WidgetColor); }
        catch { return Color.FromRgb(0x5B, 0x8C, 0xFF); }
    }

    Brush WidgetAccent() => new SolidColorBrush(WidgetColor());

    static Color Tint(Color c, double k, int add) => Color.FromRgb(
        (byte)Math.Min(255, c.R * k + add), (byte)Math.Min(255, c.G * k + add), (byte)Math.Min(255, c.B * k + add));

    static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

    static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    static readonly Color Cream = Color.FromRgb(0xFF, 0xF9, 0xF1), Peach = Color.FromRgb(0xFD, 0xEB, 0xDD);
    static readonly Color Cocoa = Color.FromRgb(0x3B, 0x2C, 0x25);

    // Seçilen tema rengini tüm uygulamaya uygular: vurgu ve türevleri, krem zemin ve pastel ışık lekeleri.
    void ApplyTheme()
    {
        var c = WidgetColor();
        var res = Application.Current.Resources;
        res["Accent"] = new SolidColorBrush(c);
        res["AccentDim"] = new SolidColorBrush(Lighten(c, 0.58));
        res["AccentSoft"] = res["Sel"] = new SolidColorBrush(Lighten(c, 0.84));
        res["AccentDeep"] = new SolidColorBrush(Tint(c, 0.72, 0));
        // Açık tema renklerinde (sarı, gri) beyaz yazı okunmaz: kakao kullan.
        res["OnAccent"] = new SolidColorBrush(Luma(c) > 0.72 ? Cocoa : Colors.White);
        res["BgGrad"] = new LinearGradientBrush(Mix(Cream, c, 0.03), Mix(Peach, c, 0.07), new Point(0, 0), new Point(1, 1));

        static void Blob(GradientStop a, GradientStop b, Color col, byte alpha)
        {
            a.Color = Color.FromArgb(alpha, col.R, col.G, col.B);
            b.Color = Color.FromArgb(0, col.R, col.G, col.B);
        }
        Blob(Blob1A, Blob1B, Lighten(c, 0.55), 0x66);
        Blob(Blob2A, Blob2B, Color.FromRgb(0xFF, 0xC9, 0xA8), 0x70);
        Blob(BlobC1, BlobC2, Color.FromRgb(0xFF, 0xE3, 0x9A), 0x6A);
        Blob(Blob4A, Blob4B, Mix(Color.FromRgb(0xB8, 0xF0, 0xD6), c, 0.25), 0x60);
    }

    // Widget: bahçe sahnesi kapalı; zemin vaktin gökyüzü (güneş saat gökyüzünde asılı durur).
    void ApplyWidgetBackground()
    {
        Background = new LinearGradientBrush(C(sunPalette.SkyTop), C(sunPalette.SkyBottom), 90);
    }

    // ---------- yuvarlak düğme ikonları ----------
    // Renkleri içinde bulundukları düğmenin Foreground'undan gelir: başlat = OnAccent, bitir = Danger.

    static System.Windows.Data.Binding ForegroundOfButton() => new("Foreground")
    { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) };

    static UIElement PlayIcon()
    {
        var p = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 3,2 L 21,12 L 3,22 Z"),
            StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, Width = 26, Height = 26, Stretch = Stretch.None,
            Margin = new Thickness(5, 0, 0, 0)
        };
        p.SetBinding(System.Windows.Shapes.Shape.FillProperty, ForegroundOfButton());
        p.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, ForegroundOfButton());
        return p;
    }

    static UIElement PauseIcon()
    {
        static Border Bar(double left)
        {
            var b = new Border { Width = 8, Height = 24, CornerRadius = new CornerRadius(4), Margin = new Thickness(left, 0, 0, 0) };
            b.SetBinding(Border.BackgroundProperty, ForegroundOfButton());
            return b;
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { Bar(0), Bar(7) } };
    }

    static UIElement StopIcon()
    {
        var b = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(6) };
        b.SetBinding(Border.BackgroundProperty, ForegroundOfButton());
        return b;
    }

    // ---------- ayarlar ----------

    // Tema rengi şekerleri SettingsView kaynaklarındaki "Swatch" stiliyle çizilir (seçili halka, tik, üzerine gelince büyüme).
    // bounce: yeni seçilen şeker küçük bir zıplamayla belirir.
    void BuildSettings(bool bounce = false)
    {
        SwatchPanel.Children.Clear();
        var cur = WidgetColor();
        var style = (Style)SettingsView.Resources["Swatch"];
        foreach (var hex in SwatchColors)
        {
            var col = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = col == cur;
            var dot = new Button
            {
                Style = style, Tag = selected ? "sel" : hex, ToolTip = hex,
                Background = new SolidColorBrush(col),
                Foreground = new SolidColorBrush(Luma(col) > 0.72 ? Cocoa : Colors.White) // tik rengi
            };
            dot.Click += (_, _) => SetWidgetColor(hex);
            SwatchPanel.Children.Add(dot);
            if (selected && bounce) SwatchBounce(dot);
        }
        HexBox.Text = data.WidgetColor.ToUpperInvariant();
        HexPreview.Background = new SolidColorBrush(cur);
        HexError.Visibility = Visibility.Collapsed;
    }

    static void SwatchBounce(FrameworkElement el)
    {
        var sc = new ScaleTransform(1, 1);
        el.RenderTransform = sc;
        var a = new System.Windows.Media.Animation.DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(0.45))
        {
            EasingFunction = new System.Windows.Media.Animation.ElasticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Oscillations = 2, Springiness = 4 }
        };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void SetWidgetColor(string hex)
    {
        data.WidgetColor = hex.ToUpperInvariant();
        Save();
        ApplyTheme();
        UpdateAll();
        BuildSettings(true);
    }

    void ApplyHex()
    {
        var t = HexBox.Text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        bool ok = t.Length == 7 && t[1..].All(Uri.IsHexDigit);
        if (!ok) { HexError.Visibility = Visibility.Visible; return; }
        SetWidgetColor(t);
    }

    void HexBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyHex(); e.Handled = true; }
    }

    void HexBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var t = HexBox.Text.Trim();
        if (t.Length > 0 && !string.Equals(t.TrimStart('#'), data.WidgetColor.TrimStart('#'), StringComparison.OrdinalIgnoreCase))
            ApplyHex();
    }

    void WidgetPin_Click(object sender, RoutedEventArgs e)
    {
        widgetOnTop = !widgetOnTop;
        Topmost = widgetOnTop;
        ApplyPinVisual();
    }

    void ApplyPinVisual()
    {
        PinBtn.Opacity = widgetOnTop ? 1.0 : 0.4;
        PinBtn.ToolTip = widgetOnTop ? "Her zaman üstte: açık (arkaya al)" : "Her zaman üstte: kapalı (öne al)";
    }

    void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!widgetMode) return;
        // Düğmelerde sürükleme yok; çemberde tık/sürükleme ayrımı Ring_* olaylarında yapılır.
        if (HasAncestor(e.OriginalSource as DependencyObject, p => p is System.Windows.Controls.Primitives.ButtonBase || p == RingHost)) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    static bool HasAncestor(DependencyObject? d, Func<DependencyObject, bool> pred)
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (pred(d)) return true;
        return false;
    }

    // ---------- çembere tıklayınca başlat / duraklat ----------

    bool ringPressed;
    Point ringPressPoint;

    void Ring_Down(object sender, MouseButtonEventArgs e)
    {
        ringPressed = true;
        ringPressPoint = e.GetPosition(this);
        RingHost.CaptureMouse();
    }

    void Ring_Move(object sender, MouseEventArgs e)
    {
        if (!ringPressed || !widgetMode || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(this) - ringPressPoint;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        // Yeterince hareket etti: tık değil, widget'ı sürüklüyor.
        ringPressed = false;
        RingHost.ReleaseMouseCapture();
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    void Ring_Up(object sender, MouseButtonEventArgs e)
    {
        if (!ringPressed) return;
        ringPressed = false;
        RingHost.ReleaseMouseCapture();
        if (RingHost.IsMouseOver) ToggleTimer();
    }

    // ---------- süreleri sıfırla ----------

    void ResetPeriod_Click(object sender, RoutedEventArgs e)
    {
        string p = (string)((Button)sender).Tag;
        string label = p switch { "today" => "bugünkü", "week" => "bu haftaki", _ => "bu aydaki" };
        var from = PeriodFrom(p);
        int n = data.Sessions.Count(s => s.Start >= from);
        bool activeIn = active && activeStart >= from;
        if (n == 0 && !activeIn)
        {
            ConfirmDialog.Info(this, "Süreleri sıfırla", $"Sıfırlanacak {label} kayıt yok.");
            return;
        }
        string cap = char.ToUpper(label[0], new System.Globalization.CultureInfo("tr-TR")) + label[1..];
        if (!ConfirmDialog.Ask(this, "Süreleri sıfırla",
                $"{cap} tüm çalışma kayıtları ({n} kayıt) silinecek. Bu işlem geri alınamaz.",
                "Sıfırla")) return;

        data.DeleteSessions(s => s.Start >= from);
        if (activeIn)
        {
            sw.Reset();
            active = false;
            data.Active = null;
            Title = "Flow State";
            PublishLive();
        }
        Save();
        UpdateAll();
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DialOpen && DialKey(e.Key)) { e.Handled = true; return; }
        if (e.Key == Key.Escape && widgetMode)
        {
            SetWidgetMode(false);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox && NavTimer.IsChecked == true)
        {
            ToggleTimer();
            e.Handled = true;
        }
    }

    bool closeFlushed;

    async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closeFlushed) return;
        Commit();
        Save();
        if (sync is { SignedIn: true })
        {
            // Çıkmadan önce kısa süre senkronla: bitirilen seans ve canlı durum diğer cihaza ulaşsın.
            e.Cancel = true;
            try { await Task.WhenAny(sync.SyncAsync(), Task.Delay(4000)); } catch { }
            closeFlushed = true;
            Close();
        }
    }
}
