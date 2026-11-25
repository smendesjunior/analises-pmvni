function ArquivosMotor([IO.DirectoryInfo]$pasta) {
    foreach ($item in $pasta.EnumerateFileSystemInfos()) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Vinculo em $($item.Name)" }
        if ($item -is [IO.DirectoryInfo]) { ArquivosMotor $item } else { $item }
    }
}
function HashMotor([IO.FileInfo]$arquivo) {
    $fluxo=$arquivo.OpenRead()
    $sha=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($fluxo)).Replace('-','') }
    finally { $fluxo.Dispose(); $sha.Dispose() }
}
function RegistrarAmbienteCriado([string]$motor,[string]$pasta) {
    $raiz=Get-Item -LiteralPath $pasta
    if ($raiz.Parent.FullName -ne [IO.Path]::GetFullPath($motor) -or ($raiz.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Raiz do ambiente invalida' }
    $arquivos=@{}
    foreach ($f in ArquivosMotor $raiz) { $arquivos[$f.FullName.Substring($raiz.FullName.Length+1)]=HashMotor $f }
    $dir=Join-Path $motor 'inventarios-de-instalacao'
    [void][IO.Directory]::CreateDirectory($dir)
    if ((Get-Item $dir).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Inventario redirecionado' }
    $final=Join-Path $dir ($raiz.Name+'.json')
    $tmp=$final+'.'+[guid]::NewGuid()
    try {
        [IO.File]::WriteAllText($tmp,([ordered]@{origem='instalacao-motor-pmvni';ambiente=$raiz.Name;arquivos=$arquivos} | ConvertTo-Json -Depth 4 -Compress))
        if (Test-Path -LiteralPath $final) { [IO.File]::Replace($tmp,$final,$null) } else { [IO.File]::Move($tmp,$final) }
    } finally { [IO.File]::Delete($tmp) }
}
function ConteudoMotorConferido($item,$motor,$bootstrap) {
    if (!$item.PSIsContainer) { return $true }
    $arquivos=@(ArquivosMotor $item)
    if (!$arquivos.Count) { return $true }
    if ($item.Parent.FullName -eq $bootstrap) {
        $historico=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'inventario-bootstrap.json') -Raw | ConvertFrom-Json
        foreach ($f in $arquivos) {
            if ($f.DirectoryName -ne $item.FullName) { return $false }
            $emUso=Join-Path $PSScriptRoot $f.Name
            $hash=HashMotor $f
            if ((Test-Path -LiteralPath $emUso -PathType Leaf) -and (HashMotor (Get-Item -LiteralPath $emUso)) -eq $hash) { continue }
            if (!$historico.PSObject.Properties[$f.Name] -or $hash -notin $historico.PSObject.Properties[$f.Name].Value) { return $false }
        }
        return $true
    }
    $arquivo=Join-Path $motor ('inventarios-de-instalacao\'+$item.Name+'.json')
    if (!(Test-Path -LiteralPath $arquivo -PathType Leaf) -or !(CaminhoSemLink $arquivo)) { return $false }
    $registro=Get-Content -LiteralPath $arquivo -Raw | ConvertFrom-Json
    if ($registro.origem -ne 'instalacao-motor-pmvni' -or $registro.ambiente -ne $item.Name) { return $false }
    foreach ($f in $arquivos) {
        $rel=$f.FullName.Substring($item.FullName.Length+1)
        $dado=$registro.arquivos.PSObject.Properties[$rel]
        if (!$dado -or (HashMotor $f) -ne $dado.Value) { return $false }
    }
    return $true
}
