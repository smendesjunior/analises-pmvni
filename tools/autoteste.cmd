@echo off
rem Abrir com explorer.exe (fora do pacote do Claude). Resultado em tests\saida\autoteste*.json.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0autoteste.ps1"
