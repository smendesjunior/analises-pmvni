<#
  Roteiro da tela (Tarefa 10), FORA do pacote do Claude (aberto pelo roteiro.cmd via explorer.exe).
  Roda o executável publicado com tests\corpus\longo.json e --roteiro: o app exercita abrir, rolagem, zoom, navegação,
  exportação, o corpus com estouro e o tema escuro, e grava docs\roteiro-etapa-1.resultado.json; capturas PNG e PDFs
  ficam em tests\saida\roteiro (ignorado pelo git). Dados do WebView2 em tests\saida\dados-app.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
$env:PMVNI_DADOS_DIR = Join-Path $saida 'dados-app'
$resultado = Join-Path $raiz 'docs\roteiro-etapa-1.resultado.json'
if (Test-Path -LiteralPath $resultado) { Remove-Item -LiteralPath $resultado -Force }
$p = Start-Process -FilePath $exe -ArgumentList ('"' + (Join-Path $raiz 'tests\corpus\longo.json') + '"'), '--roteiro', ('"' + $resultado + '"') -PassThru -Wait
"roteiro: código de saída $($p.ExitCode); resultado em $resultado" | Set-Content (Join-Path $saida 'roteiro-fim.txt') -Encoding utf8
