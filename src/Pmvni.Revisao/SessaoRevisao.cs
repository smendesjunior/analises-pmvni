using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>A sessão de revisão: aplica comandos ao <see cref="Projeto"/>, registra cada um no histórico (só se acrescenta, nada é apagado)
/// e mantém as pilhas de desfazer e refazer. Desfazer e refazer também viram registros (<c>desfeito</c>/<c>refeito</c>, com <c>Ref</c> = o
/// <c>N</c> do comando a que se referem). Não é segura para várias threads: a tela usa uma só.</summary>
public sealed class SessaoRevisao
{
    private readonly ContextoUsuario _u;
    private readonly List<RegistroHistorico> _historico;
    private Stack<RegistroHistorico> _desfazer = new();
    private Stack<RegistroHistorico> _refazer = new();
    private int _gravadoAte;
    private int _ultimoN;

    /// <summary>Parte do <paramref name="projeto"/> (que a sessão passa a mudar) e do histórico já gravado; as pilhas começam vazias.</summary>
    public SessaoRevisao(Projeto projeto, IReadOnlyList<RegistroHistorico> historico, ContextoUsuario u)
    {
        ArgumentNullException.ThrowIfNull(projeto);
        ArgumentNullException.ThrowIfNull(historico);
        ArgumentNullException.ThrowIfNull(u);
        Projeto = projeto;
        _u = u;
        _historico = new List<RegistroHistorico>(historico);
        _gravadoAte = _historico.Count;
        _ultimoN = _historico.Count == 0 ? 0 : _historico.Max(r => r.N);
    }

    public Projeto Projeto { get; }

    public bool PodeDesfazer => _desfazer.Count > 0;

    public bool PodeRefazer => _refazer.Count > 0;

    public IReadOnlyList<RegistroHistorico> HistoricoCompleto => _historico;

    /// <summary>Os registros feitos desde a última gravação (<see cref="MarcarGravado"/>): o que o diário de recuperação guarda.</summary>
    public IReadOnlyList<RegistroHistorico> Pendentes => _historico.Skip(_gravadoAte).ToList();

    /// <summary>Avisa de cada registro novo (comando, desfeito, refeito), depois de o projeto já ter mudado. Não dispara em <see cref="Reaplicar"/>.</summary>
    public event Action<RegistroHistorico>? Registrado;

    /// <summary>O projeto como ficaria depois do comando, numa cópia: a sessão, o histórico e as pilhas não mudam. Recusa como <see cref="Aplicar"/>.</summary>
    public Projeto Testar(Comando c)
    {
        var copia = Projeto.Clonar();
        var ef = Motor.Calcular(copia, c);
        if (ef is not null) Motor.Definir(copia, ef.Tipo, ef.Item, ef.Campo, ef.Depois);
        return copia;
    }

    /// <summary>Aplica o comando. Devolve o registro, ou null se o comando não muda nada (nada é registrado). Se recusado
    /// (<see cref="ComandoRecusadoException"/>), o projeto, o histórico e as pilhas ficam como estavam.</summary>
    public RegistroHistorico? Aplicar(Comando c)
    {
        var ef = Motor.Calcular(Projeto, c);
        if (ef is null) return null;
        Motor.Definir(Projeto, ef.Tipo, ef.Item, ef.Campo, ef.Depois);
        var r = NovoRegistro(ef.Tipo, ef.Item, ef.Campo, ef.Antes, ef.Depois, null);
        _desfazer.Push(r);
        _refazer.Clear();
        Registrar(r);
        return r;
    }

    public RegistroHistorico? Desfazer()
    {
        if (_desfazer.Count == 0) return null;
        var o = _desfazer.Pop();
        Motor.Definir(Projeto, o.Tipo, o.Item!, o.Campo, o.Antes);
        _refazer.Push(o);
        var r = NovoRegistro("desfeito", o.Item, o.Campo, Embrulhar(o.Tipo, o.Depois), Embrulhar(o.Tipo, o.Antes), o.N);
        Registrar(r);
        return r;
    }

    public RegistroHistorico? Refazer()
    {
        if (_refazer.Count == 0) return null;
        var o = _refazer.Pop();
        Motor.Definir(Projeto, o.Tipo, o.Item!, o.Campo, o.Depois);
        _desfazer.Push(o);
        var r = NovoRegistro("refeito", o.Item, o.Campo, Embrulhar(o.Tipo, o.Antes), Embrulhar(o.Tipo, o.Depois), o.N);
        Registrar(r);
        return r;
    }

    /// <summary>A sessão foi gravada: o que veio antes deixa de ser pendente.</summary>
    public void MarcarGravado() => _gravadoAte = _historico.Count;

    /// <summary>A gravação levou os primeiros <paramref name="registros"/> do histórico (um comando chegou durante ela): só esses deixam de
    /// ser pendentes. Nunca volta atrás nem passa do histórico.</summary>
    public void MarcarGravado(int registros) => _gravadoAte = Math.Clamp(registros, _gravadoAte, _historico.Count);

    /// <summary>Reaplica registros do diário de recuperação sobre o projeto aberto (comandos, desfeitos e refeitos, na ordem), refazendo
    /// o histórico e as pilhas. Registros com <c>N</c> já presente no histórico são ignorados (o diário pode repetir o que o pacote já tem).
    /// Tudo ou nada: se um registro não bater com o projeto, <see cref="InvalidOperationException"/> e nada muda. Os registros reaplicados
    /// contam como pendentes (ainda não estão no pacote) e não disparam <see cref="Registrado"/>.</summary>
    public void Reaplicar(IEnumerable<RegistroHistorico> registros)
    {
        ArgumentNullException.ThrowIfNull(registros);
        var trabalho = Projeto.Clonar();
        var historico = new List<RegistroHistorico>(_historico);
        var desfazer = new Stack<RegistroHistorico>(_desfazer.Reverse());
        var refazer = new Stack<RegistroHistorico>(_refazer.Reverse());
        var ultimo = _ultimoN;

        foreach (var r in registros)
        {
            if (r.N <= ultimo) continue;
            AplicarRegistro(trabalho, r);
            switch (r.Tipo)
            {
                case Importador.TipoHistorico:
                    break;
                case "desfeito":
                    if (desfazer.Count > 0 && desfazer.Peek().N == r.Ref) refazer.Push(desfazer.Pop());
                    break;
                case "refeito":
                    if (refazer.Count > 0 && refazer.Peek().N == r.Ref) desfazer.Push(refazer.Pop());
                    break;
                default:
                    desfazer.Push(r);
                    refazer.Clear();
                    break;
            }
            historico.Add(r);
            ultimo = r.N;
        }

        Projeto.Itens = trabalho.Itens;
        Projeto.Documento = trabalho.Documento;
        Projeto.Linhas = trabalho.Linhas;
        Projeto.Figuras = trabalho.Figuras;
        Projeto.Acrescimos = trabalho.Acrescimos;
        _historico.Clear();
        _historico.AddRange(historico);
        _desfazer = desfazer;
        _refazer = refazer;
        _ultimoN = ultimo;
    }

    private static void AplicarRegistro(Projeto p, RegistroHistorico r)
    {
        try
        {
            switch (r.Tipo)
            {
                case Importador.TipoHistorico:
                    return;
                case "desfeito":
                case "refeito":
                    Motor.Definir(p, r.Depois!["tipo"]!.GetValue<string>(), r.Item!, r.Campo, r.Depois["valor"]);
                    return;
                default:
                    Motor.Definir(p, r.Tipo, r.Item!, r.Campo, r.Depois);
                    return;
            }
        }
        catch (Exception e) when (e is NullReferenceException or InvalidCastException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("O registro " + r.N + " do diário não pôde ser reaplicado: " + e.Message, e);
        }
    }

    private RegistroHistorico NovoRegistro(string tipo, string? item, string? campo, JsonNode? antes, JsonNode? depois, int? @ref) =>
        new(++_ultimoN, _u.Relogio(), _u.Usuario, tipo, item, campo, antes, depois, @ref);

    private void Registrar(RegistroHistorico r)
    {
        _historico.Add(r);
        Registrado?.Invoke(r);
    }

    /// <summary>Desfeito e refeito guardam o tipo do comando a que se referem junto do valor, para se descreverem e se reaplicarem sozinhos.</summary>
    private static JsonNode Embrulhar(string tipo, JsonNode? valor) =>
        new JsonObject { ["tipo"] = tipo, ["valor"] = valor?.DeepClone() };
}
