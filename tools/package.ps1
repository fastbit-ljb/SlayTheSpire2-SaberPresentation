param([switch]$Install)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'SaberPresentation.json') -Raw | ConvertFrom-Json).version
$packageRoot = Join-Path $projectRoot 'dist\SaberPresentation'
& dotnet build (Join-Path $projectRoot 'SaberPresentation.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release build failed' }
& python -X utf8 (Join-Path $PSScriptRoot 'prepare_assets.py')
if ($LASTEXITCODE -ne 0) { throw 'Asset build failed' }
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
$zip = Join-Path $projectRoot "dist\SaberPresentation-$version.zip"
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zip -Force
if ($Install) {
    $installRoot = 'D:\L\Game\Steam\steamapps\common\Slay the Spire 2\mods\SaberPresentation'
    New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    Copy-Item -LiteralPath $artifacts -Destination $installRoot -Force
    Write-Output "Installed: $installRoot"
}
Get-Item -LiteralPath $zip | Select-Object FullName,Length
