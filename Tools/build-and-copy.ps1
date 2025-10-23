Param()

$modName = "ScreenReaderMod.tmod"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Join-Path $scriptRoot ".."
$defaultDistPath = Join-Path $repoRoot "tModLoader_dist"
$modSourceRelative = "..\Mods\ScreenReaderMod"
$dotnetExe = $null

[bool]$isWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)

$steamToolsPath = $null
if ($isWindows) {
    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $candidateSteam = Join-Path $programFilesX86 "Steam\steamapps\common\tModLoader"
        if (Test-Path $candidateSteam) {
            $steamToolsPath = $candidateSteam
            Write-Host "INFO: Found Steam tModLoader tools at $steamToolsPath"
        }
    }
} else {
    $candidateSteam = "/steam/steamapps/common/tModLoader"
    if (-not (Test-Path $candidateSteam)) {
        $candidateSteam = "/mnt/c/Program Files (x86)/Steam/steamapps/common/tModLoader"
    }
    if (Test-Path $candidateSteam) {
        $steamToolsPath = $candidateSteam
        Write-Host "INFO: Found Steam tModLoader tools at $steamToolsPath"
    }
}

$distPathCandidates = @($defaultDistPath)
if ($steamToolsPath) {
    $distPathCandidates += $steamToolsPath
}

$distPath = $null
foreach ($candidate in $distPathCandidates) {
    if (Test-Path (Join-Path $candidate "tModLoader.dll")) {
        $distPath = $candidate
        break
    }
}

if (-not $distPath) {
    Write-Host "ERROR: Unable to locate tModLoader build tools. Checked paths:" -ForegroundColor Red
    foreach ($candidate in $distPathCandidates) {
        Write-Host " - $candidate"
    }
    exit 1
}

if (-not ([string]::Equals($distPath, $defaultDistPath, [System.StringComparison]::OrdinalIgnoreCase))) {
    Write-Host "INFO: Using external tModLoader tools at $distPath"
}

if ($isWindows) {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $dotnetCommand) {
        $dotnetExe = $dotnetCommand.Path
    } else {
        $candidateRoots = @(
            [Environment]::GetEnvironmentVariable("DOTNET_ROOT"),
            [Environment]::GetEnvironmentVariable("ProgramFiles"),
            [Environment]::GetEnvironmentVariable("ProgramFiles(x86)"),
            [Environment]::GetEnvironmentVariable("LOCALAPPDATA"),
            [Environment]::GetEnvironmentVariable("USERPROFILE")
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $candidatePaths = foreach ($root in $candidateRoots) {
            @(
                Join-Path $root "dotnet\dotnet.exe",
                Join-Path $root "Microsoft\dotnet\dotnet.exe",
                Join-Path $root ".dotnet\dotnet.exe"
            )
        }
        foreach ($candidate in $candidatePaths) {
            if (Test-Path $candidate) {
                $dotnetExe = $candidate
                break
            }
        }
    }
    if (-not $dotnetExe) {
        $dotnetCandidates = @()
        $distDotnet = Join-Path $distPath "dotnet\dotnet.exe"
        if (Test-Path $distDotnet) {
            $dotnetCandidates += $distDotnet
        }
        if ($steamToolsPath) {
            $steamDotnet = Join-Path $steamToolsPath "dotnet\dotnet.exe"
            if (Test-Path $steamDotnet) {
                $dotnetCandidates += $steamDotnet
            }
        }
        foreach ($candidate in $dotnetCandidates) {
            if (Test-Path $candidate) {
                $dotnetExe = $candidate
                break
            }
        }
    }
} else {
    $candidate = Join-Path $distPath "dotnet_wsl\dotnet"
    if (Test-Path $candidate) {
        $dotnetExe = $candidate
    }
}

if (-not $dotnetExe) {
    if ($isWindows) {
        Write-Host "ERROR: Unable to locate dotnet.exe. Install the .NET runtime or add dotnet to PATH." -ForegroundColor Red
    } else {
        Write-Host "ERROR: dotnet runtime for WSL not found at $candidate" -ForegroundColor Red
    }
    exit 1
}

Write-Host "INFO: Building Terraria Access via: `"$dotnetExe`" tModLoader.dll -build `"$modSourceRelative`""

Push-Location $distPath
try {
    & $dotnetExe "tModLoader.dll" "-build" $modSourceRelative
    $exitCode = if ($global:LASTEXITCODE -eq $null) { 0 } else { $global:LASTEXITCODE }
    if ($exitCode -ne 0) {
        Write-Host "ERROR: Build failed with exit code $exitCode" -ForegroundColor Red
        exit $exitCode
    }
}
finally {
    Pop-Location
}

$documentsPath = [Environment]::GetFolderPath("MyDocuments")
$docModsDirectory = Join-Path $documentsPath "My Games\Terraria\tModLoader\Mods"
$targetModPath = Join-Path $docModsDirectory $modName

$candidatePaths = @()
if (Test-Path $targetModPath) {
    $candidatePaths += $targetModPath
}

$repoModsPath = Join-Path $repoRoot "Mods\$modName"
if (Test-Path $repoModsPath) {
    $candidatePaths += $repoModsPath
}

$distModsPath = Join-Path $distPath "Mods\$modName"
if (Test-Path $distModsPath) {
    $candidatePaths += $distModsPath
}

$wslArtifact = & wsl.exe -e sh -c "if [ -f ~/.local/share/Terraria/tModLoader/Mods/$modName ]; then wslpath -w ~/.local/share/Terraria/tModLoader/Mods/$modName; fi"
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($wslArtifact)) {
    $candidatePaths += $wslArtifact.Trim()
}

$artifactPath = $null
foreach ($candidate in ($candidatePaths | Select-Object -Unique)) {
    if (Test-Path $candidate) {
        $artifactPath = $candidate
        break
    }
}

if (-not $artifactPath) {
    Write-Host "ERROR: Expected build artifact not found. Checked paths:" -ForegroundColor Red
    foreach ($candidate in $candidatePaths) {
        Write-Host " - $candidate"
    }
    exit 1
}

if (-not (Test-Path $docModsDirectory)) {
    Write-Host "INFO: Creating Mods directory at $docModsDirectory"
    New-Item -ItemType Directory -Path $docModsDirectory | Out-Null
}

if (-not ([string]::Equals($artifactPath, $targetModPath, [System.StringComparison]::OrdinalIgnoreCase))) {
    Write-Host "INFO: Copying $artifactPath to $targetModPath"
    Copy-Item -Path $artifactPath -Destination $targetModPath -Force
} else {
    Write-Host "INFO: Build artifact already resides at $targetModPath"
}

$docModSourcesDirectory = Join-Path $documentsPath "My Games\Terraria\tModLoader\ModSources"
if (-not (Test-Path $docModSourcesDirectory)) {
    Write-Host "INFO: Creating ModSources directory at $docModSourcesDirectory"
    New-Item -ItemType Directory -Path $docModSourcesDirectory | Out-Null
}

$sourceDirectory = Join-Path $repoRoot "Mods\ScreenReaderMod"
if (-not (Test-Path $sourceDirectory)) {
    Write-Host "ERROR: Source directory not found at $sourceDirectory" -ForegroundColor Red
    exit 1
}

$targetSourceDirectory = Join-Path $docModSourcesDirectory "ScreenReaderMod"
if (Test-Path $targetSourceDirectory) {
    Write-Host "INFO: Clearing existing mod sources at $targetSourceDirectory"
    Remove-Item -Path $targetSourceDirectory -Recurse -Force
}

Write-Host "INFO: Copying mod sources to $targetSourceDirectory"
Copy-Item -Path $sourceDirectory -Destination $targetSourceDirectory -Recurse -Force

Write-Host "SUCCESS: Terraria Access packaged to $targetModPath and sources synced to $targetSourceDirectory" -ForegroundColor Green
