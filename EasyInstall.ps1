# Easy installer for Screen Reader Mod assets.

$ErrorActionPreference = 'Stop'

function Get-RequiredPath {
    param (
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing $Description at '$Path'."
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

try {
    $scriptRoot = Split-Path -LiteralPath $PSCommandPath -Parent
    $sourceRoot = Join-Path -Path $scriptRoot -ChildPath 'Terraria Access'

    $dllSource = Get-RequiredPath -Path (Join-Path $sourceRoot 'nvdaControllerClient64.dll') -Description 'NVDA controller DLL'
    $tmodSource = Get-RequiredPath -Path (Join-Path $sourceRoot 'ScreenReaderMod.tmod') -Description 'ScreenReaderMod.tmod'
    $inputProfilesSource = Get-RequiredPath -Path (Join-Path $sourceRoot 'input profiles.json') -Description 'input profiles.json'
    $enabledSource = Get-RequiredPath -Path (Join-Path $sourceRoot 'enabled.json') -Description 'enabled.json'

    $steamBase = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\tModLoader'
    $steamPath = Get-RequiredPath -Path $steamBase -Description 'tModLoader installation'

    $documentsPath = [Environment]::GetFolderPath('MyDocuments')
    $tmodDocuments = Join-Path $documentsPath 'My Games\Terraria\tModLoader'
    $modsFolder = Join-Path $tmodDocuments 'Mods'

    New-Item -ItemType Directory -Force -Path $tmodDocuments, $modsFolder | Out-Null

    Copy-Item -LiteralPath $dllSource -Destination $steamPath -Force
    Copy-Item -LiteralPath $tmodSource -Destination $modsFolder -Force
    Copy-Item -LiteralPath $inputProfilesSource -Destination $tmodDocuments -Force
    Copy-Item -LiteralPath $enabledSource -Destination $tmodDocuments -Force

    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "Installation completed successfully.`n`nNVDA DLL -> $steamPath`n.tmod -> $modsFolder`nProfiles + enabled -> $tmodDocuments",
        'Easy Install',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    ) | Out-Null
}
catch {
    Write-Error $_
    try {
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.MessageBox]::Show(
            "Installation failed: $($_.Exception.Message)",
            'Easy Install',
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::Error
        ) | Out-Null
    }
    catch {
        # Swallow message box errors to avoid masking the original issue.
    }
    exit 1
}
