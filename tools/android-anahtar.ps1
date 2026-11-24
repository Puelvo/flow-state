# Android için kalıcı imza anahtarını üretir ve GitHub gizli değerlerine (secrets) koyar. Bir kez çalıştırılır.
# Şifreyi yalnızca sen yazarsın; ekrana, dosyaya ya da komut satırına yazılmaz. Ayrıntı: docs/GUVENLIK.md
# Çalıştır: powershell -ExecutionPolicy Bypass -File tools\android-anahtar.ps1
param(
    [string]$Keystore = (Join-Path $env:USERPROFILE 'flowstate.keystore'),
    [string]$Repo = 'Puelvo/flow-state'
)
$ErrorActionPreference = 'Stop'

function ConvertTo-Plain([securestring]$s) {
    $p = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($p) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($p) }
}

$keytool = (Get-Command keytool -ErrorAction SilentlyContinue).Source
if (-not $keytool) { $keytool = Get-ChildItem "$env:ProgramFiles\Microsoft\jdk-*\bin\keytool.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1 -ExpandProperty FullName }
if (-not $keytool) { Write-Host 'keytool bulunamadi (JDK gerekli).' -ForegroundColor Red; exit 1 }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { Write-Host 'gh (GitHub CLI) bulunamadi.' -ForegroundColor Red; exit 1 }

$new = -not (Test-Path -LiteralPath $Keystore)
if ($new) { Write-Host "Yeni anahtar uretilecek: $Keystore" -ForegroundColor Cyan }
else { Write-Host "Var olan anahtar kullanilacak (ustune yazilmaz): $Keystore" -ForegroundColor Cyan }
Write-Host 'Sifre: en az 12 karakter; Turkce harf kullanma. Bunu sakla: kaybolursa uygulama guncellenemez.'

while ($true) {
    $a = ConvertTo-Plain (Read-Host 'Sifre' -AsSecureString)
    if ($a.Length -eq 0) { Write-Host 'Vazgecildi; hicbir sey degismedi.'; exit 1 }
    if ($a.Length -lt 12) { Write-Host "Cok kisa: $($a.Length) karakter yazdin, en az 12 olmali. Tekrar dene." -ForegroundColor Yellow; continue }
    if ($a -match '[^\x21-\x7E]') { Write-Host 'Bosluk ya da Turkce harf (c, g, i, o, s, u noktali/cengelli) var. Tekrar dene.' -ForegroundColor Yellow; continue }
    if ($new) {
        $b = ConvertTo-Plain (Read-Host 'Sifre (tekrar)' -AsSecureString)
        if ($a -cne $b) { Write-Host 'Iki yazdigin ayni degil. Bastan yaz.' -ForegroundColor Yellow; continue }
    }
    break
}

$env:FS_KEY_PASS = $a
try {
    if ($new) {
        # keytool bilgi satirlarini hata akisina yazar; basari yalnizca cikis kodundan anlasilir.
        $ErrorActionPreference = 'Continue'
        & $keytool -genkeypair -keystore $Keystore -storetype PKCS12 -alias flowstate -keyalg RSA -keysize 4096 -validity 10000 -dname 'CN=Puelvo, O=Flow State, C=TR' -storepass:env FS_KEY_PASS -keypass:env FS_KEY_PASS
        if ($LASTEXITCODE -ne 0) { Write-Host 'Anahtar uretilemedi.' -ForegroundColor Red; exit 1 }
    }
    $ErrorActionPreference = 'Continue'
    & $keytool -list -keystore $Keystore -alias flowstate -storepass:env FS_KEY_PASS | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host 'Sifre bu anahtari acmiyor.' -ForegroundColor Red; exit 1 }

    [Convert]::ToBase64String([IO.File]::ReadAllBytes($Keystore)) | gh secret set ANDROID_KEYSTORE_BASE64 --repo $Repo
    if ($LASTEXITCODE -ne 0) { Write-Host 'GitHub gizli degeri yazilamadi.' -ForegroundColor Red; exit 1 }
    $env:FS_KEY_PASS | gh secret set ANDROID_KEY_PASSWORD --repo $Repo
    if ($LASTEXITCODE -ne 0) { Write-Host 'GitHub gizli degeri yazilamadi.' -ForegroundColor Red; exit 1 }
}
finally { Remove-Item Env:FS_KEY_PASS -ErrorAction SilentlyContinue; $a = $b = $null }

Write-Host ''
Write-Host 'Tamam. Anahtar ve sifre GitHub gizli degerlerine kondu.' -ForegroundColor Green
Write-Host "Simdi $Keystore dosyasini ve sifreyi guvenli bir yerde yedekle (ornegin sifre yoneticisi ve harici disk)."
