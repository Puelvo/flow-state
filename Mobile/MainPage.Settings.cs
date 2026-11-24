using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// Ayarlar sayfası.
public partial class MainPage
{
	// ---------- ayarlar ----------
	View BuildSettingsView()
	{
		hexEntry = Field("#RRGGBB"); hexEntry.MaxLength = 7; hexEntry.HorizontalTextAlignment = TextAlignment.Center; hexEntry.WidthRequest = 120;
		hexEntry.Completed += (_, _) => ApplyHex();
		var hexRow = new HorizontalStackLayout { Spacing = 10, Children = { Lbl("Özel renk", 14, true, Muted), Boxed(hexEntry) } };
		((View)hexRow.Children[0]).VerticalOptions = LayoutOptions.Center;

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
			Padding = new Thickness(20, 8, 20, 120),
			Children =
			{
				Big("Ayarlar"), Sub("Uygulamayı kendine göre süsle."),
				Head("Tema rengi"),
				Card(new VerticalStackLayout { Spacing = 12, Children = { Lbl("Düğmeler, seçili öğeler ve widget bu rengi kullanır.", 13, false, Muted), swatchRow, hexRow } }),
				Head("Görünüm"),
				Card(new VerticalStackLayout { Spacing = 12, Children = { Lbl("Koyu temada sayfalar ve kartlar koyulaşır.", 13, false, Muted), themeBox } }),
				Head("Bahçe teması"),
				Card(new VerticalStackLayout { Spacing = 12, Children = { Lbl("Otomatik'te bahçe günün saatine göre değişir. İstersen bir vakti sabitle.", 13, false, Muted), gardenRow } }),
				Head("Dersler"),
				Card(new VerticalStackLayout { Spacing = 10, Children = { Boxed(courseName), addBtn, courseBox } }, 16),
				Head("Çalışırken"), Card(keepRow, 16),
				Head("Senkronizasyon"),
				Card(new VerticalStackLayout { Spacing = 10, Children = { Lbl("Telefon ve bilgisayardaki çalışmalar aynı hesapta birleşir. Veriler önce bu cihazda tutulur; internet yokken de çalışır.", 13, false, Muted), accountBox } }, 18),
				Head("Süreleri sıfırla"),
				Card(new VerticalStackLayout { Spacing = 10, Children = { Lbl("Seçtiğin dönemdeki tüm çalışma kayıtları silinir. Geri alınamaz.", 13, false, Muted), resets } }, 18)
			}
		});
	}

	void RefreshSettings()
	{
		swatchRow.Children.Clear();
		var cur = accent;
		foreach (var hex in ThemeTable.Swatches)
		{
			var h = hex; var col = Color.FromArgb(hex);
			bool on = Math.Abs(col.Red - cur.Red) + Math.Abs(col.Green - cur.Green) + Math.Abs(col.Blue - cur.Blue) < 0.01;
			var dot = new Border
			{
				WidthRequest = 40, HeightRequest = 40, BackgroundColor = col, Margin = new Thickness(0, 0, 10, 10),
				StrokeShape = new RoundRectangle { CornerRadius = 20 }, StrokeThickness = on ? 4 : 0,
				Stroke = Text
			};
			Tap(dot, () => SetColor(h)); swatchRow.Children.Add(dot);
		}
		BuildThemeRows();
		if (!hexEntry.IsFocused) hexEntry.Text = data.WidgetColor.ToUpperInvariant();

		courseBox.Children.Clear();
		foreach (var c in data.Courses.ToList())
		{
			var course = c;
			var del = new Grid { Padding = new Thickness(12, 8), VerticalOptions = LayoutOptions.Center, Children = { Ico("trash", Danger, 18) } };
			Tap(del, async () =>
			{
				if (Active && runCourseId == course.Id) { await DisplayAlert("Ders", "Çalışan seans bu dersi kullanıyor.", "Tamam"); return; }
				if (await DisplayAlert("Dersi sil", $"\"{course.Name}\" ve tüm kayıtları silinsin mi? Geri alınamaz.", "Sil", "Vazgeç")) { data.DeleteCourse(course); Changed(); }
			});
			var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(4, 0) };
			row.Add(Dot(Parse(c.Color, accent), 10), 0, 0);
			row.Add(Lbl(c.Name, 15, true), 1, 0); if (c.Id != Course.GeneralId) row.Add(del, 2, 0);
			courseBox.Children.Add(row);
		}
	}

	void SetColor(string hex)
	{
		data.WidgetColor = hex.ToUpperInvariant(); Store.Save(data); sync.MarkDirty(); Rebuild();
	}

	void ApplyHex()
	{
		var t = (hexEntry.Text ?? "").Trim();
		if (!t.StartsWith('#')) t = "#" + t;
		if (t.Length == 7 && t[1..].All(Uri.IsHexDigit)) SetColor(t);
		else hexEntry.Text = data.WidgetColor.ToUpperInvariant();
	}

	async void ResetPeriod(string key, string title)
	{
		var from = PeriodFrom(key);
		if (!await DisplayAlert(title, "Bu dönemdeki tüm çalışma kayıtları silinecek. Geri alınamaz.", "Sıfırla", "Vazgeç")) return;
		data.DeleteSessions(s => s.Start >= from);
		if (Active && runStart >= from) { runCourseId = null; runningSince = null; accumulated = 0; data.CountdownRunId = ""; PersistTimer(); data.SetLive(false, RunIdOrNew(), "", runStart, 0, null); sync.RequestLive(); SyncNotification(); }
		Changed();
	}

	// Giriş alanları yazılırken sync durumu değişirse form silinmesin
	Entry? mailBox, passBox; Label? authMsg;

	void RefreshAccount()
	{
		if (!sync.SignedIn && mailBox != null && accountBox.Children.Count > 1) return;
		accountBox.Children.Clear();
		if (sync.SignedIn)
		{
			mailBox = passBox = null;
			var outBtn = Pill("Çıkış yap", false, Danger); var nowBtn = Pill("Şimdi senkronla", false);
			Tap(outBtn, () => { sync.SignOut(); RefreshAccount(); });
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
		accountBox.Children.Add(Boxed(mailBox)); accountBox.Children.Add(Boxed(passBox)); accountBox.Children.Add(authMsg);
		accountBox.Children.Add(login); accountBox.Children.Add(reg);
	}

	/// <summary>Açık / koyu tema ve bahçe vakti seçenekleri.</summary>
	void BuildThemeRows()
	{
		themeBox.Children.Clear();
		themeBox.Children.Add(Seg(new (string, string, string?)[] { ("light", "Açık", "sun"), ("dark", "Koyu", "moon") }, data.DarkTheme ? "dark" : "light", k =>
		{
			data.DarkTheme = k == "dark"; Store.Save(data); Rebuild();
#if ANDROID
			TimerWidget.Refresh();
#endif
		}, Panel2));
		gardenRow.Children.Clear();
		foreach (var (key, name) in new[] { ("auto", "Otomatik"), ("dawn", "Şafak"), ("day", "Gündüz"), ("sunset", "Gün batımı"), ("night", "Gece") })
		{
			var k = key; bool on = DayCycle.Key(DayCycle.Parse(data.GardenTheme)) == key;
			var p = DayCycle.At(DateTime.Now, DayCycle.Parse(key));
			static Color A(uint c) => Color.FromRgba((int)(c >> 16 & 255), (int)(c >> 8 & 255), (int)(c & 255), 255);
			var pic = new Grid { WidthRequest = 84, HeightRequest = 58 };
			pic.Children.Add(new Border { StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 13 }, Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(A(p.SkyTop), 0f), new GradientStop(A(p.SkyBottom), 1f) }, new Point(0, 0), new Point(0, 1)) });
			pic.Children.Add(new Border { StrokeThickness = 0, HeightRequest = 17, VerticalOptions = LayoutOptions.End, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 0, 13, 13) }, BackgroundColor = A(PixelArt.Multiply(0xFF8DC963, PixelArt.LightMul(p.Light))) });
			var d = Dot(A(p.DiscB), 20); d.HorizontalOptions = LayoutOptions.Center; d.VerticalOptions = LayoutOptions.Start; d.Margin = new Thickness(0, 8, 0, 0);
			pic.Children.Add(d);
			var frame = new Border { Stroke = on ? accent : Colors.Transparent, StrokeThickness = 3, Padding = 3, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = pic, BackgroundColor = Colors.Transparent };
			var tile = new VerticalStackLayout { Spacing = 3, Margin = new Thickness(0, 0, 8, 8), Children = { frame, Centered(name, 12, true, on ? Text : Muted) } };
			Tap(tile, () => { data.GardenTheme = k; Store.Save(data); Buzz(); BuildThemeRows(); SyncNotification(); });
			gardenRow.Children.Add(tile);
		}
	}
}
