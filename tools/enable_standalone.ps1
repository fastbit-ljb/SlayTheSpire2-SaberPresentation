param([string]$UserDataRoot = (Join-Path $env:APPDATA 'SlayTheSpire2'))
$ErrorActionPreference = 'Stop'
$normalGame = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\steamapps\common\Slay the Spire 2\*' }
if ($normalGame) { throw 'Exit the normal game before updating its mod configuration.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$backupBase = if (Test-Path -LiteralPath (Join-Path $repoRoot 'SaberPresentation.csproj')) { Join-Path $repoRoot 'build\config-backups' } else { Join-Path $PSScriptRoot 'config-backups' }
$backupRoot = Join-Path $backupBase (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
# Keep unrelated mods as configured. These installed source packs are replaced
# by the private payload and can be selected again later in the native mod menu.
$replaced = @('saber','CznCore','CznStyleUI','CznMainMenu','CznEnemySkin','CznNineCharacter','ChizuruIroncladSkin','SerenielDefectSkin','NarjaNecrobinderSkin','MeirinWatcherSkin','CznTipheraCharacter','TressaSilentSkin','HeidemarieRegentSkin')
$profileIndex = 0
foreach ($profileFile in Get-ChildItem -LiteralPath $UserDataRoot -Filter 'settings.save' -Recurse -File) {
    $profileIndex++
    Copy-Item -LiteralPath $profileFile.FullName -Destination (Join-Path $backupRoot "profile-$profileIndex.save")
    $data = Get-Content -LiteralPath $profileFile.FullName -Raw | ConvertFrom-Json
    if ($null -eq $data.mod_settings) { $data.mod_settings = [PSCustomObject]@{ mod_list=@(); mods_enabled=$true } }
    $mods = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $data.mod_settings.mod_list) { $mods.Add($entry) }
    foreach ($id in ($replaced + 'SaberPresentation')) {
        $enabled = $id -eq 'SaberPresentation'
        $entry = $mods | Where-Object id -eq $id | Select-Object -First 1
        if ($entry) { $entry.is_enabled = $enabled }
        else { $mods.Add([PSCustomObject]@{ id=$id; is_enabled=$enabled; source=$(if ($enabled) { 'mods_directory' } else { 'steam_workshop' }) }) }
    }
    $data.mod_settings.mod_list = @($mods.ToArray())
    $data.mod_settings.mods_enabled = $true
    $data | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $profileFile.FullName -Encoding utf8
}
[PSCustomObject]@{ ProfilesUpdated=$profileIndex; EnabledMod='SaberPresentation'; ReplacedSourceMods=$replaced; Backup=$backupRoot } | ConvertTo-Json
