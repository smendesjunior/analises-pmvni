@echo off
rem Aberto via explorer.exe (fora do pacote do Claude) pelo build-release.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0limpar-extracoes.ps1"
