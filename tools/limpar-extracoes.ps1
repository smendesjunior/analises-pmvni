<#
  Apaga as extrações antigas do executável único em %TEMP%\.net\<nome do exe>\<id do bundle>,
  preservando as cópias em uso, a instalada e a build final. Roda FORA do pacote do Claude (aberto pelo limpar-extracoes.cmd via
  explorer.exe), porque %TEMP% fica em AppData. Resultado em tests\saida\limpeza.json.
  O id do bundle é lido do próprio .exe (cabeçalho do bundle .NET); se não der para lê-lo,
  preserva as extrações para a próxima limpeza.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$saida = Join-Path $raiz 'tests\saida\limpeza.json'
$base = if ($env:DOTNET_BUNDLE_EXTRACT_BASE_DIR) { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR } else { Join-Path $env:TEMP '.net' }
# Pasta da extração = nome do exe sem extensão; Pmvni.App = nome usado por builds de teste antigos.
$pastasApp = @((Join-Path $base ([IO.Path]::GetFileNameWithoutExtension($exe))), (Join-Path $base 'Pmvni.App'))

Add-Type -TypeDefinition @'
using System; using System.IO;
public static class BundleNet {
    // Assinatura do bundle .NET no apphost; os 8 bytes antes dela guardam o deslocamento do cabeçalho.
    static readonly byte[] Assinatura = {
        0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
        0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae };
    public static string Id(string arquivo) {
        byte[] b = File.ReadAllBytes(arquivo);
        for (int i = 8; i <= b.Length - Assinatura.Length; i++) {
            int j = 0;
            while (j < Assinatura.Length && b[i + j] == Assinatura[j]) j++;
            if (j < Assinatura.Length) continue;
            long cab = BitConverter.ToInt64(b, i - 8);
            if (cab <= 0 || cab >= b.Length) return null;
            using (var r = new BinaryReader(new MemoryStream(b))) {
                r.BaseStream.Position = cab + 12;   // major, minor, número de arquivos (3 x int32)
                return r.ReadString();             // id do bundle (prefixo de tamanho 7-bit)
            }
        }
        return null;
    }
}
'@

function IdDoBundle([string]$arquivo) {
    $id = [BundleNet]::Id($arquivo)
    if ($id -and $id -match '^[A-Za-z0-9_-]{8,32}$') { return $id } else { return $null }
}

$resultado = [ordered]@{ base = $base; idAtual = $null; mantidas = @(); removidas = @(); bytesLiberados = [int64]0; erros = @() }
try {
    if (Test-Path -LiteralPath $exe) { $resultado.idAtual = IdDoBundle $exe }
    $exeInstalado = Join-Path $env:LOCALAPPDATA 'Programs\Analise de Projetos PMVNI\Editor de Relatorios PMVNI.exe'
    $idInstalado = $null
    $instalado = Test-Path -LiteralPath $exeInstalado
    if ($instalado) { $idInstalado = IdDoBundle $exeInstalado }
    foreach ($pastaApp in $pastasApp) {
        if (-not (Test-Path -LiteralPath $pastaApp)) { continue }
        $pastaApp = (Get-Item -LiteralPath $pastaApp).FullName
        $nomeApp = Split-Path -Leaf $pastaApp
        foreach ($d in Get-ChildItem -LiteralPath $pastaApp -Directory -Force) {
            # O atualizador pode trocar o exe em disco enquanto a sessão aberta ainda usa outro bundle.
            if (-not $resultado.idAtual -or ($instalado -and -not $idInstalado) -or
                $d.Name -in @($resultado.idAtual, $idInstalado) -or
                $d.Name -notmatch '^[A-Za-z0-9_-]{8,32}$' -or $d.LinkType -or
                (Get-Process -Name $nomeApp -ErrorAction SilentlyContinue)) {
                $resultado.mantidas += $d.FullName
                continue
            }
            if ($d.Parent.FullName -ne $pastaApp) { throw "Extração fora da pasta do aplicativo: $($d.FullName)" }
            $tam = (Get-ChildItem -LiteralPath $d.FullName -Recurse -File -Force -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
            try {
                Remove-Item -LiteralPath $d.FullName -Recurse -Force
                $resultado.removidas += $d.FullName
                $resultado.bytesLiberados += [int64]$tam
            } catch { $resultado.erros += "$($d.FullName): $($_.Exception.Message)" }
        }
        if (-not (Get-ChildItem -LiteralPath $pastaApp -Force)) { Remove-Item -LiteralPath $pastaApp -Force }
    }
} catch { $resultado.erros += $_.Exception.Message }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $saida) | Out-Null
$json = $resultado | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText($saida + '.tmp', $json, (New-Object Text.UTF8Encoding $false))
Move-Item -LiteralPath ($saida + '.tmp') -Destination $saida -Force
