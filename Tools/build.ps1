Param(
    [switch]$SkipDeploy
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

function Resolve-TmlPath([string]$RepoRoot) {
    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($envVar in @("TML_INSTALL_PATH", "TERRARIA_TML_PATH", "TMLSteamPath")) {
        $value = [Environment]::GetEnvironmentVariable($envVar)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            $candidates.Add($value)
        }
    }

    $distPath = Join-Path $RepoRoot "tModLoader_dist"
    if (Test-Path $distPath) {
        $candidates.Add($distPath)
    }

    $repoTml = Join-Path $RepoRoot "tModLoader"
    if (Test-Path $repoTml) {
        $candidates.Add($repoTml)
    }

    foreach ($programFilesKey in @("ProgramFiles", "ProgramFiles(x86)")) {
        $root = [Environment]::GetEnvironmentVariable($programFilesKey)
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            $candidates.Add((Join-Path $root "Steam\steamapps\common\tModLoader"))
        }
    }

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $expanded = [Environment]::ExpandEnvironmentVariables($candidate)
        if (Test-Path $expanded) {
            return (Resolve-Path $expanded).Path
        }
    }

    throw "Unable to locate a tModLoader installation. Set TML_INSTALL_PATH or install via Steam."
}

function Resolve-DotNet([string]$TmlPath) {
    $embedded = Join-Path $TmlPath "dotnet\dotnet.exe"
    if (Test-Path $embedded) {
        return (Resolve-Path $embedded).Path
    }

    $global = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $global) {
        return $global.Path
    }

    throw "Unable to locate dotnet. Install the .NET 8.0 SDK or ensure tModLoader's bundled runtime is present."
}

function Invoke-Build([string]$DotNetExe, [string]$TmlPath, [string]$ModSourcePath) {
    Write-Info "Invoking dotnet build pipeline at $TmlPath"

    Push-Location $TmlPath
    try {
        & $DotNetExe "tModLoader.dll" "-build" $ModSourcePath
        $exitCode = if ($null -eq $global:LASTEXITCODE) { 0 } else { $global:LASTEXITCODE }
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "tModLoader build exited with code $exitCode"
    }
}

function Sync-ModSources([string]$SourcePath, [string]$DestinationPath) {
    Write-Info "Mirroring sources to $DestinationPath"

    $arguments = @(
        $SourcePath,
        $DestinationPath,
        "/MIR",
        "/NFL",
        "/NDL",
        "/XD", "bin", "obj", ".git", ".vs"
    )

    & robocopy @arguments | Out-Null
    $copyExit = $LASTEXITCODE

    if ($copyExit -ge 8) {
        throw "Robocopy failed with exit code $copyExit"
    }
}

function Locate-BuiltMod([string]$ModFileName) {
    $candidates = @()

    $documentsPath = [Environment]::GetFolderPath("MyDocuments")
    if (-not [string]::IsNullOrWhiteSpace($documentsPath)) {
        $candidates += Join-Path $documentsPath "My Games\Terraria\tModLoader\Mods\$ModFileName"
    }

    $wslProbe = & wsl.exe -e sh -c "if [ -f ~/.local/share/Terraria/tModLoader/Mods/$ModFileName ]; then wslpath -w ~/.local/share/Terraria/tModLoader/Mods/$ModFileName; fi" 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($wslProbe)) {
        $candidates += $wslProbe.Trim()
    }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw "Unable to locate built mod artifact. Checked:`n - " + ($candidates -join "`n - ")
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
$modSourcePath = (Resolve-Path (Join-Path $repoRoot "Mods\ScreenReaderMod")).Path
$modName = "ScreenReaderMod.tmod"

Write-Info "Repository root detected at $repoRoot"

$tmlPath = Resolve-TmlPath -RepoRoot $repoRoot
Write-Info "Using tModLoader installation at $tmlPath"

$dotnetPath = Resolve-DotNet -TmlPath $tmlPath
Write-Info "Using dotnet host at $dotnetPath"

Invoke-Build -DotNetExe $dotnetPath -TmlPath $tmlPath -ModSourcePath $modSourcePath

if (-not $SkipDeploy) {
    $artifact = Locate-BuiltMod -ModFileName $modName
    $repoModsDir = Join-Path $repoRoot "Mods"
    $repoArtifactPath = Join-Path $repoModsDir $modName
    Write-Info "Copying packaged mod from $artifact to $repoArtifactPath"
    Copy-Item -Path $artifact -Destination $repoArtifactPath -Force

    $modSourcesDestination = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "My Games\Terraria\tModLoader\ModSources\ScreenReaderMod"
    Sync-ModSources -SourcePath $modSourcePath -DestinationPath $modSourcesDestination

    Write-Success "Build artifact copied to repo Mods directory and ModSources mirrored."
} else {
    Write-Info "SkipDeploy flag specified; skipping artifact copy and ModSources sync."
}
