# Starts the backend API and the frontend dev server together (Windows / PowerShell).
# Each runs in its own window; close a window to stop that server.
#
#   ./dev.ps1
#
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

# 1. Frontend dependencies — install on first run.
if (-not (Test-Path "$Root/frontend/node_modules")) {
    Write-Host "Installing frontend dependencies..."
    Push-Location "$Root/frontend"; npm install; Pop-Location
}
if (-not (Test-Path "$Root/frontend/.env")) {
    Copy-Item "$Root/frontend/.env.example" "$Root/frontend/.env"
}

# 2. Start each server in a new PowerShell window.
Write-Host "Starting backend API  -> http://localhost:5080/swagger"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$Root/backend'; dotnet run --project src/TripPlanner.WebApi"

Write-Host "Starting frontend     -> http://localhost:5173"
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$Root/frontend'; npm run dev"
