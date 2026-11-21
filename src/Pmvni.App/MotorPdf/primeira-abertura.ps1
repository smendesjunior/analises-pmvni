# Primeira abertura do Editor (ou instalacao incompleta). O Editor grava primeira-abertura.cmd ao lado deste script e o abre com o
# explorer.exe, fora do processo do Editor, com o token do usuario interativo (Tarefa 16).
# Faz o mesmo que o botao 'Instalar ou atualizar plugins' dos Ajustes: configurar-plugins.ps1 instala o motor PDF, os plugins PMVNI
# do Codex e do Claude Code e as ferramentas PDF, e grava resumo-instalacao.txt, que o Editor confere na proxima abertura.
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
[IO.Directory]::CreateDirectory($base) | Out-Null
$log = Join-Path $base 'primeira-abertura.log'
$mutex = [Threading.Mutex]::new($false,'Local\PMVNI-PrimeiraAbertura')
try {
    try { $entrou = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
    if (-not $entrou) { return }
    try {
        & (Join-Path $PSScriptRoot 'configurar-plugins.ps1') *> $log
    } catch {
        Add-Content -LiteralPath $log -Encoding UTF8 -Value ('FALHA    ' + $_.Exception.Message)
        exit 1
    }
} finally {
    if ($entrou) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
