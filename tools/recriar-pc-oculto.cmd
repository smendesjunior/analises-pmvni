@echo off
rem Abrir com explorer.exe: roda o "Capturar agora" e depois o "Conferir" do Recriar PC sem janela (console oculto).
rem Ao terminar grava tests\saida\recriar-pc-fim.txt com o codigo de saida de cada passo (0 = deu certo); o resultado da conferencia
rem fica em Recriar PC\sistema\CONFERENCIA.md. Se o console oculto nao puder partir, segue nesta mesma janela.
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto && exit /b)
set "MOTOR=%~dp0..\..\Recriar PC\sistema\motor"
if not exist "%~dp0..\tests\saida" mkdir "%~dp0..\tests\saida"
del /q "%~dp0..\tests\saida\recriar-pc-fim.txt" 2>nul
call "%MOTOR%\Capturar agora.cmd"
set "CAPTURAR=%errorlevel%"
call "%MOTOR%\Conferir.cmd"
set "CONFERIR=%errorlevel%"
echo fim %date% %time% capturar=%CAPTURAR% conferir=%CONFERIR% > "%~dp0..\tests\saida\recriar-pc-fim.txt"
