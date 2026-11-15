<#
  Passo 0 da Tarefa 19, FORA do pacote do Claude (aberto pelo agentes-ajuda.cmd via explorer.exe): confere as opções dos
  programas instalados, sem pedir nada a nenhum modelo. Só --version, --help, "login status", "auth status".
  Grava tests\saida\agentes-ajuda.txt (ignorado pelo git). Do login/auth só entram o código de saída e dados não secretos.
#>
$ErrorActionPreference = 'Continue'
$raiz = Split-Path -Parent $PSScriptRoot
$saida = Join-Path $raiz 'tests\saida'
New-Item -ItemType Directory -Force -Path $saida | Out-Null
$arq = Join-Path $saida 'agentes-ajuda.txt'
$sb = New-Object System.Text.StringBuilder
function Linha([string]$t) { [void]$sb.AppendLine($t) }

# Variáveis de chave nunca vão para estes comandos.
foreach ($v in 'OPENAI_API_KEY', 'CODEX_API_KEY', 'ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN') { Remove-Item -LiteralPath "Env:$v" -ErrorAction SilentlyContinue }

function Rodar([string]$exe, [string[]]$args2) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $exe
  $psi.Arguments = ($args2 -join " ")  # Windows PowerShell 5.1 nao tem ArgumentList; os argumentos aqui nao tem espacos
  $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
  $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.RedirectStandardInput = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $p.StandardInput.Close()
  $o = $p.StandardOutput.ReadToEndAsync(); $e = $p.StandardError.ReadToEndAsync()
  if (-not $p.WaitForExit(30000)) { try { $p.Kill() } catch {}; return @{ Codigo = -1; Saida = 'nao respondeu em 30 s' } }
  return @{ Codigo = $p.ExitCode; Saida = ($o.Result + $e.Result) }
}

# codex.exe: o mais novo de %LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe; senão o do PATH.
$codex = Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin\*\codex.exe') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $codex) { $c = Get-Command codex.exe -ErrorAction SilentlyContinue; if ($c) { $codex = $c.Source } }
$claude = $null; $c = Get-Command claude.exe -ErrorAction SilentlyContinue; if ($c) { $claude = $c.Source }
Linha "codex.exe: $codex"
Linha "claude.exe: $claude"
Linha ''

if ($codex) {
  $r = Rodar $codex @('--version'); Linha "== codex --version (codigo $($r.Codigo))"; Linha $r.Saida
  $r = Rodar $codex @('exec', '--help'); Linha "== codex exec --help (codigo $($r.Codigo))"; Linha $r.Saida
  $r = Rodar $codex @('login', 'status'); $primeira = ($r.Saida -split "`r?`n" | Select-Object -First 1)
  Linha "== codex login status: codigo $($r.Codigo); primeira linha contem 'Logged in': $($primeira -like '*Logged in*')"
  Linha ''
}
if ($claude) {
  $r = Rodar $claude @('--version'); Linha "== claude --version (codigo $($r.Codigo))"; Linha $r.Saida
  $r = Rodar $claude @('--help'); Linha "== claude --help (codigo $($r.Codigo))"; Linha $r.Saida
  $r = Rodar $claude @('auth', 'status')
  Linha "== claude auth status: codigo $($r.Codigo)"
  try {
    $j = $r.Saida | ConvertFrom-Json
    Linha ('chaves: ' + (($j.PSObject.Properties.Name) -join ', '))
    Linha "loggedIn=$($j.loggedIn) authMethod=$($j.authMethod)"
  } catch { Linha 'saida nao e JSON (conteudo omitido)' }
  Linha ''
}
$py = Get-Command py.exe -ErrorAction SilentlyContinue
if ($py) { $r = Rodar $py.Source @('-3', '--version'); Linha "== py -3 --version (codigo $($r.Codigo)): $($r.Saida.Trim())" }
$pyx = Get-Command python.exe -ErrorAction SilentlyContinue
if ($pyx) { $r = Rodar $pyx.Source @('--version'); Linha "== python --version ($($pyx.Source)) (codigo $($r.Codigo)): $($r.Saida.Trim())" }
[System.IO.File]::WriteAllText($arq, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

