using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>Tarefa 22: o despacho é emitido pelo editor, a partir do relatório revisado, com os modelos oficiais lidos como dados do kit
/// (<c>references\despacho</c> do plugin analise; no kit antigo, os modelos do plugin despacho), sem plugin despacho e sem IA.</summary>
public sealed class DespachoNoEditorTestes
{
    static readonly DateOnly Hoje = new(2099, 3, 10);
    static readonly byte[] PdfVerdana = PdfFicticio.Criar(1, "BAAAAA+Verdana");

    /// <summary>Os modelos e os textos oficiais do kit real (somente leitura), de onde as fontes fictícias são copiadas.</summary>
    static ModelosDisponiveis Oficiais()
    {
        var m = RegistroModelos.Ler(Ambiente.ExigirKit());
        Assert.Equal(2, m.Modelos.Count);
        return m;
    }

    /// <summary>Um plugin analise "instalado" (a pasta da versão) com os dados de despacho em <c>skills\analise\references\despacho</c>: os
    /// modelos oficiais em <c>modelos</c>, os textos e o registro com caminhos a partir da própria pasta. <paramref name="ajustar"/> mexe no
    /// registro antes de gravar.</summary>
    internal static string AnaliseComModelos(string pasta, Action<JsonObject>? ajustar = null)
    {
        var oficiais = Oficiais();
        var plugin = Path.Combine(pasta, "codex", "plugins", "cache", "pmvni-kit", "analise", "4.0.0");
        var destino = Path.Combine(plugin, FontesModelos.PastaNoAnalise.Replace('/', Path.DirectorySeparatorChar));
        Copiar(oficiais, destino, Path.Combine(destino, "modelos"), "modelos/", destino, ajustar);
        return plugin;
    }

    /// <summary>Um plugin despacho antigo "instalado": modelos em <c>skills\despacho\assets\modelos</c>, registro em <c>assets</c> (caminhos a
    /// partir de <c>skills\despacho</c>) e textos em <c>references</c>, como o kit antigo empacotava.</summary>
    static string DespachoAntigoComModelos(string pasta)
    {
        var plugin = Path.Combine(pasta, "codex", "plugins", "cache", "pmvni-kit", "despacho", "2.3.0");
        var skill = Path.Combine(plugin, "skills", "despacho");
        Copiar(Oficiais(), Path.Combine(skill, "assets"), Path.Combine(skill, "assets", "modelos"), "assets/modelos/", Path.Combine(skill, "references"), null);
        return plugin;
    }

    static void Copiar(ModelosDisponiveis oficiais, string pastaRegistro, string pastaModelos, string prefixo, string pastaTextos, Action<JsonObject>? ajustar)
    {
        Directory.CreateDirectory(pastaModelos);
        Directory.CreateDirectory(pastaTextos);
        var templates = new JsonArray();
        foreach (var m in oficiais.Modelos)
        {
            var nome = Path.GetFileName(m.Caminho);
            File.Copy(m.Caminho, Path.Combine(pastaModelos, nome));
            templates.Add(new JsonObject { ["role"] = m.Papel, ["path"] = prefixo + nome, ["sha256"] = m.Sha256 });
        }
        foreach (var t in new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao })
            File.Copy(Path.Combine(oficiais.Fonte!.PastaTextos, TextosFixos.NomeArquivo(t)), Path.Combine(pastaTextos, TextosFixos.NomeArquivo(t)));
        var registro = new JsonObject { ["templates_revision"] = 10, ["templates"] = templates };
        ajustar?.Invoke(registro);
        File.WriteAllText(Path.Combine(pastaRegistro, FontesModelos.NomeRegistro), registro.ToJsonString(), new UTF8Encoding(false));
    }

    /// <summary>Os textos dos parágrafos do corpo do DOCX, na ordem.</summary>
    static List<string> Paragrafos(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var s = zip.GetEntry(PreenchimentoDocx.Documento)!.Open();
        return XDocument.Load(s).Root!.Element(PreenchimentoDocx.W + "body")!.Elements(PreenchimentoDocx.W + "p").Select(PreenchimentoDocx.Texto).ToList();
    }

    /// <summary>O relatório fictício na árvore de processos e o rascunho do tipo, com o modelo da fonte dada.</summary>
    static (ArvoreFicticia Arvore, RascunhoDespacho Rascunho) Rascunho(string pasta, ModeloDespacho modelo)
    {
        var arvore = RelatorioFicticio.Arvore(pasta);
        var etapa = Etapas.Nova(arvore.Processo.Pasta, Hoje);
        var r = ConteudoDespacho.Montar(modelo.Tipo, arvore.Pacote.Conteudo.Projeto,
            PreenchimentoDocx.LerModelo(File.ReadAllBytes(modelo.Caminho), modelo.Tipo), etapa);
        return (arvore, r);
    }

    [Fact]
    public void Modelos_manuais_vem_do_registro_e_das_referencias_do_kit_atual()
    {
        var fonte = FontesModelos.DoKitAtual(Ambiente.ExigirKit());
        var disponiveis = RegistroModelos.Ler([fonte], fonte.Raiz);
        Assert.Empty(disponiveis.Indisponiveis);
        Assert.Equal(2, disponiveis.Modelos.Count);
        foreach (var modelo in disponiveis.Modelos)
        {
            var docx = PreenchimentoDocx.LerModelo(File.ReadAllBytes(modelo.Caminho), modelo.Tipo);
            Assert.NotNull(docx);
            Assert.NotEmpty(TextosFixos.Ler(fonte, modelo.Tipo).Paragrafos);
        }
    }

    [Fact]
    public void Fontes_dos_modelos_na_ordem_com_o_kit_antigo_compativel()
    {
        using var pasta = Ambiente.PastaDeTeste("t22-fontes");
        var kitVazio = Path.Combine(pasta, "kit-vazio");
        Directory.CreateDirectory(kitVazio);

        // A pasta do kit escolhida em Ajustes vem primeiro (a cópia instalada só vale sem ela): com o kit real, os modelos dele, mesmo
        // com o plugin analise instalado trazendo os seus.
        using (var a = Ambiente.PastaDeTeste("t22-fontes-kit"))
        {
            var plugin = AnaliseComModelos(a);
            var kit = Ambiente.ExigirKit();
            var lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", plugin), null, kit), kit);
            Assert.Empty(lidos.Indisponiveis);
            Assert.Empty(lidos.Falhas);
            Assert.Equal(FontesModelos.Escolher(FontesModelos.DaPastaDoKit(kit))!.Origem, lidos.Fonte!.Origem);
            Assert.All(lidos.Modelos, m => Assert.StartsWith(kit, m.Caminho, StringComparison.OrdinalIgnoreCase));

            // Sem a pasta do kit, vale o plugin analise instalado.
            lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", plugin), null, kitVazio), kitVazio);
            Assert.Empty(lidos.Indisponiveis);
            Assert.Equal("plugin analise instalado (versão 4.0.0)", lidos.Fonte!.Origem);
            Assert.Equal(new[] { "modelos/MODELO DESPACHO CORREÇÃO.docx", "modelos/MODELO DE DESPACHO DE APROVAÇÃO.docx" }, lidos.Modelos.Select(m => m.CaminhoRelativo));
            Assert.All(lidos.Modelos, m => Assert.StartsWith(plugin, m.Caminho, StringComparison.OrdinalIgnoreCase));

            // Uma fonte que existe mas falha (o kit com o modelo de correção que não confere) cai para a próxima, e a falha é dita.
            var kitQuebrado = Path.Combine(a, "kit-quebrado");
            DocxFicticio.KitFicticio(kitQuebrado, reg => ((System.Text.Json.Nodes.JsonArray)reg["templates"]!).OfType<JsonObject>()
                .Single(e => (string?)e["role"] == "correction")["sha256"] = new string('0', 64));
            lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", plugin), null, kitQuebrado), kitQuebrado);
            Assert.Empty(lidos.Indisponiveis);
            Assert.Equal("plugin analise instalado (versão 4.0.0)", lidos.Fonte!.Origem);
            var falha = Assert.Single(lidos.Falhas);
            Assert.StartsWith("pasta do kit, 02 - MODELOS DE DOCUMENTOS (" + kitQuebrado + "): ", falha, StringComparison.Ordinal);
            Assert.Contains("não confere com o registro do kit (sha256 diferente)", falha, StringComparison.Ordinal);

            // Se nenhuma fonte oferece os dois tipos, fica a primeira que existe, com o motivo do tipo que falta.
            var parcial = RegistroModelos.Ler(FontesModelos.Candidatas(null, null, kitQuebrado), kitQuebrado);
            Assert.Equal(new[] { TipoDespacho.Aprovacao }, parcial.Modelos.Select(m => m.Tipo));
            Assert.StartsWith("pasta do kit, 02 - MODELOS DE DOCUMENTOS", parcial.Fonte!.Origem, StringComparison.Ordinal);
            Assert.Empty(parcial.Falhas);
        }

        // Kit antigo: o analise instalado ainda sem references\despacho; vale o modelo do plugin despacho instalado (só os dados dele).
        using (var b = Ambiente.PastaDeTeste("t22-fontes-antigo"))
        {
            var analiseSemDados = Path.Combine(b, "codex", "plugins", "cache", "pmvni-kit", "analise", "3.0.1");
            Directory.CreateDirectory(Path.Combine(analiseSemDados, "skills", "analise", "references"));
            var despacho = DespachoAntigoComModelos(b);
            var lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("3.0.1", analiseSemDados), ("2.3.0", despacho), kitVazio));
            Assert.Empty(lidos.Indisponiveis);
            Assert.StartsWith("plugin despacho antigo instalado (versão 2.3.0)", lidos.Fonte!.Origem);
            Assert.Equal("assets/modelos/MODELO DESPACHO CORREÇÃO.docx", lidos.Modelos[0].CaminhoRelativo);
            Assert.Equal(TextosFixos.Ler(Oficiais().Fonte!, TipoDespacho.Correcao).Paragrafos, TextosFixos.Ler(lidos.Fonte, TipoDespacho.Correcao).Paragrafos);
        }

        // Nada instalado e kit sem modelos: os dois tipos indisponíveis com "Atualize o kit PMVNI" e a pasta procurada.
        var nada = RegistroModelos.Ler(FontesModelos.Candidatas(null, null, kitVazio), kitVazio);
        Assert.Empty(nada.Modelos);
        Assert.Null(nada.Fonte);
        var msg = "Os modelos de despacho não foram encontrados: a pasta do kit (" + kitVazio + ") não tem os modelos de despacho. " + FontesModelos.AtualizeOKit;
        Assert.Equal(msg, nada.Indisponiveis[TipoDespacho.Correcao]);
        Assert.Equal(msg, nada.Indisponiveis[TipoDespacho.Aprovacao]);
        Assert.Contains("Atualize o kit PMVNI", msg, StringComparison.Ordinal);
        var semPasta = Path.Combine(pasta, "nao existe");
        Assert.Contains("a pasta do kit não foi encontrada em " + semPasta,
            RegistroModelos.Ler(FontesModelos.Candidatas(null, null, semPasta), semPasta).Indisponiveis[TipoDespacho.Correcao], StringComparison.Ordinal);
    }

    [Fact]
    public void Despacho_Correcao_SemIA()
    {
        using var pasta = Ambiente.PastaDeTeste("t22-correcao");
        var lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", AnaliseComModelos(pasta)), null, Path.Combine(pasta, "kit-vazio")));
        var modelo = lidos.Modelos.Single(m => m.Tipo == TipoDespacho.Correcao);
        var (arvore, r) = Rascunho(pasta, modelo);
        var projeto = arvore.Pacote.Conteudo.Projeto;

        // As exigências vêm do relatório revisado, em código: só as pendentes (≠ ATENDE), na ordem do relatório.
        var todos = ResumoExigencias.DoProjeto(projeto);
        var pendentes = ResumoExigencias.Pendentes(todos);
        Assert.NotEmpty(pendentes);
        Assert.Contains(todos, i => i.Situacao == "ATENDE");
        Assert.Equal(pendentes.Select(p => p.Id), r.Pendencias.Select(p => p.ItemId));

        var final = r.Congelar(TextosFixos.Ler(lidos.Fonte!, TipoDespacho.Correcao));
        var prep = EmissaoDespacho.Preparar(final, modelo, Path.Combine(pasta, "trabalho"), ConversorFalso.Com(PdfVerdana), CancellationToken.None);

        Assert.Empty(prep.Erros);
        var textos = Paragrafos(prep.Docx);
        var posicoes = pendentes.Select((p, i) => textos.IndexOf(ConteudoDespacho.Numero(i + 1)
            + ConteudoDespacho.Exigencia(p.Texto, p.Fundamento).Texto + ConteudoDespacho.Exigencia(p.Texto, p.Fundamento).Fundamento)).ToList();
        Assert.All(posicoes, i => Assert.True(i >= 0));
        Assert.Equal(posicoes.Order(), posicoes);
        foreach (var atende in todos.Where(i => i.Situacao == "ATENDE" && i.Texto.Length > 20))
            Assert.DoesNotContain(textos, t => t.Contains(atende.Texto, StringComparison.Ordinal));
        Assert.Contains(textos, t => t == final.Corpo.Single());

        // Sem IA: a emissão mora numa biblioteca que não conhece os programas de IA (eles ficam no Pmvni.Agentes e no app).
        var referencias = typeof(EmissaoDespacho).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.DoesNotContain("Pmvni.Agentes", referencias);
        Assert.DoesNotContain("Pmvni.App", referencias);
    }

    [Fact]
    public void Despacho_Aprovacao_Modelo()
    {
        using var pasta = Ambiente.PastaDeTeste("t22-aprovacao");
        var lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", AnaliseComModelos(pasta)), null, Path.Combine(pasta, "kit-vazio")));
        var modelo = lidos.Modelos.Single(m => m.Tipo == TipoDespacho.Aprovacao);
        var (_, r) = Rascunho(pasta, modelo);
        r.DefinirIntervencao(Intervencao.ObraNova, true);
        r.DefinirArea("210,52");
        var final = r.Congelar(TextosFixos.Ler(lidos.Fonte!, TipoDespacho.Aprovacao));

        var prep = EmissaoDespacho.Preparar(final, modelo, Path.Combine(pasta, "trabalho"), ConversorFalso.Com(PdfVerdana), CancellationToken.None);

        Assert.Empty(prep.Erros);
        var textos = Paragrafos(prep.Docx);
        string Campo(string rotulo) => textos.Single(t => t.TrimStart().StartsWith(rotulo, StringComparison.Ordinal));
        Assert.Contains("0001/2099", Campo(PreenchimentoDocx.RotuloProtocolo), StringComparison.Ordinal);
        Assert.Contains("FULANO DE EXEMPLO (FICTICIO)", Campo(PreenchimentoDocx.RotuloRequerente), StringComparison.Ordinal);
        Assert.Contains(final.Assunto, Campo(PreenchimentoDocx.RotuloAssunto), StringComparison.Ordinal);
        Assert.Contains(textos, t => t.Contains("10 de março de 2099", StringComparison.Ordinal));
        Assert.Equal(4, final.Corpo.Count);
        Assert.All(final.Corpo, c => Assert.Contains(c, textos));
        Assert.Contains(textos, t => t.StartsWith("Considerando", StringComparison.Ordinal) && t.Contains("obra nova", StringComparison.Ordinal));
        Assert.Contains(textos, t => t.StartsWith("Considerando", StringComparison.Ordinal) && t.Contains("210,52", StringComparison.Ordinal));
        Assert.DoesNotContain(textos, t => t.Contains("XX", StringComparison.Ordinal) || t.Contains("{area}", StringComparison.Ordinal)
                                           || t.Contains("{intervencao}", StringComparison.Ordinal));
    }

    /// <summary>As conferências do <c>validate_template_fidelity.py</c> (DOCX contra o modelo e registro contra os arquivos), em código.</summary>
    [Fact]
    public void Fidelidade_Modelo()
    {
        using var pasta = Ambiente.PastaDeTeste("t22-fidelidade");
        var lidos = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", AnaliseComModelos(pasta)), null, null));
        var m = lidos.Modelos.Single(x => x.Tipo == TipoDespacho.Correcao);
        var modelo = File.ReadAllBytes(m.Caminho);
        var (_, r) = Rascunho(pasta, m);
        var final = r.Congelar(TextosFixos.Ler(lidos.Fonte!, TipoDespacho.Correcao));
        var docx = PreenchimentoDocx.Preencher(modelo, final);

        // O despacho gerado passa nas mesmas conferências (partes fixas, seções, fontes e tamanhos, tabelas) e no conteúdo.
        Assert.Empty(ValidacaoDespacho.Comparar(modelo, docx));
        Assert.Empty(ValidacaoDespacho.Conteudo(modelo, docx, final));

        // Casos que falham, com as mensagens do kit.
        var estilos = ValidacaoDespachoTestes.Trocar(docx, "word/styles.xml", b => [.. b, (byte)' ']);
        Assert.Contains("Componente original alterado/ausente: word/styles.xml", ValidacaoDespacho.Comparar(modelo, estilos));

        var margem = ValidacaoDespachoTestes.Trocar(docx, PreenchimentoDocx.Documento, b =>
            Encoding.UTF8.GetBytes(Regex.Replace(Encoding.UTF8.GetString(b), "(<w:pgMar [^>]*w:left=\")\\d+", "${1}1")));
        Assert.Contains("Seções, margens, formato, orientação ou vínculos de cabeçalho/rodapé alterados.", ValidacaoDespacho.Comparar(modelo, margem));

        var tamanho = ValidacaoDespachoTestes.Trocar(docx, PreenchimentoDocx.Documento, b =>
            Encoding.UTF8.GetBytes(new Regex("<w:r(\\s[^>]*)?>").Replace(Encoding.UTF8.GetString(b), "$0<w:rPr><w:sz w:val=\"97\"/></w:rPr>", 1)));
        Assert.Contains("Fonte ou tamanho direto não existente no template: sz", ValidacaoDespacho.Comparar(modelo, tamanho));

        Assert.StartsWith("Não foi possível comparar os DOCX: ", Assert.Single(ValidacaoDespacho.Comparar(modelo, Encoding.UTF8.GetBytes("não é DOCX"))));
        // O próprio modelo, sem preencher: sobra a marca da data e faltam os valores congelados.
        var semPreencher = ValidacaoDespacho.Conteudo(modelo, modelo, final);
        Assert.Contains("Texto do modelo não preenchido: " + PreenchimentoDocx.MarcaData, semPreencher);
        Assert.Contains("O despacho não traz o valor de Protocolo nº.", semPreencher);

        // Registro: o arquivo que não confere com o sha256 registrado deixa o tipo indisponível (check_registry).
        using var outra = Ambiente.PastaDeTeste("t22-fidelidade-sha");
        var alterado = AnaliseComModelos(outra, reg => ((JsonArray)reg["templates"]!).OfType<JsonObject>()
            .Single(e => (string?)e["role"] == "correction")["sha256"] = new string('0', 64));
        var recusado = RegistroModelos.Ler(FontesModelos.Candidatas(("4.0.0", alterado), null, null));
        Assert.Equal("O modelo modelos/MODELO DESPACHO CORREÇÃO.docx não confere com o registro do kit (sha256 diferente).",
            recusado.Indisponiveis[TipoDespacho.Correcao]);
        Assert.Equal(new[] { TipoDespacho.Aprovacao }, recusado.Modelos.Select(x => x.Tipo));
    }
}
