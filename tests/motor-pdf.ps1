$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../src/Pmvni.App/MotorPdf/politica-limpeza.ps1')
. (Join-Path $PSScriptRoot '../src/Pmvni.App/MotorPdf/nativo.ps1')
$saida = Invocar-Nativo powershell.exe @('-NoProfile','-Command',"[Console]::Error.WriteLine('aviso'); Write-Output 'ok'; exit 0")
if ($saida -ne 'ok' -or @($saida).Count -ne 1) { throw 'stderr contaminou stdout' }
try {
    Invocar-Nativo powershell.exe @('-NoProfile','-Command',"[Console]::Error.WriteLine('falha real'); exit 7")
    throw 'Codigo de erro ignorado'
} catch { if ($_ -notmatch 'codigo 7.*falha real') { throw } }
$previsto = 'visao-0000000000000000'
$ativo = 'visao-1111111111111111'
$antigo = 'visao-2222222222222222'
foreach ($protegido in $previsto,$ativo,'fontes','python','env-11111111111111111111') {
    if ([PoliticaLimpezaMotor]::Ambiente($protegido,$previsto,$ativo)) { throw "Desprotegido: $protegido" }
}
if (![PoliticaLimpezaMotor]::Ambiente($antigo,$previsto,$ativo)) { throw 'Ambiente antigo preservado' }
if ([PoliticaLimpezaMotor]::Ambiente($antigo,$previsto,'')) { throw 'Ponteiro ausente' }
'Testes do motor PDF passaram.'
# Executa a limpeza real somente numa arvore temporaria exclusiva.
$localAnterior = $env:LOCALAPPDATA
$raiz = Join-Path ([IO.Path]::GetTempPath()) ('pmvni-teste-' + [guid]::NewGuid())
try {
    $env:LOCALAPPDATA = $raiz
    $motor = Join-Path $raiz 'PMVNI\MotorPdf'
    $scripts = Join-Path $PSScriptRoot '../src/Pmvni.App/MotorPdf'
    $atual = 'visao-' + (Get-FileHash (Join-Path $scripts 'visao.lock')).Hash.Substring(0,16)
    foreach ($nome in $atual,$ativo,$antigo,'teste-2.1.6','fontes','python') {
        [void][IO.Directory]::CreateDirectory((Join-Path $motor $nome))
    }
    $bin = [IO.Directory]::CreateDirectory((Join-Path $motor "$ativo\Scripts"))
    $python = Join-Path $bin.FullName 'python.exe'
    [IO.File]::WriteAllText($python,'')
    [IO.File]::WriteAllText((Join-Path $motor 'visao-runtime.txt'),$python)
    function Get-CimInstance { @() }
    $dirComRegistro=Join-Path $motor 'visao-3333333333333333'
    $dirComExtra=Join-Path $motor 'visao-4444444444444444'
    $dirReferido=Join-Path $motor 'visao-5555555555555555'
    . (Join-Path $scripts 'inventario-motor.ps1')
    foreach ($d in $dirComRegistro,$dirComExtra,$dirReferido) {
        [void][IO.Directory]::CreateDirectory($d)
        [IO.File]::WriteAllText((Join-Path $d 'motor.py'),'# script de ensaio')
        RegistrarAmbienteCriado $motor $d
    }
    [IO.File]::WriteAllText((Join-Path $dirComExtra 'peca.pdf'),'%PDF-reservado')
    function Get-ScheduledTask { [pscustomobject]@{ Actions=@([pscustomobject]@{Execute='python';Arguments=(Join-Path $dirReferido 'motor.py')}) } }
    $selo=Join-Path $motor 'kit-plugins-3.0.0_2.0.0_2.0.0.ok'
    [IO.File]::WriteAllText($selo,'analise, analise-pro, analise-google')
    $backup = Join-Path $raiz 'PMVNI/removidos/documento.txt'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
    [IO.File]::WriteAllText($backup,'preservar')
    [IO.File]::SetLastWriteTimeUtc($backup,[DateTime]::UtcNow.AddYears(-1))
    & (Join-Path $scripts 'limpar.ps1')
    if ((Test-Path $selo) -or (Test-Path $dirComRegistro)) { throw 'Selo e ambiente substituídos permaneceram.' }
    if (!(Test-Path (Join-Path $dirComExtra 'peca.pdf')) -or !(Test-Path $dirReferido)) { throw 'Documento ou dependência de tarefa removidos.' }
    [IO.File]::WriteAllText($selo,'arquivo pessoal')
    & (Join-Path $scripts 'limpar.ps1')
    if ([IO.File]::ReadAllText($selo) -ne 'arquivo pessoal') { throw 'Mesmo nome foi confundido com selo do kit.' }
    if ([IO.File]::ReadAllText($backup) -ne 'preservar') { throw 'Backup modificado' }
    foreach ($nome in $atual,$ativo,'fontes','python','limpeza.log') {
        if (!(Test-Path -LiteralPath (Join-Path $motor $nome))) { throw "Faltou $nome" }
    }
    foreach ($nome in $antigo,'teste-2.1.6') {
        if (Test-Path -LiteralPath (Join-Path $motor $nome)) { throw "Sobrou $nome" }
    }
} finally {
    $env:LOCALAPPDATA = $localAnterior
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($raiz)) -eq [IO.Path]::GetTempPath().TrimEnd('\') -and (Test-Path -LiteralPath $raiz)) {
        Remove-Item -LiteralPath $raiz -Recurse -Force
    }
}
