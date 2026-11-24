# Güvenlik uyarıları ve imzalama

## Uyarı neden çıkıyor
Windows (SmartScreen) ve Android (Play Protect) içeriğe değil **kimliğe** bakar: dosyayı kimin yayımladığı belli değilse ve
dosya yeniyse uyarır. Uygulamanın zararsız olması uyarıyı kaldırmaz; imza ve itibar kaldırır.

## Depoda hazır olanlar
- Exe'nin kimlik bilgileri (yayıncı, ürün, açıklama, gerçek sürüm numarası) ve yönetici izni istemeyen `app.manifest`.
- Her sürümde `SHA256SUMS.txt` ve GitHub derleme kanıtı (attestation): dosyanın bu kaynaktan derlendiğini herkes doğrulayabilir.
- Gizli değer (secret) eklenince kendiliğinden devreye giren imzalama adımları (`.github/workflows/release.yml`).
- Android'de artan sürüm kodu (Play ve üstüne güncelleme için şart).

## 1. Android: kalıcı imza anahtarı (ücretsiz, ilk yapılacak iş)
Anahtar yokken her sürüm farklı bir "Android Debug" anahtarıyla imzalanır; yeni sürüm eskisinin üstüne kurulamayabilir.
Anahtarı **sen** üret (`tools/android-anahtar.ps1` üç adımı tek seferde yapar) ve dosyayı da şifreyi de kaybetme: kaybolursa bu uygulama güncellenemez.

```powershell
keytool -genkeypair -v -keystore "$env:USERPROFILE\flowstate.keystore" -alias flowstate -keyalg RSA -keysize 4096 -validity 10000
```
```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$env:USERPROFILE\flowstate.keystore")) | gh secret set ANDROID_KEYSTORE_BASE64 --repo Puelvo/flow-state
```
```powershell
gh secret set ANDROID_KEY_PASSWORD --repo Puelvo/flow-state
```
Keystore dosyasını depoya koyma; güvenli bir yerde yedekle. İlk kalıcı imzalı sürüme geçerken telefondaki eski sürümü
kaldırmak gerekir (önce Ayarlar'dan senkronlandığından emin ol).

## 2. Windows: seçenekler
| Yol | Ücret | Sonuç |
|---|---|---|
| **Microsoft Store (MSIX)** – önerilen | Bireysel kayıt ücretsiz | Paketi Store imzalar; Store'dan kurulumda uyarı yok |
| SignPath Foundation | Ücretsiz | Açık kaynak lisansı ve başvuru onayı ister; yayıncı "SignPath Foundation" görünür |
| OV sertifikası (Certum, SSL.com vb.) | Yıllık ücretli | Yayıncı adı görünür; uyarı itibar birikince azalır |
| Azure Artifact Signing | ~10 $/ay | Bireysel kayıt yalnızca ABD ve Kanada'ya açık |

Dosya tabanlı (.pfx) bir sertifikan olursa `WINDOWS_PFX_BASE64` ve `WINDOWS_PFX_PASSWORD` gizli değerlerini eklemek yeterli.
Yeni sertifikalar çoğunlukla donanım/bulut anahtarlıdır; o durumda imzalama adımı sağlayıcının aracına göre değiştirilir.

Kendinden imzalı sertifika **çözüm değildir**: kullanıcının bilgisayarında güvenilir sayılmaz.

## 3. Yanlış alarm olursa
Microsoft Defender dosyayı zararlı diye işaretlerse (uyarı değil, engelleme): https://www.microsoft.com/wdsi/filesubmission
adresinden "Software developer" olarak dosyayı gönder.
