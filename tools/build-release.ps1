<#
  Publica o Editor de Relatórios PMVNI como executável único em Aplicativo\ e limpa os builds.
  Uso: powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1
  Ordem: publicar -> conferir que a publicação tem um único executável -> copiar para Aplicativo\
         -> conferir o arquivo final -> só então apagar bin, obj, pasta de publicação e temporários.
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

# 5. Limpeza: bin, obj, publicação e temporários dentro do projeto (caminhos conferidos sob $raiz)
& dotnet build-server shutdown | Out-Null
$alvos = @(Join-Path $raiz 'artifacts')
foreach ($dir in Get-ChildItem -LiteralPath (Join-Path $raiz 'src') -Directory) {
    $alvos += Join-Path $dir.FullName 'bin'
    $alvos += Join-Path $dir.FullName 'obj'
}
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

$mb = [math]::Round($bytes.Length / 1MB, 1)
Write-Host ''
Write-Host "Executável final: $exeFinal ($mb MB, SHA-256 $hashFinal)"
Write-Host ("Espaço liberado na limpeza: {0:N1} MB" -f ($liberado / 1MB))
