[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$frames = @()
foreach ($size in @(16, 20, 24, 32, 48, 64, 128, 256)) {
    $canvas = New-Object System.Drawing.Bitmap ($size * 4), ($size * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shell = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        ([System.Drawing.Rectangle]::new(0, 0, 256, 256)), `
        ([System.Drawing.Color]::FromArgb(48, 54, 64)), `
        ([System.Drawing.Color]::FromArgb(28, 32, 40)), 45
    $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(219, 225, 233)), 5
    $seam = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(112, 121, 135)), 3
    $brass = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(231, 190, 113))
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $scaled = [System.Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object System.IO.MemoryStream
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size * 4 / 256.0), ($size * 4 / 256.0))
        # An asymmetric thumb rest distinguishes the MD301 from a generic mouse.
        $body.AddBezier(130, 15, 91, 10, 68, 20, 67, 55)
        $body.AddBezier(67, 55, 66, 78, 70, 91, 57, 103)
        $body.AddBezier(57, 103, 30, 111, 24, 132, 25, 167)
        $body.AddBezier(25, 167, 24, 204, 43, 230, 82, 239)
        $body.AddBezier(82, 239, 127, 254, 173, 241, 194, 213)
        $body.AddBezier(194, 213, 216, 184, 209, 146, 209, 113)
        $body.AddBezier(209, 113, 207, 87, 214, 52, 200, 30)
        $body.AddBezier(200, 30, 187, 14, 162, 14, 130, 15)
        $body.CloseFigure()
        $graphics.FillPath($shell, $body)
        $graphics.DrawPath($edge, $body)
        $graphics.DrawLine($seam, 139, 18, 139, 103)
        $graphics.DrawBezier($seam, 67, 106, 111, 114, 167, 114, 208, 106)
        $graphics.FillEllipse($brass, 131, 40, 16, 36)
        $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaled.DrawImage($canvas, 0, 0, $size, $size)
        # DIB frames also work with .NET Framework's Icon.ToBitmap (PNG frames do not).
        if ($size -eq 256) {
            $bitmap.Save((Join-Path $OutputDirectory 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Bmp)
        $bmp = $stream.ToArray()
        $maskLength = [int]([Math]::Ceiling($size / 32.0) * 4 * $size)
        $frame = New-Object byte[] ($bmp.Length - 14 + $maskLength)
        [Array]::Copy($bmp, 14, $frame, 0, $bmp.Length - 14)
        [Array]::Copy([BitConverter]::GetBytes([int]($size * 2)), 0, $frame, 8, 4)
        $frames += ,$frame
    } finally {
        $stream.Dispose()
        $scaled.Dispose()
        $bitmap.Dispose()
        $brass.Dispose()
        $seam.Dispose()
        $edge.Dispose()
        $shell.Dispose()
        $body.Dispose()
        $graphics.Dispose()
        $canvas.Dispose()
    }
}

$file = [System.IO.File]::Create((Join-Path $OutputDirectory 'MD301Plus.ico'))
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    $sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
    for ($i = 0; $i -lt $frames.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally {
    $writer.Dispose()
}
