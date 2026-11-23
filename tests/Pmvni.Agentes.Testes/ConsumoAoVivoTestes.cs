using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

public class ConsumoAoVivoTestes
{
    const string TokensEm = "2026-11-23T15:11:00Z";
    const string CotaEm = "2026-11-23T15:07:00Z";
    const string SemanaEm = "2026-11-23T15:05:00Z";

    static JsonObject Amostra()
    {
        var contagem = new JsonObject { ["entrada"] = 120, ["saida"] = 30, ["entrada_cache"] = 100, ["total"] = 150 };
        return new JsonObject
        {
            ["schema"] = 1, ["execucao_id"] = "registro", ["rota"] = "codex", ["processo"] = "Processo fictício",
            ["atualizado_em"] = "2026-11-23T15:14:00Z",
            ["tokens"] = new JsonObject { ["parcial"] = true, ["observados_em"] = TokensEm,
                ["entrada"] = 120, ["saida"] = 30, ["entrada_cache"] = 100, ["total"] = 150,
                ["etapas"] = new JsonObject { ["analyze"] = contagem } },
            ["cota_aplicavel"] = true,
            ["cota"] = new JsonObject { ["janela_min"] = 300, ["usada_pct"] = 51, ["observada_em"] = CotaEm, ["fonte"] = "CLI",
                ["semana"] = new JsonObject { ["janela_min"] = 10080, ["usada_pct"] = 9, ["observada_em"] = SemanaEm, ["fonte"] = "CLI" } }
        };
    }

    static string Gravar(PastaTeste pasta, JsonObject amostra)
    {
        var controle = Path.Combine(pasta, "Processo fictício", ".pmvni-ai", "analise");
        Directory.CreateDirectory(controle);
        File.WriteAllText(Path.Combine(controle, "TELEMETRIA-IA.JSON"), amostra.ToJsonString());
        return controle;
    }

    static (IReadOnlyList<UsoEtapa> Linhas, ConsumoAoVivo? Leitura) Ler(string controle, Provedor p = Provedor.ChatGpt) =>
        ConsumoAoVivo.Ler(controle, default, p, "registro", OrdemDasEtapas.Padrao, null, null);

    [Fact]
    public void LeMetricaDoKitMesmoEmOutraEstacaoEUsaHorariosIndependentes()
    {
        using var pasta = Ambiente.PastaDeTeste("telemetria-estacao");
        var controle = Gravar(pasta, Amostra());
        var (uso, leitura) = Ler(controle);
        var medida = Assert.Single(uso);
        Assert.Equal(150L, medida.Total);
        Assert.Equal(120L, medida.Entrada);
        Assert.Equal(100L, medida.CacheLido);
        Assert.True(medida.EntradaIncluiCache);
        Assert.True(leitura!.TokensParciais);
        Assert.Equal(DateTimeOffset.Parse(TokensEm), leitura.TokensObservadosEm);
        Assert.Equal(DateTimeOffset.Parse(CotaEm), leitura.CotaObservadaEm);
        Assert.Equal(DateTimeOffset.Parse(SemanaEm), leitura.CotaSemanaObservadaEm);
        Assert.Equal(51d, leitura.Usada5h);
        Assert.Equal(9d, leitura.UsadaSemana);
        Assert.Equal("CLI", leitura.FonteCota);
    }

    [Theory]
    [InlineData("execucao_id", "outro-registro")]
    [InlineData("execucao_id", null)]
    [InlineData("rota", "claude")]
    [InlineData("processo", "Outro processo")]
    [InlineData("atualizado_em", "inválido")]
    public void IgnoraAmostraQueNaoPertenceAoRegistro(string campo, string? valor)
    {
        using var pasta = Ambiente.PastaDeTeste("telemetria-identidade");
        var amostra = Amostra();
        amostra[campo] = valor;
        var (uso, leitura) = Ler(Gravar(pasta, amostra));
        Assert.Null(leitura);
        Assert.Empty(uso);
    }

    [Fact]
    public void AtualizaComAMedicaoSemSomarDuasVezesMesmoEmRetentativa()
    {
        using var pasta = Ambiente.PastaDeTeste("telemetria-deduplicada");
        var controle = Gravar(pasta, Amostra());
        var caminho = Path.Combine(controle, UsoDasIas.Arquivo);
        File.WriteAllText(caminho, """{"execucoes":[{"etapa":"analyze","ia":"codex","tokens":30},{"etapa":"analyze","ia":"codex","tokens":90}]}""");
        var (uso, leitura) = Ler(controle);
        Assert.Equal(new long?[] { 30, 90, 30 }, uso.Select(x => x.Total));
        Assert.Equal(150, uso.Sum(x => x.Total));
        Assert.Null(uso.Last().Entrada);
        Assert.Equal(120, leitura!.Entrada);
        File.WriteAllText(caminho, """{"execucoes":[{"etapa":"analyze","ia":"codex","tokens":151}]}""");
        var posterior = Assert.Single(Ler(controle).Linhas);
        Assert.Equal(151L, posterior.Total);
    }

    [Fact]
    public void MarcaApiENaoAtribuiCotaDaContaAnterior()
    {
        using var pasta = Ambiente.PastaDeTeste("telemetria-api");
        var amostra = Amostra();
        amostra["rota"] = "claude";
        amostra["cota_aplicavel"] = false;
        var (uso, leitura) = Ler(Gravar(pasta, amostra), Provedor.Claude);
        var etapa = Assert.Single(uso);
        Assert.True(etapa.EntradaIncluiCache);
        Assert.False(leitura!.CotaAplicavel);
        Assert.Null(leitura.Usada5h);
        Assert.Null(leitura.UsadaSemana);
        Assert.Null(leitura.CotaObservadaEm);
    }
}
