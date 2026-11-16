@echo off
rem Desenvolvimento: abre o build Debug com --captura e grava os PNGs em tests\saida\captura (dados do app em pasta separada).
set PMVNI_DADOS_DIR=%~dp0..\tests\saida\dados-captura
set PMVNI_CAPTURA_PROCESSO=%1
start "" /wait "%~dp0..\src\Pmvni.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\Pmvni.App.exe" --captura "%~dp0..\tests\saida\captura"
echo fim> "%~dp0..\tests\saida\captura-fim.txt"