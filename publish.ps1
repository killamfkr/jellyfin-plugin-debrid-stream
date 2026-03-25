# Publish without relying on PATH (works in Cursor/VS Code terminals that don't see dotnet).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$candidates = @(
    Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
    Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe'
    Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
)
$dotnet = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $dotnet) {
    Write-Error @"
.NET SDK not found. Install it:
  winget install Microsoft.DotNet.SDK.9
Or: https://dotnet.microsoft.com/download/dotnet/9.0
"@
}
$csproj = Join-Path $root 'Jellyfin.Plugin.DebridStream.csproj'
$out = Join-Path $root 'publish'
Write-Host "Using: $dotnet"
& $dotnet publish $csproj -c Release -o $out
Write-Host "Output: $out (Jellyfin.Plugin.DebridStream.dll + meta.json)"
