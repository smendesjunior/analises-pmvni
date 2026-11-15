@echo off
rem Abra este arquivo com o explorer.exe (fora do pacote do Claude): roda o roteiro da tela e grava docs\roteiro-etapa-2.resultado.json.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0roteiro.ps1"
