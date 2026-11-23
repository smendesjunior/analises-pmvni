using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Tarefa 19: Ajustes › Modelos da análise. A grade (linhas = etapas do <c>etapas.json</c> do kit, para a rota e o perfil selecionados
/// na tabela do kit, com modelo e nível por etapa), as regras, a auditoria cruzada por rota, "Restaurar padrão do kit" e, no computador do kit mestre,
/// "Publicar como padrão do kit". A lógica fica em <see cref="ModelosDaAnalise"/>; aqui só a tela. O ajuste é gravado direto em
/// <c>%LOCALAPPDATA%\EditorRelatoriosPMVNI</c> (<see cref="PastaDados.Raiz"/>; o Editor não é MSIX: não há redirecionamento de pasta). O Recriar PC
/// guarda o arquivo por um passo próprio do <c>recriar.json</c>.</summary>
public sealed partial class MainWindow
{
    AjusteModelos? ajusteModelos;
    bool montandoModelosAnalise;
    ColunaModelos? rotaEmEdicao;

    /// <summary>Opções da caixa da cota baixa: (rótulo, fração; null = não desce).</summary>
    static readonly (string Texto, double? Valor)[] OpcoesCotaBaixa =
        [("Não descer por cota", null), ("5% ou menos restantes", 0.05), ("10% ou menos restantes", 0.10), ("15% ou menos restantes", 0.15),
         ("20% ou menos restantes", 0.20), ("25% ou menos restantes", 0.25), ("30% ou menos restantes", 0.30)];

    AjusteModelos AjusteDosModelos
    {
        get
        {
            if (ajusteModelos is not null) return ajusteModelos;
            string? texto = null;
            try { var f = ModelosDaAnalise.ArquivoLocal(PastaDados.Raiz); if (File.Exists(f)) texto = File.ReadAllText(f, Encoding.UTF8); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { RegistroErros.Gravar("modelos.analise.ler", e); }
            return ajusteModelos = ModelosDaAnalise.LerAjuste(texto);
        }
    }

    /// <summary>A tabela do kit instalado (null antes do kit com a tabela) e as etapas do <c>etapas.json</c> dele.</summary>
    (TabelaModelosKit? Kit, IReadOnlyList<EtapaModelos> Etapas) TabelaDoKitInstalado()
    {
        static string? Ler(string? arquivo)
        {
            try { return arquivo is not null && File.Exists(arquivo) ? File.ReadAllText(arquivo, Encoding.UTF8) : null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
        }
        var kit = ModelosDaAnalise.LerTabela(Ler(pecasAgentes.ModelosPorRota));
        return (kit, kit is null ? [] : ModelosDaAnalise.LerEtapas(Ler(pecasAgentes.EtapasJson), kit));
    }

    /// <summary>Correção 1: as listas do Gemini que a rota Gemini do kit devolveu (<c>google_runtime.py modelos</c>), por grupo (<c>conta</c>: agy; <c>api</c>:
    /// a chave), com a hora da leitura; e o último motivo de não ter lista.</summary>
    readonly Dictionary<string, (ListaDaRota Lista, DateTimeOffset Em)> listasGemini = new();
    string? falhaListaGemini;
    bool atualizandoListaGemini;

    /// <summary>Só roteiro: variáveis a mais no ambiente do <c>google_runtime.py modelos</c> (o agy falso).</summary>
    internal IReadOnlyDictionary<string, string>? AmbienteListaGeminiDoRoteiro { get; set; }

    /// <summary>A fonte da lista de modelos da rota no modo em uso: ChatGPT pelo app ou pela OpenAI API; Claude pelo app ou pela Claude API.</summary>
    FonteIa FonteDaRota(string rota) => rota == "codex"
        ? ajustes.Agentes[Provedor.ChatGpt].Modo == ModoAcesso.Chave ? FonteIa.OpenAiApi : FonteIa.Codex
        : ajustes.Agentes[Provedor.Claude].Modo == ModoAcesso.Chave ? FonteIa.AnthropicApi : FonteIa.ClaudeCode;

    /// <summary>A lista de modelos que o provedor da coluna oferece agora: ChatGPT e Claude pela lista do modo em uso (app ou API); Gemini pela lista que a rota
    /// Gemini do kit devolveu para o grupo do perfil (agy ou API) e, no Gemini Web, os modelos da tabela do kit. Enquanto o Gemini não respondeu, os da tabela,
    /// sem recusar nada.</summary>
    ListaDaRota? ListaDaRotaAgora(ColunaModelos col, TabelaModelosKit kit)
    {
        if (col.Rota == "gemini")
        {
            var grupo = ModelosDaAnalise.GrupoGemini(col.Perfil);
            if (grupo == "web") return ModelosDaAnalise.ListaGemini(kit);
            return listasGemini.TryGetValue(grupo, out var g) ? g.Lista
                : ModelosDaAnalise.ListaGemini(kit, conferida: false, "da tabela do kit (a lista do Gemini ainda não foi lida)");
        }
        var fonte = FonteDaRota(col.Rota);
        var l = CatalogoDeModelos.Lista(fonte);
        var de = fonte switch { FonteIa.Codex => "do app ChatGPT", FonteIa.OpenAiApi => "da OpenAI API", FonteIa.ClaudeCode => "do app Claude", _ => "da Claude API" };
        return new ListaDaRota(l.Modelos, Conferida: l.Origem != "reserva", de);
    }

    /// <summary>Lê de novo a lista do Gemini pelo perfil escolhido no cartão Gemini (sem gerar texto). Gemini Web e perfil não escolhido: nada a ler.
    /// Sem resposta, vale a lista anterior.</summary>
    async Task AtualizarListaGeminiAsync()
    {
        if (fechada || atualizandoListaGemini || PerfilDoGemini is not { } perfil || perfil == PerfilGemini.ContaGemini) return;
        var pecas = pecasAgentes;
        if (pecas.GoogleRuntime is null || pecas.Python is null) return;
        if (perfil == PerfilGemini.ContaUltra && pecas.Agy is null) { falhaListaGemini = "o Antigravity CLI (agy) não foi encontrado"; return; }
        string? chave = null;
        if (MatrizGemini.Modo(perfil) == ModoAcesso.Chave && (chave = CofreChaves.Ler(PastaDados.Raiz, Provedor.Gemini)) is null) { falhaListaGemini = "nenhuma chave do Gemini guardada"; return; }
        atualizandoListaGemini = true;
        try
        {
            var extra = AmbienteListaGeminiDoRoteiro;
            var (json, falha) = await Task.Run(() => ModelosDoGemini.LerAsync(pecas, perfil, chave, PastaDados.Raiz, CancellationToken.None, extra));
            var (kit, _) = TabelaDoKitInstalado();
            if (kit is not null && ModelosDaAnalise.LerListaGemini(json, kit) is { } lista)
            {
                listasGemini[ModelosDaAnalise.GrupoGemini(MatrizGemini.Codigo(perfil))] = (lista, DateTimeOffset.Now);
                falhaListaGemini = null;
            }
            else falhaListaGemini = falha ?? "o Gemini não devolveu nenhum modelo";
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            falhaListaGemini = e.Message;
        }
        finally { atualizandoListaGemini = false; }
    }

    /// <summary>De onde vem a lista de cada coluna, para a linha abaixo da grade (o Gemini Web diz que não lista modelos).</summary>
    void IniciarModelosDaAnalise()
    {
        var p = PaginaDeAjustes;
        p.CartoesRotas.SizeChanged += (_, e) => DistribuirCartoes(p.CartoesRotas, e.NewSize.Width);
        foreach (var (texto, _) in OpcoesCotaBaixa) p.CaixaCotaBaixa.Items.Add(new ComboBoxItem { Content = texto });
        p.AlternarSobeObjecao.Toggled += (_, _) =>
        {
            if (montandoModelosAnalise || TabelaDoKitInstalado().Kit is not { } kit) return;
            var a = AjusteDosModelos;
            a.SobePorObjecao = p.AlternarSobeObjecao.IsOn == kit.SobePorObjecao ? null : p.AlternarSobeObjecao.IsOn;
            GravarAjusteDosModelos("Regra da objeção técnica gravada neste computador.");
        };
        p.CaixaCotaBaixa.SelectionChanged += (_, _) =>
        {
            if (montandoModelosAnalise || p.CaixaCotaBaixa.SelectedIndex < 0 || TabelaDoKitInstalado().Kit is not { } kit) return;
            var valor = OpcoesCotaBaixa[p.CaixaCotaBaixa.SelectedIndex].Valor;
            var a = AjusteDosModelos;
            a.TrocaDescePorCota = !Igual(valor, kit.DescePorCota);
            a.DescePorCota = a.TrocaDescePorCota ? valor : null;
            GravarAjusteDosModelos("Regra da cota baixa gravada neste computador.");
        };
        p.BotaoRestaurarModelos.Click += async (_, _) =>
        {
            if (AjusteDosModelos.Vazio) { p.TextoModelosAnalise.Text = "Já está no padrão do kit."; return; }
            if (!await Perguntas.Atual.ConfirmarAsync("Restaurar o padrão do kit?", "Os modelos, os níveis, as regras e a auditoria cruzada deste computador voltam ao padrão do kit. O ajuste local é apagado.",
                    "Restaurar", "Cancelar", destrutiva: false)) return;
            ajusteModelos = ModelosDaAnalise.Restaurar();
            GravarAjusteDosModelos("Padrão do kit restaurado: o ajuste deste computador foi apagado.");
        };
        p.BotaoPublicarModelos.Click += async (_, _) => await PublicarModelosAsync();
        // Correção 1: a lista do Gemini é lida pela rota Gemini do kit ao abrir esta página e em "Atualizar lista", como as outras.
        p.CategoriaMostrada += async tag =>
        {
            if (tag != CategoriasAjustes.Modelos) return;
            // 3.2.2: a tabela de modelos vem do plugin analise localizado pela leitura das peças, que roda em segundo plano ao abrir o editor.
            // Na primeira visita a página era montada antes de a leitura terminar e mostrava "kit sem tabela" até reabrir; agora espera a leitura
            // (e a dispara, se ninguém a disparou ainda) antes de montar.
            if (ReferenceEquals(pecasAgentes, PecasNaoLidas)) AtualizarAgentes();
            try { await tarefaDosAgentes; } catch (Exception e) { System.Diagnostics.Debug.WriteLine("[modelos] " + e.Message); }
            MostrarModelosDaAnalise();
            await AtualizarListaGeminiAsync();
            MostrarModelosDaAnalise();
        };
        p.BotaoAtualizarModelosIa.Click += async (_, _) => { await Task.WhenAll(AtualizarModelosIaAsync(), AtualizarListaGeminiAsync()); MostrarModelosDaAnalise(); };
    }

    static void DistribuirCartoes(Grid grade, double largura)
    {
        var total = grade.Children.Count;
        var colunas = Math.Min(total, Math.Max(1, (int)((largura + 6) / 134)));
        if (colunas == 0) return;
        grade.ColumnDefinitions.Clear();
        grade.RowDefinitions.Clear();
        for (var i = 0; i < colunas; i++) grade.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < total; i++)
        {
            if (i % colunas == 0) grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var cartao = (FrameworkElement)grade.Children[i];
            Grid.SetRow(cartao, i / colunas);
            Grid.SetColumn(cartao, i % colunas);
        }
    }

    static bool Igual(double? a, double? b) => a is null ? b is null : b is not null && Math.Abs(a.Value - b.Value) < 1e-9;

    void GravarAjusteDosModelos(string texto)
    {
        try
        {
            ModelosDaAnalise.GravarAjuste(PastaDados.Raiz, AjusteDosModelos);
            PaginaDeAjustes.TextoModelosAnalise.Text = texto;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RegistroErros.Gravar("modelos.analise.gravar", e);
            PaginaDeAjustes.TextoModelosAnalise.Text = "Não foi possível gravar o ajuste (" + e.Message + "). Vale o que estava gravado.";
            ajusteModelos = null;
        }
        // Remonta depois, na fila da janela (mudar a grade dentro do SelectionChanged de uma caixa dela derruba o WinUI).
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, MostrarModelosDaAnalise);
    }

    /// <summary>Monta a seção: a grade, as regras, a auditoria cruzada, os avisos e os botões.</summary>
    void MostrarModelosDaAnalise()
    {
        if (fechada) return;
        var p = PaginaDeAjustes;
        var (kit, etapas) = TabelaDoKitInstalado();
        montandoModelosAnalise = true;
        try
        {
            p.PainelModelosAnalise.Children.Clear();
            p.PainelAuditoriaCruzada.Children.Clear();
            p.CartoesRotas.Children.Clear();
            p.TituloRota.Text = "";
            p.RodapeModelos.Visibility = Visibility.Collapsed;
            var habilitado = kit is not null;
            p.AlternarSobeObjecao.IsEnabled = p.CaixaCotaBaixa.IsEnabled = p.BotaoRestaurarModelos.IsEnabled = habilitado;
            if (kit is null)
            {
                p.PainelModelosAnalise.Children.Add(new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"],
                    Text = "O kit PMVNI instalado neste computador ainda não tem a tabela de modelos da análise (plugin analise com o arquivo modelos-por-rota.json). "
                        + "Atualize o kit em Ajustes › Pastas e kit; até lá, cada rota usa os modelos que o próprio kit define.",
                });
                p.AvisoModelosAnalise.IsOpen = false;
                p.BotaoPublicarModelos.Visibility = Visibility.Collapsed;
                return;
            }
            var a = AjusteDosModelos;
            if (kit.Colunas.Count == 0) return;
            var selecionada = kit.Colunas.FirstOrDefault(c => c == rotaEmEdicao) ?? kit.Colunas[0];
            foreach (var coluna in kit.Colunas)
            {
                var partes = coluna.Titulo.Split(" · ", 2, StringSplitOptions.None);
                var perfil = partes.Length == 2 ? partes[1] : coluna.Rota switch
                {
                    "gemini" => coluna.Perfil,
                    _ when FonteDaRota(coluna.Rota) is FonteIa.Codex or FonteIa.ClaudeCode => "Aplicativo",
                    _ => "API"
                };
                var botao = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
                {
                    // Nome da rota em cima e tipo embaixo, sem corte: o cartão cresce em altura, e todos da linha ficam com a mesma altura.
                    Content = new TextBlock { Text = partes[0] + "\n" + perfil, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap },
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                    IsChecked = coluna == selecionada, MinHeight = 56, Padding = new Thickness(8, 6, 8, 6)
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(botao, "Configurar " + coluna.Titulo);
                ToolTipService.SetToolTip(botao, partes[0] + " · " + perfil);
                botao.Click += (_, _) => { rotaEmEdicao = coluna; MostrarModelosDaAnalise(); };
                p.CartoesRotas.Children.Add(botao);
            }
            DistribuirCartoes(p.CartoesRotas, p.CartoesRotas.ActualWidth);
            p.TituloRota.Text = selecionada.Titulo;
            p.PainelModelosAnalise.Children.Add(GradeDosModelos(kit, etapas, a, selecionada));
            p.OrigemModelos.Text = $"Modelos obtidos {ListaDaRotaAgora(selecionada, kit)?.Fonte} e atualizados ao abrir a página e antes de cada análise." + (selecionada.Rota == "gemini" && falhaListaGemini is { } falha ? " A última consulta falhou. " + falha : "");
            p.RodapeModelos.Visibility = Visibility.Visible;

            p.AlternarSobeObjecao.IsOn = a.SobePorObjecao ?? kit.SobePorObjecao;
            var desce = a.TrocaDescePorCota ? a.DescePorCota : kit.DescePorCota;
            var i = Array.FindIndex(OpcoesCotaBaixa, o => Igual(o.Valor, desce));
            p.CaixaCotaBaixa.SelectedIndex = i >= 0 ? i : -1;
            var sobem = kit.RotasQueSobem.Select(r => Provedores.Nome(ModelosDaAnalise.ProvedorDaRota(r))).ToList();
            p.TextoRegraObjecao.Text = "Depois de uma falha técnica registrada (validador reprovado, objeção da auditoria ou da revisão final), a nova tentativa da etapa sobe um nível, até alto. "
                + "Cota e problemas do computador nunca sobem. "
                + (sobem.Count == 0 ? "Pelas exceções do kit, não vale em nenhuma rota."
                    : sobem.Count == ModelosDaAnalise.Rotas.Length ? "Vale nas três rotas."
                    : "Vale só " + (sobem.Count == 1 ? "na rota " + sobem[0] : "nas rotas " + string.Join(", ", sobem)) + "; nas outras, o kit não muda o nível por objeção.");
            p.TextoRegrasDasRotas.Text = string.Join(" ", kit.NotasDasRotas.Values)
                + (kit.NotasDasRotas.Count > 0 ? " " : "") + "Padrão do kit: objeção técnica " + (kit.SobePorObjecao ? "sobe" : "não sobe") + " um nível; cota baixa "
                + (kit.DescePorCota is { } d ? "com " + (d * 100).ToString("0", CultureInfo.InvariantCulture) + "% ou menos restantes" : "não desce o nível") + ".";

            foreach (var e in etapas.Where(e => e.Opcional))
                foreach (var rota in ModelosDaAnalise.Rotas)
                {
                    var nome = Provedores.Nome(ModelosDaAnalise.ProvedorDaRota(rota));
                    // Uma linha por rota: o texto à esquerda e a chave à direita, todas na mesma coluna (antes cada chave ficava sob o seu título,
                    // alinhada pela largura do texto, e as três apareciam desencontradas).
                    var alternar = new ToggleSwitch { IsOn = ModelosDaAnalise.Ligada(a, e, rota), Style = (Style)Application.Current.Resources["PmvniAjusteAlternar"],
                        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                    var linhaRota = new Grid { ColumnSpacing = 16, Margin = new Thickness(16, 0, 16, 8) };
                    linhaRota.ColumnDefinitions.Add(new ColumnDefinition());
                    linhaRota.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    linhaRota.Children.Add(new TextBlock
                    {
                        Text = e.Nome + " na rota " + nome + (e.LigadaPorRota.GetValueOrDefault(rota, true) ? " (padrão do kit: ligada)" : " (padrão do kit: desligada)"),
                        Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"],
                    });
                    Grid.SetColumn(alternar, 1);
                    linhaRota.Children.Add(alternar);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(alternar, e.Nome + " na rota " + nome);
                    var etapa = e;
                    alternar.Toggled += (_, _) =>
                    {
                        if (montandoModelosAnalise) return;
                        ModelosDaAnalise.Ligar(AjusteDosModelos, etapa, rota, alternar.IsOn);
                        GravarAjusteDosModelos(etapa.Nome + (alternar.IsOn ? " ligada" : " desligada") + " na rota " + nome + " neste computador.");
                    };
                    p.PainelAuditoriaCruzada.Children.Add(linhaRota);
                }

            var avisos = ModelosDaAnalise.AvisosDaAba(kit, a, etapas, c => ListaDaRotaAgora(c, kit));
            p.AvisoModelosAnalise.Message = string.Join(Environment.NewLine, avisos.Select(x => x.Texto));
            p.AvisoModelosAnalise.IsOpen = avisos.Count > 0;

            var mestre = KitMestreDesteComputador();
            p.BotaoPublicarModelos.Visibility = mestre is null ? Visibility.Collapsed : Visibility.Visible;
            if (p.TextoModelosAnalise.Text.Length == 0)
                p.TextoModelosAnalise.Text = a.Vazio ? "Tudo no padrão do kit." : "Há ajustes deste computador (as células marcadas com •).";
        }
        finally { montandoModelosAnalise = false; }
    }

    string? KitMestreDesteComputador()
    {
        try { return ComponentesDoKit.KitMestre(OrigensDoKitDoComputador()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    Grid GradeDosModelos(TabelaModelosKit kit, IReadOnlyList<EtapaModelos> etapas, AjusteModelos a, ColunaModelos rota)
    {
        var grade = new Grid { ColumnSpacing = 16, RowSpacing = 12, Margin = new Thickness(0, 16, 0, 0) };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        foreach (var largura in new[] { 320, 200 })
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(largura) });
        // Coluna vazia no fim: com o ColumnSpacing, deixa 16 px entre a caixa de Raciocínio e a borda direita do cartão da etapa (antes a caixa
        // encostava na borda e ficava cortada).
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        void Adicionar(FrameworkElement controle, int linha, int coluna)
        {
            Grid.SetRow(controle, linha);
            Grid.SetColumn(controle, coluna);
            grade.Children.Add(controle);
        }
        grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var cab = new[] { "Etapas do processamento", "Modelo", "Raciocínio" };
        for (var i = 0; i < cab.Length; i++) Adicionar(new TextBlock { Text = cab[i], Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] }, 0, i);
        var lista = ListaDaRotaAgora(rota, kit) ?? new ListaDaRota([], false, "do provedor");
        var estiloCartao = (Style)Application.Current.Resources["PmvniAjusteGrupo"];
        // Fluxo linear do kit 4.0: só as etapas com sessão de IA têm modelo e raciocínio para escolher. As de programa (preparo, conferência,
        // montagem e revisão) não ocupam mais uma linha cada uma com caixas desligadas: ficam numa nota só, no fim da grade.
        var linha = 0;
        for (var n = 0; n < etapas.Count; n++)
        {
            var etapa = etapas[n];
            if (etapa.Programa) continue;
            var i = linha++;
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var fundo = new Border { Style = estiloCartao, MinHeight = 64, IsHitTestVisible = false };
            Grid.SetColumnSpan(fundo, 4);
            Adicionar(fundo, i + 1, 0);
            var descricao = etapa.Id switch
            {
                "analyze" => "Três sessões em paralelo: seções 1–9, 10–18 e 19–28",
                "technical_audit" when rota.Rota == "codex" => "Três sessões consecutivas conferem os achados por bloco de seções",
                "autocorrecao" => "Corrige os apontamentos que o validador mantém após os consertos automáticos",
                "technical_audit" => "Uma sessão independente confere e corrige os achados", _ => ""
            };
            var bloco = new Grid { ColumnSpacing = 10, Margin = new Thickness(12, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            bloco.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bloco.ColumnDefinitions.Add(new ColumnDefinition());
            var selo = new Border { Style = estiloCartao, Padding = new Thickness(5, 2, 5, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = $"{n + 1:00}" } };
            bloco.Children.Add(selo);
            var textos = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            textos.Children.Add(new TextBlock { Text = etapa.Nome, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
            textos.Children.Add(new TextBlock { Text = descricao, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] });
            Grid.SetColumn(textos, 1);
            bloco.Children.Add(textos);
            Adicionar(bloco, i + 1, 0);
            if (kit.Celula(rota, etapa.Id) is { } padrao)
            {
                var celula = CelulaDaGrade(kit, rota, etapa, padrao, a, lista);
                Grid.SetColumnSpan(celula, 2);
                Adicionar(celula, i + 1, 1);
            }
            else
            {
                Adicionar(new Button { Content = "Indisponível", IsEnabled = false }, i + 1, 1);
                Adicionar(new Button { Content = "Não se aplica", IsEnabled = false }, i + 1, 2);
            }
        }
        var dePrograma = etapas.Where(e => e.Programa).Select(e => e.Nome).ToList();
        if (dePrograma.Count > 0)
        {
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            dePrograma.Add("consertos automáticos");
            var nomes = dePrograma.Count == 1 ? dePrograma[0] : string.Join(", ", dePrograma.Take(dePrograma.Count - 1)) + " e " + dePrograma[^1];
            var nota = new TextBlock
            {
                Text = (dePrograma.Count == 1 ? "Feita pelo programa, sem IA e sem modelo: " : "Feitas pelo programa, sem IA e sem modelo: ") + nomes + ".",
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"],
            };
            Grid.SetColumnSpan(nota, 4);
            Adicionar(nota, linha + 1, 0);
        }
        return grade;
    }
    FrameworkElement CelulaDaGrade(TabelaModelosKit kit, ColunaModelos col, EtapaModelos etapa, CelulaModelos padrao, AjusteModelos a, ListaDaRota lista)
    {
        var atual = a.Celulas.GetValueOrDefault((col.Rota, col.Perfil, etapa.Id));
        var vale = atual ?? padrao;
        var painel = new Grid { ColumnSpacing = 16, RowSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        painel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
        painel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        painel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        painel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var caixaModelo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        var caixaNivel = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixaModelo, "Modelo de " + etapa.Nome + " em " + col.Titulo);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixaNivel, "Nível de " + etapa.Nome + " em " + col.Titulo);
        foreach (var (id, texto) in ModelosDaAnalise.OpcoesDeModelo(lista, padrao, atual))
        {
            var nome = lista.Modelos.FirstOrDefault(m => m.Id == id)?.Nome ?? id;
            var item = new ComboBoxItem { Content = nome, Tag = id };
            ToolTipService.SetToolTip(item, texto);
            caixaModelo.Items.Add(item);
            if (id == vale.Modelo) ToolTipService.SetToolTip(caixaModelo, texto);
        }
        caixaModelo.SelectedItem = caixaModelo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == vale.Modelo);
        void Niveis(string modelo, string nivel)
        {
            caixaNivel.Items.Clear();
            foreach (var n in ModelosDaAnalise.NiveisDoModelo(kit, lista, modelo))
            {
                var nome = ModelosDaAnalise.NomeDoNivel(n);
                var dica = nome + (n == padrao.Nivel && modelo == padrao.Modelo ? " (padrão)" : "");
                var item = new ComboBoxItem { Content = nome, Tag = n };
                ToolTipService.SetToolTip(item, dica);
                caixaNivel.Items.Add(item);
                if (n == nivel) ToolTipService.SetToolTip(caixaNivel, dica);
            }
            caixaNivel.SelectedItem = caixaNivel.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == nivel) ?? caixaNivel.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        Niveis(vale.Modelo, vale.Nivel);
        void Aplicar()
        {
            if (montandoModelosAnalise || caixaModelo.SelectedItem is not ComboBoxItem { Tag: string modelo } || caixaNivel.SelectedItem is not ComboBoxItem { Tag: string nivel }) return;
            var aceitos = ModelosDaAnalise.NiveisDoModelo(kit, lista, modelo);
            if (!aceitos.Contains(nivel)) nivel = aceitos.Contains(padrao.Nivel) ? padrao.Nivel : aceitos[^1];
            var recusa = ModelosDaAnalise.Ajustar(AjusteDosModelos, kit, col, etapa, modelo, nivel, lista);
            if (recusa is not null)
            {
                PaginaDeAjustes.TextoModelosAnalise.Text = recusa;
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, MostrarModelosDaAnalise);
                return;
            }
            GravarAjusteDosModelos(etapa.Nome + " em " + col.Titulo + ": " + modelo + ", nível " + ModelosDaAnalise.NomeDoNivel(nivel).ToLowerInvariant() + " (neste computador).");
        }
        caixaModelo.SelectionChanged += (_, _) => Aplicar();
        caixaNivel.SelectionChanged += (_, _) => Aplicar();
        painel.Children.Add(caixaModelo);
        Grid.SetColumn(caixaNivel, 1);
        painel.Children.Add(caixaNivel);
        // Só quando o ajuste muda alguma coisa: um ajuste igual ao padrão do kit não precisa de legenda.
        if (atual is not null && (atual.Modelo != padrao.Modelo || atual.Nivel != padrao.Nivel))
        {
            var legenda = new TextBlock { Text = "• ajuste deste computador (padrão: " + padrao.Modelo + ", " + ModelosDaAnalise.NomeDoNivel(padrao.Nivel).ToLowerInvariant() + ")",
                Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"], TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(legenda, 1);
            Grid.SetColumnSpan(legenda, 2);
            painel.Children.Add(legenda);
        }
        return painel;
    }

    async Task PublicarModelosAsync()
    {
        var p = PaginaDeAjustes;
        if (TabelaDoKitInstalado().Kit is not { } kit) return;
        var mestre = KitMestreDesteComputador();
        if (!await Perguntas.Atual.ConfirmarAsync("Publicar como padrão do kit?",
                "A tabela desta página (modelos, níveis e regras) passa a ser o padrão do kit mestre" + (mestre is null ? "" : " (" + mestre + ")")
                + " e chega aos outros computadores pela atualização automática. A auditoria cruzada continua sendo ajuste de cada computador.",
                "Publicar", "Cancelar", destrutiva: false)) return;
        p.BotaoPublicarModelos.IsEnabled = false;
        try
        {
            var pasta = PastaScriptsDoKitDoRoteiro ?? await InstalacaoMotorPdf.ExtrairAsync();
            // Tudo o que é da janela é lido aqui, na thread dela; o Task.Run só mexe em arquivos e no Python.
            var analises = ProcessosComAnaliseEmAndamento();
            var (_, etapas) = TabelaDoKitInstalado();
            var listas = kit.Colunas.ToDictionary(c => (c.Rota, c.Perfil), c => ListaDaRotaAgora(c, kit));
            var ajuste = AjusteDosModelos;
            var conferir = ConferenciaDaTabelaNoKit(mestre);
            var lancador = LancadorDoKit ?? AbrirPeloWindows;
            var r = await Task.Run(() => ModelosDaAnalise.Publicar(mestre, ajuste, etapas, c => listas.GetValueOrDefault((c.Rota, c.Perfil)), analises, pasta, conferir, lancador));
            p.TextoModelosAnalise.Text = r.Texto;
            // Correção 1 (M4): o ajuste publicado virou o padrão do kit mestre; guardado aqui, ele esconderia um padrão novo que viesse depois.
            if (r.Publicada && (ajuste.Celulas.Count > 0 || ajuste.SobePorObjecao is not null || ajuste.TrocaDescePorCota)
                && await Perguntas.Atual.ConfirmarAsync("Limpar o ajuste deste computador?",
                    "A tabela publicada passa a ser o padrão do kit. Limpar os modelos, os níveis e as regras deste computador deixa valer o padrão (até a atualização "
                    + "automática instalar o kit novo, este computador usa o padrão instalado). O liga/desliga da auditoria cruzada fica.", "Limpar", "Manter", destrutiva: false))
            {
                ajusteModelos = ModelosDaAnalise.SemOPublicado(ajuste);
                GravarAjusteDosModelos(r.Texto + " O ajuste deste computador foi limpo.");
            }
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("modelos.analise.publicar", e);
            p.TextoModelosAnalise.Text = "Não foi possível publicar a tabela no kit. " + e.Message;
        }
        finally { p.BotaoPublicarModelos.IsEnabled = true; }
    }

    /// <summary>A conferência da tabela pelo próprio kit (<c>modelos_por_rota.conferir</c>), com o Python deste computador: a do kit mestre, se ele já tem o
    /// script; senão a do plugin analise instalado. Sem Python, recusa (nada é publicado sem conferir).</summary>
    Func<string, string?> ConferenciaDaTabelaNoKit(string? mestre)
    {
        var pecas = pecasAgentes;
        var scripts = mestre is not null && File.Exists(Path.Combine(ModelosDaAnalise.ScriptsNoKit(mestre), "modelos_por_rota.py"))
            ? ModelosDaAnalise.ScriptsNoKit(mestre)
            : pecas.ModelosPorRota is { } t ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(t)!, "..", "scripts")) : null;
        return arquivo => pecas.Python is null ? "o Python não foi encontrado neste computador"
            : scripts is null ? "a conferência da tabela do kit não foi achada"
            : ModelosDaAnalise.ConferirComOKit(pecas.Python, pecas.PythonPeloLauncher ? ["-3"] : [], scripts, arquivo);
    }

    /// <summary>Correção 1 (M7): as etapas Gemini da tabela efetiva de um perfil (o que a análise vai pedir), para o "Testar" do cartão Gemini; null = a matriz
    /// padrão (kit sem a tabela).</summary>
    IReadOnlyList<EtapaGemini>? EtapasGeminiEfetivas(PerfilGemini perfil)
    {
        var (kit, etapas) = TabelaDoKitInstalado();
        if (kit is null) return null;
        var t = ModelosDaAnalise.Efetiva(kit, AjusteDosModelos, etapas, c => ListaDaRotaAgora(c, kit));
        return ModelosDaAnalise.EtapasGemini(t, kit, MatrizGemini.Codigo(perfil));
    }

    // ---------- antes de cada análise ----------

    /// <summary>A tabela efetiva para a partida da análise pela rota do provedor. Revisão final I4: não espera o provedor (o Gemini podia levar até 60 s
    /// dentro da fila de comandos): monta a tabela pelas listas guardadas e atualiza a lista do provedor em segundo plano (<see cref="ModelosDaAnalise.ParaPartida"/>).
    /// Mostra no Novo relatório os avisos (modelo que a última lista já sabe inexistente: a etapa usa o padrão do kit). null = kit sem a tabela (vale o que o kit define).</summary>
    TabelaDeModelos? TabelaParaAnalise(Provedor p)
    {
        var (kit, etapas) = TabelaDoKitInstalado();
        if (kit is null) return null;
        var rota = ModelosDaAnalise.RotaDoProvedor(p);
        Func<Task>? atualizar = null;
        if (rota == "gemini") atualizar = AtualizarListaGeminiAsync;
        else if (FonteDaRota(rota) is var fonte && FonteDisponivel(fonte))
            atualizar = () => CatalogoDeModelos.AtualizarAsync(pecasAgentes, [fonte], CancellationToken.None);
        var efetiva = ModelosDaAnalise.ParaPartida(kit, AjusteDosModelos, etapas, c => ListaDaRotaAgora(c, kit), rota, atualizar,
            e => RegistroErros.Gravar("modelos.analise.lista", e));
        if (efetiva.Avisos.Count > 0)
            MostrarMensagem(InfoBarSeverity.Warning, "Modelos da análise", string.Join(Environment.NewLine, efetiva.Avisos) + " Isso fica registrado no histórico da análise.");
        return efetiva;
    }
}
