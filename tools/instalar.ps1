<#
  Instala o Editor de Relatórios PMVNI (Análise de Projetos – PMVNI) neste usuário, sem admin. Abra tools\instalar.cmd pelo explorer.exe
  (fora do pacote do Claude).
  A cópia de USO fica separada da cópia de desenvolvimento (2026-11-16):
    - cópia de uso: %LOCALAPPDATA%\Programs\Analise de Projetos PMVNI\Editor de Relatorios PMVNI.exe, baixada da Release mais recente do
      GitHub (smendesjunior/analises-pmvni, SHA-256 conferido) e atualizada sozinha pelo GitHub; usa os dados de sempre
      (%APPDATA% e %LOCALAPPDATA%\EditorRelatoriosPMVNI: ajustes, caminhos, pasta QGIS, leis);
    - cópia de desenvolvimento: Aplicativo\ do projeto (gerada pelo build-release), com dados próprios e vazios
      (%LOCALAPPDATA%\EditorRelatoriosPMVNI-Desenvolvimento) e sem atualização automática.
  Passos:
    1. baixa a Release do GitHub para a pasta da cópia de uso (sem internet: copia Aplicativo\, que depois se atualiza pelo GitHub);
    2. cria os atalhos no Menu Iniciar e na Área de Trabalho apontando para a cópia de uso;
    3. associa .rpmvni (e o formato antigo .pmvrel) à cópia de uso (HKCU\Software\Classes);
    4. atualiza o kit PMVNI (INSTALAR_KIT_PMVNI.cmd atualizar: plugin analise; o despacho é do próprio editor);
    5. abre o editor anexando as leis do kit (03 - LEGISLACAO) à Legislação (as já anexadas não se repetem).
  Resultado em tools\instalar-resultado.txt.
#>
param([switch]$SemKit, [switch]$SemLeis)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$raiz = Split-Path -Parent $PSScriptRoot
$exeDesenvolvimento = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$pastaUso = Join-Path $env:LOCALAPPDATA 'Programs\Analise de Projetos PMVNI'
$exe = Join-Path $pastaUso 'Editor de Relatorios PMVNI.exe'
$manifestoUrl = 'https://github.com/smendesjunior/analises-pmvni/releases/latest/download/EditorRelatoriosPMVNI-update.json'
$base = Join-Path $env:USERPROFILE 'OneDrive\PMVNI\SIDNEY JUNIOR'
$kit = Join-Path $base 'KIT DE TREINAMENTO DE IA (PMVNI)'
$saida = Join-Path $PSScriptRoot 'instalar-resultado.txt'
$log = New-Object System.Collections.Generic.List[string]
function Registrar([string]$t) { $log.Add((Get-Date -Format 'HH:mm:ss') + ' ' + $t) }

try {
    $nome = 'Análise de Projetos – PMVNI'
    New-Item -ItemType Directory -Force -Path $pastaUso | Out-Null

    # 1. Cópia de uso a partir do GitHub (a cópia aberta é fechada antes da troca).
    Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -ieq $exe } | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep 3; if (-not $_.HasExited) { $_.Kill() } }
    $temp = "$exe.baixando"
    try {
        $m = Invoke-RestMethod -Uri $manifestoUrl -UseBasicParsing -TimeoutSec 60
        if ($m.Url -notmatch '^https://github\.com/smendesjunior/analises-pmvni/releases/download/') { throw "Endereço inesperado no manifesto: $($m.Url)" }
        Invoke-WebRequest -Uri $m.Url -OutFile $temp -UseBasicParsing -TimeoutSec 900
        $hash = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash
        if ($hash -ne $m.Sha256) { throw "SHA-256 do download não confere ($hash, esperado $($m.Sha256))." }
        Move-Item -LiteralPath $temp -Destination $exe -Force
        Registrar "cópia de uso: Release $($m.Version) / compilação $($m.Build) baixada do GitHub em $exe (SHA-256 conferido)"
    }
    catch {
        Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue
        Registrar "GitHub indisponível ($($_.Exception.Message))"
        if (Test-Path -LiteralPath $exe) { Registrar 'mantida a cópia de uso que já existia (ela se atualiza sozinha pelo GitHub)' }
        elseif (Test-Path -LiteralPath $exeDesenvolvimento) {
            Copy-Item -LiteralPath $exeDesenvolvimento -Destination $exe -Force
            Registrar "cópia de uso: copiada de $exeDesenvolvimento (vai se atualizar pelo GitHub)"
        }
        else { throw 'Sem a Release do GitHub e sem o Aplicativo\ local: não há executável para instalar.' }
    }

    # 2. Atalhos
    $shell = New-Object -ComObject WScript.Shell
    foreach ($pasta in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
        $lnk = $shell.CreateShortcut((Join-Path $pasta ($nome + '.lnk')))
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = $pastaUso
        $lnk.IconLocation = "$exe,0"
        $lnk.Description = 'Editor de relatórios e despachos da Secretaria de Obras – PMVNI'
        $lnk.Save()
        Registrar "atalho: $(Join-Path $pasta ($nome + '.lnk')) -> $exe"
    }

    # 3. Associação dos arquivos (só este usuário)
    $classes = 'HKCU:\Software\Classes'
    $progId = 'PMVNI.RelatorioEmRevisao'
    New-Item -Path "$classes\$progId" -Force | Out-Null
    Set-ItemProperty -Path "$classes\$progId" -Name '(default)' -Value 'Relatório em revisão – PMVNI'
    New-Item -Path "$classes\$progId\DefaultIcon" -Force | Out-Null
    Set-ItemProperty -Path "$classes\$progId\DefaultIcon" -Name '(default)' -Value "`"$exe`",0"
    New-Item -Path "$classes\$progId\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path "$classes\$progId\shell\open\command" -Name '(default)' -Value "`"$exe`" `"%1`""
    foreach ($ext in @('.rpmvni', '.pmvrel')) {
        New-Item -Path "$classes\$ext" -Force | Out-Null
        Set-ItemProperty -Path "$classes\$ext" -Name '(default)' -Value $progId
    }
    Add-Type -Namespace Pmvni -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);'
    [Pmvni.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    Registrar 'associação: .rpmvni e .pmvrel -> cópia de uso'

    # 4. Kit (plugins) atualizado
    $instalarKit = Join-Path $kit 'INSTALAR_KIT_PMVNI.cmd'
    if (-not $SemKit -and (Test-Path -LiteralPath $instalarKit)) {
        $p = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$instalarKit`" atualizar" -WindowStyle Hidden -Wait -PassThru
        Registrar "kit: INSTALAR_KIT_PMVNI.cmd atualizar (código $($p.ExitCode))"
    }
    $pro = Get-ChildItem (Join-Path $env:USERPROFILE '.codex\plugins\cache\pmvni-kit\analise-pro') -Directory -ErrorAction SilentlyContinue | Sort-Object { [version]$_.Name } | Select-Object -Last 1
    $ana = Get-ChildItem (Join-Path $env:USERPROFILE '.codex\plugins\cache\pmvni-kit\analise') -Directory -ErrorAction SilentlyContinue | Sort-Object { [version]$_.Name } | Select-Object -Last 1
    Registrar "plugins instalados: analise $($ana.Name); analise-pro $($pro.Name)"

    # 5. Leis do kit (abre a cópia de uso)
    $leis = Join-Path $kit '03 - LEGISLACAO'
    if (-not $SemLeis -and (Test-Path -LiteralPath $leis)) {
        $n = (Get-ChildItem -LiteralPath $leis -Recurse -Filter *.pdf).Count
        Start-Process -FilePath $exe -ArgumentList '--anexar-leis', "`"$leis`""
        Registrar "leis: o editor abriu anexando $n PDFs de $leis"
    }
    else { Start-Process -FilePath $exe; Registrar 'editor aberto' }
    Registrar 'ok'
}
catch { Registrar ('ERRO: ' + $_.Exception.Message) }
finally { $log | Set-Content -LiteralPath $saida -Encoding utf8 }
