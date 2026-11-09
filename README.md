# Flow State

Windows için ders çalışma süresi takip uygulaması (WPF, .NET 9).

## Çalıştırma
`dist\FlowState.exe` dosyasına çift tıkla. (Bilgisayarda .NET 9 Desktop Runtime gerekir; .NET SDK kuruluysa zaten vardır.)

## Özellikler
- **Çalış:** Sayaç rakamı yok; çalıştıkça dolan bir çember var. Her saat çember 1 tur atar, tamamlanan saatler altta nokta olarak görünür. Çember dersin rengini alır.
- Ders ekle/sil (üstteki ders çipleri), başlat / duraklat / bitir, Boşluk tuşu kısayolu
- **İstatistik:** bugün / hafta / ay / toplam süre, günlük hedef ve seri, döneme göre ders dağılımı, son 14 gün grafiği
- **Geçmiş:** oturum listesi, tek tek silme, CSV (Excel) dışa aktarma
- Veriler `%APPDATA%\FlowState\data.json` içinde saklanır; 15 sn'de bir otomatik kayıt, çökme olsa bile oturum kaybolmaz

## Geliştirme
`dotnet run` ile çalıştır, `dotnet publish -c Release -o dist` ile yeniden paketle.
