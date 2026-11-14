# Assets\pixellab\src içindeki pixellab sayfalarından hayvan başına yürüme şeridi üretir.
# Çıktı: Assets\pixellab\<id>_walk.png (32x32 kareler yan yana, sağa bakar, kare 0 = duruş)
#        Assets\pixellab\<id>_sil.png  (kare 0'ın koyu silueti)
# Çalıştır: powershell -ExecutionPolicy Bypass -File tools\build-sprites.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$base = Join-Path $PSScriptRoot "..\Assets\pixellab"
$src = Join-Path $base "src"
$fmt = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
$C = 32

# Bir kare tanımı: @(kaynak bitmap, sütun, satır, yatay çevir, dikey kaydırma)
# Karenin içinde kalmış saydam delikleri (dışarıyla bağlantısı olmayan) gövdenin en yaygın rengiyle doldurur.
# pixellab bazı karelerde gövdeyi delik bırakıyor; yalnızca gövdesi kapalı hayvanlarda kullan
# (kuyruğu gövdeye değip halka yapan hayvanlarda halkanın içini de doldurur).
function FillHoles($bmp, [int]$ox) {
  $outside = New-Object 'bool[,]' $C, $C
  $q = New-Object System.Collections.Queue
  for ($k = 0; $k -lt $C; $k++) { foreach ($pt in @(@($k, 0), @($k, ($C - 1)), @(0, $k), @(($C - 1), $k))) { $q.Enqueue($pt) } }
  while ($q.Count -gt 0) {
    $pt = $q.Dequeue(); $x = $pt[0]; $y = $pt[1]
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $C -or $y -ge $C -or $outside[$x, $y]) { continue }
    if ($bmp.GetPixel($ox + $x, $y).A -ne 0) { continue }
    $outside[$x, $y] = $true
    $q.Enqueue(@(($x + 1), $y)); $q.Enqueue(@(($x - 1), $y)); $q.Enqueue(@($x, ($y + 1))); $q.Enqueue(@($x, ($y - 1)))
  }
  $count = @{}
  for ($y = 0; $y -lt $C; $y++) { for ($x = 0; $x -lt $C; $x++) { $p = $bmp.GetPixel($ox + $x, $y); if ($p.A -ne 0 -and [math]::Max($p.R, [math]::Max($p.G, $p.B)) -gt 90) { $count[$p.ToArgb()] = 1 + $count[$p.ToArgb()] } } }
  $fill = [System.Drawing.Color]::FromArgb(($count.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 1).Key)
  for ($y = 0; $y -lt $C; $y++) { for ($x = 0; $x -lt $C; $x++) {
    if ($bmp.GetPixel($ox + $x, $y).A -eq 0 -and -not $outside[$x, $y]) { $bmp.SetPixel($ox + $x, $y, $fill) }
  } }
}

function Strip([string]$id, [object[]]$frames, [bool]$fillHoles = $false, [string]$kind = "walk") {
  $out = New-Object System.Drawing.Bitmap ($C * $frames.Count), $C, $fmt
  for ($i = 0; $i -lt $frames.Count; $i++) {
    if ($null -eq $frames[$i]) { continue }   # bu hayvanda o poz yok: kare boş kalır
    $bmp = $frames[$i][0]; $sx = $frames[$i][1] * $C; $sy = $frames[$i][2] * $C; $flip = $frames[$i][3]; $dy = $frames[$i][4]
    for ($y = 0; $y -lt $C; $y++) {
      $ty = $y + $dy; if ($ty -lt 0 -or $ty -ge $C) { continue }
      for ($x = 0; $x -lt $C; $x++) {
        $p = $bmp.GetPixel($sx + $x, $sy + $y)
        if ($p.A -eq 0) { continue }
        $tx = if ($flip) { $C - 1 - $x } else { $x }
        $out.SetPixel($i * $C + $tx, $ty, $p)
      }
    }
  }
  if ($fillHoles) { for ($i = 0; $i -lt $frames.Count; $i++) { FillHoles $out ($i * $C) } }
  $out.Save((Join-Path $base "${id}_${kind}.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  if ($kind -ne "poses") { $out.Dispose(); "$id $kind : $($frames.Count) kare"; return }
  # Siluet: duruş pozundan (poz şeridinin ilk karesi)
  $sil = New-Object System.Drawing.Bitmap $C, $C, $fmt
  $dark = [System.Drawing.Color]::FromArgb(255, 40, 34, 50)
  for ($y = 0; $y -lt $C; $y++) { for ($x = 0; $x -lt $C; $x++) { if ($out.GetPixel($x, $y).A -gt 0) { $sil.SetPixel($x, $y, $dark) } } }
  $sil.Save((Join-Path $base "${id}_sil.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $sil.Dispose(); $out.Dispose()
  "$id $kind : $($frames.Count) kare"
}

$sheet = New-Object System.Drawing.Bitmap (Join-Path $src "sheet.png")
$lion  = New-Object System.Drawing.Bitmap (Join-Path $src "lion_walk_src.png")
$rhino = New-Object System.Drawing.Bitmap (Join-Path $src "rhino_walk_src.png")

# Gerçek yürüyüş döngüsü olanlar (9 kare). Aslan sayfası sola bakıyor → çevrilir.
Strip "lion"  @(0..8 | ForEach-Object { ,@($lion, $_, 0, $true, 0) })
Strip "rhino" @(0..8 | ForEach-Object { ,@($rhino, ($_ % 3), [int][math]::Floor($_ / 3), $false, 0) }) $true

# Henüz yürüyüş sayfası olmayanlar: ana sayfadan iki poz + 1 piksel zıplama (geçici).
# @(sütun, satır) çiftleri: duruş, adım
$temp = @{ zebra = @(0, 3, 1, 3); giraffe = @(0, 0, 1, 0); elephant = @(1, 6, 2, 6) }
foreach ($id in $temp.Keys) {
  $t = $temp[$id]
  $fr = New-Object System.Collections.ArrayList
  [void]$fr.Add(@($sheet, $t[0], $t[1], $false, 0))
  [void]$fr.Add(@($sheet, $t[2], $t[3], $false, 0))
  [void]$fr.Add(@($sheet, $t[0], $t[1], $false, 0))
  [void]$fr.Add(@($sheet, $t[2], $t[3], $false, -1))
  Strip $id $fr.ToArray()
}

# Pozlar (ana sayfadan): <id>_poses.png, kare sırası SABİT: duruş, oturma, uyuma, yeme, sevinç.
# Her poz @(sütun, satır, yatay çevir) — sola bakanlar çevrilir; $null = o hayvanda yok.
$poses = @{
  zebra    = @(@(0,3,$false), @(6,3,$true),  @(3,4,$true),  @(3,3,$true),  @(7,3,$false))
  giraffe  = @(@(0,0,$false), @(4,0,$true),  @(0,1,$false), @(1,1,$false), @(2,1,$false))
  rhino    = @(@(4,1,$false), @(1,2,$false), @(7,2,$true),  @(4,2,$false), @(5,2,$true))
  lion     = @(@(4,4,$true),  @(6,5,$false), @(7,5,$false), $null,         @(0,5,$false))
  elephant = @(@(0,6,$false), @(4,6,$false), @(7,6,$false), $null,         @(0,7,$false))
}
foreach ($id in $poses.Keys) {
  $fr = New-Object System.Collections.ArrayList
  foreach ($p in $poses[$id]) { if ($null -eq $p) { [void]$fr.Add($null) } else { [void]$fr.Add(@($sheet, $p[0], $p[1], $p[2], 0)) } }
  Strip $id $fr.ToArray() $false "poses"
}
$sheet.Dispose(); $lion.Dispose(); $rhino.Dispose()
