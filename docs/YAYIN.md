# Mağazalara çıkış hazırlığı

Kurallar sık değişir; başvurmadan önce bağlantılardaki güncel metne bak. Son kontrol: 24 Kasım 2026.

## Google Play
**Ücret:** Play Console kaydı bir kez 25 $. Uygulama ücretsizse başka zorunlu ücret yok.

| Gerek | Durum |
|---|---|
| Kalıcı imza anahtarı ve AAB paketi | İş akışı hazır; anahtarı üretmen gerekiyor (docs/GUVENLIK.md) |
| Android 16 (API 36) hedefi | Hazır |
| Artan sürüm kodu | Hazır (iş akışı numarası) |
| Gizlilik politikası (herkese açık adres) | **Yazılacak** |
| Uygulama içinden hesap silme + web'den silme bağlantısı | **Yapılacak** (hesap açılabildiği için zorunlu) |
| Veri güvenliği formu | Doldurulacak: e-posta, ders ve oturum kayıtları, kullanıcı adı, canlı çalışma durumu |
| İçerik derecelendirmesi, hedef yaş, reklam yok beyanı | Doldurulacak |
| Kullanıcı içeriği (kullanıcı adı, oda adı): bildirme / engelleme | **Yapılacak** |
| Mağaza görselleri: 512×512 ikon, 1024×500 tanıtım, en az 2 ekran görüntüsü | Hazırlanacak |
| Kapalı test: en az 12 kişi, aralıksız 14 gün (yeni bireysel hesaplar) | Testçi bulunacak |
| İnceleme için deneme hesabı | Açılacak |

Sıra: anahtar → hesap silme ve gizlilik politikası → Play Console kaydı ve kimlik doğrulama → kapalı test → üretim başvurusu.

Kaynaklar: [kapalı test kuralı](https://support.google.com/googleplay/android-developer/answer/14151465) ·
[hedef API](https://support.google.com/googleplay/android-developer/answer/11926878) ·
[hesap silme](https://support.google.com/googleplay/android-developer/answer/13327111)

## App Store
**Ücret:** Apple Developer Program yılda 99 $. Ücretsiz uygulamada başka zorunlu ücret yok; ücretli satışta komisyon %30
(Small Business Program'da %15).

Derleme için macOS ve Xcode gerekir: Mac yoksa herkese açık depolarda GitHub Actions'ın macOS makineleri kullanılabilir.
Kodda yapılacaklar: sahne görünümünün iOS çizicisi, widget'ın WidgetKit ile (Swift) yeniden yazılması, bildirim yerine
Live Activity ya da yerel bildirim, anahtar saklamanın Keychain'e taşınması. Deneme için bir iPhone gerekir.

Kaynaklar: [kayıt](https://developer.apple.com/support/enrollment/) ·
[Small Business Program](https://developer.apple.com/app-store/small-business-program/)

## Microsoft Store
**Ücret:** bireysel kayıt ücretsiz. Uygulama MSIX olarak paketlenir, paketi Store imzalar; SmartScreen uyarısı çıkmaz.
Gerekenler: geliştirici kaydı, uygulama adının ayrılması (paket kimliği buradan gelir), gizlilik politikası, görseller.

Kaynaklar: [ücretsiz kayıt](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/) ·
[imzalama seçenekleri](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options)
