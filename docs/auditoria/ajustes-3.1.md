# Auditoria dos Ajustes do Editor 3.1

A comparação usa o CHANGELOG 3.0.0 e o documento de uso da IA de 21/11/2026. As etapas exibidas seguem a ordem e o executor definidos pelo kit instalado. O menu e os controles vêm de PaginaAjustes.xaml. A coluna de motivo identifica o uso na interface ou o arquivo que consome o controle. As opções do modelo local aparecem na segunda tabela.

| Categoria | Item | Decisão | Motivo |
|---|---|---|---|
| Sobre e suporte | Atualizar kit PMVNI | remover | Atualizar todos já aciona o instalador na categoria Pastas e kit. |
| Geral | Imóveis e Acompanhamento ao abrir | novo | São módulos de trabalho que o Editor já consegue abrir diretamente. |
| geral | Ao abrir | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Ao abrir o editor | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Módulo mostrado ao abrir. | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Ao abrir o editor — CaixaAbrirEm | manter | Uso em MainWindow.Ajustes.cs. |
| geral | Lembrar tamanho e posição da janela | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Reabre do jeito que estava; desligado, abre com 1600 x 1000. | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Lembrar tamanho e posição da janela — AlternarJanela | manter | Uso em MainWindow.Ajustes.cs. |
| geral | Arquivos | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Arquivos recentes | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Quantos aparecem em Arquivo › Recentes. | manter | Título, orientação ou estado apresentado na categoria. |
| geral | Quantidade de arquivos recentes — CaixaRecentes | manter | Uso em MainWindow.Ajustes.cs. |
| geral | Limpar a lista de recentes · Limpar lista — BotaoLimparRecentes | manter | Uso em MainWindow.Ajustes.cs. |
| aparencia | Janela | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Tema | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Muda só a janela do editor. As folhas do relatório continuam brancas, como saem no PDF. | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Tema — CaixaTema | manter | Uso em MainWindow.Ajustes.cs. |
| aparencia | Rótulos da faixa | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Automático esconde os rótulos em janelas estreitas (menos de 1000 px). | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Rótulos da faixa — CaixaRotulosFaixa | manter | Uso em MainWindow.Ajustes.cs. |
| aparencia | Avisos de layout | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Onde aparecem "reduzido N %", "além da borda" e blocos maiores que a página. | manter | Título, orientação ou estado apresentado na categoria. |
| aparencia | Avisos de layout — CaixaAvisosLayout | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Tela | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Zoom ao abrir | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Como as folhas aparecem ao abrir um relatório. | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Zoom ao abrir — CaixaZoomInicial | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Lista de seções | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Compacta: uma linha por item, com a situação numa bolinha. Confortável: título em até duas linhas e a situação escrita embaixo. | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Lista de seções — CaixaListaSecoes | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Marcas de revisão | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Mostra nas folhas o que foi alterado (o mesmo que Revisão › Marcas de revisão). | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Marcas de revisão — AlternarMarcas | manter | Uso em MainWindow.Ajustes.cs, MainWindow.Revisao.cs. |
| relatorios | Painéis ao abrir | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Seções e Propriedades como ficaram da última vez; desligado, os dois abrem à vista. | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Lembrar os painéis ao abrir — AlternarPaineis | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Gravação | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Salvamento automático | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | O relatório é gravado sozinho depois deste tempo sem alterações (e também ao trocar de arquivo e ao fechar). | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | Intervalo do salvamento automático — CaixaIntervalo | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Recuperação | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | As alterações ainda não gravadas ficam aqui, para o caso de uma queda. O que ficou guardado há mais tempo que isto sai sozinho. | manter | Título, orientação ou estado apresentado na categoria. |
| relatorios | TextoPastaRecuperacao | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Guardar a recuperação por — CaixaRecuperacao | manter | Uso em MainWindow.Ajustes.cs. |
| relatorios | Abrir a pasta de recuperação · Abrir pasta — BotaoAbrirRecuperacao | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Pastas | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Pasta de processos | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | É a pasta que os módulos Processos e Despachos leem (uma subpasta por ano, e dentro dela uma pasta por processo). O editor só lê essa pasta: nunca move, renomeia nem apaga nada nela. | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | TextoPastaProcessos | manter | Uso em MainWindow.Pastas.cs. |
| pastas | Abrir a pasta de processos no Explorador · Abrir — BotaoAbrirPastaProcessos | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Escolher a pasta de processos · Alterar… — BotaoEscolherPasta | manter | Uso em MainWindow.Pastas.cs. |
| pastas | Usar a pasta de processos padrão · Padrão — BotaoPastaPadrao | manter | Uso em MainWindow.Pastas.cs. |
| pastas | TextoAvisoPasta | manter | Uso em MainWindow.Pastas.cs. |
| pastas | Pasta do kit PMVNI | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | De onde o Novo despacho lê os modelos oficiais (pelo registro TEMPLATES_OFICIAIS.json) e os textos fixos. O editor só lê o kit: nada é copiado nem gravado nele. | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | TextoPastaKit | manter | Uso em MainWindow.NovoDespacho.cs. |
| pastas | Abrir a pasta do kit no Explorador · Abrir — BotaoAbrirPastaKit | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Escolher a pasta do kit PMVNI · Alterar… — BotaoEscolherKit | manter | Uso em MainWindow.NovoDespacho.cs. |
| pastas | Usar a pasta do kit padrão · Padrão — BotaoKitPadrao | manter | Uso em MainWindow.NovoDespacho.cs. |
| pastas | TextoAvisoKitAmbiente | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | TextoModelos | manter | Uso em MainWindow.ModelosDaAnalise.cs, MainWindow.ModelosIa.cs. |
| pastas | Plugins e ferramentas | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Componentes do kit PMVNI | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | O plugin de análise, o servidor de PDF e os motores de PDF instalados neste computador, com a versão de cada um. As atualizações rodam fora do Editor, pelo Windows, e não começam com uma análise em andamento. | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | TextoFonteComponentes | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Componentes do kit PMVNI — ListaComponentesKit | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Verificar atualizações dos plugins e ferramentas · Verificar atualizações — BotaoVerificarComponentes | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Reparar instalação dos plugins e ferramentas · Reparar instalação — BotaoRepararInstalacao | corrigir | Uso em MainWindow.Ajustes.cs. |
| pastas | Atualizar todos os plugins e ferramentas · Atualizar todos — BotaoAtualizarTodosComponentes | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | TextoResultadoComponentes | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | TextoTarefaKit | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Listas ao abrir | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Processos: ordem | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Ordem da lista ao abrir o editor (Exibir › Ordenar muda só até fechar). | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Ordem da lista de processos — CaixaOrdemProcessos | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Despachos: agrupar e ordenar | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Como a lista de despachos abre. | manter | Título, orientação ou estado apresentado na categoria. |
| pastas | Agrupar os despachos — CaixaAgruparDespachos | manter | Uso em MainWindow.Ajustes.cs. |
| pastas | Ordem dos despachos — CaixaOrdemDespachos | manter | Uso em MainWindow.Ajustes.cs. |
| fontes_dados | Base QGIS | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Pasta dos arquivos QGIS | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | A pasta da base municipal (a que tem o Mapa PDM.qgz, as pastas shapes, 2025_10, SHAPE_LOTEAMENTOS e BASE_CADASTRAL_AUXILIAR). O editor só lê esses arquivos: nunca grava, move nem apaga nada nela. | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Pasta dos arquivos QGIS · Nenhuma pasta escolhida — CaixaPastaQgis | manter | Uso em MainWindow.Imoveis.cs. |
| fontes_dados | Selecionar a pasta dos arquivos QGIS · Selecionar pasta… — BotaoEscolherQgis | manter | Uso em MainWindow.Imoveis.cs. |
| fontes_dados | Verificar acesso e arquivos da pasta QGIS · Verificar acesso e arquivos — BotaoVerificarQgis | manter | Uso em MainWindow.Imoveis.cs. |
| fontes_dados | Abrir a pasta QGIS no Explorador · Abrir — BotaoAbrirQgis | manter | Uso em MainWindow.Imoveis.cs. |
| fontes_dados | ProgressoQgis | manter | Uso em MainWindow.Imoveis.cs. |
| fontes_dados | Verificando a pasta QGIS… | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | ResultadoQgis | manter | Uso em MainWindow.Imoveis.cs. |
| fontes_dados | Sites da Prefeitura | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Entre uma vez pelo próprio site (Essencial, GPI, Alfresco): o app guarda o acesso, protegido pelo Windows só para este usuário, e o mantém ativo até você clicar em Sair. Para o caso de o servidor encerrar a sessão por conta própria, guarde também o usuário e a senha: o app entra sozinho quando o site pedir. | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | PainelSites | manter | Uso em MainWindow.Sites.cs. |
| fontes_dados | Acompanhamento de processos | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Planilha de acompanhamento | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | A planilha do Excel no modelo padrão (ACOMPANHAMENTO DE PROCESSOS). O módulo Acompanhamento mostra todas as colunas dela, com as listas e as cores configuradas, e grava nela tudo o que você muda no app. Antes de cada gravação, uma cópia de segurança vai para a pasta de dados do editor. | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Planilha de acompanhamento de processos · Nenhuma planilha escolhida — CaixaPlanilhaAcompanhamento | manter | Uso em MainWindow.Acompanhamento.cs. |
| fontes_dados | Selecionar a planilha de acompanhamento · Selecionar planilha… — BotaoEscolherPlanilha | manter | Uso em MainWindow.Acompanhamento.cs. |
| fontes_dados | Verificar a planilha de acompanhamento · Verificar — BotaoVerificarPlanilha | manter | Uso em MainWindow.Acompanhamento.cs. |
| fontes_dados | Abrir a planilha no Excel · Abrir no Excel — BotaoAbrirPlanilha | manter | Uso em MainWindow.Acompanhamento.cs. |
| fontes_dados | ResultadoPlanilha | manter | Uso em MainWindow.Acompanhamento.cs. |
| fontes_dados | Preencher local e autor sozinho | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Quando a coluna Local ou Profissional está vazia, o editor procura no relatório analisado, na consulta QGIS e nas pranchas do processo e grava o que achar. Nunca troca o que já foi digitado. | manter | Título, orientação ou estado apresentado na categoria. |
| fontes_dados | Preencher local e autor sozinho — AlternarDetectarAcompanhamento | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | LibreOffice | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | LibreOffice | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Gera o PDF dos despachos (sobre uma cópia do DOCX; o Word nunca é usado). Sem ele, sai só o DOCX. | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | TextoSoffice | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Testar o LibreOffice · Testar — BotaoTestarSoffice | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Escolher o programa do LibreOffice · Escolher… — BotaoEscolherSoffice | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Achar o LibreOffice sozinho · Procurar automaticamente — BotaoSofficeAutomatico | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | TextoTesteSoffice | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Depois de gravar | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Abrir o PDF depois de gravar | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Abre o PDF do despacho no programa padrão assim que ele é gravado na pasta do processo. | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Abrir o PDF depois de gravar — AlternarAbrirPdf | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Profissional responsável | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | O nome e os dados que aparecem abaixo da linha de assinatura dos despachos e no fim dos relatórios gerados. Campo vazio mantém o texto do modelo. | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Nome do profissional · ex.: FULANO DE TAL — CaixaProfissionalNome | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Profissão · ex.: ARQUITETO E URBANISTA — CaixaProfissionalProfissao | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Registro profissional (CAU ou CREA) · ex.: CAU/ES A000000-0 — CaixaProfissionalRegistro | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Matrícula · ex.: 123456 — CaixaProfissionalMatricula | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Salvar os dados do profissional · Salvar dados — BotaoSalvarProfissional | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | TextoProfissionalSalvo | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Assinatura digital | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Certificado digital | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Com um certificado escolhido, o PDF de cada despacho sai assinado digitalmente, com a assinatura visível logo acima da linha de assinatura. Sem certificado, o PDF sai como sempre. | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | TextoAssinatura | manter | Uso em MainWindow.Agentes.cs, MainWindow.Assinatura.cs. |
| despachos | Escolher o arquivo do certificado digital · Escolher arquivo… — BotaoEscolherCertificado | manter | Uso em MainWindow.Assinatura.cs. |
| despachos | Usar um certificado do repositório do Windows · Certificado do Windows… — BotaoCertificadoWindows | manter | Uso em MainWindow.Assinatura.cs. |
| despachos | Conferir o certificado digital · Conferir — BotaoConferirCertificado | manter | Uso em MainWindow.Assinatura.cs. |
| despachos | Deixar de assinar os despachos · Remover — BotaoRemoverCertificado | manter | Uso em MainWindow.Assinatura.cs. |
| despachos | TextoConferenciaCertificado | manter | Uso em MainWindow.Assinatura.cs. |
| despachos | Lembrar a senha neste computador | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Desligado, a senha do arquivo é pedida a cada assinatura. Ligado, ela fica guardada cifrada, só para este usuário do Windows. | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | Lembrar a senha do certificado neste computador — AlternarLembrarSenha | manter | Uso em MainWindow.Assinatura.cs. |
| despachos | Perfil do LibreOffice | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | O editor usa um perfil próprio do LibreOffice. Se a conversão para PDF travar sempre, apague o perfil: o próximo PDF demora perto de um minuto, enquanto ele é criado de novo. | manter | Título, orientação ou estado apresentado na categoria. |
| despachos | TextoPerfilSoffice | manter | Uso em MainWindow.Ajustes.cs. |
| despachos | Redefinir o perfil do LibreOffice · Redefinir perfil — BotaoRedefinirPerfil | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | Pesquisa | manter | Título, orientação ou estado apresentado na categoria. |
| legislacao | Pesquisar em | manter | Título, orientação ou estado apresentado na categoria. |
| legislacao | O alvo da pesquisa ao abrir uma lei no leitor. | manter | Título, orientação ou estado apresentado na categoria. |
| legislacao | Pesquisar em — CaixaEscopo | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | SecaoLegislacao | manter | Uso em MainWindow.Legislacao.cs. |
| legislacao | Leis anexadas | manter | Título, orientação ou estado apresentado na categoria. |
| legislacao | Anexe os PDFs das leis que você consulta. O editor guarda uma cópia de cada um na pasta de dados dele e nunca altera o arquivo original. Nada é enviado para fora do computador. | manter | Título, orientação ou estado apresentado na categoria. |
| legislacao | TextoTotalLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | TextoSomenteLeituraLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | Anexar PDFs das leis — BotaoAnexarLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | Anexar PDFs… | manter | Título, orientação ou estado apresentado na categoria. |
| legislacao | ProgressoLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | TextoProgressoLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | BarraProgressoLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | Cancelar a anexação · Cancelar — BotaoCancelarLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | AvisoLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | AvisosAcervoLegislacao | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | ConteudoAnexos | manter | Uso em MainWindow.Ajustes.cs. |
| legislacao | ListaAnexos | manter | Uso em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| ia | Inteligência artificial — TituloAgentes | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Suas IAs | manter | Título, orientação ou estado apresentado na categoria. |
| ia | CartaoChatGpt | manter | Uso em MainWindow.Agentes.cs. |
| ia | CartaoClaude | manter | Uso em MainWindow.Agentes.cs. |
| ia | CartaoGemini | manter | Uso em MainWindow.Agentes.cs. |
| ia | Novo relatório | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Rota da conta Codex — CaixaRotaCodex | manter | Uso em MainWindow.Ajustes.cs. |
| ia | Usar configuração do Codex neste PC | manter | Elemento de ComboBoxItem da categoria. |
| ia | Conectar diretamente à OpenAI | manter | Elemento de ComboBoxItem da categoria. |
| ia | Usar Codex-LB configurado neste PC | manter | Elemento de ComboBoxItem da categoria. |
| ia | Pedido padrão | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Texto que já vem na caixa do Novo relatório (até 2000 caracteres). Vazio: a caixa começa vazia, com a dica de sempre. | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Pedido padrão para a IA — CaixaPedidoPadrao | manter | Uso em MainWindow.Ajustes.cs. |
| ia | Confirmar antes de cancelar uma análise | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Pergunta antes de parar uma análise em andamento. | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Confirmar antes de cancelar uma análise — AlternarConfirmarCancelar | manter | Uso em MainWindow.Ajustes.cs. |
| ia | Avisar ao fechar com análise do ChatGPT em andamento | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Desligado, fechar a janela encerra a análise do ChatGPT sem perguntar (a do Claude continua sozinha, como sempre). | manter | Título, orientação ou estado apresentado na categoria. |
| ia | Avisar ao fechar com análise do ChatGPT em andamento — AlternarConfirmarFechar | manter | Uso em MainWindow.Ajustes.cs. |
| modelos | Personalização local. Os padrões do kit PMVNI são compartilhados; as alterações aqui valem somente para este computador. | corrigir | Elemento de InfoBar da categoria. |
| modelos | ROTAS DE EXECUÇÃO | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | Confira os modelos da análise — AvisoModelosAnalise | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Modelos e níveis de cada etapa por rota — PainelModelosAnalise | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Objeção técnica sobe um nível | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | Depois de uma falha técnica registrada (validador reprovado, objeção da auditoria ou da revisão final), a nova tentativa da etapa sobe um nível, até alto. Cota e problemas do computador nunca sobem. Vale só na rota ChatGPT; nas outras, o kit não muda o nível por objeção. — TextoRegraObjecao | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Objeção técnica sobe um nível — AlternarSobeObjecao | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Cota baixa desce o nível | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | Com esta parte da cota restante (ou menos) na janela mais gasta da conta, o nível desce. | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | Cota restante que desce o nível — CaixaCotaBaixa | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | TextoRegrasDasRotas | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Auditoria cruzada | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | A auditoria por uma sessão independente da IA (o modelo pode ser o mesmo), antes da montagem do relatório. Ligada por padrão nas três rotas; desligada, a análise vai da auditoria técnica direto à montagem. | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | PainelAuditoriaCruzada | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | TextoModelosAnalise | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Publicar a tabela de modelos como padrão do kit · Publicar como padrão do kit — BotaoPublicarModelos | corrigir | Uso em MainWindow.ModelosDaAnalise.cs. |
| modelos | Modelos de IA | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | A IA que responde quando você pede: Rever com a IA (com uma instrução escrita), Ajustar o item, esquadrias, volumetria e a instrução do despacho: o modelo e o nível de raciocínio de cada uma. As listas vêm dos apps ChatGPT e Claude deste computador e, nas APIs, da própria API. As etapas do Novo relatório usam os modelos da rota selecionada. | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | IA da revisão e dos ajustes | corrigir | Título, orientação ou estado apresentado na categoria. |
| modelos | Escolha por mim: o Codex, se estiver neste computador; senão o Claude Code. — TextoFonteIa | corrigir | Uso em MainWindow.ModelosIa.cs. |
| modelos | IA da revisão e dos ajustes — CaixaIaFonte | corrigir | Uso em MainWindow.ModelosIa.cs, MainWindow.xaml.cs. |
| modelos | PainelModelosIa | corrigir | Uso em MainWindow.ModelosIa.cs. |
| modelos | TextoModelosIa | corrigir | Uso em MainWindow.ModelosIa.cs. |
| modelos | AnelModelosIa | corrigir | Uso em MainWindow.ModelosIa.cs. |
| modelos | Atualizar a lista de modelos de IA · Atualizar lista — BotaoAtualizarModelosIa | corrigir | Uso em MainWindow.ModelosDaAnalise.cs, MainWindow.ModelosIa.cs. |
| sobre | Atualizações | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Versão instalada | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoVersaoInstalada | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Novas versões | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Procura versões novas no GitHub do projeto, baixa, confere e troca o executável sem pedir administrador. A versão nova vale ao abrir o editor de novo. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Verificar atualizações · Verificar atualizações — BotaoVerificarAtualizacoes | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Reiniciar o editor na versão nova · Reiniciar agora — BotaoReiniciarAtualizacao | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | TextoEstadoAtualizacao | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Atualizar automaticamente | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Procura sozinho a cada 15 minutos enquanto o editor está aberto. Desligado, só pelo botão acima. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Atualizar automaticamente — AlternarAtualizarSozinho | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | ExpansorOQueMudou | manter | Elemento de Expander da categoria. |
| sobre | TextoOQueMudou | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Suporte | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Contato | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Dúvidas, sugestões e relatos de problemas. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Enviar e-mail ao desenvolvedor — LinkContato | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Relatar um problema | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Copie a versão e o sistema para enviar junto do relato, ou abra a pasta de dados com o registro de erros do editor. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Copiar informações — BotaoCopiarInformacoes | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Abrir a pasta de dados no Explorador · Abrir pasta de dados — BotaoAbrirPastaDados | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | TextoCopiado | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Atalhos de teclado | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | A lista de Ajuda › Atalhos de teclado. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Ver atalhos — BotaoVerAtalhos | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Restaurar os ajustes | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Volta todas as opções ao padrão. Não apaga chaves de API, leis anexadas nem recentes. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Restaurar os ajustes · Restaurar… — BotaoRestaurarAjustes | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Diagnóstico | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Consulte as versões, as pastas e o estado atual do editor para investigar um problema. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | O relatório pode conter nomes de processos e caminhos locais. Revise antes de compartilhar. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Atualizar diagnóstico — BotaoAtualizarDiagnostico | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Copiar o relatório de diagnóstico · Copiar diagnóstico — BotaoCopiarVersoes | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | TextoVersoes | manter | Uso em ComponentesDoKit.cs, MainWindow.Ajustes.cs. |
| sobre | Sobre | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | ImagemApp | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Análise de Projetos – PMVNI | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoSobreVersao | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Editor dos relatórios de análise de projetos da Secretaria Municipal de Obras de Venda Nova do Imigrante: revisão dos relatórios, despachos, processos, acompanhamento da tramitação, legislação, consulta de imóveis na base QGIS e os sistemas da Prefeitura, com análise por Claude e Codex. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Desenvolvedor | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoDesenvolvedor | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Direitos autorais | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoDireitos | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Sistema | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoSistema | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Runtime | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoRuntime | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Pasta de dados | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Ajustes, legislação anexada, recuperação, cópias da planilha e registro de erros. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | TextoPastaDados | manter | Uso em MainWindow.Ajustes.cs. |
| sobre | Tecnologias | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Windows App SDK (WinUI 3), .NET e WebView2 | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Ícones | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Segoe Fluent Icons e Fluent UI System Icons (licença MIT). | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Microsoft | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Componentes | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | PDF.js (Apache-2.0), PdfPig (Apache-2.0), PDFsharp (MIT), NetTopologySuite (BSD) e LibreOffice para o PDF dos despachos. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Licença do PDF.js — BotaoLicencaPdfJs | manter | Uso em MainWindow.Sobre.cs. |
| sobre | Marcas | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Claude e Anthropic, ChatGPT, Codex e OpenAI, Excel, Windows e demais nomes e logotipos pertencem aos respectivos proprietários. O editor não é afiliado a essas empresas. | manter | Título, orientação ou estado apresentado na categoria. |
| sobre | Avisos legais | manter | Título, orientação ou estado apresentado na categoria. |

### Controles montados em tempo de execução

| Categoria | Item | Decisão | Motivo |
|---|---|---|---|
| Modelos da análise | Trilha e Restaurar padrões | corrigir | A restauração confirma a exclusão do ajuste local, incluindo regras de cota e auditoria cruzada. |
| Modelos da análise | Rotas e perfis do kit | novo | Cada cartão seleciona uma coluna de modelos-por-rota.json. |
| Modelos da análise | Modelo e raciocínio de cada etapa | corrigir | As opções vêm da lista do provedor. O controlador recebe a escolha ao iniciar a análise. |
| Modelos da análise | Preparo e produção | manter | O executor declarado no kit determina se os seletores ficam desativados. |
| Modelos da análise | Origem da lista e aplicação imediata | corrigir | A página informa se o catálogo veio do aplicativo, da API ou da tabela do kit. |
| Modelos da análise | Auditoria cruzada por rota | manter | O controlador consulta o estado de cada etapa opcional antes de executar. |
| Modelos da análise | Publicar como padrão do kit | manter | A ação exige confirmação e só aparece no computador mestre. |
| Inteligência artificial | Conta, chave, perfil de acesso, executável, teste e aceite do aviso | manter | CartaoAgente e MainWindow.Agentes apresentam os controles aplicáveis a cada provedor. O cofre guarda as credenciais. |
| Pastas e kit | Versões, situação e atualização por componente | manter | ComponentesDoKit compara a instalação e a fonte, e seleciona o instalador do componente ou do kit. |
| Legislação | Título, esfera, substituir, excluir | manter | A linha de cada anexo altera o catálogo local ou a cópia do PDF. |
| Fontes de dados | Endereços dos sites | manter | A configuração alimenta os módulos de navegação. |
## Configurações gravadas

| Categoria | Item | Decisão | Motivo |
|---|---|---|---|
| Configuração local | modulo | manter | Estado utilizado em FaixaDeOpcoes.xaml.cs, Trilho.xaml.cs. |
| Configuração local | aba_por_modulo | manter | Serialização e leitura de estado local em Ajustes.cs. |
| Configuração local | tema | manter | Estado utilizado em JanelaFigura.cs, JanelaPrancha.cs. |
| Configuração local | intervalo_autosave_s | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.Gravacao.cs. |
| Configuração local | marcas_revisao | manter | Estado utilizado em CoresIcones.cs, Faixas.cs. |
| Configuração local | recentes_max | manter | Serialização e leitura de estado local em Ajustes.cs. |
| Configuração local | recentes | manter | Estado utilizado em CoresIcones.cs, Faixas.cs. |
| Configuração local | pasta_processos | manter | Estado utilizado em MainWindow.Acompanhamento.cs, MainWindow.Ajustes.cs. |
| Configuração local | pasta_kit | manter | Estado utilizado em ComponentesDoKit.cs, MainWindow.Agentes.cs. |
| Configuração local | pasta_qgis | manter | Estado utilizado em MainWindow.Acompanhamento.cs, MainWindow.Imoveis.cs. |
| Configuração local | planilha_acompanhamento | manter | Estado utilizado em MainWindow.Acompanhamento.cs, MainWindow.Sobre.cs. |
| Configuração local | planilha_aba | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | acompanhamento_resumo | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | acompanhamento_larguras | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | acompanhamento_colunas_ordem | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | acompanhamento_colunas_exibidas | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | acompanhamento_colunas_ocultas | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | lista_secoes | manter | Estado utilizado em MainWindow.Ajustes.cs. |
| Configuração local | leis_favoritas | manter | Estado utilizado em MainWindow.ListaLeis.cs. |
| Configuração local | agentes | manter | Estado utilizado em CatalogoModelosIa.cs, SessaoNovoRelatorio.cs. |
| Configuração local | plugins_vistos | manter | Estado utilizado em AcompanhamentoPlugins.cs, MainWindow.Agentes.cs. |
| Configuração local | execucoes_claude | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| Configuração local | execucoes_chatgpt | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| Configuração local | abrir_em | manter | Estado utilizado em CoresIcones.cs, Faixas.cs. |
| Configuração local | lembrar_janela | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.xaml.cs. |
| Configuração local | janela | manter | Estado utilizado em Atalhos.cs, MainWindow.Ajustes.cs. |
| Configuração local | faixa_rotulos | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.xaml.cs. |
| Configuração local | avisos_layout | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.xaml.cs. |
| Configuração local | zoom_inicial | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.xaml.cs. |
| Configuração local | lembrar_paineis | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.xaml.cs. |
| Configuração local | paineis | manter | Estado utilizado em MainWindow.Acompanhamento.cs. |
| Configuração local | recuperacao_dias | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.Gravacao.cs. |
| Configuração local | processos_ordem | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.Pastas.cs. |
| Configuração local | processos_pastas | manter | Serialização e leitura de estado local em Ajustes.cs. |
| Configuração local | despachos_agrupar | manter | Estado utilizado em DicasComandos.cs, Faixas.cs. |
| Configuração local | despachos_ordem | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.Pastas.cs. |
| Configuração local | arquivos_exibicao | manter | Estado utilizado em MainWindow.Pastas.cs. |
| Configuração local | arquivos_agrupar | manter | Estado utilizado em MainWindow.Pastas.cs, MainWindow.xaml.cs. |
| Configuração local | arquivos_ordenar | manter | Estado utilizado em MainWindow.Pastas.cs, MainWindow.xaml.cs. |
| Configuração local | arquivos_decrescente | manter | Estado utilizado em MainWindow.Pastas.cs, MainWindow.xaml.cs. |
| Configuração local | soffice | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoDespacho.cs. |
| Configuração local | despacho_abrir_pdf | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoDespacho.cs. |
| Configuração local | acompanhamento_detectar | manter | Estado utilizado em DicasComandos.cs, Faixas.cs. |
| Configuração local | assinatura_origem | manter | Estado utilizado em TextosAssinatura.cs, MainWindow.Assinatura.cs. |
| Configuração local | assinatura_arquivo | manter | Estado utilizado em TextosAssinatura.cs, MainWindow.Assinatura.cs. |
| Configuração local | assinatura_certificado_sha1 | manter | Estado utilizado em TextosAssinatura.cs, MainWindow.Assinatura.cs. |
| Configuração local | assinatura_lembrar_senha | manter | Estado utilizado em MainWindow.Assinatura.cs. |
| Configuração local | profissional_nome | manter | Estado utilizado em MainWindow.Ajustes.cs. |
| Configuração local | profissional_profissao | manter | Estado utilizado em MainWindow.Ajustes.cs. |
| Configuração local | profissional_registro | manter | Estado utilizado em MainWindow.Ajustes.cs. |
| Configuração local | profissional_matricula | manter | Estado utilizado em MainWindow.Ajustes.cs. |
| Configuração local | legislacao_escopo | manter | Estado utilizado em ComandosLegislacao.cs, DicasComandos.cs. |
| Configuração local | pedido_padrao | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| Configuração local | rota_codex | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| Configuração local | confirmar_cancelar | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| Configuração local | ia_ajuste_modelo | manter | Converte a escolha anterior em modelo por fonte ao carregar ajustes antigos. |
| Configuração local | ia_ajuste_esforco | manter | Converte a escolha anterior em modelo por fonte ao carregar ajustes antigos. |
| Configuração local | ia_revisao_fonte | manter | Estado utilizado em MainWindow.ModelosIa.cs. |
| Configuração local | ia_modelos | manter | Estado utilizado em MainWindow.ModelosIa.cs. |
| Configuração local | confirmar_fechar | manter | Estado utilizado em MainWindow.Ajustes.cs, MainWindow.NovoRelatorio.cs. |
| Configuração local | atualizar_automaticamente | manter | Estado utilizado em MainWindow.Ajustes.cs. |
| Configuração local | modo | manter | Estado utilizado em FluxoNovoRelatorio.cs, MainWindow.Agentes.cs. |
| Configuração local | executavel | manter | Estado utilizado em MainWindow.Agentes.cs. |
| Configuração local | aviso_versao | manter | Estado utilizado em FluxoNovoRelatorio.cs, TextosAgentes.cs. |
| Configuração local | aviso_em | manter | Estado utilizado em SessaoNovoRelatorio.cs, ModelosIa.cs. |

## Sugestões não implementadas

- Manter os controles de ordenação e agrupamento dos arquivos na página do processo, onde a mudança já é aplicada e gravada.
- Uma agenda própria para a atualização do kit duplicaria a tarefa do Windows gerida pelo instalador.

## Posicionamento

O painel de cartões usa empilhamento vertical com 10 px entre cartões. Nas linhas com ações, o texto e o grupo ficam centralizados na vertical. O grupo desce inteiro quando falta largura para a descrição. Se nem o grupo sozinho couber, os controles passam juntos para a orientação vertical, com 8 px de intervalo.

| Categoria | Disposição |
| --- | --- |
| Geral | A quantidade de recentes e Limpar lista compartilham uma linha com a descrição. |
| Aparência | Tema, rótulos e avisos usam cartões de largura inteira com os seletores à direita. |
| Relatórios e revisão | O prazo de recuperação e Abrir pasta ficam juntos ao lado da descrição. |
| Pastas e kit | Cada pasta tem uma linha com Abrir, Alterar… e Padrão. O caminho aparece abaixo, com reticências e dica. As ações gerais do kit acompanham o cabeçalho dos componentes. |
| Fontes de dados | As ações do QGIS e da planilha acompanham o título. Os caminhos têm uma linha própria. |
| Sites da Prefeitura | Abrir para entrar e Sair acompanham o cabeçalho. Usuário, Senha e Guardar login ficam à esquerda. O GPI tem uma linha própria para testar o boletim. |
| Despachos e PDF | As ações do LibreOffice e do certificado acompanham seus títulos. Remover é a última ação do certificado. Os dados profissionais têm campos limitados e Salvar dados acompanha a descrição. |
| Legislação | Anexar PDFs acompanha a descrição da biblioteca. Substituir arquivo e Excluir formam um grupo por lei. |
| Inteligência artificial | A rota Codex e o pedido padrão têm linhas próprias. |
| Modelos da análise | O cabeçalho, a atualização da lista e a publicação usam linhas responsivas. A tabela de etapas mantém sua rolagem horizontal independente. |
| Sobre e suporte | As ações de atualização, relato de problema e diagnóstico ficam agrupadas com a respectiva descrição. |

O Brilliant disponibilizou a documentação e abriu a tela de desenho. A revisão automática bloqueou o envio das camadas porque esta execução não permite aprovações. A implementação usa os recursos de tema e controles WinUI do projeto.
