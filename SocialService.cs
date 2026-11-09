using System.Text.Json.Nodes;

namespace FlowState;

public class MyProfile
{
    public string Username { get; set; } = "";
    public bool ShareStatus { get; set; } = true;
}

/// <summary>Canlı durum. Süre sunucudan "alındığı anki" değerdir; ekranda Elapsed ile akıtılır.</summary>
public class LiveInfo
{
    public string State { get; set; } = "offline"; // working | paused | offline
    double baseSeconds; DateTime fetched = DateTime.UtcNow;

    public bool Working => State == "working";
    public bool Paused => State == "paused";
    public bool Online => State != "offline";
    public double Elapsed => baseSeconds + (Working ? Math.Max(0, (DateTime.UtcNow - fetched).TotalSeconds) : 0);

    public static LiveInfo From(JsonNode? n)
    {
        var l = new LiveInfo();
        if (n is JsonObject o)
        {
            l.State = o["state"]?.ToString() ?? "offline";
            l.baseSeconds = o["elapsed"]?.GetValue<double>() ?? 0;
        }
        return l;
    }
}

public class FriendInfo
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Rel { get; set; } = "friend"; // friend | incoming | outgoing
    public bool Hidden { get; set; }            // arkadaş durumunu paylaşmıyor
    public LiveInfo Live { get; set; } = new();
    public long Today { get; set; }
}

public class RoomInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public int PeriodDays { get; set; } = 7;
    public bool IsOwner { get; set; }
    public bool Invited { get; set; }
    public int Members { get; set; }
    public string InvitedBy { get; set; } = "";
}

public class BoardMember
{
    public string UserId { get; set; } = "";
    public string Username { get; set; } = "";
    public bool Invited { get; set; }
    public bool IsOwner { get; set; }
    public bool IsMe { get; set; }
    public bool Hidden { get; set; }
    public long Seconds { get; set; }
    public LiveInfo Live { get; set; } = new();
    /// <summary>Bitmiş seanslar + şu an süren seans (canlı).</summary>
    public double Total => Seconds + (Live.Online ? Live.Elapsed : 0);
    public int Rank { get; set; } // 1 tabanlı; süresi olmayanda 0
}

public class Board
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public int PeriodDays { get; set; }
    public bool IsOwner { get; set; }
    public List<BoardMember> Members { get; set; } = new();
}

/// <summary>Arkadaş ve oda işlemleri. Tüm veriler Supabase'deki social_* fonksiyonlarından gelir.</summary>
public sealed class SocialService
{
    readonly SyncService sync;
    public SocialService(SyncService sync) { this.sync = sync; }

    public bool SignedIn => sync.SignedIn;

    /// <summary>Son N günün başlangıcı (bugün dahil, yerel gece yarısı) → UTC.</summary>
    public static DateTime SinceDays(int days) => DateTime.Today.AddDays(-(Math.Max(1, days) - 1)).ToUniversalTime();

    /// <summary>"Son N gün" için kısa Türkçe etiket.</summary>
    public static string PeriodLabel(int days) => days switch
    {
        1 => "Bugün",
        7 => "1 hafta",
        14 => "2 hafta",
        30 => "1 ay",
        60 => "2 ay",
        90 => "3 ay",
        365 => "1 yıl",
        _ => days % 7 == 0 && days <= 56 ? $"{days / 7} hafta" : $"{days} gün"
    };

    static string Iso(DateTime d) => d.ToUniversalTime().ToString("o");

    public async Task<MyProfile?> GetMeAsync()
    {
        if (await sync.RpcAsync("social_me") is not JsonObject o) return null;
        return new MyProfile { Username = o["username"]?.ToString() ?? "", ShareStatus = o["share_status"]?.GetValue<bool>() ?? true };
    }

    public Task SetProfileAsync(string username, bool share) =>
        sync.RpcAsync("social_set_profile", new JsonObject { ["p_username"] = username, ["p_share"] = share });

    public async Task<List<FriendInfo>> GetFriendsAsync()
    {
        var res = await sync.RpcAsync("social_friends", new JsonObject { ["p_since"] = Iso(SinceDays(1)) });
        var list = new List<FriendInfo>();
        foreach (var n in res?.AsArray() ?? new JsonArray())
        {
            if (n is not JsonObject o) continue;
            list.Add(new FriendInfo
            {
                UserId = o["user_id"]!.ToString(), Username = o["username"]!.ToString(), Rel = o["rel"]!.ToString(),
                Hidden = o["hidden"]?.GetValue<bool>() ?? false, Live = LiveInfo.From(o["live"]),
                Today = o["today"]?.GetValue<long>() ?? 0
            });
        }
        return list;
    }

    /// <summary>"sent" (istek gitti) ya da "accepted" (karşı taraf zaten istemişti).</summary>
    public async Task<string> AddFriendAsync(string username) =>
        (await sync.RpcAsync("social_friend_request", new JsonObject { ["p_username"] = username }))?.ToString() ?? "sent";

    public Task RespondFriendAsync(string userId, bool accept) =>
        sync.RpcAsync("social_friend_respond", new JsonObject { ["p_user"] = userId, ["p_accept"] = accept });

    public Task RemoveFriendAsync(string userId) =>
        sync.RpcAsync("social_friend_remove", new JsonObject { ["p_user"] = userId });

    public async Task<List<RoomInfo>> GetRoomsAsync()
    {
        var res = await sync.RpcAsync("social_rooms");
        var list = new List<RoomInfo>();
        foreach (var n in res?.AsArray() ?? new JsonArray())
        {
            if (n is not JsonObject o) continue;
            list.Add(new RoomInfo
            {
                Id = o["id"]!.ToString(), Name = o["name"]!.ToString(), Code = o["code"]?.ToString() ?? "",
                PeriodDays = o["period_days"]?.GetValue<int>() ?? 7, IsOwner = o["is_owner"]?.GetValue<bool>() ?? false,
                Invited = o["status"]?.ToString() == "invited", Members = o["members"]?.GetValue<int>() ?? 0,
                InvitedBy = o["invited_by"]?.ToString() ?? ""
            });
        }
        return list;
    }

    public async Task<string> CreateRoomAsync(string name, int days) =>
        (await sync.RpcAsync("social_room_create", new JsonObject { ["p_name"] = name, ["p_days"] = days }))?["id"]?.ToString() ?? "";

    public async Task<string> JoinRoomAsync(string code) =>
        (await sync.RpcAsync("social_room_join", new JsonObject { ["p_code"] = code }))?.ToString() ?? "";

    public Task InviteAsync(string roomId, string userId) =>
        sync.RpcAsync("social_room_invite", new JsonObject { ["p_room"] = roomId, ["p_user"] = userId });

    public Task RespondRoomAsync(string roomId, bool accept) =>
        sync.RpcAsync("social_room_respond", new JsonObject { ["p_room"] = roomId, ["p_accept"] = accept });

    public Task LeaveRoomAsync(string roomId) =>
        sync.RpcAsync("social_room_leave", new JsonObject { ["p_room"] = roomId });

    public Task KickAsync(string roomId, string userId) =>
        sync.RpcAsync("social_room_kick", new JsonObject { ["p_room"] = roomId, ["p_user"] = userId });

    public Task SetRoomPeriodAsync(string roomId, int days) =>
        sync.RpcAsync("social_room_set_period", new JsonObject { ["p_room"] = roomId, ["p_days"] = days });

    /// <summary>Odanın lider tablosu, seçilen gün aralığı için. Sıralama canlı süreyi de içerir.</summary>
    public async Task<Board> GetBoardAsync(string roomId, int days)
    {
        var o = (JsonObject)(await sync.RpcAsync("social_room_board",
            new JsonObject { ["p_room"] = roomId, ["p_since"] = Iso(SinceDays(days)) }))!;
        var b = new Board
        {
            Id = o["id"]!.ToString(), Name = o["name"]!.ToString(), Code = o["code"]?.ToString() ?? "",
            PeriodDays = o["period_days"]?.GetValue<int>() ?? 7, IsOwner = o["is_owner"]?.GetValue<bool>() ?? false
        };
        foreach (var n in o["members"]?.AsArray() ?? new JsonArray())
        {
            if (n is not JsonObject m) continue;
            b.Members.Add(new BoardMember
            {
                UserId = m["user_id"]!.ToString(), Username = m["username"]!.ToString(),
                Invited = m["status"]?.ToString() == "invited", IsOwner = m["is_owner"]?.GetValue<bool>() ?? false,
                IsMe = m["is_me"]?.GetValue<bool>() ?? false, Hidden = m["hidden"]?.GetValue<bool>() ?? false,
                Seconds = m["seconds"]?.GetValue<long>() ?? 0, Live = LiveInfo.From(m["live"])
            });
        }
        // Üyeler toplam süreye göre sıralanır; davetliler en sonda. Süresi 0 olana sıra/madalya verilmez.
        b.Members = b.Members.OrderBy(m => m.Invited).ThenByDescending(m => m.Total)
                             .ThenBy(m => m.Username, StringComparer.Ordinal).ToList();
        int rank = 0;
        foreach (var m in b.Members.Where(m => !m.Invited && m.Total >= 1)) m.Rank = ++rank;
        return b;
    }

    /// <summary>Sunucu hata kodunu kullanıcıya gösterilecek Türkçe metne çevirir.</summary>
    public static string Describe(Exception ex)
    {
        var code = ex is SocialException ? ex.Message : "offline";
        return code switch
        {
            "not_signed_in" => "Önce Ayarlar'dan hesabına giriş yap.",
            "offline" => "Bağlanılamadı. İnternetini kontrol edip tekrar dene.",
            "schema_missing" => "Sosyal özellikler henüz kurulmamış. Supabase'de supabase/social.sql dosyasını bir kez çalıştır.",
            "invalid_username" => "Kullanıcı adı 3-20 karakter olmalı; yalnızca harf (a-z), rakam ve _ kullanılabilir.",
            "username_taken" => "Bu kullanıcı adı alınmış.",
            "no_profile" => "Önce bir kullanıcı adı seçmelisin.",
            "user_not_found" => "Bu kullanıcı adında biri bulunamadı.",
            "self" => "Bu işlemi kendin için yapamazsın.",
            "already_friends" => "Zaten arkadaşsınız.",
            "already_requested" => "Bu kişiye zaten istek gönderdin.",
            "too_many_friends" => "Arkadaş sınırına ulaştın.",
            "invalid_name" => "Oda adı 1-40 karakter olmalı.",
            "too_many_rooms" => "En fazla 10 oda kurabilirsin.",
            "room_not_found" => "Bu koda ait oda bulunamadı.",
            "room_full" => "Oda dolu.",
            "not_member" => "Bu odanın üyesi değilsin.",
            "not_owner" => "Bunu yalnızca oda sahibi yapabilir.",
            "not_friends" => "Yalnızca arkadaşlarını davet edebilirsin.",
            _ => "Bir hata oluştu: " + code
        };
    }
}
