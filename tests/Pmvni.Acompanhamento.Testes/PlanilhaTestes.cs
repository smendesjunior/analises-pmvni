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
        Assert.Single(Directory.GetFiles(Path.Combine(pasta, "copias")));
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
