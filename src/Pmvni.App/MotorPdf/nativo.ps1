function Invocar-Nativo {
    param([string]$programa, [string[]]$argumentos)
    $preferencia = $ErrorActionPreference
    $erros = [Collections.Generic.Queue[string]]::new()
    try {
        $ErrorActionPreference = 'Continue'
        & $programa @argumentos 2>&1 | ForEach-Object {
            if ($_ -is [Management.Automation.ErrorRecord]) {
                $erros.Enqueue($_.ToString())
                if ($erros.Count -gt 8) { $null = $erros.Dequeue() }
            } else { Write-Output $_ }
        }
        $codigo = $LASTEXITCODE
    } finally { $ErrorActionPreference = $preferencia }
    if ($codigo -ne 0) { throw "$programa terminou com codigo $codigo. $($erros -join [Environment]::NewLine)" }
}
