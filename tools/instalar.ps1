<#
  Instala o Editor de Relatórios PMVNI (Análise de Projetos – PMVNI) neste usuário, sem admin. Abra tools\instalar.cmd pelo explorer.exe
  (fora do pacote do Claude). O executável continua em Aplicativo\ (portátil, atualizado no lugar); este script só:
    1. cria os atalhos no Menu Iniciar e na Área de Trabalho;
    2. associa .rpmvni (e o formato antigo .pmvrel) ao editor (HKCU\Software\Classes);
    3. atualiza o kit PMVNI (INSTALAR_KIT_PMVNI.cmd atualizar: plugins analise / análise pro / despacho);
    4. abre o editor anexando as leis do kit (03 - LEGISLACAO) à Legislação (as já anexadas não se repetem).
  As pastas padrão do app já são as do usuário: SIDNEY JUNIOR\PROCESSOS e SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI).
  Resultado em tools\instalar-resultado.txt.
#>
param([switch]$SemKit, [switch]$SemLeis)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $raiz 'Aplicativo\Editor de Relatorios PMVNI.exe'
$base = Join-Path $env:USERPROFILE 'OneDrive\PMVNI\SIDNEY JUNIOR'
$kit = Join-Path $base 'KIT DE TREINAMENTO DE IA (PMVNI)'
$saida = Join-Path $PSScriptRoot 'instalar-resultado.txt'
$log = New-Object System.Collections.Generic.List[string]
function Registrar([string]$t) { $log.Add((Get-Date -Format 'HH:mm:ss') + ' ' + $t) }

try {
    if (-not (Test-Path -LiteralPath $exe)) { throw "Executável não encontrado: $exe" }
    $nome = 'Análise de Projetos – PMVNI'

    # 1. Atalhos
    $shell = New-Object -ComObject WScript.Shell
    foreach ($pasta in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
        $lnk = $shell.CreateShortcut((Join-Path $pasta ($nome + '.lnk')))
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = Split-Path -Parent $exe
        $lnk.IconLocation = "$exe,0"
        $lnk.Description = 'Editor de relatórios e despachos da Secretaria de Obras – PMVNI'
        $lnk.Save()
        Registrar "atalho: $(Join-Path $pasta ($nome + '.lnk'))"
    }

    # 2. Associação dos arquivos (só este usuário)
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
    # Avisa o Explorer da associação nova (ícones e duplo clique sem reiniciar).
    Add-Type -Namespace Pmvni -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);'
    [Pmvni.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    Registrar 'associação: .rpmvni e .pmvrel -> editor'

    # 3. Kit (plugins) atualizado
    $instalarKit = Join-Path $kit 'INSTALAR_KIT_PMVNI.cmd'
    if (-not $SemKit -and (Test-Path -LiteralPath $instalarKit)) {
        $p = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', "`"$instalarKit`" atualizar" -WindowStyle Hidden -Wait -PassThru
        Registrar "kit: INSTALAR_KIT_PMVNI.cmd atualizar (código $($p.ExitCode))"
    }
    $pro = Get-ChildItem (Join-Path $env:USERPROFILE '.codex\plugins\cache\pmvni-kit\analise-pro') -Directory -ErrorAction SilentlyContinue | Sort-Object { [version]$_.Name } | Select-Object -Last 1
    $ana = Get-ChildItem (Join-Path $env:USERPROFILE '.codex\plugins\cache\pmvni-kit\analise') -Directory -ErrorAction SilentlyContinue | Sort-Object { [version]$_.Name } | Select-Object -Last 1
    Registrar "plugins instalados: analise $($ana.Name); analise-pro $($pro.Name)"

    # 4. Leis do kit
    $leis = Join-Path $kit '03 - LEGISLACAO'
    if (-not $SemLeis -and (Test-Path -LiteralPath $leis)) {
        $n = (Get-ChildItem -LiteralPath $leis -Recurse -Filter *.pdf).Count
        Start-Process -FilePath $exe -ArgumentList '--anexar-leis', "`"$leis`""
        Registrar "leis: o editor abriu anexando $n PDFs de $leis"
    }
    Registrar 'ok'
}
catch { Registrar ('ERRO: ' + $_.Exception.Message) }
finally { $log | Set-Content -LiteralPath $saida -Encoding utf8 }
