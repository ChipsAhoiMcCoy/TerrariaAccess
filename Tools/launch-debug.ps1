<#
.SYNOPSIS
    Launches tModLoader with debug logging enabled for ScreenReaderMod.

.DESCRIPTION
    Sets the SRM_DEBUG_INPUT environment variable and launches tModLoader.
    This enables verbose logging for input state and inventory focus tracking.

    Logs will be written to:
    C:\Users\<username>\Documents\My Games\Terraria\tModLoader\Logs\client.log

.EXAMPLE
    .\launch-debug.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Find tModLoader installation
$steamPaths = @(
    "C:\Program Files (x86)\Steam\steamapps\common\tModLoader",
    "C:\Program Files\Steam\steamapps\common\tModLoader",
    "D:\Steam\steamapps\common\tModLoader",
    "D:\SteamLibrary\steamapps\common\tModLoader"
)

$tModLoaderPath = $null
foreach ($path in $steamPaths) {
    if (Test-Path "$path\tModLoader.dll") {
        $tModLoaderPath = $path
        break
    }
}

if (-not $tModLoaderPath) {
    Write-Error "Could not find tModLoader installation. Please edit this script to add your Steam path."
    exit 1
}

$dotnetPath = Join-Path $tModLoaderPath "dotnet\dotnet.exe"
$tModLoaderDll = Join-Path $tModLoaderPath "tModLoader.dll"

if (-not (Test-Path $dotnetPath)) {
    Write-Error "Could not find dotnet at: $dotnetPath"
    exit 1
}

# Set debug environment variables
$env:SRM_DEBUG_INPUT = "1"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ScreenReaderMod Debug Launch" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Debug logging ENABLED for:" -ForegroundColor Green
Write-Host "  - Input state (mode, triggers, link points)"
Write-Host "  - Inventory focus tracking"
Write-Host ""
Write-Host "Logs will be written to:" -ForegroundColor Yellow
Write-Host "  $env:USERPROFILE\Documents\My Games\Terraria\tModLoader\Logs\client.log"
Write-Host ""
Write-Host "Look for lines starting with:" -ForegroundColor Yellow
Write-Host "  [InputDebug] - Input mode and trigger state"
Write-Host "  [FocusDebug] - Inventory focus tracking"
Write-Host ""
Write-Host "Launching tModLoader..." -ForegroundColor Cyan
Write-Host ""

# Launch tModLoader
Push-Location $tModLoaderPath
try {
    & $dotnetPath $tModLoaderDll
}
finally {
    Pop-Location
}
