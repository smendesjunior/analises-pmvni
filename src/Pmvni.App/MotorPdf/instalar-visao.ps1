$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'nativo.ps1')
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
$fontes = Join-Path $base 'fontes'
[IO.Directory]::CreateDirectory($fontes) | Out-Null
$lock = Join-Path $PSScriptRoot 'visao.lock'
$venv = Join-Path $base ('visao-' + (Get-FileHash $lock).Hash.Substring(0,16))
$python = Join-Path $venv 'Scripts\python.exe'
$uv = Join-Path $base 'uv-0.13.9.exe'
$env:UV_PYTHON_INSTALL_DIR = Join-Path $base 'python'
if (!(Test-Path $python)) {
    Invocar-Nativo $uv @('venv', '--quiet', '--managed-python', '--python', '3.11.14', $venv)

}
Invocar-Nativo $uv @('pip', 'sync', '--quiet', '--python', $python, '--default-index', 'https://pypi.org/simple', '--only-binary', ':all:', '--require-hashes', $lock)

foreach ($item in (Get-Content (Join-Path $PSScriptRoot 'fontes-visao.json') -Raw | ConvertFrom-Json)) {
    $arq = Join-Path $fontes $item.nome
    if (!(Test-Path $arq) -or (Get-FileHash $arq).Hash -ne $item.sha256) {
        $tmp = "$arq.$([guid]::NewGuid()).tmp"
        try {
            Invoke-WebRequest -UseBasicParsing $item.url -OutFile $tmp
            if ((Get-FileHash $tmp).Hash -ne $item.sha256) { throw 'Falha de integridade da fonte' }
            Move-Item -LiteralPath $tmp -Destination $arq -Force
        } finally { if(Test-Path $tmp){Remove-Item -LiteralPath $tmp} }
    }
    if ($item.nome -eq 'blueprint.zip') {
        Invocar-Nativo $python @('-I', '-c', 'import pathlib,sys,zipfile;z=zipfile.ZipFile(sys.argv[1]);z.extractall(sys.argv[2],[n for n in z.namelist() if chr(47)+chr(115)+chr(114)+chr(99)+chr(47) in n and n.endswith(chr(46)+chr(112)+chr(121))])', $arq, $fontes)

    }
}
$runner = Join-Path $venv 'visao.py'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'visao.py') $runner -Force
$teste = Join-Path $env:TEMP ([guid]::NewGuid().ToString())
[IO.Directory]::CreateDirectory($teste) | Out-Null
try {
    $pdf = Join-Path $teste 'teste.pdf'
    Invocar-Nativo $python @('-I', '-c', 'import pymupdf,sys; d=pymupdf.open();p=d.new_page();p.draw_rect((40,40,300,300));d.save(sys.argv[1])', $pdf)

    foreach ($nome in 'blueprint','fyp-floorplan') {
        $json = Join-Path $teste "$nome.json"
        Invocar-Nativo $python @('-I', $runner, $nome, $pdf, '1', $json)
        if (!(Test-Path $json)) { throw "Falha no teste: $nome" }
        Write-Output "$nome INSTALADO"
    }
    [IO.File]::WriteAllText((Join-Path $base 'visao-runtime.txt'),$python)
} finally {
    foreach($f in 'teste.pdf','blueprint.json','fyp-floorplan.json'){ $a=Join-Path $teste $f; if(Test-Path $a){Remove-Item -LiteralPath $a} }
    [IO.Directory]::Delete($teste)
}
