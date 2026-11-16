using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Pmvni.App;

/// <summary>A miniatura de um arquivo na grade da página do processo (2026-11-16): a prévia que o Windows dá
/// (<see cref="MiniaturasDoShell"/>), pedida em segundo plano só quando o bloco aparece na tela. Enquanto ela não chega (e quando não há
/// prévia), fica o ícone do tipo (<see cref="IconeArquivo"/>). O pedido é cancelado quando o bloco sai da tela, troca de arquivo ou é
/// reaproveitado por outro; as miniaturas prontas ficam guardadas (as últimas <see cref="MaximoGuardado"/>) para a rolagem não refazer.</summary>
public sealed partial class MiniaturaArquivo : Grid
{
    /// <summary>O lado da miniatura na tela (os "ícones grandes" do Explorador têm 96).</summary>
    public const double Lado = 104;
    const int MaximoGuardado = 200;
    // Quanto esperar antes de pedir de novo uma prévia que o Windows ainda está gerando (depois da última, fica o ícone).
    static readonly int[] Esperas = [200, 400, 800, 1600, 3200];

    public static readonly DependencyProperty CaminhoProperty = DependencyProperty.Register(
        nameof(Caminho), typeof(string), typeof(MiniaturaArquivo), new PropertyMetadata(null, (d, _) => ((MiniaturaArquivo)d).AoMudarCaminho()));

    // As miniaturas prontas, por "caminho|lado", da usada há mais tempo à mais recente. Só na thread da interface.
    static readonly Dictionary<string, LinkedListNode<(string Chave, ImageSource Imagem)>> Guardadas = new(StringComparer.OrdinalIgnoreCase);
    static readonly LinkedList<(string Chave, ImageSource Imagem)> Ordem = new();

    readonly Image imagem = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly IconeArquivo icone = new() { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    CancellationTokenSource? pedido;
    string? resolvido; // o caminho cuja miniatura já está na tela (ou que não tem miniatura)

    public MiniaturaArquivo()
    {
        Width = Lado;
        Height = Lado;
        Children.Add(icone);
        Children.Add(imagem);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += (_, _) => Garantir();
        Unloaded += (_, _) => Cancelar();
    }

    /// <summary>O arquivo cuja miniatura aparece.</summary>
    public string? Caminho
    {
        get => (string?)GetValue(CaminhoProperty);
        set => SetValue(CaminhoProperty, value);
    }

    /// <summary>Esquece as miniaturas guardadas (o processo foi lido de novo: um arquivo pode ter mudado).</summary>
    public static void EsquecerGuardadas()
    {
        Guardadas.Clear();
        Ordem.Clear();
    }

    void AoMudarCaminho()
    {
        Cancelar();
        resolvido = null;
        imagem.Source = null;
        icone.Caminho = Caminho;
        icone.Visibility = string.IsNullOrWhiteSpace(Caminho) ? Visibility.Collapsed : Visibility.Visible;
        Garantir();
    }

    /// <summary>Cancela o pedido em curso (o bloco saiu da tela ou foi para a reserva da lista).</summary>
    public void Cancelar()
    {
        pedido?.Cancel();
        pedido = null;
    }

    /// <summary>Pede a miniatura se ela ainda não está na tela nem a caminho (o bloco apareceu ou voltou da reserva).</summary>
    public void Garantir()
    {
        var caminho = Caminho;
        if (string.IsNullOrWhiteSpace(caminho) || !IsLoaded || pedido is not null || string.Equals(resolvido, caminho, StringComparison.Ordinal)) return;
        var escala = XamlRoot?.RasterizationScale ?? 1.0;
        var lado = Math.Clamp((int)Math.Ceiling(Lado * escala), 96, 256);
        var chave = caminho + "|" + lado.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Guardadas.TryGetValue(chave, out var no))
        {
            Ordem.Remove(no);
            Ordem.AddLast(no);
            Mostrar(caminho, no.Value.Imagem);
            return;
        }
        var origem = new CancellationTokenSource();
        pedido = origem;
        _ = CarregarAsync(caminho, lado, chave, origem);
    }

    async Task CarregarAsync(string caminho, int lado, string chave, CancellationTokenSource origem)
    {
        try
        {
            for (var tentativa = 0; ; tentativa++)
            {
                var pixels = await MiniaturasDoShell.ObterAsync(caminho, lado, origem.Token);
                if (origem.IsCancellationRequested || !ReferenceEquals(pedido, origem)) return;
                if (pixels is null)
                {
                    resolvido = caminho; // arquivo sumido ou bloqueado: fica o ícone do tipo
                    return;
                }
                var ultima = !pixels.Pendente || tentativa >= Esperas.Length;
                if (ultima || tentativa == 0)
                {
                    var bitmap = new WriteableBitmap(pixels.Largura, pixels.Altura);
                    using (var fluxo = bitmap.PixelBuffer.AsStream()) fluxo.Write(pixels.Bgra, 0, pixels.Bgra.Length);
                    bitmap.Invalidate();
                    imagem.Source = bitmap;
                    icone.Visibility = Visibility.Collapsed;
                    if (!pixels.Pendente) Guardar(chave, bitmap);
                }
                if (ultima)
                {
                    resolvido = caminho;
                    return;
                }
                // O Windows ainda está gerando a prévia: por ora fica o ícone grande; pede de novo daqui a pouco.
                await Task.Delay(Esperas[tentativa], origem.Token);
            }
        }
        catch (OperationCanceledException) { /* o bloco saiu da tela ou trocou de arquivo */ }
        catch (Exception e)
        {
            // Uma miniatura que falha nunca derruba a página: fica o ícone do tipo.
            System.Diagnostics.Debug.WriteLine("[miniatura] " + e.Message);
            if (ReferenceEquals(pedido, origem)) resolvido = caminho;
        }
        finally
        {
            if (ReferenceEquals(pedido, origem)) pedido = null;
            origem.Dispose();
        }
    }

    void Mostrar(string caminho, ImageSource fonte)
    {
        resolvido = caminho;
        imagem.Source = fonte;
        icone.Visibility = Visibility.Collapsed;
    }

    static void Guardar(string chave, ImageSource fonte)
    {
        if (Guardadas.Remove(chave, out var antigo)) Ordem.Remove(antigo);
        Guardadas[chave] = Ordem.AddLast((chave, fonte));
        while (Ordem.Count > MaximoGuardado && Ordem.First is { } primeiro)
        {
            Guardadas.Remove(primeiro.Value.Chave);
            Ordem.RemoveFirst();
        }
    }
}
