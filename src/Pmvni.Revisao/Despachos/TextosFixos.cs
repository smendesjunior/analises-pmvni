using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Um parágrafo fixo do kit. As lacunas que o usuário preenche ficam como <c>{intervencao}</c> e <c>{area}</c>.</summary>
public sealed record ParagrafoFixo(string Id, string Texto);

/// <summary>O texto de referência do kit não tem a forma que o editor sabe ler: o tipo fica indisponível (o app não escreve texto institucional).</summary>
public sealed class TextosForaDoPadraoException(string arquivo, string detalhe)
    : Exception("O texto de referência do kit (" + arquivo + ") não está no formato esperado: " + detalhe + ".");

/// <summary>Os parágrafos fixos do despacho (corpo da aprovação, fechamento da correção), lidos no lugar das referências do plugin
/// <c>despacho</c> do kit a cada uso (desenho §10.3). Nada deles é guardado no código além das âncoras curtas.</summary>
public sealed record TextosFixos(TipoDespacho Tipo, string Arquivo, string Sha256, IReadOnlyList<ParagrafoFixo> Paragrafos)
{
    public const string PastaReferencias = "01 - PLUGINS/plugins/despacho/skills/despacho/references";
    const string Secao = "## Conteúdo na ordem do documento";
    internal const string LacunaIntervencao = "{intervencao}", LacunaArea = "{area}";
    const string TrechoIntervencao = "obra nova/alteração de obra/ampliação de obra";

    static readonly Regex MarcaEstilo = new(@"^<!--\s*estilo:.*-->$", RegexOptions.CultureInvariant);
    static readonly Regex NumeroArea = new(@"\d{1,3}(?:\.\d{3})*,\d{2}(?=\s*m²)", RegexOptions.CultureInvariant);

    public static string NomeArquivo(TipoDespacho t) => t switch
    {
        TipoDespacho.Aprovacao => "modelo-despacho-aprovacao-gabinete.md",
        TipoDespacho.Correcao => "modelo-despacho-analise-correcao.md",
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, "Tipo de despacho sem texto de referência."),
    };

    public static TextosFixos Ler(string raizKit, TipoDespacho t)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        var arquivo = NomeArquivo(t);
        var caminho = Path.Combine(raizKit, PastaReferencias.Replace('/', Path.DirectorySeparatorChar), arquivo);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new TextosForaDoPadraoException(arquivo, "o arquivo não pôde ser lido (" + e.Message + ")");
        }
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new TextosForaDoPadraoException(arquivo, "o arquivo não é UTF-8"); }
        if (texto.StartsWith('﻿')) texto = texto[1..];

        var paragrafos = LerParagrafos(arquivo, texto);
        var fixos = t == TipoDespacho.Aprovacao ? Aprovacao(arquivo, paragrafos) : Correcao(arquivo, paragrafos);
        return new TextosFixos(t, arquivo, Convert.ToHexStringLower(SHA256.HashData(bytes)), fixos);
    }

    /// <summary>Os textos entre as linhas <c>&lt;!-- estilo: … --&gt;</c> da seção "Conteúdo na ordem do documento", aparados, sem os vazios.</summary>
    static List<string> LerParagrafos(string arquivo, string texto)
    {
        var linhas = texto.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var inicio = linhas.FindIndex(l => l.Trim() == Secao);
        if (inicio < 0) throw new TextosForaDoPadraoException(arquivo, "não tem a seção \"" + Secao + "\"");
        var lista = new List<string>();
        List<string>? atual = null;
        void Fechar()
        {
            if (atual is null) return;
            var p = string.Join(" ", atual.Select(l => l.Trim()).Where(l => l.Length > 0)).Trim();
            if (p.Length > 0) lista.Add(p);
        }
        for (var i = inicio + 1; i < linhas.Count && !linhas[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            if (MarcaEstilo.IsMatch(linhas[i].Trim()))
            {
                Fechar();
                atual = new List<string>();
            }
            else atual?.Add(linhas[i]);
        }
        Fechar();
        return lista;
    }

    static List<ParagrafoFixo> Aprovacao(string arquivo, List<string> ps)
    {
        var considerando = ps.Where(p => p.StartsWith("Considerando", StringComparison.Ordinal)).ToList();
        if (considerando.Count != 3)
            throw new TextosForaDoPadraoException(arquivo, "esperados 3 parágrafos que começam por \"Considerando\", há " + considerando.Count);
        var encaminho = ps.Where(p => p.StartsWith("Encaminho", StringComparison.Ordinal)).ToList();
        if (encaminho.Count != 1)
            throw new TextosForaDoPadraoException(arquivo, "esperado 1 parágrafo que começa por \"Encaminho\", há " + encaminho.Count);
        var pedidos = considerando.Where(p => p.Contains("solicitou", StringComparison.Ordinal)).ToList();
        var areas = considerando.Where(p => p.Contains("área total", StringComparison.Ordinal)).ToList();
        if (pedidos.Count != 1 || areas.Count != 1 || pedidos[0] == areas[0])
            throw new TextosForaDoPadraoException(arquivo, "os parágrafos do pedido (\"solicitou\") e da área (\"área total\") não foram achados");
        var analise = considerando.Single(p => p != pedidos[0] && p != areas[0]);

        var pedido = pedidos[0];
        var i = pedido.IndexOf(TrechoIntervencao, StringComparison.Ordinal);
        if (i < 0 || pedido.IndexOf(TrechoIntervencao, i + 1, StringComparison.Ordinal) >= 0)
            throw new TextosForaDoPadraoException(arquivo, "o pedido não tem uma vez o trecho \"" + TrechoIntervencao + "\"");
        pedido = pedido[..i] + LacunaIntervencao + pedido[(i + TrechoIntervencao.Length)..];

        var numeros = NumeroArea.Matches(areas[0]);
        if (numeros.Count != 1) throw new TextosForaDoPadraoException(arquivo, "o parágrafo da área não tem um número antes de \"m²\"");
        var area = areas[0][..numeros[0].Index] + LacunaArea + areas[0][(numeros[0].Index + numeros[0].Length)..];

        return new List<ParagrafoFixo>
        {
            new("considerando_pedido", pedido), new("considerando_analise", analise), new("considerando_area", area), new("encaminho", encaminho[0]),
        };
    }

    static List<ParagrafoFixo> Correcao(string arquivo, List<string> ps)
    {
        var fechamento = ps.Where(p => p.StartsWith("Para prosseguimento", StringComparison.Ordinal)).ToList();
        if (fechamento.Count != 1)
            throw new TextosForaDoPadraoException(arquivo, "esperado 1 parágrafo que começa por \"Para prosseguimento\", há " + fechamento.Count);
        return new List<ParagrafoFixo> { new("fechamento", fechamento[0]) };
    }
}
