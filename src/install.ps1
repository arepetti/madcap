<#
.SYNOPSIS
  One-shot installer for the multi-agent debate system on Windows.

.DESCRIPTION
  - Installs Ollama via winget (if not already present)
  - Sets OLLAMA_HOST (bind to network) and OLLAMA_NUM_CTX user environment variables
  - Restarts Ollama if env vars changed (so the running server uses them)
  - Pulls required models with retry logic (llama3.1:8b and qwen2.5:7b)
  - Creates a Python venv and installs requirements
  - Sanity-checks persona files

.NOTES
  Requires: Windows 10/11 with winget, Python 3.10+ on PATH.
  Run from the directory containing debate.py.

  If execution policy blocks the script:
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>

[CmdletBinding()]
param(
    [string]$PythonExe = "python",
    [int]$NumCtx = 8192,
    [string[]]$Models = @("llama3.1:8b", "qwen2.5:7b"),
    [string]$OllamaHost = "0.0.0.0:11434",
    [int]$PullRetries = 3
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

function Wait-OllamaReady {
    param([int]$TimeoutSeconds = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:11434/api/tags" `
                                   -UseBasicParsing -TimeoutSec 2 `
                                   -ErrorAction Stop
            if ($r.StatusCode -eq 200) { return $true }
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }
    return $false
}

function Stop-OllamaProcesses {
    $procs = Get-Process -Name "ollama" -ErrorAction SilentlyContinue
    if (-not $procs) { return }
    foreach ($p in $procs) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { }
    }
    Start-Sleep -Seconds 2
}

function Start-OllamaServer {
    Start-Process -FilePath "ollama" -ArgumentList "serve" `
                  -WindowStyle Hidden -PassThru | Out-Null
}

# Preflight

Write-Step "Preflight checks"

if (-not (Test-Command "winget")) {
    throw "winget is not available. Install 'App Installer' from the Microsoft Store, then re-run."
}
Write-Ok "winget found"

if (-not (Test-Command $PythonExe)) {
    throw "Python ('$PythonExe') is not on PATH. Install Python 3.10+ from https://www.python.org/downloads/ and tick 'Add to PATH'."
}
$pyVersion = & $PythonExe --version 2>&1
Write-Ok "$pyVersion"

Write-Info "Checking connectivity to ollama.com..."
try {
    $resp = Invoke-WebRequest -Uri "https://ollama.com" -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
    if ($resp.StatusCode -eq 200) {
        Write-Ok "ollama.com is reachable"
    } else {
        Write-Warn "ollama.com returned HTTP $($resp.StatusCode) - model pulls may fail."
        Write-Warn "This might indicate the site is blocked by network policies."
    }
} catch {
    Write-Warn "Could not reach ollama.com: $($_.Exception.Message)"
    Write-Warn "Model pulls will likely fail. Check firewall/proxy settings."
}

# Install Ollama

Write-Step "Installing Ollama"

if (Test-Command "ollama") {
    Write-Ok "Ollama already installed: $((& ollama --version) 2>&1 | Select-Object -First 1)"
} else {
    Write-Info "Installing via winget..."
    winget install --id=Ollama.Ollama -e --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget install failed (exit $LASTEXITCODE). Install manually from https://ollama.com/download/windows"
    }
    $ollamaDir = Join-Path $env:LOCALAPPDATA "Programs\Ollama"
    if (Test-Path $ollamaDir) {
        $env:Path = "$ollamaDir;$env:Path"
    }
    if (-not (Test-Command "ollama")) {
        Write-Warn "Ollama installed but not yet on PATH for this session."
        Write-Warn "Close and reopen PowerShell, then re-run this script to continue."
        exit 0
    }
    Write-Ok "Ollama installed"
}

# Set OLLAMA_HOST

Write-Step "Configuring OLLAMA_HOST=$OllamaHost (user-scope)"

$existingHost = [Environment]::GetEnvironmentVariable("OLLAMA_HOST", "User")
$envChanged = $false

if ($existingHost -eq $OllamaHost) {
    Write-Ok "OLLAMA_HOST already set to $OllamaHost (user)"
} else {
    [Environment]::SetEnvironmentVariable("OLLAMA_HOST", $OllamaHost, "User")
    Write-Ok "OLLAMA_HOST set to $OllamaHost (user environment)"
    $envChanged = $true
}
$env:OLLAMA_HOST = $OllamaHost

# Set OLLAMA_NUM_CTX

Write-Step "Configuring OLLAMA_NUM_CTX=$NumCtx (user-scope)"

$existingCtx = [Environment]::GetEnvironmentVariable("OLLAMA_NUM_CTX", "User")

if ($existingCtx -eq "$NumCtx") {
    Write-Ok "OLLAMA_NUM_CTX already set to $NumCtx (user)"
} else {
    [Environment]::SetEnvironmentVariable("OLLAMA_NUM_CTX", "$NumCtx", "User")
    Write-Ok "OLLAMA_NUM_CTX set to $NumCtx (user environment)"
    $envChanged = $true
}
$env:OLLAMA_NUM_CTX = "$NumCtx"

# Start (or restart) Ollama

Write-Step "Starting Ollama service"

$running = $false
try {
    Write-Info "Pinging ollama server at 127.0.0.1:11434..."
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:11434/api/tags" `
                           -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
    if ($r.StatusCode -eq 200) { $running = $true }
} catch { }

if ($running -and $envChanged) {
    Write-Info "Restarting Ollama so it picks up the new environment variables..."
    Stop-OllamaProcesses
    Start-OllamaServer
    if (Wait-OllamaReady -TimeoutSeconds 60) {
        Write-Ok "Ollama restarted with updated configuration"
    } else {
        throw "Ollama did not come back up; start it manually and re-run."
    }
} elseif ($running) {
    Write-Ok "Ollama is already running"
} else {
    Write-Info "Launching 'ollama serve' in a detached process..."
    Start-OllamaServer
    if (Wait-OllamaReady -TimeoutSeconds 60) {
        Write-Ok "Ollama is up"
    } else {
        throw "Ollama did not become ready within 60 seconds. Check logs in %LOCALAPPDATA%\Ollama\."
    }
}

# Pull models

Write-Step "Pulling models (this may take a while; ~9 GB total)"

$localModels = @()
try {
    $tagsJson = Invoke-WebRequest -Uri "http://127.0.0.1:11434/api/tags" `
                                  -UseBasicParsing -TimeoutSec 5 `
        | Select-Object -ExpandProperty Content `
        | ConvertFrom-Json
    if ($tagsJson.models) {
        $localModels = $tagsJson.models | ForEach-Object { $_.name }
    }
} catch {
    Write-Warn "Could not list installed models; will attempt pull for all."
}

foreach ($m in $Models) {
    if ($localModels -contains $m) {
        Write-Ok "$m already present"
    } else {
        $pulled = $false
        for ($attempt = 1; $attempt -le $PullRetries; $attempt++) {
            Write-Info "Pulling $m (attempt $attempt/$PullRetries) ..."
            & ollama pull $m
            if ($LASTEXITCODE -eq 0) {
                $pulled = $true
                break
            }
            if ($attempt -lt $PullRetries) {
                Write-Warn "Pull failed (exit $LASTEXITCODE). Retrying in 5 seconds..."
                Start-Sleep -Seconds 5
            }
        }
        if (-not $pulled) {
            throw "Failed to pull '$m' after $PullRetries attempts."
        }
        Write-Ok "$m pulled"
    }
}

# Python venv

Write-Step "Setting up Python virtual environment"

$venvPath = Join-Path $PSScriptRoot ".venv"
if (Test-Path $venvPath) {
    Write-Ok ".venv already exists at $venvPath"
} else {
    Write-Info "Creating .venv at $venvPath"
    & $PythonExe -m venv $venvPath
    if ($LASTEXITCODE -ne 0) { throw "venv creation failed (exit $LASTEXITCODE)." }
    Write-Ok "venv created"
}

$venvPython = Join-Path $venvPath "Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
    throw "venv python not found at $venvPython"
}

# Install requirements

Write-Step "Installing Python requirements"

$reqFile = Join-Path $PSScriptRoot "requirements.txt"
if (-not (Test-Path $reqFile)) {
    Write-Warn "requirements.txt not found at $reqFile"
    Write-Warn "Skipping pip install. Create requirements.txt and run:"
    Write-Warn "  $venvPython -m pip install -r requirements.txt"
} else {
    & $venvPython -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed (exit $LASTEXITCODE)." }

    & $venvPython -m pip install -r $reqFile
    if ($LASTEXITCODE -ne 0) { throw "pip install -r requirements.txt failed (exit $LASTEXITCODE)." }
    Write-Ok "Python requirements installed"
}

# Preflight on personas

Write-Step "Verifying persona files"

$personaDir = Join-Path $PSScriptRoot "personas"
if (-not (Test-Path $personaDir)) {
    Write-Warn "personas\ directory not found at $personaDir"
    Write-Warn "The script won't run until you create the persona files."
} else {
    $required = @("default.answerer.txt", "default.critic.txt", "default.judge.txt")
    $missing = @()
    foreach ($f in $required) {
        if (-not (Test-Path (Join-Path $personaDir $f))) {
            $missing += $f
        }
    }
    if ($missing.Count -gt 0) {
        Write-Warn "Missing required persona files:"
        $missing | ForEach-Object { Write-Warn "  $_" }
    } else {
        Write-Ok "Default persona files present"
    }
}

# Done

Write-Step "Installation complete"

Write-Host ""
Write-Host "To run the debate:" -ForegroundColor White
Write-Host "  .\.venv\Scripts\Activate.ps1" -ForegroundColor Yellow
Write-Host "  python debate.py technical" -ForegroundColor Yellow
Write-Host ""
Write-Host "Or in one line without activating the venv:" -ForegroundColor White
Write-Host "  .\.venv\Scripts\python.exe debate.py technical" -ForegroundColor Yellow
Write-Host ""