Param()

$modName = "ScreenReaderMod.tmod"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Join-Path $scriptRoot ".."
$distPath = Join-Path $repoRoot "tModLoader_dist"
$modSourceRelative = "..\Mods\ScreenReaderMod"
$dotnetExe = $null

if (-not (Test-Path $distPath)) {
    Write-Host "ERROR: tModLoader_dist directory not found at $distPath" -ForegroundColor Red
    exit 1
}

[bool]$isWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)

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
$docModsPath = Join-Path $documentsPath "My Games\Terraria\tModLoader\Mods\$modName"
$candidatePaths = @()
if (Test-Path $docModsPath) {
    $candidatePaths += $docModsPath
}

$wslArtifact = & wsl.exe -e sh -c "if [ -f ~/.local/share/Terraria/tModLoader/Mods/$modName ]; then wslpath -w ~/.local/share/Terraria/tModLoader/Mods/$modName; fi"
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($wslArtifact)) {
    $candidatePaths += $wslArtifact.Trim()
}

$artifactPath = $null
foreach ($candidate in $candidatePaths) {
    if (Test-Path $candidate) {
        $artifactPath = $candidate
        break
    }
}

if (-not $artifactPath) {
    Write-Host "ERROR: Expected build artifact not found. Checked paths:" -ForegroundColor Red
    foreach ($candidate in @($docModsPath, $wslArtifact)) {
        Write-Host " - $candidate"
    }
    exit 1
}

$steamModsPath = Join-Path $repoRoot "Mods\$modName"
Write-Host "INFO: Copying $artifactPath to $steamModsPath"
Copy-Item -Path $artifactPath -Destination $steamModsPath -Force

Write-Host "SUCCESS: Terraria Access packaged to $steamModsPath" -ForegroundColor Green
