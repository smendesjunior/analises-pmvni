using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Faixa de opções montada de uma <see cref="DefinicaoFaixa"/> (desenho §9.1): troca inteira com o módulo. Cada aba vira um
/// item do <see cref="SelectorBar"/>; os comandos da aba escolhida viram botões do <see cref="CommandBar"/>. Habilitado e ligado ficam
/// guardados por id e valem também para as abas que não estão à vista. Clicar avisa por <see cref="ComandoInvocado"/>.</summary>
public sealed partial class FaixaDeOpcoes : UserControl
{
    readonly Dictionary<string, bool> habilitado = new(StringComparer.Ordinal);
    readonly Dictionary<string, bool> ligado = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> dicas = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> rotulos = new(StringComparer.Ordinal);
    readonly Dictionary<string, MenuFlyoutItem> itensMenu = new(StringComparer.Ordinal);
    readonly Dictionary<string, AppBarButton> visiveis = new(StringComparer.Ordinal);
    readonly Dictionary<string, AppBarToggleButton> alternaveis = new(StringComparer.Ordinal);
    readonly Dictionary<string, MenuFlyout> menusDinamicos = new(StringComparer.Ordinal);
    readonly HashSet<string> marcados = new(StringComparer.Ordinal);
    DefinicaoFaixa? definicao;
    bool montando;

    public FaixaDeOpcoes() => InitializeComponent();

    public event Action<string>? ComandoInvocado;

    /// <summary>Os itens de um menu que a definição deixa vazio (ex.: Recentes), pedidos a quem monta a faixa cada vez que o menu abre:
    /// texto, valor (o caminho) e dica. Sem este provedor ou sem itens, o menu mostra "Nenhum item".</summary>
    public Func<string, IReadOnlyList<(string Texto, string Valor, string Dica)>>? ItensDinamicos { get; set; }

    /// <summary>Um item de menu dinâmico foi escolhido: id do menu e valor do item.</summary>
    public event Action<string, string>? ItemDinamicoEscolhido;

    /// <summary>A aba escolhida pelo usuário (nome), para a janela lembrar nos ajustes.</summary>
    public event Action<string>? AbaMudou;

    public DefinicaoFaixa? Definicao => definicao;

    public string? AbaAtual => definicao is null || Abas.SelectedItem is not SelectorBarItem i ? null : (string)i.Tag;

    /// <summary>Ids dos comandos da aba à vista, na ordem dos botões.</summary>
    public IReadOnlyList<string> IdsVisiveis => Barra.PrimaryCommands.OfType<FrameworkElement>().Select(e => (string)e.Tag).ToList();

    /// <summary>Rótulos compactos (só ícones) quando a janela é estreita.</summary>
    public bool Compacta
    {
        get => Barra.DefaultLabelPosition == CommandBarDefaultLabelPosition.Collapsed;
        set => Barra.DefaultLabelPosition = value ? CommandBarDefaultLabelPosition.Collapsed : CommandBarDefaultLabelPosition.Right;
    }

    public void Montar(DefinicaoFaixa f, int aba)
    {
        montando = true;
        try
        {
            definicao = f;
            Abas.Items.Clear();
            foreach (var a in f.Abas) Abas.Items.Add(new SelectorBarItem { Text = a.Nome, Tag = a.Nome });
            Abas.SelectedItem = Abas.Items[Math.Clamp(aba, 0, f.Abas.Count - 1)];
            MontarBotoes();
        }
        finally { montando = false; }
    }

    /// <summary>Escolhe a aba pelo nome (o roteiro e os ajustes lembrados usam).</summary>
    public void EscolherAba(string nome)
    {
        var item = Abas.Items.FirstOrDefault(i => (string)i.Tag == nome);
        if (item is not null) Abas.SelectedItem = item;
    }

    public void Habilitar(string id, bool sim)
    {
        habilitado[id] = sim;
        if (visiveis.TryGetValue(id, out var b)) b.IsEnabled = sim;
        if (alternaveis.TryGetValue(id, out var t)) t.IsEnabled = sim;
        if (itensMenu.TryGetValue(id, out var i)) i.IsEnabled = sim;
    }

    /// <summary>Troca o rótulo do botão (ex.: "Excluir item" ⇄ "Restaurar item"); vale também quando a aba voltar à vista.</summary>
    public void DefinirRotulo(string id, string texto)
    {
        if (rotulos.TryGetValue(id, out var r) && r == texto) return;
        rotulos[id] = texto;
        if (visiveis.TryGetValue(id, out var b)) { b.Label = texto; AutomationProperties.SetName(b, texto); }
        if (alternaveis.TryGetValue(id, out var t)) { t.Label = texto; AutomationProperties.SetName(t, texto); }
    }

    /// <summary>O rótulo atual do comando (o da definição, se nunca foi trocado).</summary>
    public string Rotulo(string id) =>
        rotulos.TryGetValue(id, out var r) ? r : definicao is null ? "" : Faixas.Comandos(definicao).FirstOrDefault(c => c.Id == id)?.Rotulo ?? "";

    /// <summary>Refaz os itens do menu dinâmico <paramref name="menu"/> agora e devolve o valor de cada um, na ordem (só existe com a aba
    /// do menu à vista; senão, lista vazia).</summary>
    public IReadOnlyList<string> AtualizarMenuDinamico(string menu)
    {
        if (!menusDinamicos.TryGetValue(menu, out var m)) return [];
        PreencherMenuDinamico(menu, m);
        return m.Items.OfType<MenuFlyoutItem>().Where(i => i.Tag is string).Select(i => (string)i.Tag).ToList();
    }

    /// <summary>O texto de cada item do menu dinâmico, como o usuário o lê.</summary>
    public IReadOnlyList<string> TextosDoMenuDinamico(string menu) =>
        menusDinamicos.TryGetValue(menu, out var m) ? m.Items.OfType<MenuFlyoutItem>().Where(i => i.Tag is string).Select(i => i.Text).ToList() : [];

    /// <summary>Marca (✓) ou desmarca um item de menu (ex.: o tema em uso); vale também quando o menu for montado de novo.</summary>
    public void MarcarItem(string id, bool sim)
    {
        if (sim) marcados.Add(id); else marcados.Remove(id);
        if (itensMenu.TryGetValue(id, out var i)) i.Icon = Icone(sim, "");
    }

    public bool Marcado(string id) => marcados.Contains(id);

    /// <summary>O ícone de um item de menu: o ✓ (Segoe Fluent E73E) quando marcado, senão o glifo da definição.</summary>
    static FontIcon Icone(bool marcado, string glifo) => new() { Glyph = marcado ? "" : glifo };

    void PreencherMenuDinamico(string menu, MenuFlyout m)
    {
        m.Items.Clear();
        var itens = ItensDinamicos?.Invoke(menu) ?? [];
        foreach (var (texto, valor, dica) in itens)
        {
            var mi = new MenuFlyoutItem { Text = texto, Tag = valor };
            ToolTipService.SetToolTip(mi, dica);
            mi.Click += (_, _) => ItemDinamicoEscolhido?.Invoke(menu, valor);
            m.Items.Add(mi);
        }
        if (itens.Count == 0) m.Items.Add(new MenuFlyoutItem { Text = "Nenhum item", IsEnabled = false });
    }

    public bool Ligado(string id) => ligado.TryGetValue(id, out var l) && l;

    public bool Habilitado(string id) => !habilitado.TryGetValue(id, out var h) || h;

    public void Ligar(string id, bool sim)
    {
        ligado[id] = sim;
        if (alternaveis.TryGetValue(id, out var t)) t.IsChecked = sim;
    }

    public void DefinirDica(string id, string texto)
    {
        dicas[id] = texto;
        if (visiveis.TryGetValue(id, out var b)) ToolTipService.SetToolTip(b, texto);
        if (itensMenu.TryGetValue(id, out var i)) ToolTipService.SetToolTip(i, texto);
    }

    public string Dica(string id) => dicas.TryGetValue(id, out var d) ? d : "";

    void AoMudarAba(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (montando || definicao is null) return;
        MontarBotoes();
        if (AbaAtual is { } nome) AbaMudou?.Invoke(nome);
    }

    void MontarBotoes()
    {
        Barra.PrimaryCommands.Clear();
        visiveis.Clear();
        alternaveis.Clear();
        itensMenu.Clear();
        menusDinamicos.Clear();
        if (definicao is null || AbaAtual is not { } nome) return;
        var aba = definicao.Abas.First(a => a.Nome == nome);
        foreach (var c in aba.Comandos) Barra.PrimaryCommands.Add(Botao(c));
    }

    ICommandBarElement Botao(ComandoFaixa c)
    {
        var icone = new FontIcon { Glyph = c.Glifo };
        var rotulo = rotulos.TryGetValue(c.Id, out var r) ? r : c.Rotulo;
        var atalho = Atalhos.Tecla(c.Id) is { } tecla ? Atalhos.Legivel(tecla) : null;
        var dica = dicas.TryGetValue(c.Id, out var d) ? d : atalho is null ? rotulo : $"{rotulo} ({atalho})";
        if (c.Tipo == TipoComando.Alternar)
        {
            var t = new AppBarToggleButton { Label = rotulo, Icon = icone, Tag = c.Id, IsEnabled = Habilitado(c.Id), IsChecked = ligado.TryGetValue(c.Id, out var l) && l };
            AutomationProperties.SetName(t, rotulo);
            ToolTipService.SetToolTip(t, dica);
            t.Click += (_, _) => ComandoInvocado?.Invoke(c.Id);
            alternaveis[c.Id] = t;
            return t;
        }
        var b = new AppBarButton { Label = rotulo, Icon = icone, Tag = c.Id, IsEnabled = Habilitado(c.Id) };
        if (atalho is not null) b.KeyboardAcceleratorTextOverride = atalho;
        AutomationProperties.SetName(b, rotulo);
        ToolTipService.SetToolTip(b, dica);
        if (c.Tipo == TipoComando.Menu && c.Itens is { } itens)
        {
            var menu = new MenuFlyout();
            if (itens.Count == 0)
            {
                menusDinamicos[c.Id] = menu;
                menu.Opening += (_, _) => PreencherMenuDinamico(c.Id, menu);
                PreencherMenuDinamico(c.Id, menu);
            }
            foreach (var i in itens)
            {
                var mi = new MenuFlyoutItem { Text = i.Rotulo, Tag = i.Id, Icon = Icone(marcados.Contains(i.Id), i.Glifo), IsEnabled = Habilitado(i.Id) };
                // A dica do item (ex.: por que um tipo de despacho está desabilitado) aparece ao passar o mouse.
                if (dicas.TryGetValue(i.Id, out var dicaItem)) ToolTipService.SetToolTip(mi, dicaItem);
                mi.Click += (_, _) => ComandoInvocado?.Invoke(i.Id);
                menu.Items.Add(mi);
                itensMenu[i.Id] = mi;
            }
            b.Flyout = menu;
        }
        else b.Click += (_, _) => ComandoInvocado?.Invoke(c.Id);
        visiveis[c.Id] = b;
        return b;
    }
}
