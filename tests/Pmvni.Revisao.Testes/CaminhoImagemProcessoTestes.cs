using System.Diagnostics;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>O <c>..</c> de um dados.json do kit só vale dentro da pasta do processo (o ancestral com <c>.pmvni-ai</c>; sem ele, a pasta pai
/// da pasta do dados.json); junções e links são resolvidos antes da comparação. No disco de verdade, em <c>tests/saida</c>.</summary>
public class CaminhoImagemProcessoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    /// <summary>Cria <c>&lt;raiz&gt;\PROCESSOS\0001 - 2099\.pmvni-ai\REVISAO_A</c> (a pasta do dados.json) e devolve (raiz, processo, pasta dos dados).</summary>
    private static (string Raiz, string Processo, string Dados) Processo(PastaTeste pasta)
    {
        var processo = Path.Combine(pasta, "PROCESSOS", "0001 - 2099");
        var dados = Path.Combine(processo, ".pmvni-ai", "REVISAO_A");
        Directory.CreateDirectory(dados);
        return (pasta, processo, dados);
    }

    private static void Gravar(string caminho)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllBytes(caminho, File.ReadAllBytes(Ambiente.Corpus("img/mapa.png")));
    }

    /// <summary>Junção de diretório (não precisa de administrador): <c>mklink /J</c>.</summary>
    private static void Juncao(string link, string alvo)
    {
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Arguments = $"/c mklink /J \"{link}\" \"{alvo}\"";
        using var p = Process.Start(psi)!;
        var saida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link), "mklink /J falhou: " + saida);
    }

    [Fact]
    public void ponto_ponto_que_sai_do_processo_e_recusado()
    {
        using var pasta = Ambiente.PastaDeTeste("img-processo-fora");
        var (raiz, _, dados) = Processo(pasta);
        Gravar(Path.Combine(raiz, "PROCESSOS", "fora.png"));            // irmão da pasta do processo
        Gravar(Path.Combine(raiz, "PROCESSOS", "0002 - 2099", "x.png")); // outro processo

        Assert.Null(CaminhoImagem.Resolver(dados, "../../../fora.png", soNaPasta: false));
        Assert.Null(CaminhoImagem.Resolver(dados, "../../../0002 - 2099/x.png", soNaPasta: false));
        Assert.Null(CaminhoImagem.Resolver(dados, "../../../../" + Path.GetFileName(raiz) + "/PROCESSOS/fora.png", soNaPasta: false));
    }

    [Fact]
    public void ponto_ponto_dentro_do_processo_e_aceito()
    {
        using var pasta = Ambiente.PastaDeTeste("img-processo-dentro");
        var (_, processo, dados) = Processo(pasta);
        var mapa = Path.Combine(processo, ".pmvni-ai", "REANALISE_x", "renders", "mapa.png");
        Gravar(mapa);
        Gravar(Path.Combine(processo, "PROJETO", "planta.png"));

        Assert.Equal(mapa, CaminhoImagem.Resolver(dados, "../REANALISE_x/renders/mapa.png", soNaPasta: false));
        // Subir além do .pmvni-ai mas ficar no processo também vale.
        Assert.Equal(Path.Combine(processo, "PROJETO", "planta.png"), CaminhoImagem.Resolver(dados, "../../PROJETO/planta.png", soNaPasta: false));
        Assert.True(CaminhoImagem.ImagemAceita("../REANALISE_x/renders/mapa.png", mapa));
    }

    [Fact]
    public void sem_pmvni_ai_o_limite_e_a_pasta_pai_dos_dados()
    {
        using var pasta = Ambiente.PastaDeTeste("img-sem-ia");
        var dados = Path.Combine(pasta, "proc", "analise");
        Directory.CreateDirectory(dados);
        Gravar(Path.Combine(pasta, "proc", "renders", "mapa.png"));
        Gravar(Path.Combine(pasta, "fora.png"));

        Assert.NotNull(CaminhoImagem.Resolver(dados, "../renders/mapa.png", soNaPasta: false));
        Assert.Null(CaminhoImagem.Resolver(dados, "../../fora.png", soNaPasta: false));
    }

    [Fact]
    public void juncao_que_aponta_para_fora_do_processo_e_recusada()
    {
        using var pasta = Ambiente.PastaDeTeste("img-juncao-fora");
        var (raiz, processo, dados) = Processo(pasta);
        var fora = Path.Combine(raiz, "OUTRO");
        Gravar(Path.Combine(fora, "segredo.png"));
        Juncao(Path.Combine(dados, "atalho"), fora);

        // Sem .. nenhum: o caminho parece estar dentro, mas a junção leva para fora.
        Assert.Null(CaminhoImagem.Resolver(dados, "atalho/segredo.png", soNaPasta: false));
        Assert.Null(CaminhoImagem.Resolver(dados, "atalho/segredo.png", soNaPasta: true));
        // Junção na pasta do processo (alcançada por ..) também.
        Juncao(Path.Combine(processo, "atalho2"), fora);
        Assert.Null(CaminhoImagem.Resolver(dados, "../../atalho2/segredo.png", soNaPasta: false));
    }

    [Fact]
    public void juncao_que_aponta_para_dentro_do_processo_e_aceita()
    {
        using var pasta = Ambiente.PastaDeTeste("img-juncao-dentro");
        var (_, processo, dados) = Processo(pasta);
        Gravar(Path.Combine(processo, "PROJETO", "planta.png"));
        Juncao(Path.Combine(dados, "atalho"), Path.Combine(processo, "PROJETO"));

        Assert.NotNull(CaminhoImagem.Resolver(dados, "atalho/planta.png", soNaPasta: false));
    }

    [Fact]
    public void importar_recusa_ponto_ponto_que_sai_do_processo_e_aceita_o_irmao_dentro()
    {
        using var pasta = Ambiente.PastaDeTeste("img-importar-processo");
        var (raiz, processo, dados) = Processo(pasta);
        var origem = Ambiente.Corpus("extremos.json");
        var json = JsonFiel.Ler(File.ReadAllBytes(origem));
        Gravar(Path.Combine(raiz, "PROCESSOS", "fora.png"));
        Gravar(Path.Combine(processo, ".pmvni-ai", "REANALISE_x", "renders", "mapa.png"));
        // As demais imagens do corpus (figuras) ficam ao lado dos dados.
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(origem)! + Path.DirectorySeparatorChar + "img"))
            Gravar(Path.Combine(dados, "img", Path.GetFileName(f)));

        json["localizacao"]!["imagem"] = "../../../fora.png";
        var caminho = Path.Combine(dados, "dados.json");
        File.WriteAllBytes(caminho, JsonFiel.Escrever(json));
        var ex = Assert.Throws<ImportacaoRecusadaException>(() => Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto()));
        Assert.Contains(ex.Erros, e => e.StartsWith("Localização: mapa não encontrado", StringComparison.Ordinal));

        json["localizacao"]!["imagem"] = "../REANALISE_x/renders/mapa.png";
        File.WriteAllBytes(caminho, JsonFiel.Escrever(json));
        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
        Assert.StartsWith("assets/", c.Projeto.Documento["localizacao"]!["imagem"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("img/x.bmp")]
    [InlineData("img/x.svg")]
    [InlineData("img/x")]
    public void renderizador_so_le_extensoes_de_imagem_permitidas(string rel)
    {
        Assert.False(CaminhoImagem.ExtensaoPermitida(rel));
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.JPG")]
    [InlineData("a.jpeg")]
    [InlineData("a.webp")]
    [InlineData("a.gif")]
    public void extensoes_de_imagem_permitidas(string rel)
    {
        Assert.True(CaminhoImagem.ExtensaoPermitida(rel));
    }

    [Fact]
    public void renderizador_recusa_arquivo_com_extensao_de_imagem_e_assinatura_falsa()
    {
        using var pasta = Ambiente.PastaDeTeste("img-assinatura-falsa");
        var arquivo = Path.Combine(pasta, "falso.png");
        File.WriteAllText(arquivo, "isto nao e uma imagem");
        Assert.False(CaminhoImagem.ImagemAceita("falso.png", arquivo));
        var texto = Path.Combine(pasta, "dados.txt");
        Gravar(texto);
        Assert.False(CaminhoImagem.ImagemAceita("dados.txt", texto)); // assinatura boa, extensão não
        var ok = Path.Combine(pasta, "ok.png");
        Gravar(ok);
        Assert.True(CaminhoImagem.ImagemAceita("ok.png", ok));
    }
}
