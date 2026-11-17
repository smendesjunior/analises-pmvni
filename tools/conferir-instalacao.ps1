$r = Join-Path $PSScriptRoot 'conferir-resultado.txt'
$cat = Join-Path $env:APPDATA 'EditorRelatoriosPMVNI\legislacao\catalogo.json'
$l = @()
$l += "ajustes no Roaming: " + (Test-Path (Join-Path $env:APPDATA 'EditorRelatoriosPMVNI\ajustes.json'))
if (Test-Path $cat) { $j = Get-Content $cat -Raw -Encoding UTF8 | ConvertFrom-Json; $n = @($j.anexos).Count; if ($n -eq 0) { $n = @($j).Count }; $l += "leis no catalogo: $n" } else { $l += "catalogo ainda nao existe" }
$l += "pdfs copiados: " + @(Get-ChildItem (Join-Path $env:APPDATA 'EditorRelatoriosPMVNI\legislacao') -Filter *.pdf -ErrorAction SilentlyContinue).Count
$l += "assoc .rpmvni: " + (Get-ItemProperty 'HKCU:\Software\Classes\.rpmvni' -ErrorAction SilentlyContinue).'(default)'
$l += "editor aberto: " + [bool](Get-Process 'Editor de Relatorios PMVNI' -ErrorAction SilentlyContinue)
$l | Set-Content $r -Encoding utf8
