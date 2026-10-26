param([string]$Version = '0.1.3', [string]$ReleaseNotes = '')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch version.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.buildtools'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $taskPublish = Join-Path $taskRoot "artifacts\publish-$Version"
    $taskReleases = Join-Path $taskRoot 'artifacts\releases'
    dotnet tool restore
    if ($LASTEXITCODE) { throw 'Installer tool restore failed.' }
    dotnet publish src/Akki.ImageConverter/Akki.ImageConverter.csproj -c Release -r win-x64 -p:Platform=x64 -p:Version=$Version --self-contained -o $taskPublish
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $taskPublish 'Akki.ImageConverter.pri'))) {
        throw 'Published WinUI resource index is missing; refusing to create a broken installer.'
    }

    # Preserve the licence notices included in every dependency package.
    $taskLicenses = Join-Path $taskPublish 'licenses'
    New-Item -ItemType Directory -Force $taskLicenses | Out-Null
    $taskAssets = Get-Content 'src\Akki.ImageConverter\obj\project.assets.json' -Raw | ConvertFrom-Json -AsHashtable
    foreach ($taskLibrary in $taskAssets.libraries.Values) {
        if ($taskLibrary.type -ne 'package') { continue }
        $taskPackage = Join-Path $env:NUGET_PACKAGES $taskLibrary.path
        foreach ($taskNotice in Get-ChildItem -LiteralPath $taskPackage -File -Recurse | Where-Object { $_.Name -match '(?i)(licen[cs]e|copyright|notice|third.party)' -and $_.Extension -notin '.dll','.pdb','.xml' }) {
            $taskName = $taskLibrary.path.Replace('/','-') + '-' + $taskNotice.FullName.Substring($taskPackage.Length + 1).Replace('\','-')
            Copy-Item -LiteralPath $taskNotice.FullName -Destination (Join-Path $taskLicenses $taskName)
        }
    }
    # Checked-in licence texts cover expression-only packages without fetching moving branches.
    Copy-Item -Path (Join-Path $taskRoot 'third-party\licenses\*') -Destination $taskLicenses -Force
    $taskAgreement = Join-Path $taskLicenses 'INSTALLER-LICENSES.md'
    $taskAgreementText = @(
        '# Akki Image Converter',
        'Original application code is licensed under MIT. Third-party components retain their own licences.',
        'By installing, you accept the applicable Microsoft Windows App SDK runtime terms reproduced below. They apply only to those Microsoft components.',
        'Nothing here limits your rights under the MIT licence or the licences of LGPL/MPL-covered components. Full third-party notices and matching native sources accompany this release.',
        (Get-Content -LiteralPath (Join-Path $taskRoot 'LICENSE') -Raw),
        (Get-Content -LiteralPath (Join-Path $taskRoot 'third-party\licenses\microsoft.windowsappsdk-1.8.260921001-license.txt') -Raw)
    ) -join "`n`n"
    Set-Content -LiteralPath $taskAgreement -Value $taskAgreementText
    $taskArguments = @('pack','--runtime','win-x64','--packId','lu.akki.ImageConverter','--packVersion',$Version,'--packDir',$taskPublish,
        '--mainExe','Akki.ImageConverter.exe','--packTitle','Akki Image Converter','--packAuthors','akki.lu',
        '--icon',(Join-Path $taskRoot 'src\Akki.ImageConverter\Assets\App.ico'),
        '--shortcuts','Desktop,StartMenuRoot','--instLicense',$taskAgreement,'--outputDir',$taskReleases)
    if ($ReleaseNotes) { $taskArguments += @('--releaseNotes', (Resolve-Path -LiteralPath $ReleaseNotes).Path) }
    dotnet vpk @taskArguments
    if ($LASTEXITCODE) { throw 'Installer packaging failed.' }
    $taskPackageFile = Join-Path $taskReleases "lu.akki.ImageConverter-$Version-full.nupkg"
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackageFile)
    try {
        if (-not ($taskArchive.Entries | Where-Object { $_.FullName.EndsWith('/Akki.ImageConverter.pri', [StringComparison]::OrdinalIgnoreCase) })) {
            throw 'The release package is missing its WinUI resources.'
        }
    } finally { $taskArchive.Dispose() }
    Write-Host "Installer and update feed: $taskReleases"
    Write-Host 'This is an unsigned development release. Nothing has been uploaded.'
} finally { Pop-Location }
