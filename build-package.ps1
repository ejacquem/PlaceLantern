# Builds the mod and creates dist\LanternFrame-<version>.zip for Thunderstore.
# Usage: .\build-package.ps1 [-ValheimDir "..."] [-ProfileDir "..."]
param(
    [string]$ValheimDir = "C:\Program Files (x86)\Steam\steamapps\common\Valheim",
    [string]$ProfileDir = "$env:APPDATA\r2modmanPlus-local\Valheim\profiles\Default"
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet build "$root\LanternFrame\LanternFrame.csproj" -c Release "-p:ValheimDir=$ValheimDir" "-p:ProfileDir=$ProfileDir"
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$manifest = Get-Content "$root\thunderstore\manifest.json" -Raw | ConvertFrom-Json
$stage = "$root\dist\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "$root\LanternFrame\bin\Release\net48\LanternFrame.dll" $stage
Copy-Item "$root\thunderstore\manifest.json", "$root\thunderstore\README.md", "$root\thunderstore\CHANGELOG.md", "$root\thunderstore\icon.png" $stage

$zip = "$root\dist\$($manifest.name)-$($manifest.version_number).zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path "$stage\*" -DestinationPath $zip
Write-Host "Created $zip"
