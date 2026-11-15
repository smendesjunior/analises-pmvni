namespace Pmvni.Revisao;

/// <summary>Despachos de vários processos, do mais recente para o mais antigo.</summary>
public static class Despachos
{
    public static Leitura<Despacho> Listar(IEnumerable<Processo> processos, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var avisos = new List<string>();
        var todos = new List<Despacho>();
        foreach (var p in processos)
        {
            ct.ThrowIfCancellationRequested();
            todos.AddRange(Processos.LerDespachos(p, avisos, ct));
        }
        // OrderBy é estável: empates de data e número ficam na ordem em que os processos vieram.
        return new Leitura<Despacho>(todos.OrderByDescending(d => d.Data).ThenByDescending(d => d.Numero).ToList(), avisos);
    }
}
