using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using Pmvni.Composicao.Kit;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Linha da lista de seções e itens (esquerda). <see cref="Ref"/> é o data-ref do bloco (null na capa; nos itens, o id do
/// item no projeto); <see cref="Folha"/>, a primeira folha (1 = primeira) em que ele aparece. O indicador de 3 x 16 marca o item da
/// folha que está na tela. Itens do relatório levam a situação efetiva (a etiqueta do relatório, <see cref="EtiquetaSituacao"/>) e o
/// marcador "alterado".</summary>
public sealed class ItemNavegacao(string rotulo, int folha, string? @ref, bool secao) : INotifyPropertyChanged
{
    static readonly Regex NumeroETitulo = new(@"^(\d{1,3})\s+(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    bool atual, selecionado, recolhida;
    int contagem;

    public string Rotulo { get; } = rotulo;
    public int Folha { get; } = folha;
    public string? Ref { get; } = @ref;
    public bool Secao { get; } = secao;
    public string FolhaTexto => "f. " + Folha;
    public FontWeight Peso => Secao ? FontWeights.SemiBold : FontWeights.Normal;
    public double TamanhoTitulo => Secao ? 14 : 13;

    /// <summary>Painel de navegação (correções 2026-11-15): o número do item ("01") fica numa coluna própria, o título ao lado e a etiqueta
    /// de situação embaixo dele; seções e figuras não têm número.</summary>
    public string Numero => !Secao && NumeroETitulo.Match(Rotulo) is { Success: true } m ? m.Groups[1].Value : "";
    public string Titulo => !Secao && NumeroETitulo.Match(Rotulo) is { Success: true } m ? m.Groups[2].Value.Trim() : Rotulo;
    public Visibility NumeroVisivel => Secao ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A barra fina à esquerda: o item selecionado ou, sem seleção na lista, o da folha que está na tela.</summary>
    public Visibility IndicadorVisivel => atual || selecionado ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fundo um pouco mais claro no item selecionado (como no Explorador de Arquivos).</summary>
    public Brush FundoLinha => selecionado && Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var b) && b is Brush pincel
        ? pincel : new SolidColorBrush(Colors.Transparent);

    /// <summary>Nas seções: quantos itens passam no filtro atual (o contador ao lado do título).</summary>
    public int Contagem
    {
        get => contagem;
        set { if (contagem == value) return; contagem = value; Avisar(nameof(Contagem), nameof(ContagemTexto), nameof(ContagemVisivel), nameof(ChevronVisivel)); }
    }

    public string ContagemTexto => Contagem.ToString(CultureInfo.InvariantCulture);
    public Visibility ContagemVisivel => Secao && Contagem > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChevronVisivel => Secao && Contagem > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A seção está recolhida (os itens dela somem da lista; a seção fica, com o contador).</summary>
    public bool Recolhida
    {
        get => recolhida;
        set { if (recolhida == value) return; recolhida = value; Avisar(nameof(Recolhida), nameof(GlifoChevron), nameof(NomeChevron)); }
    }

    public string GlifoChevron => recolhida ? "" : ""; // ChevronRight / ChevronDown
    public string NomeChevron => (recolhida ? "Expandir " : "Recolher ") + Rotulo;

    public bool Selecionado
    {
        get => selecionado;
        set { if (selecionado == value) return; selecionado = value; Avisar(nameof(IndicadorVisivel), nameof(FundoLinha)); }
    }

    void Avisar(params string[] nomes)
    {
        foreach (var n in nomes) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    /// <summary>Código da situação efetiva (ATENDE…) de um item do relatório; null nas seções, capa e figuras.</summary>
    public string? Situacao { get; set; }

    /// <summary>O item tem revisão (situação ou texto).</summary>
    public bool Alterado { get; set; }

    public string TextoSituacao => EtiquetasSituacao.Texto(Situacao);
    public Visibility SituacaoVisivel => Situacao is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AlteradoVisivel => Alterado ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>O que o Narrador lê na linha (auditoria A3/M10): "Item 07, Recuos, Não atende, alterado, folha 4".</summary>
    public string NomeAcessivel => NomesAcessiveis.ItemNavegacao(Rotulo, Secao, TextoSituacao, Alterado, Folha);

    public override string ToString() => NomeAcessivel;

    public bool Atual
    {
        get => atual;
        set
        {
            if (atual == value) return;
            atual = value;
            Avisar(nameof(IndicadorVisivel));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>O que a tela lê do dados.json efetivo além do HTML: título da barra (<see cref="TituloJanela"/>: "Proc. 9001/2026 · Requerente"),
/// processo e requerente da capa e os títulos dos itens e figuras.</summary>
internal sealed record DadosRelatorio(string Titulo, string? Processo, string? Requerente, IReadOnlyList<string> TitulosItens, IReadOnlyList<string> TitulosFiguras)
{
    static readonly Regex Marcas = new("<[^>]*>", RegexOptions.CultureInvariant);

    /// <summary>Lê os bytes do dados.json (o efetivo, gerado do pacote); <paramref name="nomeArquivo"/> é o título quando a capa não tem
    /// processo nem requerente.</summary>
    public static DadosRelatorio DeBytes(byte[] json, string nomeArquivo)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        string? Texto(JsonElement e, string nome) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        List<string> Titulos(string lista) =>
            raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(lista, out var l) && l.ValueKind == JsonValueKind.Array
                ? l.EnumerateArray().Select(x => Texto(x, "titulo") ?? "").ToList()
                : [];
        var capa = raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty("capa", out var c) ? c : default;
        var processo = Texto(capa, "processo");
        var requerente = Texto(capa, "requerente");
        return new DadosRelatorio(TituloJanela.De(processo, requerente, nomeArquivo), processo, requerente, Titulos("itens"), Titulos("figuras"));
    }

    /// <summary>Lista de navegação na ordem do documento: capa, seções (h2 do relatório), itens e figuras, cada um na primeira
    /// folha em que aparece (<see cref="ResultadoComposicao.RefsPorFolha"/>).</summary>
    public IReadOnlyList<ItemNavegacao> Navegacao(ResultadoRender render, ResultadoComposicao composicao)
    {
        var porId = new Dictionary<string, RefElemento>();
        foreach (var r in render.Refs) porId.TryAdd(r.Id, r);
        var vistos = new HashSet<string>();
        var lista = new List<ItemNavegacao> { new("Capa", 1, null, true) };
        for (var f = 0; f < composicao.RefsPorFolha.Count; f++)
        {
            foreach (var id in composicao.RefsPorFolha[f])
            {
                if (!vistos.Add(id) || !porId.TryGetValue(id, out var r)) continue;
                lista.Add(r.Seletor switch
                {
                    ".sec" => new ItemNavegacao(Limpar(r.Rotulo) ?? id, f + 1, id, true),
                    ".card" => new ItemNavegacao($"{r.Indice + 1:00}  {TituloDe(TitulosItens, r.Indice, "Item " + (r.Indice + 1))}", f + 1, id, false),
                    _ => new ItemNavegacao($"Figura {r.Indice + 1}  {TituloDe(TitulosFiguras, r.Indice, "")}".TrimEnd(), f + 1, id, false),
                });
            }
        }
        return lista;
    }

    static string TituloDe(IReadOnlyList<string> titulos, int i, string padrao) =>
        i < titulos.Count && !string.IsNullOrWhiteSpace(titulos[i]) ? titulos[i] : padrao;

    static string? Limpar(string? html) => html is null ? null : WebUtility.HtmlDecode(Marcas.Replace(html, "")).Trim();
}
