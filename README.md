# Flow State

Ders çalışma süresi takip uygulaması: Windows (WPF, .NET 9) + Android (MAUI), Supabase ile senkron.

## Çalıştırma
- **Masaüstü:** `dist\FlowState.exe` dosyasına çift tıkla. (Bilgisayarda .NET 9 Desktop Runtime gerekir; .NET SDK kuruluysa zaten vardır.)
- **Android:** `dist\FlowState.apk` dosyasını telefona kur.

## Özellikler
- **Çalış:** Sayaç rakamı yok; çalıştıkça dolan bir çember var. Her saat çember 1 tur atar, tamamlanan saatler altta nokta olarak görünür. Çember dersin rengini alır (geçişli).
- Ders ekle/sil, "Genel" ders, başlat / duraklat / bitir, Boşluk tuşu kısayolu
- **İstatistik:** bugün / hafta / ay / toplam süre, seri (güncel / en uzun / çalışılan gün), döneme göre ders dağılımı, son 14 gün grafiği, 18 haftalık ısı haritası
- **Geçmiş:** oturum listesi, tek tek silme, CSV (Excel) dışa aktarma
- **Widget modu (masaüstü):** küçük, üstte kalan, sürüklenebilir pencere; halkaya tıkla başlat/duraklat
- **Android:** masaüstüyle aynı tasarım, çalışırken bildirim (duraklat/bitir), ekranı açık tut, elle kayıt, ana ekran widget'ı (4x1: halka, ders, durum, oynat/durdur)
- **Senkron:** Supabase hesabıyla telefon ve bilgisayar aynı veriyi paylaşır; aktif seans da senkronlanır. Aynı adlı dersler tek derse birleşir. Veriler önce cihazda tutulur, internet yokken de çalışır.
- Veriler `%APPDATA%\FlowState\data.json` içinde saklanır; 15 sn'de bir otomatik kayıt, çökme olsa bile oturum kaybolmaz
- Günlük hedef özelliği kaldırıldı (veri modelinde `DailyGoalHours` alanı uyumluluk için duruyor, kullanılmıyor)

## Geliştirme
- Masaüstü: `dotnet run`, paketlemek için `dotnet publish -c Release -o dist`.
- Android: proje yolundaki Türkçe karakter (`Masaüstü`) aapt2'yi bozar. Kaynağı ASCII bir klasöre kopyala (bin/obj/dist/.git hariç), `Mobile` klasöründe şunu çalıştır:
  `dotnet publish -f net9.0-android -c Release -p:AndroidSdkDirectory=<Android SDK yolu>`
  Çıktı: `Mobile\bin\Release\net9.0-android\publish\com.puelvo.flowstate-Signed.apk` → `dist\FlowState.apk`.

## Sosyal özellikler için Supabase kurulumu (bir kez)
Supabase panelinde **SQL Editor**'e `supabase/live_session.sql`, ardından `supabase/social.sql` içeriğini yapıştırıp çalıştır. Kurulmadıysa Sosyal sekmesi bunu söyler. Tablolara doğrudan erişim kapalıdır; her şey, çağıranın kim olduğunu ve arkadaş/üye olup olmadığını denetleyen fonksiyonlardan geçer. Çevrimiçi durumu, seans sürerken ~45 sn'de bir gönderilen sinyale dayanır; 5 dakika sinyal gelmezse kişi çevrimdışı görünür.
