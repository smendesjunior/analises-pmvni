using System.Globalization;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Legislacao;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Ajustes, tema, Recentes e Comparar com o original (Tarefa 11; desenho §5.5, §8.5 e §9): a página Ajustes (tema, intervalo do
/// salvamento automático, pasta de recuperação, versões), o menu Exibir › Tema, o menu Arquivo › Recentes (só o que ainda existe) e a
/// janela "Comparar com o original". As escolhas ficam em <c>ajustes.json</c> (<see cref="Ajustes"/>); nada disso entra no pacote.</summary>
public sealed partial class MainWindow
{
    bool atualizandoAjustes;
    bool haRecentes;

    // ---------- acessores do roteiro ----------
    internal string TemaDosAjustes => ajustes.Tema;
    internal ElementTheme TemaAtual => Raiz.ActualTheme;
    internal IReadOnlyList<string> RecentesNosAjustes => ajustes.Recentes.ToList();
    internal PaginaAjustes AjustesPagina => PaginaDeAjustes;
    internal bool PaginaAjustesVisivel => PaginaDeAjustes.Visibility == Visibility.Visible;
    internal int IntervaloAutosaveAtual => ajustes.IntervaloAutosaveS;
    /// <summary>O tema escolhido na página Ajustes (<c>sistema</c>, <c>claro</c> ou <c>escuro</c>).</summary>
    internal string? TemaMarcadoNaPagina => (PaginaDeAjustes.CaixaTema.SelectedItem as ComboBoxItem)?.Tag as string;
    /// <summary>Os ajustes em memória (o roteiro confere as opções novas e "Restaurar os ajustes").</summary>
    internal Ajustes AjustesAtuais => ajustes;

    /// <summary>As caixas de escolha da página: cada opção tem o texto e o valor guardado em ajustes.json.</summary>
    static readonly (string Valor, string Texto)[] TextosAbrirEm =
        [("ultimo", "Último usado"), ("Relatorios", "Relatórios"), ("Despachos", "Despachos"), ("Processos", "Processos"), ("Legislacao", "Legislação"), ("Imoveis", "Imóveis"), ("Acompanhamento", "Acompanhamento")];
    static readonly (string Valor, string Texto)[] TextosRotulos = [("automatico", "Automático"), ("sempre", "Sempre"), ("icones", "Só ícones")];
    static readonly (string Valor, string Texto)[] TextosAvisos = [("indicador", "Indicador no rodapé"), ("faixas", "Faixas no topo")];
    static readonly (string Valor, string Texto)[] TextosZoom = [("largura", "Ajustar largura"), ("pagina", "Página inteira"), ("100", "100 %")];
    static readonly (string Valor, string Texto)[] TextosOrdemProcessos = [("numero", "Número"), ("data", "Data"), ("requerente", "Requerente")];
    static readonly (string Valor, string Texto)[] TextosAgrupar = [("data", "Por data"), ("processo", "Por processo")];
    static readonly (string Valor, string Texto)[] TextosOrdemDespachos = [("recentes", "Mais recentes primeiro"), ("antigos", "Mais antigos primeiro")];
    static readonly (string Valor, string Texto)[] TextosEscopo = [("lei", "Nesta lei"), ("todas", "Em todas as leis")];
    static readonly (string Valor, string Texto)[] TextosArquivosExibicao = [("lista", "Lista"), ("miniaturas", "Miniaturas")];
    static readonly (string Valor, string Texto)[] TextosArquivosAgrupar = [("etapa", "Por etapa"), ("tipo", "Por tipo"), ("data", "Por data"), ("nenhum", "Sem grupos")];
    static readonly (string Valor, string Texto)[] TextosArquivosOrdenar = [("nome", "Nome"), ("data", "Data"), ("tipo", "Tipo")];

    void IniciarAjustes()
    {
        var p = PaginaDeAjustes;
        static void Preencher(ComboBox c, IEnumerable<(object Valor, string Texto)> itens)
        {
            foreach (var (valor, texto) in itens) c.Items.Add(new ComboBoxItem { Content = texto, Tag = valor });
        }
        Preencher(p.CaixaIntervalo, Ajustes.Intervalos.Select(s => ((object)s, s + " segundos")));
        Preencher(p.CaixaTema, Ajustes.Temas.Select(t => ((object)t, TextosTema.De(t))));
        Preencher(p.CaixaAbrirEm, TextosAbrirEm.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaRecentes, Ajustes.OpcoesRecentes.Select(n => ((object)n, n + " arquivos")));
        Preencher(p.CaixaRotulosFaixa, TextosRotulos.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaAvisosLayout, TextosAvisos.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaZoomInicial, TextosZoom.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaListaSecoes, new (object Valor, string Texto)[] { ("compacta", "Compacta"), ("confortavel", "Confortável") });
        Preencher(p.CaixaRecuperacao, new[] { 7, 30, 90 }.Select(d => ((object)d, d + " dias")));
        Preencher(p.CaixaOrdemProcessos, TextosOrdemProcessos.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaAgruparDespachos, TextosAgrupar.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaOrdemDespachos, TextosOrdemDespachos.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaEscopo, TextosEscopo.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaArquivosExibicao, TextosArquivosExibicao.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaArquivosAgrupar, TextosArquivosAgrupar.Select(x => ((object)x.Valor, x.Texto)));
        Preencher(p.CaixaArquivosOrdenar, TextosArquivosOrdenar.Select(x => ((object)x.Valor, x.Texto)));

        // Cada escolha grava na hora (barra "Alterações salvas automaticamente") e vale já onde é lida.
        void AoEscolher(ComboBox c, Action<object> aplicar) => c.SelectionChanged += (_, _) =>
        {
            if (atualizandoAjustes || c.SelectedItem is not ComboBoxItem { Tag: { } valor }) return;
            aplicar(valor);
            SalvarAjustes();
        };
        void AoAlternar(ToggleSwitch t, Action<bool> aplicar) => t.Toggled += (_, _) =>
        {
            if (atualizandoAjustes) return;
            aplicar(t.IsOn);
            SalvarAjustes();
        };
        p.CaixaIntervalo.SelectionChanged += (_, _) =>
        {
            if (atualizandoAjustes || p.CaixaIntervalo.SelectedItem is not ComboBoxItem { Tag: int s } || s == ajustes.IntervaloAutosaveS) return;
            DefinirIntervaloAutosave(s);
            ReiniciarAutosave();
        };
        p.CaixaTema.SelectionChanged += (_, _) =>
        {
            if (!atualizandoAjustes && p.CaixaTema.SelectedItem is ComboBoxItem { Tag: string tema } && tema != ajustes.Tema) EscolherTema(tema);
        };
        AoEscolher(p.CaixaAbrirEm, v => ajustes.AbrirEm = (string)v);
        AoAlternar(p.AlternarJanela, sim => { ajustes.LembrarJanela = sim; if (!sim) ajustes.Janela = null; });
        AoEscolher(p.CaixaRecentes, v => { ajustes.RecentesMax = (int)v; AtualizarRecentes(); });
        p.BotaoLimparRecentes.Click += (_, _) =>
        {
            ajustes.LimparRecentes();
            SalvarAjustes();
            AtualizarRecentes();
        };
        AoEscolher(p.CaixaRotulosFaixa, v => { ajustes.FaixaRotulos = (string)v; AplicarLargura(Raiz.ActualWidth); });
        AoEscolher(p.CaixaAvisosLayout, v =>
        {
            ajustes.AvisosLayout = (string)v;
            if (sessao?.Atual is { } r) MostrarRecomposicao(r);
        });
        AoEscolher(p.CaixaZoomInicial, v => ajustes.ZoomInicial = (string)v);
        AoEscolher(p.CaixaListaSecoes, v => { ajustes.ListaSecoes = (string)v; ItemNavegacao.Compacta = ajustes.ListaSecoes == "compacta"; MontarSecoes(); });
        // Marcas de revisão: o mesmo que Revisão › Marcas de revisão (a faixa e a página andam juntas).
        p.AlternarMarcas.Toggled += async (_, _) =>
        {
            if (atualizandoAjustes || p.AlternarMarcas.IsOn == ajustes.MarcasRevisao) return;
            await TentarAsync(AlternarMarcasAsync);
        };
        AoAlternar(p.AlternarPaineis, sim =>
        {
            ajustes.LembrarPaineis = sim;
            ajustes.PainelLateral = R.PainelNavegacao.Visibility == Visibility.Visible;
            ajustes.PainelPropriedades = R.PainelPropriedades.Visibility == Visibility.Visible;
        });
        AoEscolher(p.CaixaRecuperacao, v => ajustes.RecuperacaoDias = (int)v);
        p.BotaoAbrirRecuperacao.Click += (_, _) => AbrirPastaDosAjustes(Path.Combine(PastaDados.Raiz, "recuperacao"));
        p.BotaoAbrirPastaProcessos.Click += (_, _) => AbrirPastaDosAjustes(ajustes.PastaProcessosEfetiva);
        p.BotaoAbrirPastaKit.Click += (_, _) => AbrirPastaDosAjustes(ajustes.PastaKitEfetiva);
        AoEscolher(p.CaixaOrdemProcessos, v => ajustes.ProcessosOrdem = (string)v);
        AoEscolher(p.CaixaAgruparDespachos, v => ajustes.DespachosAgrupar = (string)v);
        AoEscolher(p.CaixaOrdemDespachos, v => ajustes.DespachosOrdem = (string)v);
        // Arquivos do processo (2026-11-23): a mesma escolha do Exibir da página do processo, que vale na hora e fica lembrada.
        AoEscolher(p.CaixaArquivosExibicao, v => { ajustes.ArquivosExibicao = (string)v; SincronizarPastas(); });
        AoEscolher(p.CaixaArquivosAgrupar, v => { ajustes.ArquivosAgrupar = (string)v; SincronizarPastas(); });
        AoEscolher(p.CaixaArquivosOrdenar, v => { ajustes.ArquivosOrdenar = (string)v; SincronizarPastas(); });
        p.BotaoTestarSoffice.Click += (_, _) => TestarSoffice();
        p.BotaoEscolherSoffice.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(EscolherSofficeAsync));
        p.BotaoSofficeAutomatico.Click += (_, _) => DefinirSoffice(null);
        p.BotaoRedefinirPerfil.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(RedefinirPerfilSofficeAsync));
        AoAlternar(p.AlternarAbrirPdf, sim => ajustes.DespachoAbrirPdf = sim);
        AoAlternar(p.AlternarDetectarAcompanhamento, sim => ajustes.AcompanhamentoDetectar = sim);
        AoEscolher(p.CaixaEscopo, v => ajustes.LegislacaoEscopo = (string)v);
        IniciarModelosIa();
        IniciarModelosDaAnalise();
        IniciarSites();
        // Grava ao digitar também (antes só ao sair da caixa, e o texto se perdia se a janela fechasse com o cursor nela).
        p.CaixaPedidoPadrao.TextChanged += (_, _) =>
        {
            if (atualizandoAjustes || p.CaixaPedidoPadrao.Text == ajustes.PedidoPadrao) return;
            ajustes.PedidoPadrao = p.CaixaPedidoPadrao.Text;
            SalvarAjustes();
        };
        p.CaixaPedidoPadrao.LostFocus += (_, _) =>
        {
            if (atualizandoAjustes || p.CaixaPedidoPadrao.Text == ajustes.PedidoPadrao) return;
            ajustes.PedidoPadrao = p.CaixaPedidoPadrao.Text;
            SalvarAjustes();
        };
        // Profissional responsável (2026-11-17): grava a cada letra e já vale para o próximo despacho e relatório.
        foreach (var (caixa, definir) in new (TextBox, Action<string?>)[]
        {
            (p.CaixaProfissionalNome, v => ajustes.ProfissionalNome = v),
            (p.CaixaProfissionalProfissao, v => ajustes.ProfissionalProfissao = v),
            (p.CaixaProfissionalRegistro, v => ajustes.ProfissionalRegistro = v),
            (p.CaixaProfissionalMatricula, v => ajustes.ProfissionalMatricula = v),
        })
            caixa.TextChanged += (_, _) =>
            {
                if (atualizandoAjustes) return;
                definir(string.IsNullOrWhiteSpace(caixa.Text) ? null : caixa.Text.Trim());
                Pmvni.Composicao.Profissional.Atual = ajustes.Profissional;
                SalvarAjustes();
                p.TextoProfissionalSalvo.Text = "";
            };
        // "Salvar dados" (pedido do usuário): grava na hora e confirma (os campos também gravam sozinhos ao digitar).
        p.BotaoSalvarProfissional.Click += (_, _) =>
        {
            string? Valor(TextBox c) => string.IsNullOrWhiteSpace(c.Text) ? null : c.Text.Trim();
            ajustes.ProfissionalNome = Valor(p.CaixaProfissionalNome);
            ajustes.ProfissionalProfissao = Valor(p.CaixaProfissionalProfissao);
            ajustes.ProfissionalRegistro = Valor(p.CaixaProfissionalRegistro);
            ajustes.ProfissionalMatricula = Valor(p.CaixaProfissionalMatricula);
            Pmvni.Composicao.Profissional.Atual = ajustes.Profissional;
            SalvarAjustes();
            p.TextoProfissionalSalvo.Text = "Dados salvos. Valem a partir do próximo despacho e relatório.";
        };
        AoEscolher(p.CaixaRotaCodex, valor => ajustes.RotaCodex = (string)valor);
        AoAlternar(p.AlternarConfirmarCancelar, sim => ajustes.ConfirmarCancelar = sim);
        AoAlternar(p.AlternarConfirmarFechar, sim => ajustes.ConfirmarFechar = sim);
        IniciarAtualizacoes();
        p.BotaoCopiarVersoes.Click += (_, _) => { AreaTransferencia.Copiar(p.TextoVersoes.Text); ToolTipService.SetToolTip(p.BotaoCopiarVersoes, "Diagnóstico copiado."); Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(p.BotaoCopiarVersoes)?.RaiseNotificationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.ActionCompleted, Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.ImportantMostRecent, "Diagnóstico copiado.", "copiado"); p.BotaoCopiarVersoes.Content = "Copiado"; };
        p.BotaoVerAtalhos.Click += async (_, _) => await TentarAsync(MostrarAtalhosAsync);
        p.TextoPastaDados.Text = PastaDados.Raiz;
        p.BotaoAbrirPastaDados.Click += (_, _) => AbrirPastaDosAjustes(PastaDados.Raiz);
        p.BotaoRestaurarAjustes.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () => await RestaurarAjustesAsync()));
        p.TextoPerfilSoffice.Text = Path.Combine(PastaDados.Raiz, "lo");

        Faixa.ItensDinamicos = ItensDoMenuDaFaixa;
        Faixa.ItemDinamicoEscolhido += async (menu, valor) =>
        {
            if (menu == ComandosApp.ArquivoRecentes) await AbrirRecenteAsync(valor);
            else if (menu == ComandosApp.ProcessosAno) await DefinirAnoAsync(valor);
            else if (menu == ComandosApp.FigurasAbrirEditor && valor == ValorAdicionarFigura) AbrirRecortePrancha(figuraSelecionada);
            else if (menu == ComandosApp.FigurasAbrirEditor) AbrirEditorFigura(valor);
        };
        foreach (var t in Ajustes.Temas)
        {
            var tema = t;
            manipuladores["exibir.tema." + tema] = () => { EscolherTema(tema); return Task.CompletedTask; };
        }
        manipuladores[ComandosApp.RevisaoComparar] = CompararAsync;
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) AtualizarRecentes(); };
        p.TextoPastaRecuperacao.Text = Path.Combine(PastaDados.Raiz, "recuperacao");
        IniciarSobre();
        SincronizarAjustes();
        AtualizarRecentes();
    }

    /// <summary>Ajustes › Módulos › Arquivos do processo acompanha o Exibir da página do processo (sem gravar de novo).</summary>
    void MostrarArquivosNosAjustes()
    {
        var anterior = atualizandoAjustes;
        atualizandoAjustes = true;
        try
        {
            var p = PaginaDeAjustes;
            foreach (var (caixa, valor) in new[] { (p.CaixaArquivosExibicao, ajustes.ArquivosExibicao), (p.CaixaArquivosAgrupar, ajustes.ArquivosAgrupar), (p.CaixaArquivosOrdenar, ajustes.ArquivosOrdenar) })
                caixa.SelectedItem = caixa.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, valor)) ?? caixa.SelectedItem;
        }
        finally { atualizandoAjustes = anterior; }
    }

    // ---------- tema ----------

    /// <summary>Exibir › Tema e a página Ajustes: guarda a escolha, aplica ao app (as folhas continuam brancas) e marca o item em uso.</summary>
    internal void EscolherTema(string tema)
    {
        if (!Ajustes.Temas.Contains(tema)) throw new ArgumentOutOfRangeException(nameof(tema), tema, "Tema desconhecido.");
        ajustes.Tema = tema;
        SalvarAjustes();
        AplicarTemaDosAjustes();
        SincronizarAjustes();
    }

    /// <summary>Põe a faixa e a página Ajustes de acordo com os ajustes (cada caixa no valor guardado, cada chave ligada ou não).</summary>
    void SincronizarAjustes()
    {
        atualizandoAjustes = true;
        try
        {
            var p = PaginaDeAjustes;
            foreach (var t in Ajustes.Temas) Faixa.MarcarItem("exibir.tema." + t, t == ajustes.Tema);
            static void Escolher(ComboBox c, object valor)
            {
                foreach (var item in c.Items.OfType<ComboBoxItem>())
                    if (Equals(item.Tag, valor)) { c.SelectedItem = item; return; }
            }
            Escolher(p.CaixaRotaCodex, ajustes.RotaCodex);
            Escolher(p.CaixaTema, ajustes.Tema);
            Escolher(p.CaixaIntervalo, ajustes.IntervaloAutosaveS);
            Escolher(p.CaixaAbrirEm, ajustes.AbrirEm);
            Escolher(p.CaixaRecentes, ajustes.RecentesMax);
            Escolher(p.CaixaRotulosFaixa, ajustes.FaixaRotulos);
            Escolher(p.CaixaAvisosLayout, ajustes.AvisosLayout);
            Escolher(p.CaixaZoomInicial, ajustes.ZoomInicial);
            Escolher(p.CaixaListaSecoes, ajustes.ListaSecoes);
            ItemNavegacao.Compacta = ajustes.ListaSecoes == "compacta";
            Escolher(p.CaixaRecuperacao, ajustes.RecuperacaoDias);
            Escolher(p.CaixaOrdemProcessos, ajustes.ProcessosOrdem);
            Escolher(p.CaixaAgruparDespachos, ajustes.DespachosAgrupar);
            Escolher(p.CaixaOrdemDespachos, ajustes.DespachosOrdem);
            Escolher(p.CaixaEscopo, ajustes.LegislacaoEscopo);
            Escolher(p.CaixaArquivosExibicao, ajustes.ArquivosExibicao);
            Escolher(p.CaixaArquivosAgrupar, ajustes.ArquivosAgrupar);
            Escolher(p.CaixaArquivosOrdenar, ajustes.ArquivosOrdenar);
            MostrarModelosIa();
            p.AlternarJanela.IsOn = ajustes.LembrarJanela;
            p.AlternarMarcas.IsOn = ajustes.MarcasRevisao;
            p.AlternarPaineis.IsOn = ajustes.LembrarPaineis;
            p.AlternarAbrirPdf.IsOn = ajustes.DespachoAbrirPdf;
            p.AlternarDetectarAcompanhamento.IsOn = ajustes.AcompanhamentoDetectar;
            p.AlternarConfirmarCancelar.IsOn = ajustes.ConfirmarCancelar;
            p.AlternarConfirmarFechar.IsOn = ajustes.ConfirmarFechar;
            p.AlternarAtualizarSozinho.IsOn = ajustes.AtualizarAutomaticamente;
            p.CaixaPedidoPadrao.Text = ajustes.PedidoPadrao;
            p.CaixaProfissionalNome.Text = ajustes.ProfissionalNome ?? "";
            p.CaixaProfissionalProfissao.Text = ajustes.ProfissionalProfissao ?? "";
            p.CaixaProfissionalRegistro.Text = ajustes.ProfissionalRegistro ?? "";
            p.CaixaProfissionalMatricula.Text = ajustes.ProfissionalMatricula ?? "";
            Pmvni.Composicao.Profissional.Atual = ajustes.Profissional;
            AtualizarTextoSoffice();
            // PMVNI_KIT_RAIZ (testes, roteiro) manda no kit: a escolha da página não vale enquanto ela estiver definida.
            var env = Environment.GetEnvironmentVariable("PMVNI_KIT_RAIZ");
            p.TextoAvisoKitAmbiente.Text = string.IsNullOrWhiteSpace(env) ? "" : "A variável PMVNI_KIT_RAIZ está definida: o editor lê o kit em " + env + ".";
            p.TextoAvisoKitAmbiente.Visibility = string.IsNullOrWhiteSpace(env) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { atualizandoAjustes = false; }
    }

    void AbrirPastaDosAjustes(string pasta)
    {
        if (Directory.Exists(pasta)) AberturaExterna.AbrirPasta(pasta);
        else MostrarMensagem(InfoBarSeverity.Warning, "A pasta não existe", pasta + " não foi encontrada.");
    }

    // ---------- LibreOffice (Ajustes › Módulos › Despachos e PDF) ----------

    /// <summary>O programa que o Novo despacho vai usar (o escolhido, se vale; senão o achado sozinho) e de onde ele veio.</summary>
    void AtualizarTextoSoffice()
    {
        var efetivo = ConversorLibreOffice.Localizar(ajustes.Soffice);
        var origem = ajustes.Soffice is null ? "achado pelo editor" : ConversorLibreOffice.ProgramaValido(ajustes.Soffice) ? "escolhido" : "o escolhido não foi encontrado; achado pelo editor";
        PaginaDeAjustes.TextoSoffice.Text = efetivo is null ? "Nenhum LibreOffice encontrado: os despachos saem só em DOCX." : $"{efetivo}  ({origem})";
        // Sempre clicável (correções 2026-11-15): sem escolha manual, procura o LibreOffice de novo.
        PaginaDeAjustes.BotaoSofficeAutomatico.IsEnabled = true;
    }

    /// <summary>"Testar": confere o programa e a versão pelo arquivo (nada é executado).</summary>
    void TestarSoffice()
    {
        var c = new ConversorLibreOffice(ConversorLibreOffice.Localizar(ajustes.Soffice));
        var t = PaginaDeAjustes.TextoTesteSoffice;
        t.Text = c.Disponivel ? (c.Versao ?? "LibreOffice encontrado (versão não informada).") + " Pronto para gerar o PDF dos despachos."
            : "Nenhum LibreOffice encontrado: os despachos saem só em DOCX. Instale o LibreOffice ou escolha o soffice.exe.";
        t.Visibility = Visibility.Visible;
    }

    async Task EscolherSofficeAsync()
    {
        var arquivo = await Perguntas.Atual.AbrirArquivoAsync(".exe");
        if (arquivo is null) return;
        if (!ConversorLibreOffice.ProgramaValido(arquivo))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Programa não aceito", "Escolha o soffice.exe da pasta program do LibreOffice.");
            return;
        }
        DefinirSoffice(arquivo);
    }

    void DefinirSoffice(string? caminho)
    {
        ajustes.Soffice = caminho;
        SalvarAjustes();
        conversor = null; // o próximo despacho usa o programa novo
        AtualizarTextoSoffice();
        PaginaDeAjustes.TextoTesteSoffice.Visibility = Visibility.Collapsed;
    }

    /// <summary>"Redefinir perfil" (Avançado): apaga <c>&lt;dados&gt;\lo</c>, com confirmação; nunca com um despacho em preparo.</summary>
    async Task RedefinirPerfilSofficeAsync()
    {
        if (novo is not null)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Perfil não redefinido", "Termine ou cancele o despacho em edição antes de redefinir o perfil do LibreOffice.");
            return;
        }
        var perfil = Path.Combine(PastaDados.Raiz, "lo");
        if (!await Perguntas.Atual.ConfirmarAsync("Redefinir o perfil do LibreOffice?",
                "O perfil do LibreOffice usado pelo editor será apagado. O próximo PDF de despacho demora perto de um minuto, enquanto ele é criado de novo.",
                "Redefinir", "Cancelar")) return;
        try
        {
            if (Directory.Exists(perfil)) await Task.Run(() => Directory.Delete(perfil, recursive: true));
            conversor = null;
            PaginaDeAjustes.TextoTesteSoffice.Text = "Perfil redefinido: o próximo PDF cria um perfil novo.";
            PaginaDeAjustes.TextoTesteSoffice.Visibility = Visibility.Visible;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Perfil não redefinido", Erro(e));
        }
    }

    // ---------- Restaurar os ajustes (Ajustes › Sistema) ----------

    /// <summary>Volta todas as opções ao padrão, com confirmação; não toca nas chaves de API, nas leis anexadas nem nos recentes.
    /// true = restaurou.</summary>
    internal async Task<bool> RestaurarAjustesAsync()
    {
        if (!await Perguntas.Atual.ConfirmarAsync("Restaurar os ajustes?",
                "Todas as opções voltam ao padrão, inclusive a pasta de processos, a pasta do kit, o LibreOffice e a assinatura digital escolhida. Continuam como estão: chaves de API, dados do profissional, pasta QGIS, planilha de acompanhamento, logins dos sites, leis anexadas, arquivos recentes e a tabela de Modelos da análise.",
                "Restaurar", "Cancelar")) return false;
        var tinhaSoffice = ajustes.Soffice is not null;
        ajustes.RestaurarPadrao();
        // A assinatura volta a "nenhuma": a senha lembrada do certificado sai do cofre junto.
        CofreSenhaCertificado.Remover(PastaDados.Raiz);
        certificadoConferido = null;
        MostrarAssinaturaNosAjustes();
        SalvarAjustes();
        AplicarTemaDosAjustes();
        ReiniciarAutosave();
        AplicarLargura(Raiz.ActualWidth);
        if (tinhaSoffice) conversor = null;
        AtualizarPaginaDePastas();
        SincronizarAjustes();
        SincronizarPastas();
        AtualizarComandos();
        if (sessao is { Atual: { } r } s) { MostrarRecomposicao(r); await s.EnviarEstadoAsync(); }
        MostrarMensagem(InfoBarSeverity.Success, "Ajustes restaurados", "As opções voltaram ao padrão.");
        return true;
    }

    // ---------- atualizações (Ajustes › Sistema, no padrão do WinStatusBar) ----------

    /// <summary>Liga a seção Atualizações ao <see cref="Atualizador"/>: versão instalada, botão, estado, chave automática e "O que mudou".</summary>
    void IniciarAtualizacoes()
    {
        var p = PaginaDeAjustes;
        p.TextoVersaoInstalada.Text = RegrasAtualizacao.TextoVersao(Atualizador.Versao, Atualizador.Compilacao);
        p.AlternarAtualizarSozinho.Toggled += (_, _) =>
        {
            if (atualizandoAjustes) return;
            ajustes.AtualizarAutomaticamente = p.AlternarAtualizarSozinho.IsOn;
            SalvarAjustes();
        };
        p.BotaoRepararInstalacao.Click += async (_, _) => await AtualizarComponenteAsync(null, reparar: true);
        p.BotaoVerificarAtualizacoes.Click += async (_, _) => await Atualizador.VerificarAsync();
        p.BotaoReiniciarAtualizacao.Click += (_, _) =>
        {
            Atualizador.PedirReabertura();
            // Fecha pelo mesmo caminho do X da janela (grava e pergunta o que for preciso); desistir de fechar cancela a reabertura em 10 min.
            PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(this), 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
        };
        Atualizador.AutomaticaLigada = () => ajustes.AtualizarAutomaticamente;
        void EstadoMudou(EstadoAtualizacao e) => DispatcherQueue.TryEnqueue(() => MostrarEstadoAtualizacao(e));
        Atualizador.EstadoMudou += EstadoMudou;
        Closed += (_, _) =>
        {
            Atualizador.Agendar(false);
            Atualizador.EstadoMudou -= EstadoMudou;
            Atualizador.AutomaticaLigada = () => false;
        };
        MostrarEstadoAtualizacao(Atualizador.UltimoEstado ?? (Atualizador.Habilitado
            ? new EstadoAtualizacao(EtapaAtualizacao.EmDia, "Ainda não verificado nesta abertura.")
            : new EstadoAtualizacao(EtapaAtualizacao.Desligada, "Compilação de desenvolvimento: a atualização automática fica desligada.")));
        Atualizador.Iniciar();
        IniciarComponentesDoKit();
    }

    // ---------- Plugins e ferramentas (Tarefa 21; Ajustes › Kit PMVNI) ----------

    /// <summary>O roteiro troca as pastas lidas (fictícias), a pasta onde o <c>.cmd</c> é gravado e o lançador (nada é aberto de verdade).</summary>
    internal OrigensDoKit? OrigensDoKitDoRoteiro { get; set; }
    internal string? PastaScriptsDoKitDoRoteiro { get; set; }
    internal Action<string, string>? LancadorDoKit { get; set; }
    /// <summary>A última leitura mostrada no cartão (null = ainda não lido).</summary>
    internal LeituraDoKit? LeituraDoKitAtual { get; private set; }
    /// <summary>O botão "Atualizar" de cada componente (pelo <see cref="ComponenteDoKit.Id"/>), para o roteiro.</summary>
    internal IReadOnlyDictionary<string, Button> BotoesDosComponentes => botoesDosComponentes;
    readonly Dictionary<string, Button> botoesDosComponentes = new(StringComparer.Ordinal);
    int geracaoComponentes;
    bool atualizandoComponente;

    void IniciarComponentesDoKit()
    {
        var p = PaginaDeAjustes;
        p.BotaoVerificarComponentes.Click += async (_, _) => await VerificarComponentesAsync();
        p.BotaoAtualizarTodosComponentes.Click += async (_, _) => await AtualizarComponenteAsync(null);
        // A fonte do kit pode ser uma pasta de rede: só lê quando a página Kit PMVNI é vista (e pelo botão).
        p.CategoriaMostrada += tag => { if (tag == CategoriasAjustes.Kit && PaginaAjustesVisivel) _ = VerificarComponentesAsync(); };
    }

    OrigensDoKit OrigensDoKitDoComputador()
    {
        if (OrigensDoKitDoRoteiro is { } r) return r;
        if (Argumentos.Atual.Roteiro is not null)
        {
            var oRoteiro = OpcoesAgentes();
            return new OrigensDoKit(oRoteiro.CodexHome, null, oRoteiro.LocalAppData, oRoteiro.LocalAppData,
                ajustes.PastaKitEfetiva, "", () => null);
        }
        var o = Pmvni.Agentes.OpcoesLocalizacao.DoAmbiente(ajustes.PastaKitEfetiva, null, null);
        var pmvni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PMVNI");
        return new OrigensDoKit(o.CodexHome, o.PastaClaude, InstalacaoMotorPdf.BaseLocal, pmvni, ajustes.PastaKitEfetiva, InstalacaoMotorPdf.HashDoServidorPdf(),
            TarefaDoKitExiste, InstalacaoMotorPdf.VersaoDoServidorPdf(), AmbienteEsperado: InstalacaoMotorPdf.AmbienteEsperado);
    }

    /// <summary>A tarefa "PMVNI\Atualizar kit" existe? (<c>schtasks /Query</c>, só leitura; código 0 = existe, 1 = não existe; outro = não se sabe.)</summary>
    static bool? TarefaDoKitExiste()
    {
        try
        {
            var inicio = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var a in new[] { "/Query", "/TN", @"\PMVNI\Atualizar kit" }) inicio.ArgumentList.Add(a);
            using var consulta = ProcessoInterno.Iniciar(inicio);
            _ = consulta.StandardOutput.ReadToEnd();
            _ = consulta.StandardError.ReadToEnd();
            if (!consulta.Processo.WaitForExit(5000)) return null;
            return consulta.Processo.ExitCode switch { 0 => true, 1 => false, _ => null };
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>"Verificar atualizações": lê as versões (sem travar a janela; a fonte fora do alcance não é erro) e remonta as linhas.</summary>
    internal async Task VerificarComponentesAsync()
    {
        if (fechada) return;
        var p = PaginaDeAjustes;
        var geracao = ++geracaoComponentes;
        p.BotaoVerificarComponentes.IsEnabled = false;
        p.TextoResultadoComponentes.Text = "Verificando as versões…";
        try
        {
            var leitura = await ComponentesDoKit.LerAsync(OrigensDoKitDoComputador());
            if (fechada || geracao != geracaoComponentes) return;
            LeituraDoKitAtual = leitura;
            MostrarComponentes(leitura);
            p.TextoResultadoComponentes.Text = "Verificado às " + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + ".";
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("kit.componentes", e);
            if (geracao == geracaoComponentes) p.TextoResultadoComponentes.Text = "Não foi possível verificar os componentes. " + e.Message;
        }
        finally { if (geracao == geracaoComponentes) p.BotaoVerificarComponentes.IsEnabled = true; }
    }

    void MostrarComponentes(LeituraDoKit leitura)
    {
        var p = PaginaDeAjustes;
        var recursos = Application.Current.Resources;
        p.TextoFonteComponentes.Text = leitura.TextoFonte;
        p.TextoTarefaKit.Text = leitura.TextoTarefa;
        p.ListaComponentesKit.Children.Clear();
        botoesDosComponentes.Clear();
        foreach (var linha in leitura.Linhas)
        {
            var grade = new Grid { Padding = new Thickness(16, 10, 16, 10), ColumnSpacing = 16, BorderThickness = new Thickness(0, 1, 0, 0) };
            if (recursos.TryGetValue("CardStrokeColorDefaultBrush", out var borda) && borda is Microsoft.UI.Xaml.Media.Brush pincel) grade.BorderBrush = pincel;
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var textos = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(new TextBlock { Text = linha.Componente.Nome, TextWrapping = TextWrapping.Wrap, Style = (Style)recursos["PmvniAjusteTitulo"] });
            textos.Children.Add(new TextBlock { Text = linha.TextoVersoes, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Style = (Style)recursos["PmvniAjusteAjuda"] });
            textos.Children.Add(new TextBlock { Text = linha.Detalhe, TextWrapping = TextWrapping.Wrap, Style = (Style)recursos["PmvniAjusteAjuda"] });
            grade.Children.Add(textos);
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var selo = new SeloEstado();
            selo.Definir(linha.Situacao == SituacaoComponente.EmDia, linha.TextoSituacao);
            acoes.Children.Add(selo);
            var botao = new Button { Content = linha.TextoBotao };
            IconeDeAcao.SetGlifo(botao, Glifos.Atualizar);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(botao, "Atualizar " + linha.Componente.Nome + (linha.SoPeloTodos ? " (atualiza todo o kit)" : ""));
            Dicas.Aplicar(botao, linha.TextoBotao, linha.TextoDica);
            botao.Click += async (_, _) => await AtualizarComponenteAsync(linha);
            acoes.Children.Add(botao);
            Grid.SetColumn(acoes, 1);
            grade.Children.Add(acoes);
            p.ListaComponentesKit.Children.Add(grade);
            botoesDosComponentes[linha.Componente.Id] = botao;
        }
    }

    /// <summary>"Atualizar" de uma linha (ou "Atualizar todos", com <paramref name="linha"/> null): grava o <c>.cmd</c> e o abre pelo
    /// <c>explorer.exe</c>, fora do Editor. Com análise em andamento, só avisa.</summary>
    internal async Task AtualizarComponenteAsync(LinhaComponente? linha, bool reparar = false)
    {
        var p = PaginaDeAjustes;
        // Enquanto a atualização é iniciada, nenhum outro "Atualizar" (nem o "Atualizar todos") pode ser clicado.
        void Habilitar(bool sim)
        {
            p.BotaoAtualizarTodosComponentes.IsEnabled = sim;
            p.BotaoRepararInstalacao.IsEnabled = sim;
            foreach (var b in botoesDosComponentes.Values) b.IsEnabled = sim;
        }
        if (atualizandoComponente) return;
        atualizandoComponente = true;
        Habilitar(false);
        try
        {
            var analises = ProcessosComAnaliseEmAndamento();
            var pasta = PastaScriptsDoKitDoRoteiro ?? await InstalacaoMotorPdf.ExtrairAsync();
            var origens = OrigensDoKitDoComputador();
            var kit = LeituraDoKitAtual?.KitLocal ?? await Task.Run(() => ComponentesDoKit.FonteDoKit(origens).KitLocal);
            var marcador = Path.Combine(pasta, "resultado-" + Guid.NewGuid().ToString("N") + ".txt");
            string? wrapper = null;
            void Iniciar(string programa, string cmd)
            {
                if (LancadorDoKit is { } simulado) { simulado(programa, cmd); return; }
                wrapper = Path.ChangeExtension(marcador, ".cmd");
                var corpo = "@echo off\r\ncall \"" + cmd + "\"\r\n> \"" + marcador + ".tmp\" echo %errorlevel%\r\n"
                    + "move /y \"" + marcador + ".tmp\" \"" + marcador + "\" >nul\r\n";
                File.WriteAllText(ArquivoGerado.Caminho(wrapper), corpo, PrimeiraAbertura.CodificacaoDoConsole());
                AbrirPeloWindows(programa, wrapper);
            }
            p.TextoResultadoComponentes.Text = reparar
                ? ComponentesDoKit.IniciarAtualizacao("reparar", analises, pasta, kit, Iniciar)
                : await ComponentesDoKit.AtualizarAsync(linha, analises, pasta, kit, Iniciar);
            if (wrapper is null) return;
            p.TextoResultadoComponentes.Text = "Aguardando o instalador do kit terminar no Windows…";
            var espera = System.Diagnostics.Stopwatch.StartNew();
            while (!fechada && !File.Exists(marcador))
            {
                if (espera.Elapsed >= TimeSpan.FromMinutes(30))
                {
                    p.TextoResultadoComponentes.Text = "O Windows ainda não confirmou o término da instalação. Confira o instalador e use Verificar atualizações.";
                    return;
                }
                await Task.Delay(750);
            }
            if (fechada) return;
            var codigo = (await File.ReadAllTextAsync(marcador)).Trim();
            File.Delete(marcador);
            File.Delete(wrapper);
            await VerificarComponentesAsync();
            if (codigo != "0") p.TextoResultadoComponentes.Text = codigo switch
            {
                "4" => "Atualização adiada. Há uma análise ou manutenção em andamento.",
                "41" => "A fonte do kit está fora do alcance. A atualização pode ser repetida na rede da Prefeitura.",
                "42" => "O instalador do kit registrou pendências. Consulte a última execução do instalador.",
                "43" => "O instalador do kit não confirmou a conclusão desta atualização.",
                _ => $"O instalador terminou com código {codigo}. Confira as pendências acima.",
            };

        }
        catch (Exception e)
        {
            RegistroErros.Gravar("kit.componentes.atualizar", e);
            p.TextoResultadoComponentes.Text = "Não foi possível iniciar a atualização. " + e.Message;
        }
        finally { atualizandoComponente = false; Habilitar(true); }
    }

    /// <summary>O lançador de verdade: o <c>explorer.exe</c> abre o <c>.cmd</c> fora do processo do Editor.</summary>
    static void AbrirPeloWindows(string programa, string argumento)
    {
        var inicio = new System.Diagnostics.ProcessStartInfo(programa) { UseShellExecute = false };
        inicio.ArgumentList.Add(argumento);
        using var _ = TravaDeProgramas.Iniciar(inicio);
    }

    /// <summary>As execuções em andamento na janela, as que podem continuar fora dela (Claude e Gemini) e as do ChatGPT com trava viva.</summary>
    IReadOnlyList<string> ProcessosComAnaliseEmAndamento()
    {
        var nomes = new List<string>();
        if (sessaoNR is not null)
            nomes.AddRange(sessaoNR.Execucoes.Where(e => e.Fim is not { IsCompleted: true }).Select(e => e.Processo.NomePasta));
        nomes.AddRange(ajustes.ExecucoesClaude.Concat(AtualizacaoKit.ChatGptEmAndamento(ajustes.ExecucoesChatGpt))
            .Select(e => Path.GetFileName(Path.TrimEndingDirectorySeparator(e.Processo))));
        return nomes.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    bool notificouAtualizacaoPronta;

    void MostrarEstadoAtualizacao(EstadoAtualizacao e)
    {
        if (fechada) return;
        var p = PaginaDeAjustes;
        p.TextoEstadoAtualizacao.Text = e.Texto;
        p.BotaoVerificarAtualizacoes.IsEnabled = e.Etapa is EtapaAtualizacao.EmDia or EtapaAtualizacao.Falhou;
        p.BotaoReiniciarAtualizacao.Visibility = e.Etapa == EtapaAtualizacao.Pronta ? Visibility.Visible : Visibility.Collapsed;
        p.AlternarAtualizarSozinho.IsEnabled = Atualizador.Habilitado;
        p.TextoOQueMudou.Text = string.IsNullOrWhiteSpace(e.Notas) ? "As notas aparecem aqui depois da primeira verificação." : e.Notas;
        if (e.Etapa == EtapaAtualizacao.Pronta && !notificouAtualizacaoPronta)
        {
            notificouAtualizacaoPronta = true;
            NotificacoesWindows.Mostrar("Atualização do Editor pronta", "A nova versão será usada na próxima abertura do Editor.");
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    static extern bool PostMessage(IntPtr janela, uint mensagem, IntPtr w, IntPtr l);

    string TextoDasVersoes()
    {
        var versao = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "?";
        var webview = R.Navegador.CoreWebView2?.Environment.BrowserVersionString ?? FabricaWebView2.VersaoDisponivel() ?? "ausente";
        // 1.0.0: a versão exibida ("1.0") e, entre parênteses, a técnica ("1.0.0"), que o suporte usa.
        var exibida = RegrasAtualizacao.VersaoExibida(versao);
        return $"Versão do app: {(exibida == versao ? versao : $"{exibida} ({versao})")}{Environment.NewLine}Modelo do relatório: rev{ModeloOficial.Revisao}{Environment.NewLine}WebView2: {webview}"
            + $"{Environment.NewLine}PDF.js {RecursosLegislacao.VersaoPdfJs} (Apache-2.0)";
    }

    // ---------- recentes ----------

    /// <summary>O arquivo que acabou de ser aberto ou gravado vai para o topo dos Recentes (guardados nos ajustes, no máximo 10).</summary>
    void RegistrarRecente(string caminho)
    {
        ajustes.AdicionarRecente(Path.GetFullPath(caminho));
        SalvarAjustes();
        if (fechada) return;
        geracaoRecentes++;          // uma conferência em curso (de antes) não desfaz esta
        haRecentes = true;          // o arquivo acabou de ser aberto ou gravado: existe
        AtualizarComandos();
    }

    int geracaoRecentes;

    /// <summary>Confere quais recentes ainda existem (só o que existe aparece; nada fica desabilitado) e habilita o menu se houver algum. A conferência
    /// do disco corre fora da thread da interface: um recente numa pasta de rede lenta não congela a janela a cada ativação.</summary>
    void AtualizarRecentes()
    {
        if (fechada) return;
        _ = AtualizarRecentesAsync();
    }

    async Task AtualizarRecentesAsync()
    {
        var lista = ajustes.Recentes.ToList();
        var geracao = ++geracaoRecentes;
        List<string> existentes;
        try { existentes = await Task.Run(() => lista.Where(File.Exists).ToList()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        if (fechada || geracao != geracaoRecentes) return;
        haRecentes = existentes.Count > 0 || historicoAnalises.Count > 0;
        // A tela vazia de Relatórios (M5) mostra os 5 mais recentes que ainda existem.
        R.TextoVazio.DefinirRecentes(existentes.Take(5).Select(c => (Path.GetFileName(c), c)).ToList());
        AtualizarComandos();
    }

    IReadOnlyList<(string Texto, string Valor, string Dica)> ItensDoMenuDaFaixa(string menu)
    {
        if (menu == ComandosApp.ProcessosAno) return ItensDoMenuAno();
        if (menu == ComandosApp.FigurasAbrirEditor) return ItensDoMenuFiguras();
        if (menu != ComandosApp.ArquivoRecentes) return [];
        var lista = ajustes.RecentesExistentes();
        haRecentes = lista.Count > 0 || historicoAnalises.Count > 0;
        return historicoAnalises.Select(h => (h.Identificacao, h.Registro, h.Registro)).Concat(lista.Where(c => !historicoAnalises.Any(h => string.Equals(h.Pacote, c, StringComparison.OrdinalIgnoreCase))).Select(c =>
        {
            var pasta = Path.GetFileName(Path.GetDirectoryName(c)) ?? "";
            return (pasta.Length == 0 ? Path.GetFileName(c) : Path.GetFileName(c) + "  ·  " + pasta, c, c);
        })).ToList();
    }

    /// <summary>Arquivo › Recentes: abre o relatório escolhido (na fila de comandos, como Abrir); se ele sumiu, avisa.</summary>
    internal Task AbrirRecenteAsync(string caminho) => historicoAnalises.FirstOrDefault(h => MesmoRegistro(h.Registro, caminho)) is { } item
        ? TentarAsync(() => AbrirHistoricoAsync(item)) : NaFilaAsync(() => TentarAsync(async () =>
    {
        if (!File.Exists(caminho))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não existe mais", Path.GetFileName(caminho) + " não está mais em " + Path.GetDirectoryName(caminho) + ".");
            AtualizarRecentes();
            return;
        }
        await AbrirPacoteAsync(caminho);
    }));

    // ---------- comparar com o original ----------

    /// <summary>O texto de "Comparar com o original" para o relatório aberto (o mesmo que a janela mostra).</summary>
    internal string TextoComparacao()
    {
        if (sessao?.Atual is null) return "Nenhum relatório aberto.";
        var p = sessao.Revisao.Projeto;
        var h = sessao.Revisao.HistoricoCompleto;
        var texto = Comparacao.Texto(Comparacao.Diferencas(p, h), Comparacao.CitacoesImportadas(p, h), Comparacao.DiferencasLinhas(p));
        // Figuras (Etapa 4): marcações, recortes e textos das figuras, à parte (só quando alguma figura mudou).
        return ComparacaoFiguras.Texto(p) is { Length: > 0 } figuras ? texto + "\n\n" + figuras : texto;
    }

    Task CompararAsync() => Perguntas.Atual.InformarAsync("Comparar com o original", TextoComparacao());

    // ---------- legislação (desenho §11.1): Ajustes › Módulos › Legislação ----------

    SessaoLegislacao legislacao = null!;
    CancellationTokenSource? cancelarAnexo;
    readonly CancellationTokenSource cancelarReindexacao = new();
    Task reindexacao = Task.CompletedTask;
    IReadOnlyList<LinhaAnexo> linhasLegislacao = [];
    readonly SemaphoreSlim releituraLegislacao = new(1, 1);
    readonly System.Collections.ObjectModel.ObservableCollection<LinhaAnexo> linhasNaTela = [];
    bool releituraAdiada;

    internal SessaoLegislacao Legislacao => legislacao;
    /// <summary>As linhas da seção Legislação dos Ajustes (uma por anexo), como estão na tela.</summary>
    internal IReadOnlyList<LinhaAnexo> LinhasLegislacaoAjustes => linhasLegislacao;
    internal InfoBar AvisoLegislacaoAjustes => PaginaDeAjustes.AvisoLegislacao;
    internal InfoBar AvisosAcervoAjustes => PaginaDeAjustes.AvisosAcervoLegislacao;
    internal string TotalLegislacaoAjustes => PaginaDeAjustes.TextoTotalLegislacao.Text;
    /// <summary>O índice que está sendo refeito em segundo plano (o roteiro espera antes de mexer na pasta).</summary>
    internal Task ReindexacaoLegislacao => reindexacao;

    void IniciarLegislacaoAjustes()
    {
        legislacao = new SessaoLegislacao(Path.Combine(PastaDados.Itinerante, "legislacao"), CriarExtratorLegislacaoAsync);
        legislacao.Mudou += () => DispatcherQueue.TryEnqueue(() => _ = AtualizarListaLegislacaoAsync());
        var a = PaginaDeAjustes;
        a.ListaAnexos.ItemsSource = linhasNaTela;
        // Os manipuladores daqui são async void: nenhuma exceção sai deles (mensagem em português na InfoBar, detalhe no registro).
        a.BotaoAnexarLegislacao.Click += async (_, _) => await SemDerrubarAsync(() => AnexarLegislacaoAsync());
        a.BotaoCancelarLegislacao.Click += (_, _) => cancelarAnexo?.Cancel();
        PaginaDeLegislacao.BotaoCancelarAnexo.Click += (_, _) => cancelarAnexo?.Cancel();
        a.TituloEditado = async (id, t) =>
        {
            try { return await RenomearAnexoAsync(id, t); }
            catch (Exception e) { var texto = Erro(e); MostrarAvisoLegislacao(InfoBarSeverity.Error, "Título não alterado", texto); return texto; }
        };
        a.TituloConcluido += async () => { if (releituraAdiada) await SemDerrubarAsync(AtualizarListaLegislacaoAsync); };
        a.EsferaEscolhida += async (id, e) => await SemDerrubarAsync(() => DefinirEsferaAnexoAsync(id, e));
        a.SubstituirPedido += async id => await SemDerrubarAsync(() => SubstituirAnexoAsync(id));
        a.ExcluirPedido += async id => await SemDerrubarAsync(() => ExcluirAnexoAsync(id));
        a.CategoriaMostrada += tag =>
        {
            if (tag == CategoriasAjustes.Modulos && PaginaAjustesVisivel) Soltar(EntrarNaLegislacaoDosAjustesAsync);
        };
    }

    /// <summary>O extrator real (PDF.js num WebView2 oculto e InPrivate sobre esta janela, no ambiente da tela), criado na thread da
    /// interface, de onde quer que a sessão o peça.</summary>
    Task<IExtratorPdf> CriarExtratorLegislacaoAsync()
    {
        var tcs = new TaskCompletionSource<IExtratorPdf>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (fechada) throw new InvalidOperationException("A janela foi fechada.");
                var ambiente = R.Navegador.CoreWebView2?.Environment ?? await ambienteDaTela.Task.WaitAsync(TimeSpan.FromSeconds(60));
                var janela = WinRT.Interop.WindowNative.GetWindowHandle(this);
                tcs.TrySetResult(await Pmvni.Composicao.Web.Legislacao.ExtratorPdfJs.CriarAsync(ambiente, janela, legislacao.Servidor));
            }
            catch (Exception e) { tcs.TrySetException(e); }
        })) tcs.TrySetException(new InvalidOperationException("A janela foi fechada."));
        return tcs.Task;
    }

    /// <summary>Ao abrir os Ajustes: apaga entradas órfãs (mais de 1 dia), mostra os avisos da carga e refaz em segundo plano os índices
    /// que faltam (selo "Preparando a pesquisa…").</summary>
    async Task EntrarNaLegislacaoDosAjustesAsync()
    {
        try
        {
            await Task.Run(legislacao.Acervo.LimparEntradasOrfas);
            await AtualizarListaLegislacaoAsync();
            if (reindexacao.IsCompleted && !legislacao.Acervo.SomenteLeitura)
                reindexacao = legislacao.ReindexarPendentesAsync(cancelarReindexacao.Token);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MostrarAvisoLegislacao(InfoBarSeverity.Error, "Legislação", Erro(e)); }
    }

    /// <summary>Relê o acervo e redesenha a seção: total, linhas, só leitura e os avisos pendentes do acervo (lidos uma vez).</summary>
    internal async Task AtualizarListaLegislacaoAsync()
    {
        // Uma releitura por vez, na ordem: a que termina por último leu o acervo por último (quem espera esta vê o estado de agora).
        await releituraLegislacao.WaitAsync();
        try { await RelerLegislacaoAsync(); }
        catch (Exception e)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível ler a legislação anexada", Erro(e));
            ReabilitarLegislacao();
        }
        finally { releituraLegislacao.Release(); }
    }

    /// <summary>Roda uma ação da tela da legislação sem deixar exceção escapar (manipuladores async void).</summary>
    async Task SemDerrubarAsync(Func<Task> acao)
    {
        try { await acao(); }
        catch (Exception e) { MostrarAvisoLegislacao(InfoBarSeverity.Error, "A operação falhou", Erro(e)); }
    }

    void ReabilitarLegislacao()
    {
        if (fechada) return;
        var livre = !legislacao.Acervo.SomenteLeitura && cancelarAnexo is null;
        PaginaDeAjustes.BotaoAnexarLegislacao.IsEnabled = livre;
        PaginaDeAjustes.ConteudoAnexos.IsEnabled = livre;
    }

    /// <summary>Uma caixa de título das linhas está com o foco (o usuário está digitando).</summary>
    bool EditandoTituloDeAnexo() =>
        Raiz.XamlRoot is not null && FocusManager.GetFocusedElement(Raiz.XamlRoot) is TextBox { DataContext: LinhaAnexo };

    /// <summary>Atualiza as linhas na tela no lugar, por id: o que não mudou fica com o mesmo elemento (foco, rolagem e texto).</summary>
    void SincronizarLinhas(IReadOnlyList<LinhaAnexo> novas)
    {
        var col = linhasNaTela;
        var ids = novas.Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = col.Count - 1; i >= 0; i--) if (!ids.Contains(col[i].Id)) col.RemoveAt(i);
        for (var i = 0; i < novas.Count; i++)
        {
            var j = -1;
            for (var k = i; k < col.Count; k++) if (col[k].Id == novas[i].Id) { j = k; break; }
            if (j < 0) { col.Insert(i, novas[i]); continue; }
            if (j != i) col.Move(j, i);
            if (col[i] != novas[i]) col[i] = novas[i];
        }
    }

    async Task RelerLegislacaoAsync()
    {
        IReadOnlyList<Anexo> anexos;
        IReadOnlyList<LinhaAnexo> linhas;
        try { (anexos, linhas) = await legislacao.ListarAsync(TimeZoneInfo.Local); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível ler a legislação anexada", Erro(e));
            ReabilitarLegislacao();     // depois de uma anexação, Anexar e as linhas não ficam desabilitados
            return;
        }
        if (fechada) return;
        linhasLegislacao = linhas;
        var a = PaginaDeAjustes;
        // Com um título em edição, a tela espera o usuário sair da caixa (o texto digitado nunca se perde); os dados já estão novos.
        releituraAdiada = EditandoTituloDeAnexo();
        if (!releituraAdiada) SincronizarLinhas(linhas);
        a.TextoTotalLegislacao.Text = ListaLegislacao.Total(anexos);
        var somenteLeitura = legislacao.Acervo.SomenteLeitura;
        a.TextoSomenteLeituraLegislacao.Text = somenteLeitura ? MensagensAnexo.SomenteLeitura : "";
        a.TextoSomenteLeituraLegislacao.Visibility = somenteLeitura ? Visibility.Visible : Visibility.Collapsed;
        a.BotaoAnexarLegislacao.IsEnabled = !somenteLeitura && cancelarAnexo is null;
        a.ConteudoAnexos.IsEnabled = !somenteLeitura && cancelarAnexo is null;
        var avisos = legislacao.Acervo.LerAvisos();
        if (avisos.Count > 0)
        {
            a.AvisosAcervoLegislacao.Title = "Legislação";
            a.AvisosAcervoLegislacao.Message = string.Join("\n", avisos);
            a.AvisosAcervoLegislacao.IsOpen = true;
        }
        LinhasLegislacaoMudaram(anexos);
    }

    /// <summary>Gancho para o módulo Legislação acompanhar a lista (Tarefa 18).</summary>
    partial void LinhasLegislacaoMudaram(IReadOnlyList<Anexo> anexos);

    /// <summary>O aviso da legislação em Ajustes e, com o módulo Legislação à frente, também nele (onde o usuário está).</summary>
    void MostrarAvisoLegislacao(InfoBarSeverity severidade, string titulo, string mensagem)
    {
        if (fechada) return;
        foreach (var aviso in moduloAtual == Modulo.Legislacao
                     ? new[] { PaginaDeAjustes.AvisoLegislacao, PaginaDeLegislacao.AvisoMensagem }
                     : new[] { PaginaDeAjustes.AvisoLegislacao })
        {
            aviso.Severity = severidade;
            aviso.Title = titulo;
            aviso.Message = mensagem;
            aviso.IsOpen = true;
        }
    }

    /// <summary>O texto do segundo "Anexar PDFs…" com outra anexação correndo.</summary>
    internal const string AnexacaoEmAndamento = "Uma anexação já está em andamento.";

    /// <summary>"Anexar PDFs…": o seletor (múltiplo, .pdf) na fila de comandos; a leitura dos arquivos (cópia, sha, PDF.js) corre fora
    /// dela, com a linha de progresso e "Cancelar". No fim, a InfoBar com o resumo exato (<see cref="ListaLegislacao.Resumo"/>), que
    /// também é devolvido ("" se nada foi escolhido ou outra anexação já corre).</summary>
    internal async Task<string> AnexarLegislacaoAsync(IReadOnlyList<string>? escolhidos = null)
    {
        IReadOnlyList<string> caminhos = [];
        CancellationTokenSource? cts = null;
        var emAndamento = false;
        await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (!PodeAlterarLegislacao()) return;
            if (cancelarAnexo is not null)
            {
                MostrarAvisoLegislacao(InfoBarSeverity.Informational, "Anexar PDFs", AnexacaoEmAndamento);
                emAndamento = true;
                return;
            }
            caminhos = escolhidos ?? await Perguntas.Atual.AbrirArquivosAsync(".pdf");
            if (caminhos.Count == 0) return;
            if (cancelarAnexo is not null)
            {
                MostrarAvisoLegislacao(InfoBarSeverity.Informational, "Anexar PDFs", AnexacaoEmAndamento);
                emAndamento = true;
                return;
            }
            cts = cancelarAnexo = new CancellationTokenSource();
            ComecarProgressoLegislacao();
        }));
        if (cts is null) return emAndamento ? AnexacaoEmAndamento : "";
        try
        {
            var progresso = new Progress<ProgressoAnexo>(p => MostrarProgressoLegislacao("Anexando", p));
            var (anexados, recusas, ignorados) = await legislacao.AnexarContandoAsync(caminhos, progresso, cts.Token);
            var resumo = ListaLegislacao.Resumo(anexados.Count, recusas, ignorados);
            var cancelado = cts.IsCancellationRequested;
            if (cancelado) resumo += "\nAnexação cancelada: o arquivo em andamento não entrou.";
            // Cancelar não é erro: aviso neutro (só as recusas pedem atenção).
            var severidade = recusas.Count > 0 ? InfoBarSeverity.Warning : cancelado ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
            MostrarAvisoLegislacao(severidade, "Anexar PDFs", resumo);
            return resumo;
        }
        catch (Exception e)
        {
            var texto = Erro(e);
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível anexar", texto);
            return texto;
        }
        finally
        {
            TerminarProgressoLegislacao(cts);
            await AtualizarListaLegislacaoAsync();
        }
    }

    /// <summary>"Excluir" de uma linha: confirma com os textos do desenho §11.1 e apaga a linha, a cópia e o índice (o original nunca).
    /// true se excluiu. Cópia presa por outro programa: excluída do catálogo e o aviso aparece.</summary>
    internal Task<bool> ExcluirAnexoAsync(string id) => NaFilaAsync(async () =>
    {
        if (!PodeAlterarLegislacao()) return false;
        Anexo? anexo;
        try { anexo = (await Task.Run(legislacao.Acervo.Listar)).FirstOrDefault(a => a.Id == id); }
        catch (Exception e)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível excluir", Erro(e));
            return false;
        }
        if (anexo is null) return false;
        var confirmou = await Perguntas.Atual.ConfirmarAsync($"Excluir “{anexo.Titulo}”?",
            $"A cópia do PDF guardada pelo editor e o seu índice de pesquisa serão apagados. O arquivo original ({anexo.ArquivoOriginal}) não é alterado.",
            "Excluir", "Cancelar");
        if (!confirmou) return false;
        try
        {
            var aviso = await legislacao.ExcluirAsync(id);
            if (aviso is not null) MostrarAvisoLegislacao(InfoBarSeverity.Warning, $"“{anexo.Titulo}” foi excluído", aviso);
            else MostrarAvisoLegislacao(InfoBarSeverity.Success, "Excluído", $"“{anexo.Titulo}” foi excluído. O arquivo original não foi alterado.");
            return true;
        }
        catch (Exception e)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível excluir", Erro(e));
            return false;
        }
        finally { await AtualizarListaLegislacaoAsync(); }
    });

    /// <summary>"Substituir arquivo…": o seletor na fila de comandos; a leitura do arquivo novo corre fora dela (com o progresso). Devolve a
    /// mensagem de recusa, ou null (substituído, ou nada escolhido).</summary>
    internal async Task<string?> SubstituirAnexoAsync(string id)
    {
        string? caminho = null;
        CancellationTokenSource? cts = null;
        await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (!PodeAlterarLegislacao()) return;
            caminho = await Perguntas.Atual.AbrirArquivoAsync(".pdf");
            if (caminho is null || cancelarAnexo is not null) return;
            cts = cancelarAnexo = new CancellationTokenSource();
            ComecarProgressoLegislacao();
        }));
        if (cts is null || caminho is null) return null;
        try
        {
            var progresso = new Progress<ProgressoAnexo>(p => MostrarProgressoLegislacao("Substituindo pelo arquivo", p));
            var recusa = await legislacao.SubstituirAsync(id, caminho, cts.Token, progresso);
            if (recusa is not null) MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Substituir arquivo", recusa);
            else MostrarAvisoLegislacao(InfoBarSeverity.Success, "Substituir arquivo", $"O arquivo foi substituído por “{Path.GetFileName(caminho)}”.");
            return recusa;
        }
        catch (OperationCanceledException)
        {
            MostrarAvisoLegislacao(InfoBarSeverity.Informational, "Substituir arquivo", "Substituição cancelada: o arquivo anterior continua.");
            return null;
        }
        catch (Exception e)
        {
            var texto = Erro(e);
            MostrarAvisoLegislacao(InfoBarSeverity.Error, "Não foi possível substituir", texto);
            return texto;
        }
        finally
        {
            TerminarProgressoLegislacao(cts);
            await AtualizarListaLegislacaoAsync();
        }
    }

    /// <summary>Título de uma linha (ao sair da caixa ou com Enter): devolve a mensagem de recusa ("O título não pode ficar vazio.",
    /// "O título pode ter até 200 caracteres.") ou null.</summary>
    internal Task<string?> RenomearAnexoAsync(string id, string titulo) => NaFilaAsync(async () =>
    {
        string? recusa = null;
        try { await legislacao.RenomearAsync(id, titulo); }
        catch (Exception e)
        {
            recusa = MotivoLegislacao(e);
            MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Título não alterado", recusa);
        }
        await AtualizarListaLegislacaoAsync();
        return recusa;
    });

    internal Task DefinirEsferaAnexoAsync(string id, Esfera e) => NaFilaAsync(async () =>
    {
        try { await legislacao.DefinirEsferaAsync(id, e); }
        catch (Exception x) { MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Esfera não alterada", MotivoLegislacao(x)); }
        await AtualizarListaLegislacaoAsync();
    });

    /// <summary>O motivo de uma alteração recusada, em português: as mensagens do acervo já são (título vazio, só leitura); o resto passa
    /// por <see cref="Erro"/> (o detalhe vai para o registro).</summary>
    string MotivoLegislacao(Exception e) => e switch
    {
        ArgumentException or InvalidOperationException => e.Message,
        KeyNotFoundException => "Esta lei não está mais anexada.",
        _ => Erro(e),
    };

    bool PodeAlterarLegislacao()
    {
        if (!legislacao.Acervo.SomenteLeitura) return true;
        MostrarAvisoLegislacao(InfoBarSeverity.Warning, "Legislação só para leitura", MensagensAnexo.SomenteLeitura);
        return false;
    }

    void ComecarProgressoLegislacao()
    {
        var a = PaginaDeAjustes;
        a.AvisoLegislacao.IsOpen = false;
        a.TextoProgressoLegislacao.Text = "Preparando…";
        a.BarraProgressoLegislacao.IsIndeterminate = true;
        a.ProgressoLegislacao.Visibility = Visibility.Visible;
        a.BotaoAnexarLegislacao.IsEnabled = false;
        a.ConteudoAnexos.IsEnabled = false;
        var l = PaginaDeLegislacao;
        l.AvisoMensagem.IsOpen = false;
        l.TextoProgressoAnexo.Text = "Preparando…";
        l.BarraProgressoAnexo.IsIndeterminate = true;
        l.ProgressoAnexo.Visibility = Visibility.Visible;
    }

    /// <summary>"Anexando “LEI 1.382-2020.pdf” — página 12 de 81".</summary>
    void MostrarProgressoLegislacao(string acao, ProgressoAnexo p)
    {
        if (fechada) return;
        var texto = p.Total > 0 ? $"{acao} “{p.Nome}” — página {p.Pagina} de {p.Total}" : $"{acao} “{p.Nome}”…";
        foreach (var (rotulo, barra) in new[] { (PaginaDeAjustes.TextoProgressoLegislacao, PaginaDeAjustes.BarraProgressoLegislacao),
                                                (PaginaDeLegislacao.TextoProgressoAnexo, PaginaDeLegislacao.BarraProgressoAnexo) })
        {
            rotulo.Text = texto;
            barra.IsIndeterminate = p.Total <= 0;
            if (p.Total > 0) { barra.Maximum = p.Total; barra.Value = p.Pagina; }
        }
    }

    /// <summary>O andamento da anexação/substituição em curso, como aparece no módulo Legislação ("" sem nenhuma).</summary>
    internal string ProgressoNaLegislacao =>
        PaginaDeLegislacao.ProgressoAnexo.Visibility == Visibility.Visible ? PaginaDeLegislacao.TextoProgressoAnexo.Text : "";

    void TerminarProgressoLegislacao(CancellationTokenSource cts)
    {
        if (ReferenceEquals(cancelarAnexo, cts)) cancelarAnexo = null;
        cts.Dispose();
        if (fechada) return;
        PaginaDeAjustes.ProgressoLegislacao.Visibility = Visibility.Collapsed;
        PaginaDeLegislacao.ProgressoAnexo.Visibility = Visibility.Collapsed;
    }
}
