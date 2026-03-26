# Build a Jellyfin catalog .zip (dll + meta.json) and print MD5 for manifest.json "checksum".
$ErrorActionPreference = 'Stop'
& $PSScriptRoot\publish.ps1
$pub = Join-Path $PSScriptRoot 'publish'
$ver = '1.2.0.0'
$zip = Join-Path $PSScriptRoot "DebridStream_$ver.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $pub 'Jellyfin.Plugin.DebridStream.dll'), (Join-Path $pub 'meta.json') -DestinationPath $zip -Force
$md5 = (Get-FileHash $zip -Algorithm MD5).Hash.ToLowerInvariant()
Write-Host ""
Write-Host "Created: $zip"
Write-Host "MD5 for manifest.json checksum field: $md5"
Write-Host "Upload this zip to GitHub Release tag v$ver (same filename as in manifest.json sourceUrl)."
