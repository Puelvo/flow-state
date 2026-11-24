using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// Çalış: canlı piksel bahçe (SceneView) tam ekran; üstte dersler, jeton ve market, güneşin yanında mod seçici, altta yalnızca düğmeler.
// Güneşe dokununca başlar/duraklar; hayvanlara dokunulur, çift dokununca adı değişir, basılı sürükleyince taşınır.
public partial class MainPage
{
#if ANDROID
	readonly PixelScene scene = new(Sprites.Load) { PathAt = 0.42 };
#else
	readonly PixelScene scene = new(_ => new PixImg(new uint[1], 1, 1)) { PathAt = 0.42 };
#endif
	SceneView sceneView = null!;
	SkyPalette scenePalette = DayCycle.Day;
	HorizontalStackLayout chips = null!, pips = null!;
	Grid modeHost = null!, startHost = null!, stopHost = null!;
	Label coinLabel = null!;
	Border coinPill = null!, pipsPill = null!;
	VerticalStackLayout topBox = null!, bottomBox = null!;
	string? arriving;                    // bahçeye ilk kez girecek hayvan

	View BuildWorkView()
	{
		sceneView = new SceneView { Scene = scene };
		scene.NameOf = k => ThemeTable.NameOf(data, k);
		sceneView.SunTapped = ToggleTimer;
		sceneView.AnimalDoubleTapped = k => RenameAnimal(k);
		sceneView.BeforeFrame = _ => SceneFrame();
		sceneView.Place = PlaceScene;
		sceneView.LabelBg = U(Panel); sceneView.LabelFg = U(Text); sceneView.LabelAccent = U(accent); sceneView.LabelOnAccent = U(onAccent);

		// Üst: dersler + jeton
		chips = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(16, 2) };
		var chipScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = chips };

		coinLabel = new Label { Text = "0", FontSize = 16, FontFamily = F9, TextColor = Text, VerticalOptions = LayoutOptions.Center };
		coinPill = new Border { BackgroundColor = Panel, StrokeThickness = 0, Padding = new Thickness(8, 6, 14, 6), StrokeShape = new RoundRectangle { CornerRadius = 19 }, Shadow = SoftShadow, Content = new HorizontalStackLayout { Spacing = 7, Children = { CoinPic(24), coinLabel } } };
		Tap(coinPill, OpenMarket);
		var market = Candy(Row("bag", "Market", onAccent, 13, 6), accent, OpenMarket, 38, 14, new Thickness(12, 0));
		logoLabel = new Label { Text = "Flow State", FontSize = 19, FontFamily = F9, VerticalOptions = LayoutOptions.Center };
		var logo = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { logoLabel } };
		var head = new Grid { Padding = new Thickness(16, 0), ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
		head.Add(logo, 0, 0); head.Add(coinPill, 1, 0); head.Add(market, 2, 0);
		topBox = new VerticalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Start, Padding = new Thickness(0, 12, 0, 0), Children = { head, chipScroll } };

		// Mod seçici gökyüzünde, güneşin sağındaki boşlukta (bahçeyi kapatmaz); altta yalnızca düğmeler
		pips = new HorizontalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.Center };
		pipsPill = new Border { BackgroundColor = T("Glass"), StrokeThickness = 0, Padding = new Thickness(10, 5), StrokeShape = new RoundRectangle { CornerRadius = 14 }, HorizontalOptions = LayoutOptions.Center, Content = pips, InputTransparent = true };
		modeHost = new Grid { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, WidthRequest = 60, Margin = new Thickness(0, 112, 6, 0) };
		startHost = new Grid(); stopHost = new Grid();
		var btns = new HorizontalStackLayout { Spacing = 16, HorizontalOptions = LayoutOptions.Center, Children = { startHost, stopHost } };
		bottomBox = new VerticalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.End, HorizontalOptions = LayoutOptions.Center, Padding = new Thickness(0, 0, 0, 76), Children = { btns } };

		var ui = new Grid { InputTransparent = true, CascadeInputTransparent = false, Children = { topBox, pipsPill, modeHost, bottomBox } };
		topBox.InputTransparent = bottomBox.InputTransparent = true;
		topBox.CascadeInputTransparent = bottomBox.CascadeInputTransparent = false;
		head.InputTransparent = btns.InputTransparent = logo.InputTransparent = true;
		head.CascadeInputTransparent = btns.CascadeInputTransparent = false;
		pipsPill.VerticalOptions = LayoutOptions.Start;
		return new Grid { Children = { sceneView, ui } };
	}

	Label logoLabel = null!;

	/// <summary>Güneş derslerin altında, ufuk güneşin hemen altında; hayvanlar düğmelerin önünde durmaz.</summary>
	(int, int, int) PlaceScene(int w, int h, double dp)
	{
		scene.DialZ = w >= 300 ? 2 : 1;
		int r = PixelArt.DialN * scene.DialZ / 2;
		double topDp = topBox.Height > 0 ? topBox.Height : 104;
		int sunY = (int)(topDp * dp) + r + 8;
		int horizon = Math.Clamp(sunY + r + 6, (int)(h * 0.34), Math.Max((int)(h * 0.34), h - (int)(260 * dp)));
		if (horizon < sunY + r) sunY = horizon - r - 2;
		if (bottomBox.Height > 0)
		{
			int bw = (int)(Math.Max(bottomBox.Width, 80) * dp), top = h - (int)(bottomBox.Height * dp);
			scene.SetAvoid(w / 2 - bw / 2, top, w / 2 + bw / 2);
		}
		pipsTop = (sunY + r + 4) / dp;
		modeTop = (sunY - r) / dp + 2;
		return (horizon, w / 2, sunY);
	}

	double pipsTop, modeTop;
	string sceneTone = "";
	DateTime paletteAt = DateTime.MinValue;

	/// <summary>Her kareden önce: vakit, ilerleme ve güneşin yazıları.</summary>
	void SceneFrame()
	{
		var now = DateTime.Now;
		var theme = DayCycle.Parse(data.GardenTheme);
		if ((now - paletteAt).TotalSeconds > 20 || sceneTone != data.GardenTheme)
		{
			paletteAt = now; sceneTone = data.GardenTheme;
			scenePalette = DayCycle.At(now, theme);
			scene.Palette = scenePalette;
			uint t = scenePalette.SkyTop;
			double l = (0.299 * (t >> 16 & 255) + 0.587 * (t >> 8 & 255) + 0.114 * (t & 255)) / 255;
			Dispatcher.Dispatch(() => { logoLabel.TextColor = l < 0.6 ? Colors.White : Cocoa; StylePage(); });       // çizimin içinde arayüz değiştirme
		}
		double el = Active ? Elapsed : 0;
		int target = Target;
		scene.Session = Active; scene.Running = Active && Running;
		scene.Lit = !Active ? 0 : target > 0 ? Math.Min(1, el / target) * 60 : el % 3600 / 60;
		int s = target > 0 ? Math.Max(0, target - (int)el) : !Active && data.Countdown ? data.CountdownMinutes * 60 : (int)el;
		sceneView.Time = $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
		sceneView.TimeAlpha = !Active ? 0.4f : Running ? 1f : 0.6f;
		if (pipsPill.IsVisible && Math.Abs(pipsPill.Margin.Top - pipsTop) > 1) Dispatcher.Dispatch(() => pipsPill.Margin = new Thickness(0, pipsTop, 0, 0));
		if (modeHost.IsVisible && Math.Abs(modeHost.Margin.Top - modeTop) > 1) Dispatcher.Dispatch(() => modeHost.Margin = new Thickness(0, modeTop, 6, 0));
	}

	Course? CurrentCourse
	{
		get
		{
			var id = runCourseId ?? pickedCourseId;
			return data.Courses.FirstOrDefault(c => c.Id == id) ?? data.Courses.FirstOrDefault();
		}
	}

	string workKey = "", gardenKey = "";

	void RefreshWork()
	{
		RefreshChips();
		RefreshTimer();
		RefreshCoins();
		RefreshGarden();
	}

	void RefreshChips()
	{
		chips.Children.Clear();
		foreach (var c in data.Courses)
		{
			var course = c;
			bool on = CurrentCourse?.Id == c.Id;
			var chip = new Border
			{
				Padding = new Thickness(14, 9), StrokeShape = new RoundRectangle { CornerRadius = 19 }, BackgroundColor = on ? accentSoft : Panel,
				Stroke = on ? accent : Colors.Transparent, StrokeThickness = 2, Opacity = Active && !on ? 0.5 : 1, Shadow = SoftShadow,
				Content = new HorizontalStackLayout { Spacing = 8, Children = { Dot(Parse(c.Color, accent), 11), Lbl(c.Name, 14, true) } }
			};
			Tap(chip, () =>
			{
				if (Active) return;
				pickedCourseId = course.Id; Preferences.Set("lastCourse", course.Id); Buzz(); RefreshWork();
			});
			chips.Children.Add(chip);
		}
		if (!Active)
		{
			var add = new Border { Padding = new Thickness(12, 9), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 19 }, BackgroundColor = T("Glass"), Content = Row("plus", "Ders ekle", Muted, 13, 6) };
			Tap(add, AddCoursePrompt);
			chips.Children.Add(add);
		}
	}

	async void AddCoursePrompt()
	{
		var n = (await DisplayPromptAsync("Yeni ders", "Dersin adı ne olsun?", "Ekle", "Vazgeç", "örn. Matematik", 40))?.Trim();
		if (string.IsNullOrEmpty(n) || data.Courses.Any(c => c.Name.Equals(n, StringComparison.CurrentCultureIgnoreCase))) return;
		var c = new Course { Name = n, Color = Palette[data.Courses.Count % Palette.Length] };
		data.Courses.Add(c); pickedCourseId = c.Id; Preferences.Set("lastCourse", c.Id); Changed();
	}

	void RefreshTimer()
	{
		UpdateKeepOn();
		var course = CurrentCourse;
		sceneView.Course = course?.Name ?? "Bir ders ekle";
		var (text, bg, fg, dot) = !Active ? (course == null ? "" : "Başlamaya hazır", Panel2, Muted, Good)
			: Running ? ("Odaklanıyorsun", accentSoft, accentDeep, accent) : ("Mola", T("WarnSoft"), T("CoinInk"), T("Warn"));
		sceneView.Status = text; sceneView.StatusBg = U(bg); sceneView.StatusFg = U(fg); sceneView.StatusDot = U(dot);

		// Düğmeler ve mod seçici yalnızca durum değişince kurulur.
		int laps = Active && Target == 0 ? (int)Elapsed / 3600 : -1;
		var key = $"{Active}|{Running}|{course != null}|{data.Countdown}|{data.CountdownMinutes}|{laps}|{dark}|{data.WidgetColor}|{startHost.GetHashCode()}";
		if (key == workKey) return;
		workKey = key;

		startHost.Children.Clear();
		startHost.Children.Add(PixButton(Active && Running ? "pause" : "play", accent, onAccent, 76, 60, ToggleTimer, course != null ? 1 : 0.35));
		stopHost.Children.Clear();
		stopHost.IsVisible = Active;
		stopHost.Children.Add(PixButton("stop", DangerSoft, Danger, 60, 60, Commit, 1));

		modeHost.Children.Clear();
		modeHost.IsVisible = !Active;
		modeHost.Children.Add(ModePill());

		pips.Children.Clear();
		for (int i = 0; i < Math.Min(laps, 10); i++) pips.Children.Add(Ico("star", T("Coin"), 18));
		if (laps > 10) pips.Children.Add(Lbl($"+{laps - 10}", 13, true, Text));
		if (laps >= 0) pips.Children.Add(Ico("staro", accent, 18));
		pipsPill.IsVisible = laps >= 0;
	}

	/// <summary>Dikey küçük hap: kronometre / geri sayım. Geri sayıma dokununca süre kadranı açılır.</summary>
	View ModePill()
	{
		View Item(string icon, string label, bool on, Action pick)
		{
			var fg = on ? onAccent : Muted;
			var b = new Border
			{
				BackgroundColor = on ? accent : Colors.Transparent, StrokeThickness = 0, HeightRequest = 46, Padding = 0, StrokeShape = new RoundRectangle { CornerRadius = 15 },
				Content = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, InputTransparent = true, Children = { Ico(icon, fg, 20), new Label { Text = label, FontSize = 9, FontFamily = F8, TextColor = fg, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap } } }
			};
			SemanticProperties.SetDescription(b, label);
			Tap(b, () => { Buzz(); pick(); });
			return b;
		}
		return new Border
		{
			BackgroundColor = T("Glass"), StrokeThickness = 0, Padding = 3, StrokeShape = new RoundRectangle { CornerRadius = 18 },
			Content = new VerticalStackLayout
			{
				Spacing = 2, Children =
				{
					Item("timer", "Kronometre", !data.Countdown, () => { data.Countdown = false; Store.Save(data); RefreshTimer(); }),
					Item("hourglass", data.Countdown ? $"{data.CountdownMinutes} dk" : "Geri sayım", data.Countdown, () => { data.Countdown = true; Store.Save(data); RefreshTimer(); OpenDial(); })
				}
			}
		};
	}

	/// <summary>Piksel şeker düğme (başlat, bitir).</summary>
	static View PixButton(string icon, Color face, Color fg, double w, double h, Action click, double opacity)
	{
		var pic = new PixelView { Image = PixelArt.Button((int)(w / 2), (int)(h / 2), U(face), BigIcon(icon, U(fg))), WidthRequest = w, HeightRequest = h, Opacity = opacity };
		var g = new Grid { WidthRequest = w, HeightRequest = h, Children = { pic } };
		Tap(g, () => { _ = Press(); click(); });
		return g;
		async Task Press() { try { await pic.ScaleTo(0.93, 60); await pic.ScaleTo(1, 130, Easing.SpringOut); } catch { } }
	}

	static PixImg BigIcon(string name, uint c) => PixelArt.Icon(name, c);

	void RefreshCoins()
	{
		var t = Zoo.Balance(data).ToString();
		if (coinLabel.Text == t) return;
		coinLabel.Text = t;
	}

	/// <summary>Bahçedeki hayvanlar, nadir dostlar, süsler ve seri ağacı.</summary>
	void RefreshGarden()
	{
		var per = PerDay();
		int streak = Streak(per);
		bool bare = false;
		if (streak == 0)
		{
			var d = DateTime.Today.AddDays(-1);
			for (int i = 0; i < 400 && per.GetValueOrDefault(d) < StatsMath.MinDaySeconds; i++) d = d.AddDays(-1);
			while (per.GetValueOrDefault(d) >= StatsMath.MinDaySeconds) { streak++; d = d.AddDays(-1); }
			bare = streak > 0;
		}
		int stage = TreeStage(streak);
		var key = $"{string.Join(",", data.OwnedAnimals.Select(o => o.AnimalId))}|{data.Sessions.Count}|{stage}|{bare}";
		if (key == gardenKey) return;
		gardenKey = key;
		scene.SetGarden(ZooCatalog.Animals.Where(a => Zoo.Owns(data, a.Id)).Select(a => a.Id), ZooSpecials.All(data),
			ZooCatalog.Decors.Where(x => Zoo.Owns(data, x.Key)).Select(x => x.Id), stage, bare, arriving);
		arriving = null;
	}

	static int TreeStage(int days) => days <= 0 ? 0 : days <= 2 ? 1 : days <= 6 ? 2 : days <= 13 ? 3 : days <= 29 ? 4 : 5;
	static readonly string[] StageNames = { "Tohum", "Filiz", "Fidan", "Genç ağaç", "Ağaç", "Meyveli ağaç" };

	async void RenameAnimal(string key)
	{
		var n = await DisplayPromptAsync("Adı ne olsun?", "Boş bırakırsan ilk adına döner.", "Kaydet", "Vazgeç", null, 16, null, ThemeTable.NameOf(data, key));
		if (n == null) return;
		n = n.Trim();
		if (n.Length == 0) data.AnimalNames.Remove(key); else data.AnimalNames[key] = n;
		Store.Save(data);
		if (tab == ZooTab) RefreshZoo();
	}

	// ---------- katman: kadran ve ödül ----------

	void ShowOverlay(View card, bool dismiss)
	{
		overlay.Children.Clear();
		var back = new BoxView { Color = Color.FromArgb("#70201820") };
		if (dismiss) Tap(back, CloseOverlay);
		overlay.Children.Add(back);
		overlay.Children.Add(card);
		card.HorizontalOptions = card.VerticalOptions = LayoutOptions.Center;
		overlay.IsVisible = true;
		card.Scale = 0.7; card.Opacity = 0;
		_ = card.ScaleTo(1, 320, Easing.SpringOut);
		_ = card.FadeTo(1, 160);
	}

	void CloseOverlay()
	{
		overlay.IsVisible = false;
		overlay.Children.Clear();
	}

	/// <summary>Geri sayım süresi: çevrilen kadran (5 dk adım, en fazla 2 saat) ve hızlı seçimler.</summary>
	void OpenDial()
	{
		var dial = new DialDrawable { Minutes = Math.Clamp(data.CountdownMinutes, 5, 120), Track = T("Track"), Accent = accent, Knob = Panel, Mark = Muted };
		var gv = new GraphicsView { Drawable = dial, WidthRequest = 236, HeightRequest = 236 };
		var num = new Label { FontSize = 50, FontFamily = F9, TextColor = Text, HorizontalTextAlignment = TextAlignment.Center };
		var unit = new Label { Text = "dakika", FontSize = 14, FontFamily = F8, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center };
		void Set(int m) { dial.Minutes = Math.Clamp(m, 5, 120); num.Text = dial.Minutes.ToString(); gv.Invalidate(); }
		void From(PointF p)
		{
			int old = dial.Minutes;
			Set(dial.FromPoint(p, 236));
			if (dial.Minutes != old) Buzz();
		}
		gv.StartInteraction += (_, e) => From(e.Touches[0]);
		gv.DragInteraction += (_, e) => From(e.Touches[0]);
		Set(dial.Minutes);

		var quick = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center };
		foreach (int m in new[] { 15, 25, 45, 60, 90 })
		{
			var mm = m;
			var b = new Border { BackgroundColor = Panel2, StrokeThickness = 0, Padding = new Thickness(13, 8), Margin = 3, StrokeShape = new RoundRectangle { CornerRadius = 15 }, Content = Lbl(m == 60 ? "1 sa" : $"{m} dk", 13, true) };
			Tap(b, () => { Buzz(); Set(mm); });
			quick.Children.Add(b);
		}
		var buttons = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
		buttons.Add(Soft2("Tamam", () => { data.CountdownMinutes = dial.Minutes; Store.Save(data); CloseOverlay(); RefreshTimer(); }), 0, 0);
		buttons.Add(Primary("Başla", () => { data.CountdownMinutes = dial.Minutes; Store.Save(data); CloseOverlay(); if (!Active) ToggleTimer(); }, "play"), 1, 0);
		var card = Card(new VerticalStackLayout
		{
			Spacing = 12, WidthRequest = 270,
			Children =
			{
				Centered("Ne kadar odaklanalım?", 20, true, Text), Centered("Tutamacı çevir; süre dolunca oturum biter.", 13, false, Muted),
				new Grid { HorizontalOptions = LayoutOptions.Center, Children = { gv, new VerticalStackLayout { InputTransparent = true, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { num, unit } } } },
				quick, buttons
			}
		}, 22);
		ShowOverlay(card, true);
	}

	sealed class DialDrawable : IDrawable
	{
		public int Minutes; public Color Track = Colors.Gray, Accent = Colors.Blue, Knob = Colors.White, Mark = Colors.Gray;

		public int FromPoint(PointF p, double size)
		{
			double c = size / 2, deg = Math.Atan2(p.X - c, c - p.Y) * 180 / Math.PI;
			if (deg < 0) deg += 360;
			int m = (int)Math.Round(deg / 360 * 120 / 5) * 5;
			// Tepeden atlamayı engelle: uç değere yapışır.
			if (Minutes >= 100 && m <= 20) m = 120;
			else if (Minutes <= 20 && m >= 100) m = 5;
			if (m == 0) m = Minutes > 60 ? 120 : 5;
			return m;
		}

		public void Draw(ICanvas canvas, RectF r)
		{
			float cx = r.Center.X, cy = r.Center.Y, rad = Math.Min(r.Width, r.Height) / 2 - 18;
			canvas.StrokeSize = 18; canvas.StrokeColor = Track; canvas.DrawCircle(cx, cy, rad);
			canvas.FillColor = Mark;
			for (int m = 0; m < 120; m += 5)
			{
				double a = m / 120.0 * 2 * Math.PI; float q = rad - 18;
				canvas.FillCircle(cx + q * (float)Math.Sin(a), cy - q * (float)Math.Cos(a), m % 15 == 0 ? 2.6f : 1.5f);
			}
			canvas.StrokeLineCap = LineCap.Round; canvas.StrokeColor = Accent;
			float sweep = Minutes / 120f * 360;
			if (sweep >= 359.9f) canvas.DrawCircle(cx, cy, rad);
			else canvas.DrawArc(cx - rad, cy - rad, rad * 2, rad * 2, 90, 90 - sweep, true, false);
			double ea = sweep * Math.PI / 180;
			float kx = cx + rad * (float)Math.Sin(ea), ky = cy - rad * (float)Math.Cos(ea);
			canvas.FillColor = Knob; canvas.FillCircle(kx, ky, 16);
			canvas.StrokeSize = 4; canvas.DrawCircle(kx, ky, 15);
			canvas.FillColor = Accent; canvas.FillCircle(kx, ky, 4);
		}
	}

	/// <summary>Oturum bitti: "+N jeton" kartı, varsa sürpriz dost; jetonlar yukarıdaki jeton hapına uçar.</summary>
	async void ShowReward(Session s, bool timeUp, Special? special)
	{
		int coins = Zoo.SessionCoins(s);
		var course = data.Courses.FirstOrDefault(c => c.Id == s.CourseId)?.Name ?? "";
		var tagView = Tag(timeUp ? "Süre doldu!" : "Oturum tamam!", accentSoft, accentDeep, 13); tagView.HorizontalOptions = LayoutOptions.Center;
		var box = new VerticalStackLayout { Spacing = 8, WidthRequest = 250, Children = { tagView } };
		Label? count = null;
		if (coins > 0)
		{
			count = new Label { Text = "+0", FontSize = 44, FontFamily = F9, TextColor = T("CoinInk"), VerticalOptions = LayoutOptions.Center };
			box.Children.Add(new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Center, Children = { CoinPic(48), count } });
			box.Children.Add(Centered("jeton kazandın", 15, true, Text));
		}
		else
		{
			box.Children.Add(Centered("Bu oturum jeton için biraz kısaydı", 16, true, Text));
			box.Children.Add(Centered("Biraz daha uzun odaklan, jetonlar gelsin.", 13, false, Muted));
		}
		box.Children.Add(Centered($"{course} · {Fmt(s.Seconds)}", 13, true, Muted));
		if (special != null && ZooCatalog.Find(special.AnimalId) is { } animal)
		{
			box.Children.Add(new BoxView { Color = T("Line"), HeightRequest = 2, Margin = new Thickness(0, 4) });
			box.Children.Add(Stand(AnimalPic(animal.Id, 64, ZooPose.Happy, special.Kind), special.Kind == SpecialKind.Golden ? T("WarnSoft") : accentSoft, 76));
			box.Children.Add(Centered(Special.Title(animal.Name, special.Kind), 17, true, Text));
			box.Children.Add(Centered("Sürpriz! Bahçene katıldı.", 13, true, T("CoinInk")));
		}
		var card = Card(box, 24);
		ShowOverlay(card, true);
		if (count != null)
			for (int i = 1; i <= 20; i++) { count.Text = $"+{coins * i / 20}"; await Task.Delay(40); }
		if (coins > 0 && tab == 0) FlyCoins(Math.Clamp(coins, 3, 12));
		await Task.Delay(special != null ? 3600 : 2200);
		if (overlay.Children.Contains(card)) CloseOverlay();
	}

	async void FlyCoins(int n)
	{
		var rnd = new Random();
		var all = new List<Task>();
		for (int i = 0; i < n; i++)
		{
			var c = CoinPic(26);
			c.HorizontalOptions = LayoutOptions.Center; c.VerticalOptions = LayoutOptions.Center;
			shell.Children.Add(c);
			double dx = Width / 2 - 150 + rnd.Next(-10, 10), dy = -Height / 2 + 40;
			int wait = i * 60;
			all.Add(Fly());
			async Task Fly()
			{
				await Task.Delay(wait);
				await c.TranslateTo(rnd.Next(-80, 80), rnd.Next(-60, 60), 220, Easing.CubicOut);
				await c.TranslateTo(dx, dy, 480, Easing.CubicIn);
				shell.Children.Remove(c);
			}
		}
		await Task.WhenAll(all);
		RefreshCoins();
		await coinPill.ScaleTo(1.18, 90); await coinPill.ScaleTo(1, 160, Easing.SpringOut);
	}
}
