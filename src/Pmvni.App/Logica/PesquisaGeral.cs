using System.Globalization;
using System.Text;
using Pmvni.Legislacao;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O tipo de um resultado da pesquisa geral (a caixa da barra de título): define o grupo, o ícone e o que acontece ao escolher.</summary>
public enum TipoResultadoGeral { Funcao, Modulo, Ajuste, Processo, Acompanhamento, Relatorio, Despacho, Lei, TrechoDeLei, Documento }

/// <summary>Uma linha dos Ajustes como está na tela: a etiqueta da categoria (<see cref="CategoriasAjustes"/>), o grupo, o título, a frase de
/// ajuda e, para a janela, o elemento que é trazido à vista ao escolher (null nos testes). <see cref="EhGrupo"/>: o título de um grupo de cartões.</summary>
public sealed record OpcaoAjusteTela(string Categoria, string Grupo, string Titulo, string Ajuda, object? Alvo = null, bool EhGrupo = false);

/// <summary>Uma linha da planilha do Acompanhamento, sem os tipos da planilha (a lógica é compilada também nos testes).</summary>
public sealed record ProcessoAcompanhado(string Processo, string Requerente, string Status, string Chave);

/// <summary>Um trecho achado no texto das leis e o termo que o achou.</summary>
public sealed record TrechoAchado(ResultadoPesquisa Resultado, string Termo);

/// <summary>Um item que a pesquisa geral pode achar: título e "onde" (o que a lista mostra), glifo, chave única (duplicados ficam com o
/// primeiro), palavras extras que também casam (descrição, requerente, nome do arquivo…) e o dado que a janela usa para abrir o resultado.</summary>
public sealed class ItemPesquisaGeral
{
    public ItemPesquisaGeral(TipoResultadoGeral tipo, string titulo, string onde, string glifo, string chave,
        IEnumerable<string?>? palavras = null, object? dado = null, Modulo? modulo = null, int? pontosFixos = null)
    {
        Tipo = tipo;
        Titulo = titulo;
        Onde = onde;
        Glifo = glifo;
        Chave = chave;
        Dado = dado;
        Modulo = modulo;
        PontosFixos = pontosFixos;
        TituloNormal = PesquisaGeral.Normalizar(titulo);
        PalavrasTitulo = PesquisaGeral.Palavras(TituloNormal);
        var extras = string.Join(' ', (palavras ?? []).Where(p => !string.IsNullOrWhiteSpace(p)));
        TudoNormal = PesquisaGeral.Normalizar(titulo + " " + onde + " " + extras);
        TodasPalavras = PesquisaGeral.Palavras(TudoNormal);
    }

    public TipoResultadoGeral Tipo { get; }
    public string Titulo { get; }
    public string Onde { get; }
    public string Glifo { get; }
    public string Chave { get; }
    public object? Dado { get; }
    /// <summary>Funções: o módulo em cuja faixa o comando está (o primeiro, na ordem do trilho).</summary>
    public Modulo? Modulo { get; }
    /// <summary>Resultado que já vem casado de fora (trechos do índice das leis): entra com estes pontos, sem comparar o termo.</summary>
    public int? PontosFixos { get; }

    internal string TituloNormal { get; }
    internal string[] PalavrasTitulo { get; }
    internal string TudoNormal { get; }
    internal string[] TodasPalavras { get; }

    /// <summary>A categoria do item na lista (1.0.0): Arquivos, Processos, Legislação, Configurações, Funções ou Módulos.</summary>
    public string Categoria => PesquisaGeral.NomeDoGrupo(Tipo);

    /// <summary>O nome acessível da linha: "Tema, Configurações, Ajustes › Geral › Aparência › Janela".</summary>
    public string NomeAcessivel => Onde.Length == 0 ? $"{Titulo}, {Categoria}" : $"{Titulo}, {Categoria}, {Onde}";

    public override string ToString() => $"{Tipo}: {Titulo} ({Onde})";
}

/// <summary>Um grupo da lista de resultados ("Funções", "Ajustes", "Processos"…), já na ordem de mostrar.</summary>
public sealed record GrupoPesquisaGeral(string Nome, IReadOnlyList<ItemPesquisaGeral> Itens);

/// <summary>Pesquisa geral da barra de título (2026-11-17; categorias e documentos em 1.0.0): procura em ajustes, funções da faixa, módulos,
/// processos, relatórios, despachos, documentos das pastas dos processos, leis (título e texto) e processos da planilha do Acompanhamento. Sem acento e sem caixa; cada palavra digitada tem de começar uma palavra do
/// item (ou estar dentro dele, com menos pontos). Ordem: igual &gt; começa com &gt; palavras &gt; contém. No máximo 8 por grupo e 30 ao todo.
/// Sem tipos WinUI: compilada também nos testes.</summary>
public static class PesquisaGeral
{
    public const int MinimoDoTermo = 1;
    public const int PorGrupo = 8;
    public const int Total = 30;
    public const string Placeholder = "Pesquisar arquivos, funções, leis e configurações...";
    public const string NomeAcessivel = "Pesquisar no app";

    // ---------- comparação ----------

    /// <summary>Minúsculas e sem acentos ("Relatórios" → "relatorios").</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>As palavras de um texto já normalizado: sequências de letras e dígitos ("15004/2026" → "15004", "2026").</summary>
    public static string[] Palavras(string normalizado)
    {
        var lista = new List<string>();
        var inicio = -1;
        for (var i = 0; i <= normalizado.Length; i++)
        {
            var letra = i < normalizado.Length && char.IsLetterOrDigit(normalizado[i]);
            if (letra && inicio < 0) inicio = i;
            else if (!letra && inicio >= 0) { lista.Add(normalizado[inicio..i]); inicio = -1; }
        }
        return lista.ToArray();
    }

    /// <summary>Os pontos do item para o termo (0 = não casa). Igual ao título 1000; o título começa com o termo 900; toda palavra digitada
    /// começa uma palavra do título 800 (750 se a primeira não é a primeira do título); começa uma palavra do item (onde, descrição…) 500;
    /// o título contém o termo 400; o item contém cada palavra 200.</summary>
    public static int Pontuar(ItemPesquisaGeral item, string termo)
    {
        var q = Normalizar(termo).Trim();
        return Pontuar(item, q, Palavras(q));
    }

    static int Pontuar(ItemPesquisaGeral item, string q, string[] tokens)
    {
        if (item.PontosFixos is { } fixos) return fixos;
        if (q.Length == 0 || tokens.Length == 0) return 0;
        var t = item.TituloNormal;
        if (t == q) return 1000;
        if (t.StartsWith(q, StringComparison.Ordinal)) return 900;
        if (tokens.All(tok => item.PalavrasTitulo.Any(w => w.StartsWith(tok, StringComparison.Ordinal))))
            return item.PalavrasTitulo.Length > 0 && item.PalavrasTitulo[0].StartsWith(tokens[0], StringComparison.Ordinal) ? 800 : 750;
        if (tokens.All(tok => item.TodasPalavras.Any(w => w.StartsWith(tok, StringComparison.Ordinal)))) return 500;
        if (t.Contains(q, StringComparison.Ordinal)) return 400;
        if (tokens.All(tok => item.TudoNormal.Contains(tok, StringComparison.Ordinal))) return 200;
        return 0;
    }

    /// <summary>O grupo do tipo (1.0.0: as seis categorias da lista — Arquivos, Processos, Legislação, Configurações, Funções, Módulos) e a
    /// ordem dos grupos quando empatam, nessa mesma sequência.</summary>
    public static string NomeDoGrupo(TipoResultadoGeral t) => t switch
    {
        TipoResultadoGeral.Relatorio or TipoResultadoGeral.Despacho or TipoResultadoGeral.Documento => "Arquivos",
        TipoResultadoGeral.Processo or TipoResultadoGeral.Acompanhamento => "Processos",
        TipoResultadoGeral.Lei or TipoResultadoGeral.TrechoDeLei => "Legislação",
        TipoResultadoGeral.Ajuste => "Configurações",
        TipoResultadoGeral.Funcao => "Funções",
        TipoResultadoGeral.Modulo => "Módulos",
        _ => "Outros",
    };

    /// <summary>As categorias, na ordem de desempate.</summary>
    public static readonly IReadOnlyList<string> Categorias = ["Arquivos", "Processos", "Legislação", "Configurações", "Funções", "Módulos"];

    static int OrdemDoGrupo(string nome)
    {
        for (var i = 0; i < Categorias.Count; i++) if (Categorias[i] == nome) return i;
        return Categorias.Count;
    }

    /// <summary>O título partido em trechos, com os que casam com as palavras do termo marcados (sem acento e sem caixa), para o destaque na
    /// lista (1.0.0). Sem termo ou sem casamento: o título inteiro, sem destaque.</summary>
    public static IReadOnlyList<(string Texto, bool Destaque)> Destacar(string titulo, string termo)
    {
        var tokens = Palavras(Normalizar(termo).Trim());
        if (tokens.Length == 0 || titulo.Length == 0) return [(titulo, false)];
        // O título normalizado letra a letra, lembrando de que letra original veio cada caractere (acentos e caixa não mudam a posição).
        var norm = new StringBuilder(titulo.Length);
        var origem = new List<int>(titulo.Length);
        for (var i = 0; i < titulo.Length; i++)
            foreach (var c in Normalizar(titulo[i].ToString())) { norm.Append(c); origem.Add(i); }
        var n = norm.ToString();
        var marca = new bool[titulo.Length];
        foreach (var tok in tokens)
            for (var k = n.IndexOf(tok, StringComparison.Ordinal); k >= 0; k = n.IndexOf(tok, k + 1, StringComparison.Ordinal))
                for (var j = k; j < k + tok.Length && j < origem.Count; j++) marca[origem[j]] = true;
        var trechos = new List<(string, bool)>();
        var ini = 0;
        for (var i = 1; i <= titulo.Length; i++)
            if (i == titulo.Length || marca[i] != marca[ini]) { trechos.Add((titulo[ini..i], marca[ini])); ini = i; }
        return trechos;
    }

    /// <summary>Pesquisa <paramref name="termo"/> nos itens: termo com menos de <see cref="MinimoDoTermo"/> letras não acha nada. Grupos na
    /// ordem do melhor resultado de cada um (empate: Funções, Ajustes, Processos, Acompanhamento, Arquivos, Leis, Texto das leis); dentro do
    /// grupo, mais pontos, título mais curto, ordem alfabética. Itens com a mesma <see cref="ItemPesquisaGeral.Chave"/>: fica o primeiro.</summary>
    public static IReadOnlyList<GrupoPesquisaGeral> Pesquisar(string termo, IEnumerable<ItemPesquisaGeral> itens, CancellationToken ct = default,
        int porGrupo = PorGrupo, int total = Total)
    {
        var q = Normalizar(termo).Trim();
        var tokens = Palavras(q);
        if (q.Length < MinimoDoTermo || tokens.Length == 0) return [];
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var achados = new List<(ItemPesquisaGeral Item, int Pontos)>();
        var n = 0;
        foreach (var item in itens)
        {
            if ((++n & 255) == 0) ct.ThrowIfCancellationRequested();
            if (!vistos.Add(item.Chave)) continue;
            var p = Pontuar(item, q, tokens);
            if (p > 0) achados.Add((item, p));
        }
        ct.ThrowIfCancellationRequested();
        var grupos = achados
            .GroupBy(a => NomeDoGrupo(a.Item.Tipo))
            .Select(g => (Nome: g.Key, Melhor: g.Max(a => a.Pontos),
                Itens: g.OrderByDescending(a => a.Pontos).ThenBy(a => a.Item.Titulo.Length).ThenBy(a => a.Item.Titulo, StringComparer.CurrentCultureIgnoreCase)
                    .Select(a => a.Item).ToList()))
            .OrderByDescending(g => g.Melhor).ThenBy(g => OrdemDoGrupo(g.Nome))
            .ToList();
        var resultado = new List<GrupoPesquisaGeral>();
        var restam = total;
        foreach (var g in grupos)
        {
            if (restam <= 0) break;
            var itensDoGrupo = g.Itens.Take(Math.Min(porGrupo, restam)).ToList();
            restam -= itensDoGrupo.Count;
            resultado.Add(new GrupoPesquisaGeral(g.Nome, itensDoGrupo));
        }
        return resultado;
    }

    // ---------- catálogo: funções, módulos e ajustes ----------

    /// <summary>Os módulos do trilho e os comandos das faixas (os itens dos menus como "Tipo › Correção"; menus com itens que só a janela
    /// conhece, como Recentes e Ano, ficam de fora). Um comando que aparece em vários módulos entra uma vez, com o primeiro módulo do trilho.
    /// O dado é o id do comando.</summary>
    public static IReadOnlyList<ItemPesquisaGeral> Funcoes()
    {
        var lista = new List<ItemPesquisaGeral>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in Faixas.Disponiveis)
        {
            var atalho = Modulos.TemAtalho(m) ? $" · Ctrl+{Modulos.Numero(m)}" : "";
            lista.Add(new ItemPesquisaGeral(TipoResultadoGeral.Modulo, Modulos.Rotulo(m), "Módulo" + atalho, Modulos.Glifo(m), "modulo:" + m,
                [Modulos.Descricao(m), "ir para", "abrir", SinonimosDoModulo(m)], Modulos.Comando(m), m));
        }
        foreach (var m in Faixas.Disponiveis)
        {
            var f = Faixas.Para(m);
            var rotulo = Modulos.Rotulo(m);
            foreach (var c in new[] { f.Primario, f.Secundario }.OfType<ComandoFaixa>())
                Acrescentar(c, $"Comando · {rotulo}");
            foreach (var aba in f.Abas)
                foreach (var c in aba.Comandos)
                    Acrescentar(c, $"Comando · {rotulo} › {aba.Nome}");

            void Acrescentar(ComandoFaixa c, string onde)
            {
                if (c.Tipo == TipoComando.Menu)
                {
                    foreach (var i in c.Itens ?? [])
                        Item(i.Id, $"{c.Rotulo} › {i.Rotulo}", c.Glifo, onde, [c.TextoCompleto, i.TextoCompleto]);
                    return;
                }
                Item(c.Id, c.Rotulo, c.Glifo, onde, [c.Descricao]);
            }

            void Item(string id, string titulo, string glifo, string onde, string?[] palavras)
            {
                if (!ids.Add(id)) return;
                var atalho = DicasComandos.Atalho(id, m);
                DicasComandos.Descricoes.TryGetValue(id, out var descricao);
                lista.Add(new ItemPesquisaGeral(TipoResultadoGeral.Funcao, titulo, atalho is null ? onde : $"{onde} · {atalho}",
                    string.IsNullOrEmpty(glifo) ? Glifos.Pesquisar : glifo, "funcao:" + id, [.. palavras, descricao], id, m));
            }
        }
        return lista;
    }

    static string SinonimosDoModulo(Modulo m) => m switch
    {
        Modulo.Ajustes => "configurações preferências opções",
        Modulo.Legislacao => "leis normas",
        Modulo.Acompanhamento => "planilha tramitação",
        Modulo.Imoveis => "qgis cadastro lotes",
        _ => "",
    };

    /// <summary>As categorias dos Ajustes (<see cref="CategoriasAjustes.Menu"/>) e as linhas e grupos que a página mostra. O dado é a
    /// <see cref="OpcaoAjusteTela"/> (a categoria inteira: título da categoria, sem alvo).</summary>
    public static IReadOnlyList<ItemPesquisaGeral> Ajustes(IEnumerable<OpcaoAjusteTela> opcoes)
    {
        var lista = new List<ItemPesquisaGeral>();
        // Uma subpágina (Modelos da análise) mostra a categoria-mãe no "onde".
        static string Raiz(CategoriaAjustes c) => c.Pai is { } pai ? $"Ajustes › {CategoriasAjustes.De(pai).Titulo}" : "Ajustes";
        foreach (var c in CategoriasAjustes.Menu)
            lista.Add(new ItemPesquisaGeral(TipoResultadoGeral.Ajuste, c.Titulo, Raiz(c), c.Glifo, "ajuste:" + c.Tag,
                [c.Ajuda, "configurações"], new OpcaoAjusteTela(c.Tag, "", c.Titulo, c.Ajuda)));
        foreach (var o in opcoes)
        {
            if (string.IsNullOrWhiteSpace(o.Titulo)) continue;
            var c = CategoriasAjustes.De(o.Categoria);
            var onde = o.EhGrupo || o.Grupo.Length == 0 ? $"{Raiz(c)} › {c.Titulo}" : $"{Raiz(c)} › {c.Titulo} › {o.Grupo}";
            lista.Add(new ItemPesquisaGeral(TipoResultadoGeral.Ajuste, o.Titulo, onde, c.Glifo,
                $"ajuste:{c.Tag}:{(o.EhGrupo ? "grupo" : o.Grupo)}:{o.Titulo}", [o.Ajuda], o));
        }
        return lista;
    }

    // ---------- catálogo: processos, arquivos, leis, planilha ----------

    /// <summary>"15004/2026" (só o número se faltar o ano; null se a pasta não segue o padrão).</summary>
    public static string? NumeroDoProcesso(Processo? p) => p?.Numero is not { } n ? null
        : p.Ano is { } a ? $"{n.ToString(CultureInfo.InvariantCulture)}/{a.ToString(CultureInfo.InvariantCulture)}" : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Uma linha por pasta de processo: o título é o requerente (ou o nome da pasta), o "onde" é "Processo 15004/2026". Dado: o <see cref="Processo"/>.</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeProcessos(IEnumerable<Processo> processos) =>
        processos.Select(p =>
        {
            var numero = NumeroDoProcesso(p);
            return new ItemPesquisaGeral(TipoResultadoGeral.Processo, p.Requerente ?? p.NomePasta, numero is null ? "Pasta de processo" : "Processo " + numero,
                Glifos.Pasta, "processo:" + p.Pasta.ToUpperInvariant(), [p.NomePasta, numero], p);
        }).ToList();

    /// <summary>Arquivos achados nas pastas dos processos: <c>.rpmvni</c>/<c>.pmvrel</c> são relatórios (abrem no editor); <c>.docx</c>,
    /// <c>.doc</c> e <c>.pdf</c> das pastas <c>DESPACHO NN</c> são despachos (abrem no programa deles); os demais (1.0.0: DOCUMENTOS,
    /// PROJETO e a raiz do processo) são documentos, que também abrem no programa deles. Dado: o caminho.</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeArquivos(IEnumerable<(string Caminho, Processo? Processo)> arquivos) =>
        arquivos.Select(a =>
        {
            var ext = Path.GetExtension(a.Caminho);
            var relatorio = ext.Equals(".rpmvni", StringComparison.OrdinalIgnoreCase) || ext.Equals(".pmvrel", StringComparison.OrdinalIgnoreCase);
            var numero = NumeroDoProcesso(a.Processo);
            var pasta = Path.GetFileName(Path.GetDirectoryName(a.Caminho)) ?? "";
            var doProcesso = numero is not null ? " · Processo " + numero : a.Processo is not null ? " · " + a.Processo.NomePasta : "";
            var despacho = !relatorio && (pasta.StartsWith("DESPACHO", StringComparison.OrdinalIgnoreCase) || a.Processo is null);
            var tipo = relatorio ? TipoResultadoGeral.Relatorio : despacho ? TipoResultadoGeral.Despacho : TipoResultadoGeral.Documento;
            var naRaiz = a.Processo is not null && string.Equals(Path.GetDirectoryName(a.Caminho), a.Processo.Pasta, StringComparison.OrdinalIgnoreCase);
            var onde = tipo switch
            {
                TipoResultadoGeral.Relatorio => "Relatório" + doProcesso,
                TipoResultadoGeral.Despacho => "Despacho" + doProcesso + (pasta.StartsWith("DESPACHO", StringComparison.OrdinalIgnoreCase) ? " · " + pasta : ""),
                _ => "Documento" + doProcesso + (naRaiz ? "" : " · " + pasta),
            };
            var glifo = tipo switch { TipoResultadoGeral.Relatorio => Glifos.Relatorio, TipoResultadoGeral.Despacho => Glifos.Despacho, _ => Glifos.Documento };
            var palavra = tipo switch { TipoResultadoGeral.Relatorio => "relatório", TipoResultadoGeral.Despacho => "despacho", _ => "documento arquivo" };
            return new ItemPesquisaGeral(tipo, Path.GetFileName(a.Caminho), onde, glifo, "arquivo:" + a.Caminho.ToUpperInvariant(),
                [a.Processo?.Requerente, a.Processo?.NomePasta, pasta, palavra], a.Caminho);
        }).ToList();

    /// <summary>Relatórios abertos há pouco (Arquivo › Recentes). Dado: o caminho.</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeRecentes(IEnumerable<string> caminhos) =>
        caminhos.Select(c => new ItemPesquisaGeral(TipoResultadoGeral.Relatorio, Path.GetFileName(c), "Relatório recente",
            Glifos.Relatorio, "arquivo:" + c.ToUpperInvariant(), [Path.GetFileName(Path.GetDirectoryName(c)), "relatório recente"], c)).ToList();

    /// <summary>Os despachos já lidos pelo módulo Despachos: o arquivo que "Abrir" abriria (o .docx, senão o PDF). Dado: o caminho.</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeDespachos(IEnumerable<Despacho> despachos) =>
        despachos.Select(d => (d, Arquivo: d.Arquivos.FirstOrDefault(a => Path.GetExtension(a).ToLowerInvariant() is ".docx" or ".doc")
                                          ?? d.Arquivos.FirstOrDefault()))
            .Where(x => x.Arquivo is not null)
            .SelectMany(x => DeArquivos([(x.Arquivo!, x.d.Processo)]))
            .ToList();

    /// <summary>As leis anexadas: título, esfera e nome do arquivo original. Dado: o id do anexo.</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeLeis(IEnumerable<Anexo> leis) =>
        leis.Select(a => new ItemPesquisaGeral(TipoResultadoGeral.Lei, a.Titulo, "Lei · " + Esferas.Rotulo(a.Esfera), Glifos.Livro,
            "lei:" + a.Id, [a.ArquivoOriginal, "lei legislação"], a.Id)).ToList();

    /// <summary>Trechos achados pelo índice de texto das leis (já casados: entram com 150 pontos, abaixo de qualquer título). Dado: o
    /// <see cref="TrechoAchado"/> (a lei abre na página, com o termo procurado nela).</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeTrechos(IEnumerable<ResultadoPesquisa> resultados, IReadOnlyDictionary<string, Anexo> leis, string termo) =>
        resultados.Select(r => new ItemPesquisaGeral(TipoResultadoGeral.TrechoDeLei, Encurtar(r.Trecho, 120),
            $"{(leis.TryGetValue(r.AnexoId, out var a) ? a.Titulo : "Lei")} · p. {r.Pagina.ToString(CultureInfo.InvariantCulture)}",
            Glifos.Citacao, $"trecho:{r.AnexoId}:{r.Pagina.ToString(CultureInfo.InvariantCulture)}", null, new TrechoAchado(r, termo), pontosFixos: 150)).ToList();

    /// <summary>Os processos da planilha do Acompanhamento. Dado: a chave do processo (o número sem espaços).</summary>
    public static IReadOnlyList<ItemPesquisaGeral> DeAcompanhamento(IEnumerable<ProcessoAcompanhado> linhas) =>
        linhas.Where(l => l.Processo.Length > 0).Select(l => new ItemPesquisaGeral(TipoResultadoGeral.Acompanhamento,
            l.Requerente.Length > 0 ? $"{l.Processo} · {l.Requerente}" : l.Processo,
            "Acompanhamento" + (l.Status.Length > 0 ? " · " + l.Status : ""), Glifos.Grafico, "acompanhamento:" + l.Chave,
            [l.Chave, l.Status, "planilha"], l.Chave)).ToList();

    static string Encurtar(string s, int max)
    {
        var t = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    }
}
