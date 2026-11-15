@echo off
rem Abrir com explorer.exe: roda o "Capturar agora" e depois o "Conferir" do Recriar PC sem janela (console oculto).
rem Ao terminar grava tests\saida\recriar-pc-fim.txt; o resultado da conferencia fica em Recriar PC\sistema\CONFERENCIA.md.
if /i not "%~1"=="--oculto" (start "" /b conhost.exe --headless "%~f0" --oculto & exit /b)
set "MOTOR=%~dp0..\..\Recriar PC\sistema\motor"
if not exist "%~dp0..\tests\saida" mkdir "%~dp0..\tests\saida"
del /q "%~dp0..\tests\saida\recriar-pc-fim.txt" 2>nul
call "%MOTOR%\Capturar agora.cmd"
call "%MOTOR%\Conferir.cmd"
echo fim %date% %time% > "%~dp0..\tests\saida\recriar-pc-fim.txt"
