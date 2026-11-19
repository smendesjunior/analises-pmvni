using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Pmvni.Acompanhamento.Testes;

/// <summary>A amostra é a planilha padrão com nomes inventados (mesmas colunas, listas, cores, fórmulas e tabela).</summary>
public sealed class PlanilhaTestes : IDisposable
{
    readonly string pasta = Path.Combine(Path.GetTempPath(), "pmvni-acomp-" + Guid.NewGuid().ToString("N"));
    readonly string arquivo;

    public PlanilhaTestes()
    {
        Directory.CreateDirectory(pasta);
        arquivo = Path.Combine(pasta, "acompanhamento.xlsx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Amostras", "acompanhamento-ficticio.xlsx"), arquivo);
    }

    public void Dispose() { try { Directory.Delete(pasta, true); } catch (IOException) { } }

    [Theory]
    [InlineData("$AB$9")]
    [InlineData("$AB9")]
    [InlineData("AB$9")]
    public void Le_referencias_absolutas_e_mistas(string endereco)
    {
        Assert.Equal((27, 9), Planilha.Referencia(endereco));
    }

    [Fact]
    public void Copiar_formula_preserva_nomes_de_abas_e_colunas_de_tabela()
    {
        var formula = "'2026 A1'!B9+ABC1!C9+Tabela1[A1]";
        Assert.Equal("'2026 A1'!B10+ABC1!C10+Tabela1[A1]", GravadorPlanilha.Deslocar(formula, 9, 10));
    }

    [Fact]
    public void Le_colunas_listas_e_cores_da_planilha()
    {
        var p = LeitorPlanilha.Ler(arquivo);
        Assert.Equal("2026", p.Aba);
        Assert.Equal(8, p.LinhaCabecalho);
        Assert.Equal(14, p.Colunas.Count); // A..N, inclusive "Coluna1"
        Assert.Equal(19, p.Linhas.Count);
        Assert.Contains("ACOMPANHAMENTO DE PROCESSOS", p.Titulo);
        Assert.StartsWith("Solicitações de alvará", p.Subtitulo);

        var status = p.Coluna(ChaveColuna.Status)!;
        Assert.Equal(TipoColuna.Lista, status.Tipo);
        Assert.Equal(["EM ANÁLISE", "AGUARDANDO ANÁLISE", "AGUARDANDO DOCUMENTOS", "ENVIADO AO REQUERENTE", "DESPACHADO", "FINALIZADO", "ARQUIVADO"], status.Opcoes);
        Assert.Equal(new Cor("#DDEBF7", "#1F4E78"), status.CorDe("EM ANÁLISE", null));
        Assert.Equal(["BAIXA", "MÉDIA", "ALTA"], p.Coluna(ChaveColuna.Prioridade)!.Opcoes);
        Assert.Equal(["APROVADO", "NÃO APROVADO"], p.Coluna(ChaveColuna.Aprovacao)!.Opcoes);
        Assert.Equal(TipoColuna.Data, p.Coluna(ChaveColuna.Atualizacao)!.Tipo);
        Assert.Equal(TipoColuna.Data, p.Coluna(ChaveColuna.DataInicial)!.Tipo);
        Assert.Equal(TipoColuna.Calculada, p.Coluna(ChaveColuna.DiasSemAtualizacao)!.Tipo);
        var dias = p.Coluna(ChaveColuna.DiasSemAtualizacao)!;
        Assert.Equal("#9C0006", dias.CorDe("", 30)!.Texto);
        Assert.Equal("#375623", dias.CorDe("", 5)!.Texto);
    }

    [Fact]
    public void Calcula_dias_e_conta_os_status()
    {
        var p = LeitorPlanilha.Ler(arquivo);
        var l = p.Linhas.First(x => x.Celula(p.Coluna(ChaveColuna.DiasSemAtualizacao)!.Indice).Formula is not null && x.Atualizacao is not null);
        Assert.Equal((int)(new DateTime(2026, 12, 1) - l.Atualizacao!.Value.Date).TotalDays, l.Dias(new DateTime(2026, 12, 1)));
        var cont = p.Contagem(ChaveColuna.Status);
        Assert.Equal(7, cont.Count);
        Assert.Equal(19, cont.Sum(c => c.Quantidade));
    }

    [Fact]
    public void Grava_edicao_sem_mexer_no_resto_do_pacote()
    {
        var antes = LeitorPlanilha.Ler(arquivo);
        var alvo = antes.Linhas[3];
        var status = antes.Coluna(ChaveColuna.Status)!.Indice;
        var atualizacao = antes.Coluna(ChaveColuna.Atualizacao)!.Indice;
        var partesAntes = Partes(arquivo);

        GravadorPlanilha.Gravar(arquivo, [new Alteracao(alvo.Linha, alvo.Processo, new Dictionary<int, ValorNovo>
        {
            [status] = new(Texto: "FINALIZADO"),
            [atualizacao] = new(Data: new DateTime(2026, 11, 17)),
        })], Path.Combine(pasta, "copias"));

        var depois = LeitorPlanilha.Ler(arquivo);
        var l = depois.Linhas.Single(x => x.Linha == alvo.Linha);
        Assert.Equal("FINALIZADO", l.Status);
        Assert.Equal(new DateTime(2026, 11, 17), l.Atualizacao);
        Assert.Equal(alvo.Texto(ChaveColuna.Requerente), l.Texto(ChaveColuna.Requerente));
        Assert.Single(Directory.GetFiles(GravadorPlanilha.PastaDasCopias(arquivo, Path.Combine(pasta, "copias"))));
        // Estilos, tabela e a validação de dados ficam iguais.
        var partesDepois = Partes(arquivo);
        Assert.Equal(partesAntes["xl/styles.xml"], partesDepois["xl/styles.xml"]);
        Assert.Equal(partesAntes["xl/tables/table1.xml"], partesDepois["xl/tables/table1.xml"]);
        Assert.Equal(depois.Colunas.Select(c => c.Regras.Count), antes.Colunas.Select(c => c.Regras.Count));
    }

    [Fact]
    public void Processo_novo_vai_para_a_primeira_linha_livre_com_formula_e_tabela_estendida()
    {
        var antes = LeitorPlanilha.Ler(arquivo);
        var col = antes.Colunas.ToDictionary(c => c.Chave == ChaveColuna.Outra ? (ChaveColuna)(-c.Indice - 1) : c.Chave, c => c.Indice);
        var linhas = GravadorPlanilha.Gravar(arquivo, [new Alteracao(null, null, new Dictionary<int, ValorNovo>
        {
            [col[ChaveColuna.Processo]] = new(Texto: "99999/2026"),
            [col[ChaveColuna.Requerente]] = new(Texto: "NOVO REQUERENTE"),
            [col[ChaveColuna.Atualizacao]] = new(Data: new DateTime(2026, 11, 10)),
            [col[ChaveColuna.Status]] = new(Texto: "AGUARDANDO ANÁLISE"),
        })]);
        Assert.Equal(28, linhas[0]);
        var depois = LeitorPlanilha.Ler(arquivo);
        var nova = depois.Linhas.Single(l => l.Linha == 28);
        Assert.Equal("NOVO REQUERENTE", nova.Texto(ChaveColuna.Requerente));
        Assert.Equal("IF(H28=\"\",\"\",TODAY()-INT(H28))", nova.Celula(col[ChaveColuna.DiasSemAtualizacao]).Formula);
        Assert.Equal(7, nova.Dias(new DateTime(2026, 11, 17)));
        using var zip = ZipFile.OpenRead(arquivo);
        var tabela = XDocument.Load(zip.GetEntry("xl/tables/table1.xml")!.Open());
        Assert.Equal("A8:N28", (string?)tabela.Root!.Attribute("ref"));
        Assert.Null(zip.GetEntry("xl/calcChain.xml"));
    }

    [Fact]
    public void Excluir_limpa_a_linha_e_conflito_acha_pelo_numero()
    {
        var antes = LeitorPlanilha.Ler(arquivo);
        var a = antes.Linhas[0];
        var b = antes.Linhas[1];
        var obs = antes.Coluna(ChaveColuna.Observacao)!.Indice;
        // A linha informada é a de "a", mas o processo esperado é "b": grava em "b".
        GravadorPlanilha.Gravar(arquivo, [new Alteracao(a.Linha, b.Processo, new Dictionary<int, ValorNovo> { [obs] = new(Texto: "MUDOU") })]);
        Assert.Equal("MUDOU", LeitorPlanilha.Ler(arquivo).Linhas.Single(x => x.Linha == b.Linha).Texto(ChaveColuna.Observacao));
        Assert.Throws<ConflitoPlanilhaException>(() => GravadorPlanilha.Gravar(arquivo, [new Alteracao(a.Linha, "1/1900", new Dictionary<int, ValorNovo>())]));

        GravadorPlanilha.Gravar(arquivo, [new Alteracao(a.Linha, a.Processo, new Dictionary<int, ValorNovo>(), Excluir: true)]);
        Assert.DoesNotContain(LeitorPlanilha.Ler(arquivo).Linhas, x => x.Linha == a.Linha);
    }

    [Fact]
    public void Planilha_aberta_no_excel_nao_grava()
    {
        using var trava = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<PlanilhaEmUsoException>(() => GravadorPlanilha.Gravar(arquivo, [new Alteracao(null, null, new Dictionary<int, ValorNovo>())]));
    }

    [Fact]
    public void Formula_deslocada_para_outra_linha()
    {
        Assert.Equal("IF(H30=\"\",\"\",TODAY()-INT(H30))", GravadorPlanilha.Deslocar("IF(H9=\"\",\"\",TODAY()-INT(H9))", 9, 30));
        Assert.Equal("$AB$6+A31", GravadorPlanilha.Deslocar("$AB$6+A10", 9, 30));
    }

    [Fact]
    public void Capa_do_relatorio_da_local_e_autor()
    {
        var json = """
            {"capa":{"quadros":[
              {"titulo":"LOCALIZAÇÃO","destaque":"Av. Prefeito Nicolau Falchetto, nº 1890, Bananeiras"},
              {"titulo":"CARACTERIZAÇÃO","destaque":"Construção multifamiliar"},
              {"titulo":"AUTORIA INDICADA","destaque":"Daiane Exemplo"}]}}
            """;
        var d = DadosDoProcesso.DaCapa(Encoding.UTF8.GetBytes(json), "relatório");
        Assert.Equal("Av. Prefeito Nicolau Falchetto, nº 1890, Bananeiras", d.Local);
        Assert.Equal("Daiane Exemplo", d.Autor);
        var semAutor = DadosDoProcesso.DaCapa(Encoding.UTF8.GetBytes("""{"capa":{"quadros":[{"titulo":"AUTORIA","destaque":"Não informado"}]}}"""), "x");
        Assert.True(semAutor.Vazia);
        Assert.Equal("01060200304001", DadosDoProcesso.InscricaoDaConsulta(Encoding.UTF8.GetBytes("""{"lote_candidato":"01060200304001"}""")));
    }

    [Fact]
    public void Formula_nao_desloca_nomes_de_funcao_e_anda_colunas()
    {
        Assert.Equal("LOG10(H30)+DEC2BIN(A30)", GravadorPlanilha.Deslocar("LOG10(H9)+DEC2BIN(A9)", 9, 30));
        Assert.Equal("I30+$B$2+C$9", GravadorPlanilha.Deslocar("H30+$B$2+B$9", 30, 30, colunas: 1));
        Assert.Equal("Listas!A12", GravadorPlanilha.Deslocar("Listas!A3", 1, 10));
    }

    [Fact]
    public void Preenchimento_automatico_nao_troca_o_que_ja_esta_na_celula()
    {
        var antes = LeitorPlanilha.Ler(arquivo);
        var obs = antes.Coluna(ChaveColuna.Observacao)!.Indice;
        var local = antes.Coluna(ChaveColuna.Local)!.Indice;
        var cheia = antes.Linhas.First(l => l.Texto(ChaveColuna.Local).Length > 0);
        var vazia = antes.Linhas.First(l => l.Texto(ChaveColuna.Local).Length == 0);
        GravadorPlanilha.Gravar(arquivo, [
            new Alteracao(cheia.Linha, cheia.Processo, new Dictionary<int, ValorNovo> { [local] = new(Texto: "DETECTADO") }, SomenteSeVazias: true),
            new Alteracao(vazia.Linha, vazia.Processo, new Dictionary<int, ValorNovo> { [local] = new(Texto: "DETECTADO"), [obs] = new(Texto: "X") }, SomenteSeVazias: true),
        ]);
        var depois = LeitorPlanilha.Ler(arquivo);
        Assert.Equal(cheia.Texto(ChaveColuna.Local), depois.Linhas.Single(l => l.Linha == cheia.Linha).Texto(ChaveColuna.Local));
        Assert.Equal("DETECTADO", depois.Linhas.Single(l => l.Linha == vazia.Linha).Texto(ChaveColuna.Local));
    }

    [Fact]
    public void Excluir_mantem_as_formulas_da_linha_e_o_excel_nao_perde_a_mestra()
    {
        var antes = LeitorPlanilha.Ler(arquivo);
        var dias = antes.Coluna(ChaveColuna.DiasSemAtualizacao)!.Indice;
        var a = antes.Linhas[0];
        var formulaAntes = a.Celula(dias).Formula;
        GravadorPlanilha.Gravar(arquivo, [new Alteracao(a.Linha, a.Processo, new Dictionary<int, ValorNovo>(), Excluir: true)]);
        using var zip = ZipFile.OpenRead(arquivo);
        var aba = XDocument.Load(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cel = aba.Descendants(s + "c").Single(c => (string?)c.Attribute("r") == Planilha.Letra(dias) + a.Linha);
        Assert.NotNull(cel.Element(s + "f"));
        // Toda fórmula compartilhada vazia ainda tem a mestra (mesmo si, com texto).
        var mestras = aba.Descendants(s + "f").Where(f => (string?)f.Attribute("t") == "shared" && f.Value.Length > 0).Select(f => (string?)f.Attribute("si")).ToHashSet();
        Assert.All(aba.Descendants(s + "f").Where(f => (string?)f.Attribute("t") == "shared" && f.Value.Length == 0), f => Assert.Contains((string?)f.Attribute("si"), mestras));
        Assert.NotNull(formulaAntes);
    }

    [Fact]
    public void Gravar_na_mestra_compartilhada_da_formula_propria_as_outras_linhas()
    {
        // Monta uma coluna com fórmula compartilhada (mestra na 1ª linha) e escreve um valor por cima da mestra.
        var lida = LeitorPlanilha.Ler(arquivo);
        var obs = lida.Coluna(ChaveColuna.Observacao)!.Indice;
        var l1 = lida.Linhas[0].Linha;
        var l2 = lida.Linhas[1].Linha;
        using (var zip = ZipFile.Open(arquivo, ZipArchiveMode.Update))
        {
            var e = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument aba;
            using (var r = e.Open()) aba = XDocument.Load(r);
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XElement Cel(int linha) => aba.Descendants(s + "c").Single(c => (string?)c.Attribute("r") == Planilha.Letra(obs) + linha);
            foreach (var c in new[] { Cel(l1), Cel(l2) }) { c.Elements().Remove(); c.Attribute("t")?.Remove(); }
            Cel(l1).Add(new XElement(s + "f", new XAttribute("t", "shared"), new XAttribute("ref", $"{Planilha.Letra(obs)}{l1}:{Planilha.Letra(obs)}{l2}"), new XAttribute("si", "77"), $"B{l1}&\"\""));
            Cel(l2).Add(new XElement(s + "f", new XAttribute("t", "shared"), new XAttribute("si", "77")));
            e.Delete();
            using var w = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            aba.Save(w);
        }
        var linha1 = LeitorPlanilha.Ler(arquivo).Linhas.First(x => x.Linha == l1);
        GravadorPlanilha.Gravar(arquivo, [new Alteracao(l1, linha1.Processo, new Dictionary<int, ValorNovo> { [obs] = new(Texto: "TEXTO") })]);
        using var z2 = ZipFile.OpenRead(arquivo);
        var depois = XDocument.Load(z2.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var f2 = depois.Descendants(ns + "c").Single(c => (string?)c.Attribute("r") == Planilha.Letra(obs) + l2).Element(ns + "f")!;
        Assert.Equal($"B{l2}&\"\"", f2.Value);
        Assert.Null(f2.Attribute("si"));
    }

    [Fact]
    public void Aba_escolhida_pelo_nome_e_ausente_e_recusada_na_gravacao()
    {
        var p = LeitorPlanilha.Ler(arquivo, "2026");
        Assert.Equal("2026", p.Aba);
        Assert.False(p.AbaPedidaAusente);
        Assert.Contains("2026", p.Abas);
        var ausente = LeitorPlanilha.Ler(arquivo, "1999");
        Assert.True(ausente.AbaPedidaAusente);
        Assert.Throws<ConflitoPlanilhaException>(() => GravadorPlanilha.Gravar(arquivo, [new Alteracao(null, null, new Dictionary<int, ValorNovo>())], aba: "1999"));
    }

    [Fact]
    public void Gravacao_nao_deixa_temporario_e_guarda_copias_por_planilha_sem_sobrescrever()
    {
        var copias = Path.Combine(pasta, "copias");
        var p = LeitorPlanilha.Ler(arquivo);
        var obs = p.Coluna(ChaveColuna.Observacao)!.Indice;
        for (var i = 0; i < 3; i++)
            GravadorPlanilha.Gravar(arquivo, [new Alteracao(p.Linhas[0].Linha, p.Linhas[0].Processo, new Dictionary<int, ValorNovo> { [obs] = new(Texto: "V" + i) })], copias);
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp", new EnumerationOptions { AttributesToSkip = 0 }));
        var sub = GravadorPlanilha.PastaDasCopias(arquivo, copias);
        Assert.Equal(3, Directory.GetFiles(sub, "*.xlsx").Length);
        Assert.Equal("V2", LeitorPlanilha.Ler(arquivo).Linhas[0].Texto(ChaveColuna.Observacao));
    }

    [Fact]
    public void Planilha_em_uso_nao_muda_nada_e_nao_deixa_temporario()
    {
        var bytes = File.ReadAllBytes(arquivo);
        using (new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<PlanilhaEmUsoException>(() => GravadorPlanilha.Gravar(arquivo, [new Alteracao(null, null, new Dictionary<int, ValorNovo>())]));
        Assert.Equal(bytes, File.ReadAllBytes(arquivo));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp", new EnumerationOptions { AttributesToSkip = 0 }));
    }

    static Dictionary<string, string> Partes(string arquivo)
    {
        using var zip = ZipFile.OpenRead(arquivo);
        return zip.Entries.ToDictionary(e => e.FullName, e => { using var r = new StreamReader(e.Open()); return r.ReadToEnd(); });
    }
}

/// <summary>Conferência opcional numa CÓPIA da planilha real (feita pelo Excel): PMVNI_PLANILHA_REAL aponta a cópia. Sem a variável, não roda nada.</summary>
public sealed class PlanilhaRealTestes
{
    [Fact]
    public void Copia_da_planilha_real_le_e_grava()
    {
        var copia = Environment.GetEnvironmentVariable("PMVNI_PLANILHA_REAL");
        if (string.IsNullOrEmpty(copia) || !File.Exists(copia)) return;
        var p = LeitorPlanilha.Ler(copia);
        Assert.True(p.Linhas.Count > 0);
        var l = p.Linhas[0];
        GravadorPlanilha.Gravar(copia, [
            new Alteracao(l.Linha, l.Processo, new Dictionary<int, ValorNovo> { [p.Coluna(ChaveColuna.Observacao)!.Indice] = new(Texto: "TESTE DO APP") }),
            new Alteracao(null, null, new Dictionary<int, ValorNovo> { [p.Coluna(ChaveColuna.Processo)!.Indice] = new(Texto: "1/2026"), [p.Coluna(ChaveColuna.Atualizacao)!.Indice] = new(Data: DateTime.Today) }),
        ]);
        var d = LeitorPlanilha.Ler(copia);
        Assert.Equal(p.Linhas.Count + 1, d.Linhas.Count);
        Assert.Equal("TESTE DO APP", d.Linhas.Single(x => x.Linha == l.Linha).Texto(ChaveColuna.Observacao));
    }
}
