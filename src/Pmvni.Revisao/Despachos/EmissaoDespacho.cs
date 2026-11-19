using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Composicao;

namespace Pmvni.Revisao;

/// <summary>O que foi gerado na pasta de trabalho (nada ainda na pasta do processo). <see cref="Erros"/> ≠ [] impede gravar; sem PDF,
/// <see cref="SemPdfMotivo"/> (<c>SEM_LIBREOFFICE</c>, <c>CONVERSAO_FALHOU</c>, <c>TEMPO_ESGOTADO</c>, <c>SEM_VERDANA</c>) e a mensagem.</summary>
public sealed record Preparacao(string PastaTrabalho, byte[] Docx, byte[]? Pdf, int? Paginas, IReadOnlyList<string> Fontes, string? SemPdfMotivo,
    string? MensagemSemPdf, IReadOnlyList<string> Erros)
{
    /// <summary>A assinatura digital aplicada ao <see cref="Pdf"/> (2026-11-16); null = o PDF não foi assinado (sem certificado
    /// configurado, ou sem PDF).</summary>
    public AssinaturaAplicada? Assinatura { get; init; }
}

/// <summary>A assinatura digital que o PDF do despacho recebeu: de quem é o certificado, a hora e onde a aparência visível ficou.</summary>
public sealed record AssinaturaAplicada(InfoCertificado Certificado, DateTimeOffset Quando, LocalAssinatura Local);

/// <summary>De onde o despacho veio (vai para o recibo).</summary>
public sealed record OrigemDespacho(Processo Processo, string CaminhoPacote, string Sha256Pacote, string Sha256DadosEfetivos, int HistoricoN,
    string AppVersao, IReadOnlyList<string> AvisosConfirmados);

/// <summary>Os arquivos gravados na pasta da etapa.</summary>
public sealed record Entrega(string Pasta, string Docx, string? Pdf, string Recibo);

/// <summary>Emissão do despacho (desenho §10.6–10.9): preparar na pasta de trabalho (DOCX, validação, PDF) e, só depois da conferência das
/// páginas, gravar na pasta do processo sem nunca sobrescrever. Tudo síncrono, para <c>Task.Run</c>. Nada grava no kit.</summary>
public static class EmissaoDespacho
{
    const string SemRecibo = "Este despacho não foi gerado pelo editor (sem recibo); o PDF não é gerado por aqui.";
    const string DocxNaoConfere =
        "O DOCX não é mais o que o editor gravou (não confere com o recibo); o PDF não é gerado por aqui. Gere um novo despacho.";
    const int TentativasDeNome = 100;

    public static Preparacao Preparar(DespachoFinal d, ModeloDespacho m, string pastaTrabalho, IConversorPdf conv, CancellationToken ct) =>
        Preparar(d, m, pastaTrabalho, conv, null, ct);

    /// <summary>Com <paramref name="assinador"/>, o PDF gerado é assinado digitalmente (assinatura visível acima da linha de assinatura)
    /// antes da conferência: é o PDF assinado que se confere e que vai para a pasta do processo. Se a assinatura falhar, o motivo vem em
    /// <see cref="Preparacao.Erros"/> e nada pode ser gravado. Sem PDF (sem LibreOffice, conversão que falhou), nada é assinado.</summary>
    public static Preparacao Preparar(DespachoFinal d, ModeloDespacho m, string pastaTrabalho, IConversorPdf conv, IAssinadorPdf? assinador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(pastaTrabalho);
        ArgumentNullException.ThrowIfNull(conv);
        if (d.Tipo != m.Tipo) throw new ArgumentException("O modelo é de outro tipo de despacho.", nameof(m));
        ct.ThrowIfCancellationRequested();
        Preparacao Erro(params string[] erros) => new(pastaTrabalho, Array.Empty<byte>(), null, null, Array.Empty<string>(), null, null, erros);

        byte[] modelo;
        try { modelo = File.ReadAllBytes(m.Caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Erro(ModeloMudou); }
        if (!string.Equals(RegistroModelos.Sha(modelo), m.Sha256, StringComparison.OrdinalIgnoreCase)) return Erro(ModeloMudou);
        modelo = PreenchimentoDocx.ComProfissional(modelo, Pmvni.Composicao.Profissional.Atual);   // assinatura do profissional dos Ajustes

        var docx = PreenchimentoDocx.Preencher(modelo, d);
        var erros = ValidacaoDespacho.Comparar(modelo, docx).Concat(ValidacaoDespacho.Conteudo(modelo, docx, d)).ToList();
        if (erros.Count > 0) return Erro(erros.ToArray());

        var caminho = Path.Combine(pastaTrabalho, "despacho.docx");
        byte[] relido;
        try
        {
            Directory.CreateDirectory(pastaTrabalho);
            File.WriteAllBytes(caminho, docx);
            relido = File.ReadAllBytes(caminho);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Erro(Trabalho(e)); }
        erros = ValidacaoDespacho.Comparar(modelo, relido).Concat(ValidacaoDespacho.Conteudo(modelo, relido, d)).ToList();
        if (erros.Count > 0 || !relido.AsSpan().SequenceEqual(docx)) return Erro(erros.Count > 0 ? erros.ToArray() : new[] { DocxMudou });

        return Converter(pastaTrabalho, caminho, docx, conv, assinador, ct);
    }

    static string Trabalho(Exception e) => "Não foi possível gravar o despacho na pasta de trabalho (" + MensagensDeErro.ParaUsuario(e) + ").";

    const string ModeloMudou = "O modelo do kit mudou depois de aberto; abra o Novo despacho de novo.";
    const string DocxMudou = "O DOCX mudou durante a conversão; gere o despacho de novo.";

    /// <summary>Converte <paramref name="caminhoDocx"/> e confere: o DOCX não mudou (sha), o PDF abre no leitor mínimo e usa a Verdana.</summary>
    static Preparacao Converter(string pastaTrabalho, string caminhoDocx, byte[] docx, IConversorPdf conv, IAssinadorPdf? assinador, CancellationToken ct)
    {
        var sha = RegistroModelos.Sha(docx);
        ResultadoConversao r;
        try { r = conv.Disponivel ? conv.Converter(caminhoDocx, pastaTrabalho, ct) : new ResultadoConversao(null, "SEM_LIBREOFFICE", null); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Falha do conversor (arquivo preso, disco cheio, processo que não abre): sem PDF, com o motivo; nunca uma exceção crua.
            r = new ResultadoConversao(null, "CONVERSAO_FALHOU", MensagensDeErro.ParaUsuario(e));
        }
        ct.ThrowIfCancellationRequested();
        bool mudou;
        try { mudou = !File.Exists(caminhoDocx) || RegistroModelos.Sha(File.ReadAllBytes(caminhoDocx)) != sha; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { mudou = true; }
        if (mudou) return new Preparacao(pastaTrabalho, docx, null, null, Array.Empty<string>(), null, null, new[] { DocxMudou });

        Preparacao SemPdf(string motivo, string mensagem, IReadOnlyList<string>? fontes = null) =>
            new(pastaTrabalho, docx, null, null, fontes ?? Array.Empty<string>(), motivo, mensagem, Array.Empty<string>());

        if (r.Pdf is null)
            return r.Motivo == "SEM_LIBREOFFICE" || r.Motivo is null
                ? SemPdf("SEM_LIBREOFFICE", "O LibreOffice não está instalado neste computador. O despacho será gravado só em Word (DOCX). "
                                            + "Para o PDF, rode o instalador do kit (INSTALAR_KIT_PMVNI.cmd) e use Despachos › Gerar PDF.")
                : SemPdf(r.Motivo, Falhou(r.Motivo == "TEMPO_ESGOTADO" ? "tempo esgotado" : r.Detalhe ?? "sem detalhe"));

        InfoPdf info;
        try { info = InfoPdf.Ler(r.Pdf); }
        catch (InvalidDataException e) { return SemPdf("CONVERSAO_FALHOU", Falhou("PDF ilegível: " + e.Message.TrimEnd('.'))); }
        var fontes = InfoPdf.Fontes(r.Pdf);
        if (!fontes.Any(f => f.StartsWith("Verdana", StringComparison.Ordinal)))
            return SemPdf("SEM_VERDANA", "O PDF saiu sem a fonte Verdana (fontes: " + (fontes.Count == 0 ? "nenhuma" : string.Join(", ", fontes))
                                         + "); será gravado só o DOCX.", fontes);
        if (assinador is null) return new Preparacao(pastaTrabalho, docx, r.Pdf, info.Paginas, fontes, null, null, Array.Empty<string>());

        // Assinatura digital: o PDF assinado é o que se confere e o que se grava; se ela falhar, nada pode ser gravado.
        Preparacao NaoAssinado(string motivo) => new(pastaTrabalho, docx, null, null, fontes, null, null, new[] { motivo });
        PdfAssinado assinado;
        try { assinado = assinador.Assinar(r.Pdf, docx, ct); }
        catch (AssinaturaDigitalException e) { return NaoAssinado(e.Message); }
        ct.ThrowIfCancellationRequested();
        InfoPdf depois;
        try { depois = InfoPdf.Ler(assinado.Pdf); }
        catch (InvalidDataException e) { return NaoAssinado("O PDF assinado ficou ilegível (" + e.Message.TrimEnd('.') + "); nada foi gravado."); }
        if (depois.Paginas != info.Paginas) return NaoAssinado("O PDF assinado não tem as mesmas páginas do PDF gerado; nada foi gravado.");
        return new Preparacao(pastaTrabalho, docx, assinado.Pdf, info.Paginas, fontes, null, null, Array.Empty<string>())
        {
            Assinatura = new AssinaturaAplicada(assinado.Certificado, assinado.Quando, assinado.Local),
        };
    }

    static string Falhou(string detalhe) =>
        "O LibreOffice não conseguiu gerar o PDF (" + detalhe + "). O despacho será gravado só em Word (DOCX); use Despachos › Gerar PDF para tentar de novo.";

    static void ExigirPaginas(int paginas, IReadOnlyList<int>? conferidas)
    {
        if (conferidas is not null && conferidas.SequenceEqual(Enumerable.Range(1, paginas))) return;
        throw new ArgumentException(paginas == 1
            ? "Confira a página do PDF antes de gravar."
            : "Confira as " + paginas.ToString(CultureInfo.InvariantCulture) + " páginas do PDF antes de gravar.");
    }

    /// <summary>Grava DOCX, PDF (se houver) e recibo na pasta da etapa (criada se nova), cada um por um <c>.tmp</c> oculto movido sem sobrescrever;
    /// o recibo por último; depois apaga a pasta de trabalho. Em qualquer falha, desfaz tudo o que esta chamada criou (os <c>.tmp</c>, os
    /// arquivos já movidos e a pasta da etapa, se foi criada agora e ficou vazia) e relança; nunca apaga um arquivo que já existia.</summary>
    public static Entrega Gravar(Preparacao p, RascunhoDespacho r, DespachoFinal d, ModeloDespacho m, TextosFixos tx, EtapaDespacho etapa, OrigemDespacho o,
        IReadOnlyList<int>? paginasConferidas, string? motorPdf, ContextoUsuario u) =>
        Gravar(p, r, d, m, tx, etapa, o, paginasConferidas, motorPdf, u, null);

    /// <summary><paramref name="antesDeMover"/> (testes): chamado com o caminho final antes de cada troca, para simular uma falha.</summary>
    internal static Entrega Gravar(Preparacao p, RascunhoDespacho r, DespachoFinal d, ModeloDespacho m, TextosFixos tx, EtapaDespacho etapa,
        OrigemDespacho o, IReadOnlyList<int>? paginasConferidas, string? motorPdf, ContextoUsuario u, Action<string>? antesDeMover)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(etapa);
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(u);
        if (p.Erros.Count > 0) throw new InvalidOperationException(string.Join(" ", p.Erros));
        if (p.Pdf is not null) ExigirPaginas(p.Paginas ?? 0, paginasConferidas);

        var lido = ConteudoDespacho.ProcessoDaCapa(d.Protocolo);
        var numero = o.Processo.Identificador ?? (o.Processo.Numero ?? lido?.Numero)?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var ano = o.Processo.Ano ?? lido?.Ano;
        if (numero is null || ano is null) throw new InvalidOperationException(
                "O processo da capa (" + d.Protocolo + ") não está no formato número/ano; confira o relatório.");
        var nomeBase = o.Processo.Numero is { } legado
            ? NomesDespacho.Base(d.Tipo, legado, ano.Value, d.Requerente)
            : NomesDespacho.Base(d.Tipo, numero, ano.Value, d.Requerente);

        var criouPasta = false;
        if (etapa.Nova)
        {
            criouPasta = !Directory.Exists(etapa.Pasta);
            Directory.CreateDirectory(etapa.Pasta);
        }
        else if (!Directory.Exists(etapa.Pasta)) throw new DirectoryNotFoundException("A pasta da etapa não existe mais: " + etapa.Pasta);

        for (var tentativa = 0; ; tentativa++)
        {
            NomesLivres livres;
            var criados = new List<string>();
            try
            {
                // Dentro do try: uma falha aqui (pasta ilegível) também desfaz a pasta da etapa criada agora.
                livres = NomesDespacho.Livres(etapa.Pasta, nomeBase);
                Mover(etapa.Pasta, p.Docx, livres.Docx, criados, antesDeMover);
                JsonObject? pdf = null, conferidas = null;
                if (p.Pdf is not null)
                {
                    Mover(etapa.Pasta, p.Pdf, livres.Pdf, criados, antesDeMover);
                    pdf = ReciboDespacho.Pdf(livres.Pdf, p.Pdf, p.Paginas!.Value, p.Fontes, motorPdf, p.Assinatura);
                    conferidas = ReciboDespacho.Conferidas(paginasConferidas!, u);
                }
                var recibo = ReciboDespacho.Montar(r, d, m, tx, etapa, o, (numero, ano.Value), livres.Docx, p.Docx, pdf, p.Pdf is null ? p.SemPdfMotivo : null,
                    conferidas, u);
                Mover(etapa.Pasta, ReciboDespacho.Escrever(recibo), livres.Recibo, criados, antesDeMover);
            }
            catch (NomeOcupadoException) when (tentativa < TentativasDeNome)
            {
                // Outro processo criou um dos nomes no meio: os que este gravou saem e recomeça com o próximo nome livre.
                foreach (var c in criados) ApagarSeguro(c);
                continue;
            }
            catch
            {
                // Qualquer outra falha (disco cheio, OneDrive segurando o arquivo…): nada desta gravação fica pela metade.
                foreach (var c in criados) ApagarSeguro(c);
                if (criouPasta) ApagarPastaSeVazia(etapa.Pasta);
                throw;
            }
            ApagarPasta(p.PastaTrabalho);
            return new Entrega(etapa.Pasta, livres.Docx, p.Pdf is null ? null : livres.Pdf, livres.Recibo);
        }
    }

    sealed class NomeOcupadoException(string caminho) : IOException("O arquivo já existe: " + caminho);

    /// <summary>Grava <paramref name="bytes"/> num <c>&lt;nome&gt;.&lt;guid8&gt;.tmp</c> oculto da mesma pasta e o move para <paramref name="final"/>
    /// sem sobrescrever. O <c>.tmp</c> nunca fica.</summary>
    static void Mover(string pasta, byte[] bytes, string final, List<string> criados, Action<string>? antesDeMover = null)
    {
        var tmp = Path.Combine(pasta, Path.GetFileName(final) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(true);
            }
            File.SetAttributes(tmp, File.GetAttributes(tmp) | FileAttributes.Hidden);
            antesDeMover?.Invoke(final);
            try { File.Move(tmp, final, overwrite: false); }
            catch (IOException) when (File.Exists(final) || Directory.Exists(final)) { throw new NomeOcupadoException(final); }
            criados.Add(final);
            File.SetAttributes(final, File.GetAttributes(final) & ~FileAttributes.Hidden);
        }
        finally
        {
            if (File.Exists(tmp)) ApagarSeguro(tmp);
        }
    }

    /// <summary>Gerar PDF depois: só para um despacho do editor (com recibo) que ficou sem PDF. Converte uma cópia do DOCX gravado.</summary>
    public static Preparacao PrepararPdf(string docxGravado, string pastaTrabalho, IConversorPdf conv, CancellationToken ct) =>
        PrepararPdf(docxGravado, pastaTrabalho, conv, null, ct);

    /// <summary>Com <paramref name="assinador"/>, o PDF sai assinado digitalmente, como em <see cref="Preparar(DespachoFinal, ModeloDespacho, string, IConversorPdf, IAssinadorPdf?, CancellationToken)"/>.</summary>
    public static Preparacao PrepararPdf(string docxGravado, string pastaTrabalho, IConversorPdf conv, IAssinadorPdf? assinador, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docxGravado);
        ArgumentNullException.ThrowIfNull(pastaTrabalho);
        ArgumentNullException.ThrowIfNull(conv);
        Preparacao Erro(string e) => new(pastaTrabalho, Array.Empty<byte>(), null, null, Array.Empty<string>(), null, null, new[] { e });
        var recibo = LerRecibo(docxGravado);
        if (recibo is null) return Erro(SemRecibo);
        var copia = Path.Combine(pastaTrabalho, "despacho.docx");
        byte[] bytes;
        try
        {
            Directory.CreateDirectory(pastaTrabalho);
            File.Copy(docxGravado, copia, overwrite: true);
            bytes = File.ReadAllBytes(copia);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Erro(Trabalho(e)); }
        if (!ConfereComRecibo(recibo, docxGravado, bytes)) return Erro(DocxNaoConfere);
        return Converter(pastaTrabalho, copia, bytes, conv, assinador, ct);
    }

    /// <summary>Grava o PDF de <see cref="PrepararPdf"/> ao lado do DOCX (<c>&lt;base&gt;.pdf</c>, sem sobrescrever) e regrava o recibo por
    /// temporário + troca, com <c>pdf</c> e <c>paginas_conferidas</c> preenchidos e <c>sem_pdf_motivo</c> zerado.</summary>
    public static Entrega GravarPdf(Preparacao p, string docxGravado, IReadOnlyList<int> paginasConferidas, string? motorPdf, ContextoUsuario u) =>
        GravarPdf(p, docxGravado, paginasConferidas, motorPdf, u, null);

    /// <summary><paramref name="antesDaTroca"/> (testes): chamado com o caminho do recibo antes da troca, para simular uma falha.</summary>
    internal static Entrega GravarPdf(Preparacao p, string docxGravado, IReadOnlyList<int> paginasConferidas, string? motorPdf, ContextoUsuario u,
        Action<string>? antesDaTroca)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(docxGravado);
        ArgumentNullException.ThrowIfNull(u);
        var recibo = LerRecibo(docxGravado) ?? throw new InvalidOperationException(SemRecibo);
        if (p.Erros.Count > 0) throw new InvalidOperationException(string.Join(" ", p.Erros));
        if (p.Pdf is null) throw new InvalidOperationException(p.MensagemSemPdf ?? "Não há PDF para gravar.");
        ExigirPaginas(p.Paginas ?? 0, paginasConferidas);
        var atual = File.ReadAllBytes(docxGravado);
        if (!ConfereComRecibo(recibo, docxGravado, atual)) throw new InvalidOperationException(DocxNaoConfere);
        if (RegistroModelos.Sha(atual) != RegistroModelos.Sha(p.Docx)) throw new InvalidOperationException(DocxMudou);

        var pasta = Path.GetDirectoryName(Path.GetFullPath(docxGravado))!;
        var caminhoPdf = Path.ChangeExtension(Path.GetFullPath(docxGravado), ".pdf");
        var caminhoRecibo = ReciboDespacho.CaminhoDe(Path.GetFullPath(docxGravado));
        var criados = new List<string>();
        try { Mover(pasta, p.Pdf, caminhoPdf, criados); }
        catch (NomeOcupadoException) { throw new IOException("Já existe um PDF com o nome do despacho: " + Path.GetFileName(caminhoPdf)); }

        var tmp = Path.Combine(pasta, Path.GetFileName(caminhoRecibo) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            // Dentro do try: uma falha ao montar o recibo (relógio, fontes) também tira o PDF movido agora.
            recibo["pdf"] = ReciboDespacho.Pdf(caminhoPdf, p.Pdf, p.Paginas!.Value, p.Fontes, motorPdf, p.Assinatura);
            recibo["sem_pdf_motivo"] = null;
            recibo["paginas_conferidas"] = ReciboDespacho.Conferidas(paginasConferidas, u);
            File.WriteAllBytes(tmp, ReciboDespacho.Escrever(recibo));
            File.SetAttributes(tmp, File.GetAttributes(tmp) | FileAttributes.Hidden);
            antesDaTroca?.Invoke(caminhoRecibo);
            File.Move(tmp, caminhoRecibo, overwrite: true);
        }
        catch
        {
            // O recibo antigo ficou como estava: o PDF movido agora sai, para não haver PDF que o recibo não conhece.
            foreach (var c in criados) ApagarSeguro(c);
            throw;
        }
        finally
        {
            if (File.Exists(tmp)) ApagarSeguro(tmp);
        }
        try { File.SetAttributes(caminhoRecibo, File.GetAttributes(caminhoRecibo) & ~FileAttributes.Hidden); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* o recibo já foi trocado; o atributo é só cosmético */ }
        ApagarPasta(p.PastaTrabalho);
        return new Entrega(pasta, Path.GetFullPath(docxGravado), caminhoPdf, caminhoRecibo);
    }

    /// <summary>O DOCX é o que o recibo registra: mesmo nome de arquivo e mesmo sha256.</summary>
    static bool ConfereComRecibo(JsonObject recibo, string docx, byte[] bytes) =>
        recibo["docx"] is JsonObject d
        && d["arquivo"] is JsonValue a && a.TryGetValue<string>(out var arquivo) && arquivo == Path.GetFileName(docx)
        && d["sha256"] is JsonValue s && s.TryGetValue<string>(out var sha) && string.Equals(sha, RegistroModelos.Sha(bytes), StringComparison.OrdinalIgnoreCase);

    /// <summary>O recibo do editor ao lado do DOCX, ou null se não houver (ou não for um recibo do editor).</summary>
    static JsonObject? LerRecibo(string docx)
    {
        var caminho = ReciboDespacho.CaminhoDe(docx);
        if (!File.Exists(caminho)) return null;
        try
        {
            var r = ReciboDespacho.Ler(caminho);
            return r["recibo_versao"] is JsonValue v && v.TryGetValue<int>(out var versao) && versao == ReciboDespacho.Versao
                   && r["docx"] is JsonObject ? r : null;
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or InvalidDataException) { return null; }
    }

    /// <summary>Apaga as subpastas de <paramref name="raizTrabalho"/> com mais de 1 dia (pastas de trabalho que sobraram).</summary>
    public static void LimparOrfas(string raizTrabalho)
    {
        ArgumentNullException.ThrowIfNull(raizTrabalho);
        if (!Directory.Exists(raizTrabalho)) return;
        var limite = DateTime.UtcNow.AddDays(-1);
        foreach (var d in Directory.EnumerateDirectories(raizTrabalho).ToList())
            if (Directory.GetLastWriteTimeUtc(d) < limite && !Processos.EhLink(d)) ApagarPasta(d);
    }

    static void ApagarPasta(string pasta)
    {
        try { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* temporário: sai na próxima limpeza de órfãs */ }
    }

    static void ApagarPastaSeVazia(string pasta)
    {
        try { if (Directory.Exists(pasta) && !Directory.EnumerateFileSystemEntries(pasta).Any()) Directory.Delete(pasta); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static void ApagarSeguro(string arquivo)
    {
        try
        {
            File.SetAttributes(arquivo, FileAttributes.Normal);
            File.Delete(arquivo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
