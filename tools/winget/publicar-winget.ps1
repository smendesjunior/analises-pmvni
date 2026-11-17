# Pacote SidneyJunior.EditorRelatoriosPMVNI (moniker editor-pmvni) no repositório oficial do winget (microsoft/winget-pkgs).
#   -ManifestDir <pasta>      primeiro envio: valida (winget validate) e envia os manifestos dessa pasta
#   -Version X.Y.Z -Tag <tag> atualização: wingetcreate update com o .exe da Release <tag> do GitHub
# O token do GitHub CLI vai só para o processo do wingetcreate, pela variável WINGET_CREATE_GITHUB_TOKEN.
param([string]$Version, [string]$Tag, [string]$ManifestDir)
$ErrorActionPreference = 'Stop'
$id = 'SidneyJunior.EditorRelatoriosPMVNI'
$repo = 'smendesjunior/analises-pmvni'
$wc = (Get-Command wingetcreate -ErrorAction SilentlyContinue).Source
if (-not $wc) { throw 'wingetcreate não encontrado (winget install Microsoft.WingetCreate).' }
$token = (& gh auth token).Trim()
if (-not $token) { throw 'GitHub CLI sem login (gh auth login).' }
$env:WINGET_CREATE_GITHUB_TOKEN = $token
try {
    if ($ManifestDir) {
        & winget validate --manifest $ManifestDir; if ($LASTEXITCODE) { throw 'Manifestos inválidos.' }
        $v = (Select-String -Path (Join-Path $ManifestDir "$id.yaml") -Pattern '^PackageVersion:\s*(.+)$').Matches[0].Groups[1].Value.Trim()
        $out = & $wc submit --no-open --prtitle "New package: $id version $v" $ManifestDir 2>&1
    } elseif ($Version -and $Tag) {
        $url = "https://github.com/$repo/releases/download/$Tag/EditorRelatoriosPMVNI.exe"
        $out = & $wc update $id --version $Version --urls $url --release-notes-url "https://github.com/$repo/releases/tag/$Tag" --submit --no-open --prtitle "Update $id to $Version" 2>&1
    } else { throw 'Informe -ManifestDir (primeiro envio) ou -Version e -Tag (atualização).' }
    if ($LASTEXITCODE) { throw ($out -join ' ') }
    $pr = @($out | Select-String -Pattern 'https://github\.com/microsoft/winget-pkgs/pull/\d+' -AllMatches | % { $_.Matches.Value })[0]
    Write-Host ("Pedido do winget enviado: " + $(if ($pr) { $pr } else { $out -join ' ' }))
} catch { throw ($_.Exception.Message -replace [regex]::Escape($token), '***') }
finally { $token = $null; Remove-Item Env:WINGET_CREATE_GITHUB_TOKEN -ErrorAction SilentlyContinue }
