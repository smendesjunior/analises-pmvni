@echo off
rem Abrir com explorer.exe (fora do pacote do Claude). Resultado em tests\saida\agentes-ajuda.txt. Sem chamada a modelo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0agentes-ajuda.ps1"
