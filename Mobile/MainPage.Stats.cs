using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// İstatistik ve geçmiş sayfaları.
public partial class MainPage
{
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

		statsBox.Children.Add(Head("Özet"));
		var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) } };
		var items = new[] { ("Bugün", SecondsSince(PeriodFrom("today"))), ("Bu hafta", SecondsSince(PeriodFrom("week"))), ("Bu ay", SecondsSince(PeriodFrom("month"))), ("Toplam", SecondsSince(DateTime.MinValue)) };
		for (int i = 0; i < 4; i++)
			grid.Add(Card(new VerticalStackLayout { Spacing = 6, Children = { Lbl(items[i].Item1, 13, true, Muted), new Label { Text = Fmt(items[i].Item2), FontSize = 21, FontFamily = F9, TextColor = Text } } }), i % 2, i / 2);
		statsBox.Children.Add(grid);

		// seri
		statsBox.Children.Add(Head("Seri"));
		int streak = Streak(per);
		var sGrid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
		var sItems = new[] { ("Güncel", $"{streak} gün"), ("En uzun", $"{Math.Max(streak, StatsMath.LongestStreak(per))} gün"), ("Çalışılan", $"{StatsMath.ActiveDays(per)} gün") };
		for (int i = 0; i < 3; i++)
			sGrid.Add(Card(new VerticalStackLayout { Spacing = 4, Children = { Lbl(sItems[i].Item1, 12, false, Muted), Lbl(sItems[i].Item2, 16, true) } }, 12), i, 0);
		statsBox.Children.Add(sGrid);

		// ders bazında dağılım
		statsBox.Children.Add(Head("Ders bazında dağılım"));
		statsBox.Children.Add(Seg(new (string, string, string?)[] { ("today", "Bugün", null), ("week", "Hafta", null), ("month", "Ay", null), ("all", "Toplam", null) }, period, k => { period = k; RefreshStats(); }));
		var from = PeriodFrom(period);
		var rows = data.Courses.Select(c => (c, sec: SecondsSince(from, c.Id))).Where(x => x.sec > 0).OrderByDescending(x => x.sec).ToList();
		double total = rows.Sum(x => x.sec);
		if (rows.Count == 0) statsBox.Children.Add(Card(new VerticalStackLayout { Spacing = 8, Children = { Stand(Ico("chart", accent, 24), accentSoft, 52), Centered("Bu dönemde henüz kayıt yok", 15, true, Text), Centered("Bir ders seçip başla; derslerin burada renklensin.", 13, false, Muted) } }));
		foreach (var (c, sec) in rows)
		{
			var col = Parse(c.Color, accent);
			double share = sec / total * 100;
			var track = new Grid { HeightRequest = 8, ColumnDefinitions = { new(new GridLength(Math.Max(share, 0.001), GridUnitType.Star)), new(new GridLength(Math.Max(100 - share, 0.001), GridUnitType.Star)) } };
			track.Add(new Border { BackgroundColor = col, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } }, 0, 0);
			var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
			head.Add(Lbl(c.Name, 14, true), 0, 0); head.Add(Lbl($"{Fmt(sec)} · %{(int)share}", 13, true, Muted), 1, 0);
			statsBox.Children.Add(Card(new VerticalStackLayout { Spacing = 8, Children = { head, track } }, 14));
		}

		// son 14 gün
		statsBox.Children.Add(Head("Son 14 gün"));
		var days = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).ToList();
		double max = Math.Max(days.Max(d => per.GetValueOrDefault(d)), 1800);
		var chart = new Grid { HeightRequest = 190, ColumnSpacing = 3 };
		for (int i = 0; i < 14; i++)
		{
			chart.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			var d = days[i]; int sec = per.GetValueOrDefault(d); bool isToday = d == today;
			var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.End, Spacing = 3, HorizontalOptions = LayoutOptions.Fill };
			if (sec > 0) col.Children.Add(new Label { Text = Short(sec), FontSize = 8, TextColor = Muted, FontFamily = F7, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap });
			col.Children.Add(new Border
			{
				HeightRequest = sec > 0 ? Math.Max(sec / max * 120, 3) : 2, BackgroundColor = isToday ? accent : accentDim, StrokeThickness = 0,
				StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) }
			});
			col.Children.Add(new Label
			{
				Text = $"{Tr.DateTimeFormat.AbbreviatedDayNames[(int)d.DayOfWeek][..1]}\n{d.Day}", FontSize = 9, HorizontalTextAlignment = TextAlignment.Center,
				FontFamily = F7, TextColor = isToday ? accentDeep : Muted
			});
			chart.Add(col, i, 0);
		}
		statsBox.Children.Add(Card(chart, 12));

		// ısı haritası: sütun = hafta, satır = Pzt..Paz
		statsBox.Children.Add(Head("Son 18 hafta"));
		const int weeks = 18;
		const double goalSec = 2 * 3600; // ısı haritası yoğunluk referansı
		var heat = new Grid { RowSpacing = 3, ColumnSpacing = 3 };
		for (int r = 0; r < 7; r++) heat.RowDefinitions.Add(new RowDefinition(new GridLength(15)));
		for (int c = 0; c < weeks; c++) heat.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		var hStart = StatsMath.Monday(today).AddDays(-7 * (weeks - 1));
		for (int r = 0; r < 7; r++)
			for (int c = 0; c < weeks; c++)
			{
				var d = hStart.AddDays(c * 7 + r);
				if (d > today) continue;
				int lv = StatsMath.Level(per.GetValueOrDefault(d), goalSec);
				heat.Add(new Border
				{
					BackgroundColor = HeatColor(lv), StrokeThickness = d == today ? 1.5 : 0, Stroke = Text,
					StrokeShape = new RoundRectangle { CornerRadius = 3 }
				}, c, r);
			}
		var legend = new HorizontalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.End, Margin = new Thickness(0, 10, 0, 0) };
		legend.Children.Add(Lbl("az", 11, false, Muted));
		for (int l = 0; l <= 4; l++) legend.Children.Add(new Border { WidthRequest = 13, HeightRequest = 13, BackgroundColor = HeatColor(l), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 3 } });
		legend.Children.Add(Lbl("çok", 11, false, Muted));
		statsBox.Children.Add(Card(new VerticalStackLayout { Children = { heat, legend } }, 14));

		// haftalık toplam
		statsBox.Children.Add(Head("Haftalık toplam"));
		var wk = StatsMath.Weekly(per, 8);
		var thisMon = StatsMath.Monday(today);
		var wItems = new List<(string, int, bool)>();
		for (int i = 0; i < 8; i++)
		{
			var mon = thisMon.AddDays(-7 * (7 - i));
			wItems.Add(($"{mon.Day}\n{Tr.DateTimeFormat.AbbreviatedMonthNames[mon.Month - 1]}", wk[i], i == 7));
		}
		statsBox.Children.Add(Card(BarChart(wItems, true), 12));

		// günün saatleri
		statsBox.Children.Add(Head("Günün saatleri"));
		var sessions = data.Sessions.Select(s => (s.Start, s.Seconds)).ToList();
		if (Active) sessions.Add((runStart, (int)Elapsed));
		var hours = StatsMath.Hourly(sessions);
		var hItems = new List<(string, int, bool)>();
		for (int i = 0; i < 24; i++) hItems.Add((i % 3 == 0 ? i.ToString("00") : "", (int)hours[i], i == DateTime.Now.Hour));
		statsBox.Children.Add(Card(BarChart(hItems, false), 12));
	}

	Color HeatColor(int level) => level == 0 ? T("Track") : Soft(accent, level switch { 1 => 0.72, 2 => 0.48, 3 => 0.24, _ => 0 });

	static Label Centered(string t, double size, bool bold, Color c) { var l = Lbl(t, size, bold, c); l.HorizontalTextAlignment = TextAlignment.Center; return l; }

	// Ortak çubuk grafik: en yüksek değer ~120 dp'ye ölçeklenir.
	View BarChart(List<(string label, int sec, bool hi)> items, bool showValues)
	{
		double max = Math.Max(items.Max(x => x.sec), 1800);
		var chart = new Grid { HeightRequest = 180, ColumnSpacing = showValues ? 6 : 2 };
		for (int i = 0; i < items.Count; i++)
		{
			chart.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			var (label, sec, hi) = items[i];
			var col = new VerticalStackLayout { VerticalOptions = LayoutOptions.End, Spacing = 3, HorizontalOptions = LayoutOptions.Fill };
			if (showValues && sec > 0)
				col.Children.Add(new Label { Text = Short(sec), FontSize = 9, TextColor = Muted, FontFamily = F7, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap });
			col.Children.Add(new Border
			{
				HeightRequest = sec > 0 ? Math.Max(sec / max * 120, 3) : 2, BackgroundColor = hi ? accent : accentDim, StrokeThickness = 0,
				StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(4, 4, 0, 0) }
			});
			col.Children.Add(new Label
			{
				Text = label, FontSize = 9, HorizontalTextAlignment = TextAlignment.Center, FontFamily = F7, TextColor = hi ? accentDeep : Muted,
				HeightRequest = showValues ? 26 : 14, LineBreakMode = LineBreakMode.NoWrap
			});
			chart.Add(col, i, 0);
		}
		return chart;
	}

	// ---------- geçmiş ----------
	void RefreshHistory()
	{
		historyBox.Children.Clear();
		var list = data.Sessions.OrderByDescending(s => s.Start).Take(200).ToList();
		if (list.Count == 0) { historyBox.Children.Add(Card(new VerticalStackLayout { Spacing = 8, Children = { Stand(Ico("list", accent, 24), accentSoft, 52), Centered("Henüz kayıtlı çalışma yok", 15, true, Text), Centered("İlk oturumun burada görünecek.", 13, false, Muted) } })); return; }
		DateTime day = DateTime.MinValue;
		foreach (var s in list)
		{
			if (s.Start.Date != day)
			{
				day = s.Start.Date;
				var h = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
				h.Add(Head(day == DateTime.Today ? "Bugün" : day == DateTime.Today.AddDays(-1) ? "Dün" : day.ToString("d MMMM dddd", Tr)), 0, 0);
				var tot = Tag(Fmt(list.Where(x => x.Start.Date == day).Sum(x => x.Seconds)), Panel2, Muted); tot.VerticalOptions = LayoutOptions.End; tot.Margin = new Thickness(0, 0, 4, 8);
				h.Add(tot, 1, 0);
				historyBox.Children.Add(h);
			}
			var sess = s;
			var c = data.Courses.FirstOrDefault(x => x.Id == s.CourseId);
			var del = new Grid { Padding = new Thickness(12, 10), VerticalOptions = LayoutOptions.Center, Children = { Ico("trash", Faint, 18) } };
			Tap(del, async () =>
			{
				if (await DisplayAlert("Kaydı sil", "Bu çalışma kaydı silinsin mi?", "Sil", "Vazgeç")) { data.DeleteSession(sess); Changed(); }
			});
			var cc = Parse(c?.Color ?? "#8791B5", Muted);
			var dot = new Grid { WidthRequest = 36, HeightRequest = 36, VerticalOptions = LayoutOptions.Center, Children = { Dot(Soft(cc, 0.8), 36), Dot(cc, 12) } };
			dot.Children.OfType<View>().ToList().ForEach(x => x.HorizontalOptions = LayoutOptions.Center);
			var info = new VerticalStackLayout { Spacing = 2, Children = { Lbl(c?.Name ?? "(silinmiş ders)", 15, true), Lbl($"{s.Start:HH:mm} – {s.Start.AddSeconds(s.Seconds):HH:mm}", 12, true, Muted) } };
			var dur = Tag(Fmt(s.Seconds), accentSoft, accentDeep, 13);
			var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
			row.Add(dot, 0, 0); row.Add(info, 1, 0); row.Add(dur, 2, 0); row.Add(del, 3, 0);
			var card = Card(row, 0); card.Padding = new Thickness(14, 6, 4, 6);
			historyBox.Children.Add(card);
		}
	}

	// ---------- geçmiş araçları ----------
	View BuildHistoryTools()
	{
		// Elle kayıt bilinçli olarak yok: jetonlar yalnızca kronometreyle yapılan seanslardan gelir.
		var exp = Soft2("CSV olarak dışa aktar", ExportCsv, "down");
		return new HorizontalStackLayout { Margin = new Thickness(0, 10, 0, 12), Children = { exp } };
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
			var path = System.IO.Path.Combine(FileSystem.CacheDirectory, $"calisma-kayitlari-{DateTime.Today:yyyy-MM-dd}.csv");
			await File.WriteAllTextAsync(path, sb.ToString(), new System.Text.UTF8Encoding(true));
			await Share.Default.RequestAsync(new ShareFileRequest("Çalışma kayıtları", new ShareFile(path)));
		}
		catch (Exception ex) { await DisplayAlert("Dışa aktarılamadı", ex.Message, "Tamam"); }
	}
}
