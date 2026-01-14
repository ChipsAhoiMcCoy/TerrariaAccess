<#
.SYNOPSIS
    Run unit tests for the ScreenReaderMod project.

.DESCRIPTION
    Executes the xUnit test suite for the Terraria Access mod.
    Tests run without requiring Terraria or tModLoader to be installed.

.PARAMETER Filter
    Optional filter to run specific tests. Uses dotnet test --filter syntax.
    Example: -Filter "SpatialAudioPanner" runs only tests with that name.

.PARAMETER Coverage
    When specified, collects code coverage data.

.PARAMETER Verbose
    When specified, shows detailed test output.

.EXAMPLE
    .\test.ps1
    Runs all tests.

.EXAMPLE
    .\test.ps1 -Filter "CoinFormatter"
    Runs only CoinFormatter tests.

.EXAMPLE
    .\test.ps1 -Coverage
    Runs all tests with code coverage collection.

.EXAMPLE
    .\test.ps1 -Verbose
    Runs all tests with detailed output.
#>

param(
    [string]$Filter,
    [switch]$Coverage,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$testProject = Join-Path $repoRoot "Tests\ScreenReaderMod.Tests"

Write-Host "Running tests from: $testProject" -ForegroundColor Cyan
Write-Host ""

$args = @("test", $testProject)

if ($Filter) {
    $args += "--filter", $Filter
    Write-Host "Filter: $Filter" -ForegroundColor Yellow
}

if ($Coverage) {
    $args += '--collect:"XPlat Code Coverage"'
    Write-Host "Coverage: Enabled" -ForegroundColor Yellow
}

if ($Verbose) {
    $args += '--logger', 'console;verbosity=detailed'
    Write-Host "Verbose: Enabled" -ForegroundColor Yellow
}

Write-Host ""

& dotnet @args

$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host ""
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Some tests failed." -ForegroundColor Red
}

exit $exitCode
