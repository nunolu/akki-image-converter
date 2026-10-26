# Export the generated master to UI PNG and a multi-resolution Windows ICO.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskAssets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Akki.ImageConverter\Assets'
$taskSource = [Drawing.Image]::FromFile((Join-Path $taskAssets 'ArtworkMaster.png'))
try {
    $taskSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $taskImages = [Collections.Generic.List[byte[]]]::new()
    foreach ($taskSize in $taskSizes) {
        $taskBitmap = [Drawing.Bitmap]::new($taskSize, $taskSize, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        $taskGraphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $taskGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $taskGraphics.DrawImage($taskSource, 0, 0, $taskSize, $taskSize)
        $taskMemory = [IO.MemoryStream]::new()
        $taskBitmap.Save($taskMemory, [Drawing.Imaging.ImageFormat]::Png)
        $taskImages.Add($taskMemory.ToArray())
        if ($taskSize -eq 256) { $taskBitmap.Save((Join-Path $taskAssets 'AppLogo.png'), [Drawing.Imaging.ImageFormat]::Png) }
        $taskMemory.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose()
    }
    $taskFile = [IO.File]::Create((Join-Path $taskAssets 'App.ico'))
    $taskWriter = [IO.BinaryWriter]::new($taskFile)
    try {
        $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
        $taskOffset = 6 + 16 * $taskSizes.Count
        for ($taskIndex = 0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
            $taskDimension = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
            $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
            $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
            $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
            $taskWriter.Write([uint32]$taskImages[$taskIndex].Length); $taskWriter.Write([uint32]$taskOffset)
            $taskOffset += $taskImages[$taskIndex].Length
        }
        foreach ($taskBytes in $taskImages) { $taskWriter.Write($taskBytes) }
    } finally { $taskWriter.Dispose(); $taskFile.Dispose() }
} finally { $taskSource.Dispose() }
