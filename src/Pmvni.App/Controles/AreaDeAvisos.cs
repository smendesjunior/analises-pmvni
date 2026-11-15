using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Área de avisos de uma página (auditoria M7): a pilha de <see cref="InfoBar"/>s mostra no máximo <see cref="MaxVisiveis"/> abertas
/// (na ordem da página: as de ação imediata primeiro) e o resto fica atrás de "+n avisos"; nenhuma é perdida (continuam abertas, só
/// recolhidas). Informativas e de sucesso fecham sozinhas depois de <see cref="TempoParaFechar"/>, salvo as marcadas com
/// <c>Tag="fixo"</c> (têm botões ou descrevem o documento); avisos e erros ficam até o usuário fechar. Outros filhos (barra de progresso)
/// ficam sempre à vista.</summary>
public sealed partial class AreaDeAvisos : StackPanel
{
    public const int MaxVisiveis = 2;

    /// <summary>Informativas fecham sozinhas (desligado no roteiro, que confere o estado das InfoBars depois de cada passo).</summary>
    public static bool FecharSozinho { get; set; } = Argumentos.Atual.Roteiro is null;

    public static TimeSpan TempoParaFechar { get; set; } = TimeSpan.FromSeconds(8);

    readonly HyperlinkButton mais = new() { Margin = new Thickness(12, 0, 0, 2), Visibility = Visibility.Collapsed };
    readonly Dictionary<InfoBar, DispatcherQueueTimer> relogios = new();
    readonly List<InfoBar> ligadas = [];
    bool expandida;

    public AreaDeAvisos()
    {
        Spacing = 1;
        mais.Click += (_, _) => { expandida = !expandida; Reorganizar(); };
        Loaded += (_, _) => Ligar();
    }

    /// <summary>As InfoBars abertas e recolhidas atrás de "+n" (o roteiro confere que nada se perdeu).</summary>
    public IReadOnlyList<InfoBar> Recolhidas => ligadas.Where(i => i.IsOpen && i.Visibility == Visibility.Collapsed).ToList();

    public IReadOnlyList<InfoBar> Abertas => ligadas.Where(i => i.IsOpen).ToList();

    public string TextoMais => mais.Visibility == Visibility.Visible ? mais.Content as string ?? "" : "";

    void Ligar()
    {
        foreach (var info in Children.OfType<InfoBar>())
        {
            if (ligadas.Contains(info)) continue;
            ligadas.Add(info);
            info.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => Mudou(info));
            info.RegisterPropertyChangedCallback(InfoBar.MessageProperty, (_, _) => Mudou(info));
            info.RegisterPropertyChangedCallback(InfoBar.SeverityProperty, (_, _) => Mudou(info));
        }
        if (!Children.Contains(mais)) Children.Add(mais);
        Reorganizar();
    }

    void Mudou(InfoBar info)
    {
        if (relogios.Remove(info, out var r)) r.Stop();
        if (info.IsOpen && FecharSozinho && info.IsClosable && info.Tag as string != "fixo"
            && info.Severity is InfoBarSeverity.Informational or InfoBarSeverity.Success)
        {
            var t = DispatcherQueue.CreateTimer();
            t.Interval = TempoParaFechar;
            t.IsRepeating = false;
            t.Tick += (_, _) => { relogios.Remove(info); info.IsOpen = false; };
            relogios[info] = t;
            t.Start();
        }
        Reorganizar();
    }

    void Reorganizar()
    {
        var abertas = ligadas.Where(i => i.IsOpen).ToList();
        var escondidas = expandida ? 0 : Math.Max(0, abertas.Count - MaxVisiveis);
        if (escondidas == 0 && abertas.Count <= MaxVisiveis) expandida = false;
        for (var i = 0; i < ligadas.Count; i++)
        {
            var info = ligadas[i];
            var pos = abertas.IndexOf(info);
            info.Visibility = pos < 0 || expandida || pos < MaxVisiveis ? Visibility.Visible : Visibility.Collapsed;
        }
        if (abertas.Count > MaxVisiveis)
        {
            var n = abertas.Count - MaxVisiveis;
            mais.Content = expandida ? "Mostrar menos" : n == 1 ? "+1 aviso" : $"+{n} avisos";
            AutomationProperties.SetName(mais, expandida ? "Mostrar menos avisos" : $"Mostrar mais {n} " + (n == 1 ? "aviso" : "avisos"));
            mais.Visibility = Visibility.Visible;
        }
        else mais.Visibility = Visibility.Collapsed;
    }
}
