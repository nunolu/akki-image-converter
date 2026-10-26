param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug', [switch]$Run)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    $env:DOTNET_CLI_HOME = Join-Path $taskRoot '.buildtools'
    $env:NUGET_PACKAGES = Join-Path $taskRoot '.packages'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    dotnet restore Akki.ImageConverter.sln
    if ($LASTEXITCODE) { throw 'Dependency restore failed.' }
    dotnet build Akki.ImageConverter.sln -c $Configuration -p:Platform=x64 --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    dotnet run --project tests/Akki.ImageConverter.Tests -c $Configuration --no-restore
    if ($LASTEXITCODE) { throw 'Conversion tests failed.' }
    if ($Run) {
        $taskExe = Join-Path $taskRoot "src\Akki.ImageConverter\bin\x64\$Configuration\net10.0-windows10.0.26100.0\win-x64\Akki.ImageConverter.exe"
        Start-Process -FilePath $taskExe
    }
} finally { Pop-Location }
