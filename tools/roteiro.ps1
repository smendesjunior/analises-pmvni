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
$pasta = Join-Path $saida 'roteiro'
$resultado = Join-Path $pasta 'resultado.json'
$final = Join-Path $raiz 'docs\roteiro-etapa-1.resultado.json'
New-Item -ItemType Directory -Force -Path $pasta | Out-Null
if (Test-Path -LiteralPath $resultado) { Remove-Item -LiteralPath $resultado -Force }
$p = Start-Process -FilePath $exe -ArgumentList ('"' + (Join-Path $raiz 'tests\corpus\longo.json') + '"'), '--roteiro', ('"' + $resultado + '"') -PassThru -Wait
# O app só grava na pasta do JSON de saída (tests\saida\roteiro); o resultado é copiado para docs\ aqui.
if (Test-Path -LiteralPath $resultado) { Copy-Item -LiteralPath $resultado -Destination $final -Force }
"roteiro: código de saída $($p.ExitCode); resultado em $final" | Set-Content (Join-Path $saida 'roteiro-fim.txt') -Encoding utf8
