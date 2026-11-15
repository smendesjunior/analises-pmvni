@echo off
rem Roda sem janela: relança a si mesmo num console oculto (conhost --headless), com os mesmos argumentos, e fecha esta janela na hora.
rem Se o console oculto não puder partir, segue nesta mesma janela (a falha fica à vista).
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto %* && exit /b)
set "ARGS=%*"
if defined ARGS set "ARGS=%ARGS:*--oculto=%"
rem Abrir com explorer.exe (fora do pacote do Claude). Resultado em tests\saida\agentes-ajuda.txt. Sem chamada a modelo.
powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0agentes-ajuda.ps1" %ARGS%
exit /b %errorlevel%
