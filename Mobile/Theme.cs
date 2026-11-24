using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// Tema: renkler masaüstüyle ortak tablodan (ThemeTable) gelir. Renkler özellik olarak okunur; tema değişince arayüz yeniden kurulur.
public partial class MainPage
{
	static bool dark;

	static Color T(string key)
	{
		var (l, d) = ThemeTable.Colors[key];
		return Color.FromArgb(dark ? d : l);
	}

	static Color Text => T("Text");
	static Color Muted => T("Muted");
	static Color Faint => T("Faint");
	static Color Panel => T("Surface");
	static Color Panel2 => T("Panel2");
	static Color Danger => T("Danger");
	static Color DangerSoft => T("DangerSoft");
	static Color Good => T("Good");
	static readonly Color Cocoa = Color.FromArgb("#3B2C25");

	// Vurgu rengi ve türevleri (ApplyTheme hesaplar).
	Color accent = Color.FromArgb("#5B8CFF"), accentDim = Colors.Gray, accentSoft = Colors.Gray, accentDeep = Colors.Gray, onAccent = Colors.White;

	static Color Mix(Color a, Color b, double t) => Color.FromRgb(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t, a.Blue + (b.Blue - a.Blue) * t);
	static double Luma(Color c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
	static uint U(Color c) => (uint)Math.Round(c.Alpha * 255) << 24 | (uint)Math.Round(c.Red * 255) << 16 | (uint)Math.Round(c.Green * 255) << 8 | (uint)Math.Round(c.Blue * 255);
	static Color Parse(string hex, Color fallback) { try { return Color.FromArgb(hex); } catch { return fallback; } }

	/// <summary>Rengin kart üstünde duran yumuşak zemini.</summary>
	static Color Soft(Color c, double t) => dark ? Mix(Panel, c, Math.Max(0.28, 1 - t)) : Mix(c, Colors.White, t);

	void ApplyTheme()
	{
		dark = data.DarkTheme;
		accent = Parse(data.WidgetColor, Color.FromArgb("#5B8CFF"));
		accentDim = dark ? Mix(Panel, accent, 0.45) : Mix(accent, Colors.White, 0.58);
		accentSoft = dark ? Mix(Panel, accent, 0.26) : Mix(accent, Colors.White, 0.84);
		accentDeep = dark ? Mix(accent, Colors.White, 0.45) : Mix(accent, Colors.Black, 0.28);
		onAccent = Luma(accent) > 0.72 ? Cocoa : Colors.White;
	}

	static Brush PageBrush(Color accent) => new LinearGradientBrush(new GradientStopCollection
	{
		new GradientStop(Mix(T("BgA"), accent, 0.03), 0f), new GradientStop(Mix(T("BgB"), accent, 0.07), 1f)
	}, new Point(0, 0), new Point(1, 1));

	// ---------- yapı taşları ----------

	const string F6 = "N6", F7 = "N7", F8 = "N8", F9 = "N9";      // Nunito 600 / 700 / 800 / 900

	static Label Lbl(string t, double size, bool bold = false, Color? c = null) => new()
	{ Text = t, FontSize = size, TextColor = c ?? Text, FontFamily = bold ? F8 : F6 };

	static Label Big(string t) => new() { Text = t, FontSize = 30, TextColor = Text, FontFamily = F9 };
	static Label Sub(string t) => new() { Text = t, FontSize = 14, TextColor = Muted, FontFamily = F6 };
	static Label Head(string t) => new() { Text = t, FontSize = 17, TextColor = Text, FontFamily = F8, Margin = new Thickness(4, 22, 0, 8) };

	static Shadow SoftShadow => new() { Brush = new SolidColorBrush(T("Shadow")), Offset = new Point(0, 5), Radius = 16, Opacity = dark ? 0.35f : 0.14f };

	static Border Card(View content, double pad = 18) => new()
	{
		BackgroundColor = Panel, StrokeThickness = 0, Padding = pad, StrokeShape = new RoundRectangle { CornerRadius = 22 }, Content = content, Shadow = SoftShadow
	};

	static Border Tag(string text, Color bg, Color fg, double size = 12) => new()
	{
		BackgroundColor = bg, StrokeThickness = 0, Padding = new Thickness(10, 3), StrokeShape = new RoundRectangle { CornerRadius = 11 },
		HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center, Content = new Label { Text = text, FontSize = size, TextColor = fg, FontFamily = F8 }
	};

	static PixelView Pic(PixImg img, double size) => new() { Image = img, WidthRequest = size, HeightRequest = size, InputTransparent = true, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
	static PixelView Ico(string name, Color c, double size = 18) => Pic(PixelArt.Icon(name, U(c)), size);
	static PixelView CoinPic(double size) => Pic(PixelArt.Coin(), size);

	static void Tap(View v, Action a)
	{
		var g = new TapGestureRecognizer(); g.Tapped += (_, _) => a(); v.GestureRecognizers.Add(g);
	}

	/// <summary>Şeker düğme: altında koyu dudak, basınca yüz aşağı iner.</summary>
	static Grid Candy(View content, Color face, Action? click, double height = 46, double radius = 16, Thickness? pad = null)
	{
		var lip = new Border { BackgroundColor = Mix(face, Color.FromArgb("#2A1810"), 0.2), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = radius }, Margin = new Thickness(0, 4, 0, 0) };
		var top = new Border { BackgroundColor = face, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = radius }, Margin = new Thickness(0, 0, 0, 4), Padding = pad ?? new Thickness(18, 0), Content = content };
		var g = new Grid { HeightRequest = height, Children = { lip, top } };
		if (click != null)
			Tap(g, () =>
			{
				_ = Press();
				click();
			});
		return g;
		async Task Press() { try { await top.TranslateTo(0, 3, 50); await top.TranslateTo(0, 0, 110, Easing.SpringOut); } catch { } }
	}

	View Row(string? icon, string text, Color fg, double size = 15, double gap = 8)
	{
		var r = new HorizontalStackLayout { Spacing = gap, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true };
		if (icon != null) r.Children.Add(Ico(icon, fg, size + 2));
		r.Children.Add(new Label { Text = text, FontSize = size, TextColor = fg, FontFamily = F8, VerticalOptions = LayoutOptions.Center });
		return r;
	}

	Grid Primary(string text, Action click, string? icon = null) => Candy(Row(icon, text, onAccent), accent, click);
	Grid Soft2(string text, Action click, string? icon = null) => Candy(Row(icon, text, Text), Panel2, click);
	Grid Harm(string text, Action click, string? icon = null) => Candy(Row(icon, text, Danger), DangerSoft, click);

	/// <summary>Küçük seçenek hapları (dönem, mod, tema).</summary>
	View Seg(IEnumerable<(string Key, string Text, string? Icon)> items, string selected, Action<string> pick, Color? back = null)
	{
		var row = new HorizontalStackLayout { Spacing = 2 };
		foreach (var (key, text, icon) in items)
		{
			bool on = key == selected;
			var b = new Border { BackgroundColor = on ? accent : Colors.Transparent, StrokeThickness = 0, Padding = new Thickness(13, 8), StrokeShape = new RoundRectangle { CornerRadius = 17 }, Content = Row(icon, text, on ? onAccent : Muted, 13, 6) };
			Tap(b, () => { Buzz(); pick(key); });
			row.Children.Add(b);
		}
		return new Border { BackgroundColor = back ?? Panel, StrokeThickness = 0, Padding = 4, StrokeShape = new RoundRectangle { CornerRadius = 21 }, HorizontalOptions = LayoutOptions.Center, Content = row, Shadow = SoftShadow };
	}

	static Entry Field(string ph, bool pw = false, Keyboard? kb = null) => new()
	{ Placeholder = ph, IsPassword = pw, Keyboard = kb ?? Keyboard.Default, TextColor = Text, PlaceholderColor = Muted, FontFamily = F7, FontSize = 16 };

	/// <summary>Giriş alanının yuvarlak kutusu.</summary>
	static Border Boxed(View entry) => new()
	{ BackgroundColor = Panel2, StrokeThickness = 0, Padding = new Thickness(10, 0), StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = entry };

	static Border Dot(Color c, double size) => new()
	{
		WidthRequest = size, HeightRequest = size, StrokeThickness = 0, BackgroundColor = c, Padding = 0,
		StrokeShape = new RoundRectangle { CornerRadius = (float)(size / 2) }, VerticalOptions = LayoutOptions.Center
	};

	/// <summary>Yuvarlak pastel zeminde piksel görsel.</summary>
	static Grid Stand(View pic, Color bg, double size) => new()
	{ WidthRequest = size, HeightRequest = size, HorizontalOptions = LayoutOptions.Center, Children = { Dot(bg, size), pic } };

	static void Buzz() { try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); } catch { } }

	static string Fmt(double sec)
	{
		int s = (int)sec, h = s / 3600, m = s % 3600 / 60;
		return h > 0 ? $"{h} sa {m:00} dk" : m > 0 ? $"{m} dk" : $"{s} sn";
	}

	static string Short(int sec) => sec >= 3600 ? $"{sec / 3600}s{sec % 3600 / 60:00}" : sec >= 60 ? $"{sec / 60}dk" : $"{sec}sn";

	static ScrollView Scroll(View v) => new() { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
}
