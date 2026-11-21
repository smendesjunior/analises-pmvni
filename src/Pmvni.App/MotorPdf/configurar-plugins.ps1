# -Componente (Tarefa 21, cartao 'Plugins e ferramentas' dos Ajustes): so uma parte. analise = o plugin no Codex e no Claude Code;
# motores = PyMuPDF, Docling, Blueprint e FYP (instalam juntos); pmvni-pdf = o servidor de PDF e o registro dele nos programas.
# Sem -Componente, tudo (primeira abertura e botao 'Reparar instalação'). Uma parte so mantem no resumo as linhas das outras.
param([ValidateSet('','analise','motores','pmvni-pdf')][string]$Componente = '', [string]$KitLocal = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nativo.ps1')
$tudo = -not $Componente
$fazMotores = $tudo -or $Componente -eq 'motores'
$fazPlugin = $tudo -or $Componente -eq 'analise'
$fazServidor = $tudo -or $Componente -eq 'pmvni-pdf'
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
[IO.Directory]::CreateDirectory($base) | Out-Null
$estado = [ordered]@{'Blueprint Pipeline'='PENDENTE';'FYP-Floorplan'='PENDENTE';'Docling MCP 3.5.0'='PENDENTE';'PyMuPDF 2.1'='PENDENTE'}
$resumo = Join-Path $base 'resumo-instalacao.txt'
if (!$tudo -and (Test-Path -LiteralPath $resumo)) {
    foreach ($l in [IO.File]::ReadAllLines($resumo,[Text.Encoding]::UTF8)) {
        $c = @($l.Trim() -split '    ' | Where-Object { $_.Trim() })
        if ($c.Count -ge 2 -and $c[0] -ne 'FALHA') { $estado[$c[0].Trim()] = $c[-1].Trim() }
    }
}
$erros = @()
$mutex = [Threading.Mutex]::new($false,'Local\PMVNI-ConfigurarPlugins')
try {
    try { $entrou = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
    if (!$entrou) { throw 'Outra instalacao esta em andamento.' }
    if ($fazMotores) { foreach ($k in 'Blueprint Pipeline','FYP-Floorplan','Docling MCP 3.5.0','PyMuPDF 2.1') { $estado[$k]='PENDENTE' } }
    if ($fazMotores) { try {
        & (Join-Path $PSScriptRoot 'instalar.ps1')
        $python = [IO.File]::ReadAllText((Join-Path $base 'runtime.txt'))
        Invocar-Nativo $python @('-I', '-c', "import pymupdf;assert pymupdf.VersionBind=='2.1'")

        $estado['PyMuPDF 2.1']='INSTALADO'
        Invocar-Nativo $python @('-I', '-c', "from importlib.metadata import version;from docling_mcp.tools.converters.local import LOCAL_CONVERSION_AVAILABLE;assert LOCAL_CONVERSION_AVAILABLE and version('docling-mcp')=='3.5.0'")

        $estado['Docling MCP 3.5.0']='INSTALADO'
        & (Join-Path $PSScriptRoot 'instalar-visao.ps1') | ForEach-Object {
            if ($_ -eq 'blueprint INSTALADO') {$estado['Blueprint Pipeline']='INSTALADO'}
            if ($_ -eq 'fyp-floorplan INSTALADO') {$estado['FYP-Floorplan']='INSTALADO'}
        }
    } catch { $erros += $_.Exception.Message } }
    $runtime = Join-Path $base 'runtime.txt'
    if (!$python -and (Test-Path -LiteralPath $runtime)) { $python = [IO.File]::ReadAllText($runtime) }
    if ($fazServidor -and !$fazMotores -and !$python) { $erros += 'MCP sem motor PDF: atualize os motores de PDF primeiro'; $fazServidor = $false }
    $cfg = Join-Path $env:USERPROFILE '.codex\pmvni-kit.json'
    $kit = if ($KitLocal) { $KitLocal } elseif (Test-Path $cfg) {(Get-Content $cfg -Raw -Encoding UTF8|ConvertFrom-Json).kit_root}
    if (!$kit) {$kit='\\srv-fileserver.PMVNI.PREF\pmvni\Obras\Engenharia\KIT DE TREINAMENTO DE IA (PMVNI)'}
    $catalogo = Join-Path $kit '01 - PLUGINS'
    $servidor = Join-Path $base 'mcp_pmvni.py'
    if ($fazServidor) { Copy-Item (Join-Path $PSScriptRoot 'mcp_pmvni.py') $servidor -Force }
    $codex = (Get-Command codex.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (!$codex) {
        $pacote = Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($pacote.InstallLocation) {
            $cli = Join-Path $pacote.InstallLocation 'app\resources\codex.exe'
            if (Test-Path -LiteralPath $cli -PathType Leaf) { $codex = $cli }
        }
    }
    if (!$codex) {
        $codex = (Get-ChildItem "$env:LOCALAPPDATA\OpenAI\Codex\bin\*\codex.exe" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
    }
    if ($codex) { $estado.Remove('Codex') }
    if (!$codex) {$estado['Codex']='AUSENTE'} elseif ($fazPlugin -or $fazServidor) {
        try {
            Invocar-Nativo $codex @('plugin', 'marketplace', 'add', $catalogo) | Out-Null

            foreach($n in @(if ($fazPlugin) {'analise'})){
                $estado["Codex / $n"]='PENDENTE'
                $r = Invocar-Nativo $codex @('plugin', 'add', "$n@pmvni-kit", '--json') |ConvertFrom-Json
                if(!$r.version){throw "Plugin $n"}
                $estado["Codex / $n"]="INSTALADO $($r.version)"
            }
            if ($fazServidor) {
                Invocar-Nativo $codex @('mcp', 'add', 'pmvni-pdf', '--', $python, '-I', $servidor) | Out-Null

                $estado['Codex / ferramentas PDF']='CONFIGURADO'
            }
        } catch { $erros += $_.Exception.Message }
    }
    $claude = Get-Command claude.exe -ErrorAction SilentlyContinue
    if ($claude) { $estado.Remove('Claude Code') }
    if (!$claude) { $estado['Claude Code']='AUSENTE' } elseif ($fazPlugin -or $fazServidor) {
        try {
            $mk=(Get-Content (Join-Path $catalogo '.claude-plugin\marketplace.json') -Raw|ConvertFrom-Json).name
            try { Invocar-Nativo $claude.Source @('plugin', 'marketplace', 'add', $catalogo, '--scope', 'user') | Out-Null
            } catch { Invocar-Nativo $claude.Source @('plugin', 'marketplace', 'update', $mk) | Out-Null }
            if ($fazPlugin) {
                Invocar-Nativo $claude.Source @('plugin', 'install', "analise@$mk", '--scope', 'user') | Out-Null

                Invocar-Nativo $claude.Source @('plugin', 'update', "analise@$mk", '--scope', 'user') | Out-Null

                $estado['Claude Code / analise']='INSTALADO'
            }
            if ($fazServidor) {
                $existente = try { Invocar-Nativo $claude.Source @('mcp', 'get', 'pmvni-pdf') | Out-String } catch { '' }
                if ($existente -notlike "*$python*" -or $existente -notlike "*$servidor*") {
                    if($existente -match 'Scope: User') { Invocar-Nativo $claude.Source @('mcp', 'remove', '--scope', 'user', 'pmvni-pdf') | Out-Null }
                    Invocar-Nativo $claude.Source @('mcp', 'add', '--scope', 'user', '--transport', 'stdio', 'pmvni-pdf', '--', $python, '-I', $servidor) | Out-Null

                }
                $estado['Claude Code / ferramentas PDF']='CONFIGURADO'
            }
        } catch { $erros += $_.Exception.Message }
    }
    $pastas = @(Join-Path $env:APPDATA 'Claude')
    $pastas += @(Get-ChildItem "$env:LOCALAPPDATA\Packages\Claude*\LocalCache\Roaming\Claude" -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
    foreach($pasta in @(if ($fazServidor) { $pastas | Select-Object -Unique })){
        if(!(Test-Path -LiteralPath $pasta)){continue}
        try {
            $arquivo=Join-Path $pasta 'claude_desktop_config.json'
            $c=if(Test-Path $arquivo){Get-Content $arquivo -Raw -Encoding UTF8|ConvertFrom-Json}else{[pscustomobject]@{}}
            if(!$c.mcpServers){$c|Add-Member NoteProperty mcpServers ([pscustomobject]@{}) -Force}
            $c.mcpServers|Add-Member NoteProperty 'pmvni-pdf' ([ordered]@{command=$python;args=@('-I',$servidor)}) -Force
            $tmp="$arquivo.$([guid]::NewGuid()).tmp"
            [IO.File]::WriteAllText($tmp,($c|ConvertTo-Json -Depth 100))
            if(Test-Path $arquivo){[IO.File]::Replace($tmp,$arquivo,($arquivo+'.pmvni-anterior'))}else{[IO.File]::Move($tmp,$arquivo)}
            $estado['Claude Desktop / ferramentas PDF']='CONFIGURADO'
        } catch {$erros += $_.Exception.Message}
    }
    $linhas = @($estado.Keys | ForEach-Object {"$_    $($estado[$_])"})
    $linhas += @($erros | ForEach-Object {"FALHA    $_"})
    [IO.File]::WriteAllLines($resumo,[string[]]$linhas,[Text.Encoding]::UTF8)
    if ($fazMotores) { & (Join-Path $PSScriptRoot 'limpar.ps1') }
    $linhas | Write-Output
    if($erros.Count){throw 'Instalacao com pendencias. Consulte resumo-instalacao.txt.'}
} finally {if($entrou){$mutex.ReleaseMutex()};$mutex.Dispose()}
exit 0
