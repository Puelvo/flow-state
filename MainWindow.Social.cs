using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SPath = System.Windows.Shapes.Path;

namespace FlowState;

// Sosyal sekmesi: arkadaşlar (canlı durum) ve odalar (lider tablosu).
public partial class MainWindow
{
    static readonly (int days, string label)[] BoardPeriods = { (1, "Bugün"), (3, "3 gün"), (5, "5 gün"), (7, "1 hafta"), (14, "2 hafta"), (30, "1 ay") };

    // Avatar tonları: pastel zemin + aynı tonun koyusu (baş harf). Renk kullanıcı adından türetilir, her yerde aynı kalır.
    static readonly (string bg, string fg)[] SocAvatarTones =
    {
        ("#FFD9C7", "#C2573A"), // şeftali
        ("#CFF3E1", "#23855A"), // nane
        ("#D3E9FF", "#2F6FB8"), // gök
        ("#E6DDFF", "#6A4FC2"), // lavanta
        ("#FFD6E8", "#B83A78"), // pembe
        ("#FFF0B8", "#9A7410"), // tereyağı
        ("#CDF1EF", "#1F8580"), // turkuaz
        ("#FFDAD6", "#C2413A"), // mercan
    };

    // Kürsü madalyaları: (rozet zemini, yazı, kürsü zemini) — altın, gümüş, bronz
    static readonly (string bg, string fg, string stand)[] SocMedalTones =
    {
        ("#FFE9A8", "#B07D10", "#FFF4CF"),
        ("#E7EAF2", "#737B90", "#F2F4F9"),
        ("#F7D9C2", "#A8643A", "#FBEADD"),
    };

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
    Border socialMsgBox = null!, socMeAvatar = null!;
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

        SocialRoot.Children.Add(new TextBlock { Text = "Sosyal", Style = (Style)FindResource("PageTitle") });
        SocialRoot.Children.Add(new TextBlock { Text = "Arkadaşlarınla birlikte odaklan, odalarda tatlı bir yarışa gir.", Style = (Style)FindResource("PageSub") });

        // Bilgi / hata mesajı: yumuşak renkli bir hap
        socialMsg = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, FontSize = 13 };
        socialMsgBox = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Child = socialMsg
        };
        SocialRoot.Children.Add(socialMsgBox);

        socialGate = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
        socialMain = new StackPanel { Visibility = Visibility.Collapsed };
        SocialRoot.Children.Add(socialGate);
        SocialRoot.Children.Add(socialMain);

        // Arkadaşlar / Odalar segmenti
        var segRow = new StackPanel { Orientation = Orientation.Horizontal };
        segFriends = new RadioButton { GroupName = "socseg", Style = (Style)FindResource("Seg"), IsChecked = true, Content = SocIconText("IconPeople", "Arkadaşlar") };
        segRooms = new RadioButton { GroupName = "socseg", Style = (Style)FindResource("Seg"), Content = SocIconText("IconFlame", "Odalar") };
        segFriends.Checked += (_, _) => { if (!suppressSeg) SetPane("friends", true); };
        segRooms.Checked += (_, _) => { if (!suppressSeg) SetPane("rooms", true); };
        segRow.Children.Add(segFriends); segRow.Children.Add(segRooms);
        socialMain.Children.Add(new Border
        {
            Style = (Style)FindResource("SegBar"), HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 20, 0, 6), Child = segRow
        });

        // --- arkadaşlar ---
        friendsPane = new StackPanel();
        meText = new TextBlock { FontSize = 18, FontWeight = FontWeights.Black };
        shareBox = new CheckBox { Content = "Durumumu arkadaşlarım görsün", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        shareBox.Click += (_, _) =>
        {
            if (socialMe == null || social == null) return;
            var name = socialMe.Username; bool share = shareBox.IsChecked == true;
            SocialDo(async () => { await social.SetProfileAsync(name, share); socialMe.ShareStatus = share; });
        };
        socMeAvatar = new Border { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        var meGrid = new Grid();
        meGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var meInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        meInfo.Children.Add(meText);
        meInfo.Children.Add(new TextBlock { Text = "Arkadaşların seni bu kullanıcı adıyla ekler.", FontSize = 12.5, Style = (Style)FindResource("Body"), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(meInfo, 1); Grid.SetColumn(shareBox, 2);
        meGrid.Children.Add(socMeAvatar); meGrid.Children.Add(meInfo); meGrid.Children.Add(shareBox);

        addFriendBox = new TextBox { MaxLength = 20 };
        addFriendBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) AddFriend(); };
        var addBtn = SBtn("Ekle", AddFriend, accent: true, icon: "IconPlus");
        var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(addBtn, Dock.Right);
        addRow.Children.Add(addBtn);
        addRow.Children.Add(SocAtBox(addFriendBox));

        var meStack = new StackPanel();
        meStack.Children.Add(meGrid);
        meStack.Children.Add(new Border { Height = 2, Background = Res("Line"), Margin = new Thickness(0, 16, 0, 14), CornerRadius = new CornerRadius(1) });
        meStack.Children.Add(new TextBlock { Text = "Arkadaş ekle", FontWeight = FontWeights.ExtraBold, FontSize = 15 });
        meStack.Children.Add(new TextBlock { Text = "Kullanıcı adını yaz; kabul edince birbirinizin canlı durumunu görürsünüz.", FontSize = 12.5, Style = (Style)FindResource("Body"), Margin = new Thickness(0, 2, 0, 0) });
        meStack.Children.Add(addRow);
        friendsPane.Children.Add(SocCard(meStack, new Thickness(0, 14, 0, 0)));

        friendsList = new StackPanel();
        friendsPane.Children.Add(friendsList);
        socialMain.Children.Add(friendsPane);

        // --- odalar ---
        roomsPane = new StackPanel { Visibility = Visibility.Collapsed };
        roomNameBox = new TextBox { MaxLength = 40 };
        roomNameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) CreateRoom(); };
        roomCodeBox = new TextBox { MaxLength = 6, CharacterCasing = CharacterCasing.Upper, FontWeight = FontWeights.ExtraBold };
        roomCodeBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) JoinRoom(); };

        var roomTools = new Grid();
        roomTools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        roomTools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        roomTools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var createCol = SocToolColumn("Yeni oda kur", "Bir isim ver, davet kodunu arkadaşlarına gönder.", roomNameBox,
            SBtn("Oda kur", CreateRoom, accent: true, icon: "IconPlus"));
        var joinCol = SocToolColumn("Kodla katıl", "Arkadaşından aldığın 6 haneli kodu yaz.", roomCodeBox, SBtn("Katıl", JoinRoom));
        Grid.SetColumn(joinCol, 2);
        roomTools.Children.Add(createCol); roomTools.Children.Add(joinCol);
        roomsPane.Children.Add(SocCard(roomTools, new Thickness(0, 14, 0, 0)));

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
        if (error)
        {
            socialMsgBox.Background = Res("DangerSoft");
            socialMsg.Foreground = Res("Danger");
        }
        else
        {
            socialMsgBox.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            socialMsg.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeep");
        }
        socialMsgBox.Visibility = string.IsNullOrEmpty(m) ? Visibility.Collapsed : Visibility.Visible;
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
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        void Head(string icon, string bg, string fg, string title, string body)
        {
            var badge = SocIconBadge(icon, bg, fg, 76, icon == "IconSparkle");
            badge.HorizontalAlignment = HorizontalAlignment.Center;
            inner.Children.Add(badge);
            inner.Children.Add(new TextBlock
            {
                Text = title, FontSize = 21, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0)
            });
            inner.Children.Add(new TextBlock
            {
                Text = body, Style = (Style)FindResource("Body"), TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
            });
        }

        switch (kind)
        {
            case "signedout":
                Head("IconPeople", "AccentSoft", "Accent", "Arkadaşlarınla birlikte çalış",
                    "Arkadaşlarını ve odaları görmek için senkron hesabınla giriş yapmalısın. Ayarlar'dan bir dakikada hallolur.");
                var go = SBtn("Ayarlara git", () => NavSettings.IsChecked = true, accent: true);
                go.Margin = new Thickness(0, 20, 0, 0); go.HorizontalAlignment = HorizontalAlignment.Center; go.Padding = new Thickness(22, 10, 22, 10);
                inner.Children.Add(go);
                break;
            case "schema":
                Head("IconSliders", "WarnSoft", "Warn", "Sosyal özellikler henüz hazır değil", text ?? "");
                break;
            default: // profile
                Head("IconSparkle", "AccentSoft", "Accent", "Bir kullanıcı adı seç",
                    "Arkadaşların seni bu adla bulur. 3-20 karakter; küçük harf (a-z), rakam ve _ kullanılabilir.");
                var nameBox = new TextBox { MaxLength = 20 };
                var share = new CheckBox { Content = "Durumumu arkadaşlarım görsün", IsChecked = true, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
                void Save()
                {
                    var n = nameBox.Text.Trim().ToLowerInvariant();
                    SocialDo(async () => { await social!.SetProfileAsync(n, share.IsChecked == true); socialMe = null; });
                }
                nameBox.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Save(); };
                var box = SocAtBox(nameBox);
                box.Width = 300; box.Margin = new Thickness(0, 18, 0, 0); box.HorizontalAlignment = HorizontalAlignment.Center;
                var save = SBtn("Kaydet", Save, accent: true);
                save.Margin = new Thickness(0, 18, 0, 0); save.HorizontalAlignment = HorizontalAlignment.Center; save.Padding = new Thickness(26, 10, 26, 10);
                inner.Children.Add(box); inner.Children.Add(share); inner.Children.Add(save);
                break;
        }
        var card = SocCard(inner, new Thickness(0));
        card.Padding = new Thickness(36, 32, 36, 30);
        card.MaxWidth = 540;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        socialGate.Children.Add(card);
        SocPop(card);
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
        socMeAvatar.Child = SocAvatar(socialMe.Username, 54);
        shareBox.IsChecked = socialMe.ShareStatus;
        friendsList.Children.Clear(); socialTicks.Clear();

        var incoming = all.Where(f => f.Rel == "incoming").ToList();
        var friends = all.Where(f => f.Rel == "friend")
            .OrderBy(f => f.Live.Working ? 0 : f.Live.Paused ? 1 : 2).ThenBy(f => f.Username, StringComparer.Ordinal).ToList();
        var outgoing = all.Where(f => f.Rel == "outgoing").ToList();

        if (incoming.Count > 0)
        {
            friendsList.Children.Add(SHeader("Gelen istekler", incoming.Count));
            foreach (var f in incoming)
            {
                var row = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                btns.Children.Add(SBtn("Kabul et", () => SocialDo(() => social!.RespondFriendAsync(f.UserId, true)), accent: true));
                btns.Children.Add(SBtn("Reddet", () => SocialDo(() => social!.RespondFriendAsync(f.UserId, false))));
                DockPanel.SetDock(btns, Dock.Right);
                row.Children.Add(btns);
                var av = SocAvatar(f.Username, 42); av.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(av, Dock.Left);
                row.Children.Add(av);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = "@" + f.Username, FontWeight = FontWeights.ExtraBold, FontSize = 15 });
                t.Children.Add(new TextBlock { Text = "seninle arkadaş olmak istiyor", FontSize = 12.5, Style = (Style)FindResource("Body") });
                row.Children.Add(t);
                friendsList.Children.Add(SRow(row, true));
            }
        }

        friendsList.Children.Add(SHeader("Arkadaşlar", friends.Count));
        if (friends.Count == 0)
            friendsList.Children.Add(SocEmpty("IconPeople", "Henüz arkadaşın yok",
                "Yukarıya bir kullanıcı adı yazıp ekleyebilirsin. Birlikte çalışmak daha eğlenceli!"));
        foreach (var f in friends)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var av = SocAvatar(f.Username, 44, f.Live, f.Hidden); av.Margin = new Thickness(0, 0, 14, 0);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = "@" + f.Username, FontWeight = FontWeights.ExtraBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
            info.Children.Add(SocLivePill(f.Live, f.Hidden));
            var today = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            if (!f.Hidden)
            {
                today.Children.Add(new TextBlock { Text = "bugün", FontSize = 11.5, Foreground = Res("Faint"), FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
                today.Children.Add(new TextBlock { Text = Fmt(f.Today), FontSize = 15, FontWeight = FontWeights.ExtraBold, HorizontalAlignment = HorizontalAlignment.Right });
            }
            var del = SocIconBtn("IconClose", () =>
            {
                if (ConfirmDialog.Ask(this, "Arkadaşlıktan çıkar", $"@{f.Username} arkadaş listenden çıkarılsın mı?", "Çıkar"))
                    SocialDo(() => social!.RemoveFriendAsync(f.UserId));
            }, "Arkadaşlıktan çıkar");
            Grid.SetColumn(info, 1); Grid.SetColumn(today, 2); Grid.SetColumn(del, 3);
            g.Children.Add(av); g.Children.Add(info); g.Children.Add(today); g.Children.Add(del);
            friendsList.Children.Add(SRow(g));
        }

        if (outgoing.Count > 0)
        {
            friendsList.Children.Add(SHeader("Gönderilen istekler", outgoing.Count));
            foreach (var f in outgoing)
            {
                var row = new DockPanel();
                var cancel = SBtn("Geri çek", () => SocialDo(() => social!.RemoveFriendAsync(f.UserId)));
                DockPanel.SetDock(cancel, Dock.Right);
                row.Children.Add(cancel);
                var av = SocAvatar(f.Username, 36); av.Margin = new Thickness(0, 0, 12, 0); av.Opacity = 0.6;
                DockPanel.SetDock(av, Dock.Left);
                row.Children.Add(av);
                var t = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = "@" + f.Username, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                var pill = SocPill("yanıt bekleniyor", "Panel2", "Muted"); pill.Margin = new Thickness(10, 0, 0, 0);
                t.Children.Add(pill);
                row.Children.Add(t);
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
            roomsList.Children.Add(SHeader("Oda davetleri", invites.Count));
            foreach (var r in invites)
            {
                var row = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                btns.Children.Add(SBtn("Katıl", () => SocialDo(() => social!.RespondRoomAsync(r.Id, true)), accent: true));
                btns.Children.Add(SBtn("Reddet", () => SocialDo(() => social!.RespondRoomAsync(r.Id, false))));
                DockPanel.SetDock(btns, Dock.Right);
                row.Children.Add(btns);
                var badge = SocIconBadge("IconSparkle", "Surface", "Accent", 42, true); badge.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(badge, Dock.Left);
                row.Children.Add(badge);
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.ExtraBold, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis });
                t.Children.Add(new TextBlock { Text = $"@{r.InvitedBy} seni davet etti", FontSize = 12.5, Style = (Style)FindResource("Body") });
                row.Children.Add(t);
                roomsList.Children.Add(SRow(row, true));
            }
        }

        roomsList.Children.Add(SHeader("Odalarım", mine.Count));
        if (mine.Count == 0)
        {
            roomsList.Children.Add(SocEmpty("IconFlame", "Henüz bir odan yok",
                "Bir oda kur ya da arkadaşının davet kodunu gir. Kim daha çok çalışacak, görelim!"));
            return;
        }
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -14, 0) };
        foreach (var r in mine)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var (bg, fg) = SocAvatarTones[SocHash(r.Name) % SocAvatarTones.Length];
            var tile = new Border
            {
                Width = 50, Height = 50, CornerRadius = new CornerRadius(17), Background = ParseBrush(bg),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
                Child = new TextBlock
                {
                    Text = SocInitial(r.Name), FontSize = 22, FontWeight = FontWeights.Black, Foreground = ParseBrush(fg),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameLine = new DockPanel();
            if (r.IsOwner)
            {
                var own = SocPill("kurucu", "WarnSoft", "Text"); own.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(own, Dock.Right);
                nameLine.Children.Add(own);
            }
            nameLine.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.ExtraBold, FontSize = 16, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            t.Children.Add(nameLine);
            var pills = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var p1 = SocPill($"{r.Members} kişi", "Panel2", "Muted"); p1.Margin = new Thickness(0, 0, 6, 0);
            pills.Children.Add(p1);
            pills.Children.Add(SocPill(SocialService.PeriodLabel(r.PeriodDays), "AccentSoft", "AccentDeep"));
            t.Children.Add(pills);
            var chevron = new SPath
            {
                Data = Geometry.Parse("M9,5 L16,12 L9,19"), Style = (Style)FindResource("Icon"), Width = 14, Height = 14,
                Stroke = Res("Faint"), Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(t, 1); Grid.SetColumn(chevron, 2);
            g.Children.Add(tile); g.Children.Add(t); g.Children.Add(chevron);

            var card = SocCard(g, new Thickness(0, 0, 14, 14));
            card.Padding = new Thickness(16, 16, 16, 16);
            card.Cursor = System.Windows.Input.Cursors.Hand;
            var lift = new TranslateTransform();
            card.RenderTransform = lift;
            card.MouseEnter += (_, _) => { card.Effect = (Effect)FindResource("LiftShadow"); lift.Y = -3; chevron.SetResourceReference(Shape.StrokeProperty, "Accent"); };
            card.MouseLeave += (_, _) => { card.ClearValue(EffectProperty); lift.Y = 0; chevron.Stroke = Res("Faint"); };
            var rr = r;
            card.MouseLeftButtonUp += (_, _) => OpenRoom(rr.Id, rr.PeriodDays);
            grid.Children.Add(card);
        }
        roomsList.Children.Add(grid);
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

            var back = new Button { Style = (Style)FindResource("Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-10, 16, 0, 0) };
            var backContent = new StackPanel { Orientation = Orientation.Horizontal };
            backContent.Children.Add(new SPath { Data = Geometry.Parse("M15,5 L8,12 L15,19"), Style = (Style)FindResource("Icon"), Width = 12, Height = 12 });
            backContent.Children.Add(new TextBlock { Text = "Odalar", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            back.Content = backContent;
            back.Click += (_, _) => { openRoomId = null; SetPane("rooms"); };
            boardHead.Children.Add(back);

            // Başlık + davet kodu hapı
            var top = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var codePill = new Border
            {
                Background = Res("Surface"), CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 6, 6, 6),
                Effect = (Effect)FindResource("SoftShadow"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
            };
            var codeRow = new StackPanel { Orientation = Orientation.Horizontal };
            codeRow.Children.Add(new TextBlock { Text = "Davet kodu", Foreground = Res("Muted"), FontSize = 12.5, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            var codeText = new TextBlock { Text = string.Join(" ", b.Code.ToCharArray()), FontWeight = FontWeights.Black, FontSize = 17, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            codeText.SetResourceReference(TextBlock.ForegroundProperty, "AccentDeep");
            codeRow.Children.Add(codeText);
            var copy = SBtn("Kopyala", () => { try { Clipboard.SetText(b.Code); SocialMsg("Davet kodu kopyalandı."); } catch { } });
            codeRow.Children.Add(copy);
            codePill.Child = codeRow;
            DockPanel.SetDock(codePill, Dock.Right);
            top.Children.Add(codePill);
            top.Children.Add(new TextBlock { Text = b.Name, FontSize = 26, FontWeight = FontWeights.Black, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            boardHead.Children.Add(top);

            boardHead.Children.Add(SHeader("Sıralama aralığı"));
            var chips = new WrapPanel();
            bool custom = BoardPeriods.All(p => p.days != boardDays);
            var segs = new WrapPanel();
            foreach (var (days, label) in BoardPeriods)
            {
                var d = days;
                var chip = new RadioButton { Content = label, GroupName = "socboard", Style = (Style)FindResource("Seg"), IsChecked = !custom && boardDays == d };
                chip.Click += (_, _) => SetBoardDays(d);
                segs.Children.Add(chip);
            }
            chips.Children.Add(new Border { Style = (Style)FindResource("SegBar"), Margin = new Thickness(0, 0, 12, 8), Child = segs });

            var box = new TextBox { Width = 58, MaxLength = 3, Text = custom ? boardDays.ToString() : "", TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            var apply = new Action(() => { if (int.TryParse(box.Text.Trim(), out var n) && n >= 1) SetBoardDays(n); });
            box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) apply(); };
            var customRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            customRow.Children.Add(new TextBlock { Text = "Özel:", Foreground = Res("Muted"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            customRow.Children.Add(box);
            customRow.Children.Add(new TextBlock { Text = "gün", Foreground = Res("Muted"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            customRow.Children.Add(SBtn("Uygula", apply, accent: custom));
            chips.Children.Add(customRow);
            boardHead.Children.Add(chips);
            boardHead.Children.Add(new TextBlock
            {
                Text = $"Son {boardDays} gün (bugün dahil) · " + (b.IsOwner ? "Seçtiğin aralık odanın varsayılanı olarak kaydedilir." : "Bu seçim yalnızca senin ekranında geçerli."),
                FontSize = 12.5, Style = (Style)FindResource("Body"), Margin = new Thickness(4, 2, 0, 0)
            });
            boardHead.Children.Add(SHeader("Lider tablosu"));
        }

        // Sıralama
        boardList.Children.Clear();
        var active = b.Members.Where(m => !m.Invited).ToList();
        double top1 = Math.Max(1, active.Count == 0 ? 1 : active.Max(m => m.Total));

        // İlk üç: kürsü
        var podium = active.Where(m => m.Rank is >= 1 and <= 3 && m.Total >= 1).OrderBy(m => m.Rank).Take(3).ToList();
        if (podium.Count > 0) boardList.Children.Add(SocPodium(podium));

        foreach (var m in active)
        {
            var mm = m;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var rank = SocRankBadge(m.Rank, 30);
            rank.HorizontalAlignment = HorizontalAlignment.Left;
            var av = SocAvatar(m.Username, 42, m.Live, m.Hidden); av.Margin = new Thickness(0, 0, 14, 0);

            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock { Text = m.Username, FontWeight = FontWeights.ExtraBold, FontSize = m.Rank == 1 ? 16 : 15, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (m.IsMe) { var p = SocPill("sen", "Accent", "OnAccent"); p.Margin = new Thickness(8, 0, 0, 0); nameLine.Children.Add(p); }
            if (m.IsOwner) { var p = SocPill("kurucu", "WarnSoft", "Text"); p.Margin = new Thickness(6, 0, 0, 0); nameLine.Children.Add(p); }
            mid.Children.Add(nameLine);
            mid.Children.Add(SocLivePill(m.Live, m.Hidden));

            var ratio = Math.Max(0.004, m.Total / top1);
            var bar = new Grid { Height = 8, Margin = new Thickness(0, 8, 16, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star) });
            var trackBg = new Border { CornerRadius = new CornerRadius(4), Background = Res("Track") };
            Grid.SetColumnSpan(trackBg, 2);
            bar.Children.Add(trackBg);
            var fill = new Border { CornerRadius = new CornerRadius(4), Opacity = m.Total >= 1 ? 1 : 0 };
            fill.SetResourceReference(Border.BackgroundProperty, "Accent");
            bar.Children.Add(fill);
            mid.Children.Add(bar);

            var time = new TextBlock { FontWeight = FontWeights.Black, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            void Upd() => time.Text = Fmt(mm.Total);
            Upd();
            if (m.Live.Working) socialTicks.Add(Upd);

            Grid.SetColumn(av, 1); Grid.SetColumn(mid, 2); Grid.SetColumn(time, 3);
            g.Children.Add(rank); g.Children.Add(av); g.Children.Add(mid); g.Children.Add(time);
            if (b.IsOwner && !m.IsMe)
            {
                var kick = SocIconBtn("IconClose", () =>
                {
                    if (ConfirmDialog.Ask(this, "Odadan çıkar", $"@{mm.Username} odadan çıkarılsın mı?", "Çıkar"))
                        SocialDo(() => social!.KickAsync(b.Id, mm.UserId));
                }, "Odadan çıkar");
                Grid.SetColumn(kick, 4); g.Children.Add(kick);
            }
            boardList.Children.Add(SRow(g, m.IsMe));
        }
        if (active.All(m => m.Total < 1))
            boardList.Children.Add(SocEmpty("IconFlame", "Yarış henüz başlamadı",
                "Bu aralıkta henüz kimse çalışmadı. İlk çalışan kürsünün tepesine çıkar!"));

        // Davetliler + davet edilebilecek arkadaşlar + ayrıl
        boardInvite.Children.Clear();
        var pending = b.Members.Where(m => m.Invited).ToList();
        if (pending.Count > 0)
        {
            boardInvite.Children.Add(SHeader("Davet edilenler", pending.Count));
            foreach (var m in pending)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var av = SocAvatar(m.Username, 34); av.Opacity = 0.6; av.Margin = new Thickness(0, 0, 12, 0);
                row.Children.Add(av);
                row.Children.Add(new TextBlock { Text = "@" + m.Username, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
                var pill = SocPill("yanıt bekleniyor", "Panel2", "Muted"); pill.Margin = new Thickness(10, 0, 0, 0);
                row.Children.Add(pill);
                boardInvite.Children.Add(SRow(row));
            }
        }
        var inRoom = b.Members.Select(m => m.UserId).ToHashSet();
        var invitable = friends.Where(f => f.Rel == "friend" && !inRoom.Contains(f.UserId)).ToList();
        if (invitable.Count > 0)
        {
            boardInvite.Children.Add(SHeader("Arkadaşını davet et"));
            var wrap = new WrapPanel();
            foreach (var f in invitable)
            {
                var ff = f;
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(SocAvatar(f.Username, 24));
                content.Children.Add(new TextBlock { Text = f.Username, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new SPath { Data = (Geometry)FindResource("IconPlus"), Style = (Style)FindResource("Icon"), Width = 12, Height = 12 });
                var btn = new Button { Style = (Style)FindResource("Btn"), Background = Res("Surface"), Content = content, Padding = new Thickness(6, 6, 12, 6), FontSize = 13, Margin = new Thickness(0, 0, 8, 8) };
                btn.Click += (_, _) => SocialDo(() => social!.InviteAsync(b.Id, ff.UserId), $"@{ff.Username} davet edildi.");
                wrap.Children.Add(btn);
            }
            boardInvite.Children.Add(wrap);
        }
        var leave = SBtn(b.IsOwner ? "Odayı sil" : "Odadan ayrıl", () =>
        {
            var msg = b.IsOwner ? $"\"{b.Name}\" odası herkes için silinecek." : $"\"{b.Name}\" odasından ayrılmak istiyor musun?";
            if (ConfirmDialog.Ask(this, b.IsOwner ? "Odayı sil" : "Odadan ayrıl", msg, b.IsOwner ? "Sil" : "Ayrıl"))
                SocialDo(async () => { await social!.LeaveRoomAsync(b.Id); openRoomId = null; SetPane("rooms"); });
        }, danger: true, icon: b.IsOwner ? "IconTrash" : null);
        leave.Margin = new Thickness(0, 26, 0, 0); leave.HorizontalAlignment = HorizontalAlignment.Left;
        boardInvite.Children.Add(leave);
    }

    /// <summary>İlk üç kişi için kürsü: ortada birinci (en yüksek), solda ikinci, sağda üçüncü.</summary>
    FrameworkElement SocPodium(List<BoardMember> top)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int[] col = { 1, 0, 2 };          // sıradaki yer → sütun
        double[] stand = { 86, 62, 46 };  // kürsü yükseklikleri
        for (int i = 0; i < top.Count; i++)
        {
            var m = top[i];
            int medal = Math.Clamp(m.Rank, 1, 3) - 1;
            var (mbg, mfg, mstand) = SocMedalTones[medal];
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, 0) };

            if (i == 0)
                stack.Children.Add(new SPath
                {
                    Data = (Geometry)FindResource("IconSparkle"), Fill = Res("Coin"), Width = 20, Height = 20, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4)
                });
            double size = i == 0 ? 66 : 52;
            var avHost = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            var av = SocAvatar(m.Username, size);
            avHost.Children.Add(new Ellipse { Width = size + 8, Height = size + 8, Fill = ParseBrush(mbg) });
            av.HorizontalAlignment = HorizontalAlignment.Center; av.VerticalAlignment = VerticalAlignment.Center;
            avHost.Children.Add(av);
            var num = SocRankBadge(m.Rank, 24);
            num.HorizontalAlignment = HorizontalAlignment.Right; num.VerticalAlignment = VerticalAlignment.Bottom;
            num.Margin = new Thickness(0, 0, -4, -2);
            avHost.Children.Add(num);
            stack.Children.Add(avHost);

            stack.Children.Add(new TextBlock
            {
                Text = m.Username, FontWeight = FontWeights.ExtraBold, FontSize = i == 0 ? 15 : 14, HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 8, 0, 0)
            });
            var time = new TextBlock { FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Res("Muted"), HorizontalAlignment = HorizontalAlignment.Center };
            var mm = m;
            void Upd() => time.Text = Fmt(mm.Total);
            Upd();
            if (m.Live.Working) socialTicks.Add(Upd);
            stack.Children.Add(time);

            stack.Children.Add(new Border
            {
                Height = stand[i], CornerRadius = new CornerRadius(20, 20, 8, 8), Background = ParseBrush(mstand), Margin = new Thickness(0, 10, 0, 0),
                Child = new TextBlock
                {
                    Text = m.Rank.ToString(), FontSize = i == 0 ? 30 : 24, FontWeight = FontWeights.Black, Foreground = ParseBrush(mfg),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            });
            Grid.SetColumn(stack, col[i]);
            g.Children.Add(stack);
        }
        var card = SocCard(g, new Thickness(0, 0, 0, 12));
        card.Padding = new Thickness(18, 18, 18, 0);
        return card;
    }

    // ---------- küçük yapı taşları ----------

    Button SBtn(string text, Action click, bool danger = false, bool accent = false, string? icon = null)
    {
        var style = accent ? (Style)FindResource("Primary") : danger ? (Style)FindResource("DangerBtn") : (Style)FindResource("Btn");
        var b = new Button
        {
            Style = style, Padding = new Thickness(14, 7, 14, 7), FontSize = 13,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        b.Content = icon == null ? text : SocIconText(icon, text, 13);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>İkon + yazı (düğme ve segment içerikleri için). İkon rengi düğmenin yazı rengini izler.</summary>
    StackPanel SocIconText(string icon, string text, double size = 16)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new SPath { Data = (Geometry)FindResource(icon), Style = (Style)FindResource("Icon"), Width = size, Height = size });
        sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    /// <summary>Zeminsiz küçük ikon düğmesi (çıkar / sil).</summary>
    Button SocIconBtn(string icon, Action click, string tip)
    {
        var b = new Button
        {
            Style = (Style)FindResource("Ghost"), Padding = new Thickness(9), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center,
            Content = new SPath { Data = (Geometry)FindResource(icon), Style = (Style)FindResource("Icon"), Width = 12, Height = 12 }
        };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Satır: beyaz, yuvarlak, yumuşak gölgeli. me = vurgulu (açık tema rengi) zemin.</summary>
    Border SRow(UIElement child, bool me = false)
    {
        var b = new Border
        {
            Background = Res("Surface"), CornerRadius = new CornerRadius(20), Effect = (Effect)FindResource("SoftShadow"),
            Padding = new Thickness(14, 12, 12, 12), Margin = new Thickness(0, 0, 0, 10), Child = child
        };
        if (me) b.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        return b;
    }

    Border SocCard(UIElement child, Thickness margin) => new() { Style = (Style)FindResource("Card"), Margin = margin, Child = child };

    /// <summary>Bölüm başlığı; sayı verilirse yanında küçük bir hap.</summary>
    FrameworkElement SHeader(string t, int? count = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 26, 0, 12) };
        sp.Children.Add(new TextBlock { Text = t, Style = (Style)FindResource("H"), Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center });
        if (count != null)
        {
            var pill = SocPill(count.Value.ToString(), "AccentSoft", "AccentDeep");
            pill.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(pill);
        }
        return sp;
    }

    /// <summary>Küçük yuvarlak etiket. Renkler kaynak adıdır (dinamik bağlanır).</summary>
    Border SocPill(string text, string bgKey, string fgKey)
    {
        var t = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.ExtraBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, fgKey);
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = t };
        b.SetResourceReference(Border.BackgroundProperty, bgKey);
        return b;
    }

    /// <summary>Yuvarlak, pastel zeminli ikon rozeti.</summary>
    Grid SocIconBadge(string icon, string bgKey, string fgKey, double size, bool filled = false)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var e = new Ellipse();
        e.SetResourceReference(Shape.FillProperty, bgKey);
        g.Children.Add(e);
        var p = new SPath { Data = (Geometry)FindResource(icon), Style = (Style)FindResource("Icon"), Width = size * 0.46, Height = size * 0.46 };
        p.SetResourceReference(filled ? Shape.FillProperty : Shape.StrokeProperty, fgKey);
        if (filled) p.StrokeThickness = 0;
        g.Children.Add(p);
        return g;
    }

    /// <summary>Boş durum: sevimli ikon + kısa, sıcak bir cümle.</summary>
    Border SocEmpty(string icon, string title, string text)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = SocIconBadge(icon, "AccentSoft", "Accent", 52);
        badge.Margin = new Thickness(0, 0, 16, 0);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.ExtraBold, FontSize = 15 });
        t.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Body"), FontSize = 13, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(t, 1);
        g.Children.Add(badge); g.Children.Add(t);
        return SocCard(g, new Thickness(0, 0, 0, 10));
    }

    /// <summary>Başında soluk bir "@" olan kullanıcı adı kutusu.</summary>
    Grid SocAtBox(TextBox box)
    {
        var g = new Grid();
        box.Padding = new Thickness(26, 9, 12, 9);
        g.Children.Add(box);
        g.Children.Add(new TextBlock
        {
            Text = "@", Foreground = Res("Faint"), FontWeight = FontWeights.Black, FontSize = 15, IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 1)
        });
        return g;
    }

    /// <summary>Odalar sekmesindeki "oda kur / kodla katıl" sütunu.</summary>
    StackPanel SocToolColumn(string title, string text, TextBox box, Button btn)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.ExtraBold, FontSize = 15 });
        sp.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Body"), FontSize = 12.5, Margin = new Thickness(0, 2, 0, 10) });
        var row = new DockPanel();
        DockPanel.SetDock(btn, Dock.Right);
        row.Children.Add(btn);
        row.Children.Add(box);
        sp.Children.Add(row);
        return sp;
    }

    // Kararlı karma (string.GetHashCode her açılışta değişir; avatar rengi sabit kalsın)
    static int SocHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) h = (h ^ c) * 16777619;
            return (int)(h & 0x7FFFFFFF);
        }
    }

    static string SocInitial(string name)
    {
        foreach (char c in name)
            if (char.IsLetterOrDigit(c)) return char.ToUpper(c, Tr).ToString();
        return "?";
    }

    /// <summary>Sevimli avatar: kullanıcı adından renklenen pastel daire + baş harf; canlı durum verilirse sağ altta nokta.</summary>
    Grid SocAvatar(string name, double size, LiveInfo? live = null, bool hidden = false)
    {
        var (bg, fg) = SocAvatarTones[SocHash(name) % SocAvatarTones.Length];
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = ParseBrush(bg) });
        // üstte küçük parlama: şeker görünümü
        g.Children.Add(new Ellipse
        {
            Width = size * 0.3, Height = size * 0.18, Fill = Brushes.White, Opacity = 0.5, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(size * 0.2, size * 0.12, 0, 0)
        });
        g.Children.Add(new TextBlock
        {
            Text = SocInitial(name), FontSize = Math.Max(10, size * 0.42), FontWeight = FontWeights.Black, Foreground = ParseBrush(fg),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });
        if (live != null)
        {
            double d = Math.Round(size * 0.32);
            var host = new Grid { Width = d, Height = d, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2) };
            if (!hidden && live.Working)
            {
                // çalışırken nokta yavaşça "nefes alır"
                var halo = new Ellipse { Fill = Res("Good"), RenderTransformOrigin = new Point(0.5, 0.5) };
                var sc = new ScaleTransform(1, 1);
                halo.RenderTransform = sc;
                var grow = new DoubleAnimation(1, 1.9, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever };
                var fade = new DoubleAnimation(0.5, 0, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                halo.BeginAnimation(OpacityProperty, fade);
                host.Children.Add(halo);
            }
            var dot = StatusDot(live, hidden);
            dot.Width = dot.Height = d;
            dot.Stroke = Res("Surface"); dot.StrokeThickness = 2.5;
            host.Children.Add(dot);
            g.Children.Add(host);
        }
        return g;
    }

    /// <summary>Sıra rozeti: ilk üç altın / gümüş / bronz, diğerleri sade.</summary>
    Grid SocRankBadge(int rank, double size)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        var e = new Ellipse();
        var t = new TextBlock
        {
            Text = rank > 0 ? rank.ToString() : "–", FontSize = size * 0.46, FontWeight = FontWeights.Black,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        if (rank is >= 1 and <= 3)
        {
            var (bg, fg, _) = SocMedalTones[rank - 1];
            e.Fill = ParseBrush(bg); t.Foreground = ParseBrush(fg);
            e.Stroke = Res("Surface"); e.StrokeThickness = 2;
        }
        else { e.Fill = Res("Panel2"); t.Foreground = Res("Muted"); }
        g.Children.Add(e); g.Children.Add(t);
        return g;
    }

    /// <summary>Yeni beliren kart için kısa bir "pıt" animasyonu.</summary>
    static void SocPop(FrameworkElement el)
    {
        var sc = new ScaleTransform(0.94, 0.94);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = sc;
        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        var a = new DoubleAnimation(0.94, 1, TimeSpan.FromSeconds(0.35)) { EasingFunction = ease };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.25)));
    }

    Ellipse StatusDot(LiveInfo l, bool hidden) => new()
    {
        Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center,
        Fill = Res(hidden ? "Faint" : l.Working ? "Good" : l.Paused ? "Warn" : "Faint")
    };

    static string Clock(double sec)
    {
        int s = (int)sec;
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    /// <summary>"Çalışıyor · 00:12:45" gibi canlı durum yazısı; çalışıyorsa her saniye kendini günceller.</summary>
    TextBlock LiveText(LiveInfo l, bool hidden)
    {
        var t = new TextBlock { FontSize = 12, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        void Upd()
        {
            t.Text = hidden ? "Durumunu paylaşmıyor" : l.Working ? "Çalışıyor · " + Clock(l.Elapsed) : l.Paused ? "Mola · " + Clock(l.Elapsed) : "Çevrimdışı";
            t.Foreground = Res(hidden ? "Muted" : l.Working ? "Good" : l.Paused ? "Text" : "Muted");
        }
        Upd();
        if (l.Working && !hidden) socialTicks.Add(Upd);
        return t;
    }

    /// <summary>Canlı durum hapı: çalışıyor (yeşil), mola (turuncu), çevrimdışı (soluk).</summary>
    Border SocLivePill(LiveInfo l, bool hidden)
    {
        string bg = hidden ? "Panel2" : l.Working ? "GoodSoft" : l.Paused ? "WarnSoft" : "Panel2";
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = StatusDot(l, hidden);
        dot.Width = dot.Height = 7; dot.Margin = new Thickness(0, 0, 6, 0);
        sp.Children.Add(dot);
        sp.Children.Add(LiveText(l, hidden));
        return new Border
        {
            Background = Res(bg), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 0), Child = sp
        };
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
