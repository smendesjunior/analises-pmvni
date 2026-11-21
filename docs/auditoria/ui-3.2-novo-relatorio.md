# Auditoria da interface 3.2 — Novo relatório

A conferência usa o XAML, a composição visual em C#, as chamadas de diálogos, `Glifos.cs`, `Atalhos.cs` e os estilos dos Ajustes. Linhas com variantes dinâmicas identificam o campo ou o elemento que as apresenta. Tamanhos em px efetivos. As colunas “Antes” registram o estado anterior às alterações.

## Títulos, campos e ajuda

| Local e conteúdo | Nível | Antes e conformidade | Resultado |
|---|---|---|---|
| Novo relatório | Página | `PmvniTituloPaginaStyle`, conforme | Conforme. |
| Selecione o processo, adicione os anexos e escolha como analisar | Ajuda | `PmvniTextoAjuda`, fora da escala comum | T01 **Corrigido.** Ajuda usa `PmvniSecondaryTextStyle`. |
| 4 etapas | Metadado | Selo verde com texto nativo, repete a estrutura do formulário | T02 **Corrigido.** Selo redundante removido. |
| Processo, Anexos, Pedido para a IA, Analisar | Seções em ordem | `PmvniTituloSecao` com selos numerados, sem glifos | T03 **Corrigido.** Selos numéricos substituídos por `Pasta`, `Anexar`, `Editar` e `Ia`, todos a 16 px nas posições existentes. A orientação de destino agora se refere ao processo pelo nome da seção. |
| Número ou requerente, sugestões por número/ano/requerente, Escolha o processo, pedido com exemplo | Campos e lista | AutoSuggestBox, ComboBox e TextBox com nomes acessíveis, opções nativas | Conforme. |
| Resultado do filtro e caminho do processo | Ajuda e metadado | `PmvniSecondaryTextStyle`, anúncio polido no resultado do filtro | Conforme. |
| Arraste arquivos para cá / Soltar para anexar | Título da área de soltura | `BodyStrongTextBlockStyle` | T04 **Corrigido.** `PmvniAjusteTitulo` no título da área de soltura. |
| Qualquer tipo de arquivo… | Orientação de anexos | `PmvniSecondaryTextStyle` | Conforme. |
| Recusas de anexos | Mensagem de erro | Cor crítica de tema aplicada diretamente, anúncio assertivo | T05 **Corrigido.** `PmvniTextoErroStyle` com alinhamento à esquerda e anúncio assertivo. |
| Escolha o processo no passo 1… | Ajuda do destino | `PmvniSecondaryTextStyle` | T03 **Corrigido.** Selos numéricos substituídos por `Pasta`, `Anexar`, `Editar` e `Ia`, todos a 16 px nas posições existentes. A orientação de destino agora se refere ao processo pelo nome da seção. |
| Nome do compactado e quantidade de arquivos | Título do grupo de anexos | Peso seminegrito avulso | T06 **Corrigido.** `PmvniAjusteTitulo` para o grupo do compactado. |
| Nome do arquivo, tamanho/pasta, destino e prévia | Valores de linha | Nome/caminho em texto nativo, metadado em `PmvniSecondaryTextStyle`, ComboBox identificado | Conforme. |
| Escolha a ferramenta que fará a análise | Ajuda recolhida | `PmvniSecondaryTextStyle` | Conforme. |
| ChatGPT, Claude, Gemini | Título de cartão | `PmvniTituloSecao` nas três rotas | T07 **Corrigido.** `PmvniAjusteTitulo` nos três cartões. |
| Pelo Codex / Claude Code / Antigravity, rota e versão | Metadado | 12 px e opacidade 0,85 avulsos | T08 **Corrigido.** `PmvniSecondaryTextStyle` para os metadados das rotas. |
| Motivos de indisponibilidade por rota, perfil Gemini, orientação ao Claude | Ajuda | `PmvniSecondaryTextStyle` | Conforme. |
| Indicador de partida da análise | Progresso | ProgressRing acionava o brilho automático do estilo implícito | T23 **Corrigido.** O indicador usa a animação nativa do WinUI. |
| Monitor de análise | Página | `PmvniTituloPaginaStyle` com tamanho 20 sobrescrito | T09 **Corrigido.** `PmvniTituloPaginaStyle` a 24 px. |
| Execução da IA, atividades e descobertas em tempo real, provedor/processo/estado e progresso de itens | Ajuda e metadados do monitor | `PmvniSecondaryTextStyle` no subtítulo e resumo | Conforme. |
| Número do processo, Análise em andamento/com problema/concluída/parada | Contexto e título de cartão | `BodyStrongTextBlockStyle`, título de estado sobrescrito para 17 px | T10 **Corrigido.** `PmvniPainelRotulo` para o processo e `PmvniAjusteTitulo` para o estado. |
| Etapa atual, atividades registradas, contagem de etapas e percentual geral | Metadados | 12 px e cor secundária em alguns elementos | T11 **Corrigido.** `PmvniSecondaryTextStyle` para a etapa atual, as contagens e o percentual. |
| Decorrido, estimativa de término e totais Atende / Não atende / Falta comprovar / Informações divergentes | Rótulos e números | `RotuloTotal`, `NumeroTotal` e `TextoRelogio` locais | T12 **Corrigido.** `PmvniPainelRotulo` para os relógios e rótulos dos totais, `PmvniAjusteTitulo` para os números. |
| Etapas da análise | Seção | `BodyStrongTextBlockStyle`, sem ícone | T13 **Corrigido.** `PmvniTituloSecao` com `Glifos.Lista` a 16 px. |
| Nome, situação e descrição de cada etapa, percentual quando informado | Título e ajuda | Tamanho/peso/cor avulsos, nome acessível composto | T14 **Corrigido.** Títulos em `PmvniAjusteTitulo`, descrições e estados em estilos compartilhados. Estados têm texto e símbolo. |
| Atividades da análise, Principais descobertas, resumo da próxima etapa, Consumo, Detalhes da execução, Última mensagem do ChatGPT | Títulos de cartão e painel | `BodyStrongTextBlockStyle`, tamanhos 12 a 16 avulsos | T15 **Corrigido.** `PmvniAjusteTitulo` em todos os cabeçalhos de cartão e painel. |
| Acompanhe o trabalho…, Preliminares até a auditoria…, A IA ainda não relatou descobertas, próxima etapa/erro, Por etapa…, Registro de atividades e última mensagem… | Ajuda de cartões | `PmvniSecondaryTextStyle` | Conforme. |
| Ao vivo | Selo de estado | Texto em 12 px avulso com ponto temático de 8 px | T16 **Corrigido.** `PmvniSecondaryTextStyle` no texto do selo. O ponto temático indica atividade em curso. |
| Verificada / Preliminar e texto da descoberta | Selo e corpo | 11 px no selo, corpo nativo | T17 **Corrigido.** `CaptionTextBlockStyle` nos selos e ícones semânticos `Situacao`/`Avisos` a 16 px. |
| Etapa, executor, tokens, cache, tempo, cota e valores por etapa | Rótulos, selo e valores | 11 px, cor do cabeçalho e dos valores aplicadas em C# | T18 **Corrigido.** `PmvniPainelRotulo` no cabeçalho, `CaptionTextBlockStyle` nos valores e no selo do executor. |
| Rótulo do registro, eventos, última mensagem, título/detalhe/horário de cada atividade | Corpo, metadado e título | Peso, tamanhos e cor avulsos. Linha de atividade tem nome composto | T19 **Corrigido.** Corpo e metadados em estilos nativos/compartilhados. `PmvniAjusteTitulo` para títulos da linha e `PmvniTextoErroStyle` nos erros. |
| Avisos de pausa, cancelamento, cota, indisponibilidade, importação, descarte e relatório pronto | Título/mensagem de estado | `InfoBar.Title`/`Message` e severidade nativa | Conforme. |
| Cartão de IA — serviço, nome da conta e e-mail | Título, identidade e metadado | 15/20/13 px, peso e cor avulsos | T20 **Corrigido.** `PmvniAjusteTitulo` no serviço e no nome da conta. `PmvniAjusteAjuda` no e-mail. |
| Cartão de IA — Assinatura, Programa, Situação, plugins/rotas, Chave de API, Aviso antes de analisar | Títulos de linhas | `TituloLinha` local, 14 px | T21 **Corrigido.** `PmvniAjusteTitulo` nas linhas. |
| Cartão de IA — nota da conta, programa, situação, descrição dos plugins e da chave, aviso, perfil e matriz Gemini | Ajuda | `PmvniAjusteAjuda`, ComboBox com `Header` para o perfil | Conforme. |
| Cartão de IA — assinatura, mensagem do programa, avisos do teste e versão nova dos plugins | Metadados e avisos | Tamanho e cores temáticas avulsos | T22 **Corrigido.** `PmvniAjusteAjuda` nos metadados e avisos. O selo e o texto do estado continuam indicando a situação. |
| Cartão de IA — login / modo API / conta Google e caixa de chave de API | Campos | RadioButtons e PasswordBox nativos, rótulos e nome da chave atribuídos em código | Conforme. |

## Botões e controles acionáveis

Dicas descritivas herdadas pelo nome acessível do texto simples são indicadas como “implícito”. “Padrão” é o botão nativo.

| Área e ordem | Texto anterior | Estilo, ícone, dica e nome acessível antes | Resultado |
|---|---|---|---|
| Resultado, 1–2 | Mostrar registro, Importar relatório | Padrão/primário, sem ícones, `Dicas.Descricao`, implícitos | B01 **Corrigido.** “Importar relatório” primeiro, primário, sem brilho. “Abrir registro” depois, padrão, `AbrirPasta` a 16 px e nome explícito. Ambos a 36 px. |
| Retomada, 1–2 | Retomar análise, Descartar análise | Primário/padrão, sem ícones, `Dicas.Descricao`, implícitos | B02 **Corrigido.** Retomar mantém o destaque sem brilho. Altura mínima de 36 px nos dois. Descartar fica por último. |
| Cabeçalho, 1–2 | Ver a análise em andamento, Voltar ao relatório | Padrão, sem ícones, `Dicas.Descricao`, implícitos | B03 **Corrigido.** “Ver andamento” mantém a dica descritiva e ganha rótulo mais curto. |
| Processo, depois dos campos | Criar processo… | Padrão, sem ícone, `Dicas.Descricao`, implícito | Conforme. |
| Anexos, 1–2 | Anexar arquivos…, Cancelar abertura | Padrão, sem ícones, `Dicas.Descricao`, implícitos | Conforme. |
| Linha de anexo, última coluna | Remover da lista | Só ícone Excluir 14 px, tamanho pelo conteúdo, dica com descrição, “Remover {arquivo} da lista” | B04 **Corrigido.** `Excluir` a 16 px em botão 32×32, com a dica e o nome por arquivo. |
| Analisar, um por cartão | Analisar com o ChatGPT / Claude / Gemini | Tonal, cadeados Path 14 px, dica e ajuda em XAML reescritas em código, nomes derivados do conteúdo | B05 **Corrigido.** As três rotas mantêm peso tonal, sem brilho, com `Ia` a 16 px, nomes explícitos e dica única. O motivo de indisponibilidade aparece no cartão, na dica e na ajuda acessível. |
| Monitor, 1–4 | Registro, Nova análise, Pausar análise, Cancelar análise | Tonais compactos, espaçamento 6, E838/E710/E769/E711 a 14 px, `Dicas.Descricao`, implícitos | B06 **Corrigido.** Padrão nativo com altura mínima de 36 px e intervalo horizontal de 8 px. “Abrir registro” / “Preparar nova análise” / “Pausar análise” / “Cancelar análise”, com nomes explícitos e ícones a 16 px. Cancelar por último. |
| Trilho de etapas, em ordem do kit | Marcador de etapa | Padrão, só ícone a 15 px, botão 24×24, flyout, sem dica, nome “{etapa} · {situação}” | B07 **Corrigido.** Alvo 32×32, selo interno a 12 px, dica de abertura e nome existente. |
| Detalhes do monitor | Abrir evidências por PDF e página | Padrão, sem ícone, `Dicas.Descricao`, implícito | Conforme. |
| Aviso do módulo, ação única por estado | Importar relatório / Abrir relatório | Padrão, sem ícone, `Dicas.Aplicar`, nomes implícitos | B08 **Corrigido.** Altura mínima de 36 px e nomes explícitos nos dois avisos. |
| Cartão de IA, cabeçalho | Entrar / Sair | HyperlinkButton com fonte avulsa, sem ícone, dica e nome dinâmicos explícitos | B09 **Corrigido.** Tamanho padrão do HyperlinkButton e dicas existentes por ação e por conta. |
| Cartão de IA, identidade | Link da conta do provedor | HyperlinkButton com fonte avulsa, sem ícone, dica e nome explícitos com indicação de navegador | B09 **Corrigido.** Tamanho padrão do HyperlinkButton e dicas existentes por ação e por conta. |
| Cartão de IA, Programa 1–2 | Escolher programa…, Usar o padrão | `BotaoPequeno` local, sem ícones, `Dicas.Descricao`, nomes explícitos, sem motivo da indisponibilidade do padrão | B10 **Corrigido.** `PmvniPainelBotaoPequeno` e intervalo de 8 px. “Alterar…” / “Usar padrão”. Dica explica quando o padrão já está selecionado. |
| Cartão de IA, Situação | Testar | `BotaoPequeno` local, sem ícone, `Dicas.Descricao`, nome explícito, sem motivo durante o teste | B11 **Corrigido.** `PmvniPainelBotaoPequeno`. Dica indica teste em andamento. |
| Cartão de IA, Chave 1–2 | Guardar chave, Remover chave | `BotaoPequeno` local, sem ícones, `Dicas.Descricao`, nomes explícitos, sem motivo quando não há chave | B12 **Corrigido.** `PmvniPainelBotaoPequeno` e intervalo de 8 px. Dica informa quando falta chave. Remover por último. |
| Cartão de IA, Aviso | Rever aviso | `BotaoPequeno` local, sem ícone, `Dicas.Descricao`, nome explícito | B13 **Corrigido.** `PmvniPainelBotaoPequeno` no botão de rever aviso. |
| Avisos | Fechar e +N avisos | Fechar nativo do InfoBar, HyperlinkButton de `AreaDeAvisos` expande/recolhe | L01 **Referência.** Controle compartilhado. A ação “+N avisos” tem nome dinâmico. O InfoBar usa o botão nativo do InfoBar. |
| Expansor | Detalhes da execução | Indicador nativo de expandir/recolher, título mais descrição no cabeçalho | B14 **Corrigido.** Nome acessível explícito. Cabeçalho e indicador de expansão continuam no Expander. |
| Barra de título | Estado da análise e acesso ao monitor ou resultado | Botão compartilhado, nome dinâmico definido em `MainWindow.NovoRelatorio.cs` | L02 **Referência.** Superfície compartilhada fora dos arquivos da frente. A pílula tem dica e nome por estado. Recentes usa somente texto nas entradas. |

## Menus, listas de escolha e diálogos

| Superfície | Itens, ícones, separadores e atalhos | Resultado |
|---|---|---|
| Filtro e lista de processos | Sugestões e processos pelo nome/número, indicação de execução na lista. AutoSuggestBox/ComboBox nativos sem ícones, separadores nem aceleradores próprios. | Conforme. |
| Destino de anexos | Opções de `FluxoNovoRelatorio.OpcoesDestino` no ComboBox, sem ícones, separadores nem aceleradores próprios. | Conforme. |
| Perfil da chave do Gemini | Perfis de API no ComboBox, sem ícones, separadores nem aceleradores próprios. | Conforme. |
| Etapa do monitor | Flyout de texto “{etapa} · {situação}”, sem itens de menu, separadores ou aceleradores. | B07 **Corrigido.** Alvo 32×32, selo interno a 12 px, dica de abertura e nome existente. |
| Histórico em Arquivo › Recentes | Entradas por identificação da execução seguidas dos relatórios recentes sem duplicata, e “Nenhum item” desabilitado se a lista estiver vazia. O comando usa `Glifos.Recentes`. O menu usa texto, dicas por entrada, sem ícones, separadores ou aceleradores próprios. | L02 **Referência.** Superfície compartilhada fora dos arquivos da frente. A pílula tem dica e nome por estado. Recentes usa somente texto nas entradas. |
| Escolha de pendência | “Retomar análise”, texto de instrução, lista de execuções, “Abrir” e “Cancelar”. Common ContentDialog, sem ícones/atalhos próprios. | D03 **Corrigido.** Texto da escolha no histórico e comandos de retomada usam “Retomar”. Foram preservados ordem, opções e resultado dos diálogos. |
| Retomada após pausa / escolha no histórico | “Retomar a análise?”, motivo quando houver, provedor original primeiro e alternativas restantes, “Agora não”, “Continuar”, “Descartar análise” e “Cancelar”. ContentDialog nativo. | D03 **Corrigido.** Texto da escolha no histórico e comandos de retomada usam “Retomar”. Foram preservados ordem, opções e resultado dos diálogos. L03 **Referência.** Controles compartilhados. Títulos, botões, foco e atalhos são do ContentDialog ou do seletor nativo conforme o caso. |
| Criar processo | Número, Ano e Requerente, instrução de preenchimento, “Criar” e “Cancelar”. ContentDialog nativo. | L03 **Referência.** Controles compartilhados. Títulos, botões, foco e atalhos são do ContentDialog ou do seletor nativo conforme o caso. |
| Anexar arquivos / escolher executável | Seletores nativos do Windows. Itens e atalhos do sistema. | Conforme. |
| Confirmação de privacidade e revisão do aviso | “Analisar com o {provedor}”, explicação do envio, “Concordar e analisar” / “Concordo” e “Cancelar”. ContentDialog nativo. | D01 **Corrigido.** “Concordar” ao rever o aviso. “Concordar e analisar” na confirmação que inicia a análise. |
| Modo de acesso do Gemini | “Trocar o modo desta análise?”, “Usar o modo selecionado” e “Cancelar”. `TextosAgentes` fornece o texto da confirmação de processo fictício. | Conforme. |
| Decisão durante a análise | “Decisão na análise de {processo}”, pergunta e explicação da pausa, entrada livre ou lista, “Responder” e “Cancelar”. | D02 **Corrigido.** A instrução para voltar à decisão usa o rótulo “Retomar análise” nas duas variantes. |
| Peças ambíguas | “Qual versão da peça vale…?”, justificativa, opções lado a lado, miniaturas, abrir PDF, “Usar esta versão” e “Cancelar”. | D02 **Corrigido.** A instrução para voltar à decisão usa o rótulo “Retomar análise” nas duas variantes. L03 **Referência.** Controles compartilhados. Títulos, botões, foco e atalhos são do ContentDialog ou do seletor nativo conforme o caso. |
| Cancelar análise | Título por provedor, explicação, “Cancelar análise” e “Voltar”. Confirmação nativa com foco seguro. | Conforme. |
| Fechar durante análise | “Pausar a análise e fechar?”, “Pausar e fechar” e “Continuar”. Confirmação nativa. | Conforme. |
| Sair da conta / remover chave | Títulos e explicações de `TextosAgentes`, “Sair” ou “Remover” e “Cancelar”, com foco seguro. | Conforme. |

A lista de atalhos em `Atalhos.cs` não atribui atalhos aos comandos específicos de análise, retomada, registro ou histórico. Os seletores e diálogos mantêm a navegação nativa por teclado.

## Ícones

| Local | Antes | Resultado |
|---|---|---|
| Cabeçalhos Processo / Anexos / Pedido para a IA / Analisar | Selos 1–4 sem glifos | T03 **Corrigido.** Selos numéricos substituídos por `Pasta`, `Anexar`, `Editar` e `Ia`, todos a 16 px nas posições existentes. A orientação de destino agora se refere ao processo pelo nome da seção. |
| Soltura de anexos | Nuvem em Path, 16 px | I01 **Corrigido.** `Glifos.Anexar` a 16 px. |
| Rotas no formulário | SVGs OpenAI, Claude e Gemini a 22 px sobre branco fixo em caixas de 32 px | I02 **Corrigido.** `Glifos.Ia` a 16 px com cor de tema, nas mesmas colunas dos cartões. |
| Botões de rota | Cadeados em Path a 14 px | B05 **Corrigido.** As três rotas mantêm peso tonal, sem brilho, com `Ia` a 16 px, nomes explícitos e dica única. O motivo de indisponibilidade aparece no cartão, na dica e na ajuda acessível. |
| Remover anexo | `Glifos.Excluir`, 14 px | B04 **Corrigido.** `Excluir` a 16 px em botão 32×32, com a dica e o nome por arquivo. |
| Cabeçalho e estado do monitor | E99A e E9F5/E7BA/E73E, 18 px, cores de tema | I03 **Corrigido.** `Ia` no título e `Ia` / `Situacao` / `Avisos` no estado, a 16 px. |
| Registro, nova análise, pausa e cancelamento no monitor | E838/E710/E769/E711, 14 px | B06 **Corrigido.** Padrão nativo com altura mínima de 36 px e intervalo horizontal de 8 px. “Abrir registro” / “Preparar nova análise” / “Pausar análise” / “Cancelar análise”, com nomes explícitos e ícones a 16 px. Cancelar por último. |
| Relógios | E916/E787, 13 px | I04 **Corrigido.** E916 do relógio e `Ano` para a estimativa, a 12 px. |
| Etapas | Pendente: E8F1/E9F5/E721/E8A5/E8AB/E930. Feita: E73E. Atual: E768. Erro: E7BA. Círculo, 15 px. Resumo usa EA3A e os glifos de estado, 14 px, ou anel de 16 px. | T14 **Corrigido.** Títulos em `PmvniAjusteTitulo`, descrições e estados em estilos compartilhados. Estados têm texto e símbolo. B07 **Corrigido.** Alvo 32×32, selo interno a 12 px, dica de abertura e nome existente. |
| Atividades / descobertas / próxima etapa / consumo / detalhes | E8FD/E1D3/E72A/E9D9/E713, 18–20 px | I05 **Corrigido.** `Lista`, `Conferir`, `Avancar`, `Grafico` e `Propriedades`, a 16 px. |
| Descobertas | E930/E946, 16 px, cor de sucesso/atenção de tema | T17 **Corrigido.** `CaptionTextBlockStyle` nos selos e ícones semânticos `Situacao`/`Avisos` a 16 px. |
| Linha de atividade | Glifo do evento vem de `Atividades.cs` a 16 px. Códigos EA39, E975, E8BD, E9D5, E8A5, E8B7, E81E, E82D, E707, E8F1, E7B3, E721, E74E, E8C7, E90F e E768. Estado de linha E8BB/E73E a 11 px ou anel a 20 px. | I06 **Corrigido.** Selos da linha em `Avisos` e `Situacao` a 12 px. Os glifos da ação vêm de `Atividades.cs`. L04 **Pendente.** Fora do escopo. A correspondência de glifos por evento vem de `Logica/Atividades.cs`, que também é compilado nos testes. |
| Conta nos cartões de IA | Marcas em Path 24 px. Claude e Gemini usam cores fixas. PersonPicture nativo para identidade. | I07 **Corrigido.** `Glifos.Ia` temático a 16 px nos três slots, com os nomes XAML existentes. |
| Linhas de Ajustes de IA | E8C7 assinatura, E943 programa, E9D9 situação, EA86 plugins, E192 chave, E946 aviso, todos a 16 px | I08 **Corrigido.** `Documento`, `Engrenagem`, `Situacao`, `Ia`, E192 e `Sobre` a 16 px. E192 fica como glifo de chave até haver nome no catálogo. |

## Síntese para a junção

- **49 ocorrências encontradas, 48 corrigidas.** A contagem agrupa o mesmo problema quando ele se repete entre variantes. A correspondência de ícones do registro de atividade permanece no arquivo compartilhado (L04).
- Um estilo compartilhado de texto de atenção permitiria destacar avisos do diagnóstico dos agentes. Esta frente usa `PmvniAjusteAjuda`, mantendo texto e selos. O catálogo pode receber nomes para os glifos Segoe já usados de pausar (E769), cancelar (E711), relógio (E916) e chave (E192) na frente SHELL.
- **Folha de hierarquia** [Editor PMVNI 3.2 · Novo relatório](https://brilliant.design/playground?seed=cg-gla5cxfjzcp0gnutemfq&utm_source=codex&utm_medium=app&utm_campaign=design). O Brilliant armazenou a escala de títulos, os botões, o recorte do formulário e a amostra de Recentes e de glifos. A renderização ficou pendente da abertura do canvas pelo chat. Phosphor foi descartado por divergir de Segoe Fluent Icons. Os nomes e códigos de glifo da folha vieram do catálogo. Cores literais serviram apenas à representação da folha. A implementação usa estilos e pincéis de tema.
- O pincel transparente usado para receber a dica do grupo com botão desabilitado foi conferido nos [recursos oficiais do WinUI](https://github.com/microsoft/microsoft-ui-xaml/blob/main/controls/dev/CommonStyles/Common_themeresources_any.xaml).

## Validação

A saída de build foi redirecionada para uma pasta temporária e o NuGet usou o cache local. As primeiras tentativas encontraram bloqueios de rede na restauração e de gravação nas subpastas de `bin` e `obj` no worktree. A suíte localizou o corpus e as fontes de teste com cópias dos arquivos do worktree na pasta temporária. A releitura do kit local usou `PMVNI_KIT_RAIZ`.

| Verificação | Resultado |
|---|---|
| `dotnet build` da solução, com `--artifacts-path` | Build completo com zero erros. Na primeira compilação, dois avisos xUnit1031 em testes existentes. A compilação incremental final terminou com zero erros e avisos. |
| `dotnet build src\Pmvni.App` após o ajuste do indicador de partida | Zero erros e zero avisos. |
| `dotnet test tests\Pmvni.Revisao.Testes` | 1.074 aprovados, dois ignorados pelo requisito de LibreOffice e zero falhas. A execução final usou Python local e permissões para os testes de junções e subprocessos. |
| `dotnet test tests\Pmvni.Agentes.Testes --filter FullyQualifiedName~HistoricoAnalisesTestes` | Um aprovado. |
| Conferência de integração | XAML válido e compilado. Comparação com o HEAD confirmou a preservação dos `x:Name`, nomes acessíveis declarados e posições de grade dos elementos nomeados. `git diff --check` passou. |

Os temas e o Narrador foram conferidos pelos recursos e propriedades do XAML. A validação não incluiu uma sessão interativa nos três temas.

## Linhas sugeridas para o CHANGELOG

- Padronizada a hierarquia visual do Novo relatório, do monitor e dos cartões de ChatGPT, Claude e Gemini, com glifos Fluent, nomes acessíveis e dicas de indisponibilidade.
- Unificados os rótulos de retomada no histórico e nas decisões da análise.

