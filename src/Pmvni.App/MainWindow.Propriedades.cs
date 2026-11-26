using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pmvni.Composicao.Kit;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O painel Propriedades do relatório (correções 2026-11-16): seções em cartões com cabeçalho — ESTADO (no PDF, situação,
/// justificativa, IA), ESQUADRIAS (só numa linha de ambiente: janelas e portas com largura × altura editáveis e a conta em tempo real),
/// CONTEÚDO (os textos, editáveis aqui mesmo) e HISTÓRICO. A parte fixa está em PaginaRelatorios.xaml; o que depende do item é montado
/// aqui com as peças de <see cref="CartoesDePainel"/>. O painel é atualizado depois de cada comando: quando a estrutura não mudou (o
/// mesmo item, os mesmos campos), os valores são trocados no lugar, sem recriar as caixas — quem está digitando em outra caixa não
/// perde o foco nem o texto.</summary>
public sealed partial class MainWindow
{
    /// <summary>Um campo de texto do cartão Conteúdo.</summary>
    sealed record CampoNoPainel(TextBox Caixa, Border Marca, Button Restaurar, TextBlock? Original, CheckBox? Visibilidade);

    readonly Dictionary<string, CampoNoPainel> camposNoPainel = new(StringComparer.Ordinal);
    string assinaturaCampos = "";

    // ---------- acessores do roteiro ----------
    internal IReadOnlyCollection<string> CamposNoPainel => camposNoPainel.Keys;
    internal string? TextoDoCampoNoPainel(string campo) => camposNoPainel.TryGetValue(campo, out var c) ? c.Caixa.Text : null;
    internal bool EsquadriasVisiveis => R.PropEsquadrias.Visibility == Visibility.Visible;
    internal string TextoDoVaoTotal => esqTotal?.Text ?? "";
    internal string TextoDoResultadoDasEsquadrias => esqResultado?.Text ?? "";

    bool TemFoco(Control? c) => c is not null && Raiz.XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(Raiz.XamlRoot), c);

    /// <summary>O painel Propriedades foi recolhido pelo app (janela estreita, nada selecionado), não pelo usuário: volta sozinho na seleção.</summary>
    bool propriedadesRecolhidasSozinhas;
    /// <summary>O usuário abriu o painel sem nada selecionado: o app não o recolhe de novo até a próxima seleção.</summary>
    bool propriedadesPedidasSemSelecao;

    /// <summary>A largura da lista de seções antes de o app a estreitar (null = não estreitou): volta quando a janela fica larga de novo.</summary>
    double? larguraNavegacaoAntes;

    /// <summary>Há um item ou uma linha de tabela (ambiente, área) selecionada nas folhas.</summary>
    bool HaSelecaoNasPropriedades => sessao?.Atual is not null && (ItemSelecionado is not null || LinhaSelecionada is not null);

    /// <summary>Auditoria de interface (2026-11-17): em janelas estreitas (notebook 1366 px) o painel Propriedades aberto e vazio deixava a
    /// folha a 25%. Sem nada selecionado ele recolhe; ao selecionar um item ou uma linha, volta (se o usuário o deixou ligado). A lista de
    /// seções também estreita (no máximo 30% da janela) e volta à largura de antes quando a janela fica larga.</summary>
    void AjustarPropriedadesALargura()
    {
        var largura = Raiz.ActualWidth;
        if (largura <= 0) return;
        var estreita = largura < 1400;
        var teto = Math.Max(280, Math.Round(largura * 0.3));
        if (estreita && R.PainelNavegacao.Width > teto)
        {
            larguraNavegacaoAntes ??= R.PainelNavegacao.Width;
            R.PainelNavegacao.Width = teto;
        }
        else if (!estreita && larguraNavegacaoAntes is { } antes)
        {
            R.PainelNavegacao.Width = antes;
            larguraNavegacaoAntes = null;
        }
        var temSelecao = HaSelecaoNasPropriedades;
        if (temSelecao) propriedadesPedidasSemSelecao = false;
        if (!temSelecao && estreita && !propriedadesPedidasSemSelecao && R.PainelPropriedades.Visibility == Visibility.Visible)
        {
            R.PainelPropriedades.Visibility = Visibility.Collapsed;
            propriedadesRecolhidasSozinhas = true;
        }
        else if (propriedadesRecolhidasSozinhas && (temSelecao || !estreita))
        {
            R.PainelPropriedades.Visibility = Visibility.Visible;
            propriedadesRecolhidasSozinhas = false;
        }
    }

    /// <summary>Propriedades do item ou da linha selecionada.</summary>
    void AtualizarPropriedades()
    {
        if (fechada) return;
        atualizandoPropriedades = true;
        try
        {
            var p = sessao?.Revisao.Projeto;
            var it = sessao?.Atual is null ? null : ItemSelecionado;
            R.PropTextosOcultos.Visibility = p is not null &&
                (TextosDoRelatorio?.Values.Any(t => !t.Visivel) == true || p.Figuras?.Any(f => !f.Incluida) == true ||
                    TextosRevisados.CamposOcultos(p).Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;
            AjustarPropriedadesALargura(); // antes do retorno da linha de tabela: escolher um ambiente também reabre o painel
            if (textoModeloSelecionado is { } chave && TextosDoRelatorio?.GetValueOrDefault(chave) is { } texto)
            {
                MontarPropriedadesTextoDoModelo(chave, texto);
                return;
            }
            if (p is not null && FiguraParaPropriedades(p, figuraSelecionada) is { } figura)
            {
                MontarPropriedadesFigura(p, figura);
                return;
            }
            if (p is not null && sessao?.Atual is not null && LinhaSelecionada is { } linha)
            {
                MontarPropriedadesLinha(p, linha);
                return;
            }
            if (p is null || it is null)
            {
                itemNasPropriedades = null;
                assinaturaCampos = assinaturaEsquadrias = "";
                camposNoPainel.Clear();
                R.PropTitulo.Text = "Propriedades";
                R.PropId.Text = "";
                R.PropVazio.Visibility = Visibility.Visible;
                R.PropRolagem.Visibility = Visibility.Collapsed;
                return;
            }
            var editavel = Editavel;
            R.PropVazio.Visibility = Visibility.Collapsed;
            R.PropRolagem.Visibility = Visibility.Visible;
            R.PropTitulo.Text = p.NumeroAtual(it.Id) is { } n ? $"Item {n:00}" : it.Criado ? "Item novo excluído" : $"Item excluído (era o {it.NumeroOriginal:00})";
            // O código interno do item só na dica (auditoria de interface: "it_01M7…" à vista não dizia nada ao analista).
            R.PropId.Text = it.Criado ? "criado na revisão" : "";
            ToolTipService.SetToolTip(R.PropTitulo, "Código interno: " + it.Id);
            R.PropNoPdf.IsChecked = it.Incluido;
            R.PropNoPdf.IsEnabled = editavel;

            var efetiva = SituacaoEfetiva(it);
            R.PropSituacao.SelectedIndex = efetiva is null ? -1 : Faixas.OrdemSituacoes.ToList().IndexOf(efetiva);
            R.PropSituacao.IsEnabled = editavel && it.Incluido;
            var original = it.Situacao.Original is JsonValue v && v.TryGetValue<string>(out var so) ? so : null;
            R.PropSituacaoOriginal.Text = it.Criado ? "item criado na revisão (sem original)"
                : "original: " + TextoSituacao(original) + (it.Situacao.Revisada is null ? " (sem revisão)" : " · revisada");

            // A justificativa que o usuário está digitando (com o foco na caixa) não é trocada pelo valor gravado do mesmo item.
            var digitando = itemNasPropriedades == it.Id && TemFoco(R.PropJustificativa);
            if (!digitando) R.PropJustificativa.Text = it.Situacao.Justificativa ?? "";
            R.PropJustificativa.IsEnabled = editavel;
            R.PropJustificativaAviso.Visibility = !it.Criado && it.Situacao.Revisada is not null && string.IsNullOrWhiteSpace(it.Situacao.Justificativa) ? Visibility.Visible : Visibility.Collapsed;
            // Correções 2026-11-16: o botão da IA existe em todo item incluído (antes só com a situação revisada); a justificativa é a
            // instrução. Quando ela (ou a situação) muda depois dos textos, o aviso pede o reajuste — nada dispara sozinho.
            R.PropAjustarTexto.Visibility = it.Incluido ? Visibility.Visible : Visibility.Collapsed;
            R.PropAjustarRotulo.Text = "Ajustar o item com a IA";
            R.PropAjustarIcone.Glyph = Glifos.Ia;
            Dicas.SetDescricao(R.PropAjustarTexto, "A IA reajusta o item inteiro (verificação, providência, base legal, referência e, se a justificativa pedir, a situação), com o texto curto como o original. Um Desfazer volta tudo.");
            R.PropAjustarTexto.IsEnabled = editavel && it.Incluido && !ajustandoComIa;
            R.PropAjustarAviso.Text = "A justificativa mudou: reajuste o item (o botão abaixo) ou edite os textos.";
            R.PropAjustarAviso.Visibility = it.Incluido && ReajustePendente(p, it.Id, it.Situacao.Justificativa) ? Visibility.Visible : Visibility.Collapsed;
            itemNasPropriedades = it.Id;

            R.PropEsquadrias.Visibility = Visibility.Collapsed;
            assinaturaEsquadrias = "";
            var id = it.Id;
            MontarCampos(id, Campos.Editaveis.Select((c, k) => (c, NomesCampos[k])).ToList(), editavel && it.Incluido,
                efetivo: c => DadosEfetivos.TextoEfetivo(p, it, c) ?? "",
                original: c => !it.Campos.TryGetValue(c, out var ct) || ct.Ausente ? (it.Criado ? "(item criado na revisão)" : "(ausente no original)")
                    : ct.Original is JsonValue ov && ov.TryGetValue<string>(out var os) ? Citacoes.Imprimir(os, x => p.NumeroAtual(x), x => p.Item(x)?.NumeroOriginal)
                    : "(sem texto)",
                alterado: c => !it.Criado && it.Campos.TryGetValue(c, out var ct) && ct.Revisado is not null,
                dicaRestaurar: "Volta este campo ao texto original recebido da IA.");
            R.PropRestaurarOriginal.IsEnabled = editavel && !it.Criado && TemRevisao(it);
            AtualizarDicasDasAcoesDePropriedades(it.Criado ? "Este item foi criado na revisão." : null);
            MontarHistoricoDoPainel(p, it.Id);
        }
        finally { atualizandoPropriedades = false; }
    }

    /// <summary>Propriedades de uma linha de tabela: rótulo e nome, situação (original e revisada), justificativa, IA, as esquadrias (num
    /// ambiente), cada texto da linha e o histórico dela. "No PDF" fica marcado e desligado: a linha não se exclui.</summary>
    void MontarPropriedadesLinha(Projeto p, string linha)
    {
        var editavel = Editavel;
        R.PropVazio.Visibility = Visibility.Collapsed;
        R.PropRolagem.Visibility = Visibility.Visible;
        R.PropTitulo.Text = TabelasRevisao.Rotulo(linha);
        R.PropId.Text = TabelasRevisao.TextoEfetivo(p, linha, "nome") is { Length: > 0 } nome ? nome + " · " + linha : linha;
        R.PropNoPdf.IsChecked = TextosRevisados.CampoVisivel(p, linha, "");
        R.PropNoPdf.IsEnabled = editavel && !RoteiroLegado &&
            TabelasRevisao.TentarLer(linha, out var bloco, out _) && bloco is TabelasRevisao.Ambientes or TabelasRevisao.Indices or TabelasRevisao.Chamadas;

        var efetiva = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(p, linha));
        R.PropSituacao.SelectedIndex = efetiva is null ? -1 : Faixas.OrdemSituacoes.ToList().IndexOf(efetiva);
        R.PropSituacao.IsEnabled = editavel;
        p.Linhas.TryGetValue(linha, out var rev);
        R.PropSituacaoOriginal.Text = "original: " + TextoSituacao(TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoOriginal(p, linha)) ?? TabelasRevisao.SituacaoOriginal(p, linha))
                                      + (rev?.Situacao is null ? " (sem revisão)" : " · revisada");

        var digitando = itemNasPropriedades == linha && TemFoco(R.PropJustificativa);
        if (!digitando) R.PropJustificativa.Text = rev?.Justificativa ?? "";
        R.PropJustificativa.IsEnabled = editavel;
        R.PropJustificativaAviso.Visibility = rev is not null && TabelasRevisao.FaltaJustificativa(rev) ? Visibility.Visible : Visibility.Collapsed;
        R.PropAjustarTexto.Visibility = Visibility.Visible;
        R.PropAjustarTexto.IsEnabled = editavel && !ajustandoComIa;
        var pendente = ReajustePendente(p, linha, rev?.Justificativa);
        R.PropAjustarRotulo.Text = "Rever com a IA";
        R.PropAjustarIcone.Glyph = Glifos.Ia;
        Dicas.SetDescricao(R.PropAjustarTexto, "A IA revê a tabela a partir desta linha e considera a justificativa que você escreveu. Você pode desfazer a revisão.");
        R.PropAjustarAviso.Text = "A justificativa mudou. Você pode rever a linha com a IA ou editar os textos.";
        R.PropAjustarAviso.Visibility = pendente ? Visibility.Visible : Visibility.Collapsed;
        itemNasPropriedades = linha;

        // Num ambiente, as esquadrias (vãos) e a conta delas ficam no cartão próprio; o Conteúdo fica com os outros textos.
        var ambiente = MontarEsquadrias(p, linha, editavel);
        var caminhos = TabelasRevisao.Caminhos(TabelasRevisao.Original(p, linha)!)
            .Where(c => !ambiente || !(c.StartsWith("vaos/", StringComparison.Ordinal) || c.StartsWith("resultado/", StringComparison.Ordinal)))
            .Select(c => (c, TabelasRevisao.NomeDoCampo(c))).ToList();
        MontarCampos(linha, caminhos, editavel,
            efetivo: c => TabelasRevisao.TextoEfetivo(p, linha, c) ?? "",
            original: c => TabelasRevisao.TextoOriginal(p, linha, c) ?? "",
            alterado: c => rev?.Campos.ContainsKey(c) == true,
            dicaRestaurar: "Volta este texto ao original recebido da IA.");
        R.PropRestaurarOriginal.IsEnabled = editavel && rev?.Revisada == true;
        AtualizarDicasDasAcoesDePropriedades();
        MontarHistoricoDoPainel(p, linha);
    }

    void AtualizarDicasDasAcoesDePropriedades(string? motivoOriginal = null)
    {
        void Atualizar(Button botao, string nome, string descricao, string? motivo)
        {
            var dica = DicasComandos.Livre(nome, descricao, motivo: botao.IsEnabled ? null : motivo);
            if (Dicas.De(botao) != dica) Dicas.Aplicar(botao, dica);
        }

        var bloqueio = Editavel ? null : MotivoRevisaoIndisponivel();
        var motivoIa = ajustandoComIa ? "Já há uma consulta à IA em andamento." : bloqueio;
        Atualizar(R.PropAjustarTexto, R.PropAjustarRotulo.Text, Dicas.GetDescricao(R.PropAjustarTexto), motivoIa);
        Atualizar(R.PropAjustarCancelar, "Cancelar", Dicas.GetDescricao(R.PropAjustarCancelar),
            "O cancelamento já foi solicitado.");
        if (!ajustandoComIa)
            Atualizar(R.PropRestaurarOriginal, "Restaurar original", Dicas.GetDescricao(R.PropRestaurarOriginal),
                bloqueio ?? motivoOriginal ?? "Não há alterações para restaurar.");
    }

    void MontarHistoricoDoPainel(Projeto p, string id)
    {
        R.PropHistorico.Children.Clear();
        var registros = sessao!.Revisao.HistoricoCompleto.Where(r => r.Item == id).Reverse().ToList();
        if (registros.Count == 0) R.PropHistorico.Children.Add(CartoesDePainel.Apoio("Sem alterações."));
        foreach (var r in registros)
        {
            var linha = new Grid { ColumnSpacing = 8 };
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var hora = CartoesDePainel.Apoio(Hora(r.DataUtc));
            hora.Margin = new Thickness(0, 2, 0, 0);
            linha.Children.Add(hora);
            var texto = new TextBlock { Text = DescricaoHistorico.Texto(r, p), TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(texto, 1);
            linha.Children.Add(texto);
            R.PropHistorico.Children.Add(linha);
        }
    }

    // ---------- CONTEÚDO: os campos de texto, editáveis no painel ----------

    /// <summary>Os campos do cartão Conteúdo: o rótulo (com "alterado" e Restaurar, quando revisado), a caixa com o texto — editável aqui
    /// mesmo: Enter ou sair da caixa aplica, Esc desiste; passa pela mesma rota da edição na folha (<see cref="EditarTextoAsync"/>), com
    /// o recálculo e o Desfazer de sempre — e, com Original ⇄ ligado, o texto original embaixo.</summary>
    void MontarCampos(string id, IReadOnlyList<(string Campo, string Nome)> campos, bool editavel, Func<string, string> efetivo, Func<string, string> original,
        Func<string, bool> alterado, string dicaRestaurar, Func<string, Task>? restaurarCampo = null)
    {
        var assinatura = id + "|" + editavel + "|" + mostrarOriginal + "|" + string.Join(",", campos.Select(c => c.Campo));
        if (assinatura != assinaturaCampos)
        {
            assinaturaCampos = assinatura;
            camposNoPainel.Clear();
            R.PropCampos.Children.Clear();
            foreach (var (campo, nome) in campos)
            {
                var caixa = CartoesDePainel.Caixa("", nome, editavel);
                var marca = CartoesDePainel.Marca("alterado");
                var restaurar = CartoesDePainel.BotaoPequeno(Glifos.Restaurar, "Restaurar", "Restaurar o campo " + nome, dicaRestaurar);
                restaurar.Click += async (_, _) => await TentarAsync(() => restaurarCampo?.Invoke(campo) ?? AplicarAsync(new RestaurarCampo(id, campo)));
                caixa.KeyDown += async (_, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await AplicarCampoDoPainelAsync(id, campo, caixa); }
                    else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; caixa.Text = caixa.Tag as string ?? ""; }
                };
                caixa.LostFocus += async (_, _) => await AplicarCampoDoPainelAsync(id, campo, caixa);
                var bloco = CartoesDePainel.Campo(nome, caixa, marca, restaurar);
                CheckBox? visibilidade = null;
                if (editavel && !RoteiroLegado)
                {
                    var ia = CartoesDePainel.BotaoPequeno(Glifos.Ia, "Editar com a IA", "Editar " + nome + " com a IA",
                        "Aplica a resposta ao campo. A alteração pode ser desfeita.");
                    ia.Click += async (_, _) => await TentarAsync(() => EditarCampoComIaAsync(id, campo, nome));
                    bloco.Children.Add(ia);
                    if (id.StartsWith("grupo/", StringComparison.Ordinal) && TextosDoRelatorio?.GetValueOrDefault(campo) is { } membro)
                    {
                        visibilidade = new CheckBox { Content = "Incluir este texto", IsChecked = membro.Visivel };
                        var controle = visibilidade;
                        controle.Click += async (_, _) =>
                        {
                            if (TextosDoRelatorio?.GetValueOrDefault(campo) is { } atual)
                                await TentarAsync(() => AplicarAsync(new DefinirTextoDoModelo(campo, atual.Atual == atual.Original ? null : atual.Atual, controle.IsChecked == true)));
                        };
                        bloco.Children.Add(controle);
                    }
                    else if (campo.StartsWith("marcacao/", StringComparison.Ordinal))
                    {
                        var grupo = campo[..campo.LastIndexOf('/')];
                        if (campo.EndsWith("/elemento", StringComparison.Ordinal) || !campos.Any(c => c.Campo == grupo + "/elemento"))
                        {
                            var gravada = sessao?.Revisao.Projeto is { } proj && FiguraParaPropriedades(proj, id)?.Buscar(grupo[9..])?.Gravada == true;
                            visibilidade = ControleVisibilidade(id, grupo, nome, gravada ? "Incluir legenda (marca gravada na imagem)" : "Incluir nota e marcação na imagem");
                            bloco.Children.Add(visibilidade);
                        }
                    }
                    else if (!id.StartsWith("modelo/", StringComparison.Ordinal))
                    {
                        visibilidade = ControleVisibilidade(id, campo, nome);
                        bloco.Children.Add(visibilidade);
                    }
                }
                TextBlock? textoOriginal = null;
                if (mostrarOriginal)
                {
                    // Original ⇄: o original embaixo do atual (o painel é estreito para lado a lado), com as citações numeradas como hoje.
                    textoOriginal = CartoesDePainel.Valor("");
                    var blocoOriginal = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 0) };
                    blocoOriginal.Children.Add(CartoesDePainel.Apoio("Original"));
                    textoOriginal.Opacity = 0.75;
                    blocoOriginal.Children.Add(textoOriginal);
                    bloco.Children.Add(blocoOriginal);
                }
                camposNoPainel[campo] = new CampoNoPainel(caixa, marca, restaurar, textoOriginal, visibilidade);
                R.PropCampos.Children.Add(bloco);
            }
        }
        // Os valores, no lugar: a caixa em que o usuário está digitando fica como está.
        foreach (var (campo, _) in campos)
        {
            var c = camposNoPainel[campo];
            var texto = efetivo(campo);
            c.Caixa.Tag = texto;
            if (!TemFoco(c.Caixa)) c.Caixa.Text = texto;
            var mudou = alterado(campo);
            c.Marca.Visibility = mudou ? Visibility.Visible : Visibility.Collapsed;
            c.Restaurar.Visibility = mudou ? Visibility.Visible : Visibility.Collapsed;
            c.Restaurar.IsEnabled = editavel;
            if (c.Original is not null) c.Original.Text = original(campo);
            if (c.Visibilidade is { } v && sessao?.Revisao.Projeto is { } p)
                v.IsChecked = id.StartsWith("grupo/", StringComparison.Ordinal) ? TextosDoRelatorio?.GetValueOrDefault(campo)?.Visivel
                    : TextosRevisados.CampoVisivel(p, id, campo.StartsWith("marcacao/", StringComparison.Ordinal) ? campo[..campo.LastIndexOf('/')] : campo);
        }
    }

    /// <summary>Aplica o texto digitado numa caixa do Conteúdo (se mudou). Recusado (texto vazio, conferência): a caixa volta ao gravado.</summary>
    async Task AplicarCampoDoPainelAsync(string id, string campo, TextBox caixa)
    {
        if (atualizandoPropriedades || caixa.IsReadOnly || !Editavel) return;
        var gravado = caixa.Tag as string ?? "";
        var texto = caixa.Text;
        if (TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(gravado)) return;
        caixa.Tag = texto; // já a caminho: sair da caixa logo depois do Enter não aplica duas vezes
        await TentarAsync(() => EditarTextoAsync(id, campo, texto));
        if (ultimosErros.Count > 0 && camposNoPainel.TryGetValue(campo, out var c) && ReferenceEquals(c.Caixa, caixa))
            caixa.Text = caixa.Tag as string ?? gravado;   // a atualização do painel já pôs em Tag o texto que ficou valendo
    }

    // ---------- ESQUADRIAS: janelas e portas do ambiente, com a conta em tempo real ----------

    /// <summary>Uma esquadria no painel: as caixas do código, do tipo, da largura e da altura (as duas últimas só com medidas legíveis) e
    /// o texto da área.</summary>
    sealed class EsquadriaNoPainel
    {
        public int Indice;
        public TextBox Codigo = null!, Tipo = null!;
        public TextBox? Largura, Altura, Medidas;
        public TextBlock Area = null!;
        public TextBlock Nota = null!;            // o texto do alerta (embaixo das medidas)
        public FrameworkElement NotaBloco = null!; // o alerta inteiro (ícone + texto): aparece só quando há o que avisar
    }

    readonly List<EsquadriaNoPainel> esquadriasNoPainel = [];
    string assinaturaEsquadrias = "";
    string? ambienteDasEsquadrias, caminhoDoMinimo;
    TextBlock? esqTotal, esqConta, esqMinimo, esqResultado, esqAviso;
    TextBox? esqPiso, esqDivisor;

    static string Num(decimal v) => Esquadrias.TextoDoNumero(v);

    /// <summary>Monta (ou atualiza no lugar) o cartão Esquadrias da linha; false se a linha não é um ambiente.</summary>
    bool MontarEsquadrias(Projeto p, string linha, bool editavel)
    {
        if (Esquadrias.Calcular(p, linha) is not { } conta)
        {
            R.PropEsquadrias.Visibility = Visibility.Collapsed;
            assinaturaEsquadrias = "";
            ambienteDasEsquadrias = null;
            return false;
        }
        R.PropEsquadrias.Visibility = Visibility.Visible;
        ambienteDasEsquadrias = linha;
        caminhoDoMinimo = Esquadrias.CaminhoDaContaDoMinimo(p, linha);
        var ultimo = conta.Esquadrias.Count - 1;
        var assinatura = linha + "|" + editavel + "|" + (caminhoDoMinimo ?? "-") + "|"
                         + string.Join(";", conta.Esquadrias.Select(e => $"{e.Indice},{e.Categoria},{e.Acrescentada},{e.Largura is not null},{e.TemCampoDeArea}"));
        assinatura += "|" + string.Join(",", TextosRevisados.CamposOcultos(p).Where(c => c.Alvo == linha).Select(c => c.Campo));
        if (assinatura != assinaturaEsquadrias)
        {
            assinaturaEsquadrias = assinatura;
            esquadriasNoPainel.Clear();
            var corpo = R.PropEsquadriasCorpo;
            corpo.Children.Clear();
            foreach (var (categoria, titulo) in new[] { (CategoriaEsquadria.Janela, "Janelas"), (CategoriaEsquadria.Porta, "Portas"), (CategoriaEsquadria.Outro, "Outros vãos") })
            {
                var doGrupo = conta.Esquadrias.Where(e => e.Categoria == categoria).ToList();
                if (doGrupo.Count == 0 && categoria == CategoriaEsquadria.Outro) continue;
                // Cabeçalho do grupo: o nome e, colado nele, a quantidade num selo; as esquadrias; e o botão de acrescentar do grupo.
                var cabecalhoGrupo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var tituloGrupo = CartoesDePainel.Rotulo(titulo);
                tituloGrupo.VerticalAlignment = VerticalAlignment.Center;
                cabecalhoGrupo.Children.Add(tituloGrupo);
                var selo = CartoesDePainel.Marca(doGrupo.Count.ToString(CultureInfo.InvariantCulture));
                selo.VerticalAlignment = VerticalAlignment.Center;
                cabecalhoGrupo.Children.Add(selo);
                var grupo = new StackPanel { Spacing = 8 };
                grupo.Children.Add(cabecalhoGrupo);
                foreach (var e in doGrupo) grupo.Children.Add(LinhaDaEsquadria(linha, e, editavel, e.Indice == ultimo));
                if (editavel && categoria != CategoriaEsquadria.Outro) grupo.Children.Add(BotaoAdicionarEsquadria(linha, categoria));
                corpo.Children.Add(grupo);
                corpo.Children.Add(CartoesDePainel.Separador());
            }
            // A conta: vão total, mínimo exigido (piso ÷ divisor) e resultado, refeitos a cada tecla.
            esqTotal = CartoesDePainel.Valor("", forte: true);
            esqConta = CartoesDePainel.Apoio("");
            esqMinimo = CartoesDePainel.Valor("", forte: true);
            esqResultado = new TextBlock();
            esqAviso = CartoesDePainel.Apoio("");
            corpo.Children.Add(CartoesDePainel.Linha("Vão total", esqTotal));
            corpo.Children.Add(esqConta);
            esqPiso = esqDivisor = null;
            if (conta.Piso is not null && conta.Divisor is not null && caminhoDoMinimo is not null)
            {
                esqPiso = CartoesDePainel.CaixaDeNumero("", "Área do piso, em metros quadrados", editavel);
                esqDivisor = CartoesDePainel.CaixaDeNumero("", "Divisor da área do piso", editavel, 48);
                var contaMinimo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                contaMinimo.Children.Add(esqPiso);
                contaMinimo.Children.Add(new TextBlock { Text = "m² ÷", VerticalAlignment = VerticalAlignment.Center });
                contaMinimo.Children.Add(esqDivisor);
                contaMinimo.Children.Add(new TextBlock { Text = "=", VerticalAlignment = VerticalAlignment.Center });
                esqMinimo.VerticalAlignment = VerticalAlignment.Center;
                contaMinimo.Children.Add(esqMinimo);
                corpo.Children.Add(CartoesDePainel.Campo("Mínimo exigido (piso ÷ divisor)", contaMinimo));
                foreach (var caixa in new[] { esqPiso, esqDivisor })
                {
                    caixa.TextChanged += (_, _) => AtualizarContaDasEsquadrias();
                    caixa.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await AplicarMinimoAsync(); } };
                    caixa.LostFocus += (_, _) => AoSairDe(() => !TemFoco(esqPiso) && !TemFoco(esqDivisor), AplicarMinimoAsync);
                }
            }
            else corpo.Children.Add(CartoesDePainel.Linha("Mínimo exigido", esqMinimo));
            corpo.Children.Add(CartoesDePainel.Linha("Resultado", esqResultado));
            corpo.Children.Add(esqAviso);
            if (editavel && TabelasRevisao.ProximoAmbiente(p) is not null)
            {
                corpo.Children.Add(CartoesDePainel.Separador());
                corpo.Children.Add(BotaoNovoAmbiente(conta.Divisor));
            }
        }
        // Os valores gravados, no lugar (a caixa com o foco fica como está).
        foreach (var n in esquadriasNoPainel)
        {
            var e = conta.Esquadrias[n.Indice];
            Por(n.Codigo, e.Codigo);
            Por(n.Tipo, e.Tipo);
            if (n.Largura is not null && e.Largura is { } l && e.Altura is { } a) { Por(n.Largura, Num(l)); Por(n.Altura!, Num(a)); }
            if (n.Medidas is not null) Por(n.Medidas, e.Medidas);
        }
        if (esqPiso is not null && conta.Piso is { } piso && conta.Divisor is { } divisor)
        {
            Por(esqPiso, Num(piso));
            Por(esqDivisor!, divisor.ToString("0.##", Formatos.Cultura));
        }
        AtualizarContaDasEsquadrias();
        return true;

        void Por(TextBox caixa, string texto)
        {
            caixa.Tag = texto;
            if (!TemFoco(caixa)) caixa.Text = texto;
        }
    }

    /// <summary>Depois de uma caixa perder o foco: se o foco saiu do grupo dela (não foi para a caixa vizinha), aplica.</summary>
    void AoSairDe(Func<bool> saiuDoGrupo, Func<Task> aplicar) =>
        DispatcherQueue.TryEnqueue(async () => { if (!fechada && saiuDoGrupo()) await aplicar(); });

    /// <summary>Uma esquadria (redesenho 2026-11-16): bloco com o código num selo de destaque, o tipo como título e a área à direita, em
    /// destaque; embaixo, só a largura × altura (caixas compactas com legenda) e, quando preciso, um alerta com ícone. Tudo editável no
    /// lugar: código, tipo e medidas (a conta se refaz a cada tecla).</summary>
    FrameworkElement LinhaDaEsquadria(string linha, Esquadria e, bool editavel, bool ultima)
    {
        var res = Application.Current.Resources;
        var n = new EsquadriaNoPainel { Indice = e.Indice };
        var ordinal = (e.Indice + 1).ToString(CultureInfo.InvariantCulture);

        n.Codigo = CartoesDePainel.Caixa("", $"Código da esquadria {ordinal}", editavel);
        n.Codigo.TextWrapping = TextWrapping.NoWrap;
        n.Codigo.Width = 46;
        n.Codigo.MinWidth = 0;
        n.Codigo.MinHeight = 0;
        n.Codigo.Height = 32;   // selo, tipo e área numa linha só, centrados na mesma altura (antes cada um tinha a sua)
        n.Codigo.VerticalContentAlignment = VerticalAlignment.Center;
        n.Codigo.Padding = new Thickness(5, 5, 0, 0);   // a caixa do WinUI põe o texto no topo: o espaço de cima o centra nos 32
        n.Codigo.TextAlignment = TextAlignment.Left;
        n.Codigo.CornerRadius = new CornerRadius(6);
        n.Codigo.BorderThickness = new Thickness(0);
        n.Codigo.Background = (Microsoft.UI.Xaml.Media.Brush)res["AccentFillColorDefaultBrush"];
        n.Codigo.Foreground = (Microsoft.UI.Xaml.Media.Brush)res["TextOnAccentFillColorPrimaryBrush"];
        n.Codigo.VerticalAlignment = VerticalAlignment.Center;

        n.Tipo = CartoesDePainel.Caixa("", $"Tipo da esquadria {ordinal}", editavel);
        n.Tipo.TextWrapping = TextWrapping.NoWrap;
        n.Tipo.MinWidth = 0;
        n.Tipo.BorderThickness = new Thickness(0);
        n.Tipo.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        n.Tipo.Padding = new Thickness(6, 5, 6, 0);
        n.Tipo.MinHeight = 0;
        n.Tipo.Height = 32;
        n.Tipo.VerticalContentAlignment = VerticalAlignment.Center;
        n.Tipo.VerticalAlignment = VerticalAlignment.Center;

        n.Area = new TextBlock { Style = (Style)res["PmvniPainelValorForte"], HorizontalAlignment = HorizontalAlignment.Right };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(n.Area, $"Área da esquadria {ordinal}");

        foreach (var (caixa, parte) in new[] { (n.Codigo, "codigo"), (n.Tipo, "tipo") })
        {
            caixa.KeyDown += async (_, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter) { ev.Handled = true; await AplicarTextoDaEsquadriaAsync(linha, e.Indice, parte, caixa); }
                else if (ev.Key == Windows.System.VirtualKey.Escape) { ev.Handled = true; caixa.Text = caixa.Tag as string ?? ""; }
            };
            caixa.LostFocus += async (_, _) => await AplicarTextoDaEsquadriaAsync(linha, e.Indice, parte, caixa);
        }

        var grade = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < 5; r++) grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Por(FrameworkElement el, int linhaGrade, int coluna, int vao = 1)
        {
            Grid.SetRow(el, linhaGrade);
            Grid.SetColumn(el, coluna);
            Grid.SetColumnSpan(el, vao);
            grade.Children.Add(el);
        }
        Por(n.Codigo, 0, 0);
        Por(n.Tipo, 0, 1);
        Por(n.Area, 0, 2);

        // Medidas: "largura × altura m", com a legenda pequena em cima de cada caixa; alinhadas com o tipo (o selo fica sozinho à esquerda).
        TextBlock Legenda(string s) => CartoesDePainel.Rotulo(s);   // LARGURA / ALTURA em maiúsculas, como os outros rótulos do painel
        TextBlock Sinal(string s) => new() { Text = s, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7), Foreground = (Microsoft.UI.Xaml.Media.Brush)res["TextFillColorSecondaryBrush"] };
        var medidas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(6, 0, 0, 0) };
        if (e.Largura is not null)
        {
            n.Largura = CartoesDePainel.CaixaDeNumero("", $"Largura da esquadria {ordinal}, em metros", editavel, 64);
            n.Altura = CartoesDePainel.CaixaDeNumero("", $"Altura da esquadria {ordinal}, em metros", editavel, 64);
            StackPanel ComLegenda(string legenda, TextBox caixa) { var s = new StackPanel { Spacing = 2 }; s.Children.Add(Legenda(legenda)); s.Children.Add(caixa); return s; }
            medidas.Children.Add(ComLegenda("largura", n.Largura));
            medidas.Children.Add(Sinal("×"));
            medidas.Children.Add(ComLegenda("altura", n.Altura));
            medidas.Children.Add(Sinal("m"));
            foreach (var caixa in new[] { n.Largura, n.Altura })
            {
                caixa.TextChanged += (_, _) => AtualizarContaDasEsquadrias();   // tempo real: nada é gravado enquanto digita
                caixa.GotFocus += (_, _) => caixa.SelectAll();
                caixa.KeyDown += async (_, ev) =>
                {
                    if (ev.Key == Windows.System.VirtualKey.Enter) { ev.Handled = true; await AplicarMedidasAsync(linha, n); }
                    else if (ev.Key == Windows.System.VirtualKey.Escape) { ev.Handled = true; n.Largura.Text = n.Largura.Tag as string ?? ""; n.Altura.Text = n.Altura.Tag as string ?? ""; }
                };
                caixa.LostFocus += (_, _) => AoSairDe(() => !TemFoco(n.Largura) && !TemFoco(n.Altura), () => AplicarMedidasAsync(linha, n));
            }
        }
        else
        {
            // Medidas que não são "largura × altura": o texto inteiro, editável (escrito como 0,80 × 2,10 m, vira conta).
            n.Medidas = CartoesDePainel.Caixa("", $"Medidas da esquadria {ordinal}", editavel);
            n.Medidas.PlaceholderText = "largura × altura";
            n.Medidas.MinWidth = 160;
            var caixa = n.Medidas;
            caixa.KeyDown += async (_, ev) => { if (ev.Key == Windows.System.VirtualKey.Enter) { ev.Handled = true; await AplicarTextoDaEsquadriaAsync(linha, e.Indice, "medidas", caixa); } };
            caixa.LostFocus += async (_, _) => await AplicarTextoDaEsquadriaAsync(linha, e.Indice, "medidas", caixa);
            var s = new StackPanel { Spacing = 2 };
            s.Children.Add(Legenda("medidas"));
            s.Children.Add(caixa);
            medidas.Children.Add(s);
        }
        Por(medidas, 1, 1, 2);   // as medidas usam também a coluna da área: no painel estreito não cabiam ao lado dela

        if (e.Acrescentada)
        {
            var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
            var marca = CartoesDePainel.Marca("novo");
            marca.HorizontalAlignment = HorizontalAlignment.Right;
            extras.Children.Add(marca);
            if (ultima && editavel)
            {
                var remover = CartoesDePainel.BotaoPequeno(Glifos.Excluir, "Remover", $"Remover a esquadria {ordinal}", "Tira esta esquadria que você acrescentou e refaz a conta. Desfazer a traz de volta.");
                remover.Click += async (_, _) => await AplicarNasEsquadriasAsync(linha, _ => [new RemoverAcrescimo(linha, "vaos/" + e.Indice.ToString(CultureInfo.InvariantCulture))],
                    "Esquadria removida: " + e.Codigo);
                extras.Children.Add(remover);
            }
            Por(extras, 3, 1, 2);
        }

        // Alerta (só quando há): ícone de atenção + texto, na coluna do conteúdo.
        n.Nota = CartoesDePainel.Apoio("");
        var alerta = new Grid { ColumnSpacing = 6, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };   // Grid: o texto quebra na largura do bloco
        alerta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        alerta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        alerta.Children.Add(new FontIcon { Glyph = Glifos.Avisos, FontSize = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), Style = (Style)res["PmvniIconeAtencaoStyle"] });
        Grid.SetColumn(n.Nota, 1);
        alerta.Children.Add(n.Nota);
        n.NotaBloco = alerta;
        Por(alerta, 2, 1, 2);
        if (editavel && !RoteiroLegado && sessao?.Revisao.Projeto is { } projeto)
        {
            var caminho = $"vaos/{e.Indice}";
            var acoes = new StackPanel { Spacing = 2 };
            acoes.Children.Add(ControleVisibilidade(linha, caminho, $"Esquadria {ordinal}"));
            var detalhes = new Expander { Header = "Campos da esquadria" };
            var opcoes = new StackPanel { Spacing = 4 };
            foreach (var (parte, nome) in new[] { ("codigo", "Código"), ("tipo", "Tipo"), ("medidas", "Medidas"), ("area", "Área"), ("sem_area", "Aviso") })
            {
                var campo = caminho + "/" + parte;
                if (!TabelasRevisao.Editavel(projeto, linha, campo)) continue;
                var botoes = new StackPanel { Spacing = 2 };
                botoes.Children.Add(ControleVisibilidade(linha, campo, nome, "Exibir " + nome.ToLowerInvariant()));
                var ia = CartoesDePainel.BotaoPequeno(Glifos.Ia, "Rever " + nome.ToLowerInvariant() + " com a IA", nome, "Edita o campo da esquadria.");
                ia.Click += async (_, _) => await TentarAsync(() => EditarCampoComIaAsync(linha, campo, nome));
                botoes.Children.Add(ia);
                opcoes.Children.Add(botoes);
            }
            detalhes.Content = opcoes;
            acoes.Children.Add(detalhes);
            Por(acoes, 4, 1, 2);
        }

        esquadriasNoPainel.Add(n);
        return new Border
        {
            Child = grade,
            Padding = new Thickness(10, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Background = (Microsoft.UI.Xaml.Media.Brush)res["CardBackgroundFillColorSecondaryBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)res["CardStrokeColorDefaultBrush"],
        };
    }

    /// <summary>"Adicionar janela" / "Adicionar porta" no fim do grupo: cria a esquadria com a medida padrão (janela 1,00 × 1,00 m; porta
    /// 0,80 × 2,10 m) e põe o cursor na largura dela, para ajustar.</summary>
    FrameworkElement BotaoAdicionarEsquadria(string linha, CategoriaEsquadria cat)
    {
        var janela = cat == CategoriaEsquadria.Janela;
        var nome = janela ? "Adicionar janela" : "Adicionar porta";
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        conteudo.Children.Add(new FontIcon { Glyph = Glifos.Adicionar, FontSize = 16 });
        conteudo.Children.Add(new TextBlock { Text = nome });
        var b = new Button
        {
            Content = conteudo, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(10, 6, 10, 7),
            Style = (Style)Application.Current.Resources["PmvniBotaoTextoDestaqueStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, nome);
        Dicas.Aplicar(b, nome, janela ? "Acrescenta uma janela de 1,00 × 1,00 m; ajuste as medidas e a conta se refaz. Dá para desfazer."
            : "Acrescenta uma porta de 0,80 × 2,10 m; ajuste as medidas e a conta se refaz. Dá para desfazer.");
        b.Click += async (_, _) =>
        {
            string? codigo = null;
            var medidas = janela ? "1,00 × 1,00 m" : "0,80 × 2,10 m";
            var ok = await AplicarNasEsquadriasAsync(linha, conta =>
            {
                codigo = Esquadrias.ProximoCodigo(conta.Esquadrias, cat);
                return [Esquadrias.Nova(linha, codigo, janela ? "Janela" : "Porta", medidas)];
            }, "Esquadria acrescentada (" + (janela ? "janela" : "porta") + " " + medidas + ")");
            if (ok && codigo is not null)
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (esquadriasNoPainel.LastOrDefault(x => x.Codigo.Text == codigo)?.Largura is { } l) { l.Focus(FocusState.Programmatic); l.SelectAll(); }
                });
        };
        return b;
    }

    /// <summary>"Adicionar ambiente" (pedido do usuário, 2026-11-16: a conta é por ambiente — janelas + portas do ambiente = vão total):
    /// abre um formulário curto (nome, área do piso, divisor do mínimo, pavimento), cria a linha nova na tabela de iluminação e a
    /// seleciona, para acrescentar as janelas e as portas dela. Dá para desfazer.</summary>
    FrameworkElement BotaoNovoAmbiente(decimal? divisorAtual)
    {
        var res = Application.Current.Resources;
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        conteudo.Children.Add(new FontIcon { Glyph = Glifos.Adicionar, FontSize = 16 });
        conteudo.Children.Add(new TextBlock { Text = "Adicionar ambiente…" });
        var botao = new Button { Content = conteudo, Style = (Style)res["PmvniBotaoPrimarioStyle"], HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(botao, "Adicionar ambiente");
        Dicas.Aplicar(botao, "Adicionar ambiente…", "Acrescenta um ambiente à tabela de iluminação e ventilação; depois você põe as janelas e as portas dele e a conta se faz sozinha.");

        TextBox Caixa(string nome, string exemplo) { var c = new TextBox { PlaceholderText = exemplo }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(c, nome); return c; }
        var nomeAmb = Caixa("Nome do ambiente", "ex.: Banheiro social");
        var piso = Caixa("Área do piso, em metros quadrados", "ex.: 3,50");
        var divisor = Caixa("Divisor do mínimo", "ex.: 8");
        divisor.Text = divisorAtual is { } d ? d.ToString("0.##", Formatos.Cultura) : "";
        var pavimento = Caixa("Pavimento (opcional)", "ex.: Térreo");
        var erro = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Style = (Style)res["PmvniTextoErroStyle"], TextAlignment = TextAlignment.Left };
        var criar = new Button { Content = "Adicionar", Style = (Style)res["PmvniBotaoPrimarioStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        IconeDeAcao.SetGlifo(criar, Glifos.Adicionar);
        var linhaPisoDivisor = new Grid { ColumnSpacing = 8 };
        linhaPisoDivisor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        linhaPisoDivisor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var campoPiso = CartoesDePainel.Campo("Área do piso (m²)", piso);
        var campoDivisor = CartoesDePainel.Campo("Divisor", divisor);
        Grid.SetColumn(campoDivisor, 1);
        linhaPisoDivisor.Children.Add(campoPiso);
        linhaPisoDivisor.Children.Add(campoDivisor);
        var formulario = new StackPanel { Spacing = 12, Width = 300 };
        formulario.Children.Add(new TextBlock { Text = "Novo ambiente", Style = (Style)res["PmvniAjusteTitulo"] });
        formulario.Children.Add(CartoesDePainel.Campo("Nome", nomeAmb));
        formulario.Children.Add(linhaPisoDivisor);
        formulario.Children.Add(CartoesDePainel.Campo("Pavimento", pavimento));
        formulario.Children.Add(CartoesDePainel.Apoio("Mínimo exigido = área do piso ÷ divisor. Depois de criar, acrescente as janelas e as portas."));
        formulario.Children.Add(erro);
        formulario.Children.Add(criar);
        var flyout = new Flyout { Content = formulario, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Left };
        botao.Flyout = flyout;
        flyout.Opened += (_, _) => { erro.Visibility = Visibility.Collapsed; nomeAmb.Focus(FocusState.Programmatic); };

        async Task Criar()
        {
            void Erro(string texto) { erro.Text = texto; erro.Visibility = Visibility.Visible; }
            if (nomeAmb.Text.Trim().Length == 0) { Erro("Escreva o nome do ambiente."); nomeAmb.Focus(FocusState.Programmatic); return; }
            if (RecalculoLinha.LerNumero(piso.Text) is not var (areaPiso, _) || areaPiso <= 0) { Erro("A área do piso tem de ser um número (por exemplo, 3,50)."); piso.Focus(FocusState.Programmatic); return; }
            if (RecalculoLinha.LerNumero(divisor.Text) is not var (div, _) || div <= 0) { Erro("O divisor tem de ser um número maior que zero (por exemplo, 8)."); divisor.Focus(FocusState.Programmatic); return; }
            if (!Editavel || sessao is not { } s) return;
            string? criado = null;
            await TentarAsync(() => NaFilaAsync(async () =>
            {
                if (!ReferenceEquals(sessao, s) || TabelasRevisao.ProximoAmbiente(s.Revisao.Projeto) is not { } id) return;
                await AplicarAsync(new AcrescentarAmbiente(id, nomeAmb.Text, areaPiso, div, pavimento.Text));
                if (ultimosErros.Count == 0 && TabelasRevisao.Existe(s.Revisao.Projeto, id)) criado = id;
            }));
            if (criado is null) return;
            flyout.Hide();
            nomeAmb.Text = piso.Text = pavimento.Text = "";
            await TentarAsync(() => SelecionarAsync(criado));
            MostrarMensagem(InfoBarSeverity.Success, "Novo ambiente", TabelasRevisao.Rotulo(criado) + " acrescentado. Agora adicione as janelas e as portas dele; um Desfazer o tira.");
        }
        criar.Click += async (_, _) => await Criar();
        foreach (var c in new[] { nomeAmb, piso, divisor, pavimento })
            c.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await Criar(); } };
        return botao;
    }

    /// <summary>"1,50 × 1,20" das caixas de uma esquadria; null se alguma não é número (a conta mostra "—").</summary>
    static string? MedidasDigitadas(EsquadriaNoPainel n) =>
        n.Largura is null ? n.Medidas?.Text : Esquadrias.LerMedidas(n.Largura.Text.Trim() + " × " + n.Altura!.Text.Trim()) is null ? null : n.Largura.Text.Trim() + " × " + n.Altura.Text.Trim();

    /// <summary>O texto da conta do mínimo com o piso e o divisor das caixas; null se não são números (ou o divisor é zero).</summary>
    string? MinimoDigitado() =>
        esqPiso is null || esqDivisor is null ? null
        : RecalculoLinha.LerNumero(esqPiso.Text) is var (piso, _) && RecalculoLinha.LerNumero(esqDivisor.Text) is var (divisor, _) && piso > 0 && divisor > 0
            ? Esquadrias.TextoDaContaDoMinimo(piso, divisor) : null;

    /// <summary>A conta em tempo real: com o que está digitado nas caixas (nada gravado), a área de cada vão, o vão total, o mínimo e o
    /// resultado. É determinística (<see cref="Esquadrias.Calcular"/>): sem IA.</summary>
    void AtualizarContaDasEsquadrias()
    {
        if (ambienteDasEsquadrias is not { } linha || sessao?.Revisao.Projeto is not { } p || esqTotal is null) return;
        var emEdicao = new Dictionary<string, string>(StringComparer.Ordinal);
        var pendente = false;
        foreach (var n in esquadriasNoPainel)
        {
            var caminho = "vaos/" + n.Indice.ToString(CultureInfo.InvariantCulture) + "/medidas";
            if (n.Largura is not null)
            {
                emEdicao[caminho] = MedidasDigitadas(n) ?? "?";
                pendente |= n.Largura.Text != (n.Largura.Tag as string) || n.Altura!.Text != (n.Altura.Tag as string);
            }
        }
        if (esqPiso is not null && caminhoDoMinimo is not null)
        {
            emEdicao[caminhoDoMinimo] = MinimoDigitado() ?? "?";
            pendente |= esqPiso.Text != (esqPiso.Tag as string) || esqDivisor!.Text != (esqDivisor.Tag as string);
        }
        if (Esquadrias.Calcular(p, linha, emEdicao) is not { } conta) return;
        foreach (var n in esquadriasNoPainel)
        {
            if (n.Indice >= conta.Esquadrias.Count) continue;
            var e = conta.Esquadrias[n.Indice];
            n.Area.Text = e.Area is { } a ? Esquadrias.TextoArea(a) : "—";
            var semArea = e.Area is not null && !e.TemCampoDeArea;
            n.Nota.Text = semArea ? "Fora da conta: o relatório não traz a área desta esquadria." : "";
            n.NotaBloco.Visibility = semArea ? Visibility.Visible : Visibility.Collapsed;
        }
        esqTotal.Text = conta.TextoTotal;
        esqConta!.Text = conta.TextoConta == "—" ? "Nenhuma esquadria com área." : conta.TextoConta;
        esqMinimo!.Text = esqPiso is not null ? conta.TextoDoMinimo : conta.TextoMinimo;
        esqResultado!.Style = CartoesDePainel.EstiloDoResultado(conta.Atende);
        esqResultado.Text = conta.Situacao == "PENDENTE" ? "FALTA COMPROVAR (falta " + conta.TextoPendencia + ")"
            : conta.Atende is not { } atende || conta.Total is not { } total || conta.Minimo is not { } minimo
            ? "Sem como comparar"
            : atende ? (total == minimo ? "Conta nominal confere (no limite)" : "Conta nominal confere (sobram " + Esquadrias.TextoArea(total - minimo) + ")")
            : "Conta nominal não confere (faltam " + conta.TextoFalta + ")";
        var gravada = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(p, linha));
        esqAviso!.Text = pendente ? "Conta com o que está digitado. Enter (ou sair da caixa) aplica; Esc desiste."
            : conta.Atende is { } r && gravada is not null && (gravada == "ATENDE") != r
                ? "A situação gravada da linha (" + TextoSituacao(gravada) + ") não é a desta conta: ela foi dada na análise. Editar uma medida ou adicionar uma esquadria a recalcula."
                : "Conta do editor (sem IA): área = largura × altura; vão total = soma das áreas.";
    }

    /// <summary>Aplica uma mudança nas esquadrias junto com o recálculo do ambiente (<see cref="Esquadrias.Recalcular"/>), num só
    /// Desfazer, e diz o resultado. As edições são montadas na vez do comando, sobre o projeto de então. false = recusado.</summary>
    async Task<bool> AplicarNasEsquadriasAsync(string linha, Func<ContaAmbiente, IReadOnlyList<Comando>> edicoes, string descricao)
    {
        if (!Editavel || sessao is not { } s) return false;
        var aplicado = false;
        await TentarAsync(() => NaFilaAsync(async () =>
        {
            if (!ReferenceEquals(sessao, s) || Esquadrias.Calcular(s.Revisao.Projeto, linha) is not { } conta) return;
            Lote lote;
            try { lote = Esquadrias.Recalcular(s.Revisao.Projeto, linha, edicoes(conta), descricao); }
            catch (ComandoRecusadoException e)
            {
                ultimosErros.Clear();
                Recusar([e.Message]);
                return;
            }
            await AplicarAsync(lote);
            if (ultimosErros.Count > 0 || Esquadrias.Calcular(s.Revisao.Projeto, linha) is not { } depois) return;
            aplicado = true;
            MostrarMensagem(InfoBarSeverity.Success, "Esquadrias",
                $"{TabelasRevisao.Rotulo(linha)} recalculado pelo editor (sem IA): vão total {depois.TextoTotal}"
                + (depois.Minimo is not null ? $"; mínimo {depois.TextoMinimo}" : "")
                + $"; situação {TextoSituacao(TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(s.Revisao.Projeto, linha)))}. Confira o texto da situação; um Desfazer volta tudo.");
        }));
        return aplicado;
    }

    /// <summary>Largura × altura digitadas: grava as medidas ("1,70 × 1,20 m") e refaz a conta. Ilegíveis: as caixas voltam ao gravado.</summary>
    async Task AplicarMedidasAsync(string linha, EsquadriaNoPainel n)
    {
        if (n.Largura is null || n.Altura is null || n.Largura.IsReadOnly) return;
        if (n.Largura.Text == (n.Largura.Tag as string) && n.Altura.Text == (n.Altura.Tag as string)) return;
        if (Esquadrias.LerMedidas(n.Largura.Text.Trim() + " × " + n.Altura.Text.Trim()) is not var (l, a))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Esquadrias", "Largura e altura têm de ser números (por exemplo, 0,80 e 2,10); as medidas voltaram ao que estava gravado.");
            n.Largura.Text = n.Largura.Tag as string ?? "";
            n.Altura.Text = n.Altura.Tag as string ?? "";
            return;
        }
        var texto = Esquadrias.TextoMedidas(l, a);
        n.Largura.Tag = n.Largura.Text;   // já a caminho: perder o foco logo depois do Enter não aplica duas vezes
        n.Altura.Tag = n.Altura.Text;
        var caminho = "vaos/" + n.Indice.ToString(CultureInfo.InvariantCulture) + "/medidas";
        await AplicarNasEsquadriasAsync(linha, _ => [new DefinirCampo(linha, caminho, texto)], "Medidas de " + n.Codigo.Text.Trim() + ": " + texto);
    }

    /// <summary>O código, o tipo ou as medidas em texto de uma esquadria: grava e refaz a conta (o código aparece na conta do vão total).</summary>
    async Task AplicarTextoDaEsquadriaAsync(string linha, int indice, string parte, TextBox caixa)
    {
        if (atualizandoPropriedades || caixa.IsReadOnly) return;
        var gravado = caixa.Tag as string ?? "";
        var texto = caixa.Text;
        if (TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(gravado)) return;
        caixa.Tag = texto;
        var caminho = "vaos/" + indice.ToString(CultureInfo.InvariantCulture) + "/" + parte;
        if (!await AplicarNasEsquadriasAsync(linha, _ => [new DefinirCampo(linha, caminho, texto)], TabelasRevisao.NomeDoCampo(caminho) + ": " + TextoEditado.Normalizar(texto)))
            caixa.Text = caixa.Tag as string == texto ? gravado : caixa.Tag as string ?? gravado;
    }

    /// <summary>O piso e o divisor digitados: grava a conta do mínimo ("12,00 ÷ 6 = 2,00 m²") e refaz a comparação.</summary>
    async Task AplicarMinimoAsync()
    {
        if (esqPiso is null || esqDivisor is null || esqPiso.IsReadOnly || ambienteDasEsquadrias is not { } linha || caminhoDoMinimo is not { } caminho) return;
        if (esqPiso.Text == (esqPiso.Tag as string) && esqDivisor.Text == (esqDivisor.Tag as string)) return;
        if (MinimoDigitado() is not { } texto)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Esquadrias", "A área do piso e o divisor têm de ser números maiores que zero; voltaram ao que estava gravado.");
            esqPiso.Text = esqPiso.Tag as string ?? "";
            esqDivisor.Text = esqDivisor.Tag as string ?? "";
            return;
        }
        esqPiso.Tag = esqPiso.Text;
        esqDivisor.Tag = esqDivisor.Text;
        await AplicarNasEsquadriasAsync(linha, _ => [new DefinirCampo(linha, caminho, texto)], "Mínimo exigido: " + texto);
    }
}
