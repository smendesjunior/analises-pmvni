using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Pmvni.Agentes;
using Windows.Foundation;

namespace Pmvni.App;

public sealed partial class PaginaNovoRelatorio
{
    partial void GraficosDeConsumo(StackPanel painel, IReadOnlyList<UsoEtapa> uso)
    {
        painel.Children.Clear();
        var grade = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        var cartoes = new[] { ComporTokens(uso), ComporCotas(uso), ComporTempos(uso) };
        for (var i = 0; i < cartoes.Length; i++)
        {
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(cartoes[i], i);
            Grid.SetColumnSpan(cartoes[i], 3);
            grade.Children.Add(cartoes[i]);
        }
        grade.SizeChanged += (_, e) =>
        {
            var horizontal = e.NewSize.Width >= 930;
            for (var i = 0; i < cartoes.Length; i++)
            {
                Grid.SetRow(cartoes[i], horizontal ? 0 : i);
                Grid.SetColumn(cartoes[i], horizontal ? i : 0);
                Grid.SetColumnSpan(cartoes[i], horizontal ? 1 : 3);
            }
            grade.RowSpacing = horizontal ? 0 : 10;
        };
        painel.Children.Add(grade);
    }

    Border CartaoGrafico(string titulo, out StackPanel conteudo)
    {
        conteudo = new StackPanel { Spacing = 8 };
        conteudo.Children.Add(new TextBlock { Text = titulo, Style = Estilo("PmvniAjusteTitulo"), TextWrapping = TextWrapping.Wrap });
        return new Border { Style = Estilo("MonitorCartao"), Padding = new Thickness(24), MinHeight = 300, Child = conteudo };
    }

    TextBlock LegendaGrafico(string texto, string estilo = "MonitorLegenda") =>
        new() { Text = texto, Style = Estilo(estilo), TextWrapping = TextWrapping.Wrap };

    static (decimal Nova, decimal Cache)? EntradasComCache(IReadOnlyList<UsoEtapa> linhas)
    {
        if (linhas.Count == 0 || linhas.Any(e => e.Entrada is not >= 0 || e.CacheLido is not >= 0 ||
            (e.Executor != "Claude" && (e.Executor != "GPT" || e.CacheLido > e.Entrada)))) return null;
        return (linhas.Sum(e => (decimal)e.Entrada!.Value - (e.Executor == "GPT" ? e.CacheLido!.Value : 0)),
                linhas.Sum(e => (decimal)e.CacheLido!.Value));
    }

    Grid AnelMedido(double percentual, string rotulo, string estilo)
    {
        var grade = new Grid { Width = 82, Height = 82, VerticalAlignment = VerticalAlignment.Center };
        var amostra = LegendaGrafico($"{percentual:0.#}%", estilo);
        amostra.HorizontalAlignment = HorizontalAlignment.Center;
        amostra.VerticalAlignment = VerticalAlignment.Center;
        var anel = new ProgressRing { IsIndeterminate = false, IsActive = true, Value = percentual,
            Minimum = 0, Maximum = 100, Width = 82, Height = 82, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"] };
        anel.SetBinding(Control.ForegroundProperty, new Binding { Source = amostra, Path = new PropertyPath("Foreground") });
        Trabalhando.SetAutomatico(anel, false);
        AutomationProperties.SetName(anel, $"{rotulo} · {amostra.Text}");
        grade.Children.Add(anel);
        grade.Children.Add(amostra);
        return grade;
    }

    Border ComporTokens(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoGrafico("Composição dos tokens de entrada", out var painel);
        painel.Children.Add(LegendaGrafico("Quanto veio do cache e quanto foi processado de novo"));
        var linhas = uso.Where(x => x.Executor != UsoDasIas.Programa).ToArray();
        if (EntradasComCache(linhas) is not { } partes || partes.Nova + partes.Cache <= 0)
        {
            painel.Children.Add(LegendaGrafico("Aguardando medidas comparáveis de entrada e cache."));
            return cartao;
        }
        var linha = new PainelQuebra { Espaco = 12, EspacoLinhas = 6 };
        var percentual = (double)(100 * partes.Cache / (partes.Nova + partes.Cache));
        linha.Children.Add(AnelMedido(percentual, "Entrada em cache", "MonitorRotuloSucesso"));
        var legenda = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        legenda.Children.Add(LegendaGrafico($"Cache lido   {TextoConsumo.Numero((long)partes.Cache)}", "MonitorRotuloSucesso"));
        legenda.Children.Add(LegendaGrafico($"Entrada nova   {TextoConsumo.Numero((long)partes.Nova)}", "MonitorRotuloCautela"));
        legenda.Children.Add(LegendaGrafico($"Saída gerada   {TextoConsumo.Numero(SomaMedida(linhas.Select(e => e.Saida)))}"));
        linha.Children.Add(legenda);
        painel.Children.Add(linha);
        return cartao;
    }

    Border ComporCotas(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoGrafico("Uso das cotas", out var painel);
        painel.Children.Add(LegendaGrafico("Percentual usado na última leitura da conta"));
        var linha = new PainelQuebra { Espaco = 12, EspacoLinhas = 8 };
        foreach (var (nome, medida, variacao, estilo) in new (string, Func<UsoEtapa, double?>, Func<UsoEtapa, double?>, string)[] {
            ("Cota de 5 h", x => x.Usada5h, x => x.Cota5h, "MonitorRotuloCautela"),
            ("Cota semanal", x => x.UsadaSemana, x => x.CotaSemana, "MonitorRotuloDivergencia") })
        {
            var coluna = new StackPanel { Spacing = 4, MinWidth = 82, MaxWidth = 130 };
            var etapa = uso.LastOrDefault(x => medida(x) is >= 0 and <= 100);
            if (etapa is not null && medida(etapa) is double percentual)
            {
                coluna.Children.Add(AnelMedido(percentual, nome, estilo));
                coluna.Children.Add(LegendaGrafico(nome));
                if (variacao(etapa) is { } delta)
                    coluna.Children.Add(LegendaGrafico($"{Pontos(delta)} · {FluxoNovoRelatorio.NomeDaEtapa(etapa.Etapa)}"));
            }
            else
            {
                coluna.Children.Add(LegendaGrafico(nome));
                coluna.Children.Add(LegendaGrafico("Sem medição"));
            }
            linha.Children.Add(coluna);
        }
        painel.Children.Add(linha);
        return cartao;
    }

    Border ComporTempos(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoGrafico("Tempo por etapa", out var painel);
        var medidas = uso.Where(x => x.Duracao.HasValue).GroupBy(x => x.Etapa)
            .Select(g => (Nome: FluxoNovoRelatorio.NomeDaEtapa(g.Key), Segundos: g.Sum(x => x.Duracao!.Value.TotalSeconds))).ToArray();
        var maximo = medidas.Select(x => x.Segundos).DefaultIfEmpty().Max();
        foreach (var (nome, segundos) in medidas)
        {
            var tempo = FluxoNovoRelatorio.TempoLegivel(TimeSpan.FromSeconds(segundos), incluirSegundos: true);
            var rotulo = $"{nome} · {tempo}";
            painel.Children.Add(LegendaGrafico(rotulo));
            var barra = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, maximo), Value = segundos };
            AutomationProperties.SetName(barra, rotulo);
            painel.Children.Add(barra);
        }
        if (medidas.Length == 0) painel.Children.Add(LegendaGrafico("Sem duração registrada"));
        return cartao;
    }
}
