# Starts the optional Docker services (Postgres/Redis) for local dev, if
# Docker is available in any of these forms - skips silently otherwise, since
# SQLite + the in-process cache remain the zero-setup defaults and this step
# is never required. Called by dev.ps1 and start-dev.bat.
#
# Covers both ways Windows users run Docker:
#   1. Docker Desktop (or any Docker Engine) exposed directly on the Windows
#      PATH - just run `docker compose` normally, no WSL involved.
#   2. Docker Engine installed only inside a WSL distro (no Windows-side
#      `docker` command) - shell out via `wsl`. Does its own Windows-path ->
#      WSL-path translation instead of `wsl wslpath`, because passing a
#      backslash-containing Windows path as an argument to wsl.exe strips the
#      backslashes before it reaches wslpath.
# (macOS/Linux users run dev.sh instead, which calls `docker compose` natively
# - this script never runs there.)
param(
    [string]$Root = $PSScriptRoot
)

$prevEAP = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        Write-Host "Starting Docker services (Postgres/Redis)..."
        docker compose --project-directory "$Root" up -d 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  (skipped - Docker not available)"
        }
    }
    elseif (Get-Command wsl -ErrorAction SilentlyContinue) {
        $winRoot = $Root.TrimEnd('\')
        $drive = $winRoot.Substring(0, 1).ToLower()
        $rest = $winRoot.Substring(2) -replace '\\', '/'
        $wslRoot = "/mnt/$drive$rest"

        Write-Host "Starting Docker services (Postgres/Redis) via WSL..."
        wsl bash -lc "cd '$wslRoot' && docker compose up -d" 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  (skipped - Docker not available in WSL)"
        }
    }
} catch {
    Write-Host "  (skipped - could not reach Docker)"
} finally {
    $ErrorActionPreference = $prevEAP
}
