<#
  Autoteste do executável único, FORA do pacote do Claude (aberto pelo autoteste.cmd via explorer.exe).
  Usa uma pasta de extração exclusiva para uma abertura a frio com --autoteste.
  Grava autoteste.json e autoteste-log.txt em tests\saida ou na pasta de resultados informada.
  Dados do app em tests\saida\dados (PMVNI_DADOS_DIR), para não deixar nada em %LOCALAPPDATA%.
#>
param([string]$Executavel, [string]$PastaResultados)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = if ($Executavel) { $Executavel } else { Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe' }
$saida = if ($PastaResultados) { $PastaResultados } else { Join-Path $raiz 'tests\saida' }
New-Item -ItemType Directory -Force -Path $saida | Out-Null
# O build-release grava em tests\saida\autoteste-alvo.txt o .exe a testar (explorer.exe não repassa argumentos); sem o arquivo, usa Aplicativo\.
$alvo = Join-Path $saida 'autoteste-alvo.txt'
if (!$Executavel -and (Test-Path -LiteralPath $alvo)) { $exe = (Get-Content -LiteralPath $alvo -Raw -Encoding UTF8).Trim() }
$env:PMVNI_DADOS_DIR = Join-Path $saida 'dados'
$log = Join-Path $saida 'autoteste-log.txt'
# O build-release lê este log enquanto ele é gravado: cada linha é acrescentada com novas tentativas (arquivo preso por instantes).
function Registrar([Parameter(ValueFromPipeline = $true)][string]$linha) {
  process {
    for ($i = 0; $i -lt 50; $i++) {
      try { [System.IO.File]::AppendAllText($log, $linha + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false))); return }
      catch [System.IO.IOException] { Start-Sleep -Milliseconds 100 }
    }
    throw "Não consegui gravar no log: $log"
  }
}
# Extração exclusiva: não remove a extração do editor em uso.
$netTemp = Join-Path ([IO.Path]::GetTempPath()) ('pmvni-at-' + [Guid]::NewGuid().ToString('N').Substring(0,12))
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $netTemp
"TEMP=$env:TEMP" | Set-Content $log -Encoding utf8
$json = Join-Path $saida 'autoteste.json'
if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $exe -ArgumentList '--autoteste', ('"' + $json + '"') -PassThru -Wait
"abertura a frio: $([math]::Round($sw.Elapsed.TotalSeconds,2)) s, código $($p.ExitCode)" | Registrar
"--- conteúdo de $netTemp ---" | Registrar
if (Test-Path $netTemp) {
  Get-ChildItem -LiteralPath $netTemp -Directory | ForEach-Object {
    $tam = (Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force | Measure-Object Length -Sum).Sum
    "$($_.Name)  $([math]::Round($tam/1MB,1)) MB" | Registrar
    Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object { "   $($_.Name)" | Registrar }
  }
}
"FIM" | Registrar
