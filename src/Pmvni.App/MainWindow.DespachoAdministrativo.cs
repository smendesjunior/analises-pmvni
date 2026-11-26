using Microsoft.UI.Xaml.Controls;
using Pmvni.Revisao;

namespace Pmvni.App;

public sealed partial class MainWindow
{
    async Task CriarAdministrativoAsync(string tipo)
    {
        var nome = "MODELO DESPACHO " + tipo.ToUpperInvariant() + ".docx";
        var modelo = Path.Combine(PastaKitEfetiva, "02 - MODELOS DE DOCUMENTOS", "DESPACHO", nome);
        var formulario = new FormularioDespacho(await File.ReadAllBytesAsync(modelo));
        var processo = moduloAtual switch
        {
            Modulo.Processos => processoEscolhido,
            Modulo.Despachos => despachoEscolhido?.Despacho.Processo ?? processoEscolhido,
            _ => null
        };
        var painel = new StackPanel { Spacing = 8, MinWidth = 420 };
        var caixas = new Dictionary<string, TextBox>();
        var dialogo = new ContentDialog
        {
            XamlRoot = PaginaDeDespachos.XamlRoot, Title = "Despacho • " + tipo,
            Content = new ScrollViewer { Content = painel, MaxHeight = 480 },
            PrimaryButtonText = "Salvar DOCX", CloseButtonText = "Cancelar"
        };
        foreach (var campo in formulario.Campos)
        {
            var caixa = new TextBox
            {
                Header = campo, AcceptsReturn = campo == "Texto", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Text = campo switch
                {
                    "Data" => Etapas.DataPorExtenso(Hoje()),
                    "Prazo" => "24 horas",
                    "Encaminhamento" => "A solicitação foi atendida. Arquive-se.",
                    "Protocolo" => processo?.Protocolo ?? "",
                    "Requerente" => processo?.Requerente ?? "",
                    _ => ""
                }
            };
            caixas.Add(campo, caixa);
            painel.Children.Add(caixa);
            caixa.TextChanged += (_, _) => Atualizar();
        }
        void Atualizar() => dialogo.IsPrimaryButtonEnabled = caixas.Values.All(c => !string.IsNullOrWhiteSpace(c.Text));
        Atualizar();
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        var bytes = formulario.Preencher(caixas.ToDictionary(x => x.Key, x => x.Value.Text));
        var caminho = await Perguntas.Atual.SalvarArquivoAsync("DESPACHO " + tipo, ".docx");
        if (caminho is null) return;
        if (string.Equals(Path.GetFullPath(caminho), Path.GetFullPath(modelo), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Escolha um arquivo de saída diferente do modelo institucional.");
        var temporario = caminho + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporario, bytes);
            File.Move(temporario, caminho, true);
        }
        finally { File.Delete(temporario); }
        AbrirNoPrograma(caminho);
    }
}
