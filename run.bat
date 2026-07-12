@echo off
title EVO Mod Manager Launcher
echo Building...
cd /d "C:\Users\paul_\OneDrive\Documents\APP\EVO Mod Manager"
dotnet build -q
echo Running...
start /B "" "src\EVO.ModManager.App\bin\Debug\net9.0-windows\EVO.ModManager.App.exe"
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build or launch failed
    pause
    exit /b
)
echo App launched. Window should appear.
echo.
echo If you see a white screen:
echo 1. Check %LOCALAPPDATA%\EVO Mod Manager\logs\ for errors
echo 2. Make sure you have .NET 9 Desktop Runtime installed
echo 3. Try running the EXE directly from File Explorer:
echo    src\EVO.ModManager.App\bin\Debug\net9.0-windows\EVO.ModManager.App.exe
pause
