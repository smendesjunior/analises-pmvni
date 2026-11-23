using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;

namespace Pmvni.App;

public sealed partial class PaginaNovoRelatorio
{
    void AtualizarNarrativa()
    {
        DetalhesPorEtapa.Children.Clear();
        ContagemDetalhes.Text = etapasMostradas.Length == 1 ? "1 etapa mapeada" : $"{etapasMostradas.Length} etapas mapeadas";
        for (var i = 0; i < etapasMostradas.Length; i++)
        {
            var (nome, estado) = etapasMostradas[i];
            var id = IdDaEtapa(nome);
            var narrativa = new List<string>();
            var medidas = new List<string>();
            if (id is not null)
            {
                var uso = (consumoExibido ?? []).Where(x => x.Etapa == id).ToArray();
                if (detalhesEtapas.Registros.TryGetValue(id, out var registro)) narrativa.Add(registro);
                foreach (var plano in uso.Select(x => x.Plano).OfType<string>().Distinct())
                    narrativa.Add("Plano registrado pela IA" + Environment.NewLine + plano);
                if (id == "analyze")
                    narrativa.AddRange(detalhesEtapas.Frentes.Select(f => string.Join(" · ", new[] { FluxoNovoRelatorio.TextoFrente(f), f.Atividade }.OfType<string>())));
                narrativa.AddRange(detalhesEtapas.Objecoes.Where(o => o.Etapa == id).Select(o => FluxoNovoRelatorio.TextoObjecao(o)));
                if (detalhesEtapas.TextoExecutor(id) is { } executor) medidas.Add(executor);
                var duracoes = uso.Select(x => x.Duracao).OfType<TimeSpan>().ToArray();
                if (duracoes.Length > 0)
                    medidas.Add("Tempo registrado · " + FluxoNovoRelatorio.TempoLegivel(TimeSpan.FromSeconds(duracoes.Sum(d => d.TotalSeconds)), incluirSegundos: true));
                if (ProgressoAtual is { } progresso && FluxoNovoRelatorio.NomeDaEtapa(progresso.Etapa) == nome)
                    medidas.Add($"{progresso.Avaliados} de {progresso.Total} {(progresso.Secoes ? "seções com resultado" : "itens avaliados")}");
            }
            if (narrativa.Count == 0) narrativa.Add("Aguardando registro desta etapa.");
            var corpo = new StackPanel { Spacing = 8 };
            var topo = new Grid { ColumnSpacing = 8 };
            topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topo.ColumnDefinitions.Add(new ColumnDefinition());
            topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            topo.Children.Add(new Border { Style = Estilo("PmvniSeloPasso"), Child = new TextBlock { Text = $"{i + 1}", Style = Estilo("PmvniNumeroPasso") } });
            var titulo = new TextBlock { Text = nome, Style = Estilo("PmvniAjusteTitulo"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(titulo, 1);
            topo.Children.Add(titulo);
            var selo = new Border
            {
                Style = Estilo(estado switch {
                    EstadoEtapa.Feita => "PmvniSeloSucesso", EstadoEtapa.Erro => "PmvniSeloErro",
                    EstadoEtapa.Agora => "PmvniSeloAtencao", _ => "PmvniSeloNeutro" }),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = SituacaoDaEtapa(estado, execucaoAtivaNaBarra), Style = Estilo("CaptionTextBlockStyle") }
            };
            Grid.SetColumn(selo, 2);
            topo.Children.Add(selo);
            corpo.Children.Add(topo);
            corpo.Children.Add(new TextBlock { Text = string.Join(Environment.NewLine, narrativa), TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            if (medidas.Count > 0)
                corpo.Children.Add(new TextBlock { Text = string.Join("    •    ", medidas), Style = Estilo("MonitorLegenda"), TextWrapping = TextWrapping.Wrap });
            DetalhesPorEtapa.Children.Add(new Border { Style = Estilo("MonitorCartao"), Child = corpo });
        }
        if (detalhesEtapas.Plano.Count > 0)
            DetalhesPorEtapa.Children.Add(CartaoDeTexto("Plano registrado no preparo", string.Join(" → ", detalhesEtapas.Plano.Select(id => FluxoNovoRelatorio.NomeDaEtapa(id)))));
        if (detalhesEtapas.Validacoes.Count > 0)
            DetalhesPorEtapa.Children.Add(CartaoDeTexto("Avisos e consertos registrados", string.Join(Environment.NewLine, detalhesEtapas.Validacoes)));
    }

    Border CartaoDeTexto(string titulo, string corpo)
    {
        var painel = new StackPanel { Spacing = 4 };
        painel.Children.Add(new TextBlock { Text = titulo, Style = Estilo("PmvniAjusteTitulo"), TextWrapping = TextWrapping.Wrap });
        painel.Children.Add(new TextBlock { Text = corpo, TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap });
        return new Border { Style = Estilo("MonitorLadrilho"), Child = painel };
    }

    static string NomeExecutor(UsoEtapa e) => e.Executor == UsoDasIas.Programa ? "Programa local" : e.Modelo ?? e.Executor;
    static string Pontos(double? valor) => valor is { } p ? $"{p:0.#} p.p." : "—";
    static string Ghz(double? mhz) => mhz is { } valor ? $"{valor / 1000:0.##} GHz" : "—";

    Grid LinhaTabelaConsumo(string[] dados, bool cabecalho)
    {
        var grade = new Grid { Padding = new Thickness(4, 10, 4, 10), ColumnSpacing = 8 };
        foreach (var peso in new[] { 1.8, 1.6, 0.85, 0.85, 0.85, 0.85, 0.9, 1 })
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(peso, GridUnitType.Star) });
        for (var i = 0; i < dados.Length; i++)
        {
            var celula = LegendaGrafico(dados[i], cabecalho ? "MonitorRotulo" : "BodyTextBlockStyle");
            celula.FontSize = cabecalho ? 11 : 13;
            celula.TextAlignment = i >= 2 ? TextAlignment.Right : TextAlignment.Left;
            celula.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(celula, i);
            grade.Children.Add(celula);
        }
        return grade;
    }

    void DistribuirMetricas(object sender, SizeChangedEventArgs e) => DisporMetricas(e.NewSize.Width);

    void DisporMetricas(double largura)
    {
        var colunas = largura >= 760 ? 4 : largura >= 375 ? 2 : 1;
        if (TotaisConsumo.ColumnDefinitions.Count != colunas)
        {
            TotaisConsumo.ColumnDefinitions.Clear();
            TotaisConsumo.RowDefinitions.Clear();
            for (var i = 0; i < colunas; i++) TotaisConsumo.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 4 / colunas; i++) TotaisConsumo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var i = 0; i < TotaisConsumo.Children.Count; i++)
        {
            Grid.SetColumn((FrameworkElement)TotaisConsumo.Children[i], i % colunas);
            Grid.SetRow((FrameworkElement)TotaisConsumo.Children[i], i / colunas);
        }
    }

    Border CartaoConsumoTotal(string titulo, string valor, string ajuda, string icone, string estilo, bool relogio = false)
    {
        var corpo = new Grid { ColumnSpacing = 10 };
        corpo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        corpo.ColumnDefinitions.Add(new ColumnDefinition());
        var simbolo = LegendaGrafico(icone, estilo);
        simbolo.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons");
        simbolo.FontSize = 20;
        simbolo.HorizontalAlignment = HorizontalAlignment.Center;
        simbolo.VerticalAlignment = VerticalAlignment.Center;
        corpo.Children.Add(new Border { Width = 34, Height = 34, Style = Estilo("MonitorLadrilho"), Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, Child = simbolo });
        var pilha = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(pilha, 1);
        corpo.Children.Add(pilha);
        pilha.Children.Add(LegendaGrafico(titulo));
        var numero = LegendaGrafico(valor, "MonitorTitulo");
        var legenda = LegendaGrafico(ajuda);
        if (relogio)
        {
            numero.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Source = TextoTempoDecorrido, Path = new PropertyPath("Text") });
            legenda.SetBinding(TextBlock.TextProperty, new Microsoft.UI.Xaml.Data.Binding { Source = TextoTempoEstimado, Path = new PropertyPath("Text") });
            ToolTipService.SetToolTip(legenda, "Previsão de término");
        }
        pilha.Children.Add(numero);
        pilha.Children.Add(legenda);
        return new Border { Style = Estilo("MonitorCartao"), MinHeight = 92, Padding = new Thickness(12), Child = corpo };
    }

    void PreencherResumoConsumo(IReadOnlyList<UsoEtapa> uso)
    {
        var ia = uso.Where(e => e.Executor != UsoDasIas.Programa).ToArray();
        var entrada = SomaMedida(ia.Select(e => e.Entrada));
        var saida = SomaMedida(ia.Select(e => e.Saida));
        var cache = SomaMedida(ia.Select(e => e.CacheLido));
        var composicao = EntradasComCache(ia);
        var baseComparavel = composicao is { } c ? c.Nova + c.Cache : (decimal?)null;
        TotaisConsumo.Children.Clear();
        TotaisConsumo.Children.Add(CartaoConsumoTotal("Tokens de entrada", TextoConsumo.Numero(entrada),
            ia.LastOrDefault(e => e.Entrada.HasValue) is { } ultima ? NomeExecutor(ultima) : "Aguardando medição", Glifos.Entrada, "MonitorRotuloSucesso"));
        TotaisConsumo.Children.Add(CartaoConsumoTotal("Tokens de saída", TextoConsumo.Numero(saida),
            baseComparavel is > 0 && saida.HasValue && ia.All(x => x.Saida.HasValue) ? $"{100 * saida / baseComparavel:0.#}% do volume de entrada" : "Saída informada pela IA", Glifos.Saida, "MonitorRotuloDivergencia"));
        TotaisConsumo.Children.Add(CartaoConsumoTotal("Cache lido", TextoConsumo.Numero(cache),
            baseComparavel is > 0 ? $"{100 * composicao!.Value.Cache / baseComparavel:0.#}% da entrada reaproveitada" : "Cache informado pela IA", Glifos.Cache, "MonitorRotuloCautela"));
        TotaisConsumo.Children.Add(CartaoConsumoTotal("Tempo decorrido", "", "", Glifos.Relogio, "MonitorRotuloAtivo", true));
        DisporMetricas(TotaisConsumo.ActualWidth);

        ConsumoExecutores.Children.Clear();
        foreach (var grupo in uso.GroupBy(NomeExecutor))
        {
            var etapas = string.Join(", ", grupo.Select(e => FluxoNovoRelatorio.NomeDaEtapa(e.Etapa)).Distinct());
            var linha = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            foreach (var largura in new[] { 1d, 1.2, 1.2 })
                linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largura, GridUnitType.Star) });
            var executor = new StackPanel { Spacing = 3 };
            executor.Children.Add(LegendaGrafico(grupo.Key, "PmvniAjusteTitulo"));
            executor.Children.Add(LegendaGrafico(etapas));
            linha.Children.Add(executor);
            void Medida(int posicao, string nome, string valor, double? percentual = null)
            {
                var painel = new StackPanel { Spacing = 4 };
                painel.Children.Add(LegendaGrafico(nome));
                painel.Children.Add(LegendaGrafico(valor, "BodyTextBlockStyle"));
                if (percentual is >= 0 and <= 100) painel.Children.Add(new ProgressBar { Value = percentual.Value });
                Grid.SetColumn(painel, posicao);
                linha.Children.Add(painel);
            }
            if (grupo.First().Executor == UsoDasIas.Programa)
            {
                var etapasCpu = grupo.Where(x => x.CpuMediaMhz.HasValue || x.CpuPicoMhz.HasValue);
                var cpu = string.Join(Environment.NewLine, etapasCpu.Select(e => $"{FluxoNovoRelatorio.NomeDaEtapa(e.Etapa)} · média {Ghz(e.CpuMediaMhz)} · pico {Ghz(e.CpuPicoMhz)}"));
                Medida(1, "CPU da máquina", cpu.Length > 0 ? cpu : "Sem medição");
                var ram = grupo.Max(e => e.RamPicoBytes);
                Medida(2, "RAM do executor e subprocessos", ram is { } b ? $"Pico {b / 1048576d:0.#} MiB" : "Sem medição");
            }
            else
            {
                double? Medido(IEnumerable<double?> valores) => valores.Any(v => v.HasValue) ? valores.Sum() : null;
                var curta = Medido(grupo.Select(e => e.Cota5h));
                var semanal = Medido(grupo.Select(e => e.CotaSemana));
                Medida(1, "Cota de 5 h", Pontos(curta), curta);
                Medida(2, "Cota semanal", Pontos(semanal), semanal);
            }
            ConsumoExecutores.Children.Add(linha);
        }
        if (uso.Count == 0) ConsumoExecutores.Children.Add(LegendaGrafico("Aguardando registro de execução."));
    }

    partial void GraficosDeConsumo(StackPanel painel, IReadOnlyList<UsoEtapa> uso);

    static long? SomaMedida(IEnumerable<long?> valores)
    {
        var medidos = valores.OfType<long>().ToArray();
        return medidos.Length == 0 ? null : medidos.Sum();
    }
}
