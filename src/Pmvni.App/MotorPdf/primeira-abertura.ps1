# Primeira abertura do Editor (ou instalacao incompleta). O Editor grava primeira-abertura.cmd ao lado deste script e o abre com o
# explorer.exe, fora do processo do Editor, com o token do usuario interativo (Tarefa 16).
# Faz o mesmo que o botao 'Reparar instalação' dos Ajustes: configurar-plugins.ps1 instala o motor PDF, os plugins PMVNI
# do Codex e do Claude Code e as ferramentas PDF, e grava resumo-instalacao.txt, que o Editor confere na proxima abertura.
# O log (primeira-abertura.log) e todo em UTF-8.
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
[IO.Directory]::CreateDirectory($base) | Out-Null
$log = Join-Path $base 'primeira-abertura.log'
$mutex = [Threading.Mutex]::new($false,'Local\PMVNI-PrimeiraAbertura')
$codigo = 0
try {
    try { $entrou = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
    if (-not $entrou) { return }
    try {
        $global:LASTEXITCODE = 0
        & (Join-Path $PSScriptRoot 'configurar-plugins.ps1') *>&1 | Out-File -LiteralPath $log -Encoding utf8
        if ($LASTEXITCODE) {
            $codigo = $LASTEXITCODE
            Add-Content -LiteralPath $log -Encoding UTF8 -Value ("FALHA    configurar-plugins.ps1 terminou com o codigo $codigo")
        }
    } catch {
        $codigo = 1
        Add-Content -LiteralPath $log -Encoding UTF8 -Value ('FALHA    ' + $_.Exception.Message)
    }
} finally {
    if ($entrou) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
exit $codigo
