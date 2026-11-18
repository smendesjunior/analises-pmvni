using Microsoft.UI.Xaml.Controls;
using Pmvni.Revisao;

namespace Pmvni.App;

public sealed partial class MainWindow
{
    async Task EmitirProcessoAprovadoAsync()
    {
        var peticao = sessao?.Atual?.Dados.Processo?.Trim();
        if (string.IsNullOrWhiteSpace(peticao))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Emitir Processo Aprovado", "Abra um relatório com o número da petição do processo.");
            return;
        }
        if (!ajustes.AssinaturaConfigurada)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Assinatura do analista", "Configure seu certificado em Ajustes antes da emissão.");
            return;
        }
        var arquivos = await Perguntas.Atual.AbrirArquivosAsync(".pdf");
        if (arquivos.Count == 0) return;
        var pasta = await Perguntas.Atual.EscolherPastaAsync();
        if (pasta is null) return;
        var marcacoes = new List<(string Nome, string Destino, byte[] Original, MarcacaoAprovacao Marca, bool Substituir)>();
        var origens = arquivos.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var destinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var caminho in arquivos)
        {
            var nome = Path.GetFileNameWithoutExtension(caminho);
            var destino = Path.GetFullPath(Path.Combine(pasta, nome + " - Aprovado.pdf"));
            if (origens.Contains(destino) || !destinos.Add(destino))
                throw new IOException("Dois arquivos da seleção têm o mesmo destino. Escolha arquivos com nomes distintos.");
            var substituir = File.Exists(destino);
            if (substituir && !await Perguntas.Atual.ConfirmarAsync("Substituir a cópia aprovada?", destino, "Substituir", "Cancelar")) return;
            var bytes = await File.ReadAllBytesAsync(caminho);
            var marca = await JanelaAprovacao.SelecionarAsync(bytes, nome, TemaAtual);
            if (marca is null) return;
            marcacoes.Add((nome, destino, bytes, marca, substituir));
        }
        var resumo = string.Join(Environment.NewLine, marcacoes.Select(i => $"{i.Nome} · carimbo na página {i.Marca.Pagina + 1} · {i.Marca.Observacoes.Count} observações"));
        if (!await Perguntas.Atual.ConfirmarAsync("Emitir as pranchas aprovadas?", $"{peticao}\n{pasta}\n\n{resumo}", "Emitir", "Cancelar", false)) return;
        var (certificado, desistir) = await PrepararAssinaturaAsync();
        using var analista = certificado;
        if (desistir || analista is null) return;
        var agora = AgoraDaAssinatura();
        var preparadas = new List<(string Temporario, string Destino, bool Substituir)>();
        try
        {
            foreach (var item in marcacoes)
            {
                var marca = item.Marca;
                var bytes = await Task.Run(() =>
                {
                    var visto = CarimboAprovado.Aplicar(item.Original, marca.Pagina, marca.X, marca.Y, peticao, analista, agora, marca.Observacoes, marca.Bloco);
                    return CertificacaoPrancha.Emitir(visto, agora);
                });
                var temporario = Path.Combine(pasta, $".pmvni-{Guid.NewGuid():N}.tmp");
                preparadas.Add((temporario, item.Destino, item.Substituir));
                await File.WriteAllBytesAsync(temporario, bytes);
            }
            foreach (var item in preparadas) File.Move(item.Temporario, item.Destino, item.Substituir);
        }
        finally
        {
            foreach (var item in preparadas) File.Delete(item.Temporario);
        }
        MostrarMensagem(InfoBarSeverity.Success, "Processo aprovado", $"{preparadas.Count} PDF(s) emitido(s) em {pasta}.");
    }
}
