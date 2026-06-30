<#
.SYNOPSIS
  One-shot setup for the multi-agent debate system (.NET / Foundry Local) on Windows.

.DESCRIPTION
  - Verifies a .NET 10 SDK is available (offers to install it via winget if not).
  - Installs Microsoft Foundry Local via winget (if not already present).
  - Restores and builds the solution.
  - Verifies the default persona files are present.
  - Prompts to download the configured Foundry Local models now (so the first
    real run is fast). Model aliases are read from
    src/Debate.Cli/appsettings.json so they stay in sync with the app.
  - Prompts to warm up the hardware execution providers (e.g. TensorRT-RTX, CUDA)
    by running the app's '--prefetch' mode. EP binaries are large and downloaded
    once, then cached and reused on every subsequent run - just like models.

.PARAMETER Profile
  Model profile to prepare (e.g. 'small' or 'normal'). Defaults to the active
  profile configured in appsettings.json (Debate:FoundryLocal:Profile).

.PARAMETER Models
  Override the model aliases to download. Defaults to the three models of the
  selected profile in appsettings.json (Answerer, Critic, Judge).

.PARAMETER SkipBuild
  Skip the dotnet restore/build step.

.PARAMETER SkipPrefetch
  Skip the execution-provider / model warm-up step.

.PARAMETER Yes
  Assume "yes" for prompts (download models, install SDK) without asking.
  Useful for unattended setup.

.NOTES
  Requires: Windows 10/11 with winget.
  Run from anywhere:
    powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
#>

[CmdletBinding()]
param(
    [string]$Profile,
    [string[]]$Models,
    [switch]$SkipBuild,
    [switch]$SkipPrefetch,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$m) Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Info { param([string]$m) Write-Host "    $m" -ForegroundColor Gray }
function Write-Ok   { param([string]$m) Write-Host "    $m" -ForegroundColor Green }
function Write-Warn { param([string]$m) Write-Host "    $m" -ForegroundColor Yellow }

function Test-Command {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Confirm-Yes {
    param([string]$Question, [bool]$Default = $true)
    if ($Yes) { return $true }
    $suffix = if ($Default) { "[Y/n]" } else { "[y/N]" }
    $answer = Read-Host "    $Question $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer -match '^(y|yes)$'
}

# Refresh PATH from the machine + user environment so tools installed by winget
# in this session become visible without reopening the shell.
function Update-SessionPath {
    $machine = [Environment]::GetEnvironmentVariable("Path", "Machine")
    $user = [Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = @($machine, $user | Where-Object { $_ }) -join ";"
}

# Resolve repo paths (this script lives in <repo>/scripts).
$RepoRoot = Split-Path $PSScriptRoot -Parent
$SrcDir = Join-Path $RepoRoot "src"
$Solution = Join-Path $SrcDir "Debate.slnx"
$CliProject = Join-Path $SrcDir "Debate.Cli\Debate.Cli.csproj"
$AppSettings = Join-Path $SrcDir "Debate.Cli\appsettings.json"
$PersonaDir = Join-Path $SrcDir "personas"

# Preflight

Write-Step "Preflight checks"

if (-not (Test-Command "winget")) {
    throw "winget is not available. Install 'App Installer' from the Microsoft Store, then re-run."
}
Write-Ok "winget found"

# .NET SDK (need a major version >= 10)

Write-Step "Checking .NET SDK"

function Get-DotnetMajors {
    if (-not (Test-Command "dotnet")) { return @() }
    $majors = @()
    foreach ($line in (& dotnet --list-sdks 2>$null)) {
        if ($line -match '^(\d+)\.') { $majors += [int]$Matches[1] }
    }
    return $majors
}

$majors = Get-DotnetMajors
if (($majors | Where-Object { $_ -ge 10 }).Count -gt 0) {
    Write-Ok ".NET SDK present: $((& dotnet --version) 2>&1)"
} else {
    Write-Warn "No .NET 10 SDK found on PATH."
    if (Confirm-Yes "Install the .NET 10 SDK via winget now?") {
        winget install --id Microsoft.DotNet.SDK.10 -e --accept-package-agreements --accept-source-agreements
        if ($LASTEXITCODE -ne 0) {
            throw "winget install of the .NET 10 SDK failed (exit $LASTEXITCODE). Install manually from https://dotnet.microsoft.com/download"
        }
        Update-SessionPath
        $majors = Get-DotnetMajors
        if (($majors | Where-Object { $_ -ge 10 }).Count -eq 0) {
            Write-Warn ".NET SDK installed but not yet on PATH for this session."
            Write-Warn "Close and reopen PowerShell, then re-run this script."
            exit 0
        }
        Write-Ok ".NET SDK installed: $((& dotnet --version) 2>&1)"
    } else {
        throw "A .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download and re-run."
    }
}

# Install Foundry Local

Write-Step "Installing Microsoft Foundry Local"

if (Test-Command "foundry") {
    Write-Ok "Foundry Local already installed: $((& foundry --version) 2>&1 | Select-Object -First 1)"
} else {
    Write-Info "Installing via winget (Microsoft.FoundryLocal)..."
    winget install --id Microsoft.FoundryLocal -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget install of Foundry Local failed (exit $LASTEXITCODE). See https://learn.microsoft.com/azure/foundry-local/get-started"
    }
    Update-SessionPath
    if (-not (Test-Command "foundry")) {
        Write-Warn "Foundry Local installed but not yet on PATH for this session."
        Write-Warn "Close and reopen PowerShell, then re-run this script to download models."
        exit 0
    }
    Write-Ok "Foundry Local installed"
}

# Build the solution

if ($SkipBuild) {
    Write-Step "Skipping build (-SkipBuild)"
} else {
    Write-Step "Restoring and building the solution"
    if (-not (Test-Path $Solution)) {
        throw "Solution not found at $Solution"
    }
    & dotnet build $Solution -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
    Write-Ok "Build succeeded"
}

# Verify persona files

Write-Step "Verifying persona files"

if (-not (Test-Path $PersonaDir)) {
    Write-Warn "personas\ directory not found at $PersonaDir"
} else {
    $required = @(
        "default.answerer.txt", "default.critic.txt",
        "default.judge-rephraser.txt", "default.judge-restater.txt",
        "default.judge-arbiter.txt", "default.judge-profiler.txt")
    $missing = @($required | Where-Object { -not (Test-Path (Join-Path $PersonaDir $_)) })
    if ($missing.Count -gt 0) {
        Write-Warn "Missing required persona files:"
        $missing | ForEach-Object { Write-Warn "  $_" }
    } else {
        Write-Ok "Default persona files present"
    }
}

# Determine which models to download. The model lineups are defined in exactly one
# place - appsettings.json, under named profiles - so read the selected profile from
# there (unless overridden via -Models). The chosen profile is also passed to the
# prefetch step so both prepare the same lineup.

Write-Step "Resolving model list"

# Profile actually used for prefetch (resolved from config when not passed explicitly).
$ResolvedProfile = $Profile

if (-not $Models -or $Models.Count -eq 0) {
    if (Test-Path $AppSettings) {
        try {
            $cfg = Get-Content $AppSettings -Raw | ConvertFrom-Json
            if (-not $ResolvedProfile) {
                $ResolvedProfile = $cfg.Debate.FoundryLocal.Profile
            }
            if (-not $ResolvedProfile) { $ResolvedProfile = "small" }

            $m = $cfg.Debate.FoundryLocal.Profiles.$ResolvedProfile
            if (-not $m) {
                $available = ($cfg.Debate.FoundryLocal.Profiles.PSObject.Properties.Name) -join ', '
                Write-Warn "Profile '$ResolvedProfile' not found in appsettings.json. Available: $available"
            } else {
                $Models = @($m.Answerer, $m.Critic, $m.Judge) |
                    Where-Object { $_ } |
                    Select-Object -Unique
                Write-Ok "Profile '$ResolvedProfile' models from appsettings.json: $($Models -join ', ')"
            }
        } catch {
            Write-Warn "Could not parse $AppSettings ($($_.Exception.Message))."
        }
    } else {
        Write-Warn "appsettings.json not found at $AppSettings."
    }
}

# Download models now (optional)

Write-Step "Downloading models"

if (-not $Models -or $Models.Count -eq 0) {
    Write-Warn "No models could be determined from configuration; skipping download."
    Write-Warn "Set Debate:FoundryLocal:Profiles in $AppSettings (or pass -Models) and re-run."
} else {
    Write-Info "The configured models are downloaded once and cached for reuse;"
    Write-Info "doing it now means the first real run won't have to wait."
    Write-Host ""

    if (Confirm-Yes "Download the models now? ($($Models -join ', '))") {
        foreach ($model in $Models) {
            Write-Info "Downloading '$model' (foundry model download)..."
            & foundry model download $model
            if ($LASTEXITCODE -ne 0) {
                Write-Warn "Download of '$model' reported exit $LASTEXITCODE."
                Write-Warn "Check the alias against 'foundry model list'. The app will retry at runtime."
            } else {
                Write-Ok "'$model' ready"
            }
        }
    } else {
        Write-Info "Skipped. Models will download automatically on first run."
    }
}

# Warm up execution providers (and load models) via the app's --prefetch mode.
# There is no `foundry` CLI command for EPs; they are downloaded and registered
# through the SDK, so we run the app once in prefetch mode. This uses the exact
# same configuration (AppName, cache dir, EP set) the real run uses, so the
# cached EP binaries are reused afterwards - just like the model cache.

Write-Step "Warming up execution providers"

if ($SkipPrefetch) {
    Write-Info "Skipping prefetch (-SkipPrefetch)."
} elseif (-not (Test-Path $CliProject)) {
    Write-Warn "CLI project not found at $CliProject; skipping prefetch."
} else {
    Write-Info "Execution providers (e.g. TensorRT-RTX, CUDA) are large and downloaded"
    Write-Info "once, then cached and reused. Warming up now means the first real run"
    Write-Info "won't have to wait. This also loads the models to verify the setup."
    Write-Host ""

    if (Confirm-Yes "Warm up execution providers and models now?") {
        $runArgs = @("run", "--project", $CliProject, "-c", "Release")
        if (-not $SkipBuild) { $runArgs += "--no-build" }
        $runArgs += @("--", "--prefetch")
        if ($ResolvedProfile) { $runArgs += @("--profile", $ResolvedProfile) }

        & dotnet @runArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Warn "Prefetch reported exit $LASTEXITCODE. The app will retry at runtime."
        } else {
            Write-Ok "Execution providers and models are cached"
        }
    } else {
        Write-Info "Skipped. Execution providers will download automatically on first run."
    }
}

# Done

Write-Step "Setup complete"

Write-Host ""
Write-Host "To run the debate:" -ForegroundColor White
Write-Host "  cd src" -ForegroundColor Yellow
Write-Host "  dotnet run --project Debate.Cli" -ForegroundColor Yellow
Write-Host ""
Write-Host "To use a remote (OpenAI-compatible) backend instead of local models:" -ForegroundColor White
Write-Host '  $env:DEBATE_API_KEY = "sk-..."' -ForegroundColor Yellow
Write-Host "  dotnet run --project Debate.Cli -- --provider Remote" -ForegroundColor Yellow
Write-Host ""
