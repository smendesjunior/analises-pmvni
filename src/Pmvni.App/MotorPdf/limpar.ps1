param([string]$EditorAberto)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'politica-limpeza.ps1')
. (Join-Path $PSScriptRoot 'inventario-motor.ps1')
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
    try {
        if (!(ConteudoMotorConferido $item $motor (Join-Path $motor 'bootstrap'))) { return }
        if ($null -eq $script:referenciasMotor) {
            $valores=@(
                Get-CimInstance Win32_Process | ForEach-Object { $_.CommandLine; $_.ExecutablePath }
                Get-CimInstance Win32_Service | ForEach-Object PathName
                Get-CimInstance Win32_StartupCommand | ForEach-Object Command
                Get-ScheduledTask | ForEach-Object { $_.Actions | ForEach-Object { $_.Execute; $_.Arguments } }
                $shell=New-Object -ComObject WScript.Shell
                foreach($nomePasta in 'Programs','CommonPrograms','Desktop','CommonDesktopDirectory','Startup','CommonStartup') {
                    $p=[Environment]::GetFolderPath($nomePasta)
                    if ($p -and (Test-Path -LiteralPath $p)) {
                        Get-ChildItem -LiteralPath $p -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                            $lnk=$shell.CreateShortcut($_.FullName); $lnk.TargetPath; $lnk.Arguments
                        }
                    }
                }
            )
            $script:referenciasMotor=[Environment]::ExpandEnvironmentVariables(($valores -join [Environment]::NewLine))
        }
        if ($script:referenciasMotor.IndexOf($alvo,[StringComparison]::OrdinalIgnoreCase) -ge 0) { return }
        $bytes = MedirSemLinks $item
        Add-Type -AssemblyName Microsoft.VisualBasic
        if ($item.PSIsContainer) {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($alvo,'OnlyErrorDialogs','SendToRecycleBin','ThrowException')
        } else {
            [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($alvo,'OnlyErrorDialogs','SendToRecycleBin','ThrowException')
        }
        Add-Content -LiteralPath $log -Encoding UTF8 -Value "$(Get-Date -Format o) Reciclado $alvo ($bytes bytes)"
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
    $script:referenciasMotor=$null
    $previsto = 'env-' + (Get-FileHash (Join-Path $PSScriptRoot 'requirements.lock')).Hash.Substring(0,20)
    $visao = 'visao-' + (Get-FileHash (Join-Path $PSScriptRoot 'visao.lock')).Hash.Substring(0,16)
    $ativo = AmbienteAtivo 'runtime.txt'
    $visaoAtiva = AmbienteAtivo 'visao-runtime.txt'
    $selo=Join-Path $motor 'kit-plugins-3.0.0_2.0.0_2.0.0.ok'
    if (($ativo -or $visaoAtiva) -and (Test-Path -LiteralPath $selo -PathType Leaf) -and (CaminhoSemLink $selo) -and
        [IO.File]::ReadAllText($selo).Trim() -ceq 'analise, analise-pro, analise-google') { Apagar $motor (Get-Item -LiteralPath $selo) }
    foreach ($nome in 'runtime.txt','visao-runtime.txt') {
        $ant=Join-Path $motor ($nome+'.anterior')
        if (!(AmbienteAtivo $nome) -or !(Test-Path -LiteralPath $ant -PathType Leaf) -or !(CaminhoSemLink $ant)) { continue }
        $texto=[IO.File]::ReadAllText($ant).Trim()
        $padrao='^'+[regex]::Escape($motor)+'\\(?:env-[0-9a-f]{20}|visao-[0-9a-f]{16})\\Scripts\\python\.exe$'
        if ($texto -match $padrao -and ($texto -eq [IO.File]::ReadAllText((Join-Path $motor $nome)).Trim() -or !(Test-Path -LiteralPath $texto))) { Apagar $motor (Get-Item -LiteralPath $ant) }
    }
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
    $instalacao = Join-Path $env:LOCALAPPDATA 'Programs\Analise de Projetos PMVNI'
    if ($EditorAberto -eq (Join-Path $instalacao 'Editor de Relatorios PMVNI.exe')) {
        foreach ($item in Get-ChildItem -LiteralPath $instalacao -File -Force) {
            if ([PoliticaLimpezaMotor]::SobraInstalacao($item.Name) -and $item.VersionInfo.OriginalFilename -eq 'Pmvni.App.dll' -and $item.VersionInfo.CompanyName -eq 'PMVNI') { Apagar $instalacao $item }
        }
    }
} catch { Write-Warning "Limpeza adiada: $_" }
finally { foreach ($trava in $travas) { $trava.ReleaseMutex(); $trava.Dispose() } }
