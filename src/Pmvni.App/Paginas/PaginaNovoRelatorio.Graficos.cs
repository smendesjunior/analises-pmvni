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
        return new Border { Style = Estilo("MonitorLadrilho"), Child = conteudo };
    }

    TextBlock LegendaGrafico(string texto, string estilo = "MonitorLegenda") =>
        new() { Text = texto, Style = Estilo(estilo), TextWrapping = TextWrapping.Wrap };

    Border ComporTokens(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoGrafico("Composição dos tokens", out var painel);
        var linhas = uso.Where(x => x.Executor != UsoDasIas.Programa).ToArray();
        if (linhas.Length == 0 || linhas.Any(x => x.Entrada is null || x.Saida is null))
        {
            painel.Children.Add(LegendaGrafico("Composição indisponível até o executor informar entrada e saída de todas as etapas de IA registradas."));
            return cartao;
        }

        // O Codex inclui cache lido na entrada. O coletor Claude registra entrada, cache lido e saída à parte.
        // Nas demais rotas (ou com leitura incompleta), comparar só entrada e saída informadas.
        var separarCache = linhas.All(x => x.CacheLido.HasValue &&
            (x.Executor == "Claude" || (x.Executor == "GPT" && x.CacheLido <= x.Entrada)));
        var entrada = linhas.Sum(x => (decimal)x.Entrada!.Value -
            (separarCache && x.Executor == "GPT" ? x.CacheLido!.Value : 0));
        var saida = linhas.Sum(x => (decimal)x.Saida!.Value);
        var partes = new List<(string Nome, decimal Valor, string Estilo)>
        {
            (separarCache ? "Entrada nova" : "Entrada informada", entrada, "MonitorRotuloSucesso"),
            ("Saída", saida, "MonitorRotuloDivergencia")
        };
        if (separarCache)
            partes.Add(("Cache lido", linhas.Sum(x => (decimal)x.CacheLido!.Value), "MonitorRotuloAtivo"));
        var total = partes.Sum(x => x.Valor);
        if (total <= 0)
        {
            painel.Children.Add(LegendaGrafico("O executor registrou zero tokens nestas métricas."));
            return cartao;
        }

        var linha = new PainelQuebra { Espaco = 12, EspacoLinhas = 6 };
        var anel = new Grid { Width = 84, Height = 84, VerticalAlignment = VerticalAlignment.Center };
        var legenda = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        linha.Children.Add(anel);
        linha.Children.Add(legenda);
        painel.Children.Add(linha);

        double inicio = -Math.PI / 2;
        foreach (var (nome, valor, estilo) in partes)
        {
            var fracao = (double)(valor / total);
            var rotulo = LegendaGrafico($"{nome} · {valor:N0} ({fracao:P0})", estilo);
            legenda.Children.Add(rotulo);
            if (valor == 0) continue;
            var fim = inicio + 2 * Math.PI * fracao;
            Shape arco;
            if (valor == total)
                arco = new Ellipse { Margin = new Thickness(4) };
            else
            {
                static Point Ponto(double angulo) => new(42 + 34 * Math.Cos(angulo), 42 + 34 * Math.Sin(angulo));
                var figura = new PathFigure { StartPoint = Ponto(inicio), IsClosed = false };
                figura.Segments.Add(new ArcSegment { Point = Ponto(fim), Size = new Size(34, 34),
                    IsLargeArc = fracao > 0.5, SweepDirection = SweepDirection.Clockwise });
                var geometria = new PathGeometry();
                geometria.Figures.Add(figura);
                arco = new Microsoft.UI.Xaml.Shapes.Path { Data = geometria };
            }
            arco.StrokeThickness = 8;
            arco.SetBinding(Shape.StrokeProperty, new Binding { Source = rotulo, Path = new PropertyPath("Foreground") });
            ToolTipService.SetToolTip(arco, rotulo.Text);
            anel.Children.Add(arco);
            inicio = fim;
        }
        return cartao;
    }

    Border ComporCotas(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoGrafico("Cota da conta", out var painel);
        var linha = new PainelQuebra { Espaco = 16, EspacoLinhas = 8 };
        foreach (var (nome, medida) in new (string, Func<UsoEtapa, double?>)[] {
            ("5 horas", x => x.Usada5h), ("Semanal", x => x.UsadaSemana) })
        {
            var coluna = new StackPanel { Spacing = 4, MaxWidth = 125 };
            coluna.Children.Add(LegendaGrafico(nome));
            var etapa = uso.LastOrDefault(x => medida(x) is >= 0 and <= 100);
            if (etapa is null || medida(etapa) is not double percentual)
                coluna.Children.Add(LegendaGrafico("Sem medição"));
            else
            {
                var grade = new Grid { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left };
                var anel = new ProgressRing { IsIndeterminate = false, IsActive = true, Minimum = 0,
                    Maximum = 100, Value = percentual, Width = 72, Height = 72 };
                Trabalhando.SetAutomatico(anel, false);
                AutomationProperties.SetName(anel, $"Cota usada de {nome}, {percentual:0.#}%");
                grade.Children.Add(anel);
                grade.Children.Add(new TextBlock { Text = $"{percentual:0.#}%", HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center });
                coluna.Children.Add(grade);
                coluna.Children.Add(LegendaGrafico(FluxoNovoRelatorio.NomeDaEtapa(etapa.Etapa)));
            }
            linha.Children.Add(coluna);
        }
        painel.Children.Add(linha);
        return cartao;
    }

    Border ComporTempos(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoGrafico("Tempo registrado por etapa", out var painel);
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
