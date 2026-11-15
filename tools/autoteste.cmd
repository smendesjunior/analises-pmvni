@echo off
rem Roda sem janela: relança a si mesmo num console oculto (conhost --headless) e fecha esta janela na hora.
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto & exit /b)
rem Abrir com explorer.exe (fora do pacote do Claude). Resultado em tests\saida\autoteste*.json.
powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0autoteste.ps1"
