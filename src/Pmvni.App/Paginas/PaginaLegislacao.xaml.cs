using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Página do módulo Legislação (desenho §11.4): só a parte visual. Quem a preenche e reage é a janela
/// (<c>MainWindow.Legislacao.cs</c>), pelos elementos <c>internal</c>. É criada uma vez e trocada por Visibility (o WebView2 do
/// leitor nunca é recriado).</summary>
public sealed partial class PaginaLegislacao : UserControl
{
    public PaginaLegislacao()
    {
        InitializeComponent();
        AtualizarDicasDoPortal();
        foreach (var aba in AbasPainel.Items) FaixaDeOpcoes.AjustarIndicador(aba);
        foreach (var aba in ModoLeitura.Items) FaixaDeOpcoes.AjustarIndicador(aba);
    }
}

/// <summary>Um marcador na árvore: o título como texto puro (com "(link externo)" quando é um link para fora) e o caminho na
/// estrutura do PDF (índices a partir de 0), que vai ao leitor.</summary>
internal sealed record NoMarcador(string Titulo, bool Externo, IReadOnlyList<int> Caminho)
{
    public override string ToString() => Externo ? Titulo + " (link externo)" : Titulo;
}
