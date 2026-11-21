using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Ajustes › Inteligência artificial › Modelos de IA (2026-11-17; no lugar do "Terra 6.1, esforço baixo" fixo): a IA da revisão e dos ajustes
/// ("Escolha por mim", Codex, Claude Code, OpenAI API, Claude API) e, para cada fonte disponível neste computador, o modelo e o nível de raciocínio,
/// com as listas lidas dos apps e das APIs (<see cref="CatalogoModelosIa"/>). Toda pergunta curta da revisão passa por <see cref="PrepararIa"/>.</summary>
public sealed partial class MainWindow
{
    CatalogoModelosIa? catalogoIa;
    bool montandoModelosIa;
    readonly Dictionary<FonteIa, string> falhasModelosIa = new();
    bool atualizandoModelosIa;

    CatalogoModelosIa CatalogoDeModelos => catalogoIa ??= new CatalogoModelosIa(PastaDados.Raiz, () =>
        OpcoesAgentes is null ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : OpcoesAgentes().CodexHome);

    /// <summary>A fonte está disponível: o programa foi achado e, nas APIs, há chave guardada (o programa roda com ela).</summary>
    bool FonteDisponivel(FonteIa f) => f switch
    {
        FonteIa.Codex => pecasAgentes.Codex is not null,
        FonteIa.ClaudeCode => pecasAgentes.Claude is not null,
        FonteIa.OpenAiApi => pecasAgentes.Codex is not null && CofreChaves.Existe(PastaDados.Raiz, Provedor.ChatGpt),
        _ => pecasAgentes.Claude is not null && CofreChaves.Existe(PastaDados.Raiz, Provedor.Claude),
    };

    /// <summary>Por que a fonte não aparece (para a linha de baixo da seção).</summary>
    string MotivoIndisponivel(FonteIa f) => f switch
    {
        FonteIa.Codex => "o Codex (app ChatGPT) não foi encontrado",
        FonteIa.ClaudeCode => "o Claude Code (app Claude) não foi encontrado",
        FonteIa.OpenAiApi => pecasAgentes.Codex is null ? "precisa do Codex instalado" : "guarde uma chave da OpenAI no cartão ChatGPT",
        _ => pecasAgentes.Claude is null ? "precisa do Claude Code instalado" : "guarde uma chave da Anthropic no cartão Claude",
    };

    // ---------- a pergunta ----------

    /// <summary>A pergunta pronta para <see cref="IaDespacho.PerguntarAsync"/>: o programa (só o da fonte escolhida), as opções e o que a tela mostra.</summary>
    internal sealed record PerguntaIa(string? Claude, string? Codex, IaDespacho.OpcoesIa Opcoes, string Descricao);

    /// <summary>A fábrica única das perguntas curtas (revisão, ajustes, esquadrias, volumetria, instrução do despacho): resolve a fonte
    /// (<see cref="EscolhaIa.Resolver"/>), o modelo e o nível escolhidos em Ajustes, e o ambiente do processo (conta do app; nas APIs, só a chave
    /// daquela fonte, lida do cofre do Windows agora e passada só ao processo filho). Sem fonte nenhuma: os dois programas nulos (a pergunta
    /// explica que falta instalar).</summary>
    internal PerguntaIa PrepararIa(Pecas pecas, TimeSpan? limite)
    {
        bool Disponivel(FonteIa f) => f switch
        {
            FonteIa.Codex => pecas.Codex is not null,
            FonteIa.ClaudeCode => pecas.Claude is not null,
            FonteIa.OpenAiApi => pecas.Codex is not null && CofreChaves.Existe(PastaDados.Raiz, Provedor.ChatGpt),
            _ => pecas.Claude is not null && CofreChaves.Existe(PastaDados.Raiz, Provedor.Claude),
        };
        var (fonte, substituida) = EscolhaIa.Resolver(ajustes.IaRevisaoFonte, Disponivel);
        if (fonte is not { } f) return new PerguntaIa(null, null, new IaDespacho.OpcoesIa(SemArquivos: true, Limite: limite), "");

        var lista = CatalogoDeModelos.Lista(f);
        var escolha = ajustes.IaModelos[f];
        var modelo = lista.Modelo(escolha.Modelo) ?? (escolha.Modelo.Length == 0 ? lista.Modelo(lista.ModeloDoApp) : null);
        escolha = escolha with { Nivel = EscolhaIa.AjustarNivel(modelo, escolha.Nivel) };
        var argumentos = EscolhaIa.Argumentos(f, escolha);

        string? chave = null;
        if (FontesIa.PorChave(f))
            chave = CofreChaves.Ler(PastaDados.Raiz, FontesIa.Provedor(f))
                ?? throw new InvalidOperationException("Não foi possível ler a chave de " + FontesIa.NomeCurto(f) + " guardada neste computador. Guarde-a de novo em Ajustes › Inteligência artificial.");
        var provedor = FontesIa.Provedor(f);
        var modo = chave is null ? ModoAcesso.Conta : ModoAcesso.Chave;
        void Ambiente(IDictionary<string, string?> env) => AmbienteFilho.Aplicar(env, provedor, modo, chave);

        var codex = FontesIa.PeloCodex(f);
        var opcoes = new IaDespacho.OpcoesIa(PreferirCodex: codex, ArgumentosCodex: codex ? argumentos : null, SemArquivos: true, Limite: limite,
            ArgumentosClaude: codex ? null : argumentos, Ambiente: Ambiente);
        var descricao = EscolhaIa.Descricao(f, escolha, lista) + (substituida ? "; a IA escolhida não está disponível" : "");
        return new PerguntaIa(codex ? null : pecas.Claude, codex ? pecas.Codex : null, opcoes, descricao);
    }

    // ---------- a seção dos Ajustes ----------

    void IniciarModelosIa()
    {
        var p = PaginaDeAjustes;
        p.CaixaIaFonte.SelectionChanged += (_, _) =>
        {
            if (montandoModelosIa || p.CaixaIaFonte.SelectedItem is not ComboBoxItem { Tag: string v } || v == ajustes.IaRevisaoFonte) return;
            ajustes.IaRevisaoFonte = v;
            SalvarAjustes();
            // 2026-11-17: remontar a própria caixa dentro do SelectionChanged (com a lista ainda fechando) derrubava o app no WinUI
            // (COMException 0x80070490). Remonta depois, na fila da janela; a caixa da fonte só é refeita se as opções mudaram.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, MostrarModelosIa);
        };

        // A entrada na categoria e o botão "Atualizar lista" já atualizam o catálogo; sem relógio em segundo plano.
        p.CategoriaMostrada += async tag =>
        {
            if (tag != CategoriasAjustes.Modelos) return;
            try { await tarefaDosAgentes; } catch (Exception e) { System.Diagnostics.Debug.WriteLine("[modelos] " + e.Message); }
            MostrarModelosIa();
            if (PaginaAjustesVisivel && p.CategoriaAtual == CategoriasAjustes.Modelos) await AtualizarModelosIaAsync();
        };
    }

    /// <summary>"Atualizar lista" (ou a primeira leitura): roda fora da thread da janela; quem não responde fica com a lista anterior.</summary>
    async Task AtualizarModelosIaAsync(IReadOnlyCollection<FonteIa>? fontes = null)
    {
        if (fechada || atualizandoModelosIa) return;
        fontes ??= FontesIa.Todas.Where(FonteDisponivel).ToList();
        if (fontes.Count == 0) { MostrarModelosIa(); return; }
        var p = PaginaDeAjustes;
        atualizandoModelosIa = true;
        p.BotaoAtualizarModelosIa.IsEnabled = false;
        p.AnelModelosIa.IsActive = true;
        p.AnelModelosIa.Visibility = Visibility.Visible;
        p.TextoModelosIa.Text = "Lendo os modelos de " + string.Join(", ", fontes.Select(FontesIa.NomeCurto)) + "…";
        try
        {
            var pecas = pecasAgentes;
            var falhas = await Task.Run(() => CatalogoDeModelos.AtualizarAsync(pecas, fontes, CancellationToken.None));
            foreach (var f in fontes) falhasModelosIa.Remove(f);
            foreach (var (f, motivo) in falhas) falhasModelosIa[f] = motivo;
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[modelos] " + e.Message); }
        finally
        {
            atualizandoModelosIa = false;
            p.BotaoAtualizarModelosIa.IsEnabled = true;
            p.AnelModelosIa.IsActive = false;
            p.AnelModelosIa.Visibility = Visibility.Collapsed;
        }
        if (!fechada)
        {
            MostrarModelosIa();
            if (p.CategoriaAtual == CategoriasAjustes.Modelos) MostrarModelosDaAnalise();
        }
    }

    /// <summary>Monta a seção de acordo com os ajustes, as peças e as listas: a caixa da fonte, uma linha (modelo e nível) por fonte disponível e
    /// a linha de situação (de onde veio cada lista; o que não está disponível e por quê).</summary>
    void MostrarModelosIa()
    {
        if (fechada) return;
        var p = PaginaDeAjustes;
        montandoModelosIa = true;
        try
        {
            // A caixa da fonte: "Escolha por mim" e as disponíveis; a escolhida fica mesmo se sumiu (marcada como indisponível).
            var opcoes = new List<(string Texto, string Tag)> { ("Escolha por mim", EscolhaIa.Automatico) };
            foreach (var f in FontesIa.Todas)
            {
                var valor = FontesIa.Valor(f);
                if (FonteDisponivel(f)) opcoes.Add((FontesIa.Nome(f), valor));
                else if (valor == ajustes.IaRevisaoFonte) opcoes.Add((FontesIa.Nome(f) + " (indisponível)", valor));
            }
            var atuais = p.CaixaIaFonte.Items.OfType<ComboBoxItem>().Select(i => ((i.Content as string) ?? "", (string)i.Tag)).ToList();
            if (!atuais.SequenceEqual(opcoes))
            {
                p.CaixaIaFonte.Items.Clear();
                foreach (var (texto, tag) in opcoes) p.CaixaIaFonte.Items.Add(new ComboBoxItem { Content = texto, Tag = tag });
            }
            var alvo = p.CaixaIaFonte.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == ajustes.IaRevisaoFonte) ?? p.CaixaIaFonte.Items[0];
            if (!ReferenceEquals(p.CaixaIaFonte.SelectedItem, alvo)) p.CaixaIaFonte.SelectedItem = alvo;

            var (usada, substituida) = EscolhaIa.Resolver(ajustes.IaRevisaoFonte, FonteDisponivel);
            p.TextoFonteIa.Text = usada is null
                ? "Nenhuma IA disponível: instale e entre no app ChatGPT (Codex) ou no app Claude (Claude Code) nos cartões acima."
                : ajustes.IaRevisaoFonte == EscolhaIa.Automatico
                    ? "Escolha por mim: o Codex, se estiver neste computador; senão o Claude Code. Agora: " + FontesIa.NomeCurto(usada.Value) + "."
                    : substituida
                        ? "A IA escolhida não está disponível; enquanto isso, responde o " + FontesIa.NomeCurto(usada.Value) + " (depois o Claude Code). As APIs só respondem quando escolhidas."
                        : "Responde sempre o " + FontesIa.NomeCurto(usada.Value) + ". Se ele sumir deste computador, responde o Codex (depois o Claude Code).";

            p.PainelModelosIa.Children.Clear();
            foreach (var f in FontesIa.Todas.Where(FonteDisponivel))
            {
                p.PainelModelosIa.Children.Add(new Border { Style = (Style)Application.Current.Resources["PmvniAjusteDivisoria"] });
                p.PainelModelosIa.Children.Add(LinhaDaFonte(f, f == usada));
            }

            var partes = new List<string>();
            foreach (var f in FontesIa.Todas.Where(f => !FonteDisponivel(f))) partes.Add(FontesIa.NomeCurto(f) + ": " + MotivoIndisponivel(f));
            p.TextoModelosIa.Text = partes.Count == 0 ? "Todas as IAs deste computador estão na lista." : "Fora da lista — " + string.Join("; ", partes) + ".";
        }
        finally { montandoModelosIa = false; }
        // Tarefa 19: as caixas de "Modelos da análise" usam as mesmas listas (relidas ao abrir a página e em "Atualizar lista").
        MostrarModelosDaAnalise();
    }

    /// <summary>A linha de uma fonte: o nome, de onde veio a lista, e as caixas do modelo e do nível.</summary>
    Grid LinhaDaFonte(FonteIa f, bool emUso)
    {
        var lista = CatalogoDeModelos.Lista(f);
        var escolha = ajustes.IaModelos[f];

        var linha = new Grid { Style = (Style)PaginaDeAjustes.Resources["Linha"] };
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        linha.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        linha.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        linha.SizeChanged += (s, e) => LinhaAjuste.Ajustar((Grid)s, e.NewSize.Width);

        var textos = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock { TextAlignment = TextAlignment.Left, Text = FontesIa.Nome(f) + (emUso ? " — em uso" : ""), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
        textos.Children.Add(new TextBlock { TextAlignment = TextAlignment.Left, Text = OrigemDaLista(f, lista), Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] });
        linha.Children.Add(textos);

        var caixas = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(caixas, 1);
        var caixaModelo = new ComboBox { Style = (Style)Application.Current.Resources["PmvniAjusteEscolha"] };
        var caixaNivel = new ComboBox { Style = (Style)Application.Current.Resources["PmvniAjusteEscolha"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixaModelo, "Modelo de " + FontesIa.NomeCurto(f));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixaNivel, "Nível de raciocínio de " + FontesIa.NomeCurto(f));
        Dicas.Aplicar(caixaModelo, "Modelo", "O modelo que responde quando esta IA é usada. Padrão do app: o que estiver escolhido no próprio app.");
        Dicas.Aplicar(caixaNivel, "Raciocínio", "Quanto o modelo pensa antes de responder: mais nível, resposta mais cuidadosa e mais demorada.");

        var nomeDoApp = lista.Modelo(lista.ModeloDoApp)?.Nome ?? lista.ModeloDoApp;
        var itemPadrao = new ComboBoxItem { Content = "Padrão do app", Tag = "" };
        if (nomeDoApp is not null) ToolTipService.SetToolTip(itemPadrao, nomeDoApp);
        caixaModelo.Items.Add(itemPadrao);
        foreach (var m in lista.Modelos) caixaModelo.Items.Add(new ComboBoxItem { Content = m.Nome, Tag = m.Id });
        if (escolha.Modelo.Length > 0 && lista.Modelo(escolha.Modelo) is null)
            caixaModelo.Items.Add(new ComboBoxItem { Content = escolha.Modelo + " (fora da lista)", Tag = escolha.Modelo });
        caixaModelo.SelectedItem = caixaModelo.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == escolha.Modelo);

        void PreencherNiveis(string modeloId, string nivel)
        {
            var modelo = lista.Modelo(modeloId) ?? (modeloId.Length == 0 ? lista.Modelo(lista.ModeloDoApp) : null);
            var niveis = modelo?.Niveis ?? (modeloId.Length == 0 ? [] : CatalogoIa.NiveisConhecidos.Where(n => n is "low" or "medium" or "high").ToList());
            var padrao = modeloId.Length == 0 ? lista.NivelDoApp ?? modelo?.NivelPadrao : modelo?.NivelPadrao;
            caixaNivel.Items.Clear();
            var nivelPadrao = new ComboBoxItem { Content = "Padrão", Tag = "" };
            if (padrao is not null) ToolTipService.SetToolTip(nivelPadrao, CatalogoIa.NomeNivel(padrao));
            caixaNivel.Items.Add(nivelPadrao);
            foreach (var n in niveis) caixaNivel.Items.Add(new ComboBoxItem { Content = CatalogoIa.NomeNivel(n), Tag = n });
            caixaNivel.SelectedItem = caixaNivel.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == nivel) ?? caixaNivel.Items[0];
            caixaNivel.IsEnabled = niveis.Count > 0;
        }
        PreencherNiveis(escolha.Modelo, escolha.Nivel);

        caixaModelo.SelectionChanged += (_, _) =>
        {
            if (montandoModelosIa || caixaModelo.SelectedItem is not ComboBoxItem { Tag: string id }) return;
            var atual = ajustes.IaModelos[f];
            var nivel = EscolhaIa.AjustarNivel(lista.Modelo(id) ?? (id.Length == 0 ? lista.Modelo(lista.ModeloDoApp) : null), atual.Nivel);
            ajustes.IaModelos[f] = new ModeloEscolhido(id, nivel);
            SalvarAjustes();
            montandoModelosIa = true;
            try { PreencherNiveis(id, nivel); }
            finally { montandoModelosIa = false; }
        };
        caixaNivel.SelectionChanged += (_, _) =>
        {
            if (montandoModelosIa || caixaNivel.SelectedItem is not ComboBoxItem { Tag: string n }) return;
            ajustes.IaModelos[f] = ajustes.IaModelos[f] with { Nivel = n };
            SalvarAjustes();
        };
        caixas.Children.Add(caixaModelo);
        caixas.Children.Add(caixaNivel);
        linha.Children.Add(caixas);
        return linha;
    }

    string OrigemDaLista(FonteIa f, ListaModelos l)
    {
        var quando = l.LidaEm is { } q ? " em " + q.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR")) : "";
        var de = f switch
        {
            FonteIa.Codex => "do app ChatGPT",
            FonteIa.ClaudeCode => "do app Claude",
            _ => "da API",
        };
        var texto = l.Origem switch
        {
            "reserva" => "Lista conhecida do editor (ainda não foi possível ler a lista " + de + ").",
            "guardada" => "Lista " + de + ", lida" + quando + ".",
            _ => "Lista " + de + (quando.Length > 0 ? ", atualizada" + quando : "") + ".",
        };
        if (FontesIa.PorChave(f)) texto += " Cobrada por uso, com a chave guardada neste computador.";
        if (falhasModelosIa.TryGetValue(f, out var falha)) texto += " Não foi possível atualizar agora (" + falha + "); vale a lista anterior.";
        return texto;
    }
}
