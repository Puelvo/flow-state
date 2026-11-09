using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

/// <summary>Masaüstü Flow State tasarımının mobil karşılığı: koyu gradyan zemin, parlayan halka, hap sekmeler, kartlar.</summary>
public class MainPage : ContentPage
{
	// ---------- tema ----------
	static readonly Color Text = Color.FromArgb("#EEF1FF"), Muted = Color.FromArgb("#8791B5"),
		Panel = Color.FromRgba(13, 17, 38, 166), Panel2 = Color.FromRgba(28, 36, 71, 179), Danger = Color.FromArgb("#E58A8A");
	static readonly string[] Palette = { "#4F8CFF", "#F2994A", "#27AE60", "#BB6BD9", "#EB5757", "#2D9CDB", "#F2C94C", "#56CCF2" };
	static readonly string[] Swatches = { "#5B8CFF", "#FF5A6A", "#3DDC97", "#A78BFA", "#FFB04D", "#FF6FB5", "#2DD4D4", "#E5E7EB" };
	static readonly CultureInfo Tr = new("tr-TR");

	Color accent = Color.FromArgb("#5B8CFF"), accentDim = Colors.Gray, sel = Colors.Gray;

	static Color Tint(Color c, double k, int add) => Color.FromRgb(
		(int)Math.Min(255, c.Red * 255 * k + add), (int)Math.Min(255, c.Green * 255 * k + add), (int)Math.Min(255, c.Blue * 255 * k + add));
	static Color Lighten(Color c, double t) => Color.FromRgb(
		(int)(c.Red * 255 + (255 - c.Red * 255) * t), (int)(c.Green * 255 + (255 - c.Green * 255) * t), (int)(c.Blue * 255 + (255 - c.Blue * 255) * t));
	static Color Alpha(Color c, double a) => c.WithAlpha((float)a);

	// ---------- veri / zamanlayıcı ----------
	readonly AppData data;
	readonly SyncService sync;
	string? runCourseId; DateTime runStart; double accumulated; DateTime? runningSince;
	bool Active => runCourseId != null;
	bool Running => runningSince != null;
	double Elapsed => accumulated + (runningSince is { } r ? (DateTime.UtcNow - r).TotalSeconds : 0);
	string? pickedCourseId;
	string period = "today";
	int persistCounter;

	// ---------- görünüm öğeleri ----------
	readonly Grid root = new();
	readonly Grid body = new();
	readonly View[] views = new View[4];
	readonly Border[] navItems = new Border[4];
	readonly Label[] navLabels = new Label[4];
	int tab;
	readonly IDispatcherTimer ticker;

	readonly RingDrawable ring = new();
	readonly GraphicsView ringView = new() { WidthRequest = 270, HeightRequest = 270, HorizontalOptions = LayoutOptions.Center };
	readonly Label ringCourse = Lbl("Bir ders ekle", 22, true), ringStatus = Lbl("", 13, false, Muted), timeLabel = Lbl("00:00:00", 44, true),
		pipsLabel = Lbl("", 14, false, Color.FromArgb("#C9D0EE")), syncDot = Lbl("", 12, false, Muted);
	readonly HorizontalStackLayout chips = new() { Spacing = 8 };
	readonly HorizontalStackLayout pips = new() { Spacing = 8, HorizontalOptions = LayoutOptions.Center, HeightRequest = 14 };
	readonly IconDrawable startIcon = new(0), stopIcon = new(2);
	readonly GraphicsView startGv = new() { WidthRequest = 34, HeightRequest = 34, InputTransparent = true },
		stopGv = new() { WidthRequest = 34, HeightRequest = 34, InputTransparent = true };
	Border startBtn = null!, stopBtn = null!;
	readonly Border blob1 = new(), blob2 = new(), blob3 = new(), blob4 = new();
	readonly VerticalStackLayout statsBox = new() { Spacing = 10 }, historyBox = new() { Spacing = 8 },
		courseBox = new() { Spacing = 8 }, accountBox = new() { Spacing = 10 };
	readonly FlexLayout swatchRow = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
	Entry hexEntry = null!, goalEntry = null!;

	// ---------- yardımcılar ----------
	static Label Lbl(string t, double size, bool bold = false, Color? c = null) => new()
	{ Text = t, FontSize = size, TextColor = c ?? Text, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };

	static Label Head(string t) => new()
	{ Text = t, FontSize = 12, TextColor = Muted, FontAttributes = FontAttributes.Bold, CharacterSpacing = 1.5, Margin = new Thickness(2, 18, 0, 6) };

	static Border Card(View content, double pad = 16) => new()
	{
		BackgroundColor = Panel, StrokeThickness = 0, Padding = pad,
		StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = content
	};

	Border Pill(string text, bool filled, Color? fg = null)
	{
		var l = Lbl(text, 14, true, fg ?? (filled ? Color.FromArgb("#0A0E1A") : Text));
		l.HorizontalTextAlignment = TextAlignment.Center; l.VerticalTextAlignment = TextAlignment.Center;
		return new Border
		{
			BackgroundColor = filled ? accent : Panel2, StrokeThickness = 0, Padding = new Thickness(18, 0), HeightRequest = 46,
			StrokeShape = new RoundRectangle { CornerRadius = 14 }, Content = l
		};
	}

	static void Tap(View v, Action a)
	{
		var g = new TapGestureRecognizer(); g.Tapped += (_, _) => a(); v.GestureRecognizers.Add(g);
	}

	static Entry Field(string ph, bool pw = false, Keyboard? kb = null) => new()
	{ Placeholder = ph, IsPassword = pw, Keyboard = kb ?? Keyboard.Default, TextColor = Text, PlaceholderColor = Muted, BackgroundColor = Panel2 };

	static void Buzz() { try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { } }

	static string Fmt(double sec)
	{
		int s = (int)sec, h = s / 3600, m = s % 3600 / 60;
		return h > 0 ? $"{h} sa {m:00} dk" : m > 0 ? $"{m} dk" : $"{s} sn";
	}

	static string Short(int sec) => sec >= 3600 ? $"{sec / 3600}s{sec % 3600 / 60:00}" : sec >= 60 ? $"{sec / 60}dk" : $"{sec}sn";

	static Color Parse(string hex, Color fallback) { try { return Color.FromArgb(hex); } catch { return fallback; } }

	// ---------- kurulum ----------
	public MainPage()
	{
		Store.Dir = FileSystem.AppDataDirectory;
		data = Store.Load();
		RestoreTimer();
		sync = new SyncService(data, SynchronizationContext.Current ?? new SynchronizationContext(),
			new AndroidSecretCodec(), () => MainThread.BeginInvokeOnMainThread(RefreshAll),
			id => Active && runCourseId == id);
		sync.StatusChanged += () => MainThread.BeginInvokeOnMainThread(() => { RefreshAccount(); RefreshSyncDot(); });

		pickedCourseId = Preferences.Get("lastCourse", "");

		BuildBackground();
		views[0] = BuildTimerView();
		views[1] = Scroll(new VerticalStackLayout { Padding = new Thickness(20, 8, 20, 24), Children = { Lbl("İstatistikler", 26, true), statsBox } });
		views[2] = Scroll(new VerticalStackLayout { Padding = new Thickness(20, 8, 20, 24), Children = { Lbl("Geçmiş", 26, true), BuildHistoryTools(), historyBox } });
		views[3] = BuildSettingsView();
		foreach (var v in views) { v.IsVisible = false; body.Children.Add(v); }

		root.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) };
		var top = new Grid { Padding = new Thickness(24, 30, 24, 4), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
		top.Add(Lbl("Flow State", 16, true, Muted), 0, 0);
		top.Add(syncDot, 1, 0);
		root.Add(top, 0, 0); root.Add(body, 0, 1); root.Add(BuildNav(), 0, 2);
		Content = new Grid { IsClippedToBounds = true, Children = { blob1, blob2, blob3, blob4, root } };

		ticker = Dispatcher.CreateTimer();
		ticker.Interval = TimeSpan.FromSeconds(1);
		ticker.Tick += (_, _) => Tick();
		ticker.Start();

		ApplyTheme();
		ShowTab(sync.SignedIn || data.Courses.Count > 0 ? 0 : 3);
	}

	static ScrollView Scroll(View v) => new() { Content = v };

	void BuildBackground()
	{
		void B(Border b, double size, LayoutOptions h, LayoutOptions v, Thickness m)
		{
			b.WidthRequest = b.HeightRequest = size; b.HorizontalOptions = h; b.VerticalOptions = v; b.Margin = m;
			b.StrokeThickness = 0; b.StrokeShape = new RoundRectangle { CornerRadius = (float)(size / 2) }; b.InputTransparent = true;
		}
		B(blob1, 520, LayoutOptions.Start, LayoutOptions.Start, new Thickness(-210, -230, 0, 0));
		B(blob2, 480, LayoutOptions.End, LayoutOptions.End, new Thickness(0, 0, -200, -200));
		B(blob3, 420, LayoutOptions.End, LayoutOptions.Start, new Thickness(0, -150, -190, 0));
		B(blob4, 380, LayoutOptions.Start, LayoutOptions.End, new Thickness(-170, 0, 0, -150));
	}

	static RadialGradientBrush Glow(Color c, double a) => new(new[]
	{
		new GradientStop(Alpha(c, a), 0f), new GradientStop(Alpha(c, 0), 1f)
	}) { Center = new Point(0.5, 0.5), Radius = 0.5 };

	void ApplyTheme()
	{
		accent = Parse(data.WidgetColor, Color.FromArgb("#5B8CFF"));
		accentDim = Tint(accent, 0.42, 14);
		sel = Tint(accent, 0.30, 22);
		Background = new LinearGradientBrush(new GradientStopCollection
		{
			new GradientStop(Tint(accent, 0.09, 5), 0f), new GradientStop(Tint(accent, 0.20, 8), 1f)
		}, new Point(0, 0), new Point(1, 1));
		blob1.Background = Glow(accent, 0.44); blob2.Background = Glow(Lighten(accent, 0.35), 0.31);
		blob3.Background = Glow(accent, 0.47); blob4.Background = Glow(Tint(accent, 0.75, 0), 0.33);
		ring.Accent = accent; startIcon.Color = Color.FromArgb("#0A0E1A"); stopIcon.Color = Text;
		if (startBtn != null) startBtn.BackgroundColor = accent;
		for (int i = 0; i < 4; i++) StyleNav(i);
		ringView.Invalidate();
	}

	Border BuildNav()
	{
		string[] names = { "Çalış", "İstatistik", "Geçmiş", "Ayarlar" };
		var row = new Grid { ColumnSpacing = 2, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
		for (int i = 0; i < 4; i++)
		{
			int idx = i;
			navLabels[i] = Lbl(names[i], 13, true, Muted);
			navLabels[i].HorizontalOptions = LayoutOptions.Center; navLabels[i].VerticalOptions = LayoutOptions.Center;
			navItems[i] = new Border
			{
				BackgroundColor = Colors.Transparent, StrokeThickness = 0, HeightRequest = 42, Padding = new Thickness(4, 0),
				StrokeShape = new RoundRectangle { CornerRadius = 21 }, Content = navLabels[i]
			};
			Tap(navItems[i], () => ShowTab(idx));
			row.Add(navItems[i], i, 0);
		}
		return new Border
		{
			BackgroundColor = Color.FromRgba(11, 15, 32, 235), Stroke = Color.FromRgba(255, 255, 255, 34), StrokeThickness = 1,
			StrokeShape = new RoundRectangle { CornerRadius = 26 }, Padding = 4, Margin = new Thickness(18, 4, 18, 18), Content = row
		};
	}

	void StyleNav(int i)
	{
		if (navItems[i] == null) return;
		navItems[i].BackgroundColor = i == tab ? sel : Colors.Transparent;
		navLabels[i].TextColor = i == tab ? Text : Muted;
	}

	// ---------- çalış görünümü ----------
	View BuildTimerView()
	{
		ringView.Drawable = ring;
		ringCourse.HorizontalTextAlignment = TextAlignment.Center; ringCourse.MaxLines = 2; ringCourse.LineBreakMode = LineBreakMode.TailTruncation;
		ringStatus.HorizontalTextAlignment = TextAlignment.Center;
		var center = new VerticalStackLayout
		{
			Spacing = 4, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, WidthRequest = 170,
			InputTransparent = true, Children = { ringCourse, ringStatus }
		};
		var ringHost = new Grid { HorizontalOptions = LayoutOptions.Center, Children = { ringView, center } };
		Tap(ringHost, ToggleTimer);

		timeLabel.HorizontalTextAlignment = TextAlignment.Center; timeLabel.FontFamily = "OpenSansSemibold";
		pipsLabel.HorizontalTextAlignment = TextAlignment.Center;

		startGv.Drawable = startIcon; stopGv.Drawable = stopIcon;
		startBtn = new Border
		{
			WidthRequest = 84, HeightRequest = 84, StrokeThickness = 0, BackgroundColor = accent,
			StrokeShape = new RoundRectangle { CornerRadius = 42 }, Content = startGv, Padding = 25
		};
		stopBtn = new Border
		{
			WidthRequest = 84, HeightRequest = 84, StrokeThickness = 0, BackgroundColor = Panel2,
			StrokeShape = new RoundRectangle { CornerRadius = 42 }, Content = stopGv, Padding = 25
		};
		Tap(startBtn, ToggleTimer); Tap(stopBtn, Commit);
		var btns = new HorizontalStackLayout { Spacing = 20, HorizontalOptions = LayoutOptions.Center, Children = { startBtn, stopBtn } };

		var chipScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = chips };

		return Scroll(new VerticalStackLayout
		{
			Spacing = 10, Padding = new Thickness(0, 10, 0, 20),
			Children =
			{
				new Grid { Padding = new Thickness(16, 0), Children = { chipScroll } },
				new BoxView { HeightRequest = 6, Color = Colors.Transparent },
				ringHost, timeLabel, pips, pipsLabel,
				new BoxView { HeightRequest = 6, Color = Colors.Transparent }, btns
			}
		});
	}

	void RefreshChips()
	{
		chips.Children.Clear();
		foreach (var c in data.Courses)
		{
			var course = c;
			bool isSel = CurrentCourse?.Id == c.Id;
			var row = new HorizontalStackLayout
			{
				Spacing = 8,
				Children =
				{
					new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Parse(c.Color, accent), VerticalOptions = LayoutOptions.Center },
					Lbl(c.Name, 14)
				}
			};
			var chip = new Border
			{
				Padding = new Thickness(14, 9), StrokeShape = new RoundRectangle { CornerRadius = 20 },
				BackgroundColor = isSel ? sel : Panel, Stroke = isSel ? accent : Colors.Transparent, StrokeThickness = 1.5,
				Opacity = Active && !isSel ? 0.45 : 1, Content = row
			};
			Tap(chip, () =>
			{
				if (Active) return;
				pickedCourseId = course.Id; Preferences.Set("lastCourse", course.Id); Buzz(); RefreshChips(); RefreshTimer();
			});
			chips.Children.Add(chip);
		}
		if (data.Courses.Count == 0)
		{
			var add = Pill("＋ İlk dersini ekle", true);
			Tap(add, () => ShowTab(3));
			chips.Children.Add(add);
		}
	}

	Course? CurrentCourse
	{
		get
		{
			var id = runCourseId ?? pickedCourseId;
			return data.Courses.FirstOrDefault(c => c.Id == id) ?? data.Courses.FirstOrDefault();
		}
	}

	void RefreshTimer()
	{
		var s = (int)Elapsed;
		timeLabel.Text = $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
		var course = CurrentCourse;
		ringCourse.Text = course?.Name ?? "Bir ders ekle";
		ringStatus.Text = !Active ? (course == null ? "" : "Başlamaya hazır") : !Running ? "Duraklatıldı" : "Odaklanıyorsun";
		UpdateKeepOn();
		startIcon.Kind = Active && Running ? 1 : 0;
		startGv.Invalidate();
		startBtn.Opacity = course == null ? 0.35 : 1;
		stopBtn.Opacity = Active ? 1 : 0.3;
		ring.Frac = Active ? s % 3600 / 3600.0 : 0;
		ring.Glow = true;
		ringView.Invalidate();

		int laps = s / 3600;
		pips.Children.Clear();
		for (int i = 0; i < Math.Min(laps, 12); i++)
			pips.Children.Add(new BoxView { WidthRequest = 12, HeightRequest = 12, CornerRadius = 6, Color = accent });
		if (laps > 12) pips.Children.Add(Lbl($"+{laps - 12}", 13, false, Text));
		if (Active)
			pips.Children.Add(new Border { WidthRequest = 12, HeightRequest = 12, StrokeThickness = 1.5, Stroke = accent, BackgroundColor = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 6 } });
		pipsLabel.Text = !Active ? "" : laps == 0 ? "İlk saatin turu" : $"{laps} saat tamamlandı";
	}

	void RefreshSyncDot()
	{
		syncDot.Text = sync.SignedIn ? (sync.Status.Length > 28 ? sync.Status[..28] + "…" : sync.Status) : "Çevrimdışı";
	}

	// ---------- istatistik ----------
	DateTime PeriodFrom(string p)
	{
		var today = DateTime.Today;
		return p switch
		{
			"today" => today,
			"week" => today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
			"month" => new DateTime(today.Year, today.Month, 1),
			_ => DateTime.MinValue
		};
	}

	double SecondsSince(DateTime from, string? courseId = null)
	{
		double t = data.Sessions.Where(s => s.Start >= from && (courseId == null || s.CourseId == courseId)).Sum(s => (double)s.Seconds);
		if (Active && runStart >= from && (courseId == null || runCourseId == courseId)) t += Elapsed;
		return t;
	}

	Dictionary<DateTime, int> PerDay()
	{
		var r = new Dictionary<DateTime, int>();
		foreach (var s in data.Sessions) r[s.Start.Date] = r.GetValueOrDefault(s.Start.Date) + s.Seconds;
		if (Active) r[runStart.Date] = r.GetValueOrDefault(runStart.Date) + (int)Elapsed;
		return r;
	}

	static int Streak(Dictionary<DateTime, int> per)
	{
		var d = DateTime.Today;
		if (per.GetValueOrDefault(d) < 60) d = d.AddDays(-1);
		int n = 0;
		while (per.GetValueOrDefault(d) >= 60) { n++; d = d.AddDays(-1); }
		return n;
	}

	void RefreshStats()
	{
		statsBox.Children.Clear();
		var per = PerDay();
		var today = DateTime.Today;
		double todaySec = per.GetValueOrDefault(today);

		statsBox.Children.Add(Head("TÜM DERSLER"));
		var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) } };
		var items = new[] { ("Bugün", SecondsSince(PeriodFrom("today"))), ("Bu hafta", SecondsSince(PeriodFrom("week"))), ("Bu ay", SecondsSince(PeriodFrom("month"))), ("Toplam", SecondsSince(DateTime.MinValue)) };
		for (int i = 0; i < 4; i++)
			grid.Add(Card(new VerticalStackLayout { Spacing = 6, Children = { Lbl(items[i].Item1, 13, false, Muted), Lbl(Fmt(items[i].Item2), 21, true) } }), i % 2, i / 2);
		statsBox.Children.Add(grid);

		// hedef
		statsBox.Children.Add(Head("BUGÜNKÜ HEDEF"));
		double goal = Math.Max(0.1, data.DailyGoalHours) * 3600, pct = Math.Min(100, todaySec / goal * 100);
		var bar = new Grid { HeightRequest = 10, Margin = new Thickness(0, 12, 0, 0) };
		bar.Add(new Border { BackgroundColor = Panel2, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 5 } });
		var fill = new Grid { ColumnDefinitions = { new(new GridLength(Math.Max(pct, 0.001), GridUnitType.Star)), new(new GridLength(Math.Max(100 - pct, 0.001), GridUnitType.Star)) } };
		fill.Add(new Border { BackgroundColor = accent, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 5 } }, 0, 0);
		bar.Add(fill);
		statsBox.Children.Add(Card(new VerticalStackLayout
		{
			Children =
			{
				Lbl($"Bugün {Fmt(todaySec)} / {data.DailyGoalHours.ToString("0.##", Tr)} sa" + (pct >= 100 ? "  ✓ hedef tamam" : ""), 14),
				Lbl($"🔥 {Streak(per)} günlük seri", 13, false, Muted), bar
			}
		}, 18));

		// ders bazında dağılım
		statsBox.Children.Add(Head("DERS BAZINDA DAĞILIM"));
		var seg = new HorizontalStackLayout { Spacing = 4 };
		foreach (var (key, name) in new[] { ("today", "Bugün"), ("week", "Hafta"), ("month", "Ay"), ("all", "Toplam") })
		{
			var k = key; bool on = period == k;
			var l = Lbl(name, 13, true, on ? Text : Muted);
			var p = new Border { Padding = new Thickness(14, 7), StrokeThickness = 0, BackgroundColor = on ? sel : Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = l };
			Tap(p, () => { period = k; RefreshStats(); });
			seg.Children.Add(p);
		}
		statsBox.Children.Add(seg);
		var from = PeriodFrom(period);
		var rows = data.Courses.Select(c => (c, sec: SecondsSince(from, c.Id))).Where(x => x.sec > 0).OrderByDescending(x => x.sec).ToList();
		double total = rows.Sum(x => x.sec);
		if (rows.Count == 0) statsBox.Children.Add(Lbl("Bu dönemde kayıt yok.", 14, false, Muted));
		foreach (var (c, sec) in rows)
		{
			var col = Parse(c.Color, accent);
			double share = sec / total * 100;
			var track = new Grid { HeightRequest = 8, ColumnDefinitions = { new(new GridLength(Math.Max(share, 0.001), GridUnitType.Star)), new(new GridLength(Math.Max(100 - share, 0.001), GridUnitType.Star)) } };
			track.Add(new Border { BackgroundColor = col, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } }, 0, 0);
			var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
			head.Add(Lbl(c.Name, 14, true), 0, 0); head.Add(Lbl($"{Fmt(sec)} · %{(int)share}", 13, false, Muted), 1, 0);
			statsBox.Children.Add(Card(new VerticalStackLayout { Spacing = 8, Children = { head, track } }, 14));
		}

		// son 14 gün
		statsBox.Children.Add(Head("SON 14 GÜN"));
		var days = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).ToList();
		double max = Math.Max(days.Max(d => per.GetValueOrDefault(d)), 1800);
		var chart = new Grid { HeightRequest = 190, ColumnSpacing = 3 };
		for (int i = 0; i < 14; i++)
		{
			chart.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			var d = days[i]; int sec = per.GetValueOrDefault(d); bool isToday = d == today;
			var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.End, Spacing = 3, HorizontalOptions = LayoutOptions.Fill };
			if (sec > 0) col.Children.Add(new Label { Text = Short(sec), FontSize = 8, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap });
			col.Children.Add(new Border
			{
				HeightRequest = sec > 0 ? Math.Max(sec / max * 120, 3) : 2, BackgroundColor = isToday ? accent : accentDim, StrokeThickness = 0,
				StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) }
			});
			col.Children.Add(new Label
			{
				Text = $"{Tr.DateTimeFormat.AbbreviatedDayNames[(int)d.DayOfWeek][..1]}\n{d.Day}", FontSize = 9, HorizontalTextAlignment = TextAlignment.Center,
				TextColor = isToday ? Text : Muted
			});
			chart.Add(col, i, 0);
		}
		statsBox.Children.Add(Card(chart, 12));
	}

	// ---------- geçmiş ----------
	void RefreshHistory()
	{
		historyBox.Children.Clear();
		var list = data.Sessions.OrderByDescending(s => s.Start).Take(200).ToList();
		if (list.Count == 0) { historyBox.Children.Add(Lbl("Henüz kayıtlı çalışma yok.", 14, false, Muted)); return; }
		foreach (var s in list)
		{
			var sess = s;
			var c = data.Courses.FirstOrDefault(x => x.Id == s.CourseId);
			var del = Lbl("✕", 16, true, Muted); del.Padding = new Thickness(12, 8); del.VerticalOptions = LayoutOptions.Center;
			Tap(del, async () =>
			{
				if (await DisplayAlert("Kaydı sil", "Bu çalışma kaydı silinsin mi?", "Sil", "Vazgeç")) { data.DeleteSession(sess); Changed(); }
			});
			var dot = new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Parse(c?.Color ?? "#8791B5", Muted), VerticalOptions = LayoutOptions.Center };
			var info = new VerticalStackLayout { Spacing = 2, Children = { Lbl(c?.Name ?? "(silinmiş ders)", 15, true), Lbl(s.Start.ToString("d MMM yyyy HH:mm", Tr), 12, false, Muted) } };
			var dur = Lbl(Fmt(s.Seconds), 14, true); dur.VerticalOptions = LayoutOptions.Center;
			var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
			row.Add(dot, 0, 0); row.Add(info, 1, 0); row.Add(dur, 2, 0); row.Add(del, 3, 0);
			var card = Card(row, 0); card.Padding = new Thickness(14, 6, 4, 6);
			historyBox.Children.Add(card);
		}
	}

	// ---------- geçmiş araçları ----------
	View BuildHistoryTools()
	{
		var add = Pill("＋ Elle kayıt", true); var exp = Pill("CSV dışa aktar", false);
		Tap(add, AddManual); Tap(exp, ExportCsv);
		add.Margin = new Thickness(0, 0, 8, 0);
		return new HorizontalStackLayout { Margin = new Thickness(0, 10, 0, 12), Children = { add, exp } };
	}

	async void AddManual()
	{
		if (data.Courses.Count == 0) { await DisplayAlert("Kayıt", "Önce Ayarlar'dan bir ders ekle.", "Tamam"); return; }
		var names = data.Courses.Select(c => c.Name).ToArray();
		var pick = names.Length == 1 ? names[0] : await DisplayActionSheet("Hangi ders?", "Vazgeç", null, names);
		var course = data.Courses.FirstOrDefault(c => c.Name == pick);
		if (course == null) return;
		var txt = await DisplayPromptAsync("Elle kayıt", $"{course.Name} için kaç dakika çalıştın?", "Ekle", "Vazgeç", "örn. 45", 4, Keyboard.Numeric);
		if (!int.TryParse(txt, out var min) || min < 1 || min > 1440) return;
		data.Sessions.Add(new Session { CourseId = course.Id, Start = DateTime.Now.AddMinutes(-min), Seconds = min * 60 });
		Changed();
	}

	async void ExportCsv()
	{
		try
		{
			var inv = CultureInfo.InvariantCulture;
			var sb = new System.Text.StringBuilder("Tarih;Başlangıç;Ders;Süre (dk)\r\n");
			foreach (var s in data.Sessions.OrderBy(x => x.Start))
			{
				var name = (data.Courses.FirstOrDefault(c => c.Id == s.CourseId)?.Name ?? "").Replace(';', ',');
				sb.Append(s.Start.ToString("yyyy-MM-dd", inv)).Append(';').Append(s.Start.ToString("HH:mm", inv)).Append(';')
				  .Append(name).Append(';').Append(Math.Round(s.Seconds / 60.0, 1).ToString(Tr)).Append("\r\n");
			}
			var path = Path.Combine(FileSystem.CacheDirectory, $"calisma-kayitlari-{DateTime.Today:yyyy-MM-dd}.csv");
			await File.WriteAllTextAsync(path, sb.ToString(), new System.Text.UTF8Encoding(true));
			await Share.Default.RequestAsync(new ShareFileRequest("Çalışma kayıtları", new ShareFile(path)));
		}
		catch (Exception ex) { await DisplayAlert("Dışa aktarılamadı", ex.Message, "Tamam"); }
	}

	bool keepOnApplied;
	void UpdateKeepOn()
	{
		bool want = Preferences.Get("keepOn", false) && Active && Running;
		if (want == keepOnApplied) return;
		keepOnApplied = want;
		try { DeviceDisplay.Current.KeepScreenOn = want; } catch { }
	}

	// ---------- ayarlar ----------
	View BuildSettingsView()
	{
		hexEntry = Field("#RRGGBB"); hexEntry.MaxLength = 7; hexEntry.HorizontalTextAlignment = TextAlignment.Center; hexEntry.WidthRequest = 120;
		hexEntry.Completed += (_, _) => ApplyHex();
		var hexRow = new HorizontalStackLayout { Spacing = 10, Children = { Lbl("Özel renk:", 14, false, Muted), hexEntry } };
		hexRow.Children[0].VerticalOptions = LayoutOptions.Center;

		goalEntry = Field("2", false, Keyboard.Numeric); goalEntry.WidthRequest = 90; goalEntry.HorizontalTextAlignment = TextAlignment.Center;
		goalEntry.Completed += (_, _) => ApplyGoal(); goalEntry.Unfocused += (_, _) => ApplyGoal();
		var goalRow = new HorizontalStackLayout { Spacing = 10, Children = { Lbl("Günlük hedef:", 14, false, Muted), goalEntry, Lbl("saat", 14, false, Muted) } };
		goalRow.Children[0].VerticalOptions = goalRow.Children[2].VerticalOptions = LayoutOptions.Center;

		var keepSwitch = new Switch { IsToggled = Preferences.Get("keepOn", false), OnColor = accent, VerticalOptions = LayoutOptions.Center };
		keepSwitch.Toggled += (_, e) => { Preferences.Set("keepOn", e.Value); UpdateKeepOn(); };
		var keepRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 12 };
		keepRow.Add(new VerticalStackLayout { Spacing = 2, Children = { Lbl("Ekranı açık tut", 15, true), Lbl("Zamanlayıcı çalışırken ekran kapanmasın.", 12, false, Muted) } }, 0, 0);
		keepRow.Add(keepSwitch, 1, 0);

		var courseName = Field("Yeni ders adı"); courseName.MaxLength = 40;
		var addBtn = Pill("Ders ekle", true);
		void Add()
		{
			var n = courseName.Text?.Trim();
			if (string.IsNullOrEmpty(n) || data.Courses.Any(c => c.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase))) return;
			var c = new Course { Name = n, Color = Palette[data.Courses.Count % Palette.Length] };
			data.Courses.Add(c); pickedCourseId = c.Id; courseName.Text = ""; Changed();
		}
		Tap(addBtn, Add); courseName.Completed += (_, _) => Add();

		var resets = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
		foreach (var (key, name) in new[] { ("today", "Bugünü sıfırla"), ("week", "Bu haftayı sıfırla"), ("month", "Bu ayı sıfırla") })
		{
			var k = key; var b = Pill(name, false, Danger); b.Margin = new Thickness(0, 0, 8, 8);
			Tap(b, () => ResetPeriod(k, name)); resets.Children.Add(b);
		}

		return Scroll(new VerticalStackLayout
		{
			Padding = new Thickness(20, 8, 20, 24),
			Children =
			{
				Lbl("Ayarlar", 26, true),
				Head("TEMA RENGİ"),
				Card(new VerticalStackLayout { Spacing = 12, Children = { Lbl("Halka, düğmeler, seçili öğeler ve arka plan ışığı bu rengi kullanır.", 13, false, Muted), swatchRow, hexRow } }, 18),
				Head("DERSLER"),
				Card(new VerticalStackLayout { Spacing = 10, Children = { courseName, addBtn, courseBox } }, 16),
				Head("HEDEF"), Card(goalRow, 16),
				Head("ÇALIŞIRKEN"), Card(keepRow, 16),
				Head("SENKRONİZASYON"),
				Card(new VerticalStackLayout { Spacing = 10, Children = { Lbl("Telefon ve bilgisayardaki çalışmalar aynı hesapta birleşir. Veriler önce bu cihazda tutulur; internet yokken de çalışır.", 13, false, Muted), accountBox } }, 18),
				Head("SÜRELERİ SIFIRLA"),
				Card(new VerticalStackLayout { Spacing = 10, Children = { Lbl("Seçtiğin dönemdeki tüm çalışma kayıtları silinir. Geri alınamaz.", 13, false, Muted), resets } }, 18)
			}
		});
	}

	void RefreshSettings()
	{
		swatchRow.Children.Clear();
		var cur = accent;
		foreach (var hex in Swatches)
		{
			var h = hex; var col = Color.FromArgb(hex);
			bool on = Math.Abs(col.Red - cur.Red) + Math.Abs(col.Green - cur.Green) + Math.Abs(col.Blue - cur.Blue) < 0.01;
			var dot = new Border
			{
				WidthRequest = 40, HeightRequest = 40, BackgroundColor = col, Margin = new Thickness(0, 0, 10, 10),
				StrokeShape = new RoundRectangle { CornerRadius = 20 }, StrokeThickness = on ? 3 : 1,
				Stroke = on ? Colors.White : Color.FromRgba(255, 255, 255, 85)
			};
			Tap(dot, () => SetColor(h)); swatchRow.Children.Add(dot);
		}
		if (!hexEntry.IsFocused) hexEntry.Text = data.WidgetColor.ToUpperInvariant();
		if (!goalEntry.IsFocused) goalEntry.Text = data.DailyGoalHours.ToString("0.##", Tr);

		courseBox.Children.Clear();
		foreach (var c in data.Courses.ToList())
		{
			var course = c;
			var del = Lbl("Sil", 14, true, Danger); del.Padding = new Thickness(12, 8); del.VerticalOptions = LayoutOptions.Center;
			Tap(del, async () =>
			{
				if (Active && runCourseId == course.Id) { await DisplayAlert("Ders", "Çalışan seans bu dersi kullanıyor.", "Tamam"); return; }
				if (await DisplayAlert("Dersi sil", $"\"{course.Name}\" ve tüm kayıtları silinsin mi? Geri alınamaz.", "Sil", "Vazgeç")) { data.DeleteCourse(course); Changed(); }
			});
			var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(4, 0) };
			row.Add(new BoxView { WidthRequest = 10, HeightRequest = 10, CornerRadius = 5, Color = Parse(c.Color, accent), VerticalOptions = LayoutOptions.Center }, 0, 0);
			row.Add(Lbl(c.Name, 15, true), 1, 0); row.Add(del, 2, 0);
			courseBox.Children.Add(row);
		}
	}

	void SetColor(string hex)
	{
		data.WidgetColor = hex.ToUpperInvariant(); Store.Save(data); sync.MarkDirty(); ApplyTheme(); RefreshAll();
	}

	void ApplyHex()
	{
		var t = (hexEntry.Text ?? "").Trim();
		if (!t.StartsWith('#')) t = "#" + t;
		if (t.Length == 7 && t[1..].All(Uri.IsHexDigit)) SetColor(t);
		else hexEntry.Text = data.WidgetColor.ToUpperInvariant();
	}

	void ApplyGoal()
	{
		if (double.TryParse((goalEntry.Text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g > 0 && g <= 24
			&& Math.Abs(g - data.DailyGoalHours) > 0.001)
		{ data.DailyGoalHours = g; Store.Save(data); sync.MarkDirty(); }
		goalEntry.Text = data.DailyGoalHours.ToString("0.##", Tr);
	}

	async void ResetPeriod(string key, string title)
	{
		var from = PeriodFrom(key);
		if (!await DisplayAlert(title, "Bu dönemdeki tüm çalışma kayıtları silinecek. Geri alınamaz.", "Sıfırla", "Vazgeç")) return;
		data.DeleteSessions(s => s.Start >= from);
		if (Active && runStart >= from) { runCourseId = null; runningSince = null; accumulated = 0; PersistTimer(); }
		Changed();
	}

	// Giriş alanları yazılırken sync durumu değişirse form silinmesin
	Entry? mailBox, passBox; Label? authMsg;

	void RefreshAccount()
	{
		if (!sync.SignedIn && mailBox != null && accountBox.Children.Contains(mailBox)) return;
		accountBox.Children.Clear();
		if (sync.SignedIn)
		{
			mailBox = passBox = null;
			var outBtn = Pill("Çıkış yap", false, Danger); var nowBtn = Pill("Şimdi senkronla", false);
			Tap(outBtn, () => { sync.SignOut(); RefreshAccount(); RefreshSyncDot(); });
			Tap(nowBtn, async () => await sync.SyncAsync());
			accountBox.Children.Add(Lbl(sync.Email, 15, true));
			accountBox.Children.Add(Lbl(sync.Status, 13, false, accent));
			accountBox.Children.Add(nowBtn); accountBox.Children.Add(outBtn);
			return;
		}
		mailBox = Field("E-posta", false, Keyboard.Email);
		passBox = Field("Şifre", true);
		authMsg = Lbl("", 13, false, Danger);
		var login = Pill("Giriş yap", true); var reg = Pill("Kayıt ol", false);
		async Task Do(Func<string, string, Task<string?>> f)
		{
			authMsg!.Text = "Bekleyin…";
			var err = await f(mailBox!.Text?.Trim() ?? "", passBox!.Text ?? "");
			if (err != null) authMsg.Text = err;
			else { mailBox = null; RefreshAll(); ShowTab(0); await sync.SyncAsync(); }
		}
		Tap(login, async () => await Do(sync.SignInAsync)); Tap(reg, async () => await Do(sync.SignUpAsync));
		accountBox.Children.Add(Lbl("PC ile aynı hesapla giriş yap; veriler senkron olur. İstersen giriş yapmadan da kullanabilirsin.", 13, false, Muted));
		accountBox.Children.Add(mailBox); accountBox.Children.Add(passBox); accountBox.Children.Add(authMsg);
		accountBox.Children.Add(login); accountBox.Children.Add(reg);
	}

	// ---------- gezinme / yenileme ----------
	void ShowTab(int i)
	{
		tab = i;
		for (int k = 0; k < 4; k++) { views[k].IsVisible = k == i; StyleNav(k); }
		RefreshAll();
	}

	void RefreshAll()
	{
		RefreshChips(); RefreshTimer(); RefreshSyncDot();
		if (tab == 1) RefreshStats();
		if (tab == 2) RefreshHistory();
		if (tab == 3) { RefreshSettings(); RefreshAccount(); }
	}

	void Changed() { Store.Save(data); sync.MarkDirty(); RefreshAll(); }

	// ---------- zamanlayıcı ----------
	void ToggleTimer()
	{
		Buzz();
		if (!Active)
		{
			var c = CurrentCourse; if (c == null) return;
			runCourseId = c.Id; runStart = DateTime.Now; accumulated = 0; runningSince = DateTime.UtcNow; pickedCourseId = c.Id;
			Preferences.Set("lastCourse", c.Id);
		}
		else if (Running) { accumulated = Elapsed; runningSince = null; }
		else runningSince = DateTime.UtcNow;
		PersistTimer(); RefreshChips(); RefreshTimer();
	}

	void Commit()
	{
		if (!Active) return;
		Buzz();
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

	// ---------- çizimler ----------
	sealed class RingDrawable : IDrawable
	{
		public Color Accent = Colors.CornflowerBlue; public double Frac; public bool Glow = true;

		public void Draw(ICanvas canvas, RectF r)
		{
			float cx = r.Center.X, cy = r.Center.Y, rad = Math.Min(r.Width, r.Height) / 2 - 18;
			var box = new RectF(cx - rad, cy - rad, rad * 2, rad * 2);
			canvas.StrokeLineCap = LineCap.Round;
			canvas.StrokeColor = Color.FromRgba(255, 255, 255, 48);
			canvas.StrokeSize = 18;
			canvas.DrawCircle(cx, cy, rad);
			if (Frac < 0.002) return;
			float sweep = (float)(Math.Min(Frac, 0.9999) * 360);
			float end = 90 - sweep; if (end < 0) end += 360;
			if (Glow)
			{
				canvas.StrokeColor = Accent.WithAlpha(0.18f); canvas.StrokeSize = 34;
				canvas.DrawArc(box.X, box.Y, box.Width, box.Height, 90, end, true, false);
				canvas.StrokeColor = Accent.WithAlpha(0.28f); canvas.StrokeSize = 26;
				canvas.DrawArc(box.X, box.Y, box.Width, box.Height, 90, end, true, false);
			}
			canvas.StrokeColor = Accent; canvas.StrokeSize = 18;
			canvas.DrawArc(box.X, box.Y, box.Width, box.Height, 90, end, true, false);
		}
	}

	sealed class IconDrawable : IDrawable
	{
		public int Kind; public Color Color = Colors.White; // 0 oynat, 1 duraklat, 2 durdur
		public IconDrawable(int kind) => Kind = kind;

		public void Draw(ICanvas canvas, RectF r)
		{
			canvas.FillColor = Color; canvas.StrokeColor = Color;
			float w = r.Width, h = r.Height;
			switch (Kind)
			{
				case 0:
					var p = new PathF(); p.MoveTo(w * 0.22f, h * 0.08f); p.LineTo(w * 0.92f, h * 0.5f); p.LineTo(w * 0.22f, h * 0.92f); p.Close();
					canvas.StrokeSize = 5; canvas.StrokeLineJoin = LineJoin.Round; canvas.FillPath(p); canvas.DrawPath(p); break;
				case 1:
					canvas.FillRoundedRectangle(w * 0.14f, h * 0.06f, w * 0.26f, h * 0.88f, 4);
					canvas.FillRoundedRectangle(w * 0.60f, h * 0.06f, w * 0.26f, h * 0.88f, 4); break;
				default:
					canvas.FillRoundedRectangle(w * 0.1f, h * 0.1f, w * 0.8f, h * 0.8f, 7); break;
			}
		}
	}
}
