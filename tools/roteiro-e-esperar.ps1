<#
  Roda o roteiro da tela FORA do pacote do Claude e espera o resultado.
  Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1
  Apaga tests\saida\roteiro-fim.txt, abre tools\roteiro.cmd pelo explorer.exe, espera até 20 min (o passo 64 soma uma conversão do LibreOffice) pelo roteiro-fim.txt, lê
  docs\roteiro-etapa-2.resultado.json, imprime as falhas e sai com código 0 só com "ok": true e zero programas fora da lista.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$saida = Join-Path $raiz 'tests\saida'
$fim = Join-Path $saida 'roteiro-fim.txt'
$resultado = Join-Path $raiz 'docs\roteiro-etapa-2.resultado.json'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
if (Test-Path -LiteralPath $fim) { Remove-Item -LiteralPath $fim -Force }
$antes = if (Test-Path -LiteralPath $resultado) { (Get-Item -LiteralPath $resultado).LastWriteTimeUtc } else { [DateTime]::MinValue }

Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + (Join-Path $PSScriptRoot 'roteiro.cmd') + '"')
$limite = (Get-Date).AddMinutes(20)
while (-not (Test-Path -LiteralPath $fim) -and (Get-Date) -lt $limite) { Start-Sleep -Seconds 1 }
if (-not (Test-Path -LiteralPath $fim)) { Write-Host 'O roteiro não terminou em 20 min.'; exit 2 }
Write-Host (Get-Content -LiteralPath $fim -Raw -Encoding UTF8).Trim()

if (-not (Test-Path -LiteralPath $resultado) -or (Get-Item -LiteralPath $resultado).LastWriteTimeUtc -le $antes) {
    Write-Host "O roteiro não gravou um resultado novo em $resultado."
    exit 3
}
$r = Get-Content -LiteralPath $resultado -Raw -Encoding UTF8 | ConvertFrom-Json
$falhas = @($r.falhas)
Write-Host ("falhas: " + $(if ($falhas.Count -eq 0) { '(nenhuma)' } else { $falhas -join ', ' }))
foreach ($f in $falhas) {
    $passo = $r.passos.$f
    if ($passo.excecao) { Write-Host "  $f -> $($passo.excecao)" }
}
Write-Host "ok: $($r.ok)"
$fora = $r.programas_iniciados.fora_do_agente_falso
Write-Host "fora_do_agente_falso: $fora"
if ($r.ok -eq $true -and $null -ne $fora -and $fora -eq 0) { exit 0 } else { exit 1 }
