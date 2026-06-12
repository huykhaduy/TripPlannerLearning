@echo off
REM Start backend and frontend in separate command windows
SET SCRIPT_DIR=%~dp0

REM Start backend
start "Backend" /d "%SCRIPT_DIR%backend\src\TripPlanner.WebApi" cmd /k "dotnet run"

REM Start frontend: install dependencies if needed then run dev
start "Frontend" /d "%SCRIPT_DIR%frontend" cmd /k "npm install && npm run dev"

echo Launched backend and frontend. Close the windows to stop them.