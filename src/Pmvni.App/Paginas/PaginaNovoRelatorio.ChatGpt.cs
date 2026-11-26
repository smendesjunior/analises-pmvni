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
            ? $"{percentual:0.#}% · {dados.VerificacoesInformadas} de {dados.Rodadas.Sum(r => r.Verificacoes)} verificações concluídas no plano"
            : "Aguardando o primeiro grupo conferido pelo plugin.";
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

    FrameworkElement FaixaDaEtapaChatGpt(MonitorAnaliseChatGpt dados, string? etapa, EstadoEtapa estado)
    {
        var corpo = new StackPanel { Padding = new Thickness(62, 0, 16, 16), Spacing = 10 };
        if (estado is EstadoEtapa.Agora or EstadoEtapa.Erro && AtividadeChatGpt is { Length: > 0 } atividade)
            corpo.Children.Add(Texto(atividade, "MnSecundario"));
        if (etapa == "analisar_grupos")
            corpo.Children.Add(CartaoChatGpt("Consulta QGIS", TextoQgisChatGpt(dados)));
        if (etapa is "analisar_grupos" or "auditar_relatorio")
        {
            if (dados.Rodadas.Count == 0)
                corpo.Children.Add(Texto("Aguardando a leitura do plano do plugin.", "MnLegenda"));
            foreach (var rodada in dados.Rodadas.Where(r => r.Grupos.Any(g => g.Id == 15) == (etapa == "auditar_relatorio")))
            {
                var perfil = rodada.Perfil;
                var status = rodada.Confirmada ? "Concluída" :
                    "Aguardando confirmação";
                var rotulo = $"{rodada.Grupos[0].Nome} · {status}";
                var grupos = string.Join(Environment.NewLine, rodada.Grupos.Select(g =>
                    $"{g.Id:00} · {g.Nome} · verificações {string.Join(", ", g.Secoes)}"));
                if (rodada.Percentual is { } percentual)
                    grupos += $"{Environment.NewLine}{percentual:0.#}% · {rodada.Realizadas} de {rodada.Verificacoes} verificações do plano";
                var emAndamento = rodada.Plano.Where(p => p.Estado == "em_andamento").Select(p => p.Secao).Order().ToArray();
                if (emAndamento.Length > 0)
                    grupos += $"{Environment.NewLine}Em andamento · verificações {string.Join(", ", emAndamento)}";
                var perfilExibido = !rodada.Confirmada ? "Perfil previsto · " + perfil.Texto : perfil.Texto;
                corpo.Children.Add(CartaoChatGpt(rotulo, perfilExibido, grupos));
            }
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
