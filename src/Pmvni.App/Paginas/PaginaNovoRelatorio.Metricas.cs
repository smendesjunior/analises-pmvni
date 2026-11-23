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
            DetalhesPorEtapa.Children.Add(new Border { Style = Estilo("PmvniCartaoCompactoStyle"), Child = corpo });
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

    void PreencherResumoConsumo(IReadOnlyList<UsoEtapa> uso)
    {
        var ia = uso.Where(e => e.Executor != UsoDasIas.Programa).ToArray();
        var entrada = SomaMedida(ia.Select(e => e.Entrada));
        var saida = SomaMedida(ia.Select(e => e.Saida));
        var cache = SomaMedida(ia.Select(e => e.CacheLido));
        TotaisConsumo.Children.Clear();
        foreach (var (nome, total) in new[] { ("Tokens de entrada", entrada), ("Tokens de saída", saida), ("Cache lido", cache) })
            TotaisConsumo.Children.Add(CartaoDeTexto(nome, TextoConsumo.Numero(total)));
        // Codex inclui cache nos tokens de entrada. Outras rotas podem informar o cache à parte.
        var comparavel = ia.Length > 0 && ia.All(e => e.Executor == "GPT" && e.Entrada.HasValue && e.CacheLido.HasValue);
        ComposicaoTokens.Text = comparavel && entrada is > 0 && cache <= entrada
            ? $"Entrada nova · {TextoConsumo.Numero(entrada - cache)}   ·   {100d * cache / entrada:0.#}% da entrada veio do cache"
            : "";
        ComposicaoTokens.Visibility = Visivel(ComposicaoTokens.Text.Length != 0);

        ConsumoExecutores.Children.Clear();
        foreach (var grupo in uso.GroupBy(e => e.Modelo ?? e.Executor))
        {
            var metricas = new List<string> { string.Join(", ", grupo.Select(e => FluxoNovoRelatorio.NomeDaEtapa(e.Etapa)).Distinct()) };
            var duracoes = grupo.Where(e => e.Duracao.HasValue).ToArray();
            if (duracoes.Length > 0)
                metricas.Add("Tempo registrado · " + FluxoNovoRelatorio.TempoLegivel(TimeSpan.FromSeconds(duracoes.Sum(e => e.Duracao!.Value.TotalSeconds)), incluirSegundos: true));
            foreach (var (nome, valores) in new[] { ("Entrada", grupo.Select(e => e.Entrada)), ("Saída", grupo.Select(e => e.Saida)),
                    ("Cache lido", grupo.Select(e => e.CacheLido)) })
                if (SomaMedida(valores) is { } total) metricas.Add(nome + " · " + TextoConsumo.Numero(total));
            if (grupo.Key == UsoDasIas.Programa)
                foreach (var etapa in grupo)
                {
                    var cpu = etapa.CpuMediaMhz.HasValue || etapa.CpuPicoMhz.HasValue
                        ? $"média {Ghz(etapa.CpuMediaMhz)} · pico {Ghz(etapa.CpuPicoMhz)}" : "sem medição";
                    var ram = etapa.RamPicoBytes is { } bytes ? $"{bytes / 1048576d:0.#} MiB" : "sem medição";
                    metricas.Add($"{FluxoNovoRelatorio.NomeDaEtapa(etapa.Etapa)} · CPU da máquina {cpu} · RAM do executor e subprocessos (pico) {ram}");
                }
            ConsumoExecutores.Children.Add(CartaoDeTexto(grupo.Key == UsoDasIas.Programa ? "Programa local" : grupo.Key, string.Join("    ", metricas)));
        }
        TituloExecutores.Visibility = Visivel(uso.Count != 0);
    }

    static string Ghz(double? mhz) => mhz is { } valor ? $"{valor / 1000:0.##} GHz" : "—";

    partial void GraficosDeConsumo(StackPanel painel, IReadOnlyList<UsoEtapa> uso);

    static long? SomaMedida(IEnumerable<long?> valores)
    {
        var medidos = valores.OfType<long>().ToArray();
        return medidos.Length == 0 ? null : medidos.Sum();
    }
}
