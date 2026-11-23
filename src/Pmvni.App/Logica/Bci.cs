namespace Pmvni.App;

/// <summary>Boletim de Cadastro Imobiliário (BCI) oficial do GPI (2026-11-17), a partir da linha do módulo Imóveis. O núcleo aqui não conhece o
/// WebView2: o adaptador (<see cref="IAdaptadorGpi"/>) faz o que a tela do GPI pede. Baseado na proposta do Codex (CoordenadorBci.referencia.cs).</summary>
public sealed record CandidatoGpi(string InscricaoMunicipal, string TipoCadastro);
public sealed record FichaGpi(string InscricaoMunicipal, string Ano, bool SomenteConsulta);

public enum EstadoBci { Aberto, LoginNecessario, Ocupado, NaoEncontrado, Ambiguo, InscricaoInvalida, ConferenciaFalhou, SemTela, Cancelado, TempoEsgotado, Falha }

/// <summary>O resultado; <see cref="Pdf"/> são os bytes do PDF oficial (só em Aberto) e <see cref="Passo"/> onde parou (diagnóstico).</summary>
public sealed record ResultadoBci(EstadoBci Estado, string? Ano = null, string? Detalhe = null, byte[]? Pdf = null, string? Passo = null);

public static class ChaveBci
{
    /// <summary>A inscrição municipal para a consulta remota: exatamente 14 algarismos; só pontos, hífens e espaços de apresentação são aceitos.
    /// Nada é corrigido (o normalizador territorial de Imóveis continua só para o mapa).</summary>
    public static string? Estrita(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        if (valor.Any(c => !(char.IsAsciiDigit(c) || c is '.' or '-' || char.IsWhiteSpace(c)))) return null;
        var d = new string(valor.Where(char.IsAsciiDigit).ToArray());
        return d.Length == 14 ? d : null;
    }

    public static bool EhImobiliario(string tipo)
    {
        var t = tipo.Trim().TrimStart('0', '1', ' ').Trim();
        return t.Equals("Cadastro Imobiliario", StringComparison.OrdinalIgnoreCase) || t.Equals("Cadastro Imobiliário", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>O texto da barra de estado para cada resultado.</summary>
    public static string Texto(ResultadoBci r) => r.Estado switch
    {
        EstadoBci.Aberto => "Boletim cadastral gerado" + (r.Ano is { Length: > 0 } a ? " (ano " + a + ")" : "") + ".",
        EstadoBci.LoginNecessario => "O GPI pediu login. Entre pelo GPI na barra lateral (ou guarde o login em Ajustes › Pastas e fontes de dados) e clique de novo.",
        EstadoBci.Ocupado => "Já há um boletim sendo gerado. Aguarde terminar.",
        EstadoBci.NaoEncontrado => "A inscrição não foi encontrada no GPI.",
        EstadoBci.Ambiguo => "Há mais de um cadastro imobiliário para esta inscrição. Confira no GPI.",
        EstadoBci.InscricaoInvalida => "A inscrição desta linha não tem os 14 algarismos da inscrição municipal; consulte direto no GPI.",
        EstadoBci.ConferenciaFalhou => "A ficha aberta no GPI não corresponde ao imóvel escolhido; nada foi emitido." + (r.Detalhe is { } d ? " " + d : ""),
        EstadoBci.SemTela => "Não foi possível abrir a tela Contribuintes Manutenção do GPI (a tela do GPI pode ter mudado). Use Abrir no GPI para terminar por lá.",
        EstadoBci.Cancelado => "Emissão cancelada.",
        EstadoBci.TempoEsgotado => "O GPI demorou para responder. Você pode tentar novamente." + (r.Detalhe is { } t ? " (" + t + ")" : ""),
        _ => "Não foi possível gerar o boletim." + (r.Detalhe is { } f ? " " + f : ""),
    };
}

public interface IAdaptadorGpi
{
    /// <summary>true se há sessão (nenhum campo de senha à vista); se o app entrar sozinho com o login guardado, espera e confere de novo.</summary>
    Task<bool> GarantirSessaoAsync(CancellationToken ct);
    Task<bool> AbrirManutencaoAsync(CancellationToken ct);
    /// <summary>Inscrição Municipal + Igual + a chave; espera o resultado NOVO e devolve as linhas exatas (todas as grades visíveis).</summary>
    Task<IReadOnlyList<CandidatoGpi>> PesquisarExatoAsync(string inscricao, CancellationToken ct);
    Task<FichaGpi?> AbrirConsultaAsync(string inscricao, CancellationToken ct);
    /// <summary>Confere a ficha de novo, clica uma vez em "Boletim Cadastral" e devolve os bytes do PDF oficial (null se não veio PDF).</summary>
    Task<byte[]?> EmitirAsync(string inscricao, CancellationToken ct);
}

/// <summary>Uma emissão por vez (todas as linhas compartilham), com tempo-limite e conferência da identidade em cada passo.</summary>
public sealed class CoordenadorBci(IAdaptadorGpi gpi, Action<Exception> registrar)
{
    readonly SemaphoreSlim exclusao = new(1, 1);
    public static TimeSpan Limite { get; set; } = TimeSpan.FromSeconds(120);

    public async Task<ResultadoBci> GerarAsync(string inscricaoDaLinha, CancellationToken ct)
    {
        var chave = ChaveBci.Estrita(inscricaoDaLinha);
        if (chave is null) return new(EstadoBci.InscricaoInvalida);
        if (!await exclusao.WaitAsync(0, CancellationToken.None)) return new(EstadoBci.Ocupado);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(Limite);
        var token = limite.Token;
        var passo = "sessão";
        try
        {
            if (!await gpi.GarantirSessaoAsync(token)) return new(EstadoBci.LoginNecessario);
            passo = "abrir a manutenção";
            if (!await gpi.AbrirManutencaoAsync(token)) return new(EstadoBci.SemTela, Passo: passo);
            passo = "pesquisa";
            var exatos = (await gpi.PesquisarExatoAsync(chave, token))
                .Where(c => ChaveBci.Estrita(c.InscricaoMunicipal) == chave && ChaveBci.EhImobiliario(c.TipoCadastro)).ToList();
            if (exatos.Count == 0) return new(EstadoBci.NaoEncontrado);
            if (exatos.Count > 1) return new(EstadoBci.Ambiguo);
            passo = "ficha";
            var ficha = await gpi.AbrirConsultaAsync(chave, token);
            if (ficha is null) return new(EstadoBci.ConferenciaFalhou, Detalhe: "A ficha não abriu.");
            if (ChaveBci.Estrita(ficha.InscricaoMunicipal) != chave) return new(EstadoBci.ConferenciaFalhou, Detalhe: "Inscrição da ficha: " + ficha.InscricaoMunicipal + ".");
            if (!ficha.SomenteConsulta) return new(EstadoBci.ConferenciaFalhou, Detalhe: "A ficha não está em modo Consulta.");
            passo = "emissão";
            var pdf = await gpi.EmitirAsync(chave, token);
            return pdf is { Length: > 4 } && pdf[0] == '%' && pdf[1] == 'P' && pdf[2] == 'D' && pdf[3] == 'F'
                ? new(EstadoBci.Aberto, ficha.Ano, Pdf: pdf, Passo: passo)
                : new(EstadoBci.Falha, Detalhe: "O GPI não devolveu o PDF do boletim.", Passo: passo);
        }
        catch (OperationCanceledException) { return new(ct.IsCancellationRequested ? EstadoBci.Cancelado : EstadoBci.TempoEsgotado, Detalhe: "parou em: " + passo, Passo: passo); }
        catch (Exception e) { registrar(e); return new(EstadoBci.Falha, Detalhe: "Parou em: " + passo + " (" + e.Message + ").", Passo: passo); }
        finally { exclusao.Release(); }
    }
}
