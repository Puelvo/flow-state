using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

/// <summary>
/// Masaüstü Flow State'in mobil karşılığı ("Sevimli safari"): Çalış = canlı piksel bahçe ve güneş saat; diğer sayfalar krem (ya da koyu) zeminde kartlar.
/// Dosyalar: Work (Çalış), Zoo (Dostlar, market), Stats (istatistik, geçmiş), Social, Settings, Timer (zamanlayıcı), Theme (renkler ve yapı taşları).
/// </summary>
public partial class MainPage : ContentPage
{
	static readonly string[] Palette = { "#4F8CFF", "#F2994A", "#27AE60", "#BB6BD9", "#EB5757", "#2D9CDB", "#F2C94C", "#56CCF2" };
	static readonly CultureInfo Tr = new("tr-TR");

	// ---------- veri / zamanlayıcı ----------
	readonly AppData data;
	readonly SyncService sync;
	string? runCourseId; string runId = ""; DateTime runStart; double accumulated; DateTime? runningSince;
	bool Active => runCourseId != null;
	bool Running => runningSince != null;
	double Elapsed => accumulated + (runningSince is { } r ? (DateTime.UtcNow - r).TotalSeconds : 0);
	string? pickedCourseId;
	string period = "today";
	int persistCounter;

	// ---------- görünüm (tema değişince yeniden kurulur) ----------
	const int NavCount = 6, ZooTab = 3, SocialTab = 4, SettingsTab = 5;
	static readonly (string Name, string Icon)[] Tabs = { ("Çalış", "timer"), ("İstatistik", "chart"), ("Geçmiş", "list"), ("Dostlar", "paw"), ("Sosyal", "people"), ("Ayarlar", "sliders") };
	Grid body = null!, overlay = null!, shell = null!;
	View[] views = new View[NavCount];
	Border[] navItems = new Border[NavCount];
	VerticalStackLayout statsBox = null!, historyBox = null!, courseBox = null!, accountBox = null!, themeBox = null!;
	FlexLayout swatchRow = null!, gardenRow = null!;
	Entry hexEntry = null!;
	int tab;
	readonly IDispatcherTimer ticker;

	public MainPage()
	{
		Current = this;
		Store.Dir = FileSystem.AppDataDirectory;
		data = Store.Load();
		RestoreTimer();
		sync = new SyncService(data, SynchronizationContext.Current ?? new SynchronizationContext(),
			new AndroidSecretCodec(), () => MainThread.BeginInvokeOnMainThread(RefreshAll),
			id => Active && runCourseId == id, OnRemoteLive);
		sync.StatusChanged += () => MainThread.BeginInvokeOnMainThread(RefreshAccount);
		pickedCourseId = Preferences.Get("lastCourse", "");

		tab = sync.SignedIn || data.Courses.Any(c => c.Id != Course.GeneralId) ? 0 : SettingsTab;
		Rebuild();

		ticker = Dispatcher.CreateTimer();
		ticker.Interval = TimeSpan.FromSeconds(1);
		ticker.Tick += (_, _) => Tick();
		ticker.Start();
		SyncNotification();
	}

	/// <summary>Bütün arayüzü kurar (açılışta ve tema değişince).</summary>
	void Rebuild()
	{
		ApplyTheme();
		mailBox = passBox = null;
		statsBox = new() { Spacing = 10 }; historyBox = new() { Spacing = 8 }; courseBox = new() { Spacing = 8 }; accountBox = new() { Spacing = 10 }; themeBox = new();
		swatchRow = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap }; gardenRow = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };

		body = new Grid();
		views = new View[NavCount];
		views[0] = BuildWorkView();
		views[1] = Scroll(new VerticalStackLayout { Padding = new Thickness(20, 8, 20, 120), Children = { Big("İstatistikler"), Sub("Çalıştığın her dakika burada birikiyor."), statsBox } });
		views[2] = Scroll(new VerticalStackLayout { Padding = new Thickness(20, 8, 20, 120), Children = { Big("Geçmiş"), Sub("Her oturum küçük bir adım."), BuildHistoryTools(), historyBox } });
		views[ZooTab] = BuildZooView();
		views[SocialTab] = BuildSocialView();
		views[SettingsTab] = BuildSettingsView();
		foreach (var v in views) { v.IsVisible = false; body.Children.Add(v); }

		overlay = new Grid { IsVisible = false };
		shell = new Grid { Children = { body, BuildNav(), overlay } };
		Content = shell;
		ShowTab(tab);
	}

	/// <summary>Sekmeler: ortak bar yok; her sekme kendi hapında, altta yüzer.</summary>
	View BuildNav()
	{
		var row = new Grid { ColumnSpacing = 5, Margin = new Thickness(10, 0, 10, 12), VerticalOptions = LayoutOptions.End, CascadeInputTransparent = false, InputTransparent = true };
		for (int i = 0; i < NavCount; i++)
		{
			row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
			int idx = i;
			navItems[i] = new Border { StrokeThickness = 0, HeightRequest = 54, Padding = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Shadow = SoftShadow };
			Tap(navItems[i], () => { Buzz(); ShowTab(idx); });
			row.Add(navItems[i], i, 0);
		}
		return row;
	}

	void StyleNav(int i)
	{
		bool on = i == tab;
		var fg = on ? onAccent : Muted;
		navItems[i].BackgroundColor = on ? accent : T("Chip");
		var col = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, InputTransparent = true, Children = { Ico(Tabs[i].Icon, fg, 22), new Label { Text = Tabs[i].Name, FontSize = 10, FontFamily = F8, TextColor = fg, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap } } };
		if (i == SocialTab)
		{
			(socialBadge.Parent as Grid)?.Children.Remove(socialBadge);
			navItems[i].Content = new Grid { Children = { col, socialBadge } };
		}
		else navItems[i].Content = col;
	}

	void ShowTab(int i)
	{
		if (tab == ZooTab && i != ZooTab) zooPage = "garden";
		tab = i;
		for (int k = 0; k < NavCount; k++) { views[k].IsVisible = k == i; StyleNav(k); }
		sceneView.Playing = i == 0;
		StylePage();
		SocialVisible(i == SocialTab);
		RefreshAll();
	}

	/// <summary>Sayfa zemini: Çalış'ta gökyüzünün tepesi (durum çubuğunun arkası gökyüzü, gezinme çubuğunun arkası çimen olsun), diğerlerinde krem/koyu geçiş.</summary>
	void StylePage()
	{
		if (tab == 0)
		{
			static Color A(uint c) => Color.FromRgb((int)(c >> 16 & 255), (int)(c >> 8 & 255), (int)(c & 255));
			Color sky = A(scenePalette.SkyTop), grass = A(PixelArt.Multiply(0xFF8DC963, PixelArt.LightMul(scenePalette.Light)));
			Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(sky, 0f), new GradientStop(sky, 0.5f), new GradientStop(grass, 0.5f), new GradientStop(grass, 1f) }, new Point(0, 0), new Point(0, 1));
		}
		else Background = PageBrush(accent);
	}

	void RefreshAll()
	{
		RefreshWork();
		if (tab == 1) RefreshStats();
		if (tab == 2) RefreshHistory();
		if (tab == ZooTab) RefreshZoo();
		if (tab == SocialTab) RefreshSocial();
		if (tab == SettingsTab) { RefreshSettings(); RefreshAccount(); }
	}

	void Changed()
	{
		Store.Save(data); sync.MarkDirty(); RefreshAll();
#if ANDROID
		TimerWidget.Refresh();
#endif
	}

	// Eski adlar (Sosyal ve Ayarlar kullanır)
	Grid Pill(string text, bool filled, Color? fg = null) =>
		Candy(Row(null, text, fg ?? (filled ? onAccent : Text)), fg == Danger && !filled ? DangerSoft : filled ? accent : Panel2, null);
}
