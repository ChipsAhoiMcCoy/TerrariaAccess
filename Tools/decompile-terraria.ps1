Param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$TerrariaInstall = "C:\Program Files (x86)\Steam\steamapps\common\Terraria"
$TerrariaExe     = Join-Path $TerrariaInstall "Terraria.exe"
$Output          = "C:\Users\Colton\Documents\Terraria Access\Terraria Decompiled"
$Dotnet          = "C:\Users\Colton\Documents\Terraria Access\Tools\dotnet"
$IlspyCmd        = "C:\Users\Colton\Documents\Terraria Access\Tools\dotnet-tools\ilspycmd.exe"

if (-not (Test-Path $TerrariaExe)) { throw "Terraria.exe not found at: $TerrariaExe" }
if (-not (Test-Path $IlspyCmd))    { throw "ilspycmd.exe not found at: $IlspyCmd" }
if (-not (Test-Path $Dotnet))      { throw "dotnet runtime not found at: $Dotnet" }

if ($Force -and (Test-Path $Output)) {
    Write-Host "Cleaning existing output..." -ForegroundColor Cyan
    Remove-Item $Output -Recurse -Force
}
if (-not (Test-Path $Output)) {
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
}

$env:DOTNET_ROOT = $Dotnet

Write-Host "Decompiling Terraria.exe..." -ForegroundColor Cyan
Write-Host "  Source: $TerrariaExe"
Write-Host "  Output: $Output"

$sw = [System.Diagnostics.Stopwatch]::StartNew()
& $IlspyCmd $TerrariaExe -p -o $Output --nested-directories
$sw.Stop()

if ($LASTEXITCODE -ne 0) { throw "ilspycmd exited with code $LASTEXITCODE" }

$cs    = (Get-ChildItem $Output -Filter *.cs -Recurse).Count
$total = (Get-ChildItem $Output -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Done in {0:F1}s. Generated {1} .cs files ({2:F1} MB)." -f $sw.Elapsed.TotalSeconds, $cs, $total) -ForegroundColor Green
