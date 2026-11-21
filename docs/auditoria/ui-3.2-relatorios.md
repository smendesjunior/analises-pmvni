# Auditoria de interface 3.2.0 · Relatórios

Escopo: página Relatórios, revisão de um relatório aberto e interfaces construídas pelos arquivos desta frente. O inventário abaixo registra o estado de partida. Os códigos R identificam ocorrências de um mesmo problema no mesmo grupo de controles. A coluna Resultado registra a implementação e os pontos de junção. A conferência encontrou **39 grupos de inconsistências locais**, com **37 corrigidos integralmente e 2 corrigidos em parte**. E1–E6 registram dependências compartilhadas.

**Legenda:** P = primário, T = tonal, N = padrão nativo, B = botão de barra, A = ação da faixa ou da CommandBar. “Nome pelo texto” indica o nome fornecido pelo conteúdo ou rótulo do controle nativo. “—” indica ausência ou elemento que não usa aquele atributo. Medidas em px efetivos do WinUI.

## Página Relatórios

| Área | Elemento e nível | Estilo e glifo atuais | Resultado |
| --- | --- | --- | --- |
| Estado vazio | Nenhum relatório aberto / Peça um relatório novo à IA, importe o dados.json do kit (Arquivo › Importar) ou abra um relatório (.rpmvni). | SubtitleTextBlockStyle / PmvniSecondaryTextStyle. Ícone de módulo, 48 | Corrigido · título usa PmvniTituloPaginaStyle na instância de Relatórios. E1 para o controle compartilhado |
| Estado vazio | Recentes / nome e pasta por arquivo | CaptionTextBlockStyle com peso e cor locais, título e metadado do controle compartilhado | E1 |
| Avisos | Conflito: o arquivo mudou no disco / As folhas não mostram a última alteração / Somente leitura / O diário de recuperação não está sendo gravado / Exportação bloqueada | Título e mensagem nativos do InfoBar · ícones nativos de severidade | Conforme |
| Avisos | Largura / Estouro / Fora da área / Mensagem | Títulos e mensagens variáveis no InfoBar, ou indicador do rodapé · severidade nativa | Conforme · E1 para o indicador |
| Seções | Seções, Excluídos (n), Histórico | Rótulos nativos do SelectorBar | Conforme |
| Seções | Seções do relatório | Seção, PmvniTituloSecao, sem ícone | R01 Corrigido · título de seção com Glifos.Lista 16 e nível de navegação acessível |
| Seções | Navegue pelas folhas e exigências da análise | Ajuda, PmvniSecondaryTextStyle | Conforme |
| Seções | Pesquisar seção ou item / Filtrar por situação | Placeholder e Header nativos · busca E721, 15 | R02 Corrigido · Glifos.Pesquisar 16 com recurso de tema |
| Seções | Título, número, contagem, folha e resultado original alterado | Título com tamanho e peso ligados ao modelo · número 12, contagem 11, folha 10,5 seminegrito, selo 10 seminegrito em cor do tema | R03 Corrigido · PmvniAjusteTitulo em seções, BodyTextBlockStyle em itens, estilos secundário e de destaque nos metadados · E2 para o modelo compartilhado |
| Seções | Ícone de seção / chevron / situação | Modelo, 15 / modelo, 11 / controle EtiquetaSituacao com paleta por situação | R04 Parcial · glifos a 16 e chevron no estilo de barra. Alvo de 24 preservado na coluna de 24. Catálogo dinâmico e situação em E2 · E2 para catálogo e status compartilhados |
| Seções | Itens NN–NN de N | Metadado, PmvniSecondaryTextStyle | Conforme |
| Excluídos | Nenhum item excluído / título por item e número original | Ajuda em PmvniSecondaryTextStyle / item de lista em texto nativo com dica completa | Conforme |
| Histórico | Entradas de alteração | Texto nativo quebrável | Conforme |
| Folhas | Mensagem de espera / progresso | Texto nativo, anel de 24 | R05 Corrigido · PmvniAjusteAjuda e anúncio de estado |
| Propriedades | Propriedades / Item NN / linha selecionada | SubtitleTextBlockStyle, títulos de painel · Item NN em caixa alta | R06 Corrigido · PmvniAjusteTitulo, caixa de frase e cabeçalho acessível |
| Propriedades | Criado na revisão / selecione um item nas folhas ou na lista de seções | PmvniSecondaryTextStyle / PmvniSecondaryTextStyle | Conforme |
| Propriedades | Estado / Esquadrias / Conteúdo / Histórico | PmvniPainelCartaoTitulo em caixa alta · PmvniPainelCartaoIcone, 13, E73E / E80F / E8A5 / E81C | R07 Corrigido · PmvniPainelCartaoTitulo em caixa de frase com nível 3 e glifos de catálogo de 16 |
| Propriedades | No PDF / Situação / Justificativa / Inteligência artificial | PmvniPainelRotulo em caixa alta · nomes de campos existentes | R08 Corrigido · rótulos em caixa de frase com o estilo do painel |
| Propriedades | Situação original / justificativa exigida / aviso de IA / progresso da IA | PmvniSecondaryTextStyle, cor de aviso sobreposta para justificativa e IA | R09 Parcial · PmvniAjusteAjuda e anúncio de atualização. A cautela usa ThemeResource local até haver estilo compartilhado |
| Propriedades | Campos de item e de tabela · valor original, novo, alterado e entradas do histórico | CartoesDePainel.Campo/Apoio/Marca · histórico em texto nativo | Conforme · E2 para capitalização do helper |
| Esquadrias | Janelas / Portas / Outros vãos / Vão total / Mínimo exigido / Resultado | CartoesDePainel.Rotulo, contagem em Marca, valores e resultados do painel | Conforme · E2 para capitalização do helper |
| Esquadrias | Código e tipo / largura e altura / dimensões / área, nota e alerta | TextBox e rótulos, peso local para código e tipo, área 15 seminegrito, nota 12 com cor secundária, E7BA 12 com brush de cautela | R10 Corrigido · áreas e notas em estilos do painel, ícone de atenção do catálogo, caixas com peso nativo |
| Rodapé | 100 % / Folha / de N / situação da composição | Texto nativo / rótulo nativo / nativo / PmvniSecondaryTextStyle | R11 Corrigido · metadados em PmvniSecondaryTextStyle e Folha em PmvniPainelRotulo |

| Área | Botão, ordem e estilo atual | Glifo atual | Dica / nome acessível atual | Resultado |
| --- | --- | --- | --- | --- |
| Estado vazio | 1 Novo relatório (T), 2 Abrir… (N) · Abrir por item recente (N) | Catálogo pelo controle compartilhado | Dicas dos comandos · nome explícito conforme comando | E1 |
| Conflito | 1 Salvar como cópia (P), 2 Recarregar (N) | — / — | Nova cópia / abrir a versão do disco · nome pelo texto | R12 Corrigido · botão secundário com altura mínima de 36, igual à do primário |
| Desatualizado | 1 Recompor (P) | — | Refazer folhas para exportar · nome pelo texto | Conforme |
| Seções | Chevron de cada seção (B compacto, 24×24) | Dinâmico, 11 | Mostra ou esconde os itens / NomeChevron dinâmico | R04 Parcial · glifos a 16 e chevron no estilo de barra. Alvo de 24 preservado na coluna de 24. Catálogo dinâmico e situação em E2 |
| Seções | Situação de cada item (estilo de etiqueta) | Ponto e legenda da situação | Ajuda do controle EtiquetaSituacao / nome do estado | E2 |
| Filtro | Todos, Atende, Não atende, Divergentes, Falta comprovar · toggles nativos estilizados por Pilulas | E73E para Todos ativo, 12 · ponto temático por situação | DescricaoFiltro / “Mostrar [situação], N itens” | R13 Corrigido · estilo de legenda e contador secundário, marca de 12 do catálogo |
| Seções | Mais itens (N) | — | — / nome pelo texto | R14 Corrigido · Ver mais itens, nome explícito e dica com motivo no final da lista |
| Excluídos | Restaurar (N) por linha | — | Traz o item de volta / “Restaurar o item NN” | R15 Corrigido · Restaurar item em ambos os pontos, botão compacto por linha, primário no flyout, título e apoio no padrão e motivo quando bloqueado |
| Propriedades | Fechar as propriedades (B) | E711, 12 | Volta em Exibir › Propriedades / “Fechar as propriedades” | R16 Corrigido · Glifos.Fechar 16, alvo de 32×32 e nome e dica existentes |
| Propriedades | Sai no relatório (CheckBox) | Marca nativa | Desmarcar envia a Excluídos / “No PDF” | Conforme |
| Propriedades › IA | 1 Ajustar o item com a IA / Rever (sem IA) / Rever com a IA (P) | E945, 14 | Descrição varia conforme item ou tabela / sem nome explícito | R17 Corrigido · ações com 36 de altura e 8 de espaço, nome dinâmico da ação principal, Glifos.Ia ou Conferir e motivo de bloqueio |
| Propriedades › IA | 2 Rever com a IA (N) | E945, 14 | Segue justificativa / “Rever a tabela com a IA, seguindo a justificativa” | R17 Corrigido · ações com 36 de altura e 8 de espaço, nome dinâmico da ação principal, Glifos.Ia ou Conferir e motivo de bloqueio |
| Propriedades › IA | 3 Cancelar (N) | — | Interrompe a consulta / “Cancelar a consulta à IA” | R17 Corrigido · ações com 36 de altura e 8 de espaço, nome dinâmico da ação principal, Glifos.Ia ou Conferir e motivo de bloqueio |
| Propriedades › Conteúdo | Restaurar original (N), depois dos campos | E7A7, 14 | Volta ao texto original / sem nome explícito | R18 Corrigido · Glifos.Desfazer 16, nome explícito e motivo quando desativado |
| Propriedades › Campos | Restaurar por campo (PmvniPainelBotaoPequeno) | — | Texto original do campo / nome específico por campo | Conforme |
| Esquadrias | Remover por esquadria incluída (PmvniPainelBotaoPequeno), após os campos | — | Remove a esquadria / nome específico por ordinal | Conforme |
| Esquadrias | Adicionar janela / Adicionar porta (PmvniBotaoTextoDestaqueStyle), uma ação no fim de cada grupo | E710, 12 | Medidas iniciais editáveis / nome explícito | R19 Corrigido · Glifos.Adicionar 16 e reticências no botão que abre o formulário |
| Esquadrias | Adicionar ambiente (P), fim do cartão | E710, 13 | Inclui ambiente na tabela / “Adicionar ambiente” | R19 Corrigido · Glifos.Adicionar 16 e reticências no botão que abre o formulário |
| Rodapé | 1 Diminuir o zoom · 2 Aumentar o zoom · 3 Ajustar largura · 4 Página inteira (B) | E71F, E8A3, ambos 14 · —, — | Dicas dinâmicas incluindo motivo e Ctrl+−, Ctrl++, Ctrl+0 · nome explícito nos ícones | R20 Corrigido · alvos de 32×32 e glifos de zoom de 16, com as dicas e atalhos do rodapé |

## Menus, flyouts e diálogos

| Superfície | Título, itens e controles atuais na ordem original | Ícones, divisões, atalhos e acessibilidade | Resultado |
| --- | --- | --- | --- |
| Menu de item nas folhas | Situação › opções, Editar texto, Excluir item/Restaurar item, Novo item abaixo, Original, Propriedades | Glifos.Situacao, Editar, Excluir/Restaurar, NovoItem, sem ícone no toggle Original, Propriedades · nenhum separador · F2 e Del no texto do atalho | R21 Corrigido · Propriedades e Original / Situação, Editar texto e Novo item abaixo / Excluir item ou Restaurar original. Restaurar item entra no grupo de edição. Glifos de 16, separadores e aceleradores F2/Del derivados de Atalhos |
| Menu de linha nas folhas | Situação › opções, Editar texto, Restaurar original, Original, Propriedades | Glifos.Situacao, Editar, Restaurar, sem ícone no toggle Original, Propriedades · nenhum separador · F2 no texto do atalho | R21 Corrigido · Propriedades e Original / Situação, Editar texto e Novo item abaixo / Excluir item ou Restaurar original. Restaurar item entra no grupo de edição. Glifos de 16, separadores e aceleradores F2/Del derivados de Atalhos |
| Submenus Situação e menu da etiqueta | Atende, Não atende, Falta comprovar e Divergência interna conforme a ordem de Faixas.OrdemSituacoes | Todos toggles com marca nativa, sem glifo adicional/atalho · nome pelo texto | Conforme |
| Item excluído nas folhas | Item novo excluído / Item NN excluído · título do item · Restaurar (N) | BodyStrongTextBlockStyle / nativo / dica de restauração e nome pelo texto · sem glifo ou atalho | R15 Corrigido · Restaurar item em ambos os pontos, botão compacto por linha, primário no flyout, título e apoio no padrão e motivo quando bloqueado |
| Novo ambiente | Novo ambiente · Nome, Área do piso (m²), Divisor, Pavimento · ajuda do cálculo e erros de validação · Adicionar (P) | BodyStrongTextBlockStyle, CartoesDePainel.Campo/Apoio, erro com brush crítico · sem ícone de ação · Enter nos campos · nomes explícitos das caixas | R22 Corrigido · PmvniAjusteTitulo, aviso em PmvniTextoErroStyle e formulário com campos nativos |
| Diagrama › Volumetria | Ajustar a volumetria com a IA · pedido com exemplo, ajuda sobre o diagrama, Aplicar (P) e anel 18 | BodyStrongTextBlockStyle, PmvniSecondaryTextStyle · botão sem ícone ou dica, nome pelo texto · pedido com nome explícito · Enter envia | R23 Corrigido · PmvniAjusteTitulo, dica de Aplicar com Enter e motivo de bloqueio |
| Conferência | Conferência / resultados · Fechar | ContentDialog compartilhado, sem glifo / estilo e sem atalho próprio | E3 |
| Gravação › Fechar com edição recusada | O texto em edição não foi aplicado / Voltar à edição, Fechar sem ele | ContentDialog compartilhado · Voltar à edição é a ação padrão e Esc tem o mesmo efeito | E3 |
| Gravação › Fechar sem gravar | Salvar como… (ou Salvar como cópia), Fechar sem salvar, Cancelar | ContentDialog compartilhado, sem glifos · salvar é a ação padrão | E3 |
| Gravação › Recuperação | Recuperar alterações não gravadas? / Recuperar, Descartar | ContentDialog compartilhado, sem glifos · mensagem com data e quantidade | E3 |
| Gravação › Recarregar | Recarregar do disco? / Recarregar, Cancelar | ContentDialog compartilhado, sem glifos · intenção de descarte sinalizada | E3 |
| Assinatura › Arquivo | Escolher .pfx/.p12 | Seletor do Windows · sem glifos personalizados | Conforme |
| Assinatura › Certificado do Windows | Certificado do Windows / ajuda, lista de certificados / Usar este, Cancelar | ContentDialog compartilhado · lista, seleção e ações nativas · confirma só com seleção | E3 |
| Assinatura › Senha | Título e explicação de TextosAssinatura / Senha, Lembrar neste computador, Assinar e Cancelar · PIN do token pelo dispositivo | ContentDialog compartilhado · PasswordBox e opção nativos · PIN externo | E3 |
| Impressão › fallback | Imprimir pelo programa do Windows? / Imprimir, Cancelar | ContentDialog compartilhado, sem glifos · lista de impressoras do Edge quando o arquivo abre na prévia | E3 |

## Editar figura, recortar prancha e imprimir

| Tela | Título, ajuda ou estado atual | Estilo ou glifo atual | Resultado |
| --- | --- | --- | --- |
| Editar figura | Figura/prancha selecionada e modo atual | PainelPrancha.Titulo (BodyStrongTextBlockStyle) e Symbol.Crop no cabeçalho | R24 Corrigido · títulos em PmvniAjusteTitulo, modo atual como texto secundário · E4 |
| Editar figura | Dados da figura / Marcações / título da marcação | BodyStrongTextBlockStyle · primeiro em caixa alta | R24 Corrigido · títulos em PmvniAjusteTitulo, modo atual como texto secundário |
| Editar figura | Os números se refazem sozinhos… / estado / motivo para não remover | Texto nativo em opacidade 0,75 ou 0,8 | R25 Corrigido · orientações, estado e motivo em PmvniAjusteAjuda |
| Editar figura | Prancha / Título da figura / Fonte / O que é (negrito na legenda) / Solução ou o que foi conferido / Itens ligados a esta marcação | Header nativo · lista de marcações sem nome específico · marcas numeradas e “T” com cores da sobreposição | R26 Corrigido · lista de marcações com nome acessível em adição aos nomes das linhas · E5 |
| Editar figura | Marcações gravadas na imagem / A alteração não foi aplicada / Não foi possível carregar a imagem | Títulos e mensagens nativos do InfoBar | Conforme |
| Recortar prancha | PDF das pranchas / Identificação | CaptionTextBlockStyle e texto em caixa alta | R27 Corrigido · rótulos em PmvniPainelRotulo e títulos de cartão em PmvniAjusteTitulo |
| Recortar prancha | Pré-visualização da prancha / Nova figura / Prévia do recorte | PainelPrancha.Titulo e BodyStrongTextBlockStyle · Symbol.Crop nos dois quadros | R27 Corrigido · rótulos em PmvniPainelRotulo e títulos de cartão em PmvniAjusteTitulo · E4 |
| Recortar prancha | Nenhum PDF carregado / Selecione o PDF das pranchas… | SubtitleTextBlockStyle / nativo, centralizados · Symbol.Document | R28 Corrigido · PmvniTituloPaginaStyle, PmvniAjusteAjuda e Glifos.Documento 16 |
| Recortar prancha | Arraste sobre a página… / Escolha a página… / Trabalhando… / página N de M / zoom | Texto nativo, estado com opacidade · Trabalhando.Quadro compartilhado | R29 Corrigido · ajuda e metadados em estilos compartilhados, seletor com nome explícito |
| Recortar prancha | Escolha o PDF das pranchas / Prancha / Título da figura / O recorte não foi inserido | Placeholder e Headers · InfoBar nativo | R29 Corrigido · ajuda e metadados em estilos compartilhados, seletor com nome explícito |
| Impressão | Abrindo [título]… / nome do arquivo / motivos de falha | Estado da barra e diálogo de impressão nativo do Edge | R30 Corrigido · estado secundário, altura uniforme e dicas de Imprimir… e Fechar, com Ctrl+P e motivo enquanto indisponível |

| Tela e grupo | Ordem, ação e estilo atual | Ícone atual | Dicas, nomes e atalhos atuais | Resultado |
| --- | --- | --- | --- | --- |
| Figura › origem | Inserir região da prancha (A) | Symbol.Add | Dica de recorte · nome pelo rótulo | R31 Corrigido · nomes explícitos, dicas de bloqueio de edição e histórico, zoom com rótulos e glifos comuns, separação visual preservada. Glifos sem entrada em E4 |
| Figura › marcação | Selecionar, Marcar região, Seta, Texto (toggles A) | E8B0, E7A8, E72A, E8D2 | Dica por ferramenta e nome explícito · Esc volta a Selecionar | R31 Corrigido · nomes explícitos, dicas de bloqueio de edição e histórico, zoom com rótulos e glifos comuns, separação visual preservada. Glifos sem entrada em E4 |
| Figura › histórico | Desfazer, Refazer (A) | Symbol.Undo, Symbol.Redo | Dicas e Ctrl+Z/Ctrl+Y globais da janela · nome pelo rótulo | R31 Corrigido · nomes explícitos, dicas de bloqueio de edição e histórico, zoom com rótulos e glifos comuns, separação visual preservada. Glifos sem entrada em E4 |
| Figura › zoom | Diminuir, indicador, Aumentar, Ajustar (A) | Symbol.ZoomOut, Symbol.ZoomIn, E9A6 | Dicas de zoom · só ícones em Diminuir/Aumentar, sem nome explícito | R31 Corrigido · nomes explícitos, dicas de bloqueio de edição e histórico, zoom com rótulos e glifos comuns, separação visual preservada. Glifos sem entrada em E4 |
| Figura › fim da barra | Concluir (P) | — | Dica de fechamento, nome pelo texto | Conforme |
| Figura › dados | Remover marcação (N) | — | Del, motivo dinâmico também visível, nome pelo texto | Conforme |
| Prancha › PDF | Escolher arquivo… (N) e Escolher arquivo PDF (P) no estado vazio | — | Dica no primeiro · nome pelo texto em ambos | R32 Corrigido · Escolher arquivo… e dica nos dois pontos |
| Prancha › navegação | Página anterior, Próxima página (A) | Symbol.Back/Forward | Dicas, nomes explícitos · sem atalho próprio | R33 Corrigido · glifos de catálogo de 16, rótulos de zoom comuns e motivo nos limites da navegação |
| Prancha › zoom | Diminuir, indicador, Aumentar, Página inteira (A) | Symbol.ZoomOut/ZoomIn, Glifos.PaginaInteira | Dicas · nomes explícitos nos dois primeiros | R33 Corrigido · glifos de catálogo de 16, rótulos de zoom comuns e motivo nos limites da navegação |
| Prancha › menu de excesso | Selecionar página, Limpar recorte (A) | Symbol.SelectAll, Symbol.Clear | Sem dicas e sem nomes explícitos | R34 Corrigido · dicas, nomes explícitos e glifos Segoe Fluent de 16 |
| Prancha › rodapé | Inserir no relatório (P), Cancelar (N) | — / — | Dica em ambos · nome pelo texto · sem dica de bloqueio no primeiro | R35 Corrigido · primário e Cancelar com altura de 36, dica de indisponibilidade no recorte |
| Impressão › barra | Imprimir (P), Fechar (N) | — / — | Ctrl+P na dica e na janela · Fechar sem dica · nomes pelo texto | R30 Corrigido · estado secundário, altura uniforme e dicas de Imprimir… e Fechar, com Ctrl+P e motivo enquanto indisponível |

A CommandBar da figura já separava origem, ferramentas, histórico e zoom, com excesso nativo. A da prancha já separava navegação e zoom, com Selecionar página antes de Limpar recorte no menu de excesso.

## Leitura padronizada

| Região | Elementos atuais | Hierarquia, ícones e demais detalhes | Resultado |
| --- | --- | --- | --- |
| Folha › cabeçalho | Órgão e Leitura padronizada da legislação | Verdana, tamanhos e cores próprios, faixa tricolor fixa | R36 Corrigido · estilos WinUI do Editor, tema herdado, paleta temática e avisos com Glifos.Avisos 16. Ordem, colunas e recuos da folha preservados |
| Folha › selos | Esfera, Leitura padronizada do texto do PDF, Preparando as imagens do PDF…, Sem as imagens do PDF | Selos com brushes fixos, tipografia própria · aviso sem imagens tem dica/nome | R36 Corrigido · estilos WinUI do Editor, tema herdado, paleta temática e avisos com Glifos.Avisos 16. Ordem, colunas e recuos da folha preservados |
| Folha › texto | Epígrafe, Ementa, Título, Capítulo, Seção e Subseção | Verdana, peso e tamanhos locais · gradiente no título · faixas e bordas de cores fixas | R36 Corrigido · estilos WinUI do Editor, tema herdado, paleta temática e avisos com Glifos.Avisos 16. Ordem, colunas e recuos da folha preservados |
| Folha › artigo e dispositivos | Art. N, resumo, caput, parágrafo, inciso, alínea | Selo fixo, resumo 13,5, corpo 15/14,5, Verdana e cores fixas · resumo automático em itálico, dicas nos dois estados | R36 Corrigido · estilos WinUI do Editor, tema herdado, paleta temática e avisos com Glifos.Avisos 16. Ordem, colunas e recuos da folha preservados |
| Folha › notas e figuras | Rótulo da imagem, aviso de página inteira/falha, legenda acessível da imagem | “!” em círculo, tipografia e cores fixas · dica com página do PDF | R36 Corrigido · estilos WinUI do Editor, tema herdado, paleta temática e avisos com Glifos.Avisos 16. Ordem, colunas e recuos da folha preservados |
| Tópicos da lista | Cada artigo, título, capítulo ou seção (N sem borda) | Sem ícone · rótulo/resumo e nome acessível · tamanhos e peso locais | R37 Corrigido · estilos de cartão/rótulo/ajuda e dica de navegação |
| Ações do artigo | Escrever o resumo (B, só ícone) | E70F, 13, azul fixo · dica de edição e nome explícito com o número do artigo | R38 Corrigido · Glifos.Editar 16 e alvo de 32×32 com estilo da barra |
| Flyout do resumo | Resumo do Art. N · nota explicativa · Salvar (P) → Voltar ao automático (N) | Header nativo · nota em 12 e opacidade 0,8 · sem glifos nos botões, ambos com dica e nome pelo texto. Enter salva · ação secundária desabilitada para resumo automático | R39 Corrigido · PmvniAjusteAjuda, alturas uniformes, motivo para resumo já automático |
| Menu de texto da folha | Copiar / Selecionar tudo | MenuTexto compartilhado, ambos com ícone (Glifos.Copiar e E8B3), sem separador, Ctrl+C/Ctrl+A | E6 |

## Dependências e pontos de junção

| Código | Item | Resultado e motivo |
| --- | --- | --- |
| E1 | EstadoVazio, IndicadorAvisos e AreaDeAvisos | Controles compartilhados fora da lista de arquivos. O estado vazio tem ação tonal, ação padrão e recentes com dica. O indicador trabalha com avisos nativos e abre os detalhes de largura, estouro e conteúdo fora da área. |
| E2 | Cores de situação, títulos dinâmicos da lista, glifos de chevron e rótulos gerados pelo helper | ItemNavegacao, EtiquetaSituacao, Pilulas e CartoesDePainel ficam fora da frente. A situação também é identificada pelo texto · a indicação de filtro não depende só da cor. |
| E3 | ContentDialog e interfaces do sistema | Inspecionados nos pontos de chamada e no apresentador compartilhado Perguntas.cs · manter os botões e a semântica de cancelamento. Seletores de arquivo, PIN do token e diálogo do Edge têm corpo e foco controlados pelo sistema. |
| E4 | PainelPrancha.Quadro / glifos sem nome atual | O helper compartilhado recebe Symbol.Crop, já renderizado pela fonte de sistema. Glifos de clique (E8B0), marcar região (E7E6), texto (E8D2), recortar (E7A8), refazer (E7A6), seleção total (E8B3) e limpar (E894) têm referência Segoe Fluent e ficam candidatos ao catálogo em SHELL. A ferramenta Seta usa a seta E72A, compartilhada com Avançar e IrPara no catálogo atual. |
| E5 | Conteúdo da figura e do relatório | Folhas A4, imagens raster e sobreposições de desenho seguem o relatório. Marcadores da imagem carregam a mesma identidade visual da figura exportada. O azul do contorno de recorte é sobreposto ao PDF branco para marcar a geometria no visor. O painel de alertas e as ferramentas ao redor usam o tema da interface. |
| E6 | MenuTexto, navegação global, faixa principal e alternância PDF original / Leitura padronizada | Controles e rotas compartilhados. A hierarquia da faixa principal depende de SHELL. O seletor do leitor é definido em PaginaLegislacao.xaml. A inicialização de leitura aplica o tema herdado somente ao visor padronizado. |

## Brilliant

Folha de referência para Seções, Propriedades, conflito de gravação, zoom e menu de item. Os estilos, controles e nomes de catálogo foram conferidos em Estilos.xaml, Glifos.cs, PaginaAjustes.xaml e na implementação da frente. O Blueprint exige valores literais para representar as amostras de cor · o aplicativo usa recursos de tema. Os ícones da biblioteca Phosphor foram descartados, pois o Editor exige Segoe Fluent Icons. Nenhum controle do desenho entra no aplicativo.

**Entrega do desenho.** O Brilliant recebeu e armazenou as camadas da folha final, e a renderização ficou pendente da abertura da tela na conversa. O rascunho anterior usava Copiar como exemplo no menu de item e reticências em Salvar como cópia. A folha final usa apenas os comandos desse menu e o rótulo exato da cópia automática. [Folha de hierarquia no Brilliant](https://brilliant.design/playground?seed=cg-exmrovwtfwy02uni97jr)

Os glifos Segoe e a possibilidade de restringir os atalhos ao item em foco foram conferidos na documentação do WinUI. [Segoe Fluent Icons](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font) · [Aceleradores de teclado](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-accelerators)

## Mensagens e superfícies auxiliares

| Origem | Títulos emitidos por InfoBar e estado | Resultado |
| --- | --- | --- |
| Revisão | A alteração não foi aplicada · A IA não respondeu · Ajustar com a IA · As folhas não foram recompostas · Comando indisponível · Consulta à IA cancelada · Este campo não pode ser editado nas folhas · Este texto não pode ser editado · Linha recalculada · Nada pendente · Não foi possível aplicar a alteração · Não foi possível recompor · Novo item · Rever · Rever com a IA · Situação não recalculada | Título e corpo nativos do InfoBar, com detalhes vindos de validação, resultado ou exceção |
| Propriedades | Esquadrias · Novo ambiente | InfoBar nativo e mensagens de cálculo e validação |
| Figuras | Editar figura · Inserir região da prancha · Não foi possível abrir a prancha · Não foi possível abrir o editor da figura · Não foi possível carregar a imagem · O recorte não foi inserido | InfoBar nativo, com orientação e motivos de falha |
| Diagrama | Volumetria | InfoBar com resultado da consulta |
| Gravação | Cópia salva · Fechado sem salvar · Não foi possível salvar · Não foi possível salvar a cópia · Recarregado do disco · Recarregar não foi feito | InfoBar com detalhes da gravação. A falha de salvamento automático também chega pelo aviso nativo do Windows |
| Assinatura | Arquivo não aceito · Assinatura digital · texto de conferência em Ajustes | InfoBar e resumo em Ajustes. O apresentador da página de Ajustes fica fora desta frente |
| Impressão | A lei não foi impressa · Comando indisponível · Enviado para a impressora · O arquivo não foi impresso | InfoBar nativo |
| Leitura padronizada | Não foi possível guardar o resumo | InfoBar nativo |
| SessaoEdicao e HostFiguras | Estado da edição e contrato do host de figuras | Sem tela própria |
| Área de avisos | Avisos visíveis, botão +N e lista de mensagens excedentes · no rodapé, contador de avisos e Ir à folha N | Controle compartilhado E1. Resumo, erro, severidade, dica e nome acessível conferidos no código consumidor |

## Estilo para promover na junção

- Texto de ajuda com cautela em Propriedades. A base já é `PmvniAjusteAjuda` e a cor usa `SystemFillColorCautionBrush`. Um estilo compartilhado de ajuda de cautela concentraria esses setters.
- Ação compacta só com ícone para painéis e rodapés. A base é `PmvniBarraButtonStyle` e os alvos de 32×32 são ajustados nas instâncias.

## Verificação e junção

- `dotnet build` da solução passou com zero erros e dois alertas xUnit1031 em testes preexistentes.
- `dotnet test tests/Pmvni.Revisao.Testes` passou com 1.074 aprovados, zero falhas e dois ignorados. O teste do painel confere os mesmos títulos e controles, com a capitalização exigida nesta versão.
- A disposição foi conferida pelo XAML e pela montagem dos controles em C#. O fluxo em janela, a navegação por Narrador e a aparência nos três temas não foram executados de forma interativa.
- Sugestões para o CHANGELOG: **Relatórios:** padronizada a hierarquia dos painéis de revisão, com menus de contexto agrupados e dicas de indisponibilidade. **Figuras e leitura:** atualizadas as ações de figura, recorte e impressão e aplicada a hierarquia de tema à leitura padronizada.

- As 34 pastas geradas pelo build e pelos testes foram enviadas à Lixeira após a conferência de caminhos, processos, serviços, atalhos e início automático.
