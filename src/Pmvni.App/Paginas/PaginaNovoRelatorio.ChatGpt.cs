using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;

namespace Pmvni.App;

public sealed partial class PaginaNovoRelatorio
{
    MonitorAnaliseChatGpt? monitorChatGpt;
    internal MonitorAnaliseChatGpt? MonitorChatGpt
    {
        get => monitorChatGpt;
        set
        {
            var antes = monitorChatGpt;
            monitorChatGpt = value;
            if ((antes is null) != (value is null) || antes?.Percentual != value?.Percentual || antes?.RodadasConcluidas != value?.RodadasConcluidas)
                MostrarEtapas(etapasMostradas, execucaoAtivaNaBarra);
        }
    }
    internal PerfilRodadaChatGpt? PerfilChatGptAtivo { get; set; }
    internal string? AtividadeChatGpt { get; set; }

    static string TextoQgisChatGpt(MonitorAnaliseChatGpt dados) => dados.Qgis switch
    {
        "CONSULTADA" => dados.Candidatos switch
        {
            0 => "Base consultada. Nenhum candidato retornado.",
            1 => "Base consultada. Uma feição candidata retornada.",
            int total => $"Base consultada. {total} feições candidatas retornadas.",
            _ => "Base consultada."
        },
        "INDISPONIVEL" => "Base indisponível. A tentativa ficou registrada na execução.",
        _ => "Aguardando a consulta local obrigatória."
    };

    void MostrarResumoChatGpt(MonitorAnaliseChatGpt dados)
    {
        Adicionar(TituloDePagina(Glifos.Painel, "Resumo da análise", "analise-chatgpt · Conta ChatGPT"), 0);
        var plano = dados.Rodadas.Count > 0;
        var progresso = dados.Percentual is { } percentual
            ? $"{percentual:0.#}% · {dados.VerificacoesInformadas} de {dados.Rodadas.Sum(r => r.Verificacoes)} verificações com resultado registrado"
            : "Aguardando o plano de grupos do plugin.";
        var confirmadas = plano
            ? $"{dados.GruposConcluidos} de {dados.Rodadas.Sum(r => r.Grupos.Count)} grupos conferidos na matriz do plugin."
            : null;
        Adicionar(CartaoChatGpt("Plano de trabalho", progresso, confirmadas), 14);
        if (AtividadeChatGpt is { Length: > 0 } atividade)
            Adicionar(CartaoChatGpt("Atividade da análise", atividade), 12);
        Adicionar(CartaoChatGpt("Consulta QGIS", TextoQgisChatGpt(dados)), 12);
        Adicionar(CartaoChatGpt("Relatório técnico",
            etapasMostradas.Length > 0 && etapasMostradas.All(e => e.Estado == EstadoEtapa.Feita)
                ? "Pacote conferido e salvo para revisão."
                : "Os achados serão apresentados no relatório editável após a auditoria e a validação do pacote."), 12);
    }

    Border CartaoChatGpt(string titulo, string texto, string? detalhe = null)
    {
        var cartao = Cartao();
        var corpo = new StackPanel { Margin = new Thickness(16, 12, 16, 12), Spacing = 5 };
        corpo.Children.Add(Texto(titulo, "MnCorpoForte"));
        corpo.Children.Add(Texto(texto, "MnCorpo"));
        if (!string.IsNullOrWhiteSpace(detalhe)) corpo.Children.Add(Texto(detalhe, "MnSecundario"));
        cartao.Child = corpo;
        return cartao;
    }

    FrameworkElement CartoesDosGrupos(IEnumerable<RodadaDoMonitorChatGpt> rodadas, bool mostrarModelo, EstadoEtapa estadoEtapa)
    {
        var grade = new PainelQuebra { Espaco = 8, EspacoLinhas = 8 };
        foreach (var rodada in rodadas)
        {
            var grupo = rodada.Grupos[0];
            var estado = rodada.Confirmada ? "Auditoria conferida" : rodada.Realizadas == rodada.Verificacoes
                ? "Aguardando auditoria" : rodada.Realizadas > 0 ? "Em análise" : "Aguardando evidências";
            var cartao = CartaoChatGpt($"{grupo.Id:00} · {grupo.Nome}", $"{rodada.Realizadas}/{rodada.Verificacoes} seções · {estado}");
            cartao.Width = 260;
            var corpo = (StackPanel)cartao.Child;
            var barra = new ProgressBar { Minimum = 0, Maximum = 100, Value = rodada.Percentual };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(barra, $"Progresso de {grupo.Nome}");
            corpo.Children.Add(barra);
            ToolTipService.SetToolTip(cartao, $"Seções {string.Join(", ", grupo.Secoes)} · Perfil previsto: {rodada.Perfil.Texto}");
            grade.Children.Add(cartao);
        }
        if (mostrarModelo)
        {
            var perfil = PerfilChatGptAtivo;
            var bloco = Cartao();
            bloco.Width = 260;
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(16, 12, 16, 12) };
            var logo = new Image { Width = 26, Height = 26, Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Recursos/Provedores/openai.svg")) };
            linha.Children.Add(new Border { Child = logo, Width = 38, Height = 38, CornerRadius = new CornerRadius(19),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White), VerticalAlignment = VerticalAlignment.Center });
            var rotulo = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            rotulo.Children.Add(Texto(perfil?.Modelo ?? "OpenAI", "MnCorpoForte"));
            rotulo.Children.Add(Texto(perfil is null ? (estadoEtapa == EstadoEtapa.Agora ? "Aguardando perfil ativo" : "Sem modelo ativo")
                : DetalhesDasEtapas.NomeDoEsforco(perfil.Esforco) is { } nivel ? "Esforço " + nivel : "Esforço não informado", "MnSecundario"));
            linha.Children.Add(rotulo);
            bloco.Child = linha;
            grade.Children.Add(bloco);
        }
        return grade;
    }

    FrameworkElement FaixaDaEtapaChatGpt(MonitorAnaliseChatGpt dados, string? etapa, EstadoEtapa estado)
    {
        var corpo = new StackPanel { Padding = new Thickness(62, 0, 16, 16), Spacing = 10 };
        if (estado is EstadoEtapa.Agora or EstadoEtapa.Erro && AtividadeChatGpt is { Length: > 0 } atividade)
            corpo.Children.Add(Texto(atividade, "MnSecundario"));
        Border? qgis = null;
        if (etapa == "analisar_grupos")
        {
            qgis = CartaoChatGpt("Consulta QGIS", TextoQgisChatGpt(dados));
            qgis.HorizontalAlignment = HorizontalAlignment.Left;
            var barra = new ProgressBar { Minimum = 0, Maximum = 100, Value = dados.Qgis == "CONSULTADA" ? 100 : 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(barra, "Consulta QGIS. " + TextoQgisChatGpt(dados));
            ((StackPanel)qgis.Child).Children.Add(barra);
            corpo.Children.Add(qgis);
        }
        if (etapa is "analisar_grupos" or "auditar_relatorio")
        {
            if (dados.Rodadas.Count == 0)
                corpo.Children.Add(Texto("Aguardando a leitura do plano do plugin.", "MnLegenda"));
            var grade = CartoesDosGrupos(dados.Rodadas.Where(r => (r.Perfil.Numero == 15) == (etapa == "auditar_relatorio")), qgis is not null, estado);
            grade.HorizontalAlignment = HorizontalAlignment.Left;
            if (qgis is not null)
                grade.SizeChanged += (_, e) => qgis.Width = e.NewSize.Width;
            corpo.Children.Add(grade);
        }
        else
        {
            var texto = etapa == "preparar_fontes"
                ? "O Editor confere a conta ChatGPT e a seleção de documentos."
                : "O plugin gera o .rpmvni. O Editor confere o pacote e salva na etapa de despacho.";
            corpo.Children.Add(Texto(texto, "MnSecundario"));
        }
        return corpo;
    }
}
