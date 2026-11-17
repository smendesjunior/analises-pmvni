<#
  Apaga as cópias de trabalho (git worktree) que sobraram em .claude\worktrees e os registros delas em .git\worktrees, depois de juntadas ao
  ramo principal (autorizado pelo usuário em 2026-11-17). Aberto pelo explorer.exe (fora do pacote do Claude); se o Windows ainda bloquear, pede
  administrador (UAC) e tenta de novo. Grava o resultado em tests\saida\limpeza-copias.json.
  Só apaga dentro de <projeto>\.claude\worktrees e <projeto>\.git\worktrees.
#>
param([switch]$Elevado)
$ErrorActionPreference = 'Continue'
$raiz = Split-Path -Parent $PSScriptRoot
$alvos = @((Join-Path $raiz '.claude\worktrees'), (Join-Path $raiz '.git\worktrees'))
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null

function Tamanho([string]$p) { if (-not (Test-Path -LiteralPath $p)) { return 0 }; $s = (Get-ChildItem -LiteralPath $p -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum; if ($null -eq $s) { 0 } else { [int64]$s } }

$antes = ($alvos | ForEach-Object { Tamanho $_ } | Measure-Object -Sum).Sum
$erros = @()
foreach ($base in $alvos) {
    if (-not (Test-Path -LiteralPath $base)) { continue }
    foreach ($d in Get-ChildItem -LiteralPath $base -Directory -Force) {
        if (-not $d.FullName.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { continue }
        # Somente leitura e atributos de sistema atrapalham a exclusão: tira antes.
        Get-ChildItem -LiteralPath $d.FullName -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Attributes = 'Normal' } catch { } }
        & cmd.exe /c "rd /s /q `"$($d.FullName)`"" 2>$null
        if (Test-Path -LiteralPath $d.FullName) { $erros += $d.FullName }
    }
}
if ($erros.Count -gt 0 -and -not $Elevado) {
    # Ainda bloqueado: pede administrador pelo UAC e repete.
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Elevado')
    exit
}
$depois = ($alvos | ForEach-Object { Tamanho $_ } | Measure-Object -Sum).Sum
[ordered]@{ antes_bytes = $antes; depois_bytes = $depois; liberados_bytes = $antes - $depois; elevado = [bool]$Elevado; erros = $erros } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $saida 'limpeza-copias.json') -Encoding UTF8
