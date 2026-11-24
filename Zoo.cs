namespace FlowState;

/// <summary>Bir hayvan türü. Fiyat sabittir: kaç hayvan alınmış olursa olsun değişmez.
/// Görsel şimdilik emoji; ileride Sprite alanı eklenip tek yerden değişecek.</summary>
public sealed record Animal(string Id, string Name, string Emoji, string MapId, int Price);

/// <summary>Bir harita (tema). Sıralı açılır; aktif harita arka planı ve marketi belirler.</summary>
public sealed record ZooMap(string Id, string Name, string Emoji);

/// <summary>Satın alınabilen bahçe süsü. Satın alma hayvanlarla aynı listede "decor:&lt;id&gt;" kimliğiyle tutulur.</summary>
public sealed record GardenDecor(string Id, string Name, int Price)
{
    public string Key => "decor:" + Id;
}

/// <summary>Hayvan kataloğu: tüm haritalar ve hayvanlar burada tanımlı.</summary>
public static class ZooCatalog
{
    public static readonly ZooMap[] Maps =
    {
        new("savanna", "Savana", "🌍"),
    };

    // Bedava hayvan yok; zebra ucuz giriş hayvanı. Toplam 480 jeton.
    public static readonly Animal[] Animals =
    {
        new("zebra",    "Zebra",    "🦓", "savanna", 20),
        new("giraffe",  "Zürafa",   "🦒", "savanna", 60),
        new("rhino",    "Gergedan", "🦏", "savanna", 80),
        new("elephant", "Fil",      "🐘", "savanna", 120),
        new("lion",     "Aslan",    "🦁", "savanna", 200),
    };

    public static readonly GardenDecor[] Decors =
    {
        new("fence", "Çit", 25), new("flowers", "Çiçekler", 30), new("sign", "Tabela", 40),
        new("campfire", "Kamp ateşi", 70), new("tent", "Çadır", 120), new("swing", "Salıncak", 160),
    };

    public static Animal? Find(string id) => Animals.FirstOrDefault(a => a.Id == id);
    public static ZooMap Map(string id) => Maps.First(m => m.Id == id);
    public static IEnumerable<Animal> InMap(string mapId) => Animals.Where(a => a.MapId == mapId);
}

/// <summary>Satın alınmış hayvan. Satın alma kalıcıdır (silme yok); senkronda hayvan kimliği tekildir.</summary>
public class OwnedAnimal
{
    public string AnimalId { get; set; } = "";
    public int Price { get; set; }                                // ödenen jeton (bakiye hesabı için)
    public DateTime BoughtAt { get; set; } = DateTime.UtcNow;     // UTC
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;    // UTC; senkron gönderimi için
}

public enum BuyResult { Ok, AlreadyOwned, NotEnough, Unknown }

/// <summary>
/// Jeton ekonomisi. Bakiye saklanmaz, her seferinde hesaplanır:
/// kazanılan (her seansın kendi jetonlarının toplamı) − harcanan (satın alımların fiyatları).
/// Böylece cihazlar arası sayaç çakışması olmaz. Kural kullanıcıya hiçbir yerde gösterilmez.
/// </summary>
public static class Zoo
{
    // Bir seans (başlat → bitir; duraklatmalar seansı bölmez) için jeton:
    //   taban = 10 · (dakika / 60)^1.25   → 1 sa ≈ 10; uzun kesintisiz seans orantısından fazla kazandırır
    //                                       (30 dk ≈ 4, 2 sa ≈ 24, 3 sa ≈ 39)
    //   şans  = taban · [0, %40)           → 1 sa: 10–14 arası
    // Şans seansın kimliğinden türetilir: aynı seans her hesapta, her cihazda aynı jetonu verir.
    const double HourCoins = 10, Exponent = 1.25, MaxLuck = 0.40;

    static double BaseCoins(double seconds)
        => seconds <= 0 ? 0 : HourCoins * Math.Pow(seconds / 3600.0, Exponent);

    /// <summary>Kimliğe bağlı, kalıcı [0,1) sayı. string.GetHashCode süreçten sürece değiştiği için FNV-1a.</summary>
    internal static double Luck(string id)
    {
        uint h = 2166136261;
        foreach (char c in id) { h ^= c; h *= 16777619; }
        return (h % 10000) / 10000.0;
    }

    public static int SessionCoins(Session s)
        => (int)(BaseCoins(s.Seconds) * (1 + MaxLuck * Luck(s.Id)));

    /// <summary>Kazanılan jeton: yalnız bitmiş seanslar. Süren seans sayılmaz; jetonu seans bitince tek seferde gelir.</summary>
    public static int Earned(AppData d) => d.Sessions.Sum(SessionCoins);

    public static int Spent(AppData d) => d.OwnedAnimals.Sum(o => o.Price);

    public static int Balance(AppData d) => Earned(d) - Spent(d);

    public static bool Owns(AppData d, string animalId)
        => d.OwnedAnimals.Any(o => o.AnimalId == animalId);

    /// <summary>Bakiyenin yettiği, henüz alınmamış hayvan ve süs sayısı (Market vurgusu ve yeniden çizim için).</summary>
    public static int AffordableCount(AppData d, string mapId, int balance)
        => ZooCatalog.InMap(mapId).Count(a => !Owns(d, a.Id) && balance >= a.Price)
         + ZooCatalog.Decors.Count(x => !Owns(d, x.Key) && balance >= x.Price);

    public static bool MapComplete(AppData d, string mapId) => ZooCatalog.InMap(mapId).All(a => Owns(d, a.Id));

    public static BuyResult BuyDecor(AppData d, GardenDecor x)
    {
        if (Owns(d, x.Key)) return BuyResult.AlreadyOwned;
        if (Balance(d) < x.Price) return BuyResult.NotEnough;
        d.OwnedAnimals.Add(new OwnedAnimal { AnimalId = x.Key, Price = x.Price });
        return BuyResult.Ok;
    }

    public static BuyResult Buy(AppData d, string animalId)
    {
        var a = ZooCatalog.Find(animalId);
        if (a == null) return BuyResult.Unknown;
        if (Owns(d, animalId)) return BuyResult.AlreadyOwned;
        if (Balance(d) < a.Price) return BuyResult.NotEnough;
        d.OwnedAnimals.Add(new OwnedAnimal { AnimalId = animalId, Price = a.Price });
        return BuyResult.Ok;
    }
}

public enum SpecialKind { Golden, Baby }

/// <summary>Nadir dost: bir hayvanın altın renkli ya da yavru hâli. Hangi seansın getirdiği de saklanır.</summary>
public sealed record Special(string AnimalId, SpecialKind Kind, string SessionId)
{
    /// <summary>Bahçedeki kimliği (ör. "golden:zebra", "baby:lion").</summary>
    public string Key => Keyed(AnimalId, Kind);
    public static string Keyed(string animalId, SpecialKind kind) => (kind == SpecialKind.Golden ? "golden:" : "baby:") + animalId;
    public static string Title(string animalName, SpecialKind kind) => (kind == SpecialKind.Golden ? "Altın " : "Yavru ") + animalName;
}

/// <summary>
/// Nadir dostlar (koleksiyon). Saklanmaz, jeton gibi seanslardan hesaplanır: böylece cihazlar arası senkron
/// gerekmez, her cihaz aynı sonucu bulur. Uzun bir seans (45 dk+) küçük bir şansla, seans bittiğinde sahip
/// olunan hayvanlardan birinin henüz kazanılmamış altın ya da yavru hâlini getirir. Oran kullanıcıya gösterilmez.
/// </summary>
public static class ZooSpecials
{
    const double MinSeconds = 45 * 60, FullSeconds = 2 * 3600;
    const double MinChance = 0.10, MaxChance = 0.25, GoldenShare = 0.45;

    static double Chance(double seconds) => seconds < MinSeconds ? 0
        : MinChance + (MaxChance - MinChance) * Math.Clamp((seconds - MinSeconds) / (FullSeconds - MinSeconds), 0, 1);

    /// <summary>Tüm kazanılmış nadir dostlar, kazanılma sırasıyla.</summary>
    public static List<Special> All(AppData d)
    {
        var got = new List<Special>();
        var have = new HashSet<string>();
        foreach (var s in d.Sessions.Where(x => x.Seconds >= MinSeconds).OrderBy(x => x.Start).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            if (Zoo.Luck(s.Id + "#nadir") >= Chance(s.Seconds)) continue;
            // Seans bittiğinde bahçede olan hayvanlar.
            var end = DateTime.SpecifyKind(s.Start, DateTimeKind.Local).ToUniversalTime().AddSeconds(s.Seconds);
            var owned = d.OwnedAnimals.Where(o => o.BoughtAt <= end && ZooCatalog.Find(o.AnimalId) != null).Select(o => o.AnimalId).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (owned.Count == 0) continue;

            var first = Zoo.Luck(s.Id + "#tur") < GoldenShare ? SpecialKind.Golden : SpecialKind.Baby;
            foreach (var kind in new[] { first, first == SpecialKind.Golden ? SpecialKind.Baby : SpecialKind.Golden })
            {
                var open = owned.Where(a => !have.Contains(Special.Keyed(a, kind))).ToList();
                if (open.Count == 0) continue;                       // bu türün hepsi kazanılmış: diğer türü dene
                var id = open[(int)(Zoo.Luck(s.Id + "#hayvan") * open.Count)];
                var sp = new Special(id, kind, s.Id);
                got.Add(sp); have.Add(sp.Key);
                break;
            }
        }
        return got;
    }

    /// <summary>Bu seansın getirdiği nadir dost (yoksa null).</summary>
    public static Special? From(AppData d, Session s) => All(d).FirstOrDefault(x => x.SessionId == s.Id);
}
