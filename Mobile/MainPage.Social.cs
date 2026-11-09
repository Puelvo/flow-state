using Microsoft.Maui.Controls.Shapes;

namespace FlowState.Mobile;

// Sosyal sekmesi: arkadaşlar (canlı durum) ve odalar (lider tablosu). Masaüstündeki Sosyal sekmesinin karşılığı.
public partial class MainPage
{
	static readonly string[] Medals = { "", "🏆", "🥈", "🥉" };
	static readonly (int days, string label)[] BoardPeriods = { (1, "Bugün"), (3, "3 gün"), (5, "5 gün"), (7, "1 hafta"), (14, "2 hafta"), (30, "1 ay") };
	static readonly Color Green = Color.FromArgb("#3DDC97"), Amber = Color.FromArgb("#FFB04D"), Offline = Color.FromArgb("#5A6385");

	SocialService? social;
	MyProfile? socialMe;
	bool socialBusy, socialAgain, socialMsgErr, settingShare;
	string socialPane = "friends"; // friends | rooms | board
	string? openRoomId, gateKind, boardHeadKey;
	int boardDays = 7;
	Board? board;
	readonly List<Action> socialTicks = new();
	IDispatcherTimer? socialPoll, socialClock;

	readonly Label socialMsg = Lbl("", 13, false), meLabel = Lbl("", 17, true);
	readonly VerticalStackLayout socialGate = new() { Spacing = 10 }, socialMain = new(),
		friendsPane = new(), friendsList = new() { Spacing = 8 },
		roomsPane = new(), roomsList = new() { Spacing = 8 },
		boardPane = new(), boardHead = new() { Spacing = 8 }, boardList = new() { Spacing = 8 }, boardInvite = new() { Spacing = 8 };
	readonly Switch shareSwitch = new();
	readonly Border[] segs = new Border[2];
	Entry addFriendEntry = null!;

	// ---------- kurulum ----------

	View BuildSocialView()
	{
		social = new SocialService(sync);
		socialMsg.IsVisible = false; socialMsg.Margin = new Thickness(2, 4, 0, 0);
		socialMain.IsVisible = false;

		// segment: Arkadaşlar | Odalar
		var segRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 10, 0, 4), ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) } };
		string[] names = { "Arkadaşlar", "Odalar" };
		for (int i = 0; i < 2; i++)
		{
			int idx = i;
			var l = Lbl(names[i], 14, true); l.HorizontalOptions = LayoutOptions.Center; l.VerticalOptions = LayoutOptions.Center;
			segs[i] = new Border { HeightRequest = 40, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 20 }, Content = l };
			Tap(segs[i], () => { openRoomId = null; SetPane(idx == 0 ? "friends" : "rooms"); });
			segRow.Add(segs[i], i, 0);
		}
		socialMain.Children.Add(segRow);

		// --- arkadaşlar ---
		shareSwitch.OnColor = accent;
		shareSwitch.Toggled += (_, e) =>
		{
			if (settingShare || socialMe == null) return;
			var name = socialMe.Username; bool share = e.Value;
			SocialDo(async () => { await social!.SetProfileAsync(name, share); socialMe.ShareStatus = share; });
		};
		var meGrid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
		meGrid.Add(new VerticalStackLayout { Spacing = 2, Children = { meLabel, Lbl("Arkadaşların seni bu adla ekler.", 12, false, Muted) } }, 0, 0);
		meGrid.Add(shareSwitch, 1, 0);
		var shareHint = Lbl("Durumumu arkadaşlarım görsün", 13, false, Muted);
		friendsPane.Children.Add(Card(new VerticalStackLayout { Spacing = 8, Children = { meGrid, shareHint } }, 16));

		addFriendEntry = Field("arkadaşının kullanıcı adı"); addFriendEntry.MaxLength = 20; addFriendEntry.Keyboard = Keyboard.Plain;
		var addBtn = Pill("Arkadaş ekle", true);
		Tap(addBtn, AddFriend);
		addFriendEntry.Completed += (_, _) => AddFriend();
		friendsPane.Children.Add(new VerticalStackLayout { Spacing = 8, Margin = new Thickness(0, 12, 0, 0), Children = { Card(addFriendEntry, 6), addBtn } });
		friendsPane.Children.Add(friendsList);
		socialMain.Children.Add(friendsPane);

		// --- odalar ---
		var create = Pill("＋ Oda kur", true); var join = Pill("Koda katıl", false);
		Tap(create, CreateRoom); Tap(join, JoinRoom);
		roomsPane.Children.Add(new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 4, 0, 0), Children = { create, join } });
		roomsPane.Children.Add(roomsList);
		roomsPane.IsVisible = false;
		socialMain.Children.Add(roomsPane);

		// --- oda / lider tablosu ---
		boardPane.Children.Add(boardHead); boardPane.Children.Add(boardList); boardPane.Children.Add(boardInvite);
		boardPane.IsVisible = false;
		socialMain.Children.Add(boardPane);

		socialPoll = Dispatcher.CreateTimer(); socialPoll.Interval = TimeSpan.FromSeconds(8);
		socialPoll.Tick += (_, _) => _ = SocialRefresh();
		socialClock = Dispatcher.CreateTimer(); socialClock.Interval = TimeSpan.FromSeconds(1);
		socialClock.Tick += (_, _) => { foreach (var t in socialTicks.ToList()) t(); };
		StyleSegs();

		pendingTimer = Dispatcher.CreateTimer(); pendingTimer.Interval = TimeSpan.FromSeconds(45);
		pendingTimer.Tick += (_, _) => _ = CheckPending();
		pendingTimer.Start();
		Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(6), () => _ = CheckPending());

		return Scroll(new VerticalStackLayout
		{
			Padding = new Thickness(20, 8, 20, 24),
			Children = { Lbl("Sosyal", 26, true), socialMsg, socialGate, socialMain }
		});
	}

	void StyleSegs()
	{
		for (int i = 0; i < 2; i++)
		{
			bool on = (i == 0) == (socialPane == "friends");
			if (segs[i] == null) continue;
			segs[i].BackgroundColor = on ? sel : Panel2;
			((Label)segs[i].Content).TextColor = on ? Text : Muted;
		}
	}

	void SetPane(string p)
	{
		socialPane = p;
		friendsPane.IsVisible = p == "friends";
		roomsPane.IsVisible = p == "rooms";
		boardPane.IsVisible = p == "board";
		StyleSegs();
		SocialMsg(null);
		_ = SocialRefresh();
	}

	/// <summary>Sekme açılıp kapanınca yenileme/saat zamanlayıcılarını başlatır ya da durdurur.</summary>
	void SocialVisible(bool on)
	{
		if (socialPoll == null || socialClock == null) return;
		if (on) { socialPoll.Start(); socialClock.Start(); }
		else { socialPoll.Stop(); socialClock.Stop(); }
	}

	void RefreshSocial() => _ = SocialRefresh();

	// ---------- veri yenileme ----------

	async Task SocialRefresh()
	{
		if (social == null) return;
		if (socialBusy) { socialAgain = true; return; }
		socialBusy = true;
		try
		{
			if (!social.SignedIn) { socialMe = null; ShowGate("signedout"); return; }
			socialMe ??= await social.GetMeAsync();
			if (socialMe == null) { ShowGate("profile"); return; }
			gateKind = null; socialGate.IsVisible = false; socialMain.IsVisible = true;
			switch (socialPane)
			{
				case "friends": RenderFriends(await social.GetFriendsAsync()); break;
				case "rooms": RenderRooms(await social.GetRoomsAsync()); break;
				case "board":
					if (openRoomId == null) { SetPane("rooms"); break; }
					var b = await social.GetBoardAsync(openRoomId, boardDays);
					RenderBoard(b, await social.GetFriendsAsync());
					break;
			}
			if (socialMsgErr) SocialMsg(null);
		}
		catch (Exception ex)
		{
			if (ex.Message == "schema_missing") ShowGate("schema", SocialService.Describe(ex));
			else if (ex.Message == "not_member" && socialPane == "board") { openRoomId = null; SetPane("rooms"); SocialMsg("Bu odadan çıkarıldın ya da oda silindi.", true); }
			else SocialMsg(SocialService.Describe(ex), true);
		}
		finally
		{
			socialBusy = false;
			if (socialAgain) { socialAgain = false; _ = SocialRefresh(); }
		}
	}

	async void SocialDo(Func<Task> act, string? okMsg = null)
	{
		try { await act(); if (okMsg != null) SocialMsg(okMsg); }
		catch (Exception ex) { SocialMsg(SocialService.Describe(ex), true); }
		await SocialRefresh();
		_ = CheckPending();
	}

	void SocialMsg(string? m, bool error = false)
	{
		socialMsgErr = error && m != null;
		socialMsg.Text = m ?? "";
		socialMsg.TextColor = error ? Danger : accent;
		socialMsg.IsVisible = !string.IsNullOrEmpty(m);
	}

	// ---------- giriş kapısı ----------

	void ShowGate(string kind, string? text = null)
	{
		socialMain.IsVisible = false; socialGate.IsVisible = true;
		if (gateKind == kind) return; // yazmakta olduğun alan silinmesin
		gateKind = kind;
		socialGate.Children.Clear();
		switch (kind)
		{
			case "signedout":
				{
					var go = Pill("Ayarlara git", true);
					Tap(go, () => ShowTab(SettingsTab));
					socialGate.Children.Add(Card(new VerticalStackLayout
					{
						Spacing = 12,
						Children = { Lbl("Arkadaşlarını ve odaları görmek için senkron hesabınla giriş yapmalısın.", 14, false, Muted), go }
					}, 18));
					break;
				}
			case "schema":
				socialGate.Children.Add(Card(Lbl(text ?? "", 14, false, Muted), 18));
				break;
			default:
				{
					var name = Field("kullanıcı adı"); name.MaxLength = 20; name.Keyboard = Keyboard.Plain;
					var share = new Switch { IsToggled = true, OnColor = accent, HorizontalOptions = LayoutOptions.Start };
					var save = Pill("Kaydet", true);
					void Save()
					{
						var n = (name.Text ?? "").Trim().ToLowerInvariant();
						bool sh = share.IsToggled;
						SocialDo(async () => { await social!.SetProfileAsync(n, sh); socialMe = null; });
					}
					Tap(save, Save); name.Completed += (_, _) => Save();
					socialGate.Children.Add(Card(new VerticalStackLayout
					{
						Spacing = 10,
						Children =
						{
							Lbl("Bir kullanıcı adı seç", 17, true),
							Lbl("Arkadaşların seni bu adla bulur. 3-20 karakter; küçük harf (a-z), rakam ve _ kullanılabilir.", 13, false, Muted),
							Card(name, 6),
							new HorizontalStackLayout { Spacing = 10, Children = { share, new Label { Text = "Durumumu arkadaşlarım görsün", TextColor = Muted, FontSize = 13, VerticalOptions = LayoutOptions.Center } } },
							save
						}
					}, 18));
					break;
				}
		}
	}

	// ---------- arkadaşlar ----------

	void AddFriend()
	{
		var n = (addFriendEntry.Text ?? "").Trim().TrimStart('@');
		if (n.Length == 0 || social == null) return;
		SocialDo(async () =>
		{
			var r = await social.AddFriendAsync(n);
			addFriendEntry.Text = "";
			addFriendEntry.Unfocus();
			SocialMsg(r == "accepted" ? $"@{n.ToLowerInvariant()} ile arkadaş oldunuz." : $"@{n.ToLowerInvariant()} kişisine istek gönderildi.");
		});
	}

	void RenderFriends(List<FriendInfo> all)
	{
		meLabel.Text = "@" + socialMe!.Username;
		shareSwitch.OnColor = accent;
		settingShare = true; shareSwitch.IsToggled = socialMe.ShareStatus; settingShare = false;
		friendsList.Children.Clear(); socialTicks.Clear();

		var incoming = all.Where(f => f.Rel == "incoming").ToList();
		var friends = all.Where(f => f.Rel == "friend")
			.OrderBy(f => f.Live.Working ? 0 : f.Live.Paused ? 1 : 2).ThenBy(f => f.Username, StringComparer.Ordinal).ToList();
		var outgoing = all.Where(f => f.Rel == "outgoing").ToList();

		if (incoming.Count > 0)
		{
			friendsList.Children.Add(Head($"GELEN İSTEKLER ({incoming.Count})"));
			foreach (var f in incoming)
			{
				var ok = SmallBtn("Kabul et", true); var no = SmallBtn("Reddet", false);
				var fid = f.UserId;
				Tap(ok, () => SocialDo(() => social!.RespondFriendAsync(fid, true)));
				Tap(no, () => SocialDo(() => social!.RespondFriendAsync(fid, false)));
				var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
				g.Add(Lbl("@" + f.Username, 15, true), 0, 0); g.Children.Add(Cell(ok, 1)); g.Children.Add(Cell(no, 2));
				friendsList.Children.Add(Card(g, 12));
			}
		}

		friendsList.Children.Add(Head($"ARKADAŞLAR ({friends.Count})"));
		if (friends.Count == 0)
			friendsList.Children.Add(Lbl("Henüz arkadaşın yok. Yukarıya bir kullanıcı adı yazıp ekleyebilirsin.", 13, false, Muted));
		foreach (var f in friends)
		{
			var ff = f;
			var g = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
			var info = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
			info.Children.Add(Lbl("@" + f.Username, 15, true));
			info.Children.Add(LiveText(f.Live, f.Hidden));
			var right = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
			if (!f.Hidden) { var t = Lbl("Bugün " + Fmt(f.Today), 12, false, Muted); t.HorizontalTextAlignment = TextAlignment.End; right.Children.Add(t); }
			g.Add(StatusDot(f.Live, f.Hidden), 0, 0); g.Add(info, 1, 0); g.Add(right, 2, 0);
			// uzun bas yerine: karta dokununca seçenek menüsü
			var card = Card(g, 14);
			Tap(card, async () =>
			{
				var act = await DisplayActionSheet("@" + ff.Username, "Vazgeç", "Arkadaşlıktan çıkar");
				if (act == "Arkadaşlıktan çıkar" && await DisplayAlert("Arkadaşlıktan çıkar", $"@{ff.Username} arkadaş listenden çıkarılsın mı?", "Çıkar", "Vazgeç"))
					SocialDo(() => social!.RemoveFriendAsync(ff.UserId));
			});
			friendsList.Children.Add(card);
		}
		if (friends.Count > 0) friendsList.Children.Add(Lbl("Çıkarmak için bir arkadaşa dokun.", 11, false, Muted));

		if (outgoing.Count > 0)
		{
			friendsList.Children.Add(Head("GÖNDERİLEN İSTEKLER"));
			foreach (var f in outgoing)
			{
				var fid = f.UserId;
				var undo = SmallBtn("Geri çek", false);
				Tap(undo, () => SocialDo(() => social!.RemoveFriendAsync(fid)));
				var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
				g.Add(Lbl($"@{f.Username} · yanıt bekleniyor", 14, false, Muted), 0, 0); g.Add(undo, 1, 0);
				friendsList.Children.Add(Card(g, 12));
			}
		}
	}

	// ---------- odalar ----------

	async void CreateRoom()
	{
		var name = await DisplayPromptAsync("Yeni oda", "Odanın adı ne olsun?", "Kur", "Vazgeç", "örn. Sınav haftası", 40);
		if (string.IsNullOrWhiteSpace(name) || social == null) return;
		SocialDo(async () => { var id = await social.CreateRoomAsync(name.Trim(), 7); OpenRoom(id, 7); });
	}

	async void JoinRoom()
	{
		var code = await DisplayPromptAsync("Koda katıl", "Arkadaşının verdiği 6 haneli davet kodu:", "Katıl", "Vazgeç", "ABC123", 6);
		if (string.IsNullOrWhiteSpace(code) || social == null) return;
		SocialDo(async () =>
		{
			var id = await social.JoinRoomAsync(code.Trim());
			var r = (await social.GetRoomsAsync()).FirstOrDefault(x => x.Id == id);
			OpenRoom(id, r?.PeriodDays ?? 7);
		});
	}

	void OpenRoom(string id, int days)
	{
		openRoomId = id; boardDays = days; boardHeadKey = null;
		MainThread.BeginInvokeOnMainThread(() => SetPane("board"));
	}

	void RenderRooms(List<RoomInfo> rooms)
	{
		roomsList.Children.Clear(); socialTicks.Clear();
		var invites = rooms.Where(r => r.Invited).ToList();
		var mine = rooms.Where(r => !r.Invited).ToList();

		if (invites.Count > 0)
		{
			roomsList.Children.Add(Head($"ODA DAVETLERİ ({invites.Count})"));
			foreach (var r in invites)
			{
				var rid = r.Id;
				var ok = SmallBtn("Katıl", true); var no = SmallBtn("Reddet", false);
				Tap(ok, () => SocialDo(() => social!.RespondRoomAsync(rid, true)));
				Tap(no, () => SocialDo(() => social!.RespondRoomAsync(rid, false)));
				var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) } };
				g.Add(new VerticalStackLayout { Spacing = 1, Children = { Lbl(r.Name, 15, true), Lbl($"@{r.InvitedBy} seni davet etti", 12, false, Muted) } }, 0, 0);
				g.Children.Add(Cell(ok, 1)); g.Children.Add(Cell(no, 2));
				roomsList.Children.Add(Card(g, 12));
			}
		}

		roomsList.Children.Add(Head($"ODALARIM ({mine.Count})"));
		if (mine.Count == 0)
			roomsList.Children.Add(Lbl("Henüz bir odan yok. Bir oda kur ya da arkadaşının davet kodunu gir.", 13, false, Muted));
		foreach (var r in mine)
		{
			var rr = r;
			var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
			g.Add(new VerticalStackLayout
			{
				Spacing = 2,
				Children =
				{
					Lbl(r.Name + (r.IsOwner ? "  👑" : ""), 16, true),
					Lbl($"{r.Members} kişi · sıralama: {SocialService.PeriodLabel(r.PeriodDays)}", 12, false, Muted)
				}
			}, 0, 0);
			var chev = Lbl("›", 24, false, Muted); chev.VerticalOptions = LayoutOptions.Center;
			g.Add(chev, 1, 0);
			var card = Card(g, 14);
			Tap(card, () => OpenRoom(rr.Id, rr.PeriodDays));
			roomsList.Children.Add(card);
		}
	}

	// ---------- lider tablosu ----------

	void SetBoardDays(int d)
	{
		d = Math.Clamp(d, 1, 366);
		boardDays = d; boardHeadKey = null;
		if (board?.IsOwner == true && openRoomId != null && social != null)
			SocialDo(() => social.SetRoomPeriodAsync(openRoomId, d)); // oda sahibinin seçimi odanın varsayılanı olur
		else _ = SocialRefresh();
	}

	void RenderBoard(Board b, List<FriendInfo> friends)
	{
		board = b;
		socialTicks.Clear();

		var key = $"{b.Id}|{boardDays}|{b.IsOwner}";
		if (boardHeadKey != key)
		{
			boardHeadKey = key;
			boardHead.Children.Clear();
			var back = Lbl("‹ Odalar", 15, true, accent); back.Padding = new Thickness(0, 6, 12, 6);
			Tap(back, () => { openRoomId = null; SetPane("rooms"); });
			boardHead.Children.Add(back);
			boardHead.Children.Add(Lbl(b.Name, 22, true));

			var code = Lbl(b.Code, 17, true); code.VerticalOptions = LayoutOptions.Center;
			var copy = SmallBtn("Kopyala", false);
			Tap(copy, async () => { try { await Clipboard.Default.SetTextAsync(b.Code); SocialMsg("Davet kodu kopyalandı."); } catch { } });
			var share = SmallBtn("Paylaş", false);
			Tap(share, async () => { try { await Share.Default.RequestAsync(new ShareTextRequest { Text = $"Flow State'te \"{b.Name}\" odasına katıl! Davet kodu: {b.Code}", Title = "Odaya davet" }); } catch { } });
			var codeRow = new HorizontalStackLayout { Spacing = 10, Children = { Lbl("Davet kodu:", 13, false, Muted), code, copy, share } };
			boardHead.Children.Add(Card(codeRow, 12));

			boardHead.Children.Add(Head("SIRALAMA ARALIĞI"));
			var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
			bool custom = BoardPeriods.All(p => p.days != boardDays);
			foreach (var (days, label) in BoardPeriods)
			{
				var d = days;
				chips.Children.Add(Chip(label, !custom && boardDays == d, () => SetBoardDays(d)));
			}
			chips.Children.Add(Chip(custom ? $"{boardDays} gün" : "Özel…", custom, async () =>
			{
				var txt = await DisplayPromptAsync("Özel aralık", "Son kaç gün? (1-366)", "Uygula", "Vazgeç", "örn. 10", 3, Keyboard.Numeric);
				if (int.TryParse(txt, out var n) && n >= 1) SetBoardDays(n);
			}));
			boardHead.Children.Add(chips);
			boardHead.Children.Add(Lbl($"Son {boardDays} gün (bugün dahil) · " + (b.IsOwner ? "Seçtiğin aralık odanın varsayılanı olarak kaydedilir." : "Bu seçim yalnızca senin ekranında geçerli."), 12, false, Muted));
			boardHead.Children.Add(Head("LİDER TABLOSU"));
		}

		boardList.Children.Clear();
		var active = b.Members.Where(m => !m.Invited).ToList();
		double top1 = Math.Max(1, active.Count == 0 ? 1 : active.Max(m => m.Total));
		foreach (var m in active)
		{
			var mm = m;
			var g = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(new GridLength(24)), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
			var rank = Lbl(m.Rank > 0 ? m.Rank.ToString() : "–", 14, true, Muted); rank.VerticalOptions = LayoutOptions.Center;
			var medal = m.Rank is >= 1 and <= 3 ? Medals[m.Rank] + " " : "";
			var nameText = medal + m.Username + (m.IsMe ? " (sen)" : "") + (m.IsOwner ? " 👑" : "");

			var ratio = Math.Max(0.004, m.Total / top1);
			var bar = new Grid { HeightRequest = 4, Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 0, ColumnDefinitions = { new(new GridLength(ratio, GridUnitType.Star)), new(new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star)) } };
			bar.Add(new Border { BackgroundColor = accent, StrokeThickness = 0, Opacity = m.Total >= 1 ? 1 : 0.2, StrokeShape = new RoundRectangle { CornerRadius = 2 } }, 0, 0);

			var mid = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { Lbl(nameText, m.Rank == 1 ? 17 : 15, true), LiveText(m.Live, m.Hidden), bar } };
			var time = Lbl("", 15, true); time.VerticalOptions = LayoutOptions.Center;
			void Upd() => time.Text = Fmt(mm.Total);
			Upd();
			if (m.Live.Working) socialTicks.Add(Upd);

			g.Add(rank, 0, 0); g.Add(StatusDot(m.Live, m.Hidden), 1, 0); g.Add(mid, 2, 0); g.Add(time, 3, 0);
			var card = Card(g, 14);
			if (m.IsMe) card.BackgroundColor = Panel2;
			if (b.IsOwner && !m.IsMe)
				Tap(card, async () =>
				{
					if (await DisplayAlert("Odadan çıkar", $"@{mm.Username} odadan çıkarılsın mı?", "Çıkar", "Vazgeç"))
						SocialDo(() => social!.KickAsync(b.Id, mm.UserId));
				});
			boardList.Children.Add(card);
		}
		if (active.All(m => m.Total < 1))
			boardList.Children.Add(Lbl("Bu aralıkta henüz kimse çalışmadı. İlk çalışan 🏆 alır!", 13, false, Muted));
		if (b.IsOwner && active.Count > 1) boardList.Children.Add(Lbl("Birini odadan çıkarmak için ona dokun.", 11, false, Muted));

		boardInvite.Children.Clear();
		var pending = b.Members.Where(m => m.Invited).ToList();
		if (pending.Count > 0)
		{
			boardInvite.Children.Add(Head("DAVET EDİLENLER"));
			foreach (var m in pending) boardInvite.Children.Add(Card(Lbl($"@{m.Username} · yanıt bekleniyor", 14, false, Muted), 12));
		}
		var inRoom = b.Members.Select(m => m.UserId).ToHashSet();
		var invitable = friends.Where(f => f.Rel == "friend" && !inRoom.Contains(f.UserId)).ToList();
		if (invitable.Count > 0)
		{
			boardInvite.Children.Add(Head("ARKADAŞINI DAVET ET"));
			var wrap = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
			foreach (var f in invitable)
			{
				var ff = f;
				wrap.Children.Add(Chip("＋ " + f.Username, false, () => SocialDo(() => social!.InviteAsync(b.Id, ff.UserId), $"@{ff.Username} davet edildi.")));
			}
			boardInvite.Children.Add(wrap);
		}
		var leave = Pill(b.IsOwner ? "Odayı sil" : "Odadan ayrıl", false, Danger);
		leave.Margin = new Thickness(0, 18, 0, 0);
		Tap(leave, async () =>
		{
			var msg = b.IsOwner ? $"\"{b.Name}\" odası herkes için silinecek." : $"\"{b.Name}\" odasından ayrılmak istiyor musun?";
			if (await DisplayAlert(b.IsOwner ? "Odayı sil" : "Odadan ayrıl", msg, b.IsOwner ? "Sil" : "Ayrıl", "Vazgeç"))
				SocialDo(async () => { await social!.LeaveRoomAsync(b.Id); openRoomId = null; SetPane("rooms"); });
		});
		boardInvite.Children.Add(leave);
	}

	// ---------- küçük yapı taşları ----------

	Border SmallBtn(string text, bool filled)
	{
		var l = Lbl(text, 13, true, filled ? Color.FromArgb("#0A0E1A") : Text);
		l.HorizontalTextAlignment = TextAlignment.Center; l.VerticalTextAlignment = TextAlignment.Center;
		return new Border
		{
			BackgroundColor = filled ? accent : Panel2, StrokeThickness = 0, Padding = new Thickness(12, 0), HeightRequest = 36,
			StrokeShape = new RoundRectangle { CornerRadius = 12 }, VerticalOptions = LayoutOptions.Center, Content = l
		};
	}

	Border Chip(string text, bool on, Action click)
	{
		var b = new Border
		{
			BackgroundColor = on ? sel : Panel2, StrokeThickness = 0, Padding = new Thickness(14, 0), HeightRequest = 36,
			Margin = new Thickness(0, 0, 8, 8), StrokeShape = new RoundRectangle { CornerRadius = 18 },
			Content = new Label { Text = text, TextColor = on ? Text : Muted, FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center }
		};
		Tap(b, click);
		return b;
	}

	static View Cell(View v, int col) { Grid.SetColumn(v, col); return v; }

	static Border StatusDot(LiveInfo l, bool hidden) =>
		Dot(hidden ? Offline : l.Working ? Green : l.Paused ? Amber : Offline, 11);

	static string Clock(double sec)
	{
		int s = (int)sec;
		return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
	}

	/// <summary>"Çalışıyor · 00:12:45" gibi canlı durum yazısı; çalışıyorsa her saniye kendini günceller.</summary>
	Label LiveText(LiveInfo l, bool hidden)
	{
		var t = Lbl("", 12, false, Muted);
		void Upd()
		{
			t.Text = hidden ? "Durumunu paylaşmıyor" : l.Working ? "Çalışıyor · " + Clock(l.Elapsed) : l.Paused ? "Mola · " + Clock(l.Elapsed) : "Çevrimdışı";
			t.TextColor = !hidden && l.Working ? Green : Muted;
		}
		Upd();
		if (l.Working && !hidden) socialTicks.Add(Upd);
		return t;
	}
}

// Sosyal sekmesinin yanındaki kırmızı rozet: bekleyen arkadaş isteği + oda daveti sayısı.
public partial class MainPage
{
	IDispatcherTimer? pendingTimer;
	readonly Label badgeLabel = new() { TextColor = Colors.White, FontSize = 10, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
	Border? badgeBorder;

	Border socialBadge => badgeBorder ??= new Border
	{
		BackgroundColor = Color.FromArgb("#E5484D"), StrokeThickness = 0, MinimumWidthRequest = 16, HeightRequest = 16, Padding = new Thickness(4, 0),
		StrokeShape = new RoundRectangle { CornerRadius = 8 }, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start,
		Margin = new Thickness(0, 3, 4, 0), IsVisible = false, InputTransparent = true, Content = badgeLabel
	};

	async Task CheckPending()
	{
		if (social == null) return;
		if (!sync.SignedIn) { SetBadge(0); return; }
		try
		{
			int n = (await social.GetFriendsAsync()).Count(f => f.Rel == "incoming") + (await social.GetRoomsAsync()).Count(r => r.Invited);
			SetBadge(n);
		}
		catch { /* çevrimdışı ya da sosyal şema kurulu değil: rozet değişmesin */ }
	}

	void SetBadge(int n)
	{
		badgeLabel.Text = n > 9 ? "9+" : n.ToString();
		socialBadge.IsVisible = n > 0;
	}
}
