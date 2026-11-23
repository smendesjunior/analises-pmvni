using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>A senha digitada na caixa de senha do certificado e se é para lembrá-la neste computador.</summary>
public sealed record RespostaSenha(string Senha, bool Lembrar);

/// <summary>Os textos da assinatura digital dos despachos (2026-11-16) em Ajustes › Profissional e assinatura e na conferência, sem tipos WinUI
/// (compilado também nos testes). Nenhum deles leva senha.</summary>
public static class TextosAssinatura
{
    public const string SemCertificado = "Nenhum certificado escolhido: os despachos saem sem assinatura digital.";

    /// <summary>O aviso discreto da conferência quando não há certificado configurado.</summary>
    public const string ConferenciaSemAssinatura = "Sem assinatura digital (escolha o certificado em Ajustes › Profissional e assinatura)";

    public const string SenhaNaoInformada = "A senha do certificado não foi informada; o despacho não foi gerado.";

    public const string PedirSenhaTitulo = "Senha do certificado digital";

    /// <summary>O texto da caixa de senha: de qual certificado e para quê.</summary>
    public static string PedirSenhaTexto(string arquivo) =>
        "Digite a senha do certificado " + Path.GetFileName(arquivo) + " para assinar o despacho. A senha não aparece na tela nem fica em registro.";

    /// <summary>O que a página de Ajustes mostra sobre o certificado escolhido; <paramref name="conferido"/> = os dados lidos pelo
    /// "Conferir" (ou do repositório do Windows), se houver.</summary>
    public static string Estado(Ajustes a, InfoCertificado? conferido)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (!a.AssinaturaConfigurada) return SemCertificado;
        var origem = a.AssinaturaOrigem == "arquivo"
            ? "Arquivo: " + a.AssinaturaArquivo + (File.Exists(a.AssinaturaArquivo) ? "" : "  (não encontrado)")
            : "Certificado do Windows (token, cartão ou instalado): " + a.AssinaturaImpressaoDigital;
        return conferido is null ? origem : origem + Environment.NewLine + conferido.Resumo;
    }

    /// <summary>O resultado do "Conferir": o titular e a validade, e o que impede assinar agora, se algo impede.</summary>
    public static string Conferido(InfoCertificado info, string? problema) =>
        "Titular: " + info.Titular + ". Válido de " + info.ValidoDe.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
        + " a " + info.ValidoAte.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
        + (info.Emissor.Length == 0 || info.Emissor == info.Titular ? "" : ". Emitido por " + info.Emissor)
        + (problema is null ? ". Pronto para assinar os despachos." : ". " + problema);

    /// <summary>O que a conferência mostra ao lado das fontes do PDF: quem assinou, ou o aviso de que saiu sem assinatura.</summary>
    public static string NaConferencia(AssinaturaAplicada? aplicada) =>
        aplicada is null ? ConferenciaSemAssinatura : "Assinado digitalmente por " + aplicada.Certificado.Titular;

    /// <summary>Uma linha da lista de certificados do Windows.</summary>
    public static string ItemDaLista(InfoCertificado i) => i.Resumo;
}
