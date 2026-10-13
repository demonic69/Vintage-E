# Builds Vintage-E and zips it into release\vintagee_<version>.zip, ready to drop into your Mods folder.
# Requires the .NET 10 SDK and the VINTAGE_STORY environment variable pointing at a Vintage Story 1.22 install.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

if (-not $env:VINTAGE_STORY) {
    Write-Error 'Set VINTAGE_STORY to your Vintage Story install folder (the one with VintagestoryAPI.dll).'
}

dotnet build -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$version = (Get-Content modinfo.json -Raw | ConvertFrom-Json).version
$out = "release/vintagee_$version.zip"
New-Item -ItemType Directory -Force -Path release | Out-Null
if (Test-Path $out) { Remove-Item $out }
$src = 'bin/Release/Mods/vintagee'
Compress-Archive -Path "$src/modinfo.json", "$src/modicon.png", "$src/VintageE.dll", "$src/THIRD-PARTY-NOTICES.md", "$src/assets" -DestinationPath $out
Write-Host "Built $out"
