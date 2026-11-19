$ErrorActionPreference = 'Stop'
$base = Join-Path $env:LOCALAPPDATA 'PMVNI\MotorPdf'
[IO.Directory]::CreateDirectory($base) | Out-Null
$mutex = [Threading.Mutex]::new($false,'Local\PMVNI-MotorPdf')
try {
    try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { return }
    $lock = Join-Path $PSScriptRoot 'requirements.lock'
    $id = (Get-FileHash -LiteralPath $lock).Hash.Substring(0,20)
    $venv = Join-Path $base "env-$id"
    $python = Join-Path $venv 'Scripts\python.exe'
    $pointer = Join-Path $base 'runtime.txt'
    if ((Test-Path $pointer) -and ([IO.File]::ReadAllText($pointer) -eq $python) -and (Test-Path $python)) {
        & $python -I -c 'import pymupdf, docling_mcp, mcp'
        if ($LASTEXITCODE -eq 0) { return }
    }
    $spec = Get-Content -Raw (Join-Path $PSScriptRoot 'uv.json') | ConvertFrom-Json
    $uv = Join-Path $base 'uv-0.13.9.exe'
    if (!(Test-Path $uv) -or (Get-FileHash $uv).Hash -ne $spec.exe_sha256) {
        $zip = Join-Path $base 'uv.download'
        try {
            Invoke-WebRequest -UseBasicParsing $spec.url -OutFile $zip
            if ((Get-FileHash $zip).Hash -ne $spec.sha256) { throw 'Hash do instalador divergente' }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::OpenRead($zip)
            try { [IO.Compression.ZipFileExtensions]::ExtractToFile($archive.GetEntry($spec.entry),$uv,$true) } finally { $archive.Dispose() }
        } finally { if (Test-Path $zip) { Remove-Item -LiteralPath $zip } }
    }
    $env:UV_PYTHON_INSTALL_DIR = Join-Path $base 'python'
    if (!(Test-Path $python)) {
        & $uv venv --managed-python --python 3.11.14 $venv
        if ($LASTEXITCODE) { throw 'Falha ao preparar Python 3.11' }
    }
    & $uv pip sync --python $python --default-index https://pypi.org/simple --only-binary :all: --require-hashes $lock
    if ($LASTEXITCODE) { throw 'Falha ao instalar dependencias' }
    & $python -I -c 'import pymupdf, docling_mcp, mcp'
    if ($LASTEXITCODE) { throw 'Falha na verificacao do ambiente' }
    $tmp = "$pointer.new"
    [IO.File]::WriteAllText($tmp,$python)
    if (Test-Path $pointer) { [IO.File]::Replace($tmp,$pointer,($pointer + ".anterior")) } else { [IO.File]::Move($tmp,$pointer) }
} finally {
    if ($owned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
