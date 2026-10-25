param([switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $projectRoot 'SaberPresentation.csproj') -c Release -p:DefineConstants=SABER_DEMO -o (Join-Path $projectRoot 'build\demo-dll') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Demo build failed' }
$runner = Join-Path $projectRoot 'build\runner-demo'
$gameRoot = 'D:\L\Game\Steam\steamapps\common\Slay the Spire 2'
New-Item -ItemType Directory -Path $runner -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $gameRoot -File) {
    $target = Join-Path $runner $file.Name
    if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null }
}
$dataLink = Join-Path $runner 'data_sts2_windows_x86_64'
if (-not (Test-Path -LiteralPath $dataLink)) { New-Item -ItemType Junction -Path $dataLink -Target (Join-Path $gameRoot 'data_sts2_windows_x86_64') | Out-Null }
$modRoot = Join-Path $runner 'mods\SaberPresentation'
New-Item -ItemType Directory -Path $modRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\demo-dll\SaberPresentation.dll'),(Join-Path $projectRoot 'SaberPresentation.json'),(Join-Path $projectRoot 'build\SaberPresentation.pck') -Destination $modRoot -Force
$bundles = Join-Path $modRoot 'bundled'
New-Item -ItemType Directory -Path $bundles -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'build\bundled') -File) {
    $target = Join-Path $bundles $file.Name
    if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType HardLink -Path $target -Target $file.FullName | Out-Null }
}
@'
[application]
config/use_custom_user_dir=true
config/custom_user_dir_name="SlayTheSpire2-SaberPresentation-Demo"
[display]
window/size/viewport_width=1920
window/size/viewport_height=1080
window/size/mode=3
'@ | Set-Content -LiteralPath (Join-Path $runner 'override.cfg') -Encoding utf8
$demoData = Join-Path $env:APPDATA 'SlayTheSpire2-SaberPresentation-Demo'
if (-not (Test-Path -LiteralPath $demoData)) {
    New-Item -ItemType Directory -Path $demoData -Force | Out-Null
    $testData = Join-Path $env:APPDATA 'SlayTheSpire2-SaberPresentation-Test'
    foreach ($folder in @('default','mod_configs','CznModConfig')) {
        $source = Join-Path $testData $folder
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $demoData -Recurse -Force }
    }
}
if (-not $BuildOnly) {
    $demoProcess = Start-Process -FilePath (Join-Path $runner 'SlayTheSpire2.exe') -ArgumentList '--force-steam=off','--saber-demo','--fullscreen','--log-file','saber_demo.log' -WorkingDirectory $runner -PassThru
    [PSCustomObject]@{ Process=$demoProcess.Id; DemoProfile=$demoData; Controls='F8 start/replay; F9 hide hint' } | ConvertTo-Json
}
