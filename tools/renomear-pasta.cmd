@echo off
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto & exit /b)
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0renomear-pasta.ps1"
