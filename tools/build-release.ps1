<#
  Publica o Editor de Relatórios PMVNI como executável único em Aplicativo\ e limpa os builds.
  Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1
  Ordem: publicar -> conferir que a publicação tem um único executável -> copiar para Aplicativo\
         -> conferir o arquivo final -> só então apagar bin, obj, pasta de publicação e temporários
         -> abrir tools\limpar-extracoes.cmd (explorer.exe) para apagar as extrações antigas em %TEMP%\.net.
#>
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
& dotnet test (Join-Path $raiz 'EditorRelatoriosPMVNI.sln') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Os testes falharam (código $LASTEXITCODE): nenhuma versão foi publicada." }

# 1. Publicar
if (Test-Path -LiteralPath $publicacao) { Remove-Item -LiteralPath $publicacao -Recurse -Force }
& dotnet publish $projeto -c Release -o $publicacao --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou (código $LASTEXITCODE)." }

# 2. A publicação deve ter um único executável (um .pdb é tolerado e descartado)
Get-ChildItem -LiteralPath $publicacao -Filter *.pdb -File | Remove-Item -Force
$arquivos = @(Get-ChildItem -LiteralPath $publicacao -Recurse -Force -File)
if ($arquivos.Count -ne 1 -or $arquivos[0].Extension -ne '.exe') {
    $lista = ($arquivos | ForEach-Object { $_.FullName.Substring($publicacao.Length + 1) }) -join ', '
    throw "A publicação deveria ter só o .exe, mas tem $($arquivos.Count) arquivo(s): $lista"
}
$exePublicado = $arquivos[0].FullName

# 3. Copiar para Aplicativo\ (só o .exe final fica lá)
New-Item -ItemType Directory -Force -Path $pastaFinal | Out-Null
Copy-Item -LiteralPath $exePublicado -Destination $exeFinal -Force
Get-ChildItem -LiteralPath $pastaFinal -Force | Where-Object { $_.FullName -ne $exeFinal } | Remove-Item -Recurse -Force

# 4. Conferir o arquivo final: existe, é um PE (MZ) e é idêntico ao publicado
$bytes = [System.IO.File]::ReadAllBytes($exeFinal)
if ($bytes.Length -lt 1MB -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw "Arquivo final inválido: $exeFinal" }
if ((Get-FileHash -LiteralPath $exeFinal).Hash -ne (Get-FileHash -LiteralPath $exePublicado).Hash) { throw 'A cópia em Aplicativo\ difere do .exe publicado.' }
$hashFinal = (Get-FileHash -LiteralPath $exeFinal -Algorithm SHA256).Hash

# 4b. Autoteste: abre o .exe final com --autoteste, por script externo aberto com explorer.exe (fora do pacote do Claude).
#     Duas rodadas (1ª fria, 2ª quente); cada uma grava um JSON em tests\saida.
$jsons = @((Join-Path $saida 'autoteste.json'), (Join-Path $saida 'autoteste-2.json'))
$logAuto = Join-Path $saida 'autoteste-log.txt'
foreach ($f in $jsons + $logAuto) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force } }
Start-Process -FilePath 'explorer.exe' -ArgumentList ('"' + (Join-Path $PSScriptRoot 'autoteste.cmd') + '"')
$limite = (Get-Date).AddMinutes(5)
while ((Get-Date) -lt $limite) {
    if ((Test-Path -LiteralPath $logAuto) -and (Select-String -LiteralPath $logAuto -Pattern '^FIM' -Quiet)) { break }
    Start-Sleep -Milliseconds 500
}
if (-not ((Test-Path -LiteralPath $logAuto) -and (Select-String -LiteralPath $logAuto -Pattern '^FIM' -Quiet))) { throw 'O autoteste não terminou em 5 min.' }
foreach ($f in $jsons) {
    if (-not (Test-Path -LiteralPath $f)) { throw "O autoteste não gravou ${f}: o executável não abriu." }
    $r = Get-Content -LiteralPath $f -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($r.erro -or -not $r.webview2) { throw "Autoteste reprovou em $f." }
}
Write-Host 'Autoteste do executável: aprovado (2 rodadas).'

# 5. Limpeza: bin, obj, publicação e temporários dentro do projeto (caminhos conferidos sob $raiz)
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
