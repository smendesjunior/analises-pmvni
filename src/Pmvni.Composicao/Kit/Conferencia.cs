using System.Collections;
using System.Globalization;
using System.Text;

namespace Pmvni.Composicao.Kit;

/// <summary>Conferência dos dados do relatório e linha-resumo (<c>conferir</c> e <c>resumo</c> do gerar_relatorio.py do kit).</summary>
public static class Conferencia
{
    private static string Dois(int i) => i.ToString("00", CultureInfo.InvariantCulture);

    private static readonly (string Caminho, string Campos)[] CamposImpressos =
    [
        ("capa", "protocolo rotulo_data data processo requerente pontos_confirmar"),
        ("capa.quadros", "titulo destaque apoio"),
        ("itens", "titulo verificacao providencia base_legal referencia"),
        ("fontes", "rotulo texto"),
        ("assinatura", "nome cargo"),
        ("", "conclusao"),
        ("localizacao", "fonte"),
        ("localizacao.dados", "rotulo valor"),
        ("localizacao.legenda", "texto"),
        ("indices.linhas", "nome descricao calculo projeto limite"),
        ("indices.declaracoes", "rotulo valor fonte"),
        ("indices.sem_calculo", "motivo evidencia"),
        ("indices.pendencias_quadro", "rotulo fonte motivo"),
        ("diagrama", "nota"),
        ("diagrama.chamadas", "titulo texto"),
        ("iluminacao", "fonte"),
        ("iluminacao.ambientes", "nome"),
        ("iluminacao.ambientes.vaos", "codigo tipo"),
        ("iluminacao.ambientes.minimo", "texto"),
        ("iluminacao.ambientes.resultado", "total conta comparacao"),
        ("figuras", "prancha titulo fonte"),
        ("figuras.notas", "elemento itens"),
        ("historico_reanalise.pendencias", "origem situacao pendencia confronto evidencia"),
    ];

    private static IEnumerable<(string Local, IReadOnlyDictionary<string, object?> Dados)> Objetos(
        object? no, string[] caminho, int posicao, string local)
    {
        if (no is IReadOnlyDictionary<string, object?> objeto)
        {
            if (posicao == caminho.Length) yield return (local, objeto);
            else if (objeto.TryGetValue(caminho[posicao], out var filho))
            {
                var proximo = local.Length == 0 ? caminho[posicao] : local + "." + caminho[posicao];
                foreach (var item in Objetos(filho, caminho, posicao + 1, proximo)) yield return item;
            }
        }
        else if (no is IEnumerable lista and not string)
        {
            var i = 0;
            foreach (var filho in lista)
                foreach (var item in Objetos(filho, caminho, posicao, $"{local}[{++i}]")) yield return item;
        }
    }

    private static bool EmSet(object? v, IReadOnlySet<string> conjunto) => v is string s && conjunto.Contains(s);

    private static bool TextoPreenchido(object? v) => v is string s && s.Trim().ToLowerInvariant() is not ("" or "-" or "—" or "…" or "none" or "null");
    private static readonly HashSet<string> MarcadoresZona = """
        a o do da de em no na se por para com e ainda esta zona zoneamento enquadramento urbanistico urbanistica
        nao sem n nd ni consta informado informada informacao identificado identificada identificacao definido
        definida definicao comprovado comprovada comprovacao conhecido conhecida confirmar confirmado confirmada
        confirmacao aguarda aguardando pendente pendencia verificar verificacao validar validacao indisponivel
        indefinido indefinida desconhecido desconhecida especificado especificada aplicavel aplica
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static bool MarcadorZona(object? valor)
    {
        if (valor is not string texto) return true;
        var normal = string.Concat(texto.ToLowerInvariant().Normalize(NormalizationForm.FormKD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        return System.Text.RegularExpressions.Regex.Matches(normal, "[a-z0-9]+").All(p => MarcadoresZona.Contains(p.Value));
    }

    private static bool Tem(object? o, string chave) => PythonTexto.Verdadeiro(Dados.Obter(SeVerdadeiro(o), chave));
    private static bool Pendente(object? linha)
    {
        var situacao = Dados.Obter(SeVerdadeiro(Dados.Obter(linha, "apresentacao")), "situacao") ?? Dados.Obter(linha, "situacao");
        return situacao is "NAO_ATENDE" or "NAO_COMPROVADO" or "DIVERGENCIA_INTERNA" or "DIVERGENCIA" or "PENDENTE";
    }

    /// <summary>Lista de erros, na mesma ordem e com o mesmo texto do kit. <paramref name="imagemExiste"/> recebe o caminho
    /// relativo e diz se é um arquivo existente (no kit: <c>(base / caminho).resolve().is_file()</c>).</summary>
    public static IReadOnlyList<string> Conferir(IReadOnlyDictionary<string, object?> d, Func<string, bool> imagemExiste, bool rascunho)
    {
        var erros = new List<string>();
        bool Existe(object? imagem)
        {
            var rel = imagem switch { string s => s, _ => throw new MoldeErro("unsupported operand type(s) for /: 'PosixPath' and '" + PythonTexto.NomeTipo(imagem) + "'") };
            return rel.Length > 0 && imagemExiste(rel); // caminho vazio é a própria pasta: não é arquivo
        }

        foreach (var k in new[] { "capa", "itens", "fontes", "conclusao", "assinatura" })
            if (!PythonTexto.Verdadeiro(Dados.Obter(d, k))) erros.Add("Campo obrigatório ausente: " + k);

        var i = 0;
        foreach (var it in Dados.Lista(Dados.Obter(d, "itens", new List<object?>())))
        {
            i++;
            var sit = Dados.Obter(it, "situacao");
            if (!EmSet(sit, Situacoes.SitItem)) erros.Add("ITEM " + Dois(i) + ": situação inválida " + PythonTexto.Repr(sit));
            foreach (var c in new[] { "titulo", "verificacao", "providencia", "base_legal", "referencia" })
                if (Dados.Txt(Dados.Obter(it, c, "")).Trim().Length == 0) erros.Add("ITEM " + Dois(i) + ": campo vazio " + c);
        }

        var linhas = Dados.Lista(Dados.Obter(SeVerdadeiro(Dados.Obter(d, "indices")), "linhas", new List<object?>()));
        if (!rascunho && d.TryGetValue("indices", out var blocoIndices))
        {
            if (blocoIndices is not IReadOnlyDictionary<string, object?> || (linhas.Count > 0) == Tem(blocoIndices, "sem_calculo"))
                erros.Add("Índices: apresente as linhas ou justifique a ausência com motivo e evidência.");
            if (linhas.Count > 0)
            {
                var zona = Dados.Obter(blocoIndices, "zona") as string;
                if (string.IsNullOrWhiteSpace(zona)) erros.Add("Índices: falta a identificação da zona ou de sua pendência.");
                else if (MarcadorZona(zona) && MarcadorZona(Dados.Obter(blocoIndices, "observacao")))
                    erros.Add("Índices: explique na observação qual peça, parcela ou informação falta para confirmar a zona.");
            }
        }
        if (Dados.Obter(d, "indices") is IReadOnlyDictionary<string, object?> indices && !linhas.Any()
            && indices.TryGetValue("sem_calculo", out var lacuna)
            && (lacuna is not IReadOnlyDictionary<string, object?> dado ||
                string.IsNullOrWhiteSpace(Dados.Obter(dado, "motivo") as string) ||
                string.IsNullOrWhiteSpace(Dados.Obter(dado, "evidencia") as string)))
            erros.Add("Índices sem cálculo: informe motivo e evidência da peça examinada.");
        // Pacotes antigos não exigiam diagrama. Podem ser reabertos e reexportados na revisão original.
        var revisao = Dados.Txt(Dados.Obter(d, "revisao_modelo_relatorio"));
        if (!rascunho && linhas.Count > 0 && (!int.TryParse(revisao, out var numero) || numero >= 12)
            && !PythonTexto.Verdadeiro(Dados.Obter(SeVerdadeiro(Dados.Obter(d, "diagrama")), "chamadas")))
            erros.Add("Tabela de índices sem diagrama de conferência.");
        foreach (var l in linhas)
        {
            var sit = Dados.Obter(l, "situacao");
            if (!rascunho && sit is not "CONTA_CONFERE" && !TextoPreenchido(Dados.Obter(l, "texto")) && !Tem(Dados.Obter(l, "apresentacao"), "nota"))
                erros.Add("Índice \"" + Dados.Txt(Dados.Obter(l, "nome")) + "\": falta justificar o resultado indicado.");
            if (!EmSet(sit, Situacoes.SitIndice))
                erros.Add("Índice \"" + Dados.Txt(Dados.Obter(l, "nome")) + "\": situação inválida " + PythonTexto.Repr(sit));
        }
        var iluminacao = Dados.Obter(d, "iluminacao");
        if (!rascunho && PythonTexto.Verdadeiro(iluminacao) && !Tem(iluminacao, "ambientes"))
            erros.Add("Iluminação: a tabela exige ao menos um ambiente.");
        var ambientes = Dados.Lista(Dados.Obter(SeVerdadeiro(iluminacao), "ambientes", new List<object?>()));
        foreach (var a in ambientes)
        {
            var sit = Dados.Obter(a, "situacao");
            if (!rascunho && (!Tem(a, "vaos") || !Tem(a, "minimo")))
                erros.Add("Ambiente \"" + Dados.Txt(Dados.Obter(a, "nome")) + "\": faltam as aberturas ou o mínimo exigido.");
            if (!rascunho && !Tem(a, "resultado") && !Tem(a, "situacao_titulo") && !Tem(a, "textos") && !Tem(Dados.Obter(a, "apresentacao"), "nota"))
                erros.Add("Ambiente \"" + Dados.Txt(Dados.Obter(a, "nome")) + "\": falta o resultado ou a justificativa da situação.");
            if (!EmSet(sit, Situacoes.SitIndice))
                erros.Add("Ambiente \"" + Dados.Txt(Dados.Obter(a, "nome")) + "\": situação inválida " + PythonTexto.Repr(sit));
            foreach (var vao in Dados.Lista(Dados.Obter(a, "vaos", new List<object?>())))
                if (string.IsNullOrWhiteSpace(Dados.Txt(Dados.Obter(vao, "area", "")))
                    && string.IsNullOrWhiteSpace(Dados.Txt(Dados.Obter(vao, "sem_area", ""))))
                    erros.Add("Ambiente \"" + Dados.Txt(Dados.Obter(a, "nome")) + "\": abertura sem área e sem justificativa.");
        }

        if (!rascunho && Dados.Obter(d, "historico_reanalise") is { } historico && !Tem(historico, "pendencias") && !Tem(historico, "sem_pendencias"))
            erros.Add("Reanálise: informe as pendências ou a evidência de que não havia pendências.");
        var figuras = Dados.Obter(d, "figuras", new List<object?>()) as List<object?>;
        if (figuras is null) erros.Add("Figuras: informe uma lista de objetos.");
        var n = 0;
        foreach (var f in figuras ?? [])
        {
            n++;
            if (f is not IReadOnlyDictionary<string, object?> figura)
            {
                erros.Add($"Figura {n}: informe um objeto com os dados da figura.");
                continue;
            }
            var notas = Dados.Obter(figura, "notas", new List<object?>()) as List<object?>;
            if (notas is null) erros.Add($"Figura {n}: notas deve ser uma lista de objetos.");
            notas ??= [];
            if (!rascunho && notas.Count == 0 && !TextoPreenchido(Dados.Obter(figura, "texto")))
                erros.Add($"Figura {n}: falta a legenda que identifica o conteúdo ilustrado.");
            for (var j = 0; j < notas.Count; j++)
            {
                if (notas[j] is not IReadOnlyDictionary<string, object?> nota)
                {
                    erros.Add($"Figura {n}, nota {j + 1}: informe um objeto com elemento, texto e itens.");
                    continue;
                }
                if (Dados.Obter(nota, "texto", "") is not string textoNota)
                    erros.Add($"Figura {n}, nota {j + 1}: texto deve ser uma cadeia de caracteres.");
                else if (!rascunho && TextoPreenchido(textoNota) &&
                    string.Equals(textoNota.Trim().Trim('.'), (Dados.Obter(nota, "elemento") as string)?.Trim().Trim('.'), StringComparison.OrdinalIgnoreCase))
                    erros.Add($"Figura {n}, nota {j + 1}: a explicação repete o elemento; informe a providência ou o que foi conferido.");
            }
            if (notas.Count > 6) erros.Add($"Figura {n}: mais de 6 indicações; divida em recortes de detalhe");
            var imagem = Dados.Obter(figura, "imagem");
            if (imagem is not string caminho || string.IsNullOrWhiteSpace(caminho) || !Existe(caminho))
                erros.Add($"Figura {n}: imagem não encontrada ({Dados.Txt(imagem)})");
        }

        if (Dados.Len(Dados.Obter(Dados.Obter(d, "capa", new Dictionary<string, object?>()), "quadros", new List<object?>())) != 4)
            erros.Add("A capa precisa de exatamente 4 quadros");

        var loc = Dados.Obter(d, "localizacao");
        if (PythonTexto.Verdadeiro(loc))
        {
            var sit = Dados.Obter(loc, "situacao");
            if (!EmSet(sit, Situacoes.SitLocal))
                erros.Add("Localização: situação inválida " + PythonTexto.Repr(sit) + " (CONFIRMADO ou A_CONFIRMAR)");
            if (!Existe(Dados.Obter(loc, "imagem", "")))
                erros.Add("Localização: mapa não encontrado (" + Dados.Txt(Dados.Obter(loc, "imagem")) + ")");
            if (Dados.Len(Dados.Obter(loc, "dados", new List<object?>())) < 3)
                erros.Add("Localização: a ficha precisa de ao menos 3 dados (inscrição, lote, ruas…)");
            if (sit is string ss && ss == "A_CONFIRMAR" && Dados.Txt(Dados.Obter(loc, "texto", "")).Trim().Length == 0)
                erros.Add("Localização a confirmar: explique em \"texto\" o que falta");
        }

        if (!rascunho)
        {
            var pontos = (Dados.Obter(SeVerdadeiro(Dados.Obter(d,"capa")), "pontos_confirmar") as string)?.Trim().TrimEnd('.').ToLowerInvariant();
            var pendencias = Dados.Obter(SeVerdadeiro(Dados.Obter(d, "historico_reanalise")), "pendencias") as List<object?>;
            if (pontos is "sem pendências" or "sem pendências registradas" or "nenhuma pendência" &&
                (Dados.Lista(Dados.Obter(d, "itens", new List<object?>())).Any(Pendente) || linhas.Any(Pendente) || ambientes.Any(Pendente)
                 || Tem(Dados.Obter(d, "indices"), "sem_calculo") || Dados.Obter(SeVerdadeiro(loc), "situacao") is "A_CONFIRMAR"
                 || pendencias?.Any(p => Dados.Obter(p, "situacao") is not ("SANADA" or "ATENDIDA" or "RESOLVIDA")) == true))
                erros.Add("Capa: a declaração de ausência de pendências contradiz os itens ou quadros.");
            (string Caminho, string Campos)[] opcionais =
            [
                ("indices", "subtitulo zona observacao"), ("indices.linhas", "texto"), ("iluminacao", "tag subtitulo atencao"),
                ("localizacao", "subtitulo texto"), ("iluminacao.ambientes.vaos", "area sem_area medidas"), ("iluminacao.ambientes", "situacao_titulo"),
                ("figuras", "texto"), ("figuras.notas", "texto"), ("historico_reanalise", "arquivo sem_pendencias")
            ];
            foreach (var (caminho, campos) in opcionais)
                foreach (var (local, objeto) in Objetos(d, caminho.Split('.'), 0, ""))
                    foreach (var campo in campos.Split(' '))
                        if (objeto.TryGetValue(campo, out var valor) && PythonTexto.Verdadeiro(valor) && !TextoPreenchido(valor))
                            erros.Add("Campo de publicação sem conteúdo: " + local + "." + campo);
            foreach (var (caminho, campos) in CamposImpressos)
                foreach (var (local, objeto) in Objetos(d, caminho.Split('.', StringSplitOptions.RemoveEmptyEntries), 0, ""))
                    foreach (var campo in campos.Split(' '))
                    {
                        objeto.TryGetValue(campo, out var valor);
                        if (local.Length == 0 && campo == "conclusao" && !PythonTexto.Verdadeiro(valor)) continue;
                        if (!TextoPreenchido(valor))
                            erros.Add("Campo de publicação sem conteúdo: " + (local.Length == 0 ? campo : local + "." + campo));
                    }
            var pend = new SortedSet<string>(Comparer<string>.Create(Dados.ComparaPontosDeCodigo));
            foreach (var s in Textos(d)) foreach (var m in Colchetes(s)) pend.Add(m);
            if (pend.Count > 0)
                erros.Add("Campos ainda não preenchidos (texto entre colchetes): " + string.Join("; ", pend.Take(12)) + (pend.Count > 12 ? " …" : ""));
        }
        return erros;
    }

    /// <summary><c>x or {}</c> do Python: o próprio valor se for verdadeiro, senão um dict vazio.</summary>
    private static object? SeVerdadeiro(object? v) => PythonTexto.Verdadeiro(v) ? v : new Dictionary<string, object?>();

    /// <summary>Todos os textos dos valores (não das chaves), em profundidade.</summary>
    private static IEnumerable<string> Textos(object? x)
    {
        switch (x)
        {
            case string s: yield return s; break;
            case IReadOnlyDictionary<string, object?> d:
                foreach (var v in d.Values) foreach (var t in Textos(v)) yield return t;
                break;
            case IEnumerable e:
                foreach (var v in e) foreach (var t in Textos(v)) yield return t;
                break;
        }
    }

    /// <summary><c>re.findall(r'\[[^\]\n]{2,80}\]', s)</c> por pontos de código: um '[' seguido de 2 a 80 caracteres
    /// que não sejam ']' nem quebra de linha e depois ']'. Sem sobreposição, da esquerda para a direita.</summary>
    internal static IEnumerable<string> Colchetes(string s)
    {
        var cps = s.EnumerateRunes().Select(r => r.Value).ToArray();
        var i = 0;
        while (i < cps.Length)
        {
            if (cps[i] != '[') { i++; continue; }
            var j = i + 1;
            while (j < cps.Length && cps[j] != ']' && cps[j] != '\n') j++;
            var tam = j - (i + 1);
            if (j < cps.Length && cps[j] == ']' && tam >= 2 && tam <= 80)
            {
                yield return Dados.DeRunes(cps.Skip(i).Take(j - i + 1));
                i = j + 1;
            }
            else i++;
        }
    }

    /// <summary>resumo(itens): "ATENDE 5 • FALTA COMPROVAR 13 • ..." (contagens por situação, nos nomes das etiquetas).</summary>
    public static string Resumo(IEnumerable<object?> itens)
    {
        var lista = itens.ToList();
        int Conta(string k) => lista.Count(i => Equals(Dados.Exigir(i, "situacao"), k));
        var partes = new List<string> { Situacoes.Sit["ATENDE"].Texto + " " + Conta("ATENDE").ToString(CultureInfo.InvariantCulture) };
        if (Conta("NAO_ATENDE") != 0) partes.Add(Situacoes.Sit["NAO_ATENDE"].Texto + " " + Conta("NAO_ATENDE").ToString(CultureInfo.InvariantCulture));
        partes.Add(Situacoes.Sit["NAO_COMPROVADO"].Texto + " " + Conta("NAO_COMPROVADO").ToString(CultureInfo.InvariantCulture));
        if (Conta("DIVERGENCIA_INTERNA") != 0) partes.Add(Situacoes.Sit["DIVERGENCIA_INTERNA"].Texto + " " + Conta("DIVERGENCIA_INTERNA").ToString(CultureInfo.InvariantCulture));
        return string.Join(" • ", partes);
    }
}
