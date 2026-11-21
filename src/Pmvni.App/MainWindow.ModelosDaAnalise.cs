using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Tarefa 19: Ajustes › Inteligência artificial › Modelos da análise. A grade (linhas = etapas do <c>etapas.json</c> do kit; colunas = rotas e perfis
/// da tabela do kit; cada célula = modelo + nível), as regras, a auditoria cruzada por rota, "Restaurar padrão do kit" e, no computador do kit mestre,
/// "Publicar como padrão do kit". A lógica fica em <see cref="ModelosDaAnalise"/>; aqui só a tela. O ajuste é gravado direto em
/// <c>%LOCALAPPDATA%\EditorRelatoriosPMVNI</c> (<see cref="PastaDados.Raiz"/>; o Editor não é MSIX: não há redirecionamento de pasta). O Recriar PC
/// guarda o arquivo por um passo próprio do <c>recriar.json</c>.</summary>
public sealed partial class MainWindow
{
    AjusteModelos? ajusteModelos;
    bool montandoModelosAnalise;

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
    string OrigemDasListas(TabelaModelosKit kit)
    {
        var partes = new List<string>();
        foreach (var col in kit.Colunas)
        {
            var l = ListaDaRotaAgora(col, kit);
            var quando = col.Rota == "gemini" && listasGemini.TryGetValue(ModelosDaAnalise.GrupoGemini(col.Perfil), out var g) ? ", lida às " + g.Em.ToString("HH:mm", CultureInfo.InvariantCulture) : "";
            partes.Add(col.Titulo + ": lista " + (l?.Fonte ?? "não lida") + quando);
        }
        return "Listas de modelos — " + string.Join("; ", partes) + "." + (falhaListaGemini is { } f ? " Gemini: não foi possível ler a lista agora (" + f + "); vale a anterior." : "");
    }

    void IniciarModelosDaAnalise()
    {
        var p = PaginaDeAjustes;
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
            if (tag != CategoriasAjustes.Ia) return;
            await AtualizarListaGeminiAsync();
            MostrarModelosDaAnalise();
        };
        p.BotaoAtualizarModelosIa.Click += async (_, _) => { await AtualizarListaGeminiAsync(); MostrarModelosDaAnalise(); };
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
            p.PainelModelosAnalise.Children.Add(GradeDosModelos(kit, etapas, a));
            p.PainelModelosAnalise.Children.Add(new TextBlock { Text = OrigemDasListas(kit), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
                Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"], MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Left });

            p.AlternarSobeObjecao.IsOn = a.SobePorObjecao ?? kit.SobePorObjecao;
            var desce = a.TrocaDescePorCota ? a.DescePorCota : kit.DescePorCota;
            var i = Array.FindIndex(OpcoesCotaBaixa, o => Igual(o.Valor, desce));
            p.CaixaCotaBaixa.SelectedIndex = i >= 0 ? i : -1;
            var sobem = kit.RotasQueSobem.Select(r => Provedores.Nome(ModelosDaAnalise.ProvedorDaRota(r))).ToList();
            p.TextoRegraObjecao.Text = "Depois de uma falha técnica registrada (validador reprovado, objeção da auditoria ou da revisão final), a nova tentativa da etapa sobe um nível, até alto. "
                + "Cota e problemas do computador nunca sobem. Vale só " + (sobem.Count == 1 ? "na rota " + sobem[0] : "nas rotas " + string.Join(", ", sobem))
                + (sobem.Count < ModelosDaAnalise.Rotas.Length ? "; nas outras, o kit não muda o nível por objeção." : ".");
            p.TextoRegrasDasRotas.Text = string.Join(" ", kit.NotasDasRotas.Values)
                + (kit.NotasDasRotas.Count > 0 ? " " : "") + "Padrão do kit: objeção técnica " + (kit.SobePorObjecao ? "sobe" : "não sobe") + " um nível; cota baixa "
                + (kit.DescePorCota is { } d ? "com " + (d * 100).ToString("0", CultureInfo.InvariantCulture) + "% ou menos restantes" : "não desce o nível") + ".";

            foreach (var e in etapas.Where(e => e.Opcional))
                foreach (var rota in ModelosDaAnalise.Rotas)
                {
                    var nome = Provedores.Nome(ModelosDaAnalise.ProvedorDaRota(rota));
                    var alternar = new ToggleSwitch { IsOn = ModelosDaAnalise.Ligada(a, e, rota), Style = (Style)Application.Current.Resources["PmvniAjusteAlternar"], Margin = new Thickness(16, 0, 16, 8) };
                    alternar.Header = e.Nome + " na rota " + nome + (e.LigadaPorRota.GetValueOrDefault(rota, true) ? " (padrão do kit: ligada)" : " (padrão do kit: desligada)");
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(alternar, e.Nome + " na rota " + nome);
                    var etapa = e;
                    alternar.Toggled += (_, _) =>
                    {
                        if (montandoModelosAnalise) return;
                        ModelosDaAnalise.Ligar(AjusteDosModelos, etapa, rota, alternar.IsOn);
                        GravarAjusteDosModelos(etapa.Nome + (alternar.IsOn ? " ligada" : " desligada") + " na rota " + nome + " neste computador.");
                    };
                    p.PainelAuditoriaCruzada.Children.Add(alternar);
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

    /// <summary>A grade: a primeira coluna com as etapas; uma coluna por rota/perfil, com a caixa do modelo e a do nível em cada célula.</summary>
    Grid GradeDosModelos(TabelaModelosKit kit, IReadOnlyList<EtapaModelos> etapas, AjusteModelos a)
    {
        var grade = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        foreach (var _ in kit.Colunas) grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var ajuda = (Style)Application.Current.Resources["PmvniAjusteAjuda"];
        var titulo = (Style)Application.Current.Resources["PmvniAjusteTitulo"];
        for (var c = 0; c < kit.Colunas.Count; c++)
        {
            var cab = new TextBlock { Text = kit.Colunas[c].Titulo, Style = titulo, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(cab, c + 1);
            grade.Children.Add(cab);
        }
        var listas = kit.Colunas.ToDictionary(c => c, c => ListaDaRotaAgora(c, kit));
        for (var l = 0; l < etapas.Count; l++)
        {
            var etapa = etapas[l];
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var nome = new TextBlock { Text = etapa.Nome, Style = titulo, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(nome, l + 1);
            grade.Children.Add(nome);
            for (var c = 0; c < kit.Colunas.Count; c++)
            {
                var col = kit.Colunas[c];
                FrameworkElement celula;
                if (etapa.Programa) celula = new TextBlock { Text = "programa (sem IA)", Style = ajuda, VerticalAlignment = VerticalAlignment.Center };
                else if (kit.Celula(col, etapa.Id) is not { } padrao) celula = new TextBlock { Text = "—", Style = ajuda };
                else celula = CelulaDaGrade(kit, col, etapa, padrao, a, listas[col] ?? new ListaDaRota([], false, "do provedor"));
                Grid.SetRow(celula, l + 1);
                Grid.SetColumn(celula, c + 1);
                grade.Children.Add(celula);
            }
        }
        return grade;
    }

    FrameworkElement CelulaDaGrade(TabelaModelosKit kit, ColunaModelos col, EtapaModelos etapa, CelulaModelos padrao, AjusteModelos a, ListaDaRota lista)
    {
        var atual = a.Celulas.GetValueOrDefault((col.Rota, col.Perfil, etapa.Id));
        var vale = atual ?? padrao;
        var painel = new StackPanel { Spacing = 4 };
        var caixaModelo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var caixaNivel = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixaModelo, "Modelo de " + etapa.Nome + " em " + col.Titulo);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caixaNivel, "Nível de " + etapa.Nome + " em " + col.Titulo);
        foreach (var (id, texto) in ModelosDaAnalise.OpcoesDeModelo(lista, padrao, atual)) caixaModelo.Items.Add(new ComboBoxItem { Content = texto, Tag = id });
        caixaModelo.SelectedItem = caixaModelo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == vale.Modelo);
        void Niveis(string modelo, string nivel)
        {
            caixaNivel.Items.Clear();
            foreach (var n in ModelosDaAnalise.NiveisDoModelo(kit, lista, modelo))
                caixaNivel.Items.Add(new ComboBoxItem { Content = ModelosDaAnalise.NomeDoNivel(n) + (n == padrao.Nivel && modelo == padrao.Modelo ? " (padrão)" : ""), Tag = n });
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
        painel.Children.Add(caixaNivel);
        if (atual is not null)
            painel.Children.Add(new TextBlock { Text = "• ajuste deste computador (padrão: " + padrao.Modelo + ", " + ModelosDaAnalise.NomeDoNivel(padrao.Nivel).ToLowerInvariant() + ")",
                Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"], TextWrapping = TextWrapping.Wrap });
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

    /// <summary>A tabela efetiva para a partida da análise pela rota do provedor: atualiza a lista do provedor (até 15 s; sem resposta, vale a anterior), monta a
    /// tabela e mostra no Novo relatório os avisos (modelo que sumiu: a etapa usa o padrão do kit). null = kit sem a tabela (vale o que o kit define).</summary>
    async Task<TabelaDeModelos?> TabelaParaAnaliseAsync(Provedor p)
    {
        var (kit, etapas) = TabelaDoKitInstalado();
        if (kit is null) return null;
        var rota = ModelosDaAnalise.RotaDoProvedor(p);
        if (rota == "gemini")
        {
            try { await AtualizarListaGeminiAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
            catch (TimeoutException) { }
        }
        else
        {
            var fonte = FonteDaRota(rota);
            if (FonteDisponivel(fonte))
            {
                try { await CatalogoDeModelos.AtualizarAsync(pecasAgentes, [fonte], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (TimeoutException) { }
                catch (Exception e) when (e is InvalidOperationException or IOException or OperationCanceledException) { RegistroErros.Gravar("modelos.analise.lista", e); }
            }
        }
        var efetiva = ModelosDaAnalise.Efetiva(kit, AjusteDosModelos, etapas, c => ListaDaRotaAgora(c, kit)).ParaExecucao(rota);
        if (efetiva.Avisos.Count > 0)
            MostrarMensagem(InfoBarSeverity.Warning, "Modelos da análise", string.Join(Environment.NewLine, efetiva.Avisos) + " Isso fica registrado no histórico da análise.");
        return efetiva;
    }
}
