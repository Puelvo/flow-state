namespace FlowState;

/// <summary>Açık / koyu tema renkleri ve tema rengi seçenekleri. Masaüstü ve Android aynı tabloyu kullanır.</summary>
public static class ThemeTable
{
    public static readonly Dictionary<string, (string Light, string Dark)> Colors = new()
    {
        ["BgA"] = ("#FFF9F1", "#221E2C"), ["BgB"] = ("#FDEBDD", "#1A1722"), ["Bg"] = ("#FFF7EE", "#1E1B27"),
        ["Surface"] = ("#FFFFFF", "#353042"), ["Panel"] = ("#FFFFFF", "#353042"),
        ["Panel2"] = ("#FBF0E6", "#443D53"), ["Line"] = ("#F1E3D6", "#4D465C"), ["Track"] = ("#F4E7DB", "#494257"),
        ["Text"] = ("#3B2C25", "#F5EEE7"), ["Muted"] = ("#A08C80", "#ADA2B4"), ["Faint"] = ("#CBB9AC", "#766C82"),
        ["Danger"] = ("#EF5D6C", "#FF7683"), ["DangerSoft"] = ("#FDE5E7", "#4F2D38"),
        ["Good"] = ("#2FB67C", "#4FCE97"), ["GoodSoft"] = ("#DDF5EA", "#24443A"),
        ["Warn"] = ("#FFAA3B", "#FFB957"), ["WarnSoft"] = ("#FFF0D6", "#4B3B26"),
        ["Coin"] = ("#F5B731", "#F5B731"), ["CoinInk"] = ("#C98410", "#FFCB5E"),
        ["Glass"] = ("#B8FFFFFF", "#C8353042"),            // gökyüzünde duran yarı saydam haplar
        ["Chip"] = ("#EEFFFFFF", "#EE353042"),             // üst sekme düğmeleri
        ["WarmTop"] = ("#FFF6E4", "#40352E"),              // kutlama kartının sıcak üstü
        ["Shadow"] = ("#B07A55", "#000000"), ["Hover"] = ("#14502A10", "#18FFFFFF"), ["Press"] = ("#22502A10", "#28FFFFFF"),
    };

    /// <summary>Tema rengi şekerleri; ilki (mavi) varsayılan.</summary>
    public static readonly string[] Swatches =
        { "#5B8CFF", "#4CB8F5", "#22B8B0", "#3CCB8E", "#F5C33B", "#FFA94D", "#FF7A6B", "#FF7EB6", "#A78BFA", "#9C6B53" };

    /// <summary>Hayvanların varsayılan isimleri.</summary>
    public static readonly Dictionary<string, string> AnimalNames = new()
    { ["zebra"] = "Çizgi", ["giraffe"] = "Zuzu", ["rhino"] = "Boncuk", ["elephant"] = "Fındık", ["lion"] = "Paşa" };

    /// <summary>Bir hayvanın adı. Kimlik: "zebra" ya da nadir dostta "golden:zebra", "baby:lion".</summary>
    public static string NameOf(AppData data, string key)
    {
        if (data.AnimalNames.TryGetValue(key, out var n) && n.Length > 0) return n;
        if (key.StartsWith("baby:")) return "Minik " + NameOf(data, key[5..]);
        if (key.StartsWith("golden:")) return "Altın " + NameOf(data, key[7..]);
        return AnimalNames.GetValueOrDefault(key) ?? ZooCatalog.Find(key)?.Name ?? key;
    }
}
