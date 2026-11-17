<#
  Publica o Editor de Relatórios PMVNI como executável único em Aplicativo\ e limpa os builds.
  Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1
  Ordem: testes (dotnet test, com PMVNI_DADOS_DIR em tests\saida\dados) -> publicar -> conferir que a publicação
         tem um único executável (MZ, tamanho) -> abrir o .exe publicado com --autoteste (tools\autoteste.cmd, via
         explorer.exe, fora do pacote do Claude; exige ok=true e sem elevação) -> só então copiar para Aplicativo\
         com nome temporário, conferir o hash e trocar (Aplicativo\ nunca fica sem executável se algo falhar)
         -> apagar bin, obj, artifacts, tests\saida, node_modules\.cache e temporários
         -> abrir tools\limpar-extracoes.cmd (explorer.exe) para apagar as extrações antigas em %TEMP%\.net
         -> informar o espaço liberado.
  Não apaga web\paginador\node_modules (necessário para refazer o bundle), só os caches dele.
#>
param([switch]$SemGitHub, [switch]$SemLimpeza)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$projeto = Join-Path $raiz 'src\Pmvni.App\Pmvni.App.csproj'
$publicacao = Join-Path $raiz 'artifacts\publicacao'
$pastaFinal = Join-Path $raiz 'Aplicativo'
$exeFinal = Join-Path $pastaFinal 'Editor de Relatorios PMVNI.exe'

function Tamanho([string]$caminho) {
    if (-not (Test-Path -LiteralPath $caminho)) { return 0 }
    $soma = (Get-ChildItem -LiteralPath $caminho -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    if ($null -eq $soma) { return 0 } else { return [int64]$soma }
}

# 0. Testes (a pasta de dados do app vai para tests\saida\dados, ignorada pelo git, e some na limpeza)
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
$env:PMVNI_DADOS_DIR = Join-Path $saida 'dados'
$commitFonte = & git -C $raiz rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível identificar o commit da build.' }
# A solução mapeia Release para x64. Executar projetos com a mesma plataforma evita
# procurar bin/Release depois de compilar em bin/x64/Release. Sem retry: toda falha bloqueia.
$projetosTeste = @(Get-ChildItem (Join-Path $raiz 'tests') -Filter 'Pmvni.*.Testes.csproj' -Recurse | Sort-Object FullName)
if ($projetosTeste.Count -eq 0) { throw 'Nenhum projeto de teste encontrado.' }
$falhasTeste = @()
foreach ($p in $projetosTeste) {
    Write-Host "Testando $($p.BaseName) (Release/x64)..."
    $trx = Join-Path $saida "resultados\$($p.BaseName).trx"
    if (Test-Path -LiteralPath $trx) { Remove-Item -LiteralPath $trx -Force }
    & dotnet test $p.FullName -c Release "-p:Platform=x64" --nologo "-m:1" --logger "trx;LogFileName=$($p.BaseName).trx" --results-directory (Join-Path $saida 'resultados')
    $codigoTeste = $LASTEXITCODE
    if ($codigoTeste -ne 0) { $falhasTeste += "$($p.BaseName) (código $codigoTeste)"; continue }
    if (-not (Test-Path -LiteralPath $trx)) { $falhasTeste += "$($p.BaseName) (sem TRX)"; continue }
    $resultadoTeste = ([xml](Get-Content -LiteralPath $trx -Raw)).TestRun.ResultSummary
    if ($resultadoTeste.outcome -ne 'Completed' -or [int]$resultadoTeste.Counters.executed -eq 0 -or [int]$resultadoTeste.Counters.failed -ne 0) {
        $falhasTeste += "$($p.BaseName) (resultado TRX inválido)"
    }
}
if ($falhasTeste.Count) { throw "Testes reprovados: $($falhasTeste -join ', '). Nenhuma versão foi publicada." }

# 1. Publicar
if (Test-Path -LiteralPath $publicacao) { Remove-Item -LiteralPath $publicacao -Recurse -Force }
# Carimbo da atualização automática (Atualizador.cs): aaaaMMddHHmmss; os PCs instalam a compilação mais nova publicada no GitHub.
$compilacao = Get-Date -Format 'yyyyMMddHHmmss'
& dotnet publish $projeto -c Release "-p:Platform=x64" -r win-x64 -o $publicacao --nologo "-p:CompilacaoAtualizacao=$compilacao"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }

# 2. A publicação deve ter um único executável (um .pdb é tolerado e descartado)
Get-ChildItem -LiteralPath $publicacao -Filter *.pdb -File | Remove-Item -Force
$arquivos = @(Get-ChildItem -LiteralPath $publicacao -Recurse -Force -File)
if ($arquivos.Count -ne 1 -or $arquivos[0].Extension -ne '.exe') {
    $lista = ($arquivos | ForEach-Object { $_.FullName.Substring($publicacao.Length + 1) }) -join ', '
    throw "A publicação deveria ter só o .exe, mas tem $($arquivos.Count) arquivo(s): $lista"
}
$exePublicado = $arquivos[0].FullName

# 3. Conferir o .exe publicado ANTES de tocar em Aplicativo\: é um PE (MZ) e tem tamanho plausível
$bytes = [System.IO.File]::ReadAllBytes($exePublicado)
if ($bytes.Length -lt 1MB -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "Executável publicado inválido: $exePublicado" }
$hashFinal = (Get-FileHash -LiteralPath $exePublicado -Algorithm SHA256).Hash

# 4. Autoteste do .exe publicado (o mesmo arquivo que vai para Aplicativo\, conferido por hash no passo 5): abre o .exe com
#    --autoteste por script externo aberto com explorer.exe (fora do pacote do Claude), em duas rodadas (1ª fria, 2ª quente).
#    Cada rodada grava um JSON em tests\saida; exige "ok": true (só gravado depois de a página carregar no WebView2) e "elevado": false.
$jsons = @((Join-Path $saida 'autoteste.json'), (Join-Path $saida 'autoteste-2.json'))
$logAuto = Join-Path $saida 'autoteste-log.txt'
foreach ($f in $jsons + $logAuto) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
[System.IO.File]::WriteAllText((Join-Path $saida 'autoteste-alvo.txt'), $exePublicado, (New-Object System.Text.UTF8Encoding($false)))
Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + (Join-Path $PSScriptRoot 'autoteste.cmd') + '"')
# O autoteste.ps1 ainda pode estar gravando o log: leitura presa conta como "ainda não terminou".
function AutotesteTerminou {
    if (-not (Test-Path -LiteralPath $logAuto)) { return $false }
    try { return [bool](Select-String -LiteralPath $logAuto -Pattern '^FIM' -Quiet -ErrorAction Stop) } catch { return $false }
}
$limite = (Get-Date).AddMinutes(5)
while ((Get-Date) -lt $limite) {
    if (AutotesteTerminou) { break }
    Start-Sleep -Milliseconds 500
}
if (-not (AutotesteTerminou)) { throw 'O autoteste não terminou em 5 min.' }
foreach ($f in $jsons) {
    if (-not (Test-Path -LiteralPath $f)) { throw "O autoteste não gravou ${f}: o executável não abriu." }
    $r = Get-Content -LiteralPath $f -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($r.ok -ne $true) { throw "Autoteste reprovou em ${f}: sem ok=true ($($r.erro))." }
    if ($r.elevado -ne $false) { throw "Autoteste reprovou em ${f}: o app abriu elevado (elevado=$($r.elevado))." }
}
Write-Host 'Autoteste do executável: aprovado (2 rodadas, ok=true, sem elevação).'
# 4b. PDF.js embutido (Legislação, desenho §11.4): o bloco "legislacao" do autoteste (versão, arquivos, bytes, sha conferido).
$leg = (Get-Content -LiteralPath $jsons[0] -Raw -Encoding UTF8 | ConvertFrom-Json).legislacao
if ($null -eq $leg -or $leg.sha_ok -ne $true) { throw 'Autoteste reprovou: o PDF.js embutido não confere com o VERSAO.json.' }
$ptBr = [System.Globalization.CultureInfo]::GetCultureInfo('pt-BR')
Write-Host ("PDF.js {0} embutido: {1} arquivos, {2} MB (sha conferido)." -f $leg.pdfjs, $leg.arquivos, ([double]$leg.bytes / 1000000).ToString('0.00', $ptBr))

# 5. Só agora Aplicativo\ muda: copia com nome temporário, confere (hash e MZ) e troca; os executáveis antigos saem por último
New-Item -ItemType Directory -Force -Path $pastaFinal | Out-Null
$exeTemp = Join-Path $pastaFinal 'novo.exe.tmp'
Copy-Item -LiteralPath $exePublicado -Destination $exeTemp -Force
$bt = [System.IO.File]::ReadAllBytes($exeTemp)
if ($bt.Length -ne $bytes.Length -or $bt[0] -ne 0x4D -or $bt[1] -ne 0x5A -or (Get-FileHash -LiteralPath $exeTemp -Algorithm SHA256).Hash -ne $hashFinal) {
    Remove-Item -LiteralPath $exeTemp -Force
    throw 'A cópia para Aplicativo\ difere do .exe publicado e autotestado; Aplicativo\ ficou como estava.'
}
# Com o editor aberto, o .exe antigo não pode ser sobrescrito, mas pode ser renomeado: ele sai do caminho e o novo entra; o antigo é
# apagado agora ou, se ainda estiver em uso, no próximo build.
if (Test-Path -LiteralPath $exeFinal) {
    $exeAntigo = Join-Path $pastaFinal ('antigo-' + [Guid]::NewGuid().ToString('N') + '.exe.old')
    Rename-Item -LiteralPath $exeFinal -NewName (Split-Path -Leaf $exeAntigo)
}
Move-Item -LiteralPath $exeTemp -Destination $exeFinal -Force
Get-ChildItem -LiteralPath $pastaFinal -Force | Where-Object { $_.FullName -ne $exeFinal } | ForEach-Object {
    try { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop }
    catch { Write-Host "Em uso (sai no próximo build): $($_.TargetObject)" }
}
if ((Get-FileHash -LiteralPath $exeFinal -Algorithm SHA256).Hash -ne $hashFinal) { throw "Arquivo final difere do publicado: $exeFinal" }

# Evidência vinculada ao arquivo efetivamente testado, exigida também na publicação manual.
$validacao = [ordered]@{ Compilacao = $compilacao; Sha256 = $hashFinal; Commit = $commitFonte; Testes = $projetosTeste.Count; Autoteste = $true }
$validacao | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $pastaFinal 'build-validada.json') -Encoding UTF8

# 5a. GitHub (smendesjunior/analises-pmvni): Release com o .exe e o manifesto, para a atualização automática dos outros PCs.
#     Uma falha aqui não desfaz o build: o .exe novo já está em Aplicativo\ e a publicação pode ser refeita com tools\publicar-github.ps1.
#     -SemGitHub pula a publicação (por exemplo, sem internet).
if ($SemGitHub) { Write-Host 'Publicação no GitHub pulada (-SemGitHub).' }
else {
    try { & (Join-Path $PSScriptRoot 'publicar-github.ps1') -Exe $exeFinal -Compilacao $compilacao }
    catch { Write-Warning "O .exe novo está em Aplicativo\, mas a publicação no GitHub ficou pendente: $($_.Exception.Message)" }
}

if ($SemLimpeza) {
    Write-Host "Executável validado: $exeFinal (compilação $compilacao, SHA-256 $hashFinal). Intermediários preservados para auditoria (-SemLimpeza)."
    return
}

# 5b. Limpeza: bin, obj, publicação e temporários dentro do projeto (caminhos conferidos sob $raiz)
& dotnet build-server shutdown | Out-Null
$alvos = @(Join-Path $raiz 'artifacts')
foreach ($pasta in 'src', 'tests') {
    foreach ($dir in Get-ChildItem -LiteralPath (Join-Path $raiz $pasta) -Directory) {
        $alvos += Join-Path $dir.FullName 'bin'
        $alvos += Join-Path $dir.FullName 'obj'
    }
}
$alvos += Join-Path $raiz 'web\paginador\node_modules\.cache'
# tests\saida só é apagada depois de ler tests\saida\limpeza.json (passo 6)
$liberado = 0
foreach ($alvo in $alvos) {
    $completo = [System.IO.Path]::GetFullPath($alvo)
    if (-not $completo.StartsWith($raiz + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Caminho fora do projeto: $completo" }
    if (-not (Test-Path -LiteralPath $completo)) { continue }
    $tamanho = Tamanho $completo
    Remove-Item -LiteralPath $completo -Recurse -Force
    if (Test-Path -LiteralPath $completo) { throw "Não foi possível apagar: $completo" }
    $liberado += $tamanho
}

# 6. Extrações antigas do executável único em %TEMP%\.net (AppData): limpas por script externo,
#    aberto com explorer.exe para rodar fora do pacote do Claude; o resultado volta em tests\saida\limpeza.json.
$resultadoLimpeza = Join-Path $raiz 'tests\saida\limpeza.json'
if (Test-Path -LiteralPath $resultadoLimpeza) { Remove-Item -LiteralPath $resultadoLimpeza -Force }
Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + (Join-Path $PSScriptRoot 'limpar-extracoes.cmd') + '"')
$limite = (Get-Date).AddMinutes(3)
while (-not (Test-Path -LiteralPath $resultadoLimpeza) -and (Get-Date) -lt $limite) { Start-Sleep -Milliseconds 500 }
if (-not (Test-Path -LiteralPath $resultadoLimpeza)) { throw "A limpeza das extrações não respondeu em 3 min ($resultadoLimpeza)." }
$limpeza = Get-Content -LiteralPath $resultadoLimpeza -Raw -Encoding UTF8 | ConvertFrom-Json
if (@($limpeza.erros).Count -gt 0) { throw "Limpeza das extrações com erros: $(@($limpeza.erros) -join '; ')" }

# 7. tests\saida (dados do app, JSONs, PDFs e capturas dos testes; nada disso é versionado)
$liberadoSaida = Tamanho $saida
$completo = [System.IO.Path]::GetFullPath($saida)
if (-not $completo.StartsWith($raiz + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Caminho fora do projeto: $completo" }
# Os testes de junção (mklink /J) podem deixar junções em tests\saida: o Remove-Item -Recurse do PowerShell 5.1 não as apaga ("a pasta não está
# vazia"). Primeiro sai só o link de cada uma (Directory.Delete sem recursão nunca entra no alvo), sem descer por elas. Pelo LinkType, não pelo
# atributo ReparsePoint: as pastas do OneDrive também o têm e não são atalhos.
$pilha = New-Object System.Collections.Stack
if (Test-Path -LiteralPath $completo) { $pilha.Push($completo) }
while ($pilha.Count -gt 0) {
    $dir = $pilha.Pop()
    foreach ($sub in [System.IO.Directory]::GetDirectories($dir)) {
        $tipo = (Get-Item -LiteralPath $sub -Force).LinkType
        if ($tipo -eq 'Junction' -or $tipo -eq 'SymbolicLink') { [System.IO.Directory]::Delete($sub) }
        else { $pilha.Push($sub) }
    }
}
Remove-Item -LiteralPath $completo -Recurse -Force
if (Test-Path -LiteralPath $completo) { throw "Não foi possível apagar: $completo" }
$liberado += $liberadoSaida

$mb = [math]::Round($bytes.Length / 1MB, 1)
Write-Host ''
Write-Host "Executável final: $exeFinal ($mb MB, SHA-256 $hashFinal)"
Write-Host ("Espaço liberado no projeto (bin, obj, publicação, tests\saida, caches): {0:N1} MB" -f ($liberado / 1MB))
$idAtual = if ($limpeza.idAtual) { $limpeza.idAtual } else { '(não identificado: todas apagadas)' }
Write-Host "Extrações em $($limpeza.base): id atual $idAtual; removidas $(@($limpeza.removidas).Count), mantidas $(@($limpeza.mantidas).Count)"
foreach ($r in @($limpeza.removidas)) { Write-Host "  removida: $r" }
Write-Host ("Espaço liberado nas extrações: {0:N1} MB" -f ([int64]$limpeza.bytesLiberados / 1MB))
Write-Host ("Espaço liberado no total: {0:N1} MB" -f (($liberado + [int64]$limpeza.bytesLiberados) / 1MB))
