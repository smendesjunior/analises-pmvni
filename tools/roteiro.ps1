<#
  Roteiro da tela (Etapa 2), FORA do pacote do Claude (aberto pelo roteiro.cmd via explorer.exe; o roteiro-e-esperar.ps1 abre e espera).
  Roda o executável publicado com tests\corpus\longo.json e --roteiro: o app exercita o shell e o módulo Relatórios (importar para
  .pmvrel, rolagem, zoom, navegação, exportação, estouro, tema, campos em aberto, pacote mudado, faixa por módulo, reabrir) e o
  resultado vai para docs\roteiro-etapa-2.resultado.json. Pacotes, capturas PNG e PDFs ficam em tests\saida\roteiro (ignorado pelo
  git); dados do app (WebView2, ajustes.json, edicao) em tests\saida\dados-app. Ao terminar grava tests\saida\roteiro-fim.txt.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
$env:PMVNI_DADOS_DIR = Join-Path $saida 'dados-app'
$pasta = Join-Path $saida 'roteiro'
$resultado = Join-Path $pasta 'resultado.json'
$final = Join-Path $raiz 'docs\roteiro-etapa-2.resultado.json'
New-Item -ItemType Directory -Force -Path $pasta | Out-Null
if (Test-Path -LiteralPath $resultado) { Remove-Item -LiteralPath $resultado -Force }
$p = Start-Process -FilePath $exe -ArgumentList ('"' + (Join-Path $raiz 'tests\corpus\longo.json') + '"'), '--roteiro', ('"' + $resultado + '"') -PassThru -Wait
# O app só grava na pasta do JSON de saída (tests\saida\roteiro); o resultado é copiado para docs\ aqui.
if (Test-Path -LiteralPath $resultado) { Copy-Item -LiteralPath $resultado -Destination $final -Force }
"roteiro: código de saída $($p.ExitCode); resultado em $final" | Set-Content (Join-Path $saida 'roteiro-fim.txt') -Encoding utf8
