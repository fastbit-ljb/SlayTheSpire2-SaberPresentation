param([switch]$Upload)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectRoot 'SaberPresentation.json') -Raw | ConvertFrom-Json).version
$releaseRoot = Join-Path $projectRoot "dist\$version\SaberPresentation"
$workspaceRoot = Join-Path (Split-Path -Parent $projectRoot) 'SaberPresentationWorkshop'
$sourceRoot = Join-Path $projectRoot 'workshop'
$uploaderRoot = Join-Path (Split-Path -Parent $projectRoot) 'ModUploader-win-x64'
if (-not (Test-Path -LiteralPath (Join-Path $releaseRoot 'SaberPresentation.pck'))) { throw 'Build the release package first.' }
New-Item -ItemType Directory -Path $workspaceRoot -Force | Out-Null
$contentRoot = Join-Path $workspaceRoot 'content'
New-Item -ItemType Directory -Path $contentRoot -Force | Out-Null
foreach ($entry in Get-ChildItem -LiteralPath $releaseRoot -Force) {
    Copy-Item -LiteralPath $entry.FullName -Destination $contentRoot -Recurse -Force
}
foreach ($entry in Get-ChildItem -LiteralPath $sourceRoot -Force) {
    Copy-Item -LiteralPath $entry.FullName -Destination $workspaceRoot -Recurse -Force
}
$payload = @(Get-ChildItem -LiteralPath $releaseRoot -File -Recurse)
if (@(Get-ChildItem -LiteralPath $contentRoot -File -Recurse).Count -ne $payload.Count) { throw 'Workshop content contains stale files; inspect it before upload.' }
foreach ($entry in $payload) {
    $relative = [IO.Path]::GetRelativePath($releaseRoot, $entry.FullName)
    $target = Join-Path $contentRoot $relative
    if ((Get-FileHash -LiteralPath $entry.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw "Workshop file mismatch: $relative" }
}
foreach ($picture in @((Join-Path $workspaceRoot 'image.png')) + @(Get-ChildItem -LiteralPath (Join-Path $workspaceRoot 'previews') -File | ForEach-Object FullName)) {
    if ((Get-Item -LiteralPath $picture).Length -ge 1000000) { throw "Workshop preview exceeds 1 MB: $picture" }
}
Write-Output "Prepared verified $version workshop content: $workspaceRoot"
if ($Upload) {
    Push-Location $uploaderRoot
    try {
        & (Join-Path $uploaderRoot 'ModUploader.exe') upload --workspace $workspaceRoot
        if ($LASTEXITCODE -ne 0) { throw 'Steam Workshop upload failed; inspect mod-uploader.log.' }
    } finally { Pop-Location }
    $idPath = Join-Path $workspaceRoot 'mod_id.txt'
    if (-not (Test-Path -LiteralPath $idPath)) { throw 'No Workshop item ID was returned.' }
    Copy-Item -LiteralPath $idPath -Destination (Join-Path $sourceRoot 'mod_id.txt') -Force
    $itemId = (Get-Content -LiteralPath $idPath -Raw).Trim()
    Write-Output "Workshop item: https://steamcommunity.com/sharedfiles/filedetails/?id=$itemId"
}
