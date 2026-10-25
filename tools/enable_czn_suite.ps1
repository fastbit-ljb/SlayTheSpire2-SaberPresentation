param([string]$UserDataRoot = (Join-Path $env:APPDATA 'SlayTheSpire2'))
$ErrorActionPreference = 'Stop'
$normalGame = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\steamapps\common\Slay the Spire 2\*' }
if ($normalGame) { throw 'Exit the normal game before updating its mod configuration.' }
$repoRoot = Split-Path -Parent $PSScriptRoot
$backupBase = if (Test-Path -LiteralPath (Join-Path $repoRoot 'SaberPresentation.csproj')) { Join-Path $repoRoot 'build\config-backups' } else { Join-Path $PSScriptRoot 'config-backups' }
$backupRoot = Join-Path $backupBase (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$relatedIds = @('saber','SaberPresentation','CznCore','CznNineCharacter','CznStyleUI','CznMainMenu','CznEnemySkin')
$profileIndex = 0
foreach ($profileFile in Get-ChildItem -LiteralPath $UserDataRoot -Filter 'settings.save' -Recurse -File) {
    $profileIndex++
    Copy-Item -LiteralPath $profileFile.FullName -Destination (Join-Path $backupRoot "profile-$profileIndex.save")
    $data = Get-Content -LiteralPath $profileFile.FullName -Raw | ConvertFrom-Json
    if ($null -eq $data.mod_settings) {
        $data.mod_settings = [PSCustomObject]@{ mod_list = @(); mods_enabled = $true }
    }
    $mods = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $data.mod_settings.mod_list) { $mods.Add($entry) }
    foreach ($id in $relatedIds) {
        $entry = $mods | Where-Object id -eq $id | Select-Object -First 1
        if ($entry) { $entry.is_enabled = $true }
        else { $mods.Add([PSCustomObject]@{ id=$id; is_enabled=$true; source=$(if ($id -eq 'SaberPresentation') { 'mods_directory' } else { 'steam_workshop' }) }) }
    }
    $data.mod_settings.mod_list = @($mods.ToArray())
    $data.mod_settings.mods_enabled = $true
    $data | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $profileFile.FullName -Encoding utf8
}
$menuDirectory = Join-Path $UserDataRoot 'CznModConfig\CznMainMenu'
$menuFile = Join-Path $menuDirectory 'settings.cfg'
New-Item -ItemType Directory -Path $menuDirectory -Force | Out-Null
$menuText = "[main_menu]`nstartup_animation_mode=czn_skin_intro`n"
if (Test-Path -LiteralPath $menuFile) {
    Copy-Item -LiteralPath $menuFile -Destination (Join-Path $backupRoot 'main-menu-settings.cfg')
    $menuText = Get-Content -LiteralPath $menuFile -Raw
}
foreach ($pair in @(@('background_mode','fixed'), @('background_id','skin_lobby_saber'), @('music_mode','follow_background'), @('music_id','story_memorial_precious_moments'))) {
    $pattern = '(?m)^' + [regex]::Escape($pair[0]) + '=.*$'
    $replacement = $pair[0] + '=' + $pair[1]
    if ($menuText -match $pattern) { $menuText = [regex]::Replace($menuText, $pattern, $replacement) }
    else { $menuText += "`n$replacement" }
}
$menuText | Set-Content -LiteralPath $menuFile -Encoding utf8
[PSCustomObject]@{ ProfilesUpdated=$profileIndex; EnabledMods=$relatedIds; MainMenu='skin_lobby_saber'; Backup=$backupRoot } | ConvertTo-Json
