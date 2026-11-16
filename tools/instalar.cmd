@echo off
rem Abra com o explorer.exe (fora do pacote do Claude): atalhos, associação de .rpmvni, kit atualizado e leis do kit anexadas.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1" %*
exit /b %errorlevel%
