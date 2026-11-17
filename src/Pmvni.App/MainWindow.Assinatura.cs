using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Assinatura digital dos despachos (2026-11-16): a seção de Ajustes › Despachos e PDF (escolher o arquivo .pfx/.p12 ou um
/// certificado do Windows, "Conferir", "Remover" e "Lembrar a senha neste computador") e a abertura do certificado na hora de gerar o
/// PDF. A senha é pedida numa caixa de senha a cada assinatura, ou lida do <see cref="CofreSenhaCertificado"/> (DPAPI, por usuário)
/// quando o usuário pede para lembrar; nunca vai para o <c>ajustes.json</c>, para mensagem, registro ou arquivo do projeto.</summary>
public sealed partial class MainWindow
{
    /// <summary>O que o "Conferir" (ou a lista do Windows) leu do certificado escolhido, para mostrar na página; some ao trocar de certificado.</summary>
    InfoCertificado? certificadoConferido;

    // ---------- acessores do roteiro ----------
    internal TextBlock TextoAssinaturaNosAjustes => PaginaDeAjustes.TextoAssinatura;
    /// <summary>A hora da assinatura (o roteiro fixa).</summary>
    internal Func<DateTimeOffset> AgoraDaAssinatura { get; set; } = () => DateTimeOffset.Now;

    void IniciarAssinatura()
    {
        var p = PaginaDeAjustes;
        p.BotaoEscolherCertificado.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(EscolherArquivoDoCertificadoAsync));
        p.BotaoCertificadoWindows.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(EscolherCertificadoDoWindowsAsync));
        p.BotaoConferirCertificado.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(ConferirCertificadoAsync));
        p.BotaoRemoverCertificado.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => { DefinirCertificado("nenhuma", null); return Task.CompletedTask; }));
        p.AlternarLembrarSenha.Toggled += (_, _) =>
        {
            if (atualizandoAssinatura || p.AlternarLembrarSenha.IsOn == ajustes.AssinaturaLembrarSenha) return;
            ajustes.AssinaturaLembrarSenha = p.AlternarLembrarSenha.IsOn && ajustes.AssinaturaOrigem == "arquivo";
            // Desligar apaga a senha guardada na hora; ligar só guarda na próxima vez que a senha for digitada e conferir.
            if (!ajustes.AssinaturaLembrarSenha) CofreSenhaCertificado.Remover(PastaDados.Raiz);
            SalvarAjustes();
            MostrarAssinaturaNosAjustes();
        };
        MostrarAssinaturaNosAjustes();
    }

    bool atualizandoAssinatura;

    /// <summary>A seção Assinatura digital como os ajustes dizem (também depois de "Restaurar os ajustes").</summary>
    internal void MostrarAssinaturaNosAjustes()
    {
        if (fechada) return;
        var p = PaginaDeAjustes;
        atualizandoAssinatura = true;
        try
        {
            p.TextoAssinatura.Text = TextosAssinatura.Estado(ajustes, certificadoConferido);
            p.BotaoConferirCertificado.IsEnabled = p.BotaoRemoverCertificado.IsEnabled = ajustes.AssinaturaConfigurada;
            // A senha só existe para o arquivo: o token ou cartão pede a dele por conta própria.
            p.AlternarLembrarSenha.IsEnabled = ajustes.AssinaturaOrigem == "arquivo";
            p.AlternarLembrarSenha.IsOn = ajustes.AssinaturaLembrarSenha;
        }
        finally { atualizandoAssinatura = false; }
    }

    /// <summary>Troca o certificado (ou tira): a senha lembrada do anterior sai, e a conferência antiga some da página.</summary>
    internal void DefinirCertificado(string origem, string? valor, InfoCertificado? info = null)
    {
        var antes = ajustes.AssinaturaIdentidade;
        ajustes.DefinirAssinatura(origem, valor);
        if (!string.Equals(antes, ajustes.AssinaturaIdentidade, StringComparison.OrdinalIgnoreCase) || !ajustes.AssinaturaLembrarSenha)
            CofreSenhaCertificado.Remover(PastaDados.Raiz);
        certificadoConferido = info;
        SalvarAjustes();
        PaginaDeAjustes.TextoConferenciaCertificado.Visibility = Visibility.Collapsed;
        MostrarAssinaturaNosAjustes();
    }

    async Task EscolherArquivoDoCertificadoAsync()
    {
        var arquivo = await Perguntas.Atual.AbrirArquivoAsync(".pfx;.p12");
        if (arquivo is null) return;
        if (Path.GetExtension(arquivo).ToLowerInvariant() is not (".pfx" or ".p12"))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Arquivo não aceito", "Escolha o arquivo do certificado digital (.pfx ou .p12).");
            return;
        }
        DefinirCertificado("arquivo", arquivo);
        MostrarConferencia("Certificado escolhido. Clique em \"Conferir\" para ver o titular e a validade (a senha é pedida).");
    }

    async Task EscolherCertificadoDoWindowsAsync()
    {
        var lista = await Task.Run(() => CertificadoDigital.ListarDoWindows(DateTimeOffset.Now));
        if (lista.Count == 0)
        {
            MostrarConferencia("Nenhum certificado de assinatura válido foi encontrado no Windows. Conecte o token ou o cartão (ou instale o certificado) e tente de novo.");
            return;
        }
        var i = await Perguntas.Atual.EscolherNaListaAsync("Certificado do Windows",
            "Escolha o certificado que assina os despachos. A senha (PIN) do token ou do cartão é pedida pelo próprio dispositivo na hora de assinar.",
            lista.Select(TextosAssinatura.ItemDaLista).ToList());
        if (i is not { } escolhido) return;
        DefinirCertificado("windows", lista[escolhido].ImpressaoDigital, lista[escolhido]);
        MostrarConferencia(TextosAssinatura.Conferido(lista[escolhido], null));
    }

    /// <summary>"Conferir": abre o certificado (pedindo a senha do arquivo) e mostra o titular e a validade. Nada é assinado.</summary>
    async Task ConferirCertificadoAsync()
    {
        if (!ajustes.AssinaturaConfigurada) return;
        try
        {
            using var c = await AbrirCertificadoDaAssinaturaAsync(paraConferir: true);
            if (c is null) return; // cancelou a senha
            certificadoConferido = c.Info;
            MostrarAssinaturaNosAjustes();
            MostrarConferencia(TextosAssinatura.Conferido(c.Info, CertificadoDigital.Problema(c.Certificado, DateTimeOffset.Now)));
        }
        catch (AssinaturaDigitalException e) { MostrarConferencia(e.Message); }
    }

    void MostrarConferencia(string texto)
    {
        if (fechada) return;
        var t = PaginaDeAjustes.TextoConferenciaCertificado;
        t.Text = texto;
        t.Visibility = Visibility.Visible;
    }

    /// <summary>O certificado configurado, aberto e pronto para assinar. Sem certificado configurado: null (o despacho sai sem assinatura).
    /// Arquivo: usa a senha lembrada (se houver e ainda servir) ou pede numa caixa de senha; cancelar a caixa devolve null
    /// (quem chama não gera o despacho). Senha errada, arquivo ausente, certificado vencido:
    /// <see cref="AssinaturaDigitalException"/> com a mensagem (a senha lembrada que não serve mais é apagada antes).</summary>
    async Task<CertificadoAberto?> AbrirCertificadoDaAssinaturaAsync(bool paraConferir = false)
    {
        if (!ajustes.AssinaturaConfigurada) return null;
        if (ajustes.AssinaturaOrigem == "windows")
        {
            var digital = ajustes.AssinaturaImpressaoDigital!;
            return await Task.Run(() => CertificadoDigital.AbrirDoWindows(digital));
        }

        var arquivo = ajustes.AssinaturaArquivo!;
        if (!File.Exists(arquivo)) throw new AssinaturaDigitalException(CertificadoDigital.ArquivoAusente);
        if (ajustes.AssinaturaLembrarSenha && CofreSenhaCertificado.Ler(PastaDados.Raiz, arquivo) is { } lembrada)
        {
            try { return await Task.Run(() => CertificadoDigital.AbrirArquivo(arquivo, lembrada)); }
            catch (AssinaturaDigitalException e) when (e.Message == CertificadoDigital.SenhaIncorreta)
            {
                // A senha guardada não abre mais este arquivo (o certificado foi trocado ou renovado): sai do cofre e a caixa pede a nova.
                CofreSenhaCertificado.Remover(PastaDados.Raiz);
            }
        }
        var resposta = await Perguntas.Atual.PedirSenhaAsync(TextosAssinatura.PedirSenhaTitulo,
            paraConferir ? "Digite a senha do certificado " + Path.GetFileName(arquivo) + " para conferir o titular e a validade. Nada é assinado."
                : TextosAssinatura.PedirSenhaTexto(arquivo),
            ajustes.AssinaturaLembrarSenha);
        if (resposta is null) return null;
        var aberto = await Task.Run(() => CertificadoDigital.AbrirArquivo(arquivo, resposta.Senha));
        // Só uma senha que abriu o certificado é guardada; desmarcar "lembrar" na caixa apaga a que houver.
        if (resposta.Lembrar != ajustes.AssinaturaLembrarSenha)
        {
            ajustes.AssinaturaLembrarSenha = resposta.Lembrar;
            SalvarAjustes();
        }
        try
        {
            if (resposta.Lembrar) CofreSenhaCertificado.Guardar(PastaDados.Raiz, arquivo, resposta.Senha);
            else CofreSenhaCertificado.Remover(PastaDados.Raiz);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            // Não guardou: a senha será pedida de novo na próxima vez; a assinatura de agora segue.
            RegistroErros.Gravar("assinatura", e);
        }
        MostrarAssinaturaNosAjustes();
        return aberto;
    }

    /// <summary>Quem assina o PDF do despacho que vai ser gerado agora. (null, false) = sem certificado configurado: o despacho sai como
    /// sempre. (null, true) = não dá para assinar (senha cancelada ou errada, arquivo ausente, certificado vencido): o motivo já está na
    /// página e o despacho NÃO é gerado. O certificado devolvido é descartado por quem chama, depois do preparo.</summary>
    async Task<(CertificadoAberto? Certificado, bool Desistir)> PrepararAssinaturaAsync()
    {
        if (!ajustes.AssinaturaConfigurada) return (null, false);
        try
        {
            var c = await AbrirCertificadoDaAssinaturaAsync();
            if (c is null)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Assinatura digital", TextosAssinatura.SenhaNaoInformada);
                return (null, true);
            }
            if (CertificadoDigital.Problema(c.Certificado, AgoraDaAssinatura()) is { } problema)
            {
                c.Dispose();
                MostrarMensagem(InfoBarSeverity.Error, "Assinatura digital", problema + " O despacho não foi gerado.");
                return (null, true);
            }
            return (c, false);
        }
        catch (AssinaturaDigitalException e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Assinatura digital", e.Message);
            return (null, true);
        }
    }
}
