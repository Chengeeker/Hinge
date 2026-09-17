param(
    [string]$OutputPath = 'windows/Hinge.App/Assets/app_icon.ico',
    [string]$SourcePath = 'android/assets/icon9.png'
)

Add-Type -AssemblyName System.Drawing

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$outputTarget = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    Join-Path $repoRoot $OutputPath
}
$resolvedOutput = [System.IO.Path]::GetFullPath($outputTarget)
$outputDirectory = [System.IO.Path]::GetDirectoryName($resolvedOutput)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function New-RoundedRectanglePath {
    param(
        [System.Drawing.Rectangle]$Rectangle,
        [int]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-WindowsIconCanvas {
    param(
        [System.Drawing.Image]$SourceImage,
        [int]$PaddingPercent = 6
    )

    $sourceBitmap = [System.Drawing.Bitmap]$SourceImage
    $minX = $sourceBitmap.Width
    $minY = $sourceBitmap.Height
    $maxX = -1
    $maxY = -1
    for ($y = 0; $y -lt $sourceBitmap.Height; $y++) {
        for ($x = 0; $x -lt $sourceBitmap.Width; $x++) {
            if ($sourceBitmap.GetPixel($x, $y).A -gt 8) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }

    if ($maxX -lt $minX -or $maxY -lt $minY) {
        return $sourceBitmap.Clone()
    }

    $visibleWidth = $maxX - $minX + 1
    $visibleHeight = $maxY - $minY + 1
    $visibleSide = [Math]::Max($visibleWidth, $visibleHeight)
    $padding = [Math]::Max(2, [int][Math]::Ceiling($visibleSide * $PaddingPercent / 100.0))
    $cropSide = [Math]::Min(
        [Math]::Max($sourceBitmap.Width, $sourceBitmap.Height),
        $visibleSide + (2 * $padding))
    $centerX = ($minX + $maxX + 1) / 2.0
    $centerY = ($minY + $maxY + 1) / 2.0
    $left = [int][Math]::Round($centerX - ($cropSide / 2.0))
    $top = [int][Math]::Round($centerY - ($cropSide / 2.0))
    $left = [Math]::Max(0, [Math]::Min($left, $sourceBitmap.Width - $cropSide))
    $top = [Math]::Max(0, [Math]::Min($top, $sourceBitmap.Height - $cropSide))

    $canvas = New-Object System.Drawing.Bitmap(
        $cropSide,
        $cropSide,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $sourceRectangle = New-Object System.Drawing.Rectangle($left, $top, $cropSide, $cropSide)
        $destinationRectangle = New-Object System.Drawing.Rectangle(0, 0, $cropSide, $cropSide)
        $graphics.DrawImage(
            $sourceBitmap,
            $destinationRectangle,
            $sourceRectangle,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }
    return $canvas
}

function Convert-ImageToPngBytes {
    param(
        [System.Drawing.Image]$SourceImage,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($SourceImage, 0, 0, $Size, $Size)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    }
    finally {
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Convert-ImageToIcoDibBytes {
    param(
        [System.Drawing.Image]$SourceImage,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($SourceImage, 0, 0, $Size, $Size)

        $rectangle = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
        $data = $bitmap.LockBits(
            $rectangle,
            [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $stride = [Math]::Abs($data.Stride)
            $sourceBytes = New-Object byte[] ($stride * $Size)
            [Runtime.InteropServices.Marshal]::Copy(
                $data.Scan0,
                $sourceBytes,
                0,
                $sourceBytes.Length)
        }
        finally {
            $bitmap.UnlockBits($data)
        }

        # ICO DIB pixels are bottom-up BGRA rows.  The bitmap was created as
        # 32bpp ARGB, whose in-memory layout is already BGRA on Windows.
        $xorBytes = New-Object byte[] ($Size * $Size * 4)
        for ($row = 0; $row -lt $Size; $row++) {
            $sourceRow = if ($data.Stride -ge 0) { $Size - 1 - $row } else { $row }
            [Array]::Copy(
                $sourceBytes,
                $sourceRow * $stride,
                $xorBytes,
                $row * $Size * 4,
                $Size * 4)
        }

        $andRowBytes = [int]([Math]::Ceiling($Size / 32.0) * 4)
        $andBytes = New-Object byte[] ($andRowBytes * $Size)
        $stream = New-Object System.IO.MemoryStream
        $writer = New-Object System.IO.BinaryWriter($stream)
        try {
            $writer.Write([uint32]40)                # BITMAPINFOHEADER size
            $writer.Write([int32]$Size)              # width
            $writer.Write([int32]($Size * 2))        # XOR + AND height
            $writer.Write([uint16]1)                 # planes
            $writer.Write([uint16]32)                # bit count
            $writer.Write([uint32]0)                 # BI_RGB
            $writer.Write([uint32]$xorBytes.Length)  # image size
            $writer.Write([int32]0)                  # horizontal pixels/meter
            $writer.Write([int32]0)                  # vertical pixels/meter
            $writer.Write([uint32]0)                 # colors used
            $writer.Write([uint32]0)                 # important colors
            $writer.Write($xorBytes)
            $writer.Write($andBytes)
            $result = $stream.ToArray()
        }
        finally {
            $writer.Dispose()
            $stream.Dispose()
        }
        return ,$result
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sourceImage = $null
$renderSource = $null
$graphics = $null
$background = $null
$backgroundPath = $null
$ringPen = $null
$accent = $null
$accentPath = $null
if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
    $sourceTarget = if ([System.IO.Path]::IsPathRooted($SourcePath)) {
        $SourcePath
    } else {
        Join-Path $repoRoot $SourcePath
    }
    $sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $sourceTarget).Path)
} else {
    $sourceImage = New-Object System.Drawing.Bitmap(
        256,
        256,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($sourceImage)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 103, 80, 164))
    $backgroundPath = New-RoundedRectanglePath (New-Object System.Drawing.Rectangle(8, 8, 240, 240)) 52
    $graphics.FillPath($background, $backgroundPath)

    $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 26)
    $ringPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawEllipse($ringPen, 70, 70, 116, 116)

    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 142, 217, 176))
    $accentPath = New-RoundedRectanglePath (New-Object System.Drawing.Rectangle(64, 148, 128, 30)) 15
    $graphics.FillPath($accent, $accentPath)
}

# The supplied artwork intentionally has transparent breathing room for
# Android.  Windows shell surfaces allocate the whole square to the ICO, so
# crop only the Windows render source to make the visible mark comparable to
# neighbouring desktop icons without changing the Android artwork.
$renderSource = New-WindowsIconCanvas -SourceImage $sourceImage

$pngPath = [System.IO.Path]::ChangeExtension($resolvedOutput, '.png')
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$iconImages = @()
foreach ($iconSize in $iconSizes) {
    $iconImages += [pscustomobject]@{
        Size = $iconSize
        Bytes = Convert-ImageToIcoDibBytes -SourceImage $renderSource -Size $iconSize
    }
}

[System.IO.File]::WriteAllBytes(
    $pngPath,
    (Convert-ImageToPngBytes -SourceImage $renderSource -Size 256))

# Standard 32-bit DIB ICO entries preserve the source alpha channel while
# remaining readable by WinUI, the taskbar, the shell and the installer.
$icoStream = New-Object System.IO.FileStream(
    $resolvedOutput,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None
)
$icoWriter = New-Object System.IO.BinaryWriter($icoStream)
$icoWriter.Write([uint16]0)                    # reserved
$icoWriter.Write([uint16]1)                    # icon resource
$icoWriter.Write([uint16]$iconImages.Count)   # image count
$imageOffset = 6 + (16 * $iconImages.Count)
foreach ($iconImage in $iconImages) {
    $dimension = if ($iconImage.Size -eq 256) { 0 } else { $iconImage.Size }
    $icoWriter.Write([byte]$dimension)
    $icoWriter.Write([byte]$dimension)
    $icoWriter.Write([byte]0)                 # palette count
    $icoWriter.Write([byte]0)                 # reserved
    $icoWriter.Write([uint16]1)               # color planes
    $icoWriter.Write([uint16]32)              # bits per pixel
    $icoWriter.Write([uint32]$iconImage.Bytes.Length)
    $icoWriter.Write([uint32]$imageOffset)
    $imageOffset += $iconImage.Bytes.Length
}
foreach ($iconImage in $iconImages) {
    $icoWriter.Write($iconImage.Bytes)
}
$icoWriter.Flush()
$icoWriter.Dispose()
$icoStream.Dispose()

if ($null -ne $graphics) { $graphics.Dispose() }
if ($null -ne $renderSource) { $renderSource.Dispose() }
if ($null -ne $sourceImage) { $sourceImage.Dispose() }
if ($null -ne $accent) { $accent.Dispose() }
if ($null -ne $ringPen) { $ringPen.Dispose() }
if ($null -ne $background) { $background.Dispose() }
if ($null -ne $accentPath) { $accentPath.Dispose() }
if ($null -ne $backgroundPath) { $backgroundPath.Dispose() }

Write-Host "Icon generated: $resolvedOutput"
