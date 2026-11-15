# Renomeia a pasta do projeto para "Análise de Projetos - PMVNI" e atualiza o recriar.json. Rodar fora do pacote (renomear-pasta.cmd via explorer.exe), com o app e o Claude fechados.
$ErrorActionPreference = 'Stop'
$velha = Split-Path $PSScriptRoot -Parent
$nova = Join-Path (Split-Path $velha -Parent) 'Análise de Projetos - PMVNI'
if (Test-Path $nova) { throw "Já existe: $nova" }
$r = Join-Path $velha 'recriar.json'
$t = [IO.File]::ReadAllText($r)
$t = $t.Replace('"nome": "Editor de Relatorios PMVNI"', '"nome": "Análise de Projetos - PMVNI"')
[IO.File]::WriteAllText($r, $t, (New-Object Text.UTF8Encoding $false))
Rename-Item -LiteralPath $velha -NewName 'Análise de Projetos - PMVNI'
$m = Join-Path (Split-Path $velha -Parent) 'Recriar PC\sistema\motor\Capturar agora.cmd'
if (Test-Path $m) { Start-Process explorer.exe "`"$m`"" }
