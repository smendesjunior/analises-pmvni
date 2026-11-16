<#
  Publica uma compilação do editor no GitHub (smendesjunior/analises-pmvni), para a atualização automática dos outros PCs
  (Ajustes › Sobre e suporte › Atualizações; Atualizador.cs). No padrão do tools\publicar-github.ps1 do WinStatusBar.
  Chamado pelo build-release.ps1 depois de o .exe estar conferido em Aplicativo\. Pode ser rodado à mão:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\publicar-github.ps1 -Exe "Aplicativo\Editor de Relatorios PMVNI.exe" -Compilacao 20261116021306
  Passos: busca de segredos nas linhas que ainda não estão no GitHub -> etiqueta build-<compilação> no commit atual -> envio da etiqueta
  -> Release como rascunho -> envio do .exe e do manifesto -> publicada como a mais recente -> conferência pelo link fixo.
  Os PCs nunca veem uma Release pela metade. Não envia ramos (só a etiqueta e o código até ela).
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Compilacao
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
# A saída do git (assuntos dos commits, com acentos) vem em UTF-8: sem isto, o PowerShell 5.1 a lê no código de página do console
# e as notas da Release saem estragadas.
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
$OutputEncoding = [Console]::OutputEncoding
$raiz = Split-Path -Parent $PSScriptRoot
$repo = 'smendesjunior/analises-pmvni'
$nomeExe = 'EditorRelatoriosPMVNI.exe'
$nomeManifesto = 'EditorRelatoriosPMVNI-update.json'
$tag = "build-$Compilacao"
$linkFixo = "https://github.com/$repo/releases/latest/download/$nomeManifesto"

if ($Compilacao -notmatch '^\d{14}$') { throw "Compilação inválida: $Compilacao" }
if (-not (Test-Path -LiteralPath $Exe)) { throw "Executável não encontrado: $Exe" }
$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh) { $gh = @("$env:ProgramFiles\GitHub CLI\gh.exe", "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
if (-not $gh) { throw 'GitHub CLI (gh) não encontrado.' }

function Nativo([string]$programa, [string[]]$argumentos, [string]$o_que) {
    $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    $saida = & $programa @argumentos 2>&1
    $codigo = $LASTEXITCODE; $ErrorActionPreference = $antes
    if ($codigo -ne 0) { throw "$o_que falhou (código $codigo): $($saida -join ' ')" }
    return $saida
}

Nativo $gh @('auth', 'status') 'Login do GitHub CLI (gh auth status)' | Out-Null
$versao = ([xml](Get-Content -LiteralPath (Join-Path $raiz 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$temp = Join-Path ([IO.Path]::GetTempPath()) ('pmvni-publicar-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
Push-Location $raiz
try {
    if (Nativo git @('ls-remote', '--tags', 'origin', "refs/tags/$tag") 'Consulta das etiquetas publicadas') { throw "A compilação $Compilacao já foi publicada." }

    # Busca de segredos nas linhas adicionadas pelos commits que ainda não estão no GitHub (o histórico todo na primeira vez).
    $publicados = @(Nativo git @('ls-remote', '--refs', 'origin') 'Consulta do GitHub' | ForEach-Object { ("$_" -split '\s+')[0] } | Where-Object { $_ })
    $alcance = @('HEAD') + @($publicados | ForEach-Object { "^$_" })
    $padrao = '(?<![A-Za-z0-9])sk-(proj-|svcacct-|admin-|ant-)?[A-Za-z0-9_\-]{20,}|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AIza[0-9A-Za-z_\-]{35}|AKIA[0-9A-Z]{16}|xox[abpr]-[A-Za-z0-9-]{10,}|-----BEGIN [A-Z ]*PRIVATE KEY-----|\b\d{3}\.\d{3}\.\d{3}-\d{2}\b'
    $achados = @(Nativo git (@('-c', 'core.quotePath=false', 'log', '-p', '--no-color', '--format=commit %h') + $alcance) 'Leitura do histórico' |
        ForEach-Object { "$_" } | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') -and $_ -match $padrao })
    if ($achados.Count) { throw ("Possível segredo ou CPF no que iria para o GitHub; nada foi enviado:`n" + ($achados -join "`n")) }

    # O que mudou: assuntos dos commits desde a etiqueta anterior (as 40 mais recentes), sem os de merge.
    $anterior = @(Nativo git @('tag', '--list', 'build-*', '--sort=-creatordate') 'Etiquetas locais' | Where-Object { $_ }) | Select-Object -First 1
    $faixa = if ($anterior) { "$anterior..HEAD" } else { 'HEAD' }
    $notas = @(Nativo git @('log', '--no-merges', '--format=- %s', '-n', '40', $faixa) 'Notas da versão' | ForEach-Object { "$_" }) -join "`n"
    if (-not $notas) { $notas = '- Ajustes internos.' }

    $item = Get-Item -LiteralPath $Exe
    $manifesto = [ordered]@{
        Version = $versao
        Build = $Compilacao
        Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        Size = $item.Length
        Url = "https://github.com/$repo/releases/download/$tag/$nomeExe"
        Notes = $notas
    }
    $utf8 = New-Object Text.UTF8Encoding $false
    $arqManifesto = Join-Path $temp $nomeManifesto
    [IO.File]::WriteAllText($arqManifesto, ($manifesto | ConvertTo-Json), $utf8)
    $arqExe = Join-Path $temp $nomeExe
    Copy-Item -LiteralPath $item.FullName -Destination $arqExe
    $arqNotas = Join-Path $temp 'notas.md'
    [IO.File]::WriteAllText($arqNotas, "Editor de Relatórios PMVNI $versao (compilação $Compilacao)`n`n$notas`n`nBaixe ``$nomeExe`` e abra (não precisa instalar). As cópias já em uso se atualizam sozinhas.", $utf8)

    Nativo git @('tag', '-a', $tag, '-m', "Editor de Relatórios PMVNI $versao (compilação $Compilacao)") 'Etiqueta' | Out-Null
    try { Nativo git @('push', 'origin', $tag) "Envio da etiqueta $tag" | Out-Null }
    catch { & git tag -d $tag *> $null; throw }

    Nativo $gh @('release', 'create', $tag, '--repo', $repo, '--draft', '--verify-tag', '--title', "Editor de Relatórios PMVNI $versao ($Compilacao)", '--notes-file', $arqNotas) 'Criação da Release' | Out-Null
    Nativo $gh @('release', 'upload', $tag, $arqExe, '--repo', $repo, '--clobber') 'Envio do executável' | Out-Null
    Nativo $gh @('release', 'upload', $tag, $arqManifesto, '--repo', $repo, '--clobber') 'Envio do manifesto' | Out-Null
    Nativo $gh @('release', 'edit', $tag, '--repo', $repo, '--draft=false', '--latest') 'Publicação da Release' | Out-Null

    $visto = $null
    for ($i = 0; $i -lt 6; $i++) {
        try { $visto = (Invoke-RestMethod -Uri $linkFixo -Headers @{ 'Cache-Control' = 'no-cache' } -UseBasicParsing).Build } catch { }
        if ($visto -eq $Compilacao) { break }
        Start-Sleep -Seconds 10
    }
    if ($visto -ne $Compilacao) { Write-Warning "A Release foi publicada, mas o link fixo ainda mostra a compilação '$visto'. Confira depois: $linkFixo" }
    Write-Host "Publicado no GitHub: $tag — https://github.com/$repo/releases/tag/$tag"
} finally {
    Pop-Location
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
