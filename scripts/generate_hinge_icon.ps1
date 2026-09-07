param(
    [string]$OutputPath = 'windows/Hinge.App/Assets/app_icon.ico',
    [string]$SourcePath = ''
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

$size = 256
$bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

$sourceImage = $null
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
    $graphics.DrawImage($sourceImage, (New-Object System.Drawing.Rectangle(0, 0, $size, $size)))
} else {
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

$pngPath = [System.IO.Path]::ChangeExtension($resolvedOutput, '.png')
$bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

# System.Drawing can emit PNG bytes when saving ImageFormat.Icon.  That file is
# not a valid ICO container for rc.exe, so wrap the PNG in an ICO directory.
$pngBytes = [System.IO.File]::ReadAllBytes($pngPath)
$icoStream = New-Object System.IO.FileStream(
    $resolvedOutput,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None
)
$icoWriter = New-Object System.IO.BinaryWriter($icoStream)
$icoWriter.Write([uint16]0)                    # reserved
$icoWriter.Write([uint16]1)                    # icon resource
$icoWriter.Write([uint16]1)                    # image count
$icoWriter.Write([byte]0)                     # width = 256
$icoWriter.Write([byte]0)                     # height = 256
$icoWriter.Write([byte]0)                     # palette count
$icoWriter.Write([byte]0)                     # reserved
$icoWriter.Write([uint16]1)                   # color planes
$icoWriter.Write([uint16]32)                  # bits per pixel
$icoWriter.Write([uint32]$pngBytes.Length)   # image byte count
$icoWriter.Write([uint32]22)                  # image offset
$icoWriter.Write($pngBytes)
$icoWriter.Flush()
$icoWriter.Dispose()
$icoStream.Dispose()

if ($null -ne $sourceImage) { $sourceImage.Dispose() }
if ($null -ne $accent) { $accent.Dispose() }
if ($null -ne $ringPen) { $ringPen.Dispose() }
if ($null -ne $background) { $background.Dispose() }
if ($null -ne $accentPath) { $accentPath.Dispose() }
if ($null -ne $backgroundPath) { $backgroundPath.Dispose() }
$graphics.Dispose()
$bitmap.Dispose()

Write-Host "Icon generated: $resolvedOutput"
