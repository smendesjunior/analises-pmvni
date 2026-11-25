# Regras independentes da interface. Um ponteiro ausente conserva toda a familia.
class PoliticaLimpezaMotor {
    static [bool] Ambiente([string]$nome, [string]$previsto, [string]$ativo) {
        return $ativo -and $previsto -and $nome -ne $previsto -and $nome -ne $ativo -and (
            ($nome -match '^env-[0-9a-f]{20}$' -and $ativo -match '^env-[0-9a-f]{20}$') -or
            ($nome -match '^visao-[0-9a-f]{16}$' -and $ativo -match '^visao-[0-9a-f]{16}$'))
    }

    static [bool] Teste([string]$nome) { return $nome -like 'teste-*' }
    static [bool] Bootstrap([string]$nome) { return $nome -match '^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$' }
    static [bool] SobraInstalacao([string]$nome) { return $nome -match '^antigo-[\da-f]{32}\.exe\.old$' }
}
