using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// Dostlar: bahçedeki hayvanlar, nadir dostlar koleksiyonu, süsler; market (hayvan ve süs); alınca kutlama.
public partial class MainPage
{
	string zooPage = "garden";           // garden | shop | bought
	string? boughtId;
	VerticalStackLayout zooBox = null!;

	View BuildZooView()
	{
		zooBox = new VerticalStackLayout { Padding = new Thickness(20, 8, 20, 120) };
		return Scroll(zooBox);
	}

	void OpenMarket() { zooPage = "shop"; ShowTabKeep(ZooTab); }

	void ShowTabKeep(int i) { var p = zooPage; ShowTab(i); zooPage = p; RefreshZoo(); }

	/// <summary>Hayvanın piksel görseli (altın, yavru, siluet).</summary>
	PixelView AnimalPic(string id, double size, ZooPose pose, SpecialKind? kind = null, bool hidden = false)
	{
		var poses = scene.Poses(id);
		var img = ((int)pose < poses.Length ? poses[(int)pose] : null) ?? poses[0]!;
		if (kind == SpecialKind.Golden) img = PixelArt.Golden(img);
		if (hidden)
		{
			uint c = U(Faint);
			img = new PixImg(img.Px.Select(p => p >> 24 == 0 ? 0 : c).ToArray(), img.W, img.H);
		}
		return Pic(img, kind == SpecialKind.Baby ? size * 0.72 : size);
	}

	static PixImg DecorImg(string id) => id switch
	{ "fence" => PixelArt.Fence(), "flowers" => PixelArt.Flower(0), "sign" => PixelArt.Sign(), "campfire" => PixelArt.Campfire()[0], "tent" => PixelArt.Tent(), _ => PixelArt.SwingIcon() };

	View Price(int price) => new HorizontalStackLayout
	{ Spacing = 5, HorizontalOptions = LayoutOptions.Center, Children = { CoinPic(17), new Label { Text = price.ToString(), FontSize = 14, FontFamily = F8, TextColor = Text, VerticalOptions = LayoutOptions.Center } } };

	View ZooHead(string title, string sub, View action)
	{
		var g = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
		g.Add(new VerticalStackLayout { Children = { Big(title), Sub(sub) } }, 0, 0);
		action.VerticalOptions = LayoutOptions.Center;
		g.Add(action, 1, 0);
		var coin = new Border { BackgroundColor = T("WarnSoft"), StrokeThickness = 0, Padding = new Thickness(10, 6, 16, 6), Margin = new Thickness(0, 10, 0, 0), HorizontalOptions = LayoutOptions.Start, StrokeShape = new RoundRectangle { CornerRadius = 20 }, Content = new HorizontalStackLayout { Spacing = 8, Children = { CoinPic(26), new Label { Text = Zoo.Balance(data).ToString(), FontSize = 20, FontFamily = F9, TextColor = Text, VerticalOptions = LayoutOptions.Center } } } };
		return new VerticalStackLayout { Children = { g, coin } };
	}

	void RefreshZoo()
	{
		zooBox.Children.Clear();
		if (zooPage == "bought" && boughtId != null && ZooCatalog.Find(boughtId) is { } a) BuildBought(a);
		else if (zooPage == "shop") BuildShop();
		else BuildGarden();
	}

	void BuildGarden()
	{
		int bal = Zoo.Balance(data), can = Zoo.AffordableCount(data, "savanna", bal);
		var market = can > 0 ? Primary($"Market ({can})", () => { zooPage = "shop"; RefreshZoo(); }, "bag") : Soft2("Market", () => { zooPage = "shop"; RefreshZoo(); }, "bag");
		zooBox.Children.Add(ZooHead("Dostlar", "Adını değiştirmek için bir dostuna dokun.", market));

		// Bahçedekiler
		var owned = ZooCatalog.InMap("savanna").Where(x => Zoo.Owns(data, x.Id)).ToList();
		var body = new VerticalStackLayout { Spacing = 10, Children = { Lbl("Savana bahçende", 17, true) } };
		if (owned.Count == 0) body.Children.Add(Lbl("Henüz kimse yok. Marketten ilk dostunu al, bahçende dolaşmaya başlasın.", 13, false, Muted));
		else
		{
			var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
			foreach (var x in owned)
			{
				var id = x.Id;
				var tile = new VerticalStackLayout { WidthRequest = 92, Spacing = 2, Margin = new Thickness(0, 0, 4, 10), Children = { Stand(AnimalPic(id, 64, ZooPose.Happy), accentSoft, 74), Centered(ThemeTable.NameOf(data, id), 14, true, Text), Centered(x.Name, 11, true, Muted) } };
				Tap(tile, () => RenameAnimal(id));
				row.Children.Add(tile);
			}
			body.Children.Add(row);
		}
		zooBox.Children.Add(new Grid { HeightRequest = 14 });
		zooBox.Children.Add(Card(body));

		// Nadir dostlar
		if (owned.Count > 0)
		{
			var got = ZooSpecials.All(data).Select(x => x.Key).ToHashSet();
			var all = ZooCatalog.InMap("savanna").ToList();
			int have = all.Sum(x => (got.Contains(Special.Keyed(x.Id, SpecialKind.Golden)) ? 1 : 0) + (got.Contains(Special.Keyed(x.Id, SpecialKind.Baby)) ? 1 : 0));
			var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
			head.Add(Lbl("Nadir dostlar", 17, true), 0, 0); head.Add(Tag($"{have} / {all.Count * 2}", T("WarnSoft"), T("CoinInk"), 13), 1, 0);
			var col = new VerticalStackLayout { Spacing = 8, Children = { head, Lbl("Uzun oturumlar bazen bir sürpriz getirir: altın bir dost ya da minik bir yavru.", 13, false, Muted) } };
			foreach (var kind in new[] { SpecialKind.Golden, SpecialKind.Baby })
			{
				var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
				foreach (var x in all)
				{
					var key = Special.Keyed(x.Id, kind);
					bool on = got.Contains(key);
					var tile = new VerticalStackLayout { WidthRequest = 76, Spacing = 2, Margin = new Thickness(0, 0, 2, 8), Children = { Stand(AnimalPic(x.Id, 52, ZooPose.Stand, kind, !on), on ? (kind == SpecialKind.Golden ? T("WarnSoft") : accentSoft) : Panel2, 62), Centered(on ? ThemeTable.NameOf(data, key) : "???", 11, true, on ? Text : Faint) } };
					if (on) Tap(tile, () => RenameAnimal(key));
					row.Children.Add(tile);
				}
				col.Children.Add(row);
			}
			zooBox.Children.Add(new Grid { HeightRequest = 14 });
			zooBox.Children.Add(Card(col));
		}

		// Süsler
		int streak = Streak(PerDay());
		var deco = new VerticalStackLayout { Spacing = 8, Children = { Lbl("Bahçe süsleri", 17, true), Lbl($"Süsleri marketten alabilirsin. Seri ağacın: {StageNames[TreeStage(streak)]}.", 13, false, Muted) } };
		var dr = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
		foreach (var x in ZooCatalog.Decors)
		{
			bool on = Zoo.Owns(data, x.Key);
			var pic = Pic(DecorImg(x.Id), 48); pic.Opacity = on ? 1 : 0.4;
			var tile = new VerticalStackLayout { WidthRequest = 92, Spacing = 2, Margin = new Thickness(0, 0, 4, 10), Children = { Stand(pic, on ? T("GoodSoft") : Panel2, 70), Centered(x.Name, 13, true, on ? Text : Muted) } };
			if (on) tile.Children.Add(Centered("bahçende", 11, true, Good));
			else { tile.Children.Add(Price(x.Price)); Tap(tile, () => { zooPage = "shop"; RefreshZoo(); }); }
			dr.Children.Add(tile);
		}
		deco.Children.Add(dr);
		zooBox.Children.Add(new Grid { HeightRequest = 14 });
		zooBox.Children.Add(Card(deco));
	}

	void BuildShop()
	{
		int bal = Zoo.Balance(data);
		zooBox.Children.Add(ZooHead("Market", "Jetonlarınla yeni bir dost ya da süs al.", Soft2("Dostlar", () => { zooPage = "garden"; RefreshZoo(); }, "back")));
		var left = ZooCatalog.InMap("savanna").Where(a => !Zoo.Owns(data, a.Id)).OrderBy(a => a.Price).ToList();
		zooBox.Children.Add(Head("Dostlar"));
		if (left.Count == 0) zooBox.Children.Add(Card(Centered("Hepsi bahçende! Harikasın.", 15, true, Text)));
		var grid = Two();
		int i = 0;
		foreach (var a in left)
		{
			bool ok = bal >= a.Price;
			var id = a.Id;
			Put(grid, i++, ShopCard(AnimalPic(id, 88, ZooPose.Stand, null, !ok), a.Name, a.Price, ok, bal, 104, () =>
			{
				if (Zoo.Buy(data, id) != BuyResult.Ok) return;
				boughtId = id; arriving = id; zooPage = "bought"; Changed();
			}));
		}
		zooBox.Children.Add(grid);
		var decors = ZooCatalog.Decors.Where(x => !Zoo.Owns(data, x.Key)).OrderBy(x => x.Price).ToList();
		if (decors.Count == 0) return;
		zooBox.Children.Add(Head("Bahçe süsleri"));
		grid = Two(); i = 0;
		foreach (var x in decors)
		{
			bool ok = bal >= x.Price;
			var dx = x;
			var pic = Pic(DecorImg(x.Id), 58); pic.Opacity = ok ? 1 : 0.45;
			Put(grid, i++, ShopCard(pic, x.Name, x.Price, ok, bal, 84, () =>
			{
				if (Zoo.BuyDecor(data, dx) != BuyResult.Ok) return;
				zooPage = "garden"; Changed(); ShowTab(0);
			}));
		}
		zooBox.Children.Add(grid);

		static Grid Two() => new() { ColumnSpacing = 12, RowSpacing = 12, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
		static void Put(Grid g, int n, View v)
		{
			if (n % 2 == 0) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
			g.Add(v, n % 2, n / 2);
		}
	}

	View ShopCard(View pic, string name, int price, bool ok, int bal, double stand, Action buy)
	{
		var box = new VerticalStackLayout { Spacing = 8, Children = { Stand(pic, ok ? accentSoft : Panel2, stand), Centered(name, 16, true, Text), Price(price) } };
		if (!ok)
		{
			double f = Math.Clamp(bal / (double)price, 0.02, 1);
			var bar = new Grid { HeightRequest = 8, ColumnDefinitions = { new(new GridLength(f, GridUnitType.Star)), new(new GridLength(Math.Max(0.001, 1 - f), GridUnitType.Star)) } };
			var track = new Border { BackgroundColor = T("Track"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } };
			Grid.SetColumnSpan(track, 2);
			bar.Children.Add(track);
			bar.Add(new Border { BackgroundColor = T("Coin"), StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 4 } }, 0, 0);
			box.Children.Add(bar);
			box.Children.Add(Centered("Biraz daha odaklan", 12, true, Muted));
		}
		else box.Children.Add(Candy(Row(null, "Satın al", onAccent, 14), accent, () => { Buzz(); buy(); }, 42, 14, new Thickness(8, 0)));
		return Card(box, 14);
	}

	void BuildBought(Animal a)
	{
		var pic = AnimalPic(a.Id, 160, ZooPose.Happy);
		zooBox.Children.Add(new Grid { HeightRequest = 40 });
		var box = new VerticalStackLayout
		{
			Spacing = 12, Children =
			{
				Stand(pic, T("WarnSoft"), 200), Centered($"{ThemeTable.NameOf(data, a.Id)} yolda!", 30, true, Text), Centered("Yeni yuvasını görmek için sabırsızlanıyor.", 15, false, Muted),
				Soft2("Adını değiştir", () => RenameAnimal(a.Id), "pen"),
				Primary("Bahçeye götür", () => { zooPage = "garden"; boughtId = null; ShowTab(0); }, "paw")
			}
		};
		zooBox.Children.Add(Card(box, 26));
		pic.Scale = 0.3;
		_ = pic.ScaleTo(1, 700, Easing.SpringOut);
	}
}
