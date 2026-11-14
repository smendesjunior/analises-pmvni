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
$env:PMVNI_DADOS_DIR = Join-Path $saida 'dados'
$log = Join-Path $saida 'autoteste-log.txt'
$netTemp = Join-Path $env:TEMP '.net'
"TEMP=$env:TEMP" | Set-Content $log -Encoding utf8
$pastaApp = Join-Path $netTemp ([IO.Path]::GetFileNameWithoutExtension($exe))
if (Test-Path -LiteralPath $pastaApp) { Remove-Item -LiteralPath $pastaApp -Recurse -Force; "apagado: $pastaApp" | Add-Content $log -Encoding utf8 }
foreach ($rodada in 1, 2) {
  $json = if ($rodada -eq 1) { Join-Path $saida 'autoteste.json' } else { Join-Path $saida 'autoteste-2.json' }
  if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process -FilePath $exe -ArgumentList '--autoteste', ('"' + $json + '"') -PassThru -Wait
  "rodada $rodada : $([math]::Round($sw.Elapsed.TotalSeconds,2)) s, código $($p.ExitCode)" | Add-Content $log -Encoding utf8
}
"--- conteúdo de $netTemp ---" | Add-Content $log -Encoding utf8
if (Test-Path $netTemp) {
  Get-ChildItem -LiteralPath $netTemp -Directory | ForEach-Object {
    $tam = (Get-ChildItem -LiteralPath $_.FullName -Recurse -File -Force | Measure-Object Length -Sum).Sum
    "$($_.Name)  $([math]::Round($tam/1MB,1)) MB" | Add-Content $log -Encoding utf8
    Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object { "   $($_.Name)" | Add-Content $log -Encoding utf8 }
  }
}
"FIM" | Add-Content $log -Encoding utf8
