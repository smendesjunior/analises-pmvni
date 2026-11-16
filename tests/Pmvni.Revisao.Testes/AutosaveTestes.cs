using Pmvni.App;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 10: o diário da sessão aberta no app (reiniciado a cada gravação, sem nunca perder o que ainda não está no pacote),
/// a decisão de recuperação na abertura e os textos de somente leitura.</summary>
public class AutosaveTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar()
    {
        var caminho = Ambiente.Corpus("curto.json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static (SessaoRevisao Sessao, ConteudoPacote Conteudo) Sessao()
    {
        var c = Importar();
        return (new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto("ana")), c);
    }

    private static RegistroHistorico Comando(SessaoRevisao s, string texto) =>
        s.Aplicar(new DefinirCampo(s.Projeto.Incluidos[0].Id, "titulo", texto))!;

    /// <summary>As linhas do diário, lidas com ele ainda aberto pela sessão.</summary>
    private static string[] Linhas(string arquivo)
    {
        using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(f);
        return r.ReadToEnd().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
    }

    private static string[] Orfaos(string raiz) =>
        Directory.Exists(Path.Combine(raiz, "recuperacao")) ? Directory.GetFiles(Path.Combine(raiz, "recuperacao"), "*.orfao-*.jsonl") : [];

    [Fact]
    public void diario_da_sessao_registra_e_reinicia_so_com_o_cabecalho_depois_de_gravar()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-reinicia");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        Assert.True(d.Iniciar(pacote, "sha1", []));
        Assert.True(d.Registrar(Comando(s, "Titulo 1")));
        Assert.Single(Diario.Ler(pasta, pacote, "sha1")!.Registros);

        Assert.True(d.DepoisDeGravar(pacote, pacote, "sha2", [], continuar: true));

        Assert.Null(Diario.Ler(pasta, pacote, "sha2"));
        Assert.Single(Linhas(Diario.CaminhoDe(pasta, pacote))); // só o cabeçalho, com o sha novo
        Assert.Contains("sha2", Linhas(Diario.CaminhoDe(pasta, pacote))[0]);
        Assert.Empty(Orfaos(pasta));
        Assert.True(d.Registrar(Comando(s, "Titulo 2")));
        Assert.Single(Diario.Ler(pasta, pacote, "sha2")!.Registros);
    }

    [Fact]
    public void comando_aplicado_durante_a_gravacao_fica_no_diario_novo_e_o_antigo_e_guardado()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-extras");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        d.Iniciar(pacote, "sha1", []);
        d.Registrar(Comando(s, "Titulo 1"));
        var depois = Comando(s, "Titulo 2"); // chegou enquanto gravava
        d.Registrar(depois);

        Assert.True(d.DepoisDeGravar(pacote, pacote, "sha2", [depois], continuar: true));

        var rec = Diario.Ler(pasta, pacote, "sha2")!;
        Assert.True(rec.DaMesmaVersao);
        Assert.Equal([depois.N], rec.Registros.Select(r => r.N));
        Assert.Single(Orfaos(pasta)); // o diário anterior (com os dois) nunca é apagado com trabalho dentro
    }

    [Fact]
    public void salvar_como_leva_o_diario_para_o_arquivo_novo()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-salvar-como");
        var antigo = Path.Combine(pasta, "a.rpmvni");
        var novo = Path.Combine(pasta, "b.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        d.Iniciar(antigo, "sha1", []);
        d.Registrar(Comando(s, "Titulo 1"));

        Assert.True(d.DepoisDeGravar(antigo, novo, "sha2", [], continuar: true));

        Assert.False(File.Exists(Diario.CaminhoDe(pasta, antigo)));
        Assert.Single(Linhas(Diario.CaminhoDe(pasta, novo)));
    }

    [Fact]
    public void somente_leitura_depois_de_gravar_nao_comeca_diario_novo()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-sem-continuar");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        d.Iniciar(pacote, "sha1", []);
        d.Registrar(Comando(s, "Titulo 1"));

        d.DepoisDeGravar(pacote, Path.Combine(pasta, "copia.rpmvni"), "sha2", [], continuar: false);

        Assert.False(d.Ativo);
        Assert.False(File.Exists(Diario.CaminhoDe(pasta, pacote)));
        Assert.False(File.Exists(Diario.CaminhoDe(pasta, Path.Combine(pasta, "copia.rpmvni"))));
    }

    [Fact]
    public void iniciar_com_pendentes_recuperados_grava_os_pendentes_no_diario_novo()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-pendentes");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        var r1 = Comando(s, "Titulo 1");
        var r2 = Comando(s, "Titulo 2");
        using var d = new DiarioDaSessao(pasta);

        Assert.True(d.Iniciar(pacote, "sha1", [r1, r2]));

        Assert.Equal([r1.N, r2.N], Diario.Ler(pasta, pacote, "sha1")!.Registros.Select(r => r.N));
    }

    [Fact]
    public void fechar_guarda_o_diario_com_trabalho_e_apaga_o_vazio()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-fechar");
        var a = Path.Combine(pasta, "a.rpmvni");
        var b = Path.Combine(pasta, "b.rpmvni");
        var (s, _) = Sessao();
        using (var d = new DiarioDaSessao(pasta))
        {
            d.Iniciar(a, "sha1", []);
            d.Registrar(Comando(s, "Titulo 1"));
            d.Fechar(guardar: true);
        }
        using (var d = new DiarioDaSessao(pasta))
        {
            d.Iniciar(b, "sha1", []);
            d.Fechar(guardar: false);
        }

        Assert.NotNull(Diario.Ler(pasta, a, "sha1"));
        Assert.False(File.Exists(Diario.CaminhoDe(pasta, b)));
    }

    [Fact]
    public void dispose_nunca_apaga_o_diario()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-dispose");
        var a = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using (var d = new DiarioDaSessao(pasta))
        {
            d.Iniciar(a, "sha1", []);
            d.Registrar(Comando(s, "Titulo 1"));
        }
        Assert.NotNull(Diario.Ler(pasta, a, "sha1"));
    }

    [Fact]
    public void arquivar_guarda_o_diario_como_orfao_e_devolve_o_nome()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-arquivar");
        var a = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        d.Iniciar(a, "sha1", []);
        d.Registrar(Comando(s, "Titulo 1"));

        var orfao = d.Arquivar(a);

        Assert.NotNull(orfao);
        Assert.True(File.Exists(orfao));
        Assert.False(File.Exists(Diario.CaminhoDe(pasta, a)));
        Assert.False(d.Ativo);
    }

    [Fact]
    public void falha_ao_iniciar_ou_registrar_e_informada_nunca_engolida()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-falha");
        File.WriteAllText(Path.Combine(pasta, "recuperacao"), "um arquivo no lugar da pasta"); // a pasta do diário não pode ser criada
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);

        Assert.False(d.Iniciar(Path.Combine(pasta, "a.rpmvni"), "sha1", []));
        Assert.False(d.Ativo);
        Assert.False(string.IsNullOrWhiteSpace(d.UltimoErro));
        Assert.False(d.Registrar(Comando(s, "Titulo 1")));
        Assert.False(string.IsNullOrWhiteSpace(d.UltimoErro));
    }

    // ---------- T10-C2: falha do diário é tentada de novo no comando seguinte ----------

    [Fact]
    public void iniciar_que_falhou_e_tentado_de_novo_no_registro_seguinte_com_todos_os_pendentes()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-retentar");
        var bloqueio = Path.Combine(pasta, "recuperacao");
        File.WriteAllText(bloqueio, "um arquivo no lugar da pasta");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        Assert.False(d.Iniciar(pacote, "sha1", []));
        var r1 = Comando(s, "Titulo 1");
        Assert.False(d.Registrar(r1, s.Pendentes)); // ainda bloqueado

        File.Delete(bloqueio);
        var r2 = Comando(s, "Titulo 2");
        Assert.True(d.Registrar(r2, s.Pendentes));

        Assert.True(d.Ativo);
        Assert.Equal([r1.N, r2.N], Diario.Ler(pasta, pacote, "sha1")!.Registros.Select(r => r.N));
    }

    [Fact]
    public void diario_que_nao_recomecou_depois_de_gravar_e_tentado_de_novo_com_o_sha_novo()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-retentar-depois");
        var bloqueio = Path.Combine(pasta, "recuperacao");
        File.WriteAllText(bloqueio, "um arquivo no lugar da pasta");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);

        Assert.False(d.DepoisDeGravar(pacote, pacote, "sha2", [], continuar: true));
        File.Delete(bloqueio);
        var r = Comando(s, "Titulo 1");
        Assert.True(d.Registrar(r, s.Pendentes));

        Assert.Equal([r.N], Diario.Ler(pasta, pacote, "sha2")!.Registros.Select(x => x.N));
    }

    // ---------- T10-C1: órfãos com mais de 30 dias saem na abertura do app ----------

    [Fact]
    public void limpeza_apaga_so_orfaos_com_mais_de_30_dias()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-orfaos");
        var rec = Path.Combine(pasta, "recuperacao");
        Directory.CreateDirectory(rec);
        var agora = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);
        string Criar(string nome, double dias)
        {
            var c = Path.Combine(rec, nome);
            File.WriteAllText(c, "{}");
            File.SetLastWriteTimeUtc(c, agora.AddDays(-dias));
            return c;
        }
        var velho = Criar("aaa.orfao-20261101120000.jsonl", 31);
        var novo = Criar("bbb.orfao-20261205120000.jsonl", 29);
        var quase = Criar("ccc.orfao-20261201120000-2.jsonl", 29.9);
        var vivo = Criar("ddd.jsonl", 90); // diário (não órfão): nunca sai pela idade

        Assert.Equal(1, DiarioDaSessao.LimparOrfaosAntigos(pasta, agora));

        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(novo));
        Assert.True(File.Exists(quase));
        Assert.True(File.Exists(vivo));
        Assert.Equal(0, DiarioDaSessao.LimparOrfaosAntigos(Path.Combine(pasta, "nao-existe"), agora));
    }

    [Fact]
    public void limpeza_segue_os_dias_escolhidos_em_ajustes()
    {
        // Ajustes › Relatórios e revisão › Recuperação "Guardar por" 7 · 30 · 90 dias (padrão 30, o de sempre).
        using var pasta = Ambiente.PastaDeTeste("autosave-orfaos-dias");
        var rec = Path.Combine(pasta, "recuperacao");
        Directory.CreateDirectory(rec);
        var agora = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);
        string Criar(string nome, double dias)
        {
            var c = Path.Combine(rec, nome);
            File.WriteAllText(c, "{}");
            File.SetLastWriteTimeUtc(c, agora.AddDays(-dias));
            return c;
        }
        var oito = Criar("aaa.orfao-1.jsonl", 8);
        var quarenta = Criar("bbb.orfao-2.jsonl", 40);
        Assert.Equal(0, DiarioDaSessao.LimparOrfaosAntigos(pasta, agora, TimeSpan.FromDays(90)));
        Assert.Equal(1, DiarioDaSessao.LimparOrfaosAntigos(pasta, agora, TimeSpan.FromDays(30)));
        Assert.False(File.Exists(quarenta));
        Assert.Equal(1, DiarioDaSessao.LimparOrfaosAntigos(pasta, agora, TimeSpan.FromDays(7)));
        Assert.False(File.Exists(oito));
        Assert.Equal(TimeSpan.FromDays(30), DiarioDaSessao.IdadeOrfao);
    }

    [Fact]
    public void arquivar_conta_a_idade_do_orfao_a_partir_do_dia_em_que_foi_guardado()
    {
        using var pasta = Ambiente.PastaDeTeste("autosave-orfao-idade");
        var a = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using var d = new DiarioDaSessao(pasta);
        d.Iniciar(a, "sha1", []);
        d.Registrar(Comando(s, "Titulo 1"));
        // O diário vivo parou de receber registros há 40 dias (ex.: o app caiu e só voltou agora).
        var vivo = Diario.CaminhoDe(pasta, a);
        d.Fechar(guardar: true);
        File.SetLastWriteTimeUtc(vivo, DateTime.UtcNow.AddDays(-40));
        d.Iniciar(a, "sha1", []); // recomeça: o diário de 40 dias é guardado como órfão (Diario.Iniciar)
        var guardadoPeloIniciar = Assert.Single(Orfaos(pasta));
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(guardadoPeloIniciar) < TimeSpan.FromMinutes(5), "o órfão guardado pelo Iniciar conta do dia em que foi guardado");

        // Descartar / Recarregar / outra versão: DiarioDaSessao.Arquivar.
        d.Registrar(Comando(s, "Titulo 2"));
        d.Fechar(guardar: true);
        File.SetLastWriteTimeUtc(vivo, DateTime.UtcNow.AddDays(-40));
        var orfao = d.Arquivar(a);

        Assert.NotNull(orfao);
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(orfao) < TimeSpan.FromMinutes(5), "o órfão conta do dia em que foi guardado, não da última gravação do diário");
        Assert.Equal(0, DiarioDaSessao.LimparOrfaosAntigos(pasta, DateTime.UtcNow)); // um órfão de agora não sai na limpeza seguinte
        Assert.Equal(2, Orfaos(pasta).Length);
        Assert.Equal(2, DiarioDaSessao.LimparOrfaosAntigos(pasta, DateTime.UtcNow.AddDays(31))); // e sai 30 dias depois
    }

    // ---------- decisão na abertura ----------

    [Fact]
    public void sem_diario_nada_a_recuperar()
    {
        var (s, _) = Sessao();
        Assert.Equal(AcaoRecuperacao.Nada, RecuperacaoAbertura.Decidir(null, s.HistoricoCompleto).Acao);
    }

    [Fact]
    public void diario_da_mesma_versao_com_comandos_novos_pergunta_quantos()
    {
        using var pasta = Ambiente.PastaDeTeste("recuperar-pergunta");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, c) = Sessao();
        using (var d = Diario.Iniciar(pasta, pacote, "sha1"))
        {
            s.Registrado += d.Registrar;
            Comando(s, "Titulo 1");
            Comando(s, "Titulo 2");
        }

        var decisao = RecuperacaoAbertura.Decidir(Diario.Ler(pasta, pacote, "sha1"), c.Historico);

        Assert.Equal(AcaoRecuperacao.Perguntar, decisao.Acao);
        Assert.Equal(2, decisao.Comandos);
    }

    [Fact]
    public void diario_de_outra_versao_e_arquivado_sem_aplicar()
    {
        using var pasta = Ambiente.PastaDeTeste("recuperar-outra");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, c) = Sessao();
        using (var d = Diario.Iniciar(pasta, pacote, "sha1"))
        {
            s.Registrado += d.Registrar;
            Comando(s, "Titulo 1");
        }

        var decisao = RecuperacaoAbertura.Decidir(Diario.Ler(pasta, pacote, "sha-outro"), c.Historico);

        Assert.Equal(AcaoRecuperacao.Arquivar, decisao.Acao);
        Assert.False(string.IsNullOrWhiteSpace(decisao.Motivo));
    }

    [Fact]
    public void diario_que_diverge_do_historico_do_pacote_e_arquivado_sem_aplicar()
    {
        using var pasta = Ambiente.PastaDeTeste("recuperar-diverge");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s1, c) = Sessao();
        var s2 = new SessaoRevisao(c.Projeto.Clonar(), c.Historico, Ambiente.Contexto("bia"));
        using (var d = Diario.Iniciar(pasta, pacote, "sha1"))
        {
            s1.Registrado += d.Registrar;
            Comando(s1, "Titulo do diario");
            Comando(s1, "Titulo do diario 2");
        }
        // O pacote tem outro registro com o mesmo N do primeiro do diário.
        Comando(s2, "Titulo do pacote");

        var decisao = RecuperacaoAbertura.Decidir(Diario.Ler(pasta, pacote, "sha1"), s2.HistoricoCompleto);

        Assert.Equal(AcaoRecuperacao.Arquivar, decisao.Acao);
    }

    [Fact]
    public void diario_que_ja_esta_todo_no_pacote_nao_pergunta()
    {
        using var pasta = Ambiente.PastaDeTeste("recuperar-ja-gravado");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using (var d = Diario.Iniciar(pasta, pacote, "sha1"))
        {
            s.Registrado += d.Registrar;
            Comando(s, "Titulo 1");
        }

        var decisao = RecuperacaoAbertura.Decidir(Diario.Ler(pasta, pacote, "sha1"), s.HistoricoCompleto);

        Assert.Equal(AcaoRecuperacao.Nada, decisao.Acao);
    }

    [Fact]
    public void diario_de_sha_antigo_com_tudo_ja_no_pacote_e_arquivado_em_silencio()
    {
        // Queda entre o File.Replace e o recomeço do diário: o diário ficou com o sha antigo, mas tudo o que ele tem já está no arquivo.
        using var pasta = Ambiente.PastaDeTeste("recuperar-queda-na-troca");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var (s, _) = Sessao();
        using (var d = Diario.Iniciar(pasta, pacote, "sha-antes-da-gravacao"))
        {
            s.Registrado += d.Registrar;
            Comando(s, "Titulo 1");
            Comando(s, "Titulo 2");
        }

        var decisao = RecuperacaoAbertura.Decidir(Diario.Ler(pasta, pacote, "sha-depois-da-gravacao"), s.HistoricoCompleto);

        Assert.Equal(AcaoRecuperacao.Arquivar, decisao.Acao);
        Assert.Null(decisao.Motivo); // nenhuma mensagem falsa de "não foram aplicadas"

        // Com um registro que o pacote não tem, continua o aviso de outra versão.
        var (s2, c2) = Sessao();
        using (var d = Diario.Iniciar(pasta, pacote, "sha-antes-da-gravacao"))
        {
            s2.Registrado += d.Registrar;
            Comando(s2, "Titulo 1");
        }
        var outra = RecuperacaoAbertura.Decidir(Diario.Ler(pasta, pacote, "sha-depois-da-gravacao"), c2.Historico);
        Assert.Equal(AcaoRecuperacao.Arquivar, outra.Acao);
        Assert.False(string.IsNullOrWhiteSpace(outra.Motivo));
    }

    // ---------- textos ----------

    [Fact]
    public void texto_do_somente_leitura_por_trava_de_outro()
    {
        var inicio = new DateTimeOffset(2026, 11, 14, 17, 5, 0, TimeSpan.Zero);
        var t = new InfoTrava("PC-OBRAS", "maria", 42, inicio, inicio);
        Assert.Equal($"Aberto por maria em PC-OBRAS desde {inicio.ToLocalTime():HH:mm}: somente leitura.", TextosGravacao.SomenteLeituraOcupada(t));
        var desconhecida = new InfoTrava("?", "?", 0, inicio, inicio);
        Assert.Equal("Aberto em outra janela ou por outro usuário (a trava não pôde ser lida): somente leitura.", TextosGravacao.SomenteLeituraOcupada(desconhecida));
    }

    [Fact]
    public void fechar_sem_gravar_so_promete_recuperacao_quando_ela_vai_ser_oferecida()
    {
        var comum = TextosGravacao.FecharSemGravar("disco cheio.", conflito: false);
        Assert.Contains("disco cheio", comum);
        Assert.Contains("o app oferece recuperá-las", comum);
        var conflito = TextosGravacao.FecharSemGravar("o arquivo mudou no disco depois de aberto", conflito: true);
        Assert.DoesNotContain("oferece recuperá-las", conflito);
        Assert.Contains("não serão oferecidas de novo", conflito);
        Assert.Contains("Salvar como cópia", conflito);
    }

    [Fact]
    public void fechar_com_o_texto_em_edicao_recusado_pergunta_com_o_motivo()
    {
        Assert.Equal("O texto em edição não pôde ser aplicado: O campo verificacao não pode ficar vazio. Fechar mesmo assim?",
            TextosGravacao.FecharSemOTexto("O campo verificacao não pode ficar vazio."));
    }

    [Fact]
    public void salvar_como_no_proprio_arquivo_somente_leitura_ou_em_conflito_e_recusado()
    {
        var atual = Path.GetFullPath(Path.Combine("x", "Relatorio_0001_2099.rpmvni"));
        var mesmo = Path.GetFullPath(Path.Combine("x", "RELATORIO_0001_2099.RPMVNI")); // o Windows não distingue caixa
        var outro = Path.GetFullPath(Path.Combine("x", "Relatorio_0001_2099 (2).rpmvni"));
        const string motivo = "Aberto por maria em PC-OBRAS desde 14:05: somente leitura.";

        // Somente leitura (esquema mais novo, trava de outro, trava perdida): por cima do mesmo arquivo, nunca.
        var r = TextosGravacao.RecusaSalvarComo(mesmo, atual, somenteLeitura: true, conflito: false, motivo);
        Assert.NotNull(r);
        Assert.Contains(motivo.TrimEnd('.'), r);
        Assert.Contains("outro nome", r);
        // Conflito: gravar por cima apagaria a versão que outro programa deixou.
        Assert.Contains("Salvar como cópia", TextosGravacao.RecusaSalvarComo(atual, atual, somenteLeitura: false, conflito: true, null));
        // Outro arquivo, ou o próprio com a sessão editável: segue.
        Assert.Null(TextosGravacao.RecusaSalvarComo(outro, atual, somenteLeitura: true, conflito: true, motivo));
        Assert.Null(TextosGravacao.RecusaSalvarComo(mesmo, atual, somenteLeitura: false, conflito: false, null));
    }

    [Fact]
    public void exportar_sem_gravar_aponta_a_saida()
    {
        Assert.Contains("Salvar como", TextosGravacao.ExportarSemGravar(conflito: false));
        Assert.Contains("Salvar como cópia", TextosGravacao.ExportarSemGravar(conflito: true));
    }
}
