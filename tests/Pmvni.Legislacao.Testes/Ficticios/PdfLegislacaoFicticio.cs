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

    /// <summary>Lei fictícia de <paramref name="n"/> páginas, cada uma com um artigo e uma imagem (200×100 pt) e um degradê de página inteira:
    /// a leitura das figuras leva alguns segundos (testes de cancelamento e de exclusão durante a leitura).</summary>
    public static byte[] GerarComFigurasGrande(int n)
    {
        var pdf = new Corpo();
        var catalogo = pdf.Reservar();
        var arvore = pdf.Reservar();
        var fonte = pdf.Reservar();
        var imagem = pdf.Reservar();
        var sombra = pdf.Reservar();
        pdf.Definir(fonte, Latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        pdf.Definir(imagem, ComFluxo(" /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0]));
        pdf.Definir(sombra, Latin("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 595 0] /Function << /FunctionType 2 /Domain [0 1] "
            + "/C0 [1 1 1] /C1 [0.1 0.35 0.6] /N 1 >> /Extend [true true] >>"));
        var paginas = new List<int>();
        for (var i = 1; i <= n; i++)
        {
            var b = new List<byte>();
            b.AddRange(Latin("BT /F1 12 Tf 1 0 0 1 72 770 Tm ("));
            b.AddRange(Escapar($"Art. {i}º Artigo fictício com figura."));
            b.AddRange(Latin(") Tj ET\nq 200 0 0 100 72 630 cm /Im1 Do Q\nq 0 0 595 842 re W n /Sh1 sh Q\n"));
            var conteudo = pdf.Reservar();
            var pagina = pdf.Reservar();
            pdf.Definir(conteudo, ComFluxo("", [.. b]));
            pdf.Definir(pagina, Latin($"<< /Type /Page /Parent {arvore} 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {fonte} 0 R >> "
                + $"/XObject << /Im1 {imagem} 0 R >> /Shading << /Sh1 {sombra} 0 R >> >> /Contents {conteudo} 0 R >>"));
            paginas.Add(pagina);
        }
        pdf.Definir(arvore, Latin($"<< /Type /Pages /Kids [{string.Join(" ", paginas.Select(o => o + " 0 R"))}] /Count {n} >>"));
        pdf.Definir(catalogo, Latin($"<< /Type /Catalog /Pages {arvore} 0 R >>"));
        return pdf.Serializar($"/Root {catalogo} 0 R /ID [<00112233445566778899AABBCCDDEEF0><00112233445566778899AABBCCDDEEF0>]");
    }

    /// <summary>Lei fictícia com figuras (correções 2026-11-15), para a leitura padronizada. Página 1: duas linhas de texto, uma imagem
    /// (objeto de imagem 2×2 RGB, desenhada em 200×100 pt de y 630 a 730) e outra linha em y 600. Página 2: uma linha em y 770, um desenho
    /// vetorial (triângulo traçado e retângulo azul, de y 500 a 700), uma imagem embutida (BI…EI, 50×50 pt de y 600 a 650, à direita) e a
    /// última linha em y 470. Página 3: um degradê (sh) sem caixa conhecida ⇒ a página inteira.</summary>
    public static byte[] GerarComFiguras()
    {
        var pdf = new Corpo();
        var catalogo = pdf.Reservar();
        var arvore = pdf.Reservar();
        var fonte = pdf.Reservar();
        var imagem = pdf.Reservar();
        var sombra = pdf.Reservar();
        var paginas = new[] { pdf.Reservar(), pdf.Reservar(), pdf.Reservar() };
        var conteudos = new[] { pdf.Reservar(), pdf.Reservar(), pdf.Reservar() };
        pdf.Definir(fonte, Latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        pdf.Definir(imagem, ComFluxo(" /Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8",
            [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0]));
        pdf.Definir(sombra, Latin("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 595 0] /Function << /FunctionType 2 /Domain [0 1] "
            + "/C0 [1 1 1] /C1 [0.1 0.35 0.6] /N 1 >> /Extend [true true] >>"));

        static void Linha(List<byte> b, double y, string texto)
        {
            b.AddRange(Latin($"BT /F1 12 Tf 1 0 0 1 72 {y.ToString(System.Globalization.CultureInfo.InvariantCulture)} Tm ("));
            b.AddRange(Escapar(texto));
            b.AddRange(Latin(") Tj ET\n"));
        }

        var p1 = new List<byte>();
        Linha(p1, 770, TextosComFiguras[0][0]);
        Linha(p1, 752, TextosComFiguras[0][1]);
        p1.AddRange(Latin("q 200 0 0 100 72 630 cm /Im1 Do Q\n"));
        Linha(p1, 600, TextosComFiguras[0][2]);

        var p2 = new List<byte>();
        Linha(p2, 770, TextosComFiguras[1][0]);
        p2.AddRange(Latin("q 0.8 0 0 RG 2 w 72 500 m 300 700 l 300 500 l h S 0 0 1 rg 100 520 80 60 re f Q\n"));
        p2.AddRange(Latin("q 50 0 0 50 400 600 cm BI /W 2 /H 2 /CS /RGB /BPC 8 ID "));
        p2.AddRange([0, 128, 0, 0, 128, 0, 0, 128, 0, 0, 128, 0]);
        p2.AddRange(Latin("\nEI Q\n"));
        Linha(p2, 470, TextosComFiguras[1][1]);

        var p3 = new List<byte>();
        Linha(p3, 770, TextosComFiguras[2][0]);
        p3.AddRange(Latin("q 72 300 450 200 re W n /Sh1 sh Q\n"));

        var corpos = new[] { p1, p2, p3 };
        for (var i = 0; i < 3; i++)
        {
            pdf.Definir(conteudos[i], ComFluxo("", [.. corpos[i]]));
            pdf.Definir(paginas[i], Latin($"<< /Type /Page /Parent {arvore} 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 {fonte} 0 R >> "
                + $"/XObject << /Im1 {imagem} 0 R >> /Shading << /Sh1 {sombra} 0 R >> >> /Contents {conteudos[i]} 0 R >>"));
        }
        pdf.Definir(arvore, Latin($"<< /Type /Pages /Kids [{string.Join(" ", paginas.Select(o => o + " 0 R"))}] /Count 3 >>"));
        pdf.Definir(catalogo, Latin($"<< /Type /Catalog /Pages {arvore} 0 R >>"));
        return pdf.Serializar($"/Root {catalogo} 0 R /ID [<00112233445566778899AABBCCDDEEFF><00112233445566778899AABBCCDDEEFF>]");
    }

    /// <summary>O texto de cada página de <see cref="GerarComFiguras"/>.</summary>
    public static string[][] TextosComFiguras { get; } =
    [
        ["LEI Nº 9.998, DE 3 DE JANEIRO DE 2099 (FICTÍCIA)", "Art. 1º O quadro abaixo mostra o afastamento fictício.", "Art. 2º O texto continua depois da figura."],
        ["Art. 3º Segue o desenho fictício.", "Art. 4º Fim do desenho fictício."],
        ["Art. 5º Página com degradê fictício."],
    ];

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
