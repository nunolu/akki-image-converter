param([string]$OutputDirectory = 'artifacts\third-party-sources')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskDestination = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
New-Item -ItemType Directory -Force $taskDestination | Out-Null
$taskSources = Get-Content (Join-Path $taskRoot 'third-party\sources.json') -Raw | ConvertFrom-Json
$taskSources | ForEach-Object -Parallel {
    $ErrorActionPreference = 'Stop'
    $taskEntry = $_
    $taskFolder = $using:taskDestination
    $taskFilename = "$($taskEntry.name)-$($taskEntry.revision).zip"
    $taskFile = Join-Path $taskFolder $taskFilename
    if (-not (Test-Path -LiteralPath $taskFile)) {
        $taskUrl = "https://codeload.github.com/$($taskEntry.owner)/$($taskEntry.repository)/zip/$($taskEntry.revision)"
        Invoke-WebRequest -Uri $taskUrl -OutFile ($taskFile + '.download') -MaximumRetryCount 2
        Move-Item -LiteralPath ($taskFile + '.download') -Destination $taskFile
    }
    # A successfully downloaded HTTP response must also be a readable source archive.
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskFile)
    try { if ($taskArchive.Entries.Count -eq 0) { throw "Empty source archive: $taskFilename" } }
    finally { $taskArchive.Dispose() }
    Write-Output "Source ready: $($taskEntry.name)"
} -ThrottleLimit 4
Copy-Item -LiteralPath (Join-Path $taskRoot 'third-party\sources.json') -Destination $taskDestination
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\dependencies.md') -Destination (Join-Path $taskDestination 'BUILDING-AND-LICENSES.md')
Copy-Item -LiteralPath (Join-Path $taskRoot 'third-party\licenses\Magick.NET-15.0.0-NOTICE.txt') -Destination $taskDestination
$taskHashes = Get-ChildItem -LiteralPath $taskDestination -Filter '*.zip' -File | Sort-Object Name | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
}
$taskHashes | Set-Content -LiteralPath (Join-Path $taskDestination 'SHA256SUMS.txt')
$taskBundle = Join-Path $taskRoot 'artifacts\akki-image-converter-third-party-sources.zip'
$taskTemporary = $taskBundle + ".$( [Guid]::NewGuid().ToString('N') ).tmp"
try {
    [IO.Compression.ZipFile]::CreateFromDirectory($taskDestination, $taskTemporary, [IO.Compression.CompressionLevel]::NoCompression, $false)
    [IO.File]::Move($taskTemporary, $taskBundle, $true)
} finally { if (Test-Path -LiteralPath $taskTemporary) { Remove-Item -LiteralPath $taskTemporary } }
Write-Output "Source bundle: $taskBundle"
