@echo off
rem Roda sem janela: relança a si mesmo num console oculto (conhost --headless) e fecha esta janela na hora.
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto & exit /b)
rem Aberto via explorer.exe (fora do pacote do Claude) pelo build-release.ps1.
powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0limpar-extracoes.ps1"
