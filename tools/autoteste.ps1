<#
  Autoteste do executável único, FORA do pacote do Claude (aberto pelo autoteste.cmd via explorer.exe).
  Apaga a extração do app em %TEMP%\.net para medir a primeira abertura (fria), roda o .exe duas vezes com
  --autoteste e grava em tests\saida: autoteste.json (1ª), autoteste-2.json (2ª) e autoteste-log.txt.
  Dados do app em tests\saida\dados (PMVNI_DADOS_DIR), para não deixar nada em %LOCALAPPDATA%.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
# O build-release grava em tests\saida\autoteste-alvo.txt o .exe a testar (explorer.exe não repassa argumentos); sem o arquivo, usa Aplicativo\.
$alvo = Join-Path $saida 'autoteste-alvo.txt'
if (Test-Path -LiteralPath $alvo) { $exe = (Get-Content -LiteralPath $alvo -Raw -Encoding UTF8).Trim() }
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
$netTemp = Join-Path $saida ('extracao-autoteste-' + [Guid]::NewGuid().ToString('N'))
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $netTemp
"TEMP=$env:TEMP" | Set-Content $log -Encoding utf8
$pastaApp = Join-Path $netTemp ([IO.Path]::GetFileNameWithoutExtension($exe))
foreach ($rodada in 1, 2) {
  $json = if ($rodada -eq 1) { Join-Path $saida 'autoteste.json' } else { Join-Path $saida 'autoteste-2.json' }
  if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $exe -ArgumentList '--autoteste', ('"' + $json + '"') -PassThru -Wait
  "rodada $rodada : $([math]::Round($sw.Elapsed.TotalSeconds,2)) s, código $($p.ExitCode)" | Registrar
}
"--- conteúdo de $netTemp ---" | Registrar
if (Test-Path $netTemp) {
  Get-ChildItem -LiteralPath $netTemp -Directory | ForEach-Object {
    $tam = (Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force | Measure-Object Length -Sum).Sum
    "$($_.Name)  $([math]::Round($tam/1MB,1)) MB" | Registrar
    Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object { "   $($_.Name)" | Registrar }
  }
}
try {
  & (Join-Path $PSScriptRoot 'roteiro.ps1') -SomenteMonitor -Executavel $exe
} catch {
  "Falha no ensaio do monitor: $($_.Exception.Message)" | Registrar
}
"FIM" | Registrar
