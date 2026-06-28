#Requires -Version 5.1
<#
.SYNOPSIS
    Builds and packages WorldCupNotifier as a self-contained single-file Windows executable.

.DESCRIPTION
    1. Runs dotnet publish in Release mode (win-x64, single-file, self-contained)
    2. Copies the LICENSE / README into the output folder
    3. Creates a versioned zip archive ready for distribution

.EXAMPLE
    .\publish.ps1
    .\publish.ps1 -Version "1.2.0"
#>

param(
    [string]$Version = "1.0.0",
    [string]$OutputDir = "publish"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ZipName = "WorldCupNotifier-v$Version-win-x64.zip"
$ProjectFile = "WorldCupNotifier.csproj"

Write-Host "==> Building WorldCupNotifier v$Version" -ForegroundColor Cyan

# Clean previous output
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }

# Publish
dotnet publish $ProjectFile -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:Version=$Version -o $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

Write-Host "==> Publish complete" -ForegroundColor Green

# Copy extras into output folder
foreach ($extra in @("README.md", "LICENSE")) {
    if (Test-Path $extra) { Copy-Item $extra $OutputDir }
}

# Zip
if (Test-Path $ZipName) { Remove-Item $ZipName -Force }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipName
Write-Host "==> Archive created: $ZipName" -ForegroundColor Green

# Summary
$exe = Get-ChildItem $OutputDir -Filter "*.exe" | Select-Object -First 1
if ($exe) {
    $sizeMB = [math]::Round($exe.Length / 1MB, 1)
    Write-Host "    Executable : $($exe.Name) ($sizeMB MB)" -ForegroundColor White
}
$zipSize = [math]::Round((Get-Item $ZipName).Length / 1MB, 1)
Write-Host "    Archive    : $ZipName ($zipSize MB)" -ForegroundColor White
