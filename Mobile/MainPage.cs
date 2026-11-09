using System.Globalization;

namespace FlowState.Mobile;

public class MainPage : ContentPage
{
	static readonly Color Bg = Color.FromArgb("#060814"), Panel = Color.FromArgb("#0D1126"),
		TextC = Color.FromArgb("#EEF1FF"), Muted = Color.FromArgb("#8791B5"), Accent = Color.FromArgb("#7C9CFF"),
		Danger = Color.FromArgb("#FF6B81");
	static readonly string[] Palette = { "#7C9CFF", "#FF9F5A", "#FF6FA8", "#4FD6A5", "#B58CFF", "#FFD25A", "#5AD1FF" };
	static readonly CultureInfo Tr = new("tr-TR");

	readonly AppData data;
	readonly SyncService sync;

	// zamanlayıcı durumu (zaman damgalarından hesaplanır; arka plan sayacı bozmaz)
	string? runCourseId; DateTime runStart; double accumulated; DateTime? runningSince;
	bool Active => runCourseId != null;
	double Elapsed => accumulated + (runningSince is { } r ? (DateTime.UtcNow - r).TotalSeconds : 0);

	readonly Grid body = new();
	readonly VerticalStackLayout[] views = new VerticalStackLayout[4];
	readonly Button[] tabs = new Button[4];
	int tab;
	readonly IDispatcherTimer ticker;

	readonly Label timeLabel = L("00:00:00", 54, true), courseInfo = L("", 14), syncLabel = L("", 12);
	readonly Picker picker = new() { TextColor = TextC, TitleColor = Muted, BackgroundColor = Panel, Title = "Ders seç" };
	Button startBtn = null!, finishBtn = null!;
	readonly VerticalStackLayout statsBox = new() { Spacing = 10 }, historyBox = new() { Spacing = 8 },
		courseBox = new() { Spacing = 8 }, accountBox = new() { Spacing = 10 };
	int persistCounter;
	bool refreshing;

	static Label L(string t, double size, bool bold = false, Color? c = null) => new()
	{ Text = t, FontSize = size, TextColor = c ?? TextC, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };

	static Button B(string t, Color? bg = null, Color? fg = null) => new()
	{ Text = t, BackgroundColor = bg ?? Accent, TextColor = fg ?? Bg, CornerRadius = 14, HeightRequest = 48, FontAttributes = FontAttributes.Bold };

	static Entry E(string ph, bool pw = false, Keyboard? kb = null) => new()
	{ Placeholder = ph, IsPassword = pw, Keyboard = kb ?? Keyboard.Default, TextColor = TextC, PlaceholderColor = Muted, BackgroundColor = Panel };

	public MainPage()
	{
		Store.Dir = FileSystem.AppDataDirectory;
		data = Store.Load();
		RestoreTimer();
		sync = new SyncService(data, SynchronizationContext.Current ?? new SynchronizationContext(),
			new AndroidSecretCodec(), () => MainThread.BeginInvokeOnMainThread(RefreshAll),
			id => Active && runCourseId == id);
		sync.StatusChanged += () => MainThread.BeginInvokeOnMainThread(() => { syncLabel.Text = sync.Status; RefreshAccount(); });

		BackgroundColor = Bg;

		views[0] = BuildTimerView();
		views[1] = new VerticalStackLayout { Spacing = 10, Padding = 20, Children = { L("İstatistik", 22, true), statsBox } };
		views[2] = new VerticalStackLayout { Spacing = 10, Padding = 20, Children = { L("Geçmiş", 22, true), historyBox } };
		views[3] = BuildSettingsView();
		foreach (var v in views) body.Children.Add(new ScrollView { Content = v, IsVisible = false });

		var bar = new Grid { BackgroundColor = Panel, ColumnDefinitions = { new(), new(), new(), new() }, Padding = new Thickness(6, 4) };
		string[] names = { "Süre", "İstatistik", "Geçmiş", "Ayarlar" };
		for (int i = 0; i < 4; i++)
		{
			int idx = i;
			tabs[i] = new Button { Text = names[i], BackgroundColor = Colors.Transparent, TextColor = Muted, FontSize = 13 };
			tabs[i].Clicked += (_, _) => ShowTab(idx);
			bar.Add(tabs[i], i, 0);
		}
		var root = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(0, 24, 0, 0) };
		root.Add(body, 0, 0); root.Add(bar, 0, 1);
		Content = root;

		ticker = Dispatcher.CreateTimer();
		ticker.Interval = TimeSpan.FromSeconds(1);
		ticker.Tick += (_, _) => Tick();
		ticker.Start();

		ShowTab(sync.SignedIn ? 0 : 3);
	}

	// ---------- görünümler ----------

	VerticalStackLayout BuildTimerView()
	{
		startBtn = B("Başlat"); startBtn.Clicked += (_, _) => ToggleTimer();
		finishBtn = B("Bitir", Panel, TextC); finishBtn.Clicked += (_, _) => Commit();
		picker.SelectedIndexChanged += (_, _) => { if (!refreshing) { if (Selected is { } c) LastCourse = c.Id; RefreshTimer(); } };
		timeLabel.HorizontalOptions = LayoutOptions.Center; courseInfo.HorizontalOptions = LayoutOptions.Center;
		courseInfo.TextColor = Muted; syncLabel.TextColor = Muted; syncLabel.HorizontalOptions = LayoutOptions.Center;
		return new VerticalStackLayout
		{
			Spacing = 16, Padding = 20,
			Children = { L("Flow State", 22, true), picker, new BoxView { HeightRequest = 30, Color = Colors.Transparent },
				timeLabel, courseInfo, startBtn, finishBtn, syncLabel }
		};
	}

	VerticalStackLayout BuildSettingsView()
	{
		var name = E("Yeni ders adı");
		var add = B("Ders ekle");
		add.Clicked += (_, _) =>
		{
			var n = name.Text?.Trim();
			if (string.IsNullOrEmpty(n) || data.Courses.Any(c => c.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase))) return;
			data.Courses.Add(new Course { Name = n, Color = Palette[data.Courses.Count % Palette.Length] });
			name.Text = ""; Changed();
		};
		return new VerticalStackLayout
		{
			Spacing = 12, Padding = 20,
			Children = { L("Ayarlar", 22, true), L("Hesap ve senkron", 15, true, Muted), accountBox,
				L("Dersler", 15, true, Muted), name, add, courseBox }
		};
	}

	void ShowTab(int i)
	{
		tab = i;
		for (int k = 0; k < 4; k++)
		{
			((View)body.Children[k]).IsVisible = k == i;
			tabs[k].TextColor = k == i ? Accent : Muted;
		}
		RefreshAll();
	}

	// ---------- yenileme ----------

	string? LastCourse
	{
		get => Preferences.Get("lastCourse", "");
		set => Preferences.Set("lastCourse", value ?? "");
	}

	void RefreshAll()
	{
		refreshing = true;
		picker.ItemsSource = data.Courses.Select(c => c.Name).ToList();
		var sel = runCourseId ?? LastCourse;
		var idx = data.Courses.FindIndex(c => c.Id == sel);
		picker.SelectedIndex = idx >= 0 ? idx : (data.Courses.Count > 0 ? 0 : -1);
		refreshing = false;
		RefreshTimer(); RefreshStats(); RefreshHistory(); RefreshCourses(); RefreshAccount();
		syncLabel.Text = sync.Status;
	}

	Course? Selected => picker.SelectedIndex >= 0 && picker.SelectedIndex < data.Courses.Count ? data.Courses[picker.SelectedIndex] : null;

	void RefreshTimer()
	{
		var s = (int)Elapsed;
		timeLabel.Text = $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
		courseInfo.Text = !Active ? (data.Courses.Count == 0 ? "Önce Ayarlar'dan ders ekle" : "Hazır")
			: runningSince != null ? "Çalışıyor…" : "Duraklatıldı";
		startBtn.Text = !Active ? "Başlat" : runningSince != null ? "Duraklat" : "Devam";
		finishBtn.IsEnabled = Active;
		picker.IsEnabled = !Active;
	}

	static string Fmt(double sec)
	{
		int s = (int)sec; return s >= 3600 ? $"{s / 3600} sa {s % 3600 / 60} dk" : s >= 60 ? $"{s / 60} dk" : $"{s} sn";
	}

	void RefreshStats()
	{
		statsBox.Children.Clear();
		var today = DateTime.Today;
		var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
		var monthStart = new DateTime(today.Year, today.Month, 1);
		double Sum(DateTime from) => data.Sessions.Where(s => s.Start >= from).Sum(s => (double)s.Seconds)
			+ (Active && runStart >= from ? Elapsed : 0);
		var todaySum = Sum(today);
		foreach (var (t, v) in new[] { ("Bugün", todaySum), ("Bu hafta", Sum(weekStart)), ("Bu ay", Sum(monthStart)), ("Toplam", Sum(DateTime.MinValue)) })
		{
			statsBox.Children.Add(new Border
			{
				BackgroundColor = Panel, StrokeThickness = 0, Padding = 16,
				StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
				Content = new VerticalStackLayout { Children = { L(t, 13, false, Muted), L(Fmt(v), 24, true) } }
			});
		}
		var goal = data.DailyGoalHours * 3600;
		if (goal > 0) statsBox.Children.Add(L($"Günlük hedef: %{Math.Min(100, (int)(todaySum / goal * 100))} ({data.DailyGoalHours:0.##} sa)", 13, false, Muted));
	}

	void RefreshHistory()
	{
		historyBox.Children.Clear();
		foreach (var s in data.Sessions.OrderByDescending(x => x.Start).Take(100))
		{
			var c = data.Courses.FirstOrDefault(x => x.Id == s.CourseId);
			var del = new Button { Text = "Sil", BackgroundColor = Colors.Transparent, TextColor = Danger, WidthRequest = 72 };
			var sess = s;
			del.Clicked += async (_, _) =>
			{
				if (await DisplayAlert("Sil", "Bu kayıt silinsin mi?", "Sil", "Vazgeç")) { data.DeleteSession(sess); Changed(); }
			};
			var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, BackgroundColor = Panel, Padding = new Thickness(14, 6) };
			row.Add(new VerticalStackLayout { Children = { L(c?.Name ?? "(silinmiş ders)", 15, true), L($"{s.Start.ToString("d MMM HH:mm", Tr)} · {Fmt(s.Seconds)}", 12, false, Muted) } }, 0, 0);
			row.Add(del, 1, 0);
			historyBox.Children.Add(row);
		}
		if (historyBox.Children.Count == 0) historyBox.Children.Add(L("Henüz kayıt yok.", 14, false, Muted));
	}

	void RefreshCourses()
	{
		courseBox.Children.Clear();
		foreach (var c in data.Courses.ToList())
		{
			var del = new Button { Text = "Sil", BackgroundColor = Colors.Transparent, TextColor = Danger, WidthRequest = 72 };
			var course = c;
			del.Clicked += async (_, _) =>
			{
				if (Active && runCourseId == course.Id) { await DisplayAlert("Ders", "Çalışan seans bu dersi kullanıyor.", "Tamam"); return; }
				if (await DisplayAlert("Ders sil", $"\"{course.Name}\" ve tüm kayıtları silinsin mi?", "Sil", "Vazgeç")) { data.DeleteCourse(course); Changed(); }
			};
			var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, BackgroundColor = Panel, Padding = new Thickness(14, 6) };
			row.Add(L(c.Name, 15, true, Color.FromArgb(c.Color)), 0, 0);
			row.Add(del, 1, 0);
			courseBox.Children.Add(row);
		}
	}

	// Giriş alanları yazılırken sync durumu değişirse form silinmesin
	Entry? mailBox, passBox; Label? authMsg;

	void RefreshAccount()
	{
		if (!sync.SignedIn && mailBox != null && accountBox.Children.Contains(mailBox))
		{ return; }
		accountBox.Children.Clear();
		if (sync.SignedIn)
		{
			mailBox = passBox = null;
			var outBtn = B("Çıkış yap", Panel, TextC);
			outBtn.Clicked += (_, _) => { sync.SignOut(); RefreshAccount(); };
			var nowBtn = B("Şimdi senkronla", Panel, TextC);
			nowBtn.Clicked += async (_, _) => await sync.SyncAsync();
			accountBox.Children.Add(L(sync.Email, 15, true));
			accountBox.Children.Add(L(sync.Status, 13, false, Muted));
			accountBox.Children.Add(nowBtn); accountBox.Children.Add(outBtn);
			return;
		}
		mailBox = E("E-posta", false, Keyboard.Email);
		passBox = E("Şifre", true);
		authMsg = L("", 13, false, Danger);
		var login = B("Giriş yap"); var reg = B("Kayıt ol", Panel, TextC);
		async Task Do(Func<string, string, Task<string?>> f)
		{
			authMsg!.Text = "Bekleyin…";
			var err = await f(mailBox!.Text?.Trim() ?? "", passBox!.Text ?? "");
			if (err != null) authMsg.Text = err;
			else { mailBox = null; RefreshAll(); ShowTab(0); await sync.SyncAsync(); }
		}
		login.Clicked += async (_, _) => await Do(sync.SignInAsync);
		reg.Clicked += async (_, _) => await Do(sync.SignUpAsync);
		accountBox.Children.Add(L("PC ile aynı hesapla giriş yap; veriler senkron olur. İstersen giriş yapmadan da kullanabilirsin.", 13, false, Muted));
		accountBox.Children.Add(mailBox); accountBox.Children.Add(passBox); accountBox.Children.Add(authMsg);
		accountBox.Children.Add(login); accountBox.Children.Add(reg);
	}

	void Changed()
	{
		Store.Save(data); sync.MarkDirty(); RefreshAll();
	}

	// ---------- zamanlayıcı ----------

	void ToggleTimer()
	{
		if (!Active)
		{
			var c = Selected; if (c == null) return;
			runCourseId = c.Id; runStart = DateTime.Now; accumulated = 0; runningSince = DateTime.UtcNow; LastCourse = c.Id;
		}
		else if (runningSince != null) { accumulated = Elapsed; runningSince = null; }
		else runningSince = DateTime.UtcNow;
		PersistTimer(); RefreshTimer();
	}

	void Commit()
	{
		if (!Active) return;
		int s = (int)Elapsed;
		if (s >= 5) data.Sessions.Add(new Session { CourseId = runCourseId!, Start = runStart, Seconds = s });
		runCourseId = null; runningSince = null; accumulated = 0;
		PersistTimer();
		Changed();
	}

	void PersistTimer()
	{
		if (!Active) { Preferences.Remove("run"); return; }
		Preferences.Set("run", $"{runCourseId}|{runStart.Ticks}|{accumulated.ToString(CultureInfo.InvariantCulture)}|{runningSince?.Ticks ?? 0}");
	}

	void RestoreTimer()
	{
		try
		{
			var p = Preferences.Get("run", "").Split('|');
			if (p.Length != 4 || !data.Courses.Any(c => c.Id == p[0])) { Preferences.Remove("run"); return; }
			runCourseId = p[0]; runStart = new DateTime(long.Parse(p[1])); accumulated = double.Parse(p[2], CultureInfo.InvariantCulture);
			var t = long.Parse(p[3]); runningSince = t == 0 ? null : new DateTime(t, DateTimeKind.Utc);
		}
		catch { runCourseId = null; runningSince = null; accumulated = 0; }
	}

	void Tick()
	{
		if (!Active) return;
		RefreshTimer();
		if (tab == 1) RefreshStats();
		if (++persistCounter >= 15) { persistCounter = 0; PersistTimer(); }
	}

	public void OnResumed() { RefreshAll(); _ = sync.SyncAsync(); }
	public void OnStopped() { PersistTimer(); Store.Save(data); }
}
