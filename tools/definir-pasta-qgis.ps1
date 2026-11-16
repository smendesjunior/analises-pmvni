<#
  Grava a pasta QGIS do módulo Imóveis no ajustes.json do editor (%APPDATA%\EditorRelatoriosPMVNI), FORA do pacote do Claude
  (aberto pelo definir-pasta-qgis.cmd via explorer.exe). Faz o mesmo que Ajustes › Fontes de dados › Selecionar pasta…
  Uso: definir-pasta-qgis.cmd (usa a pasta do kit neste PC) ou com -Pasta "<caminho>". Feche o editor antes.
#>
param([string]$Pasta = (Join-Path $env:USERPROFILE 'OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)\05 - GEOPROCESSAMENTO (QGIS, LOTEAMENTOS, ZONEAMENTO)\2 - QGIS'))
$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'definir-pasta-qgis-resultado.txt'
try {
    if (-not (Test-Path -LiteralPath $Pasta)) { throw "Pasta não encontrada: $Pasta" }
    $dir = Join-Path $env:APPDATA 'EditorRelatoriosPMVNI'
    $arq = Join-Path $dir 'ajustes.json'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $texto = if (Test-Path -LiteralPath $arq) { [IO.File]::ReadAllText($arq) } else { '{}' }
    Add-Type -AssemblyName System.Web.Extensions
    $js = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $js.MaxJsonLength = 67108864
    $obj = $js.DeserializeObject($texto)
    $obj['pasta_qgis'] = $Pasta
    [IO.File]::WriteAllText($arq + '.tmp', $js.Serialize($obj), (New-Object Text.UTF8Encoding $false))
    Move-Item -LiteralPath ($arq + '.tmp') -Destination $arq -Force
    $conf = $js.DeserializeObject([IO.File]::ReadAllText($arq))['pasta_qgis']
    "ok: pasta_qgis = $conf" | Set-Content -LiteralPath $log -Encoding utf8
} catch { "erro: $($_.Exception.Message)" | Set-Content -LiteralPath $log -Encoding utf8 }
