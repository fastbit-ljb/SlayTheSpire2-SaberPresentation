param([switch]$WithCzn, [switch]$WithTheme, [switch]$WithAllMods)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$runnerRoot = Join-Path $projectRoot $(if ($WithAllMods) { 'build\runner-all' } elseif ($WithTheme) { 'build\runner-theme' } elseif ($WithCzn) { 'build\runner-czn' } else { 'build\runner' })
$gameRoot = 'D:\L\Game\Steam\steamapps\common\Slay the Spire 2'
New-Item -ItemType Directory -Path $runnerRoot -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $gameRoot -File) {
    $destination = Join-Path $runnerRoot $file.Name
    if (-not (Test-Path -LiteralPath $destination)) {
        New-Item -ItemType HardLink -Path $destination -Target $file.FullName | Out-Null
    }
}
$dataLink = Join-Path $runnerRoot 'data_sts2_windows_x86_64'
if (-not (Test-Path -LiteralPath $dataLink)) {
    New-Item -ItemType Junction -Path $dataLink -Target (Join-Path $gameRoot 'data_sts2_windows_x86_64') | Out-Null
}
$testMods = Join-Path $runnerRoot 'mods'
New-Item -ItemType Directory -Path (Join-Path $testMods 'saber') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $testMods 'SaberPresentation') -Force | Out-Null
Copy-Item -LiteralPath 'D:\L\Game\Steam\steamapps\workshop\content\2868840\3747659940\saber.pck','D:\L\Game\Steam\steamapps\workshop\content\2868840\3747659940\saber.json' -Destination (Join-Path $testMods 'saber') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\test-dll\SaberPresentation.dll'),(Join-Path $projectRoot 'SaberPresentation.json'),(Join-Path $projectRoot 'build\SaberPresentation.pck') -Destination (Join-Path $testMods 'SaberPresentation') -Force
if ($WithCzn -or $WithTheme -or $WithAllMods) {
    $packs = @(@{Id='CznCore'; Workshop='3826011578'}, @{Id='CznNineCharacter'; Workshop='3815657213'})
    if ($WithTheme -or $WithAllMods) {
        $packs += @(@{Id='CznStyleUI'; Workshop='3771060891'}, @{Id='CznMainMenu'; Workshop='3774940754'}, @{Id='CznEnemySkin'; Workshop='3783041069'}, @{Id='ChizuruIroncladSkin'; Workshop='3826011833'})
    }
    foreach ($borrowed in $packs) {
        $borrowedSource = "D:\L\Game\Steam\steamapps\workshop\content\2868840\$($borrowed.Workshop)"
        $borrowedTarget = Join-Path $testMods $borrowed.Id
        New-Item -ItemType Directory -Path $borrowedTarget -Force | Out-Null
        foreach ($extension in @('dll','json','pck')) {
            $sourceFile = Join-Path $borrowedSource "$($borrowed.Id).$extension"
            $targetFile = Join-Path $borrowedTarget "$($borrowed.Id).$extension"
            if ($extension -eq 'pck') {
                if (-not (Test-Path -LiteralPath $targetFile)) { New-Item -ItemType HardLink -Path $targetFile -Target $sourceFile | Out-Null }
            } else { Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force }
        }
    }
}
if ($WithAllMods) {
    foreach ($sourceFolder in Get-ChildItem -LiteralPath 'D:\L\Game\Steam\steamapps\workshop\content\2868840' -Directory) {
        foreach ($manifestFile in Get-ChildItem -LiteralPath $sourceFolder.FullName -Filter '*.json' -File) {
            $manifestData = Get-Content -LiteralPath $manifestFile.FullName -Raw | ConvertFrom-Json
            if (-not $manifestData.id) { continue }
            $allModTarget = Join-Path $testMods $manifestData.id
            New-Item -ItemType Directory -Path $allModTarget -Force | Out-Null
            foreach ($modFile in Get-ChildItem -LiteralPath $sourceFolder.FullName -File | Where-Object Extension -in '.dll','.json','.pck') {
                $modTargetFile = Join-Path $allModTarget $modFile.Name
                if ($modFile.Extension -eq '.pck') {
                    if (-not (Test-Path -LiteralPath $modTargetFile)) { New-Item -ItemType HardLink -Path $modTargetFile -Target $modFile.FullName | Out-Null }
                } else { Copy-Item -LiteralPath $modFile.FullName -Destination $modTargetFile -Force }
            }
        }
    }
}
@'
[application]
config/use_custom_user_dir=true
config/custom_user_dir_name="SlayTheSpire2-SaberPresentation-Test"
[display]
window/size/viewport_width=1600
window/size/viewport_height=900
window/size/mode=0
'@ | Set-Content -LiteralPath (Join-Path $runnerRoot 'override.cfg') -Encoding utf8
@'
extends SceneTree
func _initialize():
    print("SABER_TEST_USER_DIR=" + OS.get_user_data_dir())
    quit()
'@ | Set-Content -LiteralPath (Join-Path $runnerRoot 'probe.gd') -Encoding utf8
Write-Output $runnerRoot
