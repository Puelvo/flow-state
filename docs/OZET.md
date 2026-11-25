# Flow State: yapay zekâ için proje özeti

Bu metin, projeyi hiç görmemiş bir yapay zekâ ile fikir üretmek için yazıldı. Cevaplar Türkçe olsun.
**Senden istenen:** aşağıdaki tasarım diline ve kurallara uyan özellik, içerik ve büyüme fikirleri üret; her fikir için neden işe
yarayacağını, kabaca emeğini ve var olan sistemle nasıl birleşeceğini yaz.

## 1. Uygulama nedir
Flow State bir **ders çalışma süresi uygulaması**. Çalıştıkça süre, seri ve jeton kazanılır; jetonla yaşayan bir piksel savana
bahçesi büyütülür. Ana fikir: odaklanmak sıkıcı bir sayaç değil, içinde yaşayan sevimli bir dünya olsun.

- **Platformlar:** Windows (C#, WPF, .NET 9) ve Android (C#, .NET MAUI, .NET 10). iOS yok.
- **Arka uç:** Supabase (hesap, senkron, sosyal). Veri önce cihazda tutulur, internet yokken de çalışır; iki cihaz aynı hesapta
  birleşir, süren oturum da cihazlar arasında paylaşılır.
- **Dil:** arayüz Türkçe. **Dağıtım:** şimdilik herkese açık bir GitHub deposunun Releases sayfası (son yayın v1.3.9).
- **Hedef kitle:** ders çalışan öğrenciler. Geliştirici tek kişi. Uygulama ücretsiz, reklam yok.

## 2. Tasarım dili ("Sevimli safari")
- Krem/şeftali zemin, beyaz yuvarlak kartlar, yumuşak gölge, basınca çöken "şeker" düğmeler, Nunito yazı tipi. Koyu tema da var.
- Sahne **piksel sanat**: hayvanlar 32×32 piksel, süsler ve jeep kodla üretilir. İki platform aynı çizim kodunu kullanır.
- Ton: tatlı, sıcak, davetkâr, oyunsu ama sakin. Hareket yumuşak ve yavaş. Kısa, samimi Türkçe cümleler.
- Kullanıcının seçtiği bir tema rengi düğmeleri ve vurguları boyar.
- Kullanıcı ortak "bar" (düğmelerin dizildiği şerit) sevmiyor; düğmeler ayrı haplar hâlinde durur. Sahneyi kapatan büyük
  arayüz öğelerini sevmiyor. Soluk, perde inmiş gibi renkleri sevmiyor; gece bile canlı renk istiyor.

## 3. Çalış ekranı (uygulamanın kalbi)
- Tam ekran canlı bahçe. Gökyüzünde **güneş saat**: ortada ders adı, süre ve durum; çevresinde 60 ışın. Kronometrede her ışın
  bir dakika, her saat bir tur; geri sayımda 60 ışın hedefi paylaşır. Gece güneş yerine kraterli ay olur.
- **Gün döngüsü:** şafak, gündüz, ikindi, gün batımı, gece; saate göre otomatik ya da sabit. Gecede yıldız, ateş böceği, baykuş, far.
- Üç katmanlı büyük piksel bulutlar, kuşlar, kelebekler, yapraklar, deliklerden çıkan mirketler, gölet, akasya, patika.
- **Gezgin** kullanıcının kendisidir. Çalışma başlayınca **kırmızı jeep'e** biner, patikada gezer, ekranın sağından çıkıp soldan
  girer; hayvanlar yol verir. Molada jeep durur. Oturum bitince iner, hayvanlar etrafına toplanıp kutlar; sonra hayvanlarla
  selamlaşır, selamlaşan bir süre peşinden yürür.
- **Hayvanlar:** çalışırken sakin (uyur, oturur, yer), molada oyunbaz, gece uykucu. Göletten su içer, gölgede dinlenir.
  Tıklayınca zıplar ve **ismi** çıkar; çift tıkla adı değişir; sürükle-bırak ile taşınır.
- Kronometre ve geri sayım (en fazla 2 saat, çevrilen kadranla) modları. Tamamlanan her saat bir yıldız.
- Ana ekran **widget'ı** (Android) canlı bahçenin küçük hâli; bildirimde süre ve düğmeler. Masaüstünde küçük widget penceresi.

## 4. Ekonomi ve ilerleme
- **Jeton** yalnızca oturum bitince gelir. Uzun kesintisiz oturum orantısından fazla kazandırır (1 saat yaklaşık 10–14, 2 saat
  yaklaşık 24–33). Kural bilerek **kullanıcıya gösterilmez**. Elle süre ekleme yoktur (hile olmasın diye).
- **Market – dostlar:** zebra 20, zürafa 60, gergedan 80, fil 120, aslan 200 jeton.
- **Market – süsler:** çit 25, çiçekler 30, tabela 40, kamp ateşi 70, çadır 120, salıncak 160. Süsler kendiliğinden gelmez,
  satın alınır. Marketin tamamı 925 jeton; sonrasında jetonun kullanım yeri yok (bilinen eksik).
- **Nadir dostlar:** 45 dakikadan uzun oturumlar küçük bir şansla sahip olunan hayvanın altın ya da yavru hâlini getirir (10'luk
  koleksiyon).
- **Seri ağacı:** art arda çalışılan günlerle filizden meyveli ağaca büyür; seri bozulursa yaprakları dökülür ama kurumaz.
- Tek harita var: Savana. Kod birden çok haritaya uygun.

## 5. Diğer bölümler
- **İstatistik:** gün/hafta/ay/toplam, seri, ders dağılımı, son 14 gün, 18 haftalık ısı haritası, haftalık toplam, günün saatleri.
- **Geçmiş:** oturum listesi, silme, CSV dışa aktarma.
- **Dostlar:** bahçedeki hayvanlar, nadir dostlar, süsler, market.
- **Sosyal:** kullanıcı adıyla arkadaşlık, arkadaşın o an çalışıp çalışmadığı, kodla katılınan odalar ve lider tablosu.
- **Ayarlar:** tema rengi, açık/koyu tema, bahçe vakti, dersler, senkron hesabı.

## 6. Durum
**Yayında (v1.3.9):** yukarıdakilerin tamamı. **Hazır ama yayımlanmadı:** masaüstünde şifre değiştirme, mobilde tema rengi
yerleşimi düzeltmesi. Mobil yeni tasarım telefonda kullanılarak sınanıyor.

**Bilinen eksikler:** hayvan isimleri cihazlar arasında senkronlanmıyor; sürüklenen hayvanın yeri kalıcı değil; zebra, zürafa ve
filin gerçek yürüyüş kareleri yok; "Şifremi unuttum" ve uygulamadan hesap silme yok; Windows dosyası imzasız olduğu için
SmartScreen uyarısı çıkıyor (Android kalıcı anahtarla imzalı).

## 7. Kullanıcının istekleri ve kararları
- İki platform aynı tasarım ve özelliklerde ilerlesin.
- İleride **Google Play**'e çıkmak istiyor (App Store maliyetini de araştırdı). Play için gerekenler: gizlilik politikası,
  uygulamadan ve web'den hesap silme, en az 12 testçiyle 14 gün kapalı test. Windows için Microsoft Store düşünülebilir.
- Çok daha sonra: uygulamanın nasıl kullanılacağını anlatan küçük bir **tanıtım turu**.
- **Günlük hedef** özelliği denenip bilerek kaldırıldı; yeniden önerme.
- Güneşin altında "ilk yıldızı topla" gibi teşvik yazıları istemiyor.

## 8. Şimdiye kadar konuşulan, henüz karar verilmemiş fikirler
1. Pomodoro döngüsü (25/5; molada hayvanlar oynar, mola bitince jeep kalkar)
2. Arkadaşların bahçede görünmesi (o an çalışan arkadaşın gezgini senin yolunda belirir)
3. Haftalık kartpostal (bahçenin görseli ve haftalık süreyle paylaşılabilir resim)
4. İkinci harita (orman, kutup…) ve yeni hayvanlar
5. Jetonla alınan seri koruma kalkanı
6. Ortam sesleri (savana gecesi, yağmur, ateş)
7. Oturum başında kısa niyet yazma

## 9. Fikir üretirken uyulacak sınırlar
- Tek geliştirici: küçük parçalar hâlinde yapılabilen fikirler öncelikli. Emeği (küçük/orta/büyük) belirt.
- Odağı bölmemeli: çalışma sırasında dikkat çeken bildirim, oyun ya da sürekli etkileşim isteyen şeyler uygun değil.
- Öde-kazan, reklam, suçluluk duygusu ya da ceza yok (ör. hayvan ölmez, ağaç kurumaz).
- Piksel sanat ve sevimli ton korunmalı; yeni hayvan çizimi en pahalı iştir.
- Veri az tutulur; yeni sunucu tablosu gerekiyorsa söyle.
- Uygulama engelleme gibi ağır Android izni isteyen fikirlerden kaçın.
