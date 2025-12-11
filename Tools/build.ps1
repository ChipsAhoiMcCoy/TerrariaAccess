Param(
    [switch]$SkipDeploy,
    [switch]$NarrationLint,
    [string]$ClientLogPath
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

function Resolve-ClientLogPath([string]$TmlPath, [string]$OverridePath) {
    if (-not [string]::IsNullOrWhiteSpace($OverridePath)) {
        if (-not (Test-Path $OverridePath)) {
            throw "Client log override $OverridePath does not exist."
        }

        return (Resolve-Path $OverridePath).Path
    }

    $candidates = New-Object System.Collections.Generic.List[string]

    if (-not [string]::IsNullOrWhiteSpace($TmlPath)) {
        $candidates.Add((Join-Path $TmlPath "tModLoader-Logs\client.log"))
        $candidates.Add((Join-Path $TmlPath "Logs\client.log"))

        $tmlLogs = Join-Path $TmlPath "tModLoader-Logs"
        if (Test-Path $tmlLogs) {
            $logFiles = Get-ChildItem -Path $tmlLogs -Filter client.log -Recurse -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
            foreach ($log in $logFiles) {
                $candidates.Add($log)
            }
        }
    }

    $documentsPath = [Environment]::GetFolderPath("MyDocuments")
    if (-not [string]::IsNullOrWhiteSpace($documentsPath)) {
        $candidates.Add((Join-Path $documentsPath "My Games\Terraria\tModLoader-Logs\client.log"))
        $candidates.Add((Join-Path $documentsPath "My Games\Terraria\tModLoader\Logs\client.log"))
    }

    $wslProbe = & wsl.exe -e sh -c 'for p in ~/.local/share/Terraria/tModLoader-Logs/client.log ~/.local/share/Terraria/ModLoader/Logs/client.log; do if [ -f "$p" ]; then wslpath -w "$p"; fi; done | head -n1' 2>$null
    $wslExit = $LASTEXITCODE
    if ($wslExit -eq 0 -and -not [string]::IsNullOrWhiteSpace($wslProbe)) {
        $candidates.Add($wslProbe.Trim())
    }

    $existing = @(
        $candidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path $_) } |
        Sort-Object -Unique |
        ForEach-Object { Get-Item $_ }
    )

    if ($existing.Count -eq 0) {
        throw "Unable to locate client.log. Checked:`n - " + ($candidates -join "`n - ")
    }

    return ($existing | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}

function Invoke-NarrationLint([string]$LogPath) {
    if (-not (Test-Path $LogPath)) {
        throw "Client log not found at $LogPath"
    }

    Write-Info "Running narration lint against $LogPath"

    $rules = @(
        @{
            Name = "NvdaFailure"
            Pattern = "\[NVDA\].*(failed|unable|disable|not running|returned code|error)"
            Severity = "Error"
            Description = "NVDA reported a failure or was disabled."
        },
        @{
            Name = "MenuNarrationGap"
            Pattern = "\[MenuNarration\].*(Missing|missing|returned empty)"
            Severity = "Warning"
            Description = "Menu narration reported a missing label or empty menu list."
        },
        @{
            Name = "SpeechSuppressed"
            Pattern = "\[Diagnostics\]\[Speech\].*suppressed"
            Severity = "Warning"
            Description = "Speech requests were suppressed."
        }
    )

    $issues = New-Object System.Collections.Generic.List[psobject]
    $logLines = Get-Content -Path $LogPath -ErrorAction Stop

    foreach ($rule in $rules) {
        $matches = $logLines | Select-String -Pattern $rule.Pattern -CaseSensitive:$false
        foreach ($match in $matches) {
            $issues.Add([PSCustomObject]@{
                Rule = $rule.Name
                Severity = $rule.Severity
                Description = $rule.Description
                Line = $match.Line.Trim()
            })
        }
    }

    if ($issues.Count -eq 0) {
        $lastWrite = (Get-Item $LogPath).LastWriteTime
        Write-Success "Narration lint passed. No NVDA failures or menu narration gaps found in $(Split-Path $LogPath -Leaf) (updated $lastWrite)."
        return
    }

    $errors = @($issues | Where-Object { $_.Severity -eq "Error" })
    $warnings = @($issues | Where-Object { $_.Severity -eq "Warning" })

    foreach ($issue in $issues) {
        if ($issue.Severity -eq "Error") {
            Write-Failure "$($issue.Rule): $($issue.Description) -> $($issue.Line)"
        } else {
            Write-Info "WARN [$($issue.Rule)]: $($issue.Description) -> $($issue.Line)"
        }
    }

    if ($errors.Count -gt 0) {
        throw "Narration lint detected $($errors.Count) error(s) and $($warnings.Count) warning(s). See $LogPath."
    }

    Write-Info "Narration lint completed with $($warnings.Count) warning(s); see $LogPath for details."
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

if ($NarrationLint) {
    try {
        $logPath = Resolve-ClientLogPath -TmlPath $tmlPath -OverridePath $ClientLogPath
        Invoke-NarrationLint -LogPath $logPath
    }
    catch {
        Write-Failure $_
        exit 1
    }
}
