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

namespace StudyAssistant;

public partial class MainWindow : Window
{
    static readonly CultureInfo Tr = new("tr-TR");
    static readonly string[] Palette =
        { "#4F8CFF", "#F2994A", "#27AE60", "#BB6BD9", "#EB5757", "#2D9CDB", "#F2C94C", "#56CCF2" };

    readonly AppData data = Store.Load();
    readonly Stopwatch sw = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    bool active;                    // oturum var (çalışıyor ya da duraklatılmış)
    string activeCourseId = "";
    DateTime activeStart;
    int lastCheckpoint;
    int lastTickSecond = -1;
    string period = "today";

    bool ready;
    TextBlock[] statCards = null!;

    static DateTime Today => DateTime.Today;

    public MainWindow()
    {
        InitializeComponent();

        // Önceki çalışma çökme/kapanma ile yarım kaldıysa kaydet.
        if (data.Active is { } a)
        {
            if (a.Seconds >= 5)
                data.Sessions.Add(new Session { CourseId = a.CourseId, Start = a.Start, Seconds = a.Seconds });
            data.Active = null;
            Store.Save(data);
        }

        string[] titles = { "Bugün", "Bu hafta", "Bu ay", "Toplam" };
        statCards = BuildCards(StatsCards, titles);
        GoalBox.Text = data.DailyGoalHours.ToString("0.##", Tr);

        BuildStars();
        ApplyTheme();
        ready = true;
        RefreshCourses();
        timer.Tick += (_, _) => Tick();
        timer.Start();
        UpdateAll();

        sync = new SyncService(data, new DispatcherSynchronizationContext(Dispatcher), new WindowsSecretCodec(),
            onDataChanged: () => { Store.Save(data); RefreshCourses(); UpdateAll(); },
            courseInUse: id => active && activeCourseId == id);
        sync.StatusChanged += RefreshSyncUi;
        RefreshSyncUi();
    }

    // ---------- senkronizasyon ----------

    SyncService? sync;

    void RefreshSyncUi()
    {
        if (sync == null) return;
        SyncStatus.Text = sync.SignedIn ? $"{sync.Email} · {sync.Status}" : sync.Status;
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

    readonly List<(System.Windows.Shapes.Ellipse e, double fx, double fy)> stars = new();

    void BuildStars()
    {
        var rnd = new Random(7);
        for (int i = 0; i < 90; i++)
        {
            double size = 1 + rnd.NextDouble() * 2;
            var star = new System.Windows.Shapes.Ellipse
            {
                Width = size, Height = size,
                Fill = Brushes.White, Opacity = 0.1
            };
            var anim = new System.Windows.Media.Animation.DoubleAnimation(0.08, 0.35 + rnd.NextDouble() * 0.55,
                TimeSpan.FromSeconds(1.8 + rnd.NextDouble() * 4))
            {
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(rnd.NextDouble() * 5)
            };
            star.BeginAnimation(OpacityProperty, anim);
            stars.Add((star, rnd.NextDouble(), rnd.NextDouble()));
            StarCanvas.Children.Add(star);
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

    TextBlock[] BuildCards(Panel host, string[] titles)
    {
        var values = new TextBlock[titles.Length];
        for (int i = 0; i < titles.Length; i++)
        {
            var val = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0), Text = "-" };
            var card = new Border
            {
                Background = Res("Panel"),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = titles[i], Foreground = Res("Muted"), FontSize = 13 },
                        val
                    }
                }
            };
            host.Children.Add(card);
            values[i] = val;
        }
        return values;
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
            lastCheckpoint = 0;
            sw.Restart();
        }
        else if (sw.IsRunning) sw.Stop();
        else sw.Start();
        UpdateAll();
    }

    void FinishBtn_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        UpdateAll();
    }

    void Commit()
    {
        if (!active) return;
        sw.Stop();
        int s = (int)sw.Elapsed.TotalSeconds;
        if (s >= 5)
            data.Sessions.Add(new Session { CourseId = activeCourseId, Start = activeStart, Seconds = s });
        active = false;
        sw.Reset();
        data.Active = null;
        Save();
        Title = "Flow State";
    }

    // Çember: her saat bir tam tur. İlerleme = saatin kaçta kaçı geçti.
    void UpdateRing()
    {
        double t = active ? sw.Elapsed.TotalSeconds : 0;
        double frac = t % 3600 / 3600;
        if (frac < 0.002) { RingArc.Visibility = Visibility.Collapsed; return; }
        frac = Math.Min(frac, 0.9999);
        double ang = frac * 2 * Math.PI;
        RingSeg.Point = new Point(150 + 140 * Math.Sin(ang), 150 - 140 * Math.Cos(ang));
        RingSeg.IsLargeArc = frac > 0.5;
        RingArc.Visibility = Visibility.Visible;
    }

    void UpdatePips()
    {
        int laps = ActiveSeconds / 3600;
        HourPips.Children.Clear();
        var color = RingArc.Stroke;
        int shown = Math.Min(laps, 12);
        for (int i = 0; i < shown; i++)
            HourPips.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Fill = color, Margin = new Thickness(4, 0, 4, 0)
            });
        if (laps > 12)
            HourPips.Children.Add(new TextBlock { Text = $"+{laps - 12}", Foreground = IconLight, FontSize = 14, Margin = new Thickness(4, -1, 4, 0) });
        if (active)
            HourPips.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 12, Height = 12, Stroke = color, StrokeThickness = 1.5, Margin = new Thickness(4, 0, 4, 0)
            });
        PipsLabel.Text = !active ? "" : laps == 0 ? "İlk saatin turu" : $"{laps} saat tamamlandı";
    }

    void Tick()
    {
        UpdateRing();
        int s = ActiveSeconds;

        if (s == lastTickSecond) return;
        lastTickSecond = s;

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
                Width = 10, Height = 10, Fill = ParseBrush(c.Color),
                Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center
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
        if (TimerView == null || StatsView == null || HistoryView == null || SettingsView == null || !ready) return;
        TimerView.Visibility = NavTimer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StatsView.Visibility = NavStats.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (SettingsView.Visibility == Visibility.Visible) BuildSettings();
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
        DeleteCourseBtn.IsEnabled = course != null;
        CourseTools.IsHitTestVisible = !active;
        if (active) AddPanel.Visibility = Visibility.Collapsed;

        TimerStatus.Text = !active ? (course == null ? "" : "Başlamaya hazır")
            : !sw.IsRunning ? "Duraklatıldı"
            : "Odaklanıyorsun";

        // Çember ve başlat butonu ayarlardaki tema rengini alır.
        var tc = WidgetColor();
        Brush ring = new SolidColorBrush(tc);
        RingArc.Stroke = ring;
        StartBtn.Background = ring;
        // Widget'ta parıltı yok: sakin, sabit görünüm.
        RingArc.Effect = widgetMode ? null : new System.Windows.Media.Effects.DropShadowEffect
        { Color = tc, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.65 };

        UpdateRing();
        UpdatePips();
    }

    void UpdateStats()
    {
        var per = PerDay();
        double today = per.GetValueOrDefault(Today);
        double goal = Math.Max(0.1, data.DailyGoalHours) * 3600;
        double pct = Math.Min(100, today / goal * 100);
        GoalCol1.Width = new GridLength(pct, GridUnitType.Star);
        GoalCol2.Width = new GridLength(100 - pct, GridUnitType.Star);
        GoalText.Text = $"Bugün {Fmt(today)} / {data.DailyGoalHours.ToString("0.##", Tr)} sa" + (pct >= 100 ? "  ✓ hedef tamam" : "");
        StreakText.Text = $"🔥 {Streak(per)} günlük seri";

        statCards[0].Text = Fmt(SecondsSince(Today, null));
        statCards[1].Text = Fmt(SecondsSince(WeekStart(), null));
        statCards[2].Text = Fmt(SecondsSince(PeriodFrom("month"), null));
        statCards[3].Text = Fmt(SecondsSince(DateTime.MinValue, null));
        UpdateBreakdown();
        UpdateChart();
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
            BreakdownPanel.Children.Add(new TextBlock { Text = "Bu dönemde kayıt yok.", Foreground = Res("Muted") });
            return;
        }
        double total = rows.Sum(x => x.sec);
        foreach (var (c, sec) in rows)
        {
            double pct = sec / total * 100;
            var head = new DockPanel();
            var right = new TextBlock { Text = $"{Fmt(sec)}   %{pct:0}", Foreground = Res("Muted") };
            DockPanel.SetDock(right, Dock.Right);
            head.Children.Add(right);
            head.Children.Add(new TextBlock { Text = c.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });

            var bar = new Grid { Height = 8, Margin = new Thickness(0, 8, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - pct, GridUnitType.Star) });
            var bg = new Border { Background = Res("Panel2"), CornerRadius = new CornerRadius(4) };
            Grid.SetColumnSpan(bg, 2);
            bar.Children.Add(bg);
            bar.Children.Add(new Border { Background = ParseBrush(c.Color), CornerRadius = new CornerRadius(4) });

            BreakdownPanel.Children.Add(new Border
            {
                Background = Res("Panel"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 14), Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel { Children = { head, bar } }
            });
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
            double h = sec / max * 130;
            var cell = new Grid
            {
                Margin = new Thickness(3, 0, 3, 0),
                Background = Brushes.Transparent,
                ToolTip = $"{d.ToString("d MMMM dddd", Tr)}: {Fmt(sec)}"
            };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            if (sec > 0)
                stack.Children.Add(new TextBlock
                {
                    Text = ShortTime(sec), FontSize = 11, Foreground = Res("Muted"),
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 3)
                });
            stack.Children.Add(new Border
            {
                Height = sec > 0 ? Math.Max(h, 3) : 2,
                Background = d == Today ? Res("Accent") : Res("AccentDim"),
                CornerRadius = new CornerRadius(4, 4, 0, 0)
            });
            cell.Children.Add(stack);

            var label = new TextBlock
            {
                Text = $"{Tr.DateTimeFormat.AbbreviatedDayNames[(int)d.DayOfWeek]}\n{d.Day}",
                FontSize = 11, TextAlignment = TextAlignment.Center,
                Foreground = d == Today ? Res("Text") : Res("Muted"),
                Margin = new Thickness(0, 6, 0, 0)
            };
            Grid.SetRow(label, 1);
            cell.Children.Add(label);
            ChartGrid.Children.Add(cell);
        }
    }

    void UpdateHistory()
    {
        HistoryPanel.Children.Clear();
        var list = data.Sessions.OrderByDescending(s => s.Start).Take(300).ToList();
        if (list.Count == 0)
        {
            HistoryPanel.Children.Add(new TextBlock { Text = "Henüz kayıtlı çalışma yok.", Foreground = Res("Muted") });
            return;
        }
        foreach (var s in list)
        {
            var course = data.Courses.FirstOrDefault(c => c.Id == s.CourseId);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var date = new TextBlock { Text = s.Start.ToString("d MMM yyyy HH:mm", Tr), Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = course?.Name ?? "(silinmiş ders)", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var dur = new TextBlock { Text = Fmt(s.Seconds), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var del = new Button { Content = "✕", Style = (Style)FindResource("Btn"), Padding = new Thickness(10, 4, 10, 4), ToolTip = "Bu kaydı sil" };
            var session = s;
            del.Click += (_, _) => { data.DeleteSession(session); Save(); UpdateAll(); };

            Grid.SetColumn(name, 1); Grid.SetColumn(dur, 2); Grid.SetColumn(del, 3);
            g.Children.Add(date); g.Children.Add(name); g.Children.Add(dur); g.Children.Add(del);

            HistoryPanel.Children.Add(new Border
            {
                Background = Res("Panel"), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 0, 4), Child = g
            });
        }
    }

    // ---------- hedef, dışa aktarma, klavye ----------

    void GoalBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyGoal(); Keyboard.ClearFocus(); }
    }

    void GoalBox_LostFocus(object sender, RoutedEventArgs e) => ApplyGoal();

    void ApplyGoal()
    {
        if (double.TryParse(GoalBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g)
            && g > 0 && g <= 24)
        {
            data.DailyGoalHours = g;
            Save();
        }
        GoalBox.Text = data.DailyGoalHours.ToString("0.##", Tr);
        UpdateAll();
    }

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
            CourseTools.Visibility = Visibility.Collapsed;
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
            BtnRow.Margin = new Thickness(0, 6, 0, 0);
            StartBtn.Width = StartBtn.Height = FinishBtn.Width = FinishBtn.Height = 60;
            TimerCourseName.FontSize = 30;
            TimerStatus.FontSize = 20;
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
            CourseTools.Visibility = Visibility.Visible;
            WidgetFrame.Visibility = Visibility.Collapsed;
            WidgetBar.Visibility = Visibility.Collapsed;
            BgLayer.Visibility = Visibility.Visible;
            SetResourceReference(BackgroundProperty, "BgGrad");
            CourseList.Visibility = Visibility.Visible;
            HourPips.Visibility = Visibility.Visible;
            PipsLabel.Visibility = Visibility.Visible;
            TimerPanel.Margin = new Thickness(24, 12, 24, 28);
            RingHost.Margin = new Thickness(0, 22, 0, 0);
            BtnRow.Margin = new Thickness(0, 24, 0, 0);
            StartBtn.Width = StartBtn.Height = FinishBtn.Width = FinishBtn.Height = 84;
            TimerCourseName.FontSize = 24;
            TimerStatus.FontSize = 14;
            TimerPanel.LayoutTransform = Transform.Identity;

            Left = normalBounds.X; Top = normalBounds.Y;
            Width = normalBounds.Width; Height = normalBounds.Height;
        }
        UpdateAll();
    }

    bool widgetOnTop = true;

    static readonly string[] SwatchColors =
        { "#5B8CFF", "#FF5A6A", "#3DDC97", "#A78BFA", "#FFB04D", "#FF6FB5", "#2DD4D4", "#E5E7EB" };

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

    // Seçilen tema rengini tüm uygulamaya uygular: vurgu, seçili öğeler, zemin ve arka plan ışıkları.
    void ApplyTheme()
    {
        var c = WidgetColor();
        var res = Application.Current.Resources;
        res["Accent"] = new SolidColorBrush(c);
        res["AccentDim"] = new SolidColorBrush(Tint(c, 0.42, 14));
        res["Sel"] = new SolidColorBrush(Tint(c, 0.30, 22));
        res["BgGrad"] = new LinearGradientBrush(Tint(c, 0.09, 5), Tint(c, 0.20, 8), new Point(0, 0), new Point(1, 1));

        static void Blob(GradientStop a, GradientStop b, Color col, byte alpha)
        {
            a.Color = Color.FromArgb(alpha, col.R, col.G, col.B);
            b.Color = Color.FromArgb(0, col.R, col.G, col.B);
        }
        Blob(Blob1A, Blob1B, c, 0x70);
        Blob(Blob2A, Blob2B, Lighten(c, 0.35), 0x50);
        Blob(BlobC1, BlobC2, c, 0x78);
        Blob(Blob4A, Blob4B, Tint(c, 0.75, 0), 0x55);
    }

    // Widget'ta hareketli arka plan (bloblar, yıldızlar) kapalı; yerine seçilen rengin koyu tonu.
    void ApplyWidgetBackground()
    {
        var c = WidgetColor();
        Background = new LinearGradientBrush(Tint(c, 0.16, 5), Tint(c, 0.06, 3), new Point(0, 0), new Point(1, 1));
    }

    // ---------- yuvarlak düğme ikonları ----------

    static readonly Brush IconDark = new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x1A));
    static readonly Brush IconLight = new SolidColorBrush(Color.FromRgb(0xEE, 0xF1, 0xFF));

    static UIElement PlayIcon() => new System.Windows.Shapes.Path
    {
        Data = Geometry.Parse("M 3,2 L 21,12 L 3,22 Z"),
        Fill = IconDark, Stroke = IconDark, StrokeThickness = 5,
        StrokeLineJoin = PenLineJoin.Round, Width = 26, Height = 26, Stretch = Stretch.None,
        Margin = new Thickness(4, 0, 0, 0)
    };

    static UIElement PauseIcon() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Children =
        {
            new Border { Width = 8, Height = 24, CornerRadius = new CornerRadius(4), Background = IconDark },
            new Border { Width = 8, Height = 24, CornerRadius = new CornerRadius(4), Background = IconDark, Margin = new Thickness(7, 0, 0, 0) }
        }
    };

    static UIElement StopIcon() => new Border
    { Width = 22, Height = 22, CornerRadius = new CornerRadius(7), Background = IconLight };

    // ---------- ayarlar ----------

    void BuildSettings()
    {
        SwatchPanel.Children.Clear();
        var cur = WidgetColor();
        foreach (var hex in SwatchColors)
        {
            var col = (Color)ColorConverter.ConvertFromString(hex);
            bool selected = col == cur;
            var dot = new Button
            {
                Width = 38, Height = 38, Margin = new Thickness(4), Cursor = Cursors.Hand, Tag = hex,
                ToolTip = hex,
                Template = SwatchTemplate(col, selected)
            };
            dot.Click += (_, _) => SetWidgetColor(hex);
            SwatchPanel.Children.Add(dot);
        }
        HexBox.Text = data.WidgetColor.ToUpperInvariant();
        HexPreview.Background = new SolidColorBrush(cur);
        HexError.Visibility = Visibility.Collapsed;
    }

    static ControlTemplate SwatchTemplate(Color col, bool selected)
    {
        var tpl = new ControlTemplate(typeof(Button));
        var root = new FrameworkElementFactory(typeof(Border));
        root.SetValue(Border.CornerRadiusProperty, new CornerRadius(19));
        root.SetValue(Border.BackgroundProperty, new SolidColorBrush(col));
        root.SetValue(Border.BorderThicknessProperty, new Thickness(selected ? 3 : 1));
        root.SetValue(Border.BorderBrushProperty, new SolidColorBrush(selected ? Colors.White : Color.FromArgb(0x55, 255, 255, 255)));
        tpl.VisualTree = root;
        return tpl;
    }

    void SetWidgetColor(string hex)
    {
        data.WidgetColor = hex.ToUpperInvariant();
        Save();
        ApplyTheme();
        UpdateAll();
        BuildSettings();
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
        }
        Save();
        UpdateAll();
    }

    void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Commit();
        Save();
    }
}
