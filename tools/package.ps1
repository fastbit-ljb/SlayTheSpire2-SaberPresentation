param([switch]$Install, [switch]$EnableStandalone)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'SaberPresentation.json') -Raw | ConvertFrom-Json).version
$packageRoot = Join-Path $projectRoot "dist\$version\SaberPresentation"
& dotnet build (Join-Path $projectRoot 'SaberPresentation.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
& python -X utf8 (Join-Path $PSScriptRoot 'prepare_assets.py')
if ($LASTEXITCODE -ne 0) { throw 'Asset build failed' }
& python -X utf8 (Join-Path $PSScriptRoot 'prepare_standalone.py')
if ($LASTEXITCODE -ne 0) { throw 'Standalone payload build failed' }
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
$artifacts = @(
    (Join-Path $projectRoot 'bin\Release\net9.0\SaberPresentation.dll'),
    (Join-Path $projectRoot 'SaberPresentation.json'),
    (Join-Path $projectRoot 'build\SaberPresentation.pck')
)
Copy-Item -LiteralPath $artifacts -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\sources.json') -Destination (Join-Path $packageRoot 'ASSET_SOURCES.json') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CZN_ASSETS.json'),(Join-Path $projectRoot 'VERSION_HISTORY.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CZN_PARITY.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\generated\provenance.json') -Destination (Join-Path $packageRoot 'GENERATED_ASSETS.json') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\generated\PROMPTS.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\generated\PROMPTS_ANIME_V2.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\generated\PROMPTS_RIG.md'),(Join-Path $projectRoot 'NINE_PORTRAIT_REVIEW.md'),(Join-Path $projectRoot 'CHIZURU_PRESENTATION_REVIEW.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'CZN_FULLSCREEN_PRESENTATION_REVIEW.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'STANDALONE_COMPONENTS.json'),(Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'enable_standalone.ps1') -Destination (Join-Path $packageRoot 'ENABLE_STANDALONE.ps1') -Force
$bundledPackage = Join-Path $packageRoot 'bundled'
New-Item -ItemType Directory -Path $bundledPackage -Force | Out-Null
$componentLock = Get-Content -LiteralPath (Join-Path $projectRoot 'STANDALONE_COMPONENTS.json') -Raw | ConvertFrom-Json
$payloadFiles = @($componentLock.libraries | ForEach-Object { $_.files | ForEach-Object { Join-Path $projectRoot ('build\' + $_.file) } })
if ($payloadFiles.Count -gt 0) { Copy-Item -LiteralPath $payloadFiles -Destination $bundledPackage -Force }
$zip = Join-Path $projectRoot "dist\SaberPresentation-$version.zip"
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zip -CompressionLevel Fastest -Force
if ($Install) {
    $liveGame = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\steamapps\common\Slay the Spire 2\*' }
    if ($liveGame) { throw 'The verified ZIP is ready. Save and exit the normal game before replacing its resource pack.' }
    $installRoot = 'D:\L\Game\Steam\steamapps\common\Slay the Spire 2\mods\SaberPresentation'
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Copy-Item -LiteralPath $artifacts -Destination $installRoot -Force
    $bundledInstall = Join-Path $installRoot 'bundled'
    New-Item -ItemType Directory -Path $bundledInstall -Force | Out-Null
    foreach ($removed in @('CznCore.dll','CznCore.pck','CznStyleUI.dll','CznStyleUI.pck','CznMainMenu.dll','CznMainMenu.pck','CznEnemySkin.dll','CznEnemySkin.pck')) {
        $removedPath = Join-Path $bundledInstall $removed
        if (Test-Path -LiteralPath $removedPath) { Remove-Item -LiteralPath $removedPath -Force }
    }
    if ($payloadFiles.Count -gt 0) { Copy-Item -LiteralPath $payloadFiles -Destination $bundledInstall -Force }
    Copy-Item -LiteralPath (Join-Path $packageRoot 'STANDALONE_COMPONENTS.json'),(Join-Path $packageRoot 'THIRD_PARTY_NOTICES.md'),(Join-Path $packageRoot 'README.md') -Destination $installRoot -Force
    Write-Output "Installed: $installRoot"
}
if ($EnableStandalone) { & (Join-Path $PSScriptRoot 'enable_standalone.ps1') }
Get-Item -LiteralPath $zip | Select-Object FullName,Length
