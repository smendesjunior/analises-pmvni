param([string]$EditorAberto)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'politica-limpeza.ps1')
$pmvni = Join-Path $env:LOCALAPPDATA 'PMVNI'
$motor = Join-Path $pmvni 'MotorPdf'
$log = Join-Path $motor 'limpeza.log'

function CaminhoSemLink($caminho) {
    while ($caminho) {
        if ([IO.File]::GetAttributes($caminho) -band [IO.FileAttributes]::ReparsePoint) { return $false }
        $caminho = [IO.Path]::GetDirectoryName($caminho)
    }
    return $true
}

function MedirSemLinks($item) {
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Contem link' }
    if (!$item.PSIsContainer) { return $item.Length }
    [long]$total = 0
    foreach ($filho in Get-ChildItem -LiteralPath $item.FullName -Force) {
        $total += MedirSemLinks $filho
    }
    return $total
}

function Apagar($raiz, $item) {
    $alvo = [IO.Path]::GetFullPath($item.FullName)
    if ([IO.Path]::GetDirectoryName($alvo) -ne $raiz -or !(CaminhoSemLink $alvo)) { return }
    foreach ($processo in $processos) {
        if (($processo.CommandLine -and $processo.CommandLine.IndexOf($alvo,[StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ($processo.ExecutablePath -and $processo.ExecutablePath.StartsWith($alvo,[StringComparison]::OrdinalIgnoreCase))) { return }
    }
    try {
        $bytes = MedirSemLinks $item
        Remove-Item -LiteralPath $alvo -Recurse -Force
        Add-Content -LiteralPath $log -Encoding UTF8 -Value "$(Get-Date -Format o) Apagado $alvo ($bytes bytes)"
    } catch { Add-Content -LiteralPath $log -Encoding UTF8 -Value "$(Get-Date -Format o) Preservado $alvo : $_" }
}

function AmbienteAtivo($arquivo) {
    $ponteiro = Join-Path $motor $arquivo
    if (!(Test-Path -LiteralPath $ponteiro -PathType Leaf)) { return }
    $python = [IO.File]::ReadAllText($ponteiro).Trim()
    if (!(Test-Path -LiteralPath $python -PathType Leaf)) { return }
    $ambiente = [IO.Directory]::GetParent($python).Parent
    if ($ambiente.Parent.FullName -eq $motor -and $python -eq (Join-Path $ambiente.FullName 'Scripts\python.exe')) { $ambiente.Name }
}

$travas = @()
try {
    if (!(CaminhoSemLink $motor) -or ((Test-Path $log) -and !(CaminhoSemLink $log))) { return }
    foreach ($nome in 'ConfigurarPlugins','MotorPdf') {
        $trava = [Threading.Mutex]::new($false,"Local\PMVNI-$nome")
        try { $entrou = $trava.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
        if (!$entrou) { $trava.Dispose(); return }
        $travas += $trava
    }
    $processos = @(Get-CimInstance Win32_Process -Property CommandLine,ExecutablePath)
    $previsto = 'env-' + (Get-FileHash (Join-Path $PSScriptRoot 'requirements.lock')).Hash.Substring(0,20)
    $visao = 'visao-' + (Get-FileHash (Join-Path $PSScriptRoot 'visao.lock')).Hash.Substring(0,16)
    $ativo = AmbienteAtivo 'runtime.txt'
    $visaoAtiva = AmbienteAtivo 'visao-runtime.txt'
    foreach ($item in Get-ChildItem -LiteralPath $motor -Directory) {
        if ([PoliticaLimpezaMotor]::Teste($item.Name) -or [PoliticaLimpezaMotor]::Ambiente($item.Name,$previsto,$ativo) -or
            [PoliticaLimpezaMotor]::Ambiente($item.Name,$visao,$visaoAtiva)) { Apagar $motor $item }
    }
    $bootstrap = Join-Path $motor 'bootstrap'
    if (Test-Path -LiteralPath $bootstrap) {
        $pastas = Get-ChildItem -LiteralPath $bootstrap -Directory | Where-Object { [PoliticaLimpezaMotor]::Bootstrap($_.Name) } | Sort-Object LastWriteTimeUtc -Descending
        foreach ($item in $pastas | Select-Object -Skip 1) {
            if ($item.FullName -ne $PSScriptRoot) { Apagar $bootstrap $item }
        }
    }
    $removidos = Join-Path $pmvni 'removidos'
    if (Test-Path -LiteralPath $removidos) {
        foreach ($item in Get-ChildItem -LiteralPath $removidos -Force) {
            if ([PoliticaLimpezaMotor]::Vencido($item.LastWriteTimeUtc,[datetime]::UtcNow)) { Apagar $removidos $item }
        }
    }
    $instalacao = Join-Path $env:LOCALAPPDATA 'Programs\Analise de Projetos PMVNI'
    if ($EditorAberto -eq (Join-Path $instalacao 'Editor de Relatorios PMVNI.exe')) {
        foreach ($item in Get-ChildItem -LiteralPath $instalacao -File -Force) {
            if ([PoliticaLimpezaMotor]::SobraInstalacao($item.Name)) { Apagar $instalacao $item }
        }
    }
} catch { Write-Warning "Limpeza adiada: $_" }
finally { foreach ($trava in $travas) { $trava.ReleaseMutex(); $trava.Dispose() } }
