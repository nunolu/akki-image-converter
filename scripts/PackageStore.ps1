param([string]$Version = '1.0.0', [string]$OutputDirectory = 'artifacts/store')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[1-9]\d*\.\d+\.\d+$' -or @($Version.Split('.') | Where-Object { [int]$_ -gt 65535 }).Count) {
    throw 'Store version must be major.minor.patch, with major at least 1 and components at most 65535.'
}
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot "$OutputDirectory/$Version"))
$taskBuildOutput = Join-Path $taskOutput 'build'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
Push-Location $taskRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.buildtools'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    & (Join-Path $PSScriptRoot 'ExportStoreArtwork.ps1') -Destination (Join-Path $taskRoot 'packaging/store/Assets')
    [xml]$taskManifest = Get-Content 'packaging/store/AppxManifest.xml' -Raw
    $taskManifest.Package.Identity.Version = "$Version.0"
    $taskManifestPath = Join-Path $taskOutput 'Package.appxmanifest'
    $taskManifest.Save($taskManifestPath)
    # Use WinUI single-project packaging so the app and SDK PRI resources are merged.
    # SideloadOnly controls the build container, not Store eligibility. Partner Center accepts .msix.
    # StoreUpload currently fails with the installed .NET MSIX tasks; do not ship a failed build.
    dotnet build src/Akki.ImageConverter/Akki.ImageConverter.csproj -c Release -p:Platform=x64 -p:DistributionChannel=Store -p:Version=$Version -p:GenerateAppxPackageOnBuild=true -p:UapAppxPackageBuildMode=SideloadOnly "-p:StoreManifestPath=$taskManifestPath" "-p:AppxPackageDir=$taskBuildOutput\" *> (Join-Path $taskOutput 'build.log')
    if ($LASTEXITCODE) {
        Get-Content (Join-Path $taskOutput 'build.log') -Tail 35
        throw 'Store MSIX build failed.'
    }
    $taskCandidates = @(Get-ChildItem -LiteralPath $taskBuildOutput -Filter '*.msix' -Recurse -File | Where-Object Name -eq "Akki.ImageConverter_${Version}.0_x64.msix")
    if ($taskCandidates.Count -ne 1) { throw 'Expected exactly one generated x64 MSIX.' }
    $taskPackageFile = Join-Path $taskOutput "Akki.ImageConverter_${Version}.0_x64.msix"
    Copy-Item -LiteralPath $taskCandidates[0].FullName -Destination $taskPackageFile -Force
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackageFile)
    try {
        $taskNames = $taskArchive.Entries.FullName
        foreach ($taskRequired in 'resources.pri','coreclr.dll','Magick.Native-Q16-x64.dll','AppxManifest.xml','LICENSE','THIRD-PARTY-NOTICES.md') {
            if ($taskNames -notcontains $taskRequired) { throw "Missing package component: $taskRequired" }
        }
        if ($taskNames -match '(^|/)(Velopack\.(dll|exe)|Update\.exe)$|private-notes|private-documents') { throw 'Unexpected updater or private file in Store package.' }
        $taskReader = [IO.StreamReader]::new($taskArchive.GetEntry('AppxManifest.xml').Open())
        try { [xml]$taskBuiltManifest = $taskReader.ReadToEnd() } finally { $taskReader.Dispose() }
        if ($taskBuiltManifest.Package.Identity.Name -ne 'akki.lu.AkkiImageConverter' -or $taskBuiltManifest.Package.Identity.Publisher -ne 'CN=4FD0D4C6-4C34-4C91-860A-7F1D8A63FBF2' -or $taskBuiltManifest.Package.Identity.Version -ne "$Version.0") { throw 'Package identity mismatch.' }
        if ($taskBuiltManifest.Package.Dependencies.TargetDeviceFamily.MinVersion -ne '10.0.22000.0') { throw 'Store package must require Windows 11.' }
        $taskReader = [IO.StreamReader]::new($taskArchive.GetEntry('Akki.ImageConverter.deps.json').Open())
        try { if ($taskReader.ReadToEnd() -match 'Velopack') { throw 'Standalone updater in Store dependency graph.' } } finally { $taskReader.Dispose() }
    } finally { $taskArchive.Dispose() }
    $taskHash = (Get-FileHash -LiteralPath $taskPackageFile -Algorithm SHA256).Hash.ToLowerInvariant()
    "$taskHash  $([IO.Path]::GetFileName($taskPackageFile))" | Set-Content (Join-Path $taskOutput 'SHA256SUMS.txt')
    Write-Output "Store submission package: $taskPackageFile"
    Write-Output 'Unsigned MSIX for Partner Center. Microsoft signs the approved Store download. Nothing has been submitted.'
} finally { Pop-Location }
