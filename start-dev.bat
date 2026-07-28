@echo off
REM Start backend and frontend in separate command windows
SET SCRIPT_DIR=%~dp0

REM Start optional Docker services (Postgres/Redis) via WSL, if available.
REM Skips silently if WSL/Docker isn't set up - SQLite + in-process cache
REM remain the zero-setup defaults.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%dev-docker.ps1" -Root "%SCRIPT_DIR%"

REM Start backend
start "Backend" /d "%SCRIPT_DIR%backend\src\TripPlanner.WebApi" cmd /k "dotnet run"

REM Start frontend: install dependencies if needed then run dev
start "Frontend" /d "%SCRIPT_DIR%frontend" cmd /k "npm install && npm run dev"

echo Launched backend and frontend. Close the windows to stop them.