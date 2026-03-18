Param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info([string]$Message) {
    Write-Host "INFO: $Message" -ForegroundColor Cyan
}

function Write-Success([string]$Message) {
    Write-Host "SUCCESS: $Message" -ForegroundColor Green
}

function Write-Failure([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
}

# Configuration
$TmlInstallPath = "C:\Program Files (x86)\Steam\steamapps\common\tModLoader"
$TmlDllPath = Join-Path $TmlInstallPath "tModLoader.dll"
$DecompiledPath = Join-Path $TmlInstallPath "TModLoaderDecompiled"

# Verify tModLoader.dll exists
if (-not (Test-Path $TmlDllPath)) {
    Write-Failure "tModLoader.dll not found at: $TmlDllPath"
    exit 1
}

# Check if ilspycmd is available
$ilspyCmd = Get-Command "ilspycmd" -ErrorAction SilentlyContinue
if (-not $ilspyCmd) {
    Write-Failure "ilspycmd not found. Install with: dotnet tool install -g ilspycmd"
    exit 1
}

# Get DLL modification time
$dllLastWrite = (Get-Item $TmlDllPath).LastWriteTime

# Check if decompilation is needed
$needsDecompile = $Force

if (-not $needsDecompile -and (Test-Path $DecompiledPath)) {
    # Check if any .cs file exists and compare timestamps
    $csFiles = Get-ChildItem $DecompiledPath -Filter "*.cs" -Recurse | Select-Object -First 1
    if ($csFiles) {
        $decompiledTime = $csFiles.LastWriteTime
        if ($dllLastWrite -gt $decompiledTime) {
            Write-Info "tModLoader.dll has been updated since last decompilation"
            $needsDecompile = $true
        } else {
            Write-Info "Decompiled source is up to date (DLL: $dllLastWrite, Decompiled: $decompiledTime)"
        }
    } else {
        Write-Info "No .cs files found in decompiled folder"
        $needsDecompile = $true
    }
} elseif (-not (Test-Path $DecompiledPath)) {
    Write-Info "Decompiled folder does not exist"
    $needsDecompile = $true
}

if (-not $needsDecompile) {
    Write-Success "Decompiled source is already up to date. Use -Force to re-decompile anyway."
    exit 0
}

# Clean existing decompiled folder
if (Test-Path $DecompiledPath) {
    Write-Info "Removing existing decompiled folder..."
    Remove-Item $DecompiledPath -Recurse -Force
}

# Create fresh folder
New-Item -ItemType Directory -Path $DecompiledPath -Force | Out-Null

# Decompile
Write-Info "Decompiling tModLoader.dll (this may take a few minutes)..."
Write-Info "Source: $TmlDllPath"
Write-Info "Output: $DecompiledPath"

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

& ilspycmd $TmlDllPath -p -o $DecompiledPath

$stopwatch.Stop()

if ($LASTEXITCODE -ne 0) {
    Write-Failure "Decompilation failed with exit code $LASTEXITCODE"
    exit 1
}

# Report results
$csCount = (Get-ChildItem $DecompiledPath -Filter "*.cs" -Recurse).Count
$totalSize = (Get-ChildItem $DecompiledPath -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB

Write-Success "Decompilation completed in $($stopwatch.Elapsed.TotalSeconds.ToString('F1')) seconds"
Write-Info "Generated $csCount .cs files ($($totalSize.ToString('F1')) MB total)"
Write-Info "Output: $DecompiledPath"
