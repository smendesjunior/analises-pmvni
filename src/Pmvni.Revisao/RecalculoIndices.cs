using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Correção 2026-11-17: os valores ligados por uma conta na tabela ÍNDICES URBANÍSTICOS acompanham a edição manual, sem IA.
/// Antes, só a situação da própria linha era refeita (valor × limite); mudar a área de projeção ou a do lote no "Cálculo / descrição"
/// não mexia no resultado da conta, no "No projeto" nem na situação, e as outras taxas continuavam com a área antiga.
/// <para>O que se liga a quê (tudo num <see cref="Lote"/>, um Desfazer):</para>
/// <list type="bullet">
/// <item>Cálculo de uma linha (<c>126,40 ÷ 360,00 × 100 = 35,1%</c>, <see cref="ContaIndice"/>): mudou um operando e o resultado ficou
/// como estava → o resultado é refeito (se o usuário mudou o resultado, vale o dele).</item>
/// <item>Resultado da conta → "No projeto", só se o valor no projeto era o da conta (concordava com o resultado antigo). Um valor
/// declarado diferente da conta ("10%" com a conta em 35,1%) é o que o projeto informa e não muda.</item>
/// <item>Área do lote: a do subtítulo da tabela ("Lote de 360,00 m²") ou a da linha "Área do terreno". Mudou o divisor de uma conta que
/// usava essa área, ou o valor da linha do terreno → as outras contas com a mesma área, a linha do terreno e a chamada "lote" do
/// diagrama acompanham.</item>
/// <item>Valor no projeto e limite → situação, como antes (<see cref="Comparar"/>). Só muda sozinha a situação que é comparação de
/// números: INFORMAÇÕES DIVERGENTES fica enquanto o valor no projeto e a conta não batem; FALTA COMPROVAR só muda quando o usuário
/// edita o valor ou o limite da própria linha.</item>
/// <item>Taxa de permeabilidade → a área permeável da planta do diagrama ("35% no projeto", "35,1% no cálculo"), quando o número escrito
/// lá é o antigo.</item>
/// </list>
/// O subtítulo da tabela não é texto editável no editor: a área dele não muda (a tela diz isso no aviso).</summary>
public static partial class RecalculoLinha
{
    const string CampoCalculo = "calculo";

    /// <summary>As mudanças de um recálculo, antes de virarem comandos: o que cada texto, situação e justificativa passa a ser.
    /// Lê primeiro o que já foi mudado aqui, depois o projeto.</summary>
    sealed class Plano(Projeto p)
    {
        public Projeto P { get; } = p;
        readonly List<string> ordem = [];
        readonly Dictionary<string, List<(string Campo, string Texto)>> textos = new(StringComparer.Ordinal);
        readonly Dictionary<string, string> situacoes = new(StringComparer.Ordinal);
        readonly Dictionary<string, string?> justificativas = new(StringComparer.Ordinal);

        public IReadOnlyList<string> Linhas => ordem;

        public string? Texto(string linha, string campo) =>
            textos.TryGetValue(linha, out var l) && l.FindLastIndex(x => x.Campo == campo) is var i and >= 0 ? l[i].Texto : TabelasRevisao.TextoEfetivo(P, linha, campo);

        public string? Situacao(string linha) => situacoes.TryGetValue(linha, out var s) ? s : TabelasRevisao.SituacaoEfetiva(P, linha);

        public bool Mudou(string linha, string campo) => textos.TryGetValue(linha, out var l) && l.Exists(x => x.Campo == campo);

        public bool MudouSituacao(string linha) => situacoes.ContainsKey(linha);

        void Tocar(string linha)
        {
            if (!ordem.Contains(linha)) ordem.Add(linha);
        }

        /// <summary>Muda o texto (que tem de existir na linha); false se não existe ou já era esse.</summary>
        public bool Definir(string linha, string campo, string texto)
        {
            if (!TabelasRevisao.Editavel(P, linha, campo) || Texto(linha, campo) == texto) return false;
            Tocar(linha);
            if (!textos.TryGetValue(linha, out var l)) textos[linha] = l = [];
            var i = l.FindIndex(x => x.Campo == campo);
            if (i >= 0) l[i] = (campo, texto);
            else l.Add((campo, texto));
            return true;
        }

        public void DefinirSituacao(string linha, string situacao)
        {
            Tocar(linha);
            situacoes[linha] = situacao;
        }

        public void DefinirJustificativa(string linha, string? justificativa)
        {
            Tocar(linha);
            justificativas[linha] = justificativa;
        }

        public List<Comando> Comandos()
        {
            var r = new List<Comando>();
            foreach (var linha in ordem)
            {
                if (textos.TryGetValue(linha, out var l)) r.AddRange(l.Select(x => (Comando)new DefinirCampo(linha, x.Campo, x.Texto)));
                if (situacoes.TryGetValue(linha, out var s)) r.Add(new DefinirSituacao(linha, s));
                if (justificativas.TryGetValue(linha, out var j)) r.Add(new DefinirJustificativa(linha, j));
            }
            return r;
        }
    }

    enum Estado { SemCriterio, Ilegivel, Mantida, Calculada }

    /// <summary>O que a comparação do valor com o limite deu numa linha.</summary>
    sealed record Avaliacao(Estado Estado, string TextoValor, string TextoLimite, bool Atende, string Diferenca, string? Situacao, bool MudaSituacao,
        bool MudaTexto, bool Divergente);

    /// <summary>A edição do valor, do limite ou do cálculo de uma linha de índice, com tudo o que depende dela.</summary>
    static EdicaoDeLinha AoEditarIndice(Projeto p, string linha, string campo, string texto, EdicaoDeLinha simples)
    {
        if (TabelasRevisao.Original(p, linha) is not { } original || !TabelasRevisao.Caminhos(original).Contains(campo)) return simples;
        var novo = TextoEditado.Normalizar(texto);
        var antes = TabelasRevisao.TextoEfetivo(p, linha, campo);
        if (novo.Length == 0 || novo == antes) return simples;   // vazio é recusado adiante; igual não muda nada

        var pl = new Plano(p);
        pl.Definir(linha, campo, novo);
        var rotulo = TabelasRevisao.Rotulo(linha);
        Avaliacao propria;
        var areaNova = (decimal?)null;
        if (campo == CampoCalculo)
        {
            if (ContaIndice.Ler(novo) is not { } digitada) return simples;   // um cálculo sem conta é texto livre
            var anterior = ContaIndice.Ler(antes);
            var final = novo;
            // Mudou um operando e o resultado ficou como estava: o resultado é refeito. Se o usuário escreveu outro resultado, vale o dele.
            if (anterior is not null && antes![anterior.PosResultado] == novo[digitada.PosResultado]
                && (anterior.Dividendo.Valor != digitada.Dividendo.Valor || anterior.Divisor.Valor != digitada.Divisor.Valor))
            {
                final = digitada.Refazer(novo);
                pl.Definir(linha, CampoCalculo, final);
            }
            var depois = ContaIndice.Ler(final)!;
            SeguirResultado(pl, linha, anterior, depois);
            if (anterior is not null && anterior.Divisor.Valor != depois.Divisor.Valor && UsaAreaDoLote(p, linha))
            {
                PropagarArea(pl, anterior.Divisor.Valor, depois.Divisor.Valor, linha, exigeVinculo: true);
                areaNova = depois.Divisor.Valor;
            }
            propria = Reavaliar(pl, linha, direta: false);
        }
        else
        {
            if (campo == CampoValor && EhTerreno(p, linha) && LerMedida(antes) is { Unidade: "m²" } a && LerMedida(novo) is { Unidade: "m²" } n && a.Valor != n.Valor)
            {
                PropagarArea(pl, a.Valor, n.Valor, linha, exigeVinculo: false);
                areaNova = n.Valor;
            }
            propria = Reavaliar(pl, linha, direta: true);
        }
        foreach (var l in pl.Linhas.ToList()) DiagramaAcompanha(pl, l);

        var comandos = pl.Comandos();
        var outras = pl.Linhas.Where(l => l != linha).ToList();
        var soAEdicao = comandos.Count == 1;
        var ilegivel = "o texto mudou, mas o editor não conseguiu comparar \"" + propria.TextoValor + "\" com \"" + propria.TextoLimite + "\"";

        // Só a própria linha, editando o valor ou o limite: as mensagens de sempre.
        if (campo != CampoCalculo && outras.Count == 0)
        {
            if (propria.Estado == Estado.Ilegivel)
                return new EdicaoDeLinha(simples.Comando, false, true,
                    rotulo + ": " + ilegivel + "; a situação ficou como estava. Confira-a, ou escreva na justificativa o que rever e use «Rever com a IA» nas Propriedades.");
            if (propria.Estado == Estado.Mantida && propria.Divergente && soAEdicao)
                return new EdicaoDeLinha(simples.Comando, false, false,
                    rotulo + ": o valor no projeto (" + propria.TextoValor + ") continua diferente do resultado da conta no cálculo; a situação continua "
                    + ConferenciaRevisao.TextoSituacao(propria.Situacao) + ". Confira-a, ou escreva na justificativa o que rever e use «Rever com a IA» nas Propriedades.");
            if (soAEdicao) return simples;
            if (propria.Estado == Estado.Calculada)
                return new EdicaoDeLinha(new Lote(linha, comandos, ResumoDaComparacao(propria)), true, false,
                    rotulo + " recalculado pelo editor (sem IA): " + propria.TextoValor + (propria.Atende ? " atende a " : " não atende a ") + propria.TextoLimite
                    + (propria.MudaSituacao ? "; situação " : "; a situação continua ") + ConferenciaRevisao.TextoSituacao(propria.Situacao)
                    + (propria.MudaTexto ? "; texto: \"" + propria.Diferenca + "\"" : "") + ". Um Desfazer volta tudo.");
        }
        if (soAEdicao) return simples;

        var partes = new List<string>();
        if (DescreverLinha(p, pl, linha, propria) is { Length: > 0 } daPropria) partes.Add(daPropria);
        if (propria.Estado == Estado.Ilegivel && campo != CampoCalculo) partes.Add(ilegivel + "; a situação ficou como estava");
        foreach (var l in outras)
            if (DescreverLinha(p, pl, l, null) is { Length: > 0 } t) partes.Add(TabelasRevisao.Rotulo(l) + ": " + t);
        if (areaNova is { } area && TemSubtituloComArea(p))
            partes.Add("o subtítulo da tabela não é editável e continua com a área antiga; corrija-o na análise se precisar (área nova: "
                       + Formatar(area, ContaIndice.CasasDeArea, "m²") + ")");
        var mensagem = rotulo + " recalculado pelo editor (sem IA): " + string.Join("; ", partes) + ". Um Desfazer volta tudo.";
        var resumo = campo != CampoCalculo && propria.Estado == Estado.Calculada
            ? ResumoDaComparacao(propria)
            : "Recalculado: " + TabelasRevisao.NomeDoCampo(campo) + " " + (pl.Texto(linha, campo) ?? novo);
        return new EdicaoDeLinha(new Lote(linha, comandos, resumo), true, false, mensagem);
    }

    static string ResumoDaComparacao(Avaliacao a) => "Recalculado: " + a.TextoValor + (a.Atende ? " atende a " : " não atende a ") + a.TextoLimite;

    /// <summary>O que mudou numa linha, em poucas palavras ("conta = 36,1%; no projeto 36,1%; situação ATENDE").</summary>
    static string DescreverLinha(Projeto p, Plano pl, string linha, Avaliacao? avaliacao)
    {
        var partes = new List<string>();
        if (TabelasRevisao.TentarLer(linha, out var bloco, out _) && bloco != TabelasRevisao.Indices)
            return pl.Linhas.Contains(linha) ? "texto atualizado (" + string.Join(", ", CamposMudados(p, pl, linha)) + ")" : "";
        if (pl.Mudou(linha, CampoCalculo) && ContaIndice.Ler(pl.Texto(linha, CampoCalculo)) is { } c)
            partes.Add("conta = " + Formatar(c.Resultado.Valor, c.Resultado.Casas, c.Resultado.Unidade));
        if (pl.Mudou(linha, CampoValor)) partes.Add("no projeto " + pl.Texto(linha, CampoValor));
        var sit = pl.Situacao(linha);
        if (pl.MudouSituacao(linha)) partes.Add("situação " + ConferenciaRevisao.TextoSituacao(sit));
        else if (avaliacao is { Estado: Estado.Mantida, Divergente: true })
            partes.Add("a situação continua " + ConferenciaRevisao.TextoSituacao(sit) + " (o valor no projeto não bate com a conta)");
        else if (avaliacao is { Estado: Estado.Calculada or Estado.Mantida }) partes.Add("a situação continua " + ConferenciaRevisao.TextoSituacao(sit));
        return string.Join("; ", partes);
    }

    static IEnumerable<string> CamposMudados(Projeto p, Plano pl, string linha) =>
        TabelasRevisao.Original(p, linha) is { } o
            ? TabelasRevisao.Caminhos(o).Where(c => pl.Mudou(linha, c)).Select(TabelasRevisao.NomeDoCampo)
            : [];

    // ---------- a comparação de uma linha ----------

    /// <summary>Compara o valor com o limite da linha (com o que o plano já mudou) e escreve a situação, o texto da diferença e a
    /// justificativa do editor. <paramref name="direta"/>: o usuário editou o valor ou o limite desta linha (FALTA COMPROVAR pode virar
    /// ATENDE/NÃO ATENDE); senão a linha só acompanha outra edição e só a situação numérica muda.</summary>
    static Avaliacao Reavaliar(Plano pl, string linha, bool direta)
    {
        var p = pl.P;
        var textoValor = pl.Texto(linha, CampoValor) ?? "";
        var textoLimite = pl.Texto(linha, CampoLimite) ?? "";
        var situacao = pl.Situacao(linha);
        var caminhos = TabelasRevisao.Original(p, linha) is { } o ? TabelasRevisao.Caminhos(o) : [];
        if (situacao is null || !caminhos.Contains(CampoValor) || !caminhos.Contains(CampoLimite))
            return new Avaliacao(Estado.SemCriterio, textoValor, textoLimite, false, "", situacao, false, false, false);
        if (LerMedida(textoValor) is not { } valor || LerCriterio(textoLimite) is not { } criterio || Comparar(valor, criterio) is not var (atendeEscrito, _))
            return new Avaliacao(Estado.Ilegivel, textoValor, textoLimite, false, "", situacao, false, false, false);

        var conta = ContaIndice.Ler(pl.Texto(linha, CampoCalculo));
        var divergente = conta is not null && valor.Unidade == conta.Resultado.Unidade && !conta.ConcordaCom(valor);
        // Tarefa 15 (casos-ouro do kit): o "No projeto" que é o resultado da conta compara pelo valor EXATO da conta, sem arredondar a
        // favor, como o calcular_indices.py: 240,01 ÷ 300,00 × 100 = 80,0033% não atende TO ≤ 80%, mesmo escrito "80,0%".
        var exata = conta is not null && valor.Unidade == conta.Resultado.Unidade && !divergente;
        var (atende, diferenca) = exata ? Comparar(valor with { Valor = conta!.Calcular() }, criterio)!.Value : Comparar(valor, criterio)!.Value;
        var mantem = situacao switch
        {
            "DIVERGENCIA" => divergente || !direta && conta is null,   // a divergência é juízo da análise enquanto os números não batem
            "PENDENTE" => !direta,                                     // falta comprovar: só o usuário, editando esta linha, resolve
            _ => !direta && divergente,
        };
        if (mantem) return new Avaliacao(Estado.Mantida, textoValor, textoLimite, atende, diferenca, situacao, false, false, divergente);

        // O número escrito parece estar do outro lado do limite ("80,0%" ao lado de "≤ 80%" com a conta em 80,0033%): o "No projeto" e o
        // resultado da conta ganham as casas precisas para mostrar o que a conta dá, como o br_fiel do kit ("80,003%"). Não quando o próprio
        // usuário acabou de escrever o valor ou o limite desta linha (direta): o texto dele fica; a situação e a diferença dizem o que a conta dá.
        if (exata && !direta && atendeEscrito != atende)
        {
            var exato = conta!.Calcular();
            var casas = Math.Max(valor.Casas, 2);
            while (casas < 10 && Comparar(new Medida(Math.Round(exato, casas, MidpointRounding.AwayFromZero), valor.Unidade, casas), criterio)?.Atende != atende) casas++;
            var numero = Formatar(Math.Round(exato, casas, MidpointRounding.AwayFromZero), casas, "");
            if (TrocarNumero(textoValor, valor.Valor, valor.Casas, null, numero) is { } novoValor && pl.Definir(linha, CampoValor, novoValor)) textoValor = novoValor;
            if (pl.Texto(linha, CampoCalculo) is { } calculo) pl.Definir(linha, CampoCalculo, conta.ComResultado(calculo, numero));
        }

        var novaSituacao = atende ? "CONTA_CONFERE" : "NAO_ATENDE";
        var textoAtual = pl.Texto(linha, CampoTexto);
        var mudaTexto = false;
        if (caminhos.Contains(CampoTexto) && textoAtual is not null)
        {
            // Continua não atendendo: a citação "(item NN)" do texto antigo acompanha a nova diferença.
            var citacao = !atende && CitacaoFinal().Match(textoAtual) is { Success: true } m ? m.Value.TrimEnd() : "";
            var novoTexto = citacao.Length > 0 ? diferenca.TrimEnd('.') + citacao + "." : diferenca;
            // O texto só é refeito quando a situação muda ou quando ele já era um texto de diferença (o editor não apaga um texto livre).
            if (novoTexto != textoAtual && (novaSituacao != situacao || EhTextoDeDiferenca(textoAtual)))
                mudaTexto = pl.Definir(linha, CampoTexto, novoTexto);
        }
        var mudaSituacao = novaSituacao != situacao;
        if (mudaSituacao) pl.DefinirSituacao(linha, novaSituacao);
        if (mudaSituacao || mudaTexto)
        {
            // A conferência final exige justificativa quando a situação difere da original: o editor escreve a dele, se o usuário não escreveu.
            var justificativa = TabelasRevisao.Justificativa(p, linha);
            if (string.IsNullOrWhiteSpace(justificativa) || justificativa!.StartsWith(MarcaJustificativa, StringComparison.Ordinal))
            {
                var volta = novaSituacao == TabelasRevisao.SituacaoOriginal(p, linha);
                pl.DefinirJustificativa(linha, volta ? null : MarcaJustificativa + " no projeto " + textoValor + "; exigido " + textoLimite + ".");
            }
        }
        return new Avaliacao(Estado.Calculada, textoValor, textoLimite, atende, diferenca, novaSituacao, mudaSituacao, mudaTexto, divergente);
    }

    // ---------- o que acompanha a conta ----------

    /// <summary>O resultado da conta mudou: o "No projeto" acompanha, se era o resultado antigo (concordava com ele). Só o número muda
    /// ("35,1%" → "36,1%"; o resto do texto fica).</summary>
    static void SeguirResultado(Plano pl, string linha, ContaIndice? antes, ContaIndice depois)
    {
        if (antes is null) return;
        var texto = pl.Texto(linha, CampoValor);
        if (LerMedida(texto) is not { } valor || valor.Unidade != antes.Resultado.Unidade || !antes.ConcordaCom(valor)) return;
        if (antes.Resultado.Valor == depois.Resultado.Valor || depois.ConcordaCom(valor) && valor.Casas >= depois.Resultado.Casas) return;
        if (TrocarNumero(texto!, valor.Valor, valor.Casas, null, Formatar(depois.Resultado.Valor, depois.Resultado.Casas, "")) is { } novo)
            pl.Definir(linha, CampoValor, novo);
    }

    /// <summary>A área do lote mudou de <paramref name="antiga"/> para <paramref name="nova"/>: as contas das outras linhas que dividem
    /// por ela (e, com <paramref name="exigeVinculo"/>, que já dividiam pela área do lote no relatório recebido), a linha do terreno e a
    /// chamada "lote" do diagrama acompanham.</summary>
    static void PropagarArea(Plano pl, decimal antiga, decimal nova, string exceto, bool exigeVinculo)
    {
        var p = pl.P;
        foreach (var id in TabelasRevisao.Ids(p))
        {
            if (id == exceto || !TabelasRevisao.TentarLer(id, out var bloco, out _)) continue;
            if (bloco == TabelasRevisao.Chamadas)
            {
                if (TabelasRevisao.Original(p, id)?["ancora"] is JsonValue v && v.TryGetValue<string>(out var ancora) && ancora == "lote"
                    && TrocarNumero(pl.Texto(id, CampoTexto), antiga, null, "m²", Formatar(nova, ContaIndice.CasasDeArea, "")) is { } t)
                    pl.Definir(id, CampoTexto, t);
                continue;
            }
            if (bloco != TabelasRevisao.Indices) continue;
            if (EhTerreno(p, id))
            {
                if (LerMedida(pl.Texto(id, CampoValor)) is { Unidade: "m²" } m && m.Valor == antiga
                    && TrocarNumero(pl.Texto(id, CampoValor), antiga, null, null, Formatar(nova, ContaIndice.CasasDeArea, "")) is { } t)
                {
                    pl.Definir(id, CampoValor, t);
                    Reavaliar(pl, id, direta: false);
                }
                continue;
            }
            var calculo = pl.Texto(id, CampoCalculo);
            if (ContaIndice.Ler(calculo) is not { } conta || conta.Divisor.Valor != antiga || exigeVinculo && !UsaAreaDoLote(p, id)) continue;
            var novoCalculo = conta.TrocarDivisor(calculo!, nova);
            if (!pl.Definir(id, CampoCalculo, novoCalculo)) continue;
            SeguirResultado(pl, id, conta, ContaIndice.Ler(novoCalculo)!);
            Reavaliar(pl, id, direta: false);
        }
    }

    /// <summary>A taxa de permeabilidade mudou: a área permeável da planta do diagrama ("35% no projeto" / "35,1% no cálculo") acompanha,
    /// quando o número escrito lá é o antigo.</summary>
    static void DiagramaAcompanha(Plano pl, string linha)
    {
        var p = pl.P;
        if (!TabelasRevisao.TentarLer(linha, out var bloco, out _) || bloco != TabelasRevisao.Indices || !EhPermeabilidade(p, linha)) return;
        var planta = TabelasRevisao.Id(TabelasRevisao.Planta, 1);
        if (!TabelasRevisao.Existe(p, planta)) return;
        if (pl.Mudou(linha, CampoValor) && LerMedida(TabelasRevisao.TextoEfetivo(p, linha, CampoValor)) is { } va && LerMedida(pl.Texto(linha, CampoValor)) is { } vd
            && TrocarNumero(pl.Texto(planta, "permeavel/linha1"), va.Valor, va.Casas, "%", Formatar(vd.Valor, vd.Casas, "")) is { } l1)
            pl.Definir(planta, "permeavel/linha1", l1);
        if (pl.Mudou(linha, CampoCalculo) && ContaIndice.Ler(TabelasRevisao.TextoEfetivo(p, linha, CampoCalculo)) is { } ca
            && ContaIndice.Ler(pl.Texto(linha, CampoCalculo)) is { } cd && ca.Resultado.Valor != cd.Resultado.Valor
            && TrocarNumero(pl.Texto(planta, "permeavel/linha2"), ca.Resultado.Valor, ca.Resultado.Casas, "%", Formatar(cd.Resultado.Valor, cd.Resultado.Casas, "")) is { } l2)
            pl.Definir(planta, "permeavel/linha2", l2);
    }

    /// <summary>O texto com o número que vale <paramref name="valor"/> (na precisão <paramref name="casas"/>, ou exato) trocado por
    /// <paramref name="novo"/>; com <paramref name="unidade"/>, só o número seguido dela. Null se não há exatamente um número assim.</summary>
    static string? TrocarNumero(string? texto, decimal valor, int? casas, string? unidade, string novo)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        var achados = new List<Match>();
        foreach (Match m in Numeros().Matches(texto))
        {
            var bruto = m.Value.TrimEnd('.', ',');
            if (LerNumero(bruto) is not var (v, c)) continue;
            if (casas is { } k ? !ContaIndice.Concorda(v, c, valor, k) : v != valor) continue;
            if (unidade is not null)
            {
                var depois = texto[(m.Index + bruto.Length)..].TrimStart();
                var ok = unidade == "m²" ? depois.StartsWith("m²", StringComparison.Ordinal) || depois.StartsWith("m2", StringComparison.OrdinalIgnoreCase) : depois.StartsWith(unidade, StringComparison.Ordinal);
                if (!ok) continue;
            }
            achados.Add(m);
        }
        if (achados.Count != 1) return null;
        var a = achados[0];
        var tamanho = a.Value.TrimEnd('.', ',').Length;
        return texto[..a.Index] + novo + texto[(a.Index + tamanho)..];
    }

    // ---------- que linha é qual ----------

    static string Nome(Projeto p, string linha) =>
        SemAcento(TabelasRevisao.TextoOriginal(p, linha, "nome") ?? "") + " " + SemAcento(TabelasRevisao.TextoEfetivo(p, linha, "nome") ?? "");

    static string SemAcento(string s) =>
        new string(s.Normalize(System.Text.NormalizationForm.FormD)
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();

    static string? Icone(Projeto p, string linha) =>
        TabelasRevisao.Original(p, linha)?["icone"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A linha "Área do terreno" (ícone <c>plot</c> do kit, ou o nome diz terreno/área do lote) com o valor em m².</summary>
    static bool EhTerreno(Projeto p, string linha)
    {
        if (LerMedida(TabelasRevisao.TextoOriginal(p, linha, CampoValor)) is not { Unidade: "m²" }) return false;
        var nome = Nome(p, linha);
        return Icone(p, linha) == "plot" || nome.Contains("terreno", StringComparison.Ordinal) || nome.Contains("area do lote", StringComparison.Ordinal);
    }

    static bool EhPermeabilidade(Projeto p, string linha) => Icone(p, linha) == "tp" || Nome(p, linha).Contains("permeab", StringComparison.Ordinal);

    [GeneratedRegex(@"(?<n>[0-9]+(?:[.,][0-9]+)*)\s*(?:m²|m2|M2)", RegexOptions.CultureInvariant)]
    private static partial Regex AreaEmM2();

    /// <summary>A área do lote no relatório recebido: o valor da linha do terreno, senão a única área em m² do subtítulo da tabela
    /// ("Lote de 360,00 m² (matrícula) • zona ZOC 2."). Null se não se sabe.</summary>
    static decimal? AreaDoLoteOriginal(Projeto p)
    {
        foreach (var id in TabelasRevisao.Ids(p))
            if (TabelasRevisao.TentarLer(id, out var b, out _) && b == TabelasRevisao.Indices && EhTerreno(p, id)
                && LerMedida(TabelasRevisao.TextoOriginal(p, id, CampoValor)) is { Unidade: "m²" } m)
                return m.Valor;
        if (SubtituloDaTabela(p) is not { } sub) return null;
        var ms = AreaEmM2().Matches(sub);
        return ms.Count == 1 && LerNumero(ms[0].Groups["n"].Value) is var (v, _) ? v : null;
    }

    static string? SubtituloDaTabela(Projeto p) =>
        (p.Documento["indices"] as JsonObject)?["subtitulo"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static bool TemSubtituloComArea(Projeto p) => SubtituloDaTabela(p) is { } s && AreaEmM2().IsMatch(s);

    /// <summary>A conta da linha, como veio no relatório, divide pela área do lote (é uma taxa ou coeficiente sobre o terreno).</summary>
    static bool UsaAreaDoLote(Projeto p, string linha) =>
        AreaDoLoteOriginal(p) is { } area && ContaIndice.Ler(TabelasRevisao.TextoOriginal(p, linha, CampoCalculo)) is { } c && c.Divisor.Valor == area;
}

/// <summary>Uma mudança do "Rever" sem IA: a linha, o nome do que mudou ("No projeto", "situação") e o valor de antes e o de agora.</summary>
public sealed record ValorRevisto(string Linha, string Campo, string Antes, string Depois);

/// <summary>O resultado do "Rever" sem IA: o <see cref="Lote"/> (um Desfazer; null = nada a mudar), as mudanças com os valores anteriores
/// e os novos, e a mensagem para a tela.</summary>
public sealed record RevisaoDaTabela(Lote? Lote, IReadOnlyList<ValorRevisto> Mudancas, string Mensagem);

public static partial class RecalculoLinha
{
    /// <summary>A descrição do "Rever" sem IA no histórico.</summary>
    public const string DescricaoRever = "Revisto pelo editor (sem IA)";

    /// <summary>As tabelas que o editor revê sozinho (Tarefa 15): ÍNDICES URBANÍSTICOS e as esquadrias dos ambientes. O diagrama
    /// (chamadas, planta, volumetria) não tem conta: lá o "Rever" continua sendo com a IA.</summary>
    public static bool ReveSemIa(string linha) =>
        TabelasRevisao.TentarLer(linha, out var bloco, out _) && bloco is TabelasRevisao.Indices or TabelasRevisao.Ambientes;

    /// <summary>O "Rever" de uma linha de tabela vai à IA? Só sob comando (Tarefa 15): numa tabela sem conta, ou quando o revisor digitou uma
    /// instrução livre na justificativa que ainda não foi atendida (<paramref name="justificativaPendente"/>). A justificativa escrita pelo
    /// próprio editor ("Recalculado pelo editor: …") não é instrução.</summary>
    public static bool PrecisaDaIa(string linha, string? justificativa, bool justificativaPendente)
    {
        ArgumentNullException.ThrowIfNull(linha);
        if (!ReveSemIa(linha)) return true;
        return justificativaPendente && !string.IsNullOrWhiteSpace(justificativa)
               && !justificativa.TrimStart().StartsWith(MarcaJustificativa, StringComparison.Ordinal);
    }

    /// <summary>"Rever" sem IA (Tarefa 15) a tabela da linha <paramref name="linha"/>, inteira: nos índices, cada conta do "Cálculo /
    /// descrição" é refeita, o "No projeto" que era o resultado dela acompanha e a situação numérica é comparada de novo com o limite (pelo
    /// valor exato da conta, como o kit); FALTA COMPROVAR e INFORMAÇÕES DIVERGENTES ficam enquanto os números não mudarem. Nos ambientes, a
    /// conta das esquadrias (<see cref="Esquadrias.Recalcular"/>). Tudo num <see cref="Lote"/> (um Desfazer), com as mudanças de antes e de agora.</summary>
    public static RevisaoDaTabela Rever(Projeto p, string linha)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (!TabelasRevisao.TentarLer(linha, out var bloco, out _) || !ReveSemIa(linha))
            throw new ArgumentException("O editor só revê sem IA as tabelas de índices e de esquadrias.", nameof(linha));
        var comandos = new List<Comando>();
        var ids = TabelasRevisao.Ids(p).Where(i => TabelasRevisao.TentarLer(i, out var b, out _) && b == bloco).ToList();
        if (bloco == TabelasRevisao.Indices)
        {
            var pl = new Plano(p);
            foreach (var id in ids)
            {
                if (pl.Texto(id, CampoCalculo) is { } calculo && ContaIndice.Ler(calculo) is { } conta && !conta.ResultadoConfere)
                {
                    var refeito = conta.Refazer(calculo);
                    pl.Definir(id, CampoCalculo, refeito);
                    SeguirResultado(pl, id, conta, ContaIndice.Ler(refeito)!);
                }
                Reavaliar(pl, id, direta: false);
            }
            foreach (var l in pl.Linhas.ToList()) DiagramaAcompanha(pl, l);
            comandos.AddRange(pl.Comandos());
        }
        else
            foreach (var id in ids) comandos.AddRange(Esquadrias.Recalcular(p, id, [], DescricaoRever).Comandos);

        var mudancas = new List<ValorRevisto>();
        foreach (var c in comandos)
        {
            if (c is DefinirCampo dc)
                mudancas.Add(new ValorRevisto(dc.Item, TabelasRevisao.NomeDoCampo(dc.Campo), TabelasRevisao.TextoEfetivo(p, dc.Item, dc.Campo) ?? "", dc.Texto));
            else if (c is DefinirSituacao ds)
                mudancas.Add(new ValorRevisto(ds.Item, "situação", ConferenciaRevisao.TextoSituacao(TabelasRevisao.SituacaoEfetiva(p, ds.Item)),
                    ConferenciaRevisao.TextoSituacao(ds.Situacao)));
        }
        if (comandos.Count == 0)
            return new RevisaoDaTabela(null, [], "Revisto pelo editor, sem IA: as contas da tabela conferem com os números dela; nada mudou.");
        const int Mostradas = 8;
        var lista = string.Join("; ", mudancas.Take(Mostradas).Select(m => TabelasRevisao.Rotulo(m.Linha) + " — " + m.Campo + ": " + m.Antes + " → " + m.Depois));
        if (mudancas.Count > Mostradas) lista += "; e mais " + (mudancas.Count - Mostradas).ToString(CultureInfo.InvariantCulture);
        var mensagem = "Revisto pelo editor, sem IA: " + (lista.Length > 0 ? lista : "justificativa do editor atualizada") + ". Um Desfazer volta tudo.";
        return new RevisaoDaTabela(new Lote(linha, comandos, DescricaoRever), mudancas, mensagem);
    }
}

/// <summary>Os índices urbanísticos a partir dos operandos (Tarefa 15): a mesma conta do calcular_indices.py do kit para a taxa de
/// ocupação, o coeficiente de aproveitamento e a taxa de permeabilidade, com os limites da zona no Anexo VI do PDM
/// (references/anexo-vi-zonas.json do kit). A comparação usa o valor exato, sem arredondar a favor; só a exibição é arredondada, e com as
/// casas precisas para o número não parecer do outro lado do limite. Os casos-ouro do kit (casos-ouro.json) conferem que o C# e o Python dão
/// o mesmo resultado. Lote, pavimentos e afastamentos não estão aqui: não têm conta de divisão (o editor os compara pelo texto da linha).</summary>
public static class RecalculoIndices
{
    static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Uma linha calculada, com os textos como o kit os escreve na tabela e o valor exato (null = falta operando: PENDENTE).</summary>
    public sealed record IndiceCalculado(string Icone, string Nome, string Situacao, decimal? Valor, string Calculo, string Projeto, string Limite, string Texto);

    sealed record Taxa(string Icone, string Nome, string Limite, string Operando, string Rotulo, string NomeOperando, bool Maximo, bool Percentual);

    static readonly Taxa[] Taxas =
    [
        new("to", "Taxa de ocupação (TO)", "to_max", "area_projecao", "projeção", "área de projeção", true, true),
        new("ca", "Coeficiente de aproveitamento (CA)", "ca_max", "area_computavel", "área computável", "área computável", true, false),
        new("tp", "Taxa de permeabilidade (TP)", "tp_min", "area_permeavel", "área permeável", "área permeável", false, true),
    ];

    static string Chave(string? texto) => new((texto ?? "").ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9').ToArray());

    /// <summary>O código da zona como o Anexo VI o escreve ("zrc 1" → "ZRC-1"). Zona fora do anexo: <see cref="ArgumentException"/>, e nada é calculado.</summary>
    public static string ZonaCanonica(JsonObject anexo, string zona)
    {
        ArgumentNullException.ThrowIfNull(anexo);
        var zonas = anexo["zonas"] as JsonObject ?? throw new ArgumentException("O Anexo VI não tem a lista de zonas.", nameof(anexo));
        var alvo = Chave(zona);
        foreach (var (codigo, _) in zonas)
            if (Chave(codigo) == alvo) return codigo;
        throw new ArgumentException("A zona \"" + zona + "\" não consta do Anexo VI do PDM; nenhum índice foi calculado.", nameof(zona));
    }

    /// <summary>TO, CA e TP da zona com os operandos (<c>area_terreno</c>, <c>area_projecao</c>, <c>area_computavel</c>,
    /// <c>area_permeavel</c> e, opcional, <c>declarados</c> {to, ca, tp} do quadro de áreas), na ordem do kit.</summary>
    public static IReadOnlyList<IndiceCalculado> Calcular(JsonObject anexo, string zona, JsonObject operandos)
    {
        ArgumentNullException.ThrowIfNull(operandos);
        var z = (JsonObject)anexo["zonas"]![ZonaCanonica(anexo, zona)]!;
        var declarados = operandos["declarados"] as JsonObject;
        return Taxas.Select(t =>
        {
            var limite = Numero(z[t.Limite]?["valor"], t.Limite) ?? throw new ArgumentException("A zona não tem o limite " + t.Limite + ".", nameof(anexo));
            return CalcularTaxa(t, Numero(operandos[t.Operando], t.Operando), Numero(operandos["area_terreno"], "area_terreno"), limite,
                Numero(declarados?[t.Icone], "declarados." + t.Icone));
        }).ToList();
    }

    static decimal? Numero(JsonNode? n, string nome)
    {
        if (n is null) return null;
        if (n is JsonValue v && v.TryGetValue<decimal>(out var d) && d >= 0) return d;
        throw new ArgumentException(nome + " deve ser número (ou ausente quando não comprovado).", nameof(n));
    }

    static IndiceCalculado CalcularTaxa(Taxa t, decimal? operando, decimal? terreno, decimal limite, decimal? declarado)
    {
        var unidade = t.Percentual ? "%" : "";
        var limiteTexto = (t.Percentual && limite == decimal.Truncate(limite) ? Br(limite, 0) : Br(limite, 2)) + unidade;
        var limiteLinha = (t.Maximo ? "≤ " : "≥ ") + limiteTexto;
        var faltando = new List<string>();
        if (operando is null) faltando.Add(t.NomeOperando);
        if (terreno is not { } area || area == 0m) faltando.Add("área do terreno");
        if (faltando.Count > 0)
            return new IndiceCalculado(t.Icone, t.Nome, "PENDENTE", null,
                t.Rotulo + " ÷ área do terreno" + (t.Percentual ? " × 100" : "") + ": dado faltante",
                declarado is { } d0 ? Br(d0, 2) + unidade : "—", limiteLinha, "Falta comprovar: " + string.Join(", ", faltando) + ".");

        var valor = operando!.Value / terreno!.Value * (t.Percentual ? 100m : 1m);
        bool Atende(decimal x) => t.Maximo ? x <= limite : x >= limite;
        var ok = Atende(valor);
        var (texto, fiel) = Fiel(valor, Atende);
        var txt = texto + unidade + (fiel ? "" : valor > limite ? " (acima do limite)" : " (abaixo do limite)");
        var calculo = BrOperando(operando.Value) + " ÷ " + BrOperando(terreno.Value) + (t.Percentual ? " × 100" : "") + " = " + txt;
        string? declaradoTexto = null;
        var divergente = false;
        if (declarado is { } d)
        {
            var (td, fd) = Fiel(d, Atende);
            declaradoTexto = td + unidade + (fd ? "" : d > limite ? " (acima do limite)" : " (abaixo do limite)");
            divergente = Br(d, 2) != Br(valor, 2) || Atende(d) != ok;
        }
        var motivos = new List<string>();
        if (!ok)
            motivos.Add(texto + unidade + (t.Maximo ? " acima do máximo" : " abaixo do mínimo") + " de " + limiteTexto
                        + (fiel ? "" : " (diferença menor que 0,0000000001)") + ".");
        if (divergente) motivos.Add("O quadro declara " + declaradoTexto + "; a conta dá " + txt + ".");
        var situacao = !ok ? "NAO_ATENDE" : divergente ? "DIVERGENCIA" : "CONTA_CONFERE";
        return new IndiceCalculado(t.Icone, t.Nome, situacao, valor, calculo, !ok || declaradoTexto is null ? txt : declaradoTexto, limiteLinha,
            string.Join(" ", motivos));
    }

    /// <summary>"1.234,56": arredondamento meio para cima, ponto de milhar, vírgula decimal (o <c>br</c> do kit).</summary>
    static string Br(decimal v, int casas) =>
        Math.Round(v, casas, MidpointRounding.AwayFromZero).ToString("N" + casas.ToString(CultureInfo.InvariantCulture), PtBr);

    /// <summary>O operando como foi informado: 2 casas, ou as precisas para não alterar o número (até 10) — o <c>br_operando</c> do kit.</summary>
    static string BrOperando(decimal v)
    {
        for (var n = 2; n <= 10; n++)
            if (Math.Round(v, n, MidpointRounding.AwayFromZero) == v) return Br(v, n);
        return Enxuto(Br(v, 10));
    }

    /// <summary>As menores casas (de 2 a 10) em que o número arredondado fica do mesmo lado do limite que o exato — o <c>_fiel</c> do kit;
    /// fiel = false quando nem 10 casas mostram a diferença.</summary>
    static (string Texto, bool Fiel) Fiel(decimal v, Func<decimal, bool> atende)
    {
        for (var n = 2; n <= 10; n++)
            if (atende(Math.Round(v, n, MidpointRounding.AwayFromZero)) == atende(v)) return (Br(v, n), true);
        return (Enxuto(Br(v, 10)), false);
    }

    static string Enxuto(string texto)
    {
        var i = texto.LastIndexOf(',');
        if (i < 0) return texto;
        var dec = texto[(i + 1)..].TrimEnd('0');
        return texto[..i] + "," + dec.PadRight(2, '0');
    }
}
