$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
[IO.Directory]::CreateDirectory($base) | Out-Null
$estado = [ordered]@{'Blueprint Pipeline'='PENDENTE';'FYP-Floorplan'='PENDENTE';'Docling MCP 3.5.0'='PENDENTE';'PyMuPDF 2.1'='PENDENTE'}
$erros = @()
$mutex = [Threading.Mutex]::new($false,'Local\PMVNI-ConfigurarPlugins')
try {
    try { $entrou = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
    if (!$entrou) { throw 'Outra instalacao esta em andamento.' }
    try {
        & (Join-Path $PSScriptRoot 'instalar.ps1')
        $python = [IO.File]::ReadAllText((Join-Path $base 'runtime.txt'))
        & $python -I -c "import pymupdf;assert pymupdf.VersionBind=='2.1'"
        if ($LASTEXITCODE) { throw 'PyMuPDF indisponivel' }
        $estado['PyMuPDF 2.1']='INSTALADO'
        & $python -I -c "from importlib.metadata import version;from docling_mcp.tools.converters.local import LOCAL_CONVERSION_AVAILABLE;assert LOCAL_CONVERSION_AVAILABLE and version('docling-mcp')=='3.5.0'"
        if ($LASTEXITCODE) { throw 'Docling indisponivel' }
        $estado['Docling MCP 3.5.0']='INSTALADO'
        & (Join-Path $PSScriptRoot 'instalar-visao.ps1') | ForEach-Object {
            if ($_ -eq 'blueprint INSTALADO') {$estado['Blueprint Pipeline']='INSTALADO'}
            if ($_ -eq 'fyp-floorplan INSTALADO') {$estado['FYP-Floorplan']='INSTALADO'}
        }
    } catch { $erros += $_.Exception.Message }
    $cfg = Join-Path $env:USERPROFILE '.codex\pmvni-kit.json'
    $kit = if (Test-Path $cfg) {(Get-Content $cfg -Raw -Encoding UTF8|ConvertFrom-Json).kit_root}
    if (!$kit) {$kit='\\srv-fileserver.PMVNI.PREF\pmvni\Obras\Engenharia\KIT DE TREINAMENTO DE IA (PMVNI)'}
    $catalogo = Join-Path $kit '01 - PLUGINS'
    $servidor = Join-Path $base 'mcp_pmvni.py'
    Copy-Item (Join-Path $PSScriptRoot 'mcp_pmvni.py') $servidor -Force
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
    if (!$codex) {$estado['Codex']='AUSENTE'} else {
        try {
            & $codex plugin marketplace add $catalogo | Out-Null
            if ($LASTEXITCODE) { throw 'Catalogo Codex' }
            foreach($n in 'analise','analise-pro','analise-google'){
                $estado["Codex / $n"]='PENDENTE'
                $r = & $codex plugin add "$n@pmvni-kit" --json |ConvertFrom-Json
                if($LASTEXITCODE -or !$r.version){throw "Plugin $n"}
                $estado["Codex / $n"]="INSTALADO $($r.version)"
            }
            & $codex mcp add pmvni-pdf -- $python -I $servidor | Out-Null
            if($LASTEXITCODE){throw 'MCP Codex'}
            $estado['Codex / ferramentas PDF']='CONFIGURADO'
        } catch { $erros += $_.Exception.Message }
    }
    $claude = Get-Command claude.exe -ErrorAction SilentlyContinue
    if (!$claude) { $estado['Claude Code']='AUSENTE' } else {
        try {
            $mk=(Get-Content (Join-Path $catalogo '.claude-plugin\marketplace.json') -Raw|ConvertFrom-Json).name
            & $claude.Source plugin marketplace add $catalogo --scope user | Out-Null
            if($LASTEXITCODE){ & $claude.Source plugin marketplace update $mk | Out-Null; if($LASTEXITCODE){throw 'Catalogo Claude'} }
            & $claude.Source plugin install "analise@$mk" --scope user | Out-Null
            if($LASTEXITCODE){throw 'Plugin Claude'}
            & $claude.Source plugin update "analise@$mk" --scope user | Out-Null
            if($LASTEXITCODE){throw 'Atualizacao Claude'}
            $estado['Claude Code / analise']='INSTALADO'
            $existente = (& $claude.Source mcp get pmvni-pdf | Out-String)
            if ($existente -notlike "*$python*" -or $existente -notlike "*$servidor*") {
                if($existente -match 'Scope: User') { & $claude.Source mcp remove --scope user pmvni-pdf | Out-Null }
                & $claude.Source mcp add --scope user --transport stdio pmvni-pdf -- $python -I $servidor | Out-Null
                if($LASTEXITCODE){throw 'MCP Claude Code'}
            }
            $estado['Claude Code / ferramentas PDF']='CONFIGURADO'
        } catch { $erros += $_.Exception.Message }
    }
    $pastas = @(Join-Path $env:APPDATA 'Claude')
    $pastas += @(Get-ChildItem "$env:LOCALAPPDATA\Packages\Claude*\LocalCache\Roaming\Claude" -Directory -ErrorAction SilentlyContinue | ForEach-Object FullName)
    foreach($pasta in $pastas | Select-Object -Unique){
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
    [IO.File]::WriteAllLines((Join-Path $base 'resumo-instalacao.txt'),[string[]]$linhas,[Text.Encoding]::UTF8)
    $linhas | Write-Output
    if($erros.Count){throw 'Instalacao com pendencias. Consulte resumo-instalacao.txt.'}
} finally {if($entrou){$mutex.ReleaseMutex()};$mutex.Dispose()}
