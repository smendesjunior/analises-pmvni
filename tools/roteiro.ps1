<#
  Roteiro da tela (Etapa 2), FORA do pacote do Claude (aberto pelo roteiro.cmd via explorer.exe; o roteiro-e-esperar.ps1 abre e espera).
  Publica o agente falso (tests\AgenteFalso, em tests\saida\agente-falso; faz o papel do codex, do claude e do python nos passos 49 em diante) e roda o executável publicado com tests\corpus\longo.json e --roteiro: o app exercita o shell e o módulo Relatórios (importar para
  .pmvrel, rolagem, zoom, navegação, exportação, estouro, tema, campos em aberto, pacote mudado, faixa por módulo, reabrir) e o
  resultado vai para docs\roteiro-etapa-2.resultado.json. Pacotes, capturas PNG e PDFs ficam em tests\saida\roteiro (ignorado pelo
  git); dados do app (WebView2, ajustes.json, edicao) em tests\saida\dados-app. Ao terminar grava tests\saida\roteiro-fim.txt.
#>
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
$env:PMVNI_DADOS_DIR = Join-Path $saida 'dados-app'
$pasta = Join-Path $saida 'roteiro'
$resultado = Join-Path $pasta 'resultado.json'
$final = Join-Path $raiz 'docs\roteiro-etapa-2.resultado.json'
New-Item -ItemType Directory -Force -Path $pasta | Out-Null
if (Test-Path -LiteralPath $resultado) { Remove-Item -LiteralPath $resultado -Force }
# Agente falso (Tarefa 19, passos 49 e 50 em diante): faz o papel do codex.exe, do claude.exe e do python.exe, sem rede e sem IA. O roteiro NUNCA toca o
# codex nem o claude de verdade (ExecucaoProcesso.SomenteExecutaveisEm recusa qualquer programa fora desta pasta).
$agenteFalso = Join-Path $saida 'agente-falso'
& dotnet publish (Join-Path $raiz 'tests\AgenteFalso\AgenteFalso.csproj') -c Release -o $agenteFalso --nologo | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $agenteFalso 'AgenteFalso.exe'))) {
    "roteiro: o agente falso não foi publicado (dotnet publish, código $LASTEXITCODE); o app não foi aberto." | Set-Content (Join-Path $saida 'roteiro-fim.txt') -Encoding utf8
    exit 1
}
$p = Start-Process -FilePath $exe -ArgumentList ('"' + (Join-Path $raiz 'tests\corpus\longo.json') + '"'), '--roteiro', ('"' + $resultado + '"') -PassThru -Wait
# O app só grava na pasta do JSON de saída (tests\saida\roteiro); o resultado é copiado para docs\ aqui sem nada do computador (o
# arquivo é versionado): a pasta de dados vira <dados>, a do kit <kit>, a raiz do repositório ".", o perfil do Windows %USERPROFILE% e
# os nomes do usuário e do computador <usuario> e <maquina> (onda 1, item 6). Cada caminho é trocado como no JSON (\ dobrada), puro e com /.
function Anonimizar([string]$texto) {
    $kit = if ($env:PMVNI_KIT_RAIZ) { $env:PMVNI_KIT_RAIZ } else { Join-Path $env:USERPROFILE 'OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)' }
    $curto = try { (New-Object -ComObject Scripting.FileSystemObject).GetFolder($env:USERPROFILE).ShortPath } catch { $null }
    $trocas = [ordered]@{}
    $trocas[$env:PMVNI_DADOS_DIR] = '<dados>'
    $trocas[$kit] = '<kit>'
    $trocas[$raiz] = '.'
    $trocas[$env:USERPROFILE] = '%USERPROFILE%'
    if ($curto -and $curto -ne $env:USERPROFILE) { $trocas[$curto] = '%USERPROFILE%' }
    foreach ($de in @($trocas.Keys)) {
        $por = $trocas[$de].Replace('$', '$$')
        foreach ($forma in @($de.Replace('\', '\\'), $de, $de.Replace('\', '/'))) {
            $texto = [regex]::Replace($texto, [regex]::Escape($forma), $por, 'IgnoreCase')
        }
    }
    if ($env:USERNAME) { $texto = [regex]::Replace($texto, [regex]::Escape($env:USERNAME), '<usuario>', 'IgnoreCase') }
    if ($env:COMPUTERNAME) { $texto = [regex]::Replace($texto, [regex]::Escape($env:COMPUTERNAME), '<maquina>', 'IgnoreCase') }
    return $texto
}
if (Test-Path -LiteralPath $resultado) {
    $json = Anonimizar ([System.IO.File]::ReadAllText($resultado, [System.Text.Encoding]::UTF8))
    [System.IO.File]::WriteAllText($final, $json, (New-Object System.Text.UTF8Encoding($false)))
}
"roteiro: código de saída $($p.ExitCode); resultado em $final" | Set-Content (Join-Path $saida 'roteiro-fim.txt') -Encoding utf8
