using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FlowState;

// Sosyal sekmesi: arkadaşlar (canlı durum) ve odalar (lider tablosu).
public partial class MainWindow
{
    static readonly string[] Medals = { "", "🏆", "🥈", "🥉" };
    static readonly (int days, string label)[] BoardPeriods = { (1, "Bugün"), (3, "3 gün"), (5, "5 gün"), (7, "1 hafta"), (14, "2 hafta"), (30, "1 ay") };

    SocialService? social;
    MyProfile? socialMe;
    bool socialBuilt, socialBusy, socialAgain, suppressSeg;
    string socialPane = "friends"; // friends | rooms | board
    string? openRoomId, gateKind, boardHeadKey;
    int boardDays = 7;
    bool socialMsgErr;
    readonly List<Action> socialTicks = new();
    DispatcherTimer? socialPoll, socialClock;

    StackPanel socialGate = null!, socialMain = null!, friendsPane = null!, roomsPane = null!, boardPane = null!,
        friendsList = null!, roomsList = null!, boardHead = null!, boardList = null!, boardInvite = null!;
    TextBlock socialMsg = null!, meText = null!;
    CheckBox shareBox = null!;
    TextBox addFriendBox = null!, roomNameBox = null!, roomCodeBox = null!;
    RadioButton segFriends = null!, segRooms = null!;

    // ---------- yaşam döngüsü ----------

    void SocialShown(bool on)
    {
        if (!on) { socialPoll?.Stop(); socialClock?.Stop(); return; }
        if (!socialBuilt) BuildSocial();
        if (social == null && sync != null) social = new SocialService(sync);
        socialPoll!.Start(); socialClock!.Start();
        _ = SocialRefresh();
    }

    void BuildSocial()
    {
        socialBuilt = true;
        socialPoll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        socialPoll.Tick += (_, _) => _ = SocialRefresh();
        socialClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        socialClock.Tick += (_, _) => { foreach (var t in socialTicks.ToList()) t(); };

        SocialRoot.Children.Add(new TextBlock { Text = "Sosyal", FontSize = 28, FontWeight = FontWeights.Bold });
        socialMsg = new TextBlock { Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        SocialRoot.Children.Add(socialMsg);
        socialGate = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        socialMain = new StackPanel { Visibility = Visibility.Collapsed };
        SocialRoot.Children.Add(socialGate);
        SocialRoot.Children.Add(socialMain);

        var seg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-3, 14, 0, 4) };
        segFriends = new RadioButton { GroupName = "socseg", Style = (Style)FindResource("Nav"), Content = "Arkadaşlar", IsChecked = true, Padding = new Thickness(14, 6, 14, 6), FontSize = 13 };
        segRooms = new RadioButton { GroupName = "socseg", Style = (Style)FindResource("Nav"), Content = "Odalar", Padding = new Thickness(14, 6, 14, 6), FontSize = 13 };
        segFriends.Checked += (_, _) => { if (!suppressSeg) SetPane("friends", true); };
        segRooms.Checked += (_, _) => { if (!suppressSeg) SetPane("rooms", true); };
        seg.Children.Add(segFriends); seg.Children.Add(segRooms);
        socialMain.Children.Add(seg);

        // --- arkadaşlar ---
        friendsPane = new StackPanel();
        meText = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold };
        shareBox = new CheckBox { Content = "Durumumu arkadaşlarım görsün", Foreground = Res("Text"), VerticalAlignment = VerticalAlignment.Center };
        shareBox.Click += (_, _) =>
        {
            if (socialMe == null || social == null) return;
            var name = socialMe.Username; bool share = shareBox.IsChecked == true;
            SocialDo(async () => { await social.SetProfileAsync(name, share); socialMe.ShareStatus = share; });
        };
        var meDock = new DockPanel();
        DockPanel.SetDock(shareBox, Dock.Right);
        meDock.Children.Add(shareBox);
        meDock.Children.Add(new StackPanel
        {
            Children =
            {
                meText,
                new TextBlock { Text = "Arkadaşların seni bu kullanıcı adıyla ekler.", FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(0, 2, 0, 0) }
            }
        });
        friendsPane.Children.Add(new Border { Background = Res("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(18, 14, 18, 14), Child = meDock });

        addFriendBox = new TextBox { Width = 240, MaxLength = 20 };
        addFriendBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) AddFriend(); };
        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        addRow.Children.Add(new TextBlock { Text = "Kullanıcı adıyla ekle:", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        addRow.Children.Add(addFriendBox);
        addRow.Children.Add(SBtn("Arkadaş ekle", AddFriend, accent: true));
        friendsPane.Children.Add(addRow);
        friendsList = new StackPanel();
        friendsPane.Children.Add(friendsList);
        socialMain.Children.Add(friendsPane);

        // --- odalar ---
        roomsPane = new StackPanel { Visibility = Visibility.Collapsed };
        roomNameBox = new TextBox { Width = 240, MaxLength = 40 };
        roomNameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) CreateRoom(); };
        var createRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        createRow.Children.Add(new TextBlock { Text = "Yeni oda:", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        createRow.Children.Add(roomNameBox);
        createRow.Children.Add(SBtn("Oda kur", CreateRoom, accent: true));
        roomsPane.Children.Add(createRow);
        roomCodeBox = new TextBox { Width = 130, MaxLength = 6, CharacterCasing = CharacterCasing.Upper };
        roomCodeBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) JoinRoom(); };
        var joinRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        joinRow.Children.Add(new TextBlock { Text = "Davet koduyla katıl:", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        joinRow.Children.Add(roomCodeBox);
        joinRow.Children.Add(SBtn("Katıl", JoinRoom));
        roomsPane.Children.Add(joinRow);
        roomsList = new StackPanel();
        roomsPane.Children.Add(roomsList);
        socialMain.Children.Add(roomsPane);

        // --- oda / lider tablosu ---
        boardPane = new StackPanel { Visibility = Visibility.Collapsed };
        boardHead = new StackPanel(); boardList = new StackPanel(); boardInvite = new StackPanel();
        boardPane.Children.Add(boardHead); boardPane.Children.Add(boardList); boardPane.Children.Add(boardInvite);
        socialMain.Children.Add(boardPane);
    }

    void SetPane(string p, bool fromSeg = false)
    {
        socialPane = p;
        friendsPane.Visibility = p == "friends" ? Visibility.Visible : Visibility.Collapsed;
        roomsPane.Visibility = p == "rooms" ? Visibility.Visible : Visibility.Collapsed;
        boardPane.Visibility = p == "board" ? Visibility.Visible : Visibility.Collapsed;
        if (!fromSeg)
        {
            suppressSeg = true;
            (p == "friends" ? segFriends : segRooms).IsChecked = true;
            suppressSeg = false;
        }
        SocialMsg(null);
        _ = SocialRefresh();
    }

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
            ShowMain();
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
            if (socialMsgErr) SocialMsg(null); // eski hata temizlenir; bilgi mesajları kalır
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

    /// <summary>Bir işlemi çalıştırır, hatayı gösterir, ardından ekranı yeniler.</summary>
    async void SocialDo(Func<Task> act, string? okMsg = null)
    {
        try { await act(); if (okMsg != null) SocialMsg(okMsg); }
        catch (Exception ex) { SocialMsg(SocialService.Describe(ex), true); }
        await SocialRefresh();
        _ = CheckPending();
    }

    void SocialMsg(string? m, bool error = false)
    {
        if (!socialBuilt) return;
        socialMsgErr = error && m != null;
        socialMsg.Text = m ?? "";
        socialMsg.Foreground = error ? ParseBrush("#E58A8A") : Res("Accent");
        socialMsg.Visibility = string.IsNullOrEmpty(m) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- giriş kapısı (hesap yok / kullanıcı adı yok / şema yok) ----------

    void ShowMain() { gateKind = null; socialGate.Visibility = Visibility.Collapsed; socialMain.Visibility = Visibility.Visible; }

    void ShowGate(string kind, string? text = null)
    {
        socialMain.Visibility = Visibility.Collapsed;
        socialGate.Visibility = Visibility.Visible;
        if (gateKind == kind) return; // yazmakta olduğun alan silinmesin
        gateKind = kind;
        socialGate.Children.Clear();
        var inner = new StackPanel();
        switch (kind)
        {
            case "signedout":
                inner.Children.Add(new TextBlock { Text = "Arkadaşlarını ve odaları görmek için senkron hesabınla giriş yapmalısın.", TextWrapping = TextWrapping.Wrap, Foreground = Res("Muted") });
                var go = SBtn("Ayarlara git", () => NavSettings.IsChecked = true, accent: true);
                go.Margin = new Thickness(0, 14, 0, 0); go.HorizontalAlignment = HorizontalAlignment.Left;
                inner.Children.Add(go);
                break;
            case "schema":
                inner.Children.Add(new TextBlock { Text = text ?? "", TextWrapping = TextWrapping.Wrap, Foreground = Res("Muted") });
                break;
            default: // profile
                inner.Children.Add(new TextBlock { Text = "Bir kullanıcı adı seç", FontSize = 16, FontWeight = FontWeights.SemiBold });
                inner.Children.Add(new TextBlock
                {
                    Text = "Arkadaşların seni bu adla bulur. 3-20 karakter; küçük harf (a-z), rakam ve _ kullanılabilir.",
                    TextWrapping = TextWrapping.Wrap, Foreground = Res("Muted"), Margin = new Thickness(0, 4, 0, 12)
                });
                var nameBox = new TextBox { Width = 260, MaxLength = 20, HorizontalAlignment = HorizontalAlignment.Left };
                var share = new CheckBox { Content = "Durumumu arkadaşlarım görsün", IsChecked = true, Foreground = Res("Text"), Margin = new Thickness(0, 12, 0, 0) };
                void Save()
                {
                    var n = nameBox.Text.Trim().ToLowerInvariant();
                    SocialDo(async () => { await social!.SetProfileAsync(n, share.IsChecked == true); socialMe = null; });
                }
                nameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Save(); };
                var save = SBtn("Kaydet", Save, accent: true);
                save.Margin = new Thickness(0, 14, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Left;
                inner.Children.Add(nameBox); inner.Children.Add(share); inner.Children.Add(save);
                break;
        }
        socialGate.Children.Add(new Border { Background = Res("Panel"), CornerRadius = new CornerRadius(14), Padding = new Thickness(20, 18, 20, 18), Child = inner });
    }

    // ---------- arkadaşlar ----------

    void AddFriend()
    {
        var n = addFriendBox.Text.Trim().TrimStart('@');
        if (n.Length == 0 || social == null) return;
        SocialDo(async () =>
        {
            var r = await social.AddFriendAsync(n);
            addFriendBox.Clear();
            SocialMsg(r == "accepted" ? $"@{n.ToLowerInvariant()} ile arkadaş oldunuz." : $"@{n.ToLowerInvariant()} kişisine istek gönderildi.");
        });
    }

    void RenderFriends(List<FriendInfo> all)
    {
        meText.Text = "@" + socialMe!.Username;
        shareBox.IsChecked = socialMe.ShareStatus;
        friendsList.Children.Clear(); socialTicks.Clear();

        var incoming = all.Where(f => f.Rel == "incoming").ToList();
        var friends = all.Where(f => f.Rel == "friend")
            .OrderBy(f => f.Live.Working ? 0 : f.Live.Paused ? 1 : 2).ThenBy(f => f.Username, StringComparer.Ordinal).ToList();
        var outgoing = all.Where(f => f.Rel == "outgoing").ToList();

        if (incoming.Count > 0)
        {
            friendsList.Children.Add(SHeader($"GELEN İSTEKLER ({incoming.Count})"));
            foreach (var f in incoming)
            {
                var row = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(SBtn("Kabul et", () => SocialDo(() => social!.RespondFriendAsync(f.UserId, true)), accent: true));
                btns.Children.Add(SBtn("Reddet", () => SocialDo(() => social!.RespondFriendAsync(f.UserId, false))));
                DockPanel.SetDock(btns, Dock.Right);
                row.Children.Add(btns);
                row.Children.Add(new TextBlock { Text = "@" + f.Username, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
                friendsList.Children.Add(SRow(row));
            }
        }

        friendsList.Children.Add(SHeader($"ARKADAŞLAR ({friends.Count})"));
        if (friends.Count == 0)
            friendsList.Children.Add(new TextBlock { Text = "Henüz arkadaşın yok. Yukarıya bir kullanıcı adı yazıp ekleyebilirsin.", Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap });
        foreach (var f in friends)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = StatusDot(f.Live, f.Hidden); dot.Margin = new Thickness(0, 0, 12, 0);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = "@" + f.Username, FontWeight = FontWeights.SemiBold });
            info.Children.Add(LiveText(f.Live, f.Hidden));
            var today = new TextBlock
            {
                Text = f.Hidden ? "" : "Bugün " + Fmt(f.Today), Foreground = Res("Muted"), FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0)
            };
            var del = SBtn("✕", () =>
            {
                if (ConfirmDialog.Ask(this, "Arkadaşlıktan çıkar", $"@{f.Username} arkadaş listenden çıkarılsın mı?", "Çıkar"))
                    SocialDo(() => social!.RemoveFriendAsync(f.UserId));
            }, danger: true);
            del.ToolTip = "Arkadaşlıktan çıkar";
            Grid.SetColumn(info, 1); Grid.SetColumn(today, 2); Grid.SetColumn(del, 3);
            g.Children.Add(dot); g.Children.Add(info); g.Children.Add(today); g.Children.Add(del);
            friendsList.Children.Add(SRow(g));
        }

        if (outgoing.Count > 0)
        {
            friendsList.Children.Add(SHeader("GÖNDERİLEN İSTEKLER"));
            foreach (var f in outgoing)
            {
                var row = new DockPanel();
                var cancel = SBtn("Geri çek", () => SocialDo(() => social!.RemoveFriendAsync(f.UserId)));
                DockPanel.SetDock(cancel, Dock.Right);
                row.Children.Add(cancel);
                row.Children.Add(new TextBlock { Text = $"@{f.Username}  ·  yanıt bekleniyor", VerticalAlignment = VerticalAlignment.Center, Foreground = Res("Muted") });
                friendsList.Children.Add(SRow(row));
            }
        }
    }

    // ---------- odalar ----------

    void CreateRoom()
    {
        var n = roomNameBox.Text.Trim();
        if (n.Length == 0 || social == null) return;
        SocialDo(async () =>
        {
            var id = await social.CreateRoomAsync(n, 7);
            roomNameBox.Clear();
            OpenRoom(id, 7);
        });
    }

    void JoinRoom()
    {
        var c = roomCodeBox.Text.Trim();
        if (c.Length == 0 || social == null) return;
        SocialDo(async () =>
        {
            var id = await social.JoinRoomAsync(c);
            roomCodeBox.Clear();
            var r = (await social.GetRoomsAsync()).FirstOrDefault(x => x.Id == id);
            OpenRoom(id, r?.PeriodDays ?? 7);
        });
    }

    void OpenRoom(string id, int days)
    {
        openRoomId = id; boardDays = days; boardHeadKey = null;
        SetPane("board");
    }

    void RenderRooms(List<RoomInfo> rooms)
    {
        roomsList.Children.Clear(); socialTicks.Clear();
        var invites = rooms.Where(r => r.Invited).ToList();
        var mine = rooms.Where(r => !r.Invited).ToList();

        if (invites.Count > 0)
        {
            roomsList.Children.Add(SHeader($"ODA DAVETLERİ ({invites.Count})"));
            foreach (var r in invites)
            {
                var row = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                btns.Children.Add(SBtn("Katıl", () => SocialDo(() => social!.RespondRoomAsync(r.Id, true)), accent: true));
                btns.Children.Add(SBtn("Reddet", () => SocialDo(() => social!.RespondRoomAsync(r.Id, false))));
                DockPanel.SetDock(btns, Dock.Right);
                row.Children.Add(btns);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold });
                t.Children.Add(new TextBlock { Text = $"@{r.InvitedBy} seni davet etti", FontSize = 12, Foreground = Res("Muted") });
                row.Children.Add(t);
                roomsList.Children.Add(SRow(row));
            }
        }

        roomsList.Children.Add(SHeader($"ODALARIM ({mine.Count})"));
        if (mine.Count == 0)
            roomsList.Children.Add(new TextBlock { Text = "Henüz bir odan yok. Bir oda kur ya da arkadaşının davet kodunu gir.", Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap });
        foreach (var r in mine)
        {
            var row = new DockPanel { Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            var arrow = new TextBlock { Text = "›", FontSize = 22, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(arrow, Dock.Right);
            row.Children.Add(arrow);
            var t = new StackPanel();
            t.Children.Add(new TextBlock { Text = r.Name + (r.IsOwner ? "  👑" : ""), FontWeight = FontWeights.SemiBold, FontSize = 15 });
            t.Children.Add(new TextBlock { Text = $"{r.Members} kişi  ·  sıralama: {SocialService.PeriodLabel(r.PeriodDays)}", FontSize = 12, Foreground = Res("Muted") });
            row.Children.Add(t);
            var rr = r;
            row.MouseLeftButtonUp += (_, _) => OpenRoom(rr.Id, rr.PeriodDays);
            roomsList.Children.Add(SRow(row));
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

    Board? board;

    void RenderBoard(Board b, List<FriendInfo> friends)
    {
        board = b;
        socialTicks.Clear();

        // Üst bölüm yalnızca oda/aralık değişince yeniden kurulur (yazılan alanlar silinmesin).
        var key = $"{b.Id}|{boardDays}|{b.IsOwner}";
        if (boardHeadKey != key)
        {
            boardHeadKey = key;
            boardHead.Children.Clear();
            var top = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var back = SBtn("‹ Odalar", () => { openRoomId = null; SetPane("rooms"); });
            back.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(back, Dock.Left);
            top.Children.Add(back);
            top.Children.Add(new TextBlock { Text = b.Name, FontSize = 22, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            boardHead.Children.Add(top);

            var code = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            code.Children.Add(new TextBlock { Text = "Davet kodu:", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
            code.Children.Add(new TextBlock { Text = b.Code, FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var copy = SBtn("Kopyala", () => { try { Clipboard.SetText(b.Code); SocialMsg("Davet kodu kopyalandı."); } catch { } });
            code.Children.Add(copy);
            boardHead.Children.Add(code);

            boardHead.Children.Add(SHeader("SIRALAMA ARALIĞI"));
            var chips = new WrapPanel();
            bool custom = BoardPeriods.All(p => p.days != boardDays);
            foreach (var (days, label) in BoardPeriods)
            {
                var d = days;
                var chip = new Button { Content = label, Style = (Style)FindResource("Btn"), Padding = new Thickness(14, 6, 14, 6), FontSize = 13, Margin = new Thickness(0, 0, 6, 6) };
                if (!custom && boardDays == d) chip.SetResourceReference(Control.BackgroundProperty, "Sel");
                chip.Click += (_, _) => SetBoardDays(d);
                chips.Children.Add(chip);
            }
            var box = new TextBox { Width = 54, MaxLength = 3, Text = custom ? boardDays.ToString() : "", TextAlignment = TextAlignment.Center, Margin = new Thickness(6, 0, 6, 6) };
            var apply = new Action(() => { if (int.TryParse(box.Text.Trim(), out var n) && n >= 1) SetBoardDays(n); });
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) apply(); };
            var customRow = new StackPanel { Orientation = Orientation.Horizontal };
            customRow.Children.Add(new TextBlock { Text = "Özel:", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 6) });
            customRow.Children.Add(box);
            customRow.Children.Add(new TextBlock { Text = "gün", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
            var ok = SBtn("Uygula", apply); ok.Margin = new Thickness(8, 0, 0, 6);
            customRow.Children.Add(ok);
            chips.Children.Add(customRow);
            boardHead.Children.Add(chips);
            boardHead.Children.Add(new TextBlock
            {
                Text = $"Son {boardDays} gün (bugün dahil) · " + (b.IsOwner ? "Seçtiğin aralık odanın varsayılanı olarak kaydedilir." : "Bu seçim yalnızca senin ekranında geçerli."),
                FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(2, 2, 0, 0)
            });
            boardHead.Children.Add(SHeader("LİDER TABLOSU"));
        }

        // Sıralama
        boardList.Children.Clear();
        var active = b.Members.Where(m => !m.Invited).ToList();
        double top1 = Math.Max(1, active.Count == 0 ? 1 : active.Max(m => m.Total));
        foreach (var m in active)
        {
            var mm = m;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rank = new TextBlock
            {
                Text = m.Rank > 0 ? m.Rank.ToString() : "–", Foreground = Res("Muted"), FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var dot = StatusDot(m.Live, m.Hidden); dot.Margin = new Thickness(0, 0, 12, 0);
            var medal = m.Rank is >= 1 and <= 3 ? Medals[m.Rank] + " " : "";

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock { Text = medal + m.Username, FontWeight = FontWeights.SemiBold, FontSize = m.Rank == 1 ? 16 : 14 });
            if (m.IsMe) nameLine.Children.Add(new TextBlock { Text = "  (sen)", Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
            if (m.IsOwner) nameLine.Children.Add(new TextBlock { Text = "  👑", VerticalAlignment = VerticalAlignment.Center });
            mid.Children.Add(nameLine);
            mid.Children.Add(LiveText(m.Live, m.Hidden));

            var ratio = Math.Max(0.004, m.Total / top1);
            var bar = new Grid { Height = 4, Margin = new Thickness(0, 6, 14, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star) });
            var fill = new Border { CornerRadius = new CornerRadius(2), Opacity = m.Total >= 1 ? 1 : 0.2 };
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            bar.Children.Add(fill);
            mid.Children.Add(bar);

            var time = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            void Upd() => time.Text = Fmt(mm.Total);
            Upd();
            if (m.Live.Working) socialTicks.Add(Upd);

            Grid.SetColumn(dot, 1); Grid.SetColumn(mid, 2); Grid.SetColumn(time, 3);
            g.Children.Add(rank); g.Children.Add(dot); g.Children.Add(mid); g.Children.Add(time);
            if (b.IsOwner && !m.IsMe)
            {
                var kick = SBtn("✕", () =>
                {
                    if (ConfirmDialog.Ask(this, "Odadan çıkar", $"@{mm.Username} odadan çıkarılsın mı?", "Çıkar"))
                        SocialDo(() => social!.KickAsync(b.Id, mm.UserId));
                }, danger: true);
                kick.ToolTip = "Odadan çıkar";
                Grid.SetColumn(kick, 4); g.Children.Add(kick);
            }
            boardList.Children.Add(SRow(g, m.IsMe));
        }
        if (active.All(m => m.Total < 1))
            boardList.Children.Add(new TextBlock { Text = "Bu aralıkta henüz kimse çalışmadı. İlk çalışan 🏆 alır!", Foreground = Res("Muted"), Margin = new Thickness(2, 4, 0, 0) });

        // Davetliler + davet edilebilecek arkadaşlar + ayrıl
        boardInvite.Children.Clear();
        var pending = b.Members.Where(m => m.Invited).ToList();
        if (pending.Count > 0)
        {
            boardInvite.Children.Add(SHeader("DAVET EDİLENLER"));
            foreach (var m in pending)
                boardInvite.Children.Add(SRow(new TextBlock { Text = $"@{m.Username}  ·  yanıt bekleniyor", Foreground = Res("Muted") }));
        }
        var inRoom = b.Members.Select(m => m.UserId).ToHashSet();
        var invitable = friends.Where(f => f.Rel == "friend" && !inRoom.Contains(f.UserId)).ToList();
        if (invitable.Count > 0)
        {
            boardInvite.Children.Add(SHeader("ARKADAŞINI DAVET ET"));
            var wrap = new WrapPanel();
            foreach (var f in invitable)
            {
                var ff = f;
                var btn = SBtn("＋ " + f.Username, () => SocialDo(() => social!.InviteAsync(b.Id, ff.UserId), $"@{ff.Username} davet edildi."));
                btn.Margin = new Thickness(0, 0, 6, 6);
                wrap.Children.Add(btn);
            }
            boardInvite.Children.Add(wrap);
        }
        var leave = SBtn(b.IsOwner ? "Odayı sil" : "Odadan ayrıl", () =>
        {
            var msg = b.IsOwner ? $"\"{b.Name}\" odası herkes için silinecek." : $"\"{b.Name}\" odasından ayrılmak istiyor musun?";
            if (ConfirmDialog.Ask(this, b.IsOwner ? "Odayı sil" : "Odadan ayrıl", msg, b.IsOwner ? "Sil" : "Ayrıl"))
                SocialDo(async () => { await social!.LeaveRoomAsync(b.Id); openRoomId = null; SetPane("rooms"); });
        }, danger: true);
        leave.Margin = new Thickness(0, 22, 0, 0); leave.HorizontalAlignment = HorizontalAlignment.Left;
        boardInvite.Children.Add(leave);
    }

    // ---------- küçük yapı taşları ----------

    Button SBtn(string text, Action click, bool danger = false, bool accent = false)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Btn"), Padding = new Thickness(12, 6, 12, 6), FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
        if (danger) b.Foreground = ParseBrush("#E58A8A");
        if (accent) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.Foreground = ParseBrush("#0A0E1A"); }
        b.Click += (_, _) => click();
        return b;
    }

    Border SRow(UIElement child, bool me = false) => new()
    {
        Background = Res(me ? "Panel2" : "Panel"), CornerRadius = new CornerRadius(12),
        Padding = new Thickness(14, 10, 8, 10), Margin = new Thickness(0, 0, 0, 6), Child = child
    };

    TextBlock SHeader(string t) => new() { Text = t, Style = (Style)FindResource("H"), Margin = new Thickness(0, 22, 0, 10) };

    static Ellipse StatusDot(LiveInfo l, bool hidden) => new()
    {
        Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center,
        Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hidden ? "#5A6385" : l.Working ? "#3DDC97" : l.Paused ? "#FFB04D" : "#5A6385"))
    };

    static string Clock(double sec)
    {
        int s = (int)sec;
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    /// <summary>"Çalışıyor · 00:12:45" gibi canlı durum yazısı; çalışıyorsa her saniye kendini günceller.</summary>
    TextBlock LiveText(LiveInfo l, bool hidden)
    {
        var t = new TextBlock { FontSize = 12 };
        void Upd()
        {
            t.Text = hidden ? "Durumunu paylaşmıyor" : l.Working ? "Çalışıyor · " + Clock(l.Elapsed) : l.Paused ? "Mola · " + Clock(l.Elapsed) : "Çevrimdışı";
            t.Foreground = !hidden && l.Working ? ParseBrush("#3DDC97") : Res("Muted");
        }
        Upd();
        if (l.Working && !hidden) socialTicks.Add(Upd);
        return t;
    }
}

// Sosyal sekmesinin yanındaki kırmızı rozet: bekleyen arkadaş isteği + oda daveti sayısı.
public partial class MainWindow
{
    Border? socialBadge;
    TextBlock? socialBadgeText;
    DispatcherTimer? pendingTimer;

    void InitSocialBadge()
    {
        socialBadgeText = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        socialBadge = new Border
        {
            Background = Res("Danger"), CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Child = socialBadgeText
        };
        NavSocialPanel.Children.Add(socialBadge);

        pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        pendingTimer.Tick += (_, _) => _ = CheckPending();
        var first = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        first.Tick += (_, _) => { first.Stop(); _ = CheckPending(); pendingTimer.Start(); };
        first.Start();
    }

    async Task CheckPending()
    {
        if (sync == null || socialBadge == null) return;
        if (!sync.SignedIn) { SetBadge(0); return; }
        try
        {
            social ??= new SocialService(sync);
            int n = (await social.GetFriendsAsync()).Count(f => f.Rel == "incoming") + (await social.GetRoomsAsync()).Count(r => r.Invited);
            SetBadge(n);
        }
        catch { /* çevrimdışı ya da sosyal şema kurulu değil: rozet değişmesin */ }
    }

    void SetBadge(int n)
    {
        if (socialBadge == null) return;
        socialBadgeText!.Text = n > 9 ? "9+" : n.ToString();
        socialBadge.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        NavSocial.ToolTip = n > 0 ? $"{n} bekleyen istek/davet" : null;
    }
}
