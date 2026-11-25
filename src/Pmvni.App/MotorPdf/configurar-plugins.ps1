# -Componente (Tarefa 21, cartao 'Plugins e ferramentas' dos Ajustes): so uma parte. analise = o plugin no Codex e no Claude Code;
# motores = PyMuPDF, Docling, Blueprint e FYP (instalam juntos); pmvni-pdf = o servidor de PDF e o registro dele nos programas.
# Sem -Componente, tudo (primeira abertura e botao 'Reparar instalação'). Uma parte so mantem no resumo as linhas das outras.
param([ValidateSet('','analise','motores','pmvni-pdf')][string]$Componente = '', [string]$KitLocal = '', [switch]$AtualizarKit,
      [string]$PastaPmvni = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PMVNI'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nativo.ps1')
if ($AtualizarKit) {
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
}
$tudo = -not $Componente
$fazMotores = $tudo -or $Componente -eq 'motores'
$fazPlugin = !$AtualizarKit -and ($tudo -or $Componente -eq 'analise')
$fazServidor = $tudo -or $Componente -eq 'pmvni-pdf'
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
[IO.Directory]::CreateDirectory($base) | Out-Null
$estado = [ordered]@{'PyMuPDF 2.1'='PENDENTE'}
$resumo = Join-Path $base 'resumo-instalacao.txt'
$erros = @()
# Confere a instalação efetiva com o verificador que o executor usa no pré-voo.
function Conferir-Plugin([string]$programa) {
    $origem = Join-Path $catalogo 'plugins\analise'
    $versao = (Get-Content -LiteralPath (Join-Path $origem '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
    if ($programa -eq 'Codex') {
        $raiz = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
        $plugin = Join-Path $raiz "plugins\cache\pmvni-kit\analise\$versao"
    } else {
        $raiz = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { Join-Path $env:USERPROFILE '.claude' }
        $registro = Join-Path $raiz 'plugins\installed_plugins.json'
        if (!(Test-Path -LiteralPath $registro)) { return $false }
        $instalados = (Get-Content -LiteralPath $registro -Raw -Encoding UTF8 | ConvertFrom-Json).plugins
        $entrada = @($instalados."analise@$mk" | Where-Object { $_.version -eq $versao -and $_.scope -eq 'user' }) | Select-Object -First 1
        if (!$entrada.installPath) { return $false }
        $plugin = $entrada.installPath
    }
    $skill = Join-Path $plugin 'skills\analise'
    if (!(Test-Path -LiteralPath $skill -PathType Container)) { return $false }
    if (!$python -or !(Test-Path -LiteralPath $python -PathType Leaf)) { throw 'Motor PDF indisponivel para conferir o plugin' }
    $verificador = Join-Path $origem 'skills\analise\scripts\check_kit_version.py'
    $resultado = & $python -B -X utf8 $verificador --kit-root $kit --skill-root $skill | Out-String | ConvertFrom-Json
    if ($resultado.status -eq 'CURRENT' -and $LASTEXITCODE -eq 0 -and $resultado.kit_plugin_copy) { return $true }
    if ($resultado.status -eq 'UPDATE_REQUIRED' -or @($resultado.different_skill_files | Where-Object { $_ }).Count) { return $false }
    throw ("Conferencia do kit para $programa inconclusiva: " + $resultado.status + ' ' + ($resultado.errors -join ' / '))
}
$mutex = [Threading.Mutex]::new($false,'Local\PMVNI-ConfigurarPlugins')
try {
    try { $entrou = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entrou = $true }
    if (!$entrou) { throw 'Outra instalacao esta em andamento.' }
    try {
        $reserva = [IO.File]::Open((Join-Path $env:USERPROFILE '.pmvni-kit-execucao.lock'),
            [IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    } catch [IO.IOException] { exit 4 }
    $falhasAnteriores = @()
    if ((!$tudo -or $AtualizarKit) -and (Test-Path -LiteralPath $resumo)) {
        foreach ($linha in [IO.File]::ReadAllLines($resumo,[Text.Encoding]::UTF8)) {
            $campos = @($linha.Trim() -split '    ',2)
            if ($campos.Count -ne 2) { continue }
            if ($campos[0] -eq 'FALHA') {
                $erro = $campos[1].Trim()
                if (($fazMotores -and $erro -like 'Motor *') -or ($fazServidor -and $erro -like 'MCP *') -or
                    ($fazPlugin -and $erro -like 'Plugin *')) { continue }
                if (!$tudo) { $falhasAnteriores += $erro }
            } else { $estado[$campos[0].Trim()] = $campos[1].Trim() }
        }
    }
    if ($fazMotores) { foreach ($k in 'PyMuPDF 2.1') { $estado[$k]='PENDENTE' } }
    if ($fazMotores) {
        foreach ($antiga in @($estado.Keys)) {
            if ($antiga -match '^(PyMuPDF |Docling MCP |Blueprint Pipeline|FYP-Floorplan)' -and $antiga -ne 'PyMuPDF 2.1') { $estado.Remove($antiga) }
        }
        try {
            & (Join-Path $PSScriptRoot 'instalar.ps1')
            $python = [IO.File]::ReadAllText((Join-Path $base 'runtime.txt'))
            Invocar-Nativo $python @('-I', '-c', "import pymupdf;assert pymupdf.VersionBind=='2.1'")

            $estado['PyMuPDF 2.1']='INSTALADO'
        } catch { $erros += 'Motor ' + $_.Exception.Message }
    }
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
            $origemErro = 'Plugin Codex'
            if ($fazPlugin) { Invocar-Nativo $codex @('plugin', 'marketplace', 'add', $catalogo) | Out-Null }

            foreach($n in @(if ($fazPlugin) {'analise'})){
                $estado["Codex / $n"]='PENDENTE'
                $r = Invocar-Nativo $codex @('plugin', 'add', "$n@pmvni-kit", '--json') |ConvertFrom-Json
                if(!$r.version){throw "Plugin $n"}
                if (!(Conferir-Plugin 'Codex')) {
                    Invocar-Nativo $codex @('plugin', 'remove', "$n@pmvni-kit") | Out-Null
                    $r = Invocar-Nativo $codex @('plugin', 'add', "$n@pmvni-kit", '--json') | ConvertFrom-Json
                    if (!(Conferir-Plugin 'Codex')) { throw 'Conteudo instalado ainda diverge do kit' }
                }
                $estado["Codex / $n"]="INSTALADO $($r.version)"
            }
            if ($fazServidor) {
                $origemErro = 'MCP Codex'
                Invocar-Nativo $codex @('mcp', 'add', 'pmvni-pdf', '--', $python, '-I', $servidor) | Out-Null

                $estado['Codex / ferramentas PDF']='CONFIGURADO'
            }
        } catch { $erros += "$origemErro - $($_.Exception.Message)" }
    }
    $claude = Get-Command claude.exe -ErrorAction SilentlyContinue
    if ($claude) { $estado.Remove('Claude Code') }
    if (!$claude) { $estado['Claude Code']='AUSENTE' } elseif ($fazPlugin -or $fazServidor) {
        try {
            $origemErro = 'Plugin Claude'
            if ($fazPlugin) {
                $mk=(Get-Content (Join-Path $catalogo '.claude-plugin\marketplace.json') -Raw|ConvertFrom-Json).name
                try { Invocar-Nativo $claude.Source @('plugin', 'marketplace', 'add', $catalogo, '--scope', 'user') | Out-Null
                } catch { Invocar-Nativo $claude.Source @('plugin', 'marketplace', 'update', $mk) | Out-Null }
                Invocar-Nativo $claude.Source @('plugin', 'install', "analise@$mk", '--scope', 'user') | Out-Null

                Invocar-Nativo $claude.Source @('plugin', 'update', "analise@$mk", '--scope', 'user') | Out-Null

                if (!(Conferir-Plugin 'Claude')) {
                    Invocar-Nativo $claude.Source @('plugin', 'uninstall', "analise@$mk", '--scope', 'user') | Out-Null
                    Invocar-Nativo $claude.Source @('plugin', 'install', "analise@$mk", '--scope', 'user') | Out-Null
                    if (!(Conferir-Plugin 'Claude')) { throw 'Conteudo instalado ainda diverge do kit' }
                }
                $estado['Claude Code / analise']='INSTALADO'
            }
            if ($fazServidor) {
                $origemErro = 'MCP Claude'
                $existente = try { Invocar-Nativo $claude.Source @('mcp', 'get', 'pmvni-pdf') | Out-String } catch { '' }
                if ($existente -notlike "*$python*" -or $existente -notlike "*$servidor*") {
                    if($existente -match 'Scope: User') { Invocar-Nativo $claude.Source @('mcp', 'remove', '--scope', 'user', 'pmvni-pdf') | Out-Null }
                    Invocar-Nativo $claude.Source @('mcp', 'add', '--scope', 'user', '--transport', 'stdio', 'pmvni-pdf', '--', $python, '-I', $servidor) | Out-Null

                }
                $estado['Claude Code / ferramentas PDF']='CONFIGURADO'
            }
        } catch { $erros += "$origemErro - $($_.Exception.Message)" }
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
        } catch {$erros += 'MCP Claude Desktop - ' + $_.Exception.Message}
    }
    $linhas = @($estado.Keys | ForEach-Object {"$_    $($estado[$_])"})
    $linhas += @(@($falhasAnteriores + $erros) | Select-Object -Unique | ForEach-Object {"FALHA    $_"})
    [IO.File]::WriteAllLines($resumo,[string[]]$linhas,[Text.Encoding]::UTF8)
    if ($fazMotores) { & (Join-Path $PSScriptRoot 'limpar.ps1') }
    $linhas | Write-Output
    if($erros.Count){throw 'Instalacao com pendencias. Consulte resumo-instalacao.txt.'}
} finally {if($reserva){$reserva.Dispose()};if($entrou){$mutex.ReleaseMutex()};$mutex.Dispose()}
exit 0
