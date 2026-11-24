using System.Text.Json;

namespace Pmvni.Agentes;

internal sealed record OrcamentoDaAnalise(double Medido, double SemAtribuicao)
{
    internal bool MedicaoCompleta { get; init; } = true;
    internal static OrcamentoDaAnalise? Ler(string controle, DateTime desde)
    {
        var registro = ArquivoJsonDoKit.Ler(Path.Combine(controle, "orcamento_lb.json"), desde);
        var estado = ArquivoJsonDoKit.Ler(Path.Combine(controle, "engine_state.json"), desde);
        var preparo = UsoDasIas.Objeto(UsoDasIas.Objeto(estado, "stages"), "prepare");
        if (registro is not { ValueKind: JsonValueKind.Object } r || preparo is not { } p
            || ProgressoLocal.Texto(p, "certificado_id") is not { Length: > 0 } id
            || ProgressoLocal.Texto(r, "preparo") != id || ProgressoLocal.Numero(r, "versao_medicao") != 2
            || ProgressoLocal.Numero(r, "consumo_observado") is not double medido || medido < 0
            || ProgressoLocal.Numero(r, "pontos_sem_atribuicao") is not double reserva || reserva < 0
            || !r.TryGetProperty("contas_vinculadas", out var contas) || contas.ValueKind != JsonValueKind.Array
            || contas.GetArrayLength() == 0 && (medido > 0 || reserva == 0)) return null;
        return new(medido, reserva)
        {
            MedicaoCompleta = !r.TryGetProperty("medicao_5h_completa", out var completa) || completa.ValueKind != JsonValueKind.False
        };
    }
}
