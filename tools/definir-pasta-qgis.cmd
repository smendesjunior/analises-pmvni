@echo off
rem Grava a pasta QGIS do módulo Imóveis nos ajustes do editor (fora do pacote do Claude). Feche o editor antes.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0definir-pasta-qgis.ps1" %*
