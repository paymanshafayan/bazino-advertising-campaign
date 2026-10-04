# render-slides.ps1 — ساخت اسلایدهای کاروسل و استوری بازینو
# نسخهٔ ۳ (۲۰۲۶-۱۰-۰۳): بازنویسی بر اساس سند الگوی کاروسل (Doc/carousel-content-pattern-fa.md)
#   * تصویر قهرمان (تولیدشده با مدل تصویرساز) پایهٔ هر اسلاید است، نه شکل‌های تخت.
#   * کاور: لوگوی برند بالاوسط، تیتر دورنگ (زرد + سفید) و تصویر قهرمان در نیمهٔ پایین.
#   * اسلاید خبری: تصویر مدرک در کادر گوشه‌گرد با تب شماره، تیتر مزیت‌محور دورنگ، حداکثر ۳ خط متن.
#   * محتوای خبری هیچ ایموجی روی تصویر ندارد (بند ۷٫۳ راهنما)؛ ایموجی فقط در اورلی ویدئو و کپشن.
#   * فونت‌های مصوب Assets/fonts؛ فایل باید UTF-8 با BOM ذخیره شود.
#
# اجرا:
#   powershell -NoProfile -ExecutionPolicy Bypass -File render-slides.ps1 -DataFile content-<name>.json
#
# ورودی: JSON با ساختار { "output": "...", "slides": [ ... ] }
# انواع اسلاید: cover | promise | news | closing | story_poll | story_news
param(
    [Parameter(Mandatory = $true)][string]$DataFile
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$data = [System.IO.File]::ReadAllText($DataFile, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$outDir = $data.output
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$baseDir = Split-Path -Parent (Resolve-Path $DataFile).Path

$workDir = Join-Path $env:USERPROFILE 'Downloads\BazinoMarketing'
$fontDir = Join-Path $workDir 'fonts'
$emojiDir = Join-Path $fontDir 'emoji'
New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
New-Item -ItemType Directory -Force -Path $emojiDir | Out-Null

function Ensure-File([string]$Url, [string]$Target) {
    if (Test-Path $Target) { return }
    try { Invoke-WebRequest -Uri $Url -OutFile $Target -UseBasicParsing -TimeoutSec 45 }
    catch { Write-Host ("WARN download failed: {0} -> {1}" -f $Url, $_.Exception.Message) }
}

$gf = 'https://raw.githubusercontent.com/google/fonts/main/ofl'
$fontUrls = @{
    'BebasNeue-Regular.ttf'   = "$gf/bebasneue/BebasNeue-Regular.ttf"
    'Anton-Regular.ttf'       = "$gf/anton/Anton-Regular.ttf"
    'Poppins-Bold.ttf'        = "$gf/poppins/Poppins-Bold.ttf"
    'ArchivoBlack-Regular.ttf'= "$gf/archivoblack/ArchivoBlack-Regular.ttf"
    'RussoOne-Regular.ttf'    = "$gf/russoone/RussoOne-Regular.ttf"
    'Lato-Bold.ttf'           = "$gf/lato/Lato-Bold.ttf"
}
foreach ($k in $fontUrls.Keys) { Ensure-File $fontUrls[$k] (Join-Path $fontDir $k) }

$emojiMap = @{
    '🎮' = '1f3ae'; '🔥' = '1f525'; '📰' = '1f4f0'; '⚡' = '26a1'; '🎯' = '1f3af';
    '👾' = '1f47e'; '✨' = '2728';  '💬' = '1f4ac'; '🔖' = '1f516'; '📩' = '1f4e9';
    '😎' = '1f60e'; '🤔' = '1f914'; '⬇' = '2b07';  '❄' = '2744';  '🕹' = '1f579';
    '🏆' = '1f3c6'; '🚀' = '1f680'; '⏰' = '23f0';  '🗺' = '1f5fa'; '🎁' = '1f381';
    '👇' = '1f447'; '😂' = '1f602'; '💡' = '1f4a1'; '📅' = '1f4c5'; '🔔' = '1f514';
    '🎬' = '1f3ac'; '⭐' = '2b50';  '📱' = '1f4f1'; '✅' = '2705';  '❌' = '274c';
    '🎉' = '1f389'; '👀' = '1f440'
}
$tw = 'https://raw.githubusercontent.com/jdecked/twemoji/main/assets/72x72'
foreach ($k in $emojiMap.Keys) { Ensure-File "$tw/$($emojiMap[$k]).png" (Join-Path $emojiDir "$($emojiMap[$k]).png") }

$script:emojiPattern = '(?:' + (($emojiMap.Keys | ForEach-Object { [regex]::Escape([string]$_) }) -join '|') + ')'
$script:sfTypo = New-Object System.Drawing.StringFormat ([System.Drawing.StringFormat]::GenericTypographic)
$script:sfTypo.FormatFlags = $script:sfTypo.FormatFlags -bor [System.Drawing.StringFormatFlags]::MeasureTrailingSpaces

$script:keepAlive = @()
function Get-Family([string]$File) {
    $pfc = New-Object System.Drawing.Text.PrivateFontCollection
    $pfc.AddFontFile($File)
    $script:keepAlive += $pfc
    return $pfc.Families[0]
}
$famTitle = Get-Family (Join-Path $fontDir 'BebasNeue-Regular.ttf')
$famHeavy = Get-Family (Join-Path $fontDir 'Anton-Regular.ttf')
$famText  = Get-Family (Join-Path $fontDir 'Poppins-Bold.ttf')
$famBody  = Get-Family (Join-Path $fontDir 'Lato-Bold.ttf')
$famCaps  = Get-Family (Join-Path $fontDir 'ArchivoBlack-Regular.ttf')

function Col { param([int]$r, [int]$g, [int]$b, [int]$a = 255) return [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }
$cNavyTop = Col 6 10 24
$cNavyBot = Col 16 26 56
$cYellow  = Col 255 212 0
$cWhite   = Col 245 248 255
$cCyan    = Col 0 208 255
$cMuted   = Col 168 182 215
$cPanel   = Col 255 255 255 18
$cDark    = Col 8 12 30

function New-Font { param($Fam, [int]$Size, [string]$Style = 'Regular') return New-Object System.Drawing.Font($Fam, $Size, [System.Drawing.FontStyle]::$Style, [System.Drawing.GraphicsUnit]::Pixel) }
function Brush($Color) { return New-Object System.Drawing.SolidBrush($Color) }

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc(($x + $w - $d), $y, $d, $d, 270, 90)
    $path.AddArc(($x + $w - $d), ($y + $h - $d), $d, $d, 0, 90)
    $path.AddArc($x, ($y + $h - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Round-Rect($g, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r, $brush) {
    $path = New-RoundedPath $x $y $w $h $r
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function New-Canvas([int]$W, [int]$H) {
    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cNavyTop, $cNavyBot, 62.0)
    $g.FillRectangle($grad, $rect)
    $grad.Dispose()
    return @($bmp, $g)
}

function Draw-Scrim($g, [float]$X, [float]$Y, [float]$W, [float]$H, [int]$A1, [int]$A2, [float]$Angle = 90.0) {
    $rect = New-Object System.Drawing.RectangleF($X, $Y, $W, $H)
    $c1 = Col 6 10 24 $A1
    $c2 = Col 6 10 24 $A2
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, $Angle)
    $g.FillRectangle($br, $rect)
    $br.Dispose()
}

function Draw-ImageCover($g, [string]$ImgPath, [float]$X, [float]$Y, [float]$W, [float]$H) {
    if ([string]::IsNullOrWhiteSpace($ImgPath) -or -not (Test-Path $ImgPath)) { Write-Host ("WARN image missing: {0}" -f $ImgPath); return }
    $img = [System.Drawing.Image]::FromFile($ImgPath)
    try {
        $scale = [Math]::Max($W / $img.Width, $H / $img.Height)
        $dw = $img.Width * $scale; $dh = $img.Height * $scale
        $dx = $X + (($W - $dw) / 2); $dy = $Y + (($H - $dh) / 2)
        $state = $g.Save()
        $g.SetClip((New-Object System.Drawing.RectangleF($X, $Y, $W, $H)), [System.Drawing.Drawing2D.CombineMode]::Intersect)
        $g.DrawImage($img, [float]$dx, [float]$dy, [float]$dw, [float]$dh)
        $g.Restore($state)
    } finally { $img.Dispose() }
}

function Draw-ImageFrame($g, [string]$ImgPath, [float]$Fx, [float]$Fy, [float]$Fw, [float]$Fh, [float]$R, [float]$border = 0) {
    if ([string]::IsNullOrWhiteSpace($ImgPath) -or -not (Test-Path $ImgPath)) { Write-Host ("WARN frame image missing: {0}" -f $ImgPath); return }
    $shape = New-RoundedPath $Fx $Fy $Fw $Fh $R
    $state = $g.Save()
    $g.SetClip([System.Drawing.Drawing2D.GraphicsPath]$shape, [System.Drawing.Drawing2D.CombineMode]::Replace)
    Draw-ImageCover $g $ImgPath $Fx $Fy $Fw $Fh
    $g.Restore($state)
    if ($border -gt 0) {
        $pen = New-Object System.Drawing.Pen((Brush (Col 255 212 0 220)), $border)
        $g.DrawPath($pen, $shape); $pen.Dispose()
    }
    $shape.Dispose()
}

function Draw-Emoji($g, [string]$Emoji, [float]$X, [float]$Y, [int]$Size) {
    if (-not $emojiMap.ContainsKey($Emoji)) { return }
    $file = Join-Path $emojiDir "$($emojiMap[$Emoji]).png"
    if (-not (Test-Path $file)) { return }
    $img = [System.Drawing.Image]::FromFile($file)
    $g.DrawImage($img, [float]$X, [float]$Y, [float]$Size, [float]$Size)
    $img.Dispose()
}

function Measure-Typo { param($g, [string]$Text, $Font) return [float]$g.MeasureString($Text, $Font, (New-Object System.Drawing.PointF(0, 0)), $script:sfTypo).Width }

function Convert-ToRichTokens {
    param([string]$Text)
    $clean = $Text -replace ([string][char]0xFE0F), ''
    $tokens = New-Object System.Collections.ArrayList
    $rx = [regex]$script:emojiPattern
    $pos = 0
    foreach ($m in $rx.Matches($clean)) {
        if ($m.Index -gt $pos) { [void]$tokens.Add(@{ kind = 'text'; value = $clean.Substring($pos, $m.Index - $pos) }) }
        [void]$tokens.Add(@{ kind = 'emoji'; value = $m.Value })
        $pos = $m.Index + $m.Length
    }
    if ($pos -lt $clean.Length) { [void]$tokens.Add(@{ kind = 'text'; value = $clean.Substring($pos) }) }
    return @($tokens.ToArray())
}

function Get-RichLines {
    param($g, [string]$Text, $Font, [float]$MaxW)
    $emojiSize = [float]($Font.Size * 0.98)
    $spaceW = (Measure-Typo $g 'a a' $Font) - (Measure-Typo $g 'aa' $Font)
    if ($spaceW -le 0) { $spaceW = [float]$Font.Size * 0.28 }
    $lines = New-Object System.Collections.ArrayList
    $line = New-Object System.Collections.ArrayList
    $lineW = 0.0
    foreach ($tok in (Convert-ToRichTokens $Text)) {
        if ($tok.kind -eq 'emoji') { $items = @(@{ kind = 'emoji'; value = $tok.value; w = $emojiSize }) }
        else {
            $items = @()
            $words = $tok.value -split ' '
            for ($k = 0; $k -lt $words.Count; $k++) {
                if ($words[$k] -ne '') { $items += @{ kind = 'text'; value = $words[$k]; w = (Measure-Typo $g $words[$k] $Font) } }
                if ($k -lt ($words.Count - 1)) { $items += @{ kind = 'space'; value = ' '; w = $spaceW } }
            }
        }
        foreach ($it in $items) {
            if ((($lineW + $it.w) -gt ($MaxW + 0.5)) -and ($line.Count -gt 0)) {
                while (($line.Count -gt 0) -and ($line[$line.Count - 1].kind -eq 'space')) { $lineW -= $line[$line.Count - 1].w; $line.RemoveAt($line.Count - 1) }
                [void]$lines.Add(@{ items = @($line.ToArray()); width = $lineW })
                $line = New-Object System.Collections.ArrayList
                $lineW = 0.0
                if ($it.kind -eq 'space') { continue }
            }
            [void]$line.Add($it)
            $lineW += $it.w
        }
    }
    while (($line.Count -gt 0) -and ($line[$line.Count - 1].kind -eq 'space')) { $lineW -= $line[$line.Count - 1].w; $line.RemoveAt($line.Count - 1) }
    if (($line.Count -gt 0) -or ($lines.Count -eq 0)) { [void]$lines.Add(@{ items = @($line.ToArray()); width = $lineW }) }
    return @($lines.ToArray())
}

function Draw-Rich {
    param($g, [string]$Text, $Font, $BrushObj, [float]$X, [float]$Y, [float]$MaxW, [string]$Align = 'Near')
    $lines = @(Get-RichLines $g $Text $Font $MaxW)
    $emojiSize = [float]($Font.Size * 0.98)
    $textH = [float]$g.MeasureString('Ağy', $Font, (New-Object System.Drawing.PointF(0, 0)), $script:sfTypo).Height
    $lineHS = [float]([Math]::Max([float]$Font.Size * 1.3, $textH * 1.02))
    $cy = $Y
    foreach ($ln in $lines) {
        $cx = $X
        if ($Align -eq 'Center') { $cx = $X + (($MaxW - $ln.width) / 2) }
        elseif ($Align -eq 'Far') { $cx = $X + $MaxW - $ln.width }
        $ty = $cy + (($lineHS - $textH) / 2)
        foreach ($it in $ln.items) {
            if ($it.kind -eq 'emoji') { Draw-Emoji $g $it.value $cx ($cy + (($lineHS - $emojiSize) / 2)) ([int]$emojiSize) }
            elseif ($it.kind -eq 'text') { $g.DrawString($it.value, $Font, $BrushObj, $cx, $ty, $script:sfTypo) }
            $cx += $it.w
        }
        $cy += $lineHS
    }
    return ($cy - $Y)
}

function Draw-Text {
    param($g, [string]$Text, $Font, $BrushObj, [float]$X, [float]$Y, [float]$W, [string]$Align = 'Near')
    if ([string]::IsNullOrEmpty($Text)) { return 0 }
    if ([regex]::IsMatch($Text, $script:emojiPattern)) { return (Draw-Rich $g $Text $Font $BrushObj $X $Y $W $Align) }
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::$Align
    $sf.Trimming = [System.Drawing.StringTrimming]::Word
    $rect = New-Object System.Drawing.RectangleF($X, $Y, $W, 4000)
    $g.DrawString($Text, $Font, $BrushObj, $rect, $sf)
    $size = $g.MeasureString($Text, $Font, [int]$W, $sf)
    return $size.Height
}

function Measure-Block {
    param($g, [string]$Text, $Font, [float]$MaxW)
    if ([string]::IsNullOrEmpty($Text)) { return 0.0 }
    $lines = @(Get-RichLines $g $Text $Font $MaxW)
    $textH = [float]$g.MeasureString('Agy', $Font, (New-Object System.Drawing.PointF(0, 0)), $script:sfTypo).Height
    $lineHS = [float]([Math]::Max([float]$Font.Size * 1.3, $textH * 1.02))
    return [float]($lines.Count * $lineHS)
}

function Draw-Line {
    param($g, [string]$Text, $Font, $BrushObj, [float]$X, [float]$Y)
    if ([string]::IsNullOrEmpty($Text)) { return }
    $g.DrawString($Text, $Font, $BrushObj, $X, $Y, $script:sfTypo)
}

function Draw-Wordmark {
    param($g, [int]$W, [int]$H, [string]$Note = '')
    $font = New-Font $famTitle 40
    $brush = Brush $cWhite
    $text = 'BAZINO'
    $spacing = 9
    $widths = @(); $total = 0
    foreach ($ch in $text.ToCharArray()) { $sz = $g.MeasureString($ch, $font); $widths += $sz.Width; $total += $sz.Width + $spacing }
    $total -= $spacing
    $x = ($W - $total) / 2
    $y = $H - 96
    $i = 0
    foreach ($ch in $text.ToCharArray()) { $g.DrawString($ch, $font, $brush, $x, $y); $x += $widths[$i] + $spacing; $i++ }
    $g.FillRectangle((Brush $cCyan), (($W - 120) / 2), ($H - 48), 120, 4)
    if ($Note -ne '') { Draw-Text $g $Note (New-Font $famBody 22) (Brush $cMuted) 90 ($H - 42) ($W - 180) 'Center' | Out-Null }
}

function Draw-WordmarkTop {
    param($g, [int]$W)
    $font = New-Font $famTitle 44
    $brush = Brush $cWhite
    $text = 'BAZINO'
    $spacing = 10
    $widths = @(); $total = 0
    foreach ($ch in $text.ToCharArray()) { $sz = $g.MeasureString($ch, $font); $widths += $sz.Width; $total += $sz.Width + $spacing }
    $total -= $spacing
    $x = ($W - $total) / 2
    $y = 78
    $i = 0
    foreach ($ch in $text.ToCharArray()) { $g.DrawString($ch, $font, $brush, $x, $y); $x += $widths[$i] + $spacing; $i++ }
    $g.FillRectangle((Brush $cYellow), (($W - 140) / 2), 142, 140, 5)
}

function Draw-Counter($g, [int]$Index, [int]$Total, [int]$H) {
    Draw-Text $g "$Index / $Total" (New-Font $famBody 24) (Brush $cMuted) 90 ($H - 100) 200 'Near' | Out-Null
}

function Draw-Tab($g, [int]$W, [float]$Y, [string]$Text) {
    $font = New-Font $famText 26
    $w = (Measure-Typo $g $Text $font) + 84
    $x = ($W - $w) / 2
    Round-Rect $g $x $Y $w 58 29 (Brush $cYellow)
    Draw-Text $g $Text $font (Brush $cDark) ($x + 42) ($Y + 13) ($w - 84) 'Near' | Out-Null
}

function Save-Slide {
    param($bmp, [string]$Path, [int]$Quality = 92)
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
    $params = New-Object System.Drawing.Imaging.EncoderParameters(1)
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter([System.Drawing.Imaging.Encoder]::Quality, [int64]$Quality)
    $bmp.Save($Path, $codec, $params)
}

function Resolve-Asset([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return '' }
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $baseDir $Path)
}

$total = @($data.slides | Where-Object { $_.kind -notlike 'story*' }).Count
$slideNo = 0
foreach ($slide in $data.slides) {
    if ($slide.kind -notlike 'story*') { $slideNo++ }
    if ($slide.kind -like 'story*') { $W = 1080; $H = 1920 } else { $W = 1080; $H = 1351 }
    if ($slide.width)  { $W = [int]$slide.width }
    if ($slide.height) { $H = [int]$slide.height }
    $canvas = New-Canvas $W $H
    $bmp = $canvas[0]; $g = $canvas[1]
    $x = 70
    $w = $W - 140
    $bgPath = Resolve-Asset ([string]$slide.bg)
    $y = 0

    switch ($slide.kind) {
        'cover' {
            $bandH = [int]($H * 0.42)
            $bandY = $H - $bandH
            Draw-ImageCover $g $bgPath 0 $bandY $W $bandH
            Draw-Scrim $g 0 $bandY $W 240 245 0
            $g.FillRectangle((Brush $cYellow), 0, ($bandY - 5), $W, 5)
            Draw-WordmarkTop $g $W

            $badgeFont = New-Font $famText 27
            $badgeText = [string]$slide.badge
            $bw = [float]($g.MeasureString($badgeText, $badgeFont, (New-Object System.Drawing.PointF(0, 0)), $script:sfTypo).Width) + 84
            Round-Rect $g $x 226 $bw 66 33 (Brush (Col 255 212 0 40))
            Draw-Line $g $badgeText $badgeFont (Brush $cYellow) ($x + 42) 244

            $titleText = [string]$slide.title
            $title2Text = [string]$slide.title2
            $leadText = [string]$slide.lead
            $zoneTop = 328
            $zoneBottom = $bandY - 40
            $size = 92
            while ($size -ge 54) {
                $f1 = New-Font $famHeavy $size
                $block = Measure-Block $g $titleText $f1 $w
                if ($title2Text -ne '') { $block += 8 + (Measure-Block $g $title2Text $f1 $w) }
                if ($leadText -ne '') { $block += 26 + (Measure-Block $g $leadText (New-Font $famText 36) $w) }
                if ($slide.cta) { $block += 30 + 92 }
                if (($zoneTop + $block) -le $zoneBottom) { break }
                $size -= 6
            }
            $y = $zoneTop
            $y += Draw-Text $g $titleText (New-Font $famHeavy $size) (Brush $cYellow) $x $y $w 'Near'
            if ($title2Text -ne '') { $y += 8; $y += Draw-Text $g $title2Text (New-Font $famHeavy $size) (Brush $cWhite) $x $y $w 'Near' }
            if ($leadText -ne '') { $y += 26; Draw-Text $g $leadText (New-Font $famText 36) (Brush (Col 226 234 255)) $x $y $w 'Near' | Out-Null }

            if ($slide.cta) {
                $cf = New-Font $famHeavy 38
                $ctaW = [float]($g.MeasureString([string]$slide.cta, $cf, (New-Object System.Drawing.PointF(0, 0)), $script:sfTypo).Width) + 96
                $ctaY = [Math]::Min(($y + 30), ($bandY - 116))
                Round-Rect $g $x $ctaY $ctaW 92 46 (Brush $cYellow)
                Draw-Line $g ([string]$slide.cta) $cf (Brush $cDark) ($x + 48) ($ctaY + 18)
            }
            if ($slide.footer) { Draw-Text $g $slide.footer (New-Font $famBody 22) (Brush $cMuted) $x ($H - 62) $w 'Near' | Out-Null }
        }
        'promise' {
            Draw-ImageCover $g $bgPath 0 0 $W $H
            Draw-Scrim $g 0 0 $W $H 200 235
            $y = 170
            $y += Draw-Text $g $slide.title (New-Font $famHeavy 92) (Brush $cYellow) $x $y $w 'Near'
            $y += 40
            foreach ($line in $slide.lines) {
                $bh = Draw-Text $g $line (New-Font $famText 42) (Brush $cWhite) $x $y $w 'Near'
                $y += $bh + 40
            }
            if ($slide.note) {
                Round-Rect $g $x ($H - 284) $w 92 24 (Brush (Col 255 255 255 34))
                Draw-Text $g $slide.note (New-Font $famBody 28) (Brush (Col 214 224 250)) ($x + 34) ($H - 258) ($w - 68) 'Near' | Out-Null
            }
        }
        'news' {
            $frameY = 76
            $frameH = 500
            Draw-ImageFrame $g (Resolve-Asset ([string]$slide.image)) $x $frameY $w $frameH 36 3
            Draw-Tab $g $W ($frameY - 29) ("HABER " + [string]$slide.index)

            $kickY = $frameY + $frameH + 62
            Draw-Text $g $slide.kicker (New-Font $famText 28) (Brush $cCyan) $x $kickY $w 'Near' | Out-Null
            $y = $kickY + 50
            $limit = $H - 214
            $size = 72
            while ($size -ge 52) {
                $f1 = New-Font $famHeavy $size
                $block = Measure-Block $g ([string]$slide.title) $f1 $w
                if ($slide.title2) { $block += 6 + (Measure-Block $g ([string]$slide.title2) $f1 $w) }
                $block += 26
                $bf = New-Font $famText 34
                foreach ($b in $slide.lines) { $block += [Math]::Max((Measure-Block $g $b $bf ($w - 60)), 46.0) + 16 }
                if (($y + $block) -le $limit) { break }
                $size -= 4
            }
            $y += Draw-Text $g $slide.title (New-Font $famHeavy $size) (Brush $cYellow) $x $y $w 'Near'
            if ($slide.title2) { $y += 6; $y += Draw-Text $g $slide.title2 (New-Font $famHeavy $size) (Brush $cWhite) $x $y $w 'Near' }
            $y += 26
            foreach ($b in $slide.lines) {
                $g.FillEllipse((Brush $cYellow), ($x + 4), ($y + 15), 14, 14)
                $bh = Draw-Text $g $b (New-Font $famText 34) (Brush $cWhite) ($x + 42) $y ($w - 60) 'Near'
                $y += [Math]::Max($bh, 46.0) + 16
            }
            if ($slide.credit) { Draw-Text $g $slide.credit (New-Font $famBody 22) (Brush $cMuted) $x ($H - 150) $w 'Near' | Out-Null }
        }
        'closing' {
            Draw-ImageCover $g $bgPath 0 0 $W $H
            Draw-Scrim $g 0 0 $W $H 150 215
            $y = 300
            $y += Draw-Text $g $slide.title (New-Font $famHeavy 108) (Brush $cYellow) $x $y $w 'Near'
            $y += 30
            $y += Draw-Text $g $slide.question (New-Font $famText 46) (Brush $cWhite) $x $y $w 'Near'
            if ($slide.cta) {
                $cf = New-Font $famText 38
                Round-Rect $g $x ($H - 320) $w 124 40 (Brush (Col 255 212 0 46))
                Draw-Text $g $slide.cta $cf (Brush $cYellow) ($x + 30) ($H - 288) ($w - 60) 'Center' | Out-Null
            }
            if ($slide.footer) { Draw-Text $g $slide.footer (New-Font $famBody 26) (Brush $cMuted) $x ($H - 178) $w 'Center' | Out-Null }
        }
        'story_poll' {
            Draw-ImageCover $g $bgPath 0 0 $W $H
            Draw-Scrim $g 0 0 $W $H 170 225
            $y = 500
            Draw-Text $g $slide.badge (New-Font $famText 30) (Brush $cCyan) $x $y $w 'Center' | Out-Null
            $y += 96
            $y += Draw-Text $g $slide.question (New-Font $famHeavy 92) (Brush $cWhite) $x $y $w 'Center'
            $y += 70
            foreach ($opt in $slide.options) {
                Round-Rect $g $x $y $w 150 40 (Brush (Col 255 212 0 34))
                Draw-Text $g $opt (New-Font $famHeavy 58) (Brush $cYellow) ($x + 30) ($y + 38) ($w - 60) 'Center' | Out-Null
                $y += 190
            }
            if ($slide.note) { Draw-Text $g $slide.note (New-Font $famBody 34) (Brush $cMuted) $x ($y + 12) $w 'Center' | Out-Null }
        }
        'story_news' {
            Draw-ImageCover $g $bgPath 0 0 $W $H
            Draw-Scrim $g 0 0 $W $H 175 230
            $y = 430
            Draw-Text $g $slide.badge (New-Font $famText 30) (Brush $cCyan) $x $y $w 'Center' | Out-Null
            $y += 96
            $y += Draw-Text $g $slide.title (New-Font $famHeavy 96) (Brush $cYellow) $x $y $w 'Center'
            $y += 52
            foreach ($line in $slide.lines) {
                $bh = Draw-Text $g $line (New-Font $famText 42) (Brush $cWhite) $x $y $w 'Center'
                $y += $bh + 30
            }
            if ($slide.cta) { Draw-Text $g $slide.cta (New-Font $famBody 34) (Brush $cMuted) $x ($y + 56) $w 'Center' | Out-Null }
        }
    }

    if ($slide.kind -ne 'cover') { Draw-Wordmark $g $W $H '' }
    $file = Join-Path $outDir ("{0}.jpg" -f $slide.name)
    Save-Slide $bmp $file
    $g.Dispose(); $bmp.Dispose()
    Write-Host ("OK {0} -> {1}" -f $slide.name, $file)
}

Write-Host ("DONE {0} slides -> {1}" -f $total, $outDir)
