using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class RegistroModelosTestes
{
    [Fact]
    public void registro_do_kit_oferece_correcao_e_aprovacao()
    {
        var r = RegistroModelos.Ler(Ambiente.ExigirKit());

        Assert.Empty(r.Indisponiveis);
        Assert.Equal(new[] { (TipoDespacho.Correcao, "correction"), (TipoDespacho.Aprovacao, "approval_simple") },
            r.Modelos.Select(m => (m.Tipo, m.Papel)));
        Assert.All(r.Modelos, m => Assert.Equal(10, m.RevisaoRegistro));
        // Falha de propósito se o kit mudar: rever o desenho §10.1 antes de ajustar o teste.
        Assert.Equal("856dcf9ee49da68f5a7ea440bda43dc5a6f5ab06453c501b94b9a222f4039d61", r.Modelos[0].Sha256);
        Assert.Equal("f5598577e2954726e3a2ce99d06fc728e6447dc461101e6c2df2280d82e152b7", r.Modelos[1].Sha256);
        Assert.Equal("02 - MODELOS DE DOCUMENTOS/DESPACHO/MODELO DESPACHO CORREÇÃO.docx", r.Modelos[0].CaminhoRelativo);
        Assert.True(File.Exists(r.Modelos[0].Caminho));
        Assert.Equal(64, r.Modelos[0].Sha256Registro.Length);
        Assert.Equal("Correção", RegistroModelos.Rotulo(TipoDespacho.Correcao));
        Assert.Equal("Aprovação", RegistroModelos.Rotulo(TipoDespacho.Aprovacao));
        Assert.Equal("Correcao", RegistroModelos.NomeNoArquivo(TipoDespacho.Correcao));
        Assert.Equal("Aprovacao", RegistroModelos.NomeNoArquivo(TipoDespacho.Aprovacao));
    }

    [Fact]
    public void so_papeis_de_despacho_conhecidos_viram_tipo()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-papeis");
        DocxFicticio.KitFicticio(pasta, reg =>
        {
            var t = (JsonArray)reg["templates"]!;
            var aprovacao = t.OfType<JsonObject>().Single(e => (string?)e["role"] == "approval_simple");
            aprovacao["role"] = "approval_complete";
            t.Add(new JsonObject { ["role"] = "declaration", ["path"] = "02 - MODELOS DE DOCUMENTOS/DESPACHO/APROVACAO FICTICIA.docx", ["sha256"] = aprovacao["sha256"]!.GetValue<string>() });
        });

        var r = RegistroModelos.Ler(pasta);

        Assert.Equal(new[] { TipoDespacho.Correcao }, r.Modelos.Select(m => m.Tipo));
        Assert.Equal("O registro de modelos do kit não tem o modelo approval_simple.", r.Indisponiveis[TipoDespacho.Aprovacao]);
        Assert.False(r.Indisponiveis.ContainsKey(TipoDespacho.Correcao));
    }

    [Fact]
    public void sha_diferente_deixa_o_tipo_indisponivel()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-sha");
        DocxFicticio.KitFicticio(pasta, reg => Entrada(reg, "correction")["sha256"] = new string('0', 64));

        var r = RegistroModelos.Ler(pasta);

        Assert.Equal(new[] { TipoDespacho.Aprovacao }, r.Modelos.Select(m => m.Tipo));
        Assert.Equal("O modelo 02 - MODELOS DE DOCUMENTOS/DESPACHO/CORRECAO FICTICIA.docx não confere com o registro do kit (sha256 diferente).",
            r.Indisponiveis[TipoDespacho.Correcao]);
    }

    [Fact]
    public void caminho_fora_da_raiz_e_recusado()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-fora");
        DocxFicticio.KitFicticio(pasta, reg => Entrada(reg, "correction")["path"] = "../fora.docx");

        var r = RegistroModelos.Ler(pasta);

        Assert.Equal("O registro aponta um modelo fora da pasta do kit: ../fora.docx.", r.Indisponiveis[TipoDespacho.Correcao]);
    }

    [Fact]
    public void modelo_ausente_e_modelo_sem_ancora_ficam_indisponiveis()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-ausente");
        DocxFicticio.KitFicticio(pasta, reg => Entrada(reg, "correction")["path"] = "02 - MODELOS DE DOCUMENTOS/DESPACHO/NAO EXISTE.docx");
        var semData = DocxFicticio.Criar("Protocolo nº:", "Requerente: ", "Assunto: **X", "**AO X:", "", "", "______________");
        var caminho = Path.Combine(pasta, "02 - MODELOS DE DOCUMENTOS", "DESPACHO", "APROVACAO FICTICIA.docx");
        File.WriteAllBytes(caminho, semData);
        var registro = Path.Combine(pasta, RegistroModelos.CaminhoRegistro);
        var reg = JsonNode.Parse(File.ReadAllText(registro))!.AsObject();
        Entrada(reg, "approval_simple")["sha256"] = DocxFicticio.Sha(semData);
        File.WriteAllText(registro, reg.ToJsonString());

        var r = RegistroModelos.Ler(pasta);

        Assert.Empty(r.Modelos);
        Assert.Equal("O modelo 02 - MODELOS DE DOCUMENTOS/DESPACHO/NAO EXISTE.docx não foi encontrado no kit.", r.Indisponiveis[TipoDespacho.Correcao]);
        Assert.Equal("O modelo 02 - MODELOS DE DOCUMENTOS/DESPACHO/APROVACAO FICTICIA.docx não está no formato esperado (âncora \"dia de mês de ano\").",
            r.Indisponiveis[TipoDespacho.Aprovacao]);
    }

    [Fact]
    public void revisao_antiga_e_recusada()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-revisao");
        DocxFicticio.KitFicticio(pasta, reg => reg["templates_revision"] = 9);

        var r = RegistroModelos.Ler(pasta);

        Assert.Empty(r.Modelos);
        const string msg = "O registro de modelos do kit está na revisão 9; o editor precisa da revisão 10 ou posterior.";
        Assert.Equal(msg, r.Indisponiveis[TipoDespacho.Correcao]);
        Assert.Equal(msg, r.Indisponiveis[TipoDespacho.Aprovacao]);
    }

    [Fact]
    public void kit_ausente_deixa_os_dois_indisponiveis()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-sem-kit");
        var raiz = Path.Combine(pasta, "nao existe");

        var r = RegistroModelos.Ler(raiz);

        Assert.Empty(r.Modelos);
        var msg = FontesModelos.NaoEncontrados(raiz);
        Assert.Equal(msg, r.Indisponiveis[TipoDespacho.Correcao]);
        Assert.Equal(msg, r.Indisponiveis[TipoDespacho.Aprovacao]);
    }

    [Fact]
    public void texto_de_referencia_fora_do_formato_deixa_o_tipo_indisponivel()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-texto");
        DocxFicticio.KitFicticio(pasta);
        File.WriteAllText(Path.Combine(pasta, TextosFixos.PastaReferencias, DocxFicticio.MdCorrecao), DocxFicticio.TextoCorrecao("Outro fechamento."));

        var r = RegistroModelos.Ler(pasta);

        Assert.Equal(new[] { TipoDespacho.Aprovacao }, r.Modelos.Select(m => m.Tipo));
        Assert.StartsWith("O texto de referência do kit (modelo-despacho-analise-correcao.md) não está no formato esperado: ", r.Indisponiveis[TipoDespacho.Correcao]);
    }

    [Fact]
    public void ler_nao_grava_nada_no_kit()
    {
        var kit = Ambiente.ExigirKit();
        var pastas = new[] { Path.Combine(kit, "02 - MODELOS DE DOCUMENTOS"), Path.Combine(kit, TextosFixos.PastaReferencias) };
        static List<(string, long, DateTime)> Retrato(string p) =>
            Directory.GetFileSystemEntries(p, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                .Select(f => (f, File.Exists(f) ? new FileInfo(f).Length : -1, File.GetLastWriteTimeUtc(f))).ToList();
        var antes = pastas.Select(Retrato).ToList();

        RegistroModelos.Ler(kit);

        Assert.Equal(antes, pastas.Select(Retrato).ToList());
    }

    static JsonObject Entrada(JsonObject reg, string papel) =>
        ((JsonArray)reg["templates"]!).OfType<JsonObject>().Single(e => (string?)e["role"] == papel);
}
