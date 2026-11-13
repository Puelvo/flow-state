namespace FlowState;

/// <summary>Bir hayvan türü. Görsel şimdilik emoji; ileride Sprite alanı eklenip tek yerden değişecek.</summary>
public sealed record Animal(string Id, string Name, string Emoji, string MapId, bool Starter = false);

/// <summary>Bir harita (tema). Sıralı açılır; aktif harita arka planı ve marketi belirler.</summary>
public sealed record ZooMap(string Id, string Name, string Emoji, int[] PriceLadder);

/// <summary>Hayvan kataloğu: tüm haritalar ve hayvanlar burada tanımlı.</summary>
public static class ZooCatalog
{
    public static readonly ZooMap[] Maps =
    {
        // Fiyat hayvana değil alım sırasına bağlı: haritadaki 1. alım 30, 2. alım 30, 3. alım 60…
        new("savanna", "Savana", "🌍", new[] { 30, 30, 60, 120, 250 }),
    };

    // GEÇİCİ liste — kesin hayvanlar kullanıcıyla seçilecek.
    public static readonly Animal[] Animals =
    {
        new("meerkat",  "Mirket",  "🦦", "savanna", Starter: true),
        new("zebra",    "Zebra",   "🦓", "savanna"),
        new("rhino",    "Gergedan", "🦏", "savanna"),
        new("giraffe",  "Zürafa",  "🦒", "savanna"),
        new("elephant", "Fil",     "🐘", "savanna"),
        new("lion",     "Aslan",   "🦁", "savanna"),
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
/// kazanılan (tüm seansların toplam süresinden) − harcanan (satın alımların fiyatları).
/// Böylece cihazlar arası sayaç çakışması olmaz.
/// </summary>
public static class Zoo
{
    public const int SecondsPerCoin = 360; // 6 dk = 1 jeton. Kullanıcıya hiçbir yerde gösterilmez.

    /// <param name="liveSeconds">Sürmekte olan seansın geçen süresi; jeton çalışırken canlı artsın diye.</param>
    public static int Earned(AppData d, double liveSeconds = 0)
        => (int)((d.Sessions.Sum(s => (long)s.Seconds) + (long)Math.Max(0, liveSeconds)) / SecondsPerCoin);

    public static int Spent(AppData d) => d.OwnedAnimals.Sum(o => o.Price);

    public static int Balance(AppData d, double liveSeconds = 0) => Earned(d, liveSeconds) - Spent(d);

    public static bool Owns(AppData d, string animalId)
        => ZooCatalog.Find(animalId)?.Starter == true || d.OwnedAnimals.Any(o => o.AnimalId == animalId);

    /// <summary>Haritadaki sıradaki alımın fiyatı; harita tamamlandıysa null.</summary>
    public static int? NextPrice(AppData d, string mapId)
    {
        var ladder = ZooCatalog.Map(mapId).PriceLadder;
        int bought = d.OwnedAnimals.Count(o => ZooCatalog.Find(o.AnimalId) is { Starter: false } a && a.MapId == mapId);
        return bought < ladder.Length ? ladder[bought] : null;
    }

    public static bool MapComplete(AppData d, string mapId) => ZooCatalog.InMap(mapId).All(a => Owns(d, a.Id));

    public static BuyResult Buy(AppData d, string animalId, double liveSeconds = 0)
    {
        var a = ZooCatalog.Find(animalId);
        if (a == null) return BuyResult.Unknown;
        if (Owns(d, animalId)) return BuyResult.AlreadyOwned;
        if (NextPrice(d, a.MapId) is not int price) return BuyResult.Unknown;
        if (Balance(d, liveSeconds) < price) return BuyResult.NotEnough;
        d.OwnedAnimals.Add(new OwnedAnimal { AnimalId = animalId, Price = price });
        return BuyResult.Ok;
    }
}
