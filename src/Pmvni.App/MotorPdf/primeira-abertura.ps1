# Primeira abertura da compilacao. Executado com o token do usuario interativo.
$ErrorActionPreference = 'Stop'
$motor = Join-Path $PSScriptRoot 'instalar.ps1'
& $motor
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
$cfg = Join-Path $env:USERPROFILE '.codex\pmvni-kit.json'
$kit = if (Test-Path -LiteralPath $cfg) { (Get-Content -Raw -Encoding UTF8 -LiteralPath $cfg | ConvertFrom-Json).kit_root }
if (-not $kit) { $kit = '\\srv-fileserver.PMVNI.PREF\pmvni\Obras\Engenharia\KIT DE TREINAMENTO DE IA (PMVNI)' }
$catalogo = Join-Path $kit '01 - PLUGINS'
$nomes = @('analise','analise-pro','analise-google')
$versoes = foreach ($n in $nomes) {
    $manifesto = Join-Path $catalogo "plugins\$n\.codex-plugin\plugin.json"
    (Get-Content -LiteralPath $manifesto -Raw -Encoding UTF8 | ConvertFrom-Json).version
}
$recibo = Join-Path $PSScriptRoot ('plugins-' + ($versoes -join '_') + '.ok')
if (Test-Path -LiteralPath $recibo) { return }
$lockPlugins = [Threading.Mutex]::new($false,'Local\PMVNI-PluginsEditor')
try {
    try { $entrou = $lockPlugins.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
    if (-not $entrou -or (Test-Path -LiteralPath $recibo)) { return }
    $codex = (Get-Command codex.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    & $codex plugin marketplace add $catalogo
    if ($LASTEXITCODE) { throw 'Falha ao registrar catalogo PMVNI no Codex.' }
    foreach ($n in $nomes) {
        & $codex plugin add "$n@pmvni-kit" --json
        if ($LASTEXITCODE) { throw "Codex: falha ao instalar $n." }
    }
    $claude = Get-Command claude.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($claude) {
        $nomeCatalogo = (Get-Content -LiteralPath (Join-Path $catalogo '.claude-plugin\marketplace.json') -Raw -Encoding UTF8 | ConvertFrom-Json).name
        & $claude.Source plugin marketplace add $catalogo --scope user
        if ($LASTEXITCODE) {
            & $claude.Source plugin marketplace update $nomeCatalogo
            if ($LASTEXITCODE) { throw 'Claude Code: falha ao atualizar catalogo.' }
        }
        & $claude.Source plugin install "analise@$nomeCatalogo" --scope user
        if ($LASTEXITCODE) { throw 'Claude Code: falha ao instalar analise.' }
        & $claude.Source plugin update "analise@$nomeCatalogo" --scope user
        if ($LASTEXITCODE) { throw 'Claude Code: falha ao atualizar analise.' }
    }
    [IO.File]::WriteAllText($recibo,($nomes -join ', '))
} finally {
    if ($entrou) { $lockPlugins.ReleaseMutex() }
    $lockPlugins.Dispose()
}
