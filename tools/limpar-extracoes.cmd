@echo off
rem Roda sem janela: relança a si mesmo num console oculto (conhost --headless), com os mesmos argumentos, e fecha esta janela na hora.
rem Se o console oculto não puder partir, segue nesta mesma janela (a falha fica à vista).
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto %* && exit /b)
set "ARGS=%*"
if defined ARGS set "ARGS=%ARGS:*--oculto=%"
rem Aberto via explorer.exe (fora do pacote do Claude) pelo build-release.ps1.
powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0limpar-extracoes.ps1" %ARGS%
exit /b %errorlevel%
