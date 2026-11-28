using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;

namespace Pmvni.App;

public sealed partial class SeletoresPerfilPlugin : UserControl
{
    public SeletoresPerfilPlugin() => InitializeComponent();

    internal void Configurar(string etapa, PerfilDoPlugin atual, PerfilDoPlugin padrao,
        IReadOnlyList<ModeloIa> catalogo, Func<PerfilDoPlugin, bool> salvar)
    {
        var modelos = catalogo.ToList();
        foreach (var perfil in new[] { atual, padrao })
            if (!modelos.Any(m => m.Id == perfil.Modelo))
                modelos.Add(new(perfil.Modelo, perfil.Modelo, [perfil.Esforco], perfil.Esforco));

        void Exibir(PerfilDoPlugin ativo)
        {
            var modelo = modelos.First(m => m.Id == ativo.Modelo);
            ModeloAtual.Text = Nome(modelo);
            NivelAtual.Text = CatalogoIa.NomeNivel(ativo.Esforco);
            AutomationProperties.SetName(EscolherModelo, $"{etapa}, modelo {ModeloAtual.Text}");
            AutomationProperties.SetName(EscolherNivel, $"{etapa}, inteligência {NivelAtual.Text}");
            EscolherModelo.Flyout = Menu(modelos.Select(m => (m.Id, Nome(m))), modelo.Id, id =>
            {
                var escolhido = modelos.First(m => m.Id == id);
                var niveis = Niveis(escolhido);
                var nivel = niveis.Contains(ativo.Esforco) ? ativo.Esforco
                    : escolhido.NivelPadrao is { } n && niveis.Contains(n) ? n : niveis.First();
                Aplicar(new(id, nivel));
            });
            EscolherNivel.Flyout = Menu(Niveis(modelo).Select(n => (n, CatalogoIa.NomeNivel(n))), ativo.Esforco,
                nivel => Aplicar(ativo with { Esforco = nivel }));
        }

        IReadOnlyList<string> Niveis(ModeloIa m) => m.Niveis.Count > 0 ? m.Niveis :
            m.Id == padrao.Modelo ? [padrao.Esforco] : [m.NivelPadrao ?? atual.Esforco];
        void Aplicar(PerfilDoPlugin novo) { if (salvar(novo)) Exibir(novo); }
        Exibir(atual);
    }

    static string Nome(ModeloIa modelo) => modelo.Nome.Replace("GPT-", "", StringComparison.OrdinalIgnoreCase)
        .Replace('-', ' ').Trim();

    static MenuFlyout Menu(IEnumerable<(string Id, string Nome)> opcoes, string ativo, Action<string> escolher)
    {
        var menu = new MenuFlyout();
        foreach (var (id, nome) in opcoes)
        {
            var item = new RadioMenuFlyoutItem { Text = nome, IsChecked = id == ativo };
            item.Click += (_, _) => escolher(id);
            menu.Items.Add(item);
        }
        return menu;
    }
}

