param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskRoot = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$taskSource = [Drawing.Image]::FromFile((Join-Path $taskRoot 'src/Akki.ImageConverter/Assets/ArtworkMaster.png'))
try {
    $taskImages = @(
        @{ Name='StoreLogo.png'; Width=50; Height=50; Icon=50 },
        @{ Name='Square44x44Logo.png'; Width=44; Height=44; Icon=44 },
        @{ Name='Square150x150Logo.png'; Width=150; Height=150; Icon=150 },
        @{ Name='Square310x310Logo.png'; Width=310; Height=310; Icon=310 },
        @{ Name='Wide310x150Logo.png'; Width=310; Height=150; Icon=140 }
    )
    foreach ($taskImage in $taskImages) {
        $taskBitmap = [Drawing.Bitmap]::new($taskImage.Width, $taskImage.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
        try {
            $taskGraphics.Clear([Drawing.Color]::Transparent)
            $taskGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $taskGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $taskGraphics.DrawImage($taskSource, [int](($taskImage.Width-$taskImage.Icon)/2), [int](($taskImage.Height-$taskImage.Icon)/2), [int]$taskImage.Icon, [int]$taskImage.Icon)
            $taskBitmap.Save((Join-Path $Destination $taskImage.Name), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $taskGraphics.Dispose(); $taskBitmap.Dispose() }
    }
} finally { $taskSource.Dispose() }
