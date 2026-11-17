using System.Reflection;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Faixa de opções por dados (Tarefa 8): uma definição por módulo, Arquivo primeiro e Ajuda por último, aba padrão a segunda.</summary>
public class FaixasTestes
{
    /// <summary>Os módulos com faixa: os disponíveis no trilho (Relatórios e Ajustes, o último embaixo).</summary>
    private static IEnumerable<Modulo> ComFaixa() => Faixas.Disponiveis.Append(Modulo.Ajustes).Distinct();

    [Fact]
    public void toda_faixa_comeca_em_arquivo_e_termina_em_ajuda()
    {
        Assert.NotEmpty(Faixas.Disponiveis);
        Assert.Contains(Modulo.Relatorios, Faixas.Disponiveis);
        foreach (var m in ComFaixa())
        {
            var f = Faixas.Para(m);
            Assert.Equal(m, f.Modulo);
            if (m != Modulo.Ajustes) Assert.Equal("Arquivo", f.Abas[0].Nome); // Ajustes não abre relatórios: só a Ajuda (2026-11-17)
            Assert.Equal("Ajuda", f.Abas[^1].Nome);
            Assert.All(f.Abas, a => Assert.NotEmpty(a.Comandos));
        }
        Assert.Equal(new[] { "Ajuda" }, Faixas.Para(Modulo.Ajustes).Abas.Select(a => a.Nome));
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Revisão", "Figuras", "Exibir", "Ajuda" }, Faixas.Para(Modulo.Relatorios).Abas.Select(a => a.Nome));
    }

    [Fact]
    public void aba_padrao_e_a_segunda_exceto_ajustes()
    {
        foreach (var m in ComFaixa())
        {
            var f = Faixas.Para(m);
            Assert.Equal(m == Modulo.Ajustes ? 0 : 1, f.AbaPadrao);
            Assert.InRange(f.AbaPadrao, 0, f.Abas.Count - 1);
        }
    }

    [Fact]
    public void ids_de_comando_unicos_e_conhecidos()
    {
        var conhecidos = typeof(ComandosApp).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(c => c.IsLiteral && c.FieldType == typeof(string))
            .Select(c => (string)c.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("arquivo.importar", conhecidos);
        Assert.Contains("ajuda.sobre", conhecidos);
        foreach (var m in ComFaixa())
        {
            // O mesmo comando pode aparecer em duas abas (Exportar PDF em Arquivo e em Página Inicial); dentro de uma aba, não.
            foreach (var aba in Faixas.Para(m).Abas)
            {
                var ids = aba.Comandos.SelectMany(c => c.Itens is null ? [c] : c.Itens.Prepend(c)).Select(c => c.Id).ToList();
                Assert.All(ids, id => Assert.Contains(id, conhecidos));
                Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            }
            Assert.All(Faixas.Para(m).Abas.SelectMany(a => a.Comandos), c => Assert.False(string.IsNullOrWhiteSpace(c.Rotulo)));
        }
    }

    [Fact]
    public void pagina_inicial_e_revisao_tem_os_comandos_da_revisao()
    {
        var f = Faixas.Para(Modulo.Relatorios);
        var inicio = f.Abas.Single(a => a.Nome == "Página Inicial");
        Assert.Equal(new[]
        {
            ComandosApp.InicioSituacao, ComandosApp.InicioNovoItem, ComandosApp.InicioExcluirRestaurar, ComandosApp.InicioEditarTexto, ComandosApp.InicioOriginal,
            ComandosApp.InicioRestaurarOriginal, ComandosApp.InicioItemAnterior, ComandosApp.InicioItemProximo, ComandosApp.InicioProximoPendente,
            ComandosApp.FigurasAbrirEditor, ComandosApp.ArquivoExportarPdf, ComandosApp.ArquivoImprimir, ComandosApp.DespachosNovo,
        }, inicio.Comandos.Select(c => c.Id));
        var situacao = inicio.Comandos[0];
        Assert.Equal(TipoComando.Menu, situacao.Tipo);
        // As 4 situações de item do kit, pelos textos que o relatório imprime.
        Assert.Equal(Pmvni.Composicao.Kit.Situacoes.SitItem.Order(StringComparer.Ordinal), situacao.Itens!.Select(i => ComandosApp.SituacaoDoComando(i.Id)!).Order(StringComparer.Ordinal));
        Assert.All(situacao.Itens!, i => Assert.Equal(Pmvni.Composicao.Kit.Situacoes.Sit[ComandosApp.SituacaoDoComando(i.Id)!].Texto, i.Rotulo));
        Assert.Equal(TipoComando.Alternar, inicio.Comandos.Single(c => c.Id == ComandosApp.InicioOriginal).Tipo);

        var revisao = f.Abas.Single(a => a.Nome == "Revisão");
        Assert.Equal(new[] { ComandosApp.RevisaoMarcas, ComandosApp.RevisaoSomenteAlterados, ComandosApp.RevisaoConferencia, ComandosApp.RevisaoComparar }, revisao.Comandos.Select(c => c.Id));
        Assert.Equal(new[] { TipoComando.Alternar, TipoComando.Alternar, TipoComando.Botao, TipoComando.Botao }, revisao.Comandos.Select(c => c.Tipo));

        var exibir = f.Abas.Single(a => a.Nome == "Exibir");
        Assert.Equal(TipoComando.Alternar, exibir.Comandos.Single(c => c.Id == ComandosApp.ExibirPropriedades).Tipo);
    }

    [Fact]
    public void ajustes_no_trilho_e_comandos_de_pesquisa_tema_recentes_e_comparar()
    {
        // Ajustes é módulo do trilho (embaixo) e tem faixa própria, só com a Ajuda (auditoria de interface 2026-11-17: a aba Arquivo confundia).
        Assert.Equal(new[] { Modulo.Relatorios, Modulo.Despachos, Modulo.Processos, Modulo.Legislacao, Modulo.Acompanhamento, Modulo.Imoveis, Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco, Modulo.Ajustes }, Faixas.Disponiveis);
        Assert.True(Modulos.Embaixo(Modulo.Ajustes));
        Assert.Equal(new[] { ComandosApp.AjudaAtalhos, ComandosApp.AjudaSobre }, Faixas.Para(Modulo.Ajustes).Abas[0].Comandos.Select(c => c.Id));

        var arquivo = Faixas.Para(Modulo.Relatorios).Abas.Single(a => a.Nome == "Arquivo");
        var recentes = arquivo.Comandos.Single(c => c.Id == ComandosApp.ArquivoRecentes);
        Assert.Equal(TipoComando.Menu, recentes.Tipo);
        Assert.Empty(recentes.Itens!); // os itens vêm dos ajustes, na hora de abrir o menu

        // Exibir › Tema: Sistema, Claro, Escuro, com os ids que levam ao valor guardado em Ajustes.Tema.
        var tema = Faixas.Para(Modulo.Relatorios).Abas.Single(a => a.Nome == "Exibir").Comandos.Single(c => c.Id == ComandosApp.ExibirTema);
        Assert.Equal(TipoComando.Menu, tema.Tipo);
        Assert.Equal(new[] { "Acompanhar o Windows", "Claro", "Escuro" }, tema.Itens!.Select(i => i.Rotulo));
        Assert.Equal(Ajustes.Temas, tema.Itens!.Select(i => ComandosApp.TemaDoComando(i.Id)!));
        Assert.Null(ComandosApp.TemaDoComando(ComandosApp.ExibirTema));
        Assert.Null(ComandosApp.TemaDoComando(ComandosApp.ExibirPainel));

        // O atalho de pesquisa existe e Propriedades (Tarefa 9) continua uma só.
        Assert.Equal("Ctrl+F", Atalhos.Tecla(ComandosApp.PesquisaFocar));
        Assert.Single(Faixas.Comandos(Faixas.Para(Modulo.Relatorios)), c => c.Id == ComandosApp.ExibirPropriedades);
    }

    [Fact]
    public void id_em_duas_abas_e_o_mesmo_comando()
    {
        foreach (var m in ComFaixa())
        {
            var repetidos = Faixas.Comandos(Faixas.Para(m)).GroupBy(c => c.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).ToList();
            // Mesmo id em duas abas = o mesmo comando: rótulo, ícone, tipo e itens iguais (e um manipulador só, na janela).
            Assert.All(repetidos, g => Assert.All(g, c => Assert.Equal(g.First(), c)));
            if (m == Modulo.Relatorios)
            {
                Assert.Equal(2, repetidos.Single(g => g.Key == ComandosApp.ArquivoExportarPdf).Count());
                Assert.Equal(2, repetidos.Single(g => g.Key == ComandosApp.ArquivoImprimir).Count());
            }
        }
    }

    [Fact]
    public void trilho_na_ordem_do_desenho()
    {
        // Relatórios, Despachos, Processos, Legislação (Tarefa 18; desenho §11.4) e, embaixo, Ajustes.
        Assert.Equal(new[] { Modulo.Relatorios, Modulo.Despachos, Modulo.Processos, Modulo.Legislacao, Modulo.Acompanhamento, Modulo.Imoveis, Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco, Modulo.Ajustes }, Faixas.Disponiveis);
        Assert.Equal(new[] { Modulo.Ajustes }, Faixas.Disponiveis.Where(Modulos.Embaixo));
    }

    [Fact]
    public void cada_modulo_tem_a_faixa_do_desenho()
    {
        static string[] Abas(Modulo m) => Faixas.Para(m).Abas.Select(a => a.Nome).ToArray();
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Revisão", "Figuras", "Exibir", "Ajuda" }, Abas(Modulo.Relatorios));
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Exibir", "Ajuda" }, Abas(Modulo.Despachos));
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Exibir", "Ajuda" }, Abas(Modulo.Processos));
        Assert.Equal(new[] { "Ajuda" }, Abas(Modulo.Ajustes));
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Exibir", "Ajuda" }, Abas(Modulo.Legislacao));

        // Processos (desenho §9.1).
        var processos = Faixas.Para(Modulo.Processos);
        var inicioP = processos.Abas.Single(a => a.Nome == "Página Inicial");
        Assert.Equal(new[]
        {
            ComandosApp.ProcessosAbrirRelatorio, ComandosApp.ProcessosImportarAnalise, ComandosApp.DespachosNovo, ComandosApp.ProcessosAbrirArquivo,
            ComandosApp.ArquivoImprimir, ComandosApp.ProcessosAbrirPasta, ComandosApp.ProcessosAtualizar, ComandosApp.ProcessosAno,
        }, inicioP.Comandos.Select(c => c.Id));
        Assert.Equal(TipoComando.Menu, inicioP.Comandos.Single(c => c.Id == ComandosApp.ProcessosAno).Tipo);
        // O menu do ano é dinâmico (os anos vêm da pasta): a definição o deixa vazio, como Recentes.
        Assert.Empty(inicioP.Comandos.Single(c => c.Id == ComandosApp.ProcessosAno).Itens!);
        var exibirP = processos.Abas.Single(a => a.Nome == "Exibir");
        Assert.Equal(new[] { ComandosApp.ProcessosOrdenar }, exibirP.Comandos.Select(c => c.Id));
        var ordenarP = exibirP.Comandos[0];
        Assert.Equal(TipoComando.Menu, ordenarP.Tipo);
        Assert.Equal(new[] { "Número", "Data", "Requerente" }, ordenarP.Itens!.Select(i => i.Rotulo));

        // Despachos (desenho §9.1).
        var despachos = Faixas.Para(Modulo.Despachos);
        var inicioD = despachos.Abas.Single(a => a.Nome == "Página Inicial");
        Assert.Equal(new[]
        {
            ComandosApp.DespachosAbrir, ComandosApp.DespachosAbrirPdf, ComandosApp.DespachosGerarPdf, ComandosApp.ArquivoImprimir,
            ComandosApp.DespachosMostrar, ComandosApp.DespachosIrAoProcesso,
            ComandosApp.DespachosAtualizar, ComandosApp.DespachosFiltro, ComandosApp.DespachosDoProcesso,
        }, inicioD.Comandos.Select(c => c.Id));
        var filtro = inicioD.Comandos.Single(c => c.Id == ComandosApp.DespachosFiltro);
        Assert.Equal(TipoComando.Menu, filtro.Tipo);
        Assert.Equal(new[] { "Todos", "Correção", "Aprovação" }, filtro.Itens!.Select(i => i.Rotulo));
        Assert.Equal(TipoComando.Alternar, inicioD.Comandos.Single(c => c.Id == ComandosApp.DespachosDoProcesso).Tipo);
        var exibirD = despachos.Abas.Single(a => a.Nome == "Exibir");
        Assert.Equal(new[] { ComandosApp.DespachosAgrupar, ComandosApp.DespachosOrdenar }, exibirD.Comandos.Select(c => c.Id));
        Assert.Equal(new[] { "Por processo", "Por data" }, exibirD.Comandos[0].Itens!.Select(i => i.Rotulo));
        Assert.Equal(new[] { "Mais recentes primeiro", "Mais antigos primeiro" }, exibirD.Comandos[1].Itens!.Select(i => i.Rotulo));

        // Arquivo (Importar, Abrir, Recentes) vale em todos; Ajuda é a de sempre.
        foreach (var m in new[] { Modulo.Despachos, Modulo.Processos })
        {
            Assert.Equal(new[] { ComandosApp.ArquivoImportar, ComandosApp.ArquivoAbrir, ComandosApp.ArquivoRecentes },
                Faixas.Para(m).Abas[0].Comandos.Select(c => c.Id));
            Assert.Equal(1, Faixas.Para(m).AbaPadrao);
        }
    }

    [Fact]
    public void novo_despacho_nos_tres_modulos_com_os_dois_tipos()
    {
        foreach (var m in new[] { Modulo.Despachos, Modulo.Processos, Modulo.Relatorios })
        {
            var inicio = Faixas.Para(m).Abas.Single(a => a.Nome == "Página Inicial").Comandos;
            // Em Despachos o Novo despacho é o botão primário (à esquerda dos comandos, em todas as abas); nos outros, fica na Página Inicial.
            var novo = m == Modulo.Despachos ? Faixas.Para(m).Primario! : Assert.Single(inicio, c => c.Id == ComandosApp.DespachosNovo);
            Assert.Equal(ComandosApp.DespachosNovo, novo.Id);
            Assert.Equal(TipoComando.Menu, novo.Tipo);
            Assert.Equal("Novo despacho", novo.Rotulo);
            Assert.Equal(new[] { "despachos.novo.correcao", "despachos.novo.correcaoAutomatica", "despachos.novo.aprovacao" }, novo.Itens!.Select(i => i.Id));
            Assert.Equal(new[] { "Correção", "Correção automática (itens pendentes do relatório)", "Aprovação" }, novo.Itens!.Select(i => i.Rotulo));
            var i = inicio.ToList().IndexOf(novo);
            switch (m)
            {
                case Modulo.Despachos: Assert.Equal(-1, i); break;
                case Modulo.Processos: Assert.Equal(ComandosApp.ProcessosImportarAnalise, inicio[i - 1].Id); break;
                default: Assert.Equal(inicio.Count - 1, i); break;
            }
            // Nenhuma outra aba do módulo tem o Novo despacho.
            Assert.Single(Faixas.Comandos(Faixas.Para(m)), c => c.Id == ComandosApp.DespachosNovo);
        }
        Assert.DoesNotContain(Faixas.Comandos(Faixas.Para(Modulo.Ajustes)), c => c.Id.StartsWith("despachos.", StringComparison.Ordinal));
        // Os itens do menu valem fora de Despachos (abrem a página Novo despacho no módulo Despachos); o resto de despachos.* não.
        Assert.True(ComandosApp.ValeForaDeDespachos(ComandosApp.DespachosNovoCorrecao));
        Assert.True(ComandosApp.ValeForaDeDespachos(ComandosApp.DespachosNovoAprovacao));
        Assert.False(ComandosApp.ValeForaDeDespachos(ComandosApp.DespachosGerarPdf));
        Assert.False(ComandosApp.ValeForaDeDespachos(ComandosApp.DespachosAbrir));
        Assert.Equal(TipoDespacho.Correcao, ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovoCorrecao));
        Assert.Equal(TipoDespacho.Aprovacao, ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovoAprovacao));
        Assert.Null(ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovo));
    }

    [Fact]
    public void gerar_pdf_so_em_despachos()
    {
        foreach (var m in Faixas.Disponiveis)
        {
            var todos = Faixas.Comandos(Faixas.Para(m)).Where(c => c.Id == ComandosApp.DespachosGerarPdf).ToList();
            if (m != Modulo.Despachos) { Assert.Empty(todos); continue; }
            Assert.Single(todos);
            var inicio = Faixas.Para(m).Abas.Single(a => a.Nome == "Página Inicial").Comandos.ToList();
            var i = inicio.FindIndex(c => c.Id == ComandosApp.DespachosGerarPdf);
            Assert.Equal(ComandosApp.DespachosAbrirPdf, inicio[i - 1].Id);
            Assert.Equal("Gerar PDF", inicio[i].Rotulo);
            Assert.Equal(TipoComando.Botao, inicio[i].Tipo);
        }
    }

    [Fact]
    public void comandos_do_relatorio_nao_valem_nos_outros_modulos()
    {
        // Delete, Ctrl+Z, Alt+↓ e os do zoom não podem mexer num relatório que ficou escondido atrás de Processos ou Despachos.
        foreach (var id in new[]
        {
            ComandosApp.InicioExcluirRestaurar, ComandosApp.InicioEditarTexto, ComandosApp.InicioSituacaoAtende, ComandosApp.InicioItemProximo,
            ComandosApp.RevisaoMarcas, ComandosApp.RevisaoComparar, ComandosApp.EditarDesfazer, ComandosApp.EditarRefazer,
            ComandosApp.ExibirMais, ComandosApp.ExibirMenos, ComandosApp.ExibirLargura, ComandosApp.ExibirPagina,
            ComandosApp.ExibirPainel, ComandosApp.ExibirPropriedades, ComandosApp.JanelaRegioes,
        })
            Assert.True(ComandosApp.SoDoRelatorio(id), id);
        // Arquivo (Salvar, Abrir…), a pesquisa e os comandos dos outros módulos seguem valendo.
        foreach (var id in new[]
        {
            ComandosApp.ArquivoAbrir, ComandosApp.ArquivoSalvar, ComandosApp.PesquisaFocar, ComandosApp.AjudaSobre, ComandosApp.ExibirTemaEscuro,
            ComandosApp.ProcessosAtualizar, ComandosApp.DespachosAbrir,
        })
            Assert.False(ComandosApp.SoDoRelatorio(id), id);
    }

    [Fact]
    public void novo_relatorio_na_faixa()
    {
        Assert.Equal("relatorios.novo", ComandosApp.RelatoriosNovo);
        var relatorios = Faixas.Para(Modulo.Relatorios);
        // Primário de Relatórios; correção 2026-11-16: não se repete em Arquivo, e Abrir é o segundo botão verde, ao lado dele.
        Assert.Equal(ComandosApp.RelatoriosNovo, relatorios.Primario!.Id);
        Assert.Equal(TipoComando.Botao, relatorios.Primario.Tipo);
        Assert.Equal("Novo relatório", relatorios.Primario.Rotulo);
        Assert.DoesNotContain(relatorios.Abas.Single(a => a.Nome == "Arquivo").Comandos, c => c.Id == ComandosApp.RelatoriosNovo);
        Assert.Equal(ComandosApp.ArquivoAbrir, relatorios.Secundario!.Id);
        Assert.DoesNotContain(relatorios.Abas.SelectMany(a => a.Comandos), c => c.Id == ComandosApp.ArquivoAbrir);
        Assert.Single(Faixas.Comandos(relatorios), c => c.Id == ComandosApp.ArquivoAbrir);
        Assert.DoesNotContain(relatorios.Abas.Single(a => a.Nome == "Página Inicial").Comandos, c => c.Id == ComandosApp.RelatoriosNovo);
        Assert.Equal(ComandosApp.DespachosNovo, relatorios.Abas.Single(a => a.Nome == "Página Inicial").Comandos[^1].Id);

        // Processos: primário Novo relatório; Novo despacho fica na Página Inicial.
        var processos = Faixas.Para(Modulo.Processos);
        Assert.Equal(ComandosApp.RelatoriosNovo, processos.Primario!.Id);
        Assert.Single(Faixas.Comandos(processos), c => c.Id == ComandosApp.RelatoriosNovo);
        Assert.DoesNotContain(processos.Abas.SelectMany(a => a.Comandos), c => c.Id == ComandosApp.RelatoriosNovo);

        foreach (var m in new[] { Modulo.Despachos, Modulo.Ajustes })
            Assert.DoesNotContain(Faixas.Comandos(Faixas.Para(m)), c => c.Id == ComandosApp.RelatoriosNovo);
        // Abrir a página Novo relatório não mexe no relatório aberto: vale em qualquer módulo.
        Assert.False(ComandosApp.SoDoRelatorio(ComandosApp.RelatoriosNovo));
    }

    [Fact]
    public void botao_primario_por_modulo()
    {
        Assert.Equal(ComandosApp.RelatoriosNovo, Faixas.Para(Modulo.Relatorios).Primario?.Id);
        Assert.Equal(ComandosApp.DespachosNovo, Faixas.Para(Modulo.Despachos).Primario?.Id);
        Assert.Equal(TipoComando.Menu, Faixas.Para(Modulo.Despachos).Primario!.Tipo);
        Assert.Equal(ComandosApp.RelatoriosNovo, Faixas.Para(Modulo.Processos).Primario?.Id);
        Assert.Equal(ComandosApp.LegislacaoAnexar, Faixas.Para(Modulo.Legislacao).Primario?.Id);
        Assert.Null(Faixas.Para(Modulo.Ajustes).Primario);
        // O primário entra em Faixas.Comandos (manipulador obrigatório) e não se repete na barra da mesma faixa (2026-11-16: nem em Relatórios).
        foreach (var m in Faixas.Disponiveis.Where(m => Faixas.Para(m).Primario is not null))
        {
            var f = Faixas.Para(m);
            Assert.Equal(f.Primario, Faixas.Comandos(f).First());
            var naBarra = f.Abas.SelectMany(a => a.Comandos).Count(c => c.Id == f.Primario!.Id);
            Assert.Equal(0, naBarra);
        }
    }

    [Fact]
    public void rotulos_curtos_com_texto_completo_na_descricao()
    {
        // Auditoria A5: nenhum rótulo cortado na faixa ("Restaurar ori…"); o texto completo vai na dica e no nome acessível.
        var esperado = new Dictionary<string, (string Rotulo, string Completo)>
        {
            [ComandosApp.InicioRestaurarOriginal] = ("Restaurar", "Restaurar o original"),
            [ComandosApp.RevisaoComparar] = ("Comparar", "Comparar com o original"),
            [ComandosApp.DespachosDoProcesso] = ("Só do processo", "Só os despachos do processo escolhido"),
            [ComandosApp.ProcessosImportarAnalise] = ("Importar análise", "Importar a análise da IA"),
            [ComandosApp.ProcessosAbrirPasta] = ("Abrir pasta", "Abrir a pasta no Explorador"),
        };
        var todos = Faixas.Disponiveis.SelectMany(m => Faixas.Comandos(Faixas.Para(m))).ToList();
        foreach (var (id, (rotulo, completo)) in esperado)
        {
            var c = todos.First(x => x.Id == id);
            Assert.Equal(rotulo, c.Rotulo);
            Assert.Equal(completo, c.TextoCompleto);
            Assert.True(c.Rotulo.Length <= 16, c.Rotulo);
        }
        // Auditoria M3: nenhuma seta tipográfica nos rótulos (o Narrador lia "triângulo"); a seta é o ícone.
        Assert.All(todos, c => Assert.DoesNotContain(c.Rotulo, ch => ch is (char)0x25C2 or (char)0x25B8));
        Assert.Equal("Item anterior", todos.First(c => c.Id == ComandosApp.InicioItemAnterior).Rotulo);
        Assert.Equal("Próximo item", todos.First(c => c.Id == ComandosApp.InicioItemProximo).Rotulo);
    }

    static bool UsoPrivado(char c) => c >= (char)0xE000 && c <= (char)0xF8FF;

    [Fact]
    public void glifos_sao_caracteres_de_uso_privado_com_nome()
    {
        var campos = typeof(Glifos).GetFields(BindingFlags.Public | BindingFlags.Static).Where(c => c.IsLiteral).ToList();
        Assert.NotEmpty(campos);
        Assert.All(campos, c =>
        {
            var g = (string)c.GetRawConstantValue()!;
            Assert.Single(g);
            Assert.True(UsoPrivado(g[0]), c.Name);
        });
        // Nenhum glifo literal sobrou nas definições (B1): todos vêm de Glifos, escritos como escapes.
        foreach (var arquivo in new[] { "Faixas.cs", "Modulos.cs", "Glifos.cs" })
        {
            var texto = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Ambiente.PastaTestes)!, "src", "Pmvni.App", "Logica", arquivo));
            Assert.DoesNotContain(texto, UsoPrivado);
        }
        Assert.All(Faixas.Disponiveis.SelectMany(m => Faixas.Comandos(Faixas.Para(m))).Where(c => c.Glifo.Length > 0),
            c => Assert.True(UsoPrivado(c.Glifo[0]), c.Id));
    }

    [Fact]
    public void imprimir_na_faixa_de_todo_modulo_com_documento()
    {
        // Pedido de 2026-11-17: botão de imprimir (impressoras e Salvar como PDF) na barra de opções de tudo que mostra documento.
        Assert.Equal(new[] { Modulo.Relatorios, Modulo.Despachos, Modulo.Processos, Modulo.Legislacao, Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco },
            ComandosImpressao.Modulos);
        foreach (var m in ComFaixa())
        {
            var f = Faixas.Para(m);
            var inicio = f.Abas.SingleOrDefault(a => a.Nome == "Página Inicial")?.Comandos.ToList() ?? [];
            var imprimir = inicio.FindIndex(c => c.Id == ComandosApp.ArquivoImprimir);
            if (!ComandosImpressao.Modulos.Contains(m))
            {
                Assert.DoesNotContain(Faixas.Comandos(f), c => c.Id == ComandosApp.ArquivoImprimir);
                continue;
            }
            Assert.True(imprimir >= 0, m.ToString());
            var c = inicio[imprimir];
            Assert.Equal("Imprimir", c.Rotulo);
            Assert.Equal(Glifos.Imprimir, c.Glifo);
            Assert.Equal(TipoComando.Botao, c.Tipo);
            Assert.Equal("Imprimir ou salvar como PDF", c.TextoCompleto);
            // Ao lado do comando que gera ou abre o documento.
            var vizinho = m switch
            {
                Modulo.Relatorios => ComandosApp.ArquivoExportarPdf,
                Modulo.Despachos => ComandosApp.DespachosGerarPdf,
                Modulo.Processos => ComandosApp.ProcessosAbrirArquivo,
                Modulo.Legislacao => ComandosApp.LegislacaoCopiarCitacao,
                _ => ComandosApp.SiteNavegador,
            };
            Assert.Equal(vizinho, inicio[imprimir - 1].Id);
        }
        // Relatórios: também em Arquivo, depois de Exportar PDF.
        var arquivo = Faixas.Para(Modulo.Relatorios).Abas[0].Comandos.Select(c => c.Id).ToList();
        Assert.Equal(arquivo.IndexOf(ComandosApp.ArquivoExportarPdf) + 1, arquivo.IndexOf(ComandosApp.ArquivoImprimir));
        // Imóveis segue com o seu Imprimir (relatório do imóvel).
        Assert.Contains(Faixas.Comandos(Faixas.Para(Modulo.Imoveis)), c => c.Id == ComandosApp.ImoveisImprimir);
        Assert.False(ComandosApp.SoDoRelatorio(ComandosApp.ArquivoImprimir));
        Assert.NotEqual("", DicasComandos.Descricao(ComandosApp.ArquivoImprimir));
    }

    [Fact]
    public void modulo_sem_faixa_definida_e_recusado()
    {
        foreach (var m in Enum.GetValues<Modulo>().Except(ComFaixa()))
            Assert.Throws<ArgumentOutOfRangeException>(() => Faixas.Para(m));
    }

    [Fact]
    public void faixa_da_legislacao()
    {
        var f = Faixas.Para(Modulo.Legislacao);
        Assert.Equal(Modulo.Legislacao, f.Modulo);
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Exibir", "Ajuda" }, f.Abas.Select(a => a.Nome));
        Assert.Equal(1, f.AbaPadrao);
        static (string, string, TipoComando)[] Linhas(AbaFaixa a) => a.Comandos.Select(c => (c.Id, c.Rotulo, c.Tipo)).ToArray();

        Assert.Equal(new[]
        {
            ("legislacao.gerenciar", "Gerenciar anexos", TipoComando.Botao),
            ("arquivo.importar", "Importar análise da IA…", TipoComando.Botao), ("arquivo.abrir", "Abrir", TipoComando.Botao), ("arquivo.recentes", "Recentes", TipoComando.Menu),
        }, Linhas(f.Abas[0]));
        Assert.Equal(new[]
        {
            ("legislacao.copiar", "Copiar", TipoComando.Botao), ("legislacao.copiarCitacao", "Copiar citação", TipoComando.Botao),
            ("arquivo.imprimir", "Imprimir", TipoComando.Botao),
            ("legislacao.paginaAnterior", "Página anterior", TipoComando.Botao), ("legislacao.paginaProxima", "Próxima página", TipoComando.Botao),
            ("legislacao.escopo", "Pesquisar em", TipoComando.Menu),
            ("legislacao.ocorrenciaAnterior", "Ocorrência anterior", TipoComando.Botao), ("legislacao.ocorrenciaProxima", "Próxima ocorrência", TipoComando.Botao),
        }, Linhas(f.Abas[1]));
        var escopo = f.Abas[1].Comandos.Single(c => c.Id == "legislacao.escopo");
        Assert.Equal(new[] { ("legislacao.escopo.lei", "Nesta lei"), ("legislacao.escopo.todas", "Em todas as leis") }, escopo.Itens!.Select(i => (i.Id, i.Rotulo)));
        Assert.Equal(new[]
        {
            ("legislacao.zoomMenos", "Diminuir", TipoComando.Botao), ("legislacao.zoomMais", "Aumentar", TipoComando.Botao),
            ("legislacao.ajustarLargura", "Ajustar largura", TipoComando.Botao), ("legislacao.paginaInteira", "Página inteira", TipoComando.Botao),
            ("legislacao.marcadores", "Marcadores", TipoComando.Alternar), ("legislacao.lista", "Lista de leis", TipoComando.Alternar),
        }, Linhas(f.Abas[2]));
        Assert.Equal(new[] { ComandosApp.AjudaAtalhos, ComandosApp.AjudaSobre }, f.Abas[3].Comandos.Select(c => c.Id));
        // Os comandos do leitor não são do relatório: valem com a Legislação à vista, sem mexer num relatório escondido.
        Assert.All(Faixas.Comandos(f).Where(c => c.Id.StartsWith("legislacao.", StringComparison.Ordinal)), c => Assert.False(ComandosApp.SoDoRelatorio(c.Id)));
    }
}
