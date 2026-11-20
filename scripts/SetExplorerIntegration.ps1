param(
    [Parameter(Mandatory)][ValidateSet('Enable','Disable')][string]$Action,
    [string]$Executable
)
$ErrorActionPreference = 'Stop'
$taskExtensions = @('.png','.jpg','.jpeg','.webp','.avif','.tif','.tiff','.bmp','.ico','.gif')
$taskMarker = 'AkkiImageToolsExplorerIntegration'
$taskVerb = 'AkkiImageTools.Open'
if ($Action -eq 'Enable') {
    if ([string]::IsNullOrWhiteSpace($Executable)) { throw 'Pass -Executable with the full path to Akki.ImageConverter.exe.' }
    $taskExe = (Resolve-Path -LiteralPath $Executable -ErrorAction Stop).Path
    if ((Split-Path -Leaf $taskExe) -ine 'Akki.ImageConverter.exe' -or -not (Test-Path -LiteralPath $taskExe -PathType Leaf)) { throw 'Executable must be an existing Akki.ImageConverter.exe.' }
}
$taskKeys = foreach ($taskExtension in $taskExtensions) { "HKCU:\Software\Classes\SystemFileAssociations\$taskExtension\shell\$taskVerb" }
# Never replace or remove a verb that was not created by this script.
foreach ($taskKey in $taskKeys) {
    if (Test-Path -LiteralPath $taskKey) {
        $taskExisting = (Get-ItemProperty -LiteralPath $taskKey -Name $taskMarker -ErrorAction SilentlyContinue).$taskMarker
        if ($taskExisting -ne 1) { throw "An existing unrelated registry key was found at $taskKey. No changes were made." }
    }
}
foreach ($taskKey in $taskKeys) {
    if ($Action -eq 'Disable') {
        if (Test-Path -LiteralPath $taskKey) { Remove-Item -LiteralPath $taskKey -Recurse -Force }
        continue
    }
    New-Item -Path $taskKey -Force | Out-Null
    Set-Item -LiteralPath $taskKey -Value 'Open in Akki Image Tools'
    New-ItemProperty -LiteralPath $taskKey -Name $taskMarker -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $taskKey -Name 'Icon' -Value $taskExe -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $taskKey -Name 'MultiSelectModel' -Value 'Document' -PropertyType String -Force | Out-Null
    $taskCommand = Join-Path $taskKey 'command'
    New-Item -Path $taskCommand -Force | Out-Null
    Set-Item -LiteralPath $taskCommand -Value ('"{0}" "%1"' -f $taskExe)
}
Write-Host "Explorer integration: $Action. On Windows 11, look under Show more options."
if ($Action -eq 'Enable') { Write-Host "Executable: $taskExe" }
