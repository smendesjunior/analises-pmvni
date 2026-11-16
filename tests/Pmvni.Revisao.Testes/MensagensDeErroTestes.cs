using Xunit;

namespace Pmvni.Revisao.Testes;

[Collection("Ambiente")] // mexe no gancho estático do registro
public class MensagensDeErroTestes
{
    [Fact]
    public void arquivo_preso_por_outro_processo_vira_texto_em_portugues_com_o_nome()
    {
        using var pasta = Ambiente.PastaDeTeste("erro-preso");
        var arquivo = Path.Combine(pasta, "Relatorio_0001_2099.rpmvni");
        File.WriteAllText(arquivo, "x");
        using var preso = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.None);
        var e = Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(arquivo));

        var texto = MensagensDeErro.Traduzir(e);

        Assert.Equal("o arquivo Relatorio_0001_2099.rpmvni está em uso por outro programa (feche-o ou espere o OneDrive terminar de sincronizar e tente de novo)", texto);
        Assert.DoesNotContain("process", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(pasta.Caminho, texto, StringComparison.OrdinalIgnoreCase); // o caminho inteiro (com o usuário) não aparece
    }

    [Fact]
    public void sem_permissao_disco_cheio_e_inexistente_em_portugues()
    {
        using var pasta = Ambiente.PastaDeTeste("erro-permissao");
        var arquivo = Path.Combine(pasta, "somente-leitura.rpmvni");
        File.WriteAllText(arquivo, "x");
        File.SetAttributes(arquivo, FileAttributes.ReadOnly);
        try
        {
            var negado = Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(arquivo, "y"));
            Assert.StartsWith("sem permissão para acessar somente-leitura.rpmvni", MensagensDeErro.Traduzir(negado), StringComparison.Ordinal);
        }
        finally { File.SetAttributes(arquivo, FileAttributes.Normal); }

        var cheio = new IOException("There is not enough space on the disk. : 'C:\\x\\a.rpmvni.tmp'", unchecked((int)0x80070070));
        Assert.Equal("não há espaço suficiente no disco para gravar o arquivo a.rpmvni.tmp", MensagensDeErro.Traduzir(cheio));

        var falta = Assert.Throws<FileNotFoundException>(() => File.ReadAllBytes(Path.Combine(pasta, "nao-existe.json")));
        Assert.Equal("o arquivo nao-existe.json não foi encontrado", MensagensDeErro.Traduzir(falta));
    }

    [Fact]
    public void mensagem_do_proprio_editor_passa_como_veio_e_o_detalhe_vai_so_para_o_registro()
    {
        var propria = new IOException("Já existe um arquivo com esse nome: a.rpmvni. Escolha outro nome.");
        Assert.Equal("Já existe um arquivo com esse nome: a.rpmvni. Escolha outro nome", MensagensDeErro.Traduzir(propria));

        var registradas = new List<Exception>();
        MensagensDeErro.Registrar = registradas.Add;
        try
        {
            var preso = new IOException("The process cannot access the file 'C:\\x\\b.rpmvni' because it is being used by another process.", unchecked((int)0x80070020));
            Assert.StartsWith("o arquivo b.rpmvni está em uso", MensagensDeErro.ParaUsuario(preso), StringComparison.Ordinal);
            Assert.Same(preso, Assert.Single(registradas));
        }
        finally { MensagensDeErro.Registrar = null; }
    }
}
