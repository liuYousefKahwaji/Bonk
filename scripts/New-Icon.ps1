# Recreate the checked-in app icon from the same simple microphone geometry as assets/bonk.svg.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bonkSizes = @(16, 32, 48, 64, 128, 256)
$bonkImages = @()
foreach ($bonkSize in $bonkSizes) {
    $bonkBitmap = [System.Drawing.Bitmap]::new($bonkSize, $bonkSize)
    $bonkGraphics = [System.Drawing.Graphics]::FromImage($bonkBitmap)
    $bonkGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $bonkGraphics.ScaleTransform($bonkSize / 64.0, $bonkSize / 64.0)
    $bonkAmber = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFBD69'))
    $bonkInk = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#151C24'))
    $bonkPen = [System.Drawing.Pen]::new($bonkInk, 4)
    $bonkPen.StartCap = $bonkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $bonkPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $bonkPath.AddArc(0, 0, 32, 32, 180, 90)
    $bonkPath.AddArc(32, 0, 32, 32, 270, 90)
    $bonkPath.AddArc(32, 32, 32, 32, 0, 90)
    $bonkPath.AddArc(0, 32, 32, 32, 90, 90)
    $bonkPath.CloseFigure()
    $bonkGraphics.FillPath($bonkAmber, $bonkPath)
    $bonkGraphics.FillEllipse($bonkInk, 25, 12, 14, 14)
    $bonkGraphics.FillRectangle($bonkInk, 25, 19, 14, 14)
    $bonkGraphics.FillEllipse($bonkInk, 25, 26, 14, 14)
    $bonkGraphics.DrawLine($bonkPen, 19, 30, 19, 34)
    $bonkGraphics.DrawArc($bonkPen, 19, 21, 26, 26, 0, 180)
    $bonkGraphics.DrawLine($bonkPen, 45, 30, 45, 34)
    $bonkGraphics.DrawLine($bonkPen, 32, 47, 32, 54)
    $bonkGraphics.DrawLine($bonkPen, 25, 54, 39, 54)
    $bonkStream = [System.IO.MemoryStream]::new()
    $bonkBitmap.Save($bonkStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bonkImages += ,$bonkStream.ToArray()
    $bonkStream.Dispose()
    $bonkPen.Dispose()
    $bonkInk.Dispose()
    $bonkAmber.Dispose()
    $bonkPath.Dispose()
    $bonkGraphics.Dispose()
    $bonkBitmap.Dispose()
}
$bonkIconPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets\bonk.ico'
$bonkWriter = [System.IO.BinaryWriter]::new([System.IO.File]::Create($bonkIconPath))
try {
    $bonkWriter.Write([uint16]0)
    $bonkWriter.Write([uint16]1)
    $bonkWriter.Write([uint16]$bonkSizes.Count)
    $bonkOffset = 6 + 16 * $bonkSizes.Count
    for ($bonkIndex = 0; $bonkIndex -lt $bonkSizes.Count; $bonkIndex++) {
        $bonkDimension = if ($bonkSizes[$bonkIndex] -eq 256) { 0 } else { $bonkSizes[$bonkIndex] }
        $bonkWriter.Write([byte]$bonkDimension)
        $bonkWriter.Write([byte]$bonkDimension)
        $bonkWriter.Write([uint16]0)
        $bonkWriter.Write([uint16]1)
        $bonkWriter.Write([uint16]32)
        $bonkWriter.Write([uint32]$bonkImages[$bonkIndex].Length)
        $bonkWriter.Write([uint32]$bonkOffset)
        $bonkOffset += $bonkImages[$bonkIndex].Length
    }
    foreach ($bonkImage in $bonkImages) { $bonkWriter.Write([byte[]]$bonkImage) }
}
finally { $bonkWriter.Dispose() }
Write-Output "Icon written to $bonkIconPath"
