namespace FlowState;

/// <summary>Bir hayvan türü. Fiyat sabittir: kaç hayvan alınmış olursa olsun değişmez.
/// Görsel şimdilik emoji; ileride Sprite alanı eklenip tek yerden değişecek.</summary>
public sealed record Animal(string Id, string Name, string Emoji, string MapId, int Price);

/// <summary>Bir harita (tema). Sıralı açılır; aktif harita arka planı ve marketi belirler.</summary>
public sealed record ZooMap(string Id, string Name, string Emoji);

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
    static double Luck(string id)
    {
        uint h = 2166136261;
        foreach (char c in id) { h ^= c; h *= 16777619; }
        return (h % 10000) / 10000.0;
    }

    public static int SessionCoins(Session s)
        => (int)(BaseCoins(s.Seconds) * (1 + MaxLuck * Luck(s.Id)));

    /// <param name="liveSeconds">Sürmekte olan seansın geçen süresi. Canlıyken yalnız taban sayılır;
    /// şans payı seans bitince eklenir (sürpriz). Böylece canlı bakiye bitişteki değeri hiç aşmaz.</param>
    public static int Earned(AppData d, double liveSeconds = 0)
        => d.Sessions.Sum(SessionCoins) + (int)BaseCoins(Math.Max(0, liveSeconds));

    public static int Spent(AppData d) => d.OwnedAnimals.Sum(o => o.Price);

    public static int Balance(AppData d, double liveSeconds = 0) => Earned(d, liveSeconds) - Spent(d);

    public static bool Owns(AppData d, string animalId)
        => d.OwnedAnimals.Any(o => o.AnimalId == animalId);

    /// <summary>Bakiyenin yettiği, henüz alınmamış hayvan sayısı (Market vurgusu ve yeniden çizim için).</summary>
    public static int AffordableCount(AppData d, string mapId, int balance)
        => ZooCatalog.InMap(mapId).Count(a => !Owns(d, a.Id) && balance >= a.Price);

    public static bool MapComplete(AppData d, string mapId) => ZooCatalog.InMap(mapId).All(a => Owns(d, a.Id));

    public static BuyResult Buy(AppData d, string animalId, double liveSeconds = 0)
    {
        var a = ZooCatalog.Find(animalId);
        if (a == null) return BuyResult.Unknown;
        if (Owns(d, animalId)) return BuyResult.AlreadyOwned;
        if (Balance(d, liveSeconds) < a.Price) return BuyResult.NotEnough;
        d.OwnedAnimals.Add(new OwnedAnimal { AnimalId = animalId, Price = a.Price });
        return BuyResult.Ok;
    }
}
