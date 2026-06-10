# Sinh app icon cho ClearTool: mosaic treemap trên nền xanh bo góc.
# Output: src\ClearTool.App\Assets\app.ico (16..256, entry DIB/BMP chuẩn —
# entry PNG tự ghép làm CSC báo CS7065) + icon-256.png cho TitleBar.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\ClearTool.App\Assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(1.0, 2 * $r)
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $size / 256.0

    # Nền: rounded square gradient xanh (kiểu icon Win11)
    $bgRect = New-RoundRectPath (2 * $s) (2 * $s) (252 * $s) (252 * $s) (56 * $s)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF(0, [float]$size)),
        [System.Drawing.Color]::FromArgb(255, 0x2A, 0x8C, 0xEA),
        [System.Drawing.Color]::FromArgb(255, 0x0D, 0x47, 0xA1))
    $g.FillPath($bgBrush, $bgRect)

    # Mosaic treemap: 1 khối lớn xanh lá (an toàn) + vàng + trắng + đỏ
    $tiles = @(
        @{ X = 44;  Y = 44;  W = 98;  H = 168; C = [System.Drawing.Color]::FromArgb(255, 0x66, 0xBB, 0x6A) },
        @{ X = 152; Y = 44;  W = 60;  H = 80;  C = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xC1, 0x07) },
        @{ X = 152; Y = 134; W = 60;  H = 42;  C = [System.Drawing.Color]::FromArgb(255, 0xE8, 0xF1, 0xFB) },
        @{ X = 152; Y = 186; W = 60;  H = 26;  C = [System.Drawing.Color]::FromArgb(255, 0xEF, 0x53, 0x50) }
    )
    foreach ($t in $tiles) {
        $brush = New-Object System.Drawing.SolidBrush($t.C)
        $path = New-RoundRectPath ($t.X * $s) ($t.Y * $s) ($t.W * $s) ($t.H * $s) ([Math]::Max(1.0, 10 * $s))
        $g.FillPath($brush, $path)
        $brush.Dispose(); $path.Dispose()
    }

    $g.Dispose()
    return $bmp
}

# Entry ICO dạng DIB 32bpp (BITMAPINFOHEADER + BGRA bottom-up + AND mask rỗng)
function ConvertTo-DibEntry([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
    $bw.Write([uint32]($w * $h * 4)); $bw.Write([int32]0); $bw.Write([int32]0)
    $bw.Write([uint32]0); $bw.Write([uint32]0)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rowBytes = $w * 4
    $row = New-Object byte[] $rowBytes
    for ($y = $h - 1; $y -ge 0; $y--) {
        $src = [IntPtr]::Add($data.Scan0, $y * $data.Stride)
        [System.Runtime.InteropServices.Marshal]::Copy($src, $row, 0, $rowBytes)
        $bw.Write($row)
    }
    $bmp.UnlockBits($data)

    $maskRowBytes = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRowBytes * $h)))
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()
    return , $bytes
}

# PNG 256 cho TitleBar
$bmp256 = New-IconBitmap 256
$bmp256.Save((Join-Path $assets "icon-256.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp256.Dispose()

# .ico đa kích thước
$sizes = 16, 24, 32, 48, 64, 128, 256
$entries = @()
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $entries += , (ConvertTo-DibEntry $bmp)
    $bmp.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $bytes = $entries[$i]
    $dim = if ($sz -eq 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$bytes.Length); $bw.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $entries) { $bw.Write([byte[]]$bytes) }
[IO.File]::WriteAllBytes((Join-Path $assets "app.ico"), $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Output "OK: $assets\app.ico ($($sizes.Count) sizes DIB) + icon-256.png"
