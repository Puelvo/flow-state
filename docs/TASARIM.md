# Tasarım sistemi — "Sevimli safari"

Eski koyu "uzay" teması yerine açık, sıcak ve oyunsu bir tema. Hedef: tatlı, davetkâr, dokununca tepki veren bir arayüz.
Bütün kaynaklar `App.xaml` içinde. Tema rengi (Ayarlar'dan seçilen) `ApplyTheme()` ile `Accent*` kaynaklarına yazılır.

## Açık / koyu tema
- Renkler `AppTheme.cs` tablosunda (her anahtarın açık ve koyu değeri). `App.xaml` fırçaları oraya bağlıdır; tema canlı değişir.
- Sabit renk yazma: kart üstünde `Surface`/`Text`..., gökyüzünde duran haplarda `Glass`/`Chip` kullan. Güneş saatin içindeki yazı her temada kakaodur.
- Vaktin ışığı saydam örtü değildir: görseller ışık rengiyle çarpılır (`MainWindow.Light.cs`), böylece gece renkler solmaz.

## İlkeler
- **Krem zemin, beyaz kartlar.** Sayfa zemini krem/şeftali geçişi (`BgGrad`). İçerik beyaz kartlarda (`Style="{StaticResource Card}"`).
  Kartlar 24 px köşe yarıçaplı ve yumuşak, sıcak tonlu gölgeli (`SoftShadow`). Kenar çizgisi yok.
- **Şeker düğmeler.** Düğmelerin altında koyulaşmış bir "dudak" var, basınca yüz aşağı iner (`Btn`, `Primary`, `DangerBtn`, `Round`).
  Küçük araç düğmeleri için zeminsiz `Ghost`.
- **Yuvarlak ve kalın yazı.** Nunito (Assets/fonts). Başlıklar Black/ExtraBold, gövde SemiBold. Sayılar büyük ve kalın.
- **Renk az ama neşeli.** Vurgu = tema rengi. Pastel tonlar (`AccentSoft`, `GoodSoft`, `WarnSoft`, `DangerSoft`) rozet ve zeminlerde.
  Ders renkleri (Course.Color) chip noktalarında ve grafiklerde kullanılır.
- **Küçük canlılıklar.** Seçilen sekme zıplar, arka planda pastel noktalar süzülür. Yeni öğeler belirirken kısa bir
  ölçek/opaklık animasyonu (0.25–0.4 sn, BackEase/SineEase) uygundur. Abartma: hareket yumuşak ve yavaş olsun.
- **Boş durumlar sevimli.** Boş liste yerine kısa, sıcak bir cümle ve küçük bir ikon/illüstrasyon.
- **Emoji yok.** WPF renkli emoji çizemez (tek renk çıkar). İkonlar `App.xaml`'deki `Icon*` geometrileri + `Style="{StaticResource Icon}"`.

## Kaynaklar (App.xaml)
| Anahtar | Kullanım |
|---|---|
| `AppFont` | Nunito. Pencere ve diyaloglarda `FontFamily="{StaticResource AppFont}"` |
| `BgGrad`, `Bg` | Sayfa zemini |
| `Surface` (= `Panel`) | Beyaz kart zemini |
| `Panel2` | İkincil yüzey: ikincil düğme, iz, boş kutucuk |
| `Line` | İnce ayırıcı, kart içi çerçeve |
| `Track` | Halka/çubuk izi (boş kısım) |
| `Text` / `Muted` / `Faint` | Kakao metin / ikincil metin / çok sönük metin |
| `Accent` (Dynamic) | Tema rengi |
| `AccentDim` (Dynamic) | Grafikte sönük çubuk |
| `AccentSoft` = `Sel` (Dynamic) | Seçili öğe zemini, vurgulu rozet zemini |
| `AccentDeep` (Dynamic) | Koyu vurgu metni |
| `OnAccent` (Dynamic) | Vurgu zemininde metin (açık temalarda kakao, diğerlerinde beyaz) |
| `Danger`/`DangerSoft`, `Good`/`GoodSoft`, `Warn`/`WarnSoft`, `Coin` | Durum renkleri, jeton sarısı |
| `SoftShadow`, `LiftShadow` | Kart gölgesi / üstte duran öğe gölgesi |

Vurgu kaynaklarını C#'ta kullanırken **dinamik** bağla: `el.SetResourceReference(Border.BackgroundProperty, "Accent")`
(tema değişince güncellenir). `Res("Accent")` o anki değeri bir kez alır.

## Stiller
- Düğme: `Btn` (krem), `Primary` (tema rengi), `DangerBtn` (pembe/kırmızı), `Ghost` (zeminsiz), `Round` (yuvarlak, başlat/bitir)
- Sekme/segment: `Nav`, `Seg` (küçük), grup zemini `SegBar`
- Kart: `Card`
- Yazı: `PageTitle` (30 Black), `PageSub`, `H` (bölüm başlığı, 16 ExtraBold), `Body`
- İkon: `<Path Data="{StaticResource IconChart}" Style="{StaticResource Icon}"/>` — rengi içinde bulunduğu denetimin
  `Foreground`'udur. İkonlar: Timer, Chart, History, Paw, People, Sliders, Widget, Plus, Close, Expand, Pin, Trash,
  Download, Sparkle (dolgulu kullan), Heart, Flame.
- `TextBox`, `PasswordBox`, `ListBoxItem` (ders chip'i), `ScrollBar`, `ToolTip` varsayılan stilleri yeni temada.

## Sayfa düzeni
- Üstte logo + ortada ayrı ayrı haplarda ikonlu sekmeler (ortak bar yok) + sağda Widget düğmesi. Çalış'ta gökyüzü bunların arkasına uzanır.
- Sayfa başlığı: `PageTitle` + altında bir satırlık sıcak açıklama (`PageSub`).
- İçerik en fazla ~880 px genişlikte, ortalı; kartlar arası 14–18 px boşluk.

## Denetim
- Derleme (Linux'ta da çalışır): `dotnet build -p:EnableWindowsTargeting=true`
- Kaynak adları: `python tools/check-resources.py` — eksik `StaticResource` uygulamayı açılışta çökertir, derleme yakalamaz.
