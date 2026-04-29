<#
.SYNOPSIS
    Launches tModLoader with TerrariaAccess debug logging enabled.

.DESCRIPTION
    This script is meant to be shared with testers. It discovers Steam library
    folders, finds tModLoader, sets debug environment variables, and launches
    tModLoader through its bundled dotnet runtime.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Optional manual override. Leave blank for automatic Steam library discovery.
$TModLoaderPath = ""

function Add-CandidatePath {
    param(
        [System.Collections.Generic.List[string]] $List,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($Path.Trim())
    if ($List.Contains($expanded)) {
        return
    }

    $List.Add($expanded)
}

function Get-SteamRootCandidates {
    $roots = [System.Collections.Generic.List[string]]::new()

    $registryKeys = @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'
    )

    foreach ($key in $registryKeys) {
        try {
            $props = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
            Add-CandidatePath $roots $props.SteamPath
            Add-CandidatePath $roots $props.InstallPath
        }
        catch {
            # Registry key is optional.
        }
    }

    Add-CandidatePath $roots 'C:\Program Files (x86)\Steam'
    Add-CandidatePath $roots 'C:\Program Files\Steam'
    Add-CandidatePath $roots 'D:\Steam'
    Add-CandidatePath $roots 'D:\SteamLibrary'

    return $roots
}

function Get-SteamLibraryCandidates {
    $libraries = [System.Collections.Generic.List[string]]::new()

    foreach ($root in Get-SteamRootCandidates) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        Add-CandidatePath $libraries $root

        $libraryFile = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $libraryFile)) {
            continue
        }

        $content = Get-Content -LiteralPath $libraryFile -Raw
        $matches = [regex]::Matches($content, '"path"\s*"([^"]+)"')
        foreach ($match in $matches) {
            $path = $match.Groups[1].Value -replace '\\\\', '\'
            Add-CandidatePath $libraries $path
        }
    }

    return $libraries
}

function Test-TModLoaderPath {
    param([string] $Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $false
    }

    $dll = Join-Path $Path 'tModLoader.dll'
    $dotnet = Join-Path $Path 'dotnet\dotnet.exe'
    return (Test-Path -LiteralPath $dll) -and (Test-Path -LiteralPath $dotnet)
}

function Find-TModLoaderPath {
    if (Test-TModLoaderPath $TModLoaderPath) {
        return (Resolve-Path -LiteralPath $TModLoaderPath).Path
    }

    $candidates = [System.Collections.Generic.List[string]]::new()

    if (-not [string]::IsNullOrWhiteSpace($env:TMODLOADER_PATH)) {
        Add-CandidatePath $candidates $env:TMODLOADER_PATH
    }

    foreach ($library in Get-SteamLibraryCandidates) {
        Add-CandidatePath $candidates (Join-Path $library 'steamapps\common\tModLoader')
    }

    $commonPaths = @(
        'C:\Program Files (x86)\Steam\steamapps\common\tModLoader',
        'C:\Program Files\Steam\steamapps\common\tModLoader',
        'D:\Steam\steamapps\common\tModLoader',
        'D:\SteamLibrary\steamapps\common\tModLoader'
    )

    foreach ($path in $commonPaths) {
        Add-CandidatePath $candidates $path
    }

    foreach ($candidate in $candidates) {
        if (Test-TModLoaderPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

$resolvedTModLoaderPath = Find-TModLoaderPath
if (-not $resolvedTModLoaderPath) {
    Write-Host ''
    Write-Host 'Could not find tModLoader.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Open this script in Notepad and set $TModLoaderPath near the top.'
    Write-Host 'Example:'
    Write-Host '  $TModLoaderPath = "D:\SteamLibrary\steamapps\common\tModLoader"'
    Write-Host ''
    exit 1
}

$dotnetPath = Join-Path $resolvedTModLoaderPath 'dotnet\dotnet.exe'
$tModLoaderDll = Join-Path $resolvedTModLoaderPath 'tModLoader.dll'

$env:SRM_DEBUG_INPUT = '1'
$env:SRM_DEBUG_HOTBAR = '1'

Write-Host '========================================' -ForegroundColor Cyan
Write-Host 'TerrariaAccess Debug Launch' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Found tModLoader:' -ForegroundColor Green
Write-Host "  $resolvedTModLoaderPath"
Write-Host ''
Write-Host 'Debug logging enabled for:' -ForegroundColor Green
Write-Host '  - Input state'
Write-Host '  - Inventory focus tracking'
Write-Host '  - Hotbar narration'
Write-Host ''
Write-Host 'After reproducing the issue, send client.log from tModLoader-Logs or Documents\My Games\Terraria\tModLoader\Logs.'
Write-Host ''
Write-Host 'Launching tModLoader...' -ForegroundColor Cyan
Write-Host ''

Push-Location $resolvedTModLoaderPath
try {
    & $dotnetPath $tModLoaderDll
}
finally {
    Pop-Location
}
