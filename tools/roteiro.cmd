@echo off
rem Roda sem janela: relança a si mesmo num console oculto (conhost --headless) e fecha esta janela na hora.
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto & exit /b)
rem Abra este arquivo com o explorer.exe (fora do pacote do Claude): roda o roteiro da tela e grava docs\roteiro-etapa-2.resultado.json.
powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0roteiro.ps1"
