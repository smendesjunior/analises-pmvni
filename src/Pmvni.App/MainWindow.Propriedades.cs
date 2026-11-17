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
    sealed record CampoNoPainel(TextBox Caixa, Border Marca, Button Restaurar, TextBlock? Original);

    readonly Dictionary<string, CampoNoPainel> camposNoPainel = new(StringComparer.Ordinal);
    string assinaturaCampos = "";

    // ---------- acessores do roteiro ----------
    internal IReadOnlyCollection<string> CamposNoPainel => camposNoPainel.Keys;
    internal string? TextoDoCampoNoPainel(string campo) => camposNoPainel.TryGetValue(campo, out var c) ? c.Caixa.Text : null;
    internal bool EsquadriasVisiveis => R.PropEsquadrias.Visibility == Visibility.Visible;
    internal string TextoDoVaoTotal => esqTotal?.Text ?? "";
    internal string TextoDoResultadoDasEsquadrias => esqResultado?.Text ?? "";

    bool TemFoco(Control? c) => c is not null && Raiz.XamlRoot is not null && ReferenceEquals(FocusManager.GetFocusedElement(Raiz.XamlRoot), c);

    /// <summary>Propriedades do item ou da linha selecionada.</summary>
    void AtualizarPropriedades()
    {
        if (fechada) return;
        atualizandoPropriedades = true;
        try
        {
            var p = sessao?.Revisao.Projeto;
            var it = sessao?.Atual is null ? null : ItemSelecionado;
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
            R.PropTitulo.Text = p.NumeroAtual(it.Id) is { } n ? $"ITEM {n:00}" : it.Criado ? "Item novo excluído" : $"Item excluído (era o {it.NumeroOriginal:00})";
            R.PropId.Text = it.Criado ? "criado na revisão · " + it.Id : it.Id;
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
        R.PropNoPdf.IsChecked = true;
        R.PropNoPdf.IsEnabled = false;

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
        R.PropAjustarRotulo.Text = "Rever com a IA";
        R.PropAjustarAviso.Text = "A justificativa mudou: reveja a linha com a IA (o botão abaixo) ou edite os textos.";
        R.PropAjustarAviso.Visibility = ReajustePendente(p, linha, rev?.Justificativa) ? Visibility.Visible : Visibility.Collapsed;
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
        MontarHistoricoDoPainel(p, linha);
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
        Func<string, bool> alterado, string dicaRestaurar)
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
                var restaurar = CartoesDePainel.BotaoPequeno("Restaurar", "Restaurar o campo " + nome, dicaRestaurar);
                restaurar.Click += async (_, _) => await TentarAsync(() => AplicarAsync(new RestaurarCampo(id, campo)));
                caixa.KeyDown += async (_, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await AplicarCampoDoPainelAsync(id, campo, caixa); }
                    else if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; caixa.Text = caixa.Tag as string ?? ""; }
                };
                caixa.LostFocus += async (_, _) => await AplicarCampoDoPainelAsync(id, campo, caixa);
                var bloco = CartoesDePainel.Campo(nome, caixa, marca, restaurar);
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
                camposNoPainel[campo] = new CampoNoPainel(caixa, marca, restaurar, textoOriginal);
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
        public TextBlock Nota = null!; // embaixo das medidas (ao lado, era cortado na largura do painel)
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
                var contagem = CartoesDePainel.Apoio(doGrupo.Count == 0 ? "nenhuma" : doGrupo.Count == 1 ? "1 esquadria" : doGrupo.Count.ToString(CultureInfo.InvariantCulture) + " esquadrias");
                contagem.VerticalAlignment = VerticalAlignment.Center;
                var tituloGrupo = CartoesDePainel.Rotulo(titulo);
                tituloGrupo.VerticalAlignment = VerticalAlignment.Center;
                corpo.Children.Add(CartoesDePainel.Cabecalho(tituloGrupo, contagem));
                foreach (var e in doGrupo) corpo.Children.Add(LinhaDaEsquadria(linha, e, editavel, e.Indice == ultimo));
                corpo.Children.Add(CartoesDePainel.Separador());
            }
            if (editavel)
            {
                corpo.Children.Add(LinhaDeAdicionar(linha));
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

    /// <summary>A linha de uma esquadria: código e tipo (editáveis), "novo" e Remover (na última acrescentada), e largura × altura
    /// (editáveis) com a área calculada ao lado.</summary>
    FrameworkElement LinhaDaEsquadria(string linha, Esquadria e, bool editavel, bool ultima)
    {
        var n = new EsquadriaNoPainel { Indice = e.Indice };
        var ordinal = (e.Indice + 1).ToString(CultureInfo.InvariantCulture);
        n.Codigo = CartoesDePainel.Caixa("", $"Código da esquadria {ordinal}", editavel);
        n.Codigo.Width = 58;
        n.Codigo.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        n.Codigo.TextWrapping = TextWrapping.NoWrap;
        n.Tipo = CartoesDePainel.Caixa("", $"Tipo da esquadria {ordinal}", editavel);
        n.Tipo.TextWrapping = TextWrapping.NoWrap;
        var topo = new Grid { ColumnSpacing = 6 };
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.Children.Add(n.Codigo);
        Grid.SetColumn(n.Tipo, 1);
        topo.Children.Add(n.Tipo);
        if (e.Acrescentada)
        {
            var extras = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            extras.Children.Add(CartoesDePainel.Marca("novo"));
            if (ultima && editavel)
            {
                var remover = CartoesDePainel.BotaoPequeno("Remover", $"Remover a esquadria {ordinal}", "Tira esta esquadria que você acrescentou e refaz a conta. Desfazer a traz de volta.");
                remover.Click += async (_, _) => await AplicarNasEsquadriasAsync(linha, _ => [new RemoverAcrescimo(linha, "vaos/" + e.Indice.ToString(CultureInfo.InvariantCulture))],
                    "Esquadria removida: " + e.Codigo);
                extras.Children.Add(remover);
            }
            Grid.SetColumn(extras, 2);
            topo.Children.Add(extras);
        }
        foreach (var (caixa, parte, nome) in new[] { (n.Codigo, "codigo", "Código"), (n.Tipo, "tipo", "Tipo") })
        {
            caixa.KeyDown += async (_, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter) { ev.Handled = true; await AplicarTextoDaEsquadriaAsync(linha, e.Indice, parte, caixa); }
                else if (ev.Key == Windows.System.VirtualKey.Escape) { ev.Handled = true; caixa.Text = caixa.Tag as string ?? ""; }
            };
            caixa.LostFocus += async (_, _) => await AplicarTextoDaEsquadriaAsync(linha, e.Indice, parte, caixa);
        }

        // Correção 2026-11-16 (pedido do usuário: "não ficou bem organizado"): as medidas em colunas com rótulo — LARGURA (m) × ALTURA (m)
        // = ÁREA —, as caixas dividindo a largura do painel e a área alinhada à direita; antes ia tudo numa fila que estourava o painel.
        n.Area = CartoesDePainel.Valor("", forte: true);
        n.Area.VerticalAlignment = VerticalAlignment.Center;
        n.Area.HorizontalAlignment = HorizontalAlignment.Right;
        n.Area.MinWidth = 76;
        n.Area.TextAlignment = TextAlignment.Right;
        var medidas = new Grid { ColumnSpacing = 6, RowSpacing = 3 };
        medidas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        medidas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var largura in new[] { new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            medidas.ColumnDefinitions.Add(new ColumnDefinition { Width = largura });
        void Por(UIElement el, int linhaGrade, int coluna, int vao = 1)
        {
            Grid.SetRow((FrameworkElement)el, linhaGrade);
            Grid.SetColumn((FrameworkElement)el, coluna);
            Grid.SetColumnSpan((FrameworkElement)el, vao);
            medidas.Children.Add(el);
        }
        TextBlock Sinal(string s) => new() { Text = s, VerticalAlignment = VerticalAlignment.Center, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
        var rotuloArea = CartoesDePainel.Rotulo("Área");
        rotuloArea.HorizontalAlignment = HorizontalAlignment.Right;
        Por(rotuloArea, 0, 4);
        Por(n.Area, 1, 4);
        if (e.Largura is not null)
        {
            n.Largura = CartoesDePainel.CaixaDeNumero("", $"Largura da esquadria {ordinal}, em metros", editavel);
            n.Altura = CartoesDePainel.CaixaDeNumero("", $"Altura da esquadria {ordinal}, em metros", editavel);
            foreach (var c in new[] { n.Largura, n.Altura }) { c.Width = double.NaN; c.HorizontalAlignment = HorizontalAlignment.Stretch; }
            Por(CartoesDePainel.Rotulo("Largura"), 0, 0);
            Por(CartoesDePainel.Rotulo("Altura"), 0, 2);
            Por(n.Largura, 1, 0);
            Por(Sinal("×"), 1, 1);
            Por(n.Altura, 1, 2);
            Por(Sinal("m  ="), 1, 3);
            foreach (var caixa in new[] { n.Largura, n.Altura })
            {
                caixa.TextChanged += (_, _) => AtualizarContaDasEsquadrias();   // tempo real: nada é gravado enquanto digita
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
            var caixa = n.Medidas;
            caixa.KeyDown += async (_, ev) => { if (ev.Key == Windows.System.VirtualKey.Enter) { ev.Handled = true; await AplicarTextoDaEsquadriaAsync(linha, e.Indice, "medidas", caixa); } };
            caixa.LostFocus += async (_, _) => await AplicarTextoDaEsquadriaAsync(linha, e.Indice, "medidas", caixa);
            Por(CartoesDePainel.Rotulo("Medidas"), 0, 0, 3);
            Por(caixa, 1, 0, 3);
            Por(Sinal("="), 1, 3);
        }
        n.Nota = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
        esquadriasNoPainel.Add(n);
        var conteudo = new StackPanel { Spacing = 10 };
        conteudo.Children.Add(topo);
        conteudo.Children.Add(medidas);
        conteudo.Children.Add(n.Nota);
        // Cada esquadria num bloco próprio, com fundo e borda discretos: separa uma da outra sem depender de linhas.
        return new Border
        {
            Child = conteudo,
            Padding = new Thickness(10, 10, 10, 10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Margin = new Thickness(0, 0, 0, 2),
        };
    }

    /// <summary>"Adicionar esquadria": categoria (janela ou porta), largura × altura e o botão. O código é o próximo livre (J2, P1).</summary>
    FrameworkElement LinhaDeAdicionar(string linha)
    {
        var categoria = new ComboBox { MinWidth = 96 };
        categoria.Items.Add(new ComboBoxItem { Content = "Porta", Tag = CategoriaEsquadria.Porta });
        categoria.Items.Add(new ComboBoxItem { Content = "Janela", Tag = CategoriaEsquadria.Janela });
        categoria.SelectedIndex = 0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(categoria, "Categoria da esquadria nova");
        var largura = CartoesDePainel.CaixaDeNumero("", "Largura da esquadria nova, em metros", true);
        var altura = CartoesDePainel.CaixaDeNumero("", "Altura da esquadria nova, em metros", true);
        largura.PlaceholderText = "0,80";
        altura.PlaceholderText = "2,10";
        var adicionar = new Button { Content = "Adicionar", Style = (Style)Application.Current.Resources["AccentButtonStyle"] }; // ação principal do bloco: cor de destaque
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(adicionar, "Adicionar esquadria");
        Dicas.Aplicar(adicionar, "Adicionar esquadria", "Acrescenta a esquadria ao ambiente, calcula a área dela e refaz o vão total e a situação. Dá para desfazer.");
        async Task Adicionar()
        {
            var cat = categoria.SelectedItem is ComboBoxItem { Tag: CategoriaEsquadria c } ? c : CategoriaEsquadria.Porta;
            var medidas = largura.Text.Trim() + " × " + altura.Text.Trim();
            var ok = await AplicarNasEsquadriasAsync(linha, conta =>
            {
                var codigo = Esquadrias.ProximoCodigo(conta.Esquadrias, cat);
                return [Esquadrias.Nova(linha, codigo, cat == CategoriaEsquadria.Janela ? "Janela" : "Porta", medidas)];
            }, "Esquadria acrescentada (" + (cat == CategoriaEsquadria.Janela ? "janela" : "porta") + " " + medidas + ")");
            if (!ok) (Esquadrias.LerMedidas(medidas) is null ? largura : (Control)adicionar).Focus(FocusState.Programmatic);
        }
        adicionar.Click += async (_, _) => await Adicionar();
        foreach (var caixa in new[] { largura, altura })
            caixa.KeyDown += async (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await Adicionar(); } };
        var linhaDeMedidas = new PainelQuebra { Espaco = 6, EspacoLinhas = 6 };
        linhaDeMedidas.Children.Add(categoria);
        linhaDeMedidas.Children.Add(largura);
        linhaDeMedidas.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(0, 6, 0, 0) });
        linhaDeMedidas.Children.Add(altura);
        linhaDeMedidas.Children.Add(new TextBlock { Text = "m", Margin = new Thickness(0, 6, 0, 0) });
        linhaDeMedidas.Children.Add(adicionar);
        return CartoesDePainel.Campo("Adicionar esquadria", linhaDeMedidas);
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
            n.Nota.Visibility = semArea ? Visibility.Visible : Visibility.Collapsed;
        }
        esqTotal.Text = conta.TextoTotal;
        esqConta!.Text = conta.TextoConta == "—" ? "Nenhuma esquadria com área." : conta.TextoConta;
        esqMinimo!.Text = esqPiso is not null ? (conta.Minimo is { } m ? Esquadrias.TextoArea(m) : "—") : conta.TextoMinimo;
        esqResultado!.Style = CartoesDePainel.EstiloDoResultado(conta.Atende);
        esqResultado.Text = conta.Atende is not { } atende || conta.Total is not { } total || conta.Minimo is not { } minimo
            ? "Sem como comparar"
            : atende ? (total == minimo ? "ATENDE (no limite)" : "ATENDE (sobram " + Esquadrias.TextoArea(total - minimo) + ")")
            : "NÃO ATENDE (faltam " + Esquadrias.TextoArea(minimo - total) + ")";
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
