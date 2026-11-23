using Microsoft.UI.Xaml.Controls;
using Pmvni.Revisao;

namespace Pmvni.App;

public sealed partial class MainWindow
{
    async Task EmitirProcessoAprovadoAsync()
    {
        var arquivos = await Perguntas.Atual.AbrirArquivosAsync(".pdf");
        if (arquivos.Count == 0) return;
        var primeiraPrancha = await File.ReadAllBytesAsync(arquivos[0]);
        var primeiraMarca = await JanelaAprovacao.SelecionarAsync(primeiraPrancha, Path.GetFileName(arquivos[0]), TemaAtual);
        if (primeiraMarca is null) return;
        if (!ajustes.AssinaturaConfigurada)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Assinatura do analista", "Configure seu certificado em Ajustes antes da emissão.");
            return;
        }
        if (!CertificacaoPrancha.DisponivelNesteWindows())
        {
            if (!File.Exists(CertificacaoPrancha.PacoteRede))
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Certificado institucional", "Conecte este PC à rede da Engenharia para acessar o pacote de inscrição do kit PMVNI.");
                return;
            }
            var pedido = await Perguntas.Atual.PedirSenhaAsync("Habilitar emissão neste Windows",
                "Informe a senha de inscrição fornecida pela Engenharia. A chave será instalada no repositório pessoal deste usuário.", false);
            if (pedido is null) return;
            try { await Task.Run(() => CertificacaoPrancha.InstalarNesteWindows(pedido.Senha)); }
            catch (System.Security.Cryptography.CryptographicException)
            {
                MostrarMensagem(InfoBarSeverity.Error, "Inscrição do certificado", "A senha ou o pacote do kit não pôde ser validado.");
                return;
            }
        }
        var (certificado, desistir) = await PrepararAssinaturaAsync();
        using var analista = certificado;
        if (desistir || analista is null) return;
        var agora = AgoraDaAssinatura();
        var revisadas = new List<(string Caminho, byte[] Desenhada, int Pagina, int Quantidade, string Peticao)>();
        foreach (var caminho in arquivos)
        {
            var nome = Path.GetFileName(caminho);
            var peticao = PeticaoDaPrancha.Identificar(caminho);
            var desenhada = revisadas.Count == 0 ? primeiraPrancha : await File.ReadAllBytesAsync(caminho);
            int quantidade = 0, pagina;
            do
            {
                var marca = primeiraMarca ?? await JanelaAprovacao.SelecionarAsync(desenhada, nome, TemaAtual);
                primeiraMarca = null;
                if (marca is null) return;
                if (peticao is null)
                {
                    peticao = await Perguntas.Atual.PedirTextoAsync("Petição da prancha", $"Número da petição de {nome}", "");
                    if (string.IsNullOrWhiteSpace(peticao)) return;
                    peticao = peticao.Trim();
                }
                pagina = marca.Pagina;
                desenhada = await Task.Run(() => CarimboAprovado.Desenhar(desenhada, pagina, marca.X, marca.Y, peticao,
                    analista, agora, marca.Observacoes, marca.Bloco, marca.TemCarimbo));
                quantidade++;
                var escolha = await JanelaAprovacao.RevisarAsync(desenhada, nome, pagina, TemaAtual);
                if (escolha is null) return;
                if (escolha == false) break;
            } while (true);
            revisadas.Add((caminho, desenhada, pagina, quantidade, peticao));
        }
        var pasta = await Perguntas.Atual.EscolherPastaAsync();
        if (pasta is null) return;
        var marcacoes = new List<(string Nome, string Destino, byte[] Desenhada, int Pagina, int Quantidade, string Peticao, bool Substituir)>();
        var origens = arquivos.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var destinos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var revisada in revisadas)
        {
            var nome = Path.GetFileNameWithoutExtension(revisada.Caminho);
            var destino = Path.GetFullPath(Path.Combine(pasta, nome.ToUpperInvariant() + " - APROVADO.PDF"));
            if (origens.Contains(destino) || !destinos.Add(destino))
                throw new IOException("Dois arquivos da seleção têm o mesmo destino. Escolha arquivos com nomes distintos.");
            var substituir = File.Exists(destino);
            if (substituir && !await Perguntas.Atual.ConfirmarAsync("Substituir a cópia aprovada?", destino, "Substituir", "Cancelar")) return;
            marcacoes.Add((nome, destino, revisada.Desenhada, revisada.Pagina, revisada.Quantidade, revisada.Peticao, substituir));
        }
        var resumo = string.Join(Environment.NewLine, marcacoes.Select(i => $"{i.Nome} · petição {i.Peticao} · {i.Quantidade} marcação(ões)"));
        if (!await Perguntas.Atual.ConfirmarAsync("Emitir as pranchas aprovadas?", $"{pasta}\n\n{resumo}", "Emitir", "Cancelar", false)) return;
        var preparadas = new List<(string Temporario, string Destino, bool Substituir)>();
        try
        {
            foreach (var item in marcacoes)
            {
                var bytes = await Task.Run(() =>
                {
                    var visto = CarimboAprovado.Assinar(item.Desenhada, item.Pagina, analista, agora);
                    return CertificacaoPrancha.Emitir(visto, agora);
                });
                var temporario = Path.Combine(pasta, $".pmvni-{Guid.NewGuid():N}.tmp");
                preparadas.Add((temporario, item.Destino, item.Substituir));
                await File.WriteAllBytesAsync(ArquivoGerado.Caminho(temporario), bytes);
            }
            foreach (var item in preparadas) File.Move(item.Temporario, ArquivoGerado.Caminho(item.Destino), item.Substituir);
        }
        catch
        {
            NotificacoesWindows.Mostrar("Emissão de pranchas interrompida", "Confira a mensagem no Editor e os arquivos na pasta de destino.");
            throw;
        }
        finally
        {
            foreach (var item in preparadas) File.Delete(item.Temporario);
        }
        MostrarMensagem(InfoBarSeverity.Success, "Processo aprovado", $"{preparadas.Count} PDF(s) emitido(s) em {pasta}.");
        NotificacoesWindows.Mostrar("Pranchas aprovadas emitidas", preparadas.Count == 1
            ? "A prancha aprovada foi gravada em PDF."
            : $"{preparadas.Count} pranchas aprovadas foram gravadas em PDF.");
    }
}
