param([Parameter(Mandatory)][string]$KitLocal, [string]$PastaPmvni = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PMVNI'))
$ErrorActionPreference = 'Stop'
$instalador = Join-Path $KitLocal 'INSTALAR_KIT_PMVNI.cmd'
# O instalador da fonte conhece a nova versao fixada. O 4.6.12 local
# encerraria a atualizacao antes mesmo de copiar o instalador 5.0.0.
$cfgPath = Join-Path $PastaPmvni 'instalacao.json'
if (Test-Path -LiteralPath $cfgPath -PathType Leaf) {
    $cfg = [IO.File]::ReadAllText($cfgPath) | ConvertFrom-Json
    if ($cfg.modo_kit -notin @('local','espelho') -and $cfg.fonte) {
        $publicado = Join-Path $cfg.fonte 'INSTALAR_KIT_PMVNI.cmd'
        if (!(Test-Path -LiteralPath $publicado -PathType Leaf)) { exit 41 }
        $instalador = $publicado
    }
}
$arquivoEstado = Join-Path $PastaPmvni 'estado.json'
# O instalador antigo pode copiar seu sucessor enquanto ainda executa a versão anterior.
# Nesse caso a segunda passagem usa o novo instalador dentro do mesmo clique.
for ($passagem = 0; $passagem -lt 2; $passagem++) {
    $assinaturaInicial = (Get-FileHash -LiteralPath $instalador -Algorithm SHA256).Hash
    $inicio = [DateTime]::UtcNow
    & $instalador atualizar --sem-pausa --pasta-local $PastaPmvni
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    if (!(Test-Path -LiteralPath $arquivoEstado -PathType Leaf) -or
        [IO.File]::GetLastWriteTimeUtc($arquivoEstado) -lt $inicio) { exit 43 }
    $resultadoKit = [IO.File]::ReadAllText($arquivoEstado) | ConvertFrom-Json
    if ($resultadoKit.modo -ne 'atualizar') { exit 43 }
    if ($resultadoKit.situacao -notin @('CONCLUIDO', 'CONCLUIDO_COM_PENDENCIAS') -or
        $assinaturaInicial -eq (Get-FileHash -LiteralPath $instalador -Algorithm SHA256).Hash -or
        @($resultadoKit.etapas | Where-Object nome -like 'Limpeza da esta??o').Count -gt 0) { break }
    if ($passagem -eq 1) { exit 42 }
}
switch ($resultadoKit.situacao) {
    'ADIADO' { exit 4 }
    'SEM_FONTE' { exit 41 }
    'CONCLUIDO_COM_PENDENCIAS' { exit 42 }
    'CONCLUIDO' {}
    default { exit 43 }
}
