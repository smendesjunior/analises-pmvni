# Auditoria de interface · Processos e Despachos 3.2.0

Inventário da base anterior às alterações, com o resultado por grupo. As indicações P01–P37 identificam ocorrências auditáveis, contadas uma vez por problema no mesmo grupo ou modelo de controle. Tamanhos em DIP. A referência é `padrao-ui.md`, com os estilos de `Estilos.xaml` e a escala de Ajustes.

## Títulos, campos e ajuda

| Tela / textos da base | Nível e estilo anterior | Conferência / resultado |
|---|---|---|
| Processos · Acervo de processos; Gestão de documentos e histórico | Página `PmvniTituloPaginaStyle`; ajuda `PmvniSecondaryTextStyle` | Textos conformes. P01: ícone vetorial substituído pelo catálogo. |
| Processos · contagem do acervo, espelho da contagem no cabeçalho; filtro «Número ou nome do processo» | Contagem lateral secundária; espelho sem estilo; campo `PmvniCampoPesquisa` | P02: mesmo estilo nos dois lugares, com o espelho fora da leitura duplicada. P03: pesquisa com glifo do catálogo a 16. |
| Processos · Processos (lista), Relatórios, Despachos, Projeto e documentos | Seções `PmvniTituloSecao`; contagens `CaptionTextBlockStyle` | P04: glifo de 16 antes da lista e substituição dos três vetores. Contagens no estilo secundário. |
| Processos · iniciais, título e detalhe de cada processo | Iniciais 13 seminegrito e pincel de sucesso; título seminegrito local; detalhe secundário | P05: selo e título usam estilos existentes. |
| Detalhe · requerente ou nome da pasta, protocolo, caminho e «Escolha um processo na lista» | `TitleTextBlockStyle`, `CaptionTextBlockStyle`, secundário e secundário com 16 local | P06: cabeçalho de painel, selo e ajuda na escala comum. |
| Detalhe · linhas em três colunas e lista auxiliar `ListaDetalhe` | Grupo Caption com peso e cor locais ou BodyStrong; nome corpo padrão; informação secundária | P07: títulos de grupo no mesmo estilo; lista auxiliar invisível fora da tabulação. |
| Miniaturas · título e contagem do grupo, nome do arquivo e «Nenhum arquivo neste processo» | Título 15 local; contagem secundária; nome 12 local; estado vazio secundário | P08: título de cartão e legenda secundária; contagem e vazio conformes. |
| Processos · pasta ausente; leitura parcial ou falha da pasta/processo; avisos de ações; «Nenhum processo combina com o filtro» / «Nenhum processo foi encontrado nesta pasta» | `InfoBar` e `EstadoVazio`, com título Subtitle e frase secundária | Avisos nativos conformes. Estado vazio compartilhado registrado abaixo. |
| Despachos · contagem com escopo, cabeçalhos de grupo, nomes e metadados da lista | Secundário; BodyStrong; corpo padrão; secundário | P09: título de grupo no estilo de painel. Contagem e lista ficam recolhidas na base. |
| Despachos · pasta ausente, avisos de leitura e de ações; Despacho gravado (número de arquivos, pasta, recibo); estado de criação e mensagens de lista vazia | `InfoBar`; `EstadoVazio`; textos dinâmicos em `MainWindow.Pastas.cs` | Avisos nativos conformes. A frase do estado de criação é independente das mensagens da lista auxiliar. |
| Novo despacho · título de página dinâmico e «Defina a origem e os documentos de referência» | Título de página correto; ajuda `PmvniTextoAjuda` | P10: ajuda adota o mesmo estilo secundário das demais telas. |
| Origem · número 1; Origem; Tipo; Processo; Documentos de referência; Relatório base; Pasta da etapa | Selo `PmvniNumeroPasso`; título de cartão; todos os textos de campo/grupo em maiúsculas com `PmvniPainelRotulo` | P11: capitalização de frase; Documentos de referência recebe título de cartão. Selo de etapa mantido. |
| Origem · Como funciona; «O relatório é opcional…»; opções de tipo, processo, relatório base e etapa | Seção com E946 e cor de tema; ajuda secundária; itens nativos de ComboBox | P12: glifo do catálogo e resumo usa o rótulo do seletor. Opções mantidas. |
| Conteúdo · 2 · Conteúdo; Protocolo nº; Requerente; Assunto; Tipo de obra; Área total a construir; obra nova / alteração / ampliação; orientação de tipo e área | Título de cartão; rótulos em maiúsculas; checkboxes nativas; ajuda secundária | P13: capitalização da interface e ícone de cartão com glifo de 16. Campos e orientação mantidos. |
| Conteúdo · Exigências; Item NN · situação · título; Grupo; Texto da exigência; Fundamento; Original do relatório | BodyStrong no grupo e na linha; cabeçalhos nativos dos campos; original secundário | P14: seção com ícone, título de cartão e identificação em capitalização de frase. Valor da situação vem do kit. |
| Prévia · caixa de instrução, instrução de edição das folhas, campos editáveis e dados do documento | Placeholder nativo; ajuda secundária; Hyperlink com nome e dica; tipografia do DOCX | P15: barra da IA usa glifos de catálogo, exemplo simplificado e progresso identificado. |
| Flyout da folha · protocolo, requerente, assunto, destinatário, data, texto do despacho, título de grupo, exigência, fundamento, área e tipo de obra | Header nativo, ou BodyStrong no título de tipo de obra; «Outro (digitar)» em Header; ajuda secundária com Enter e Esc | P16: título do tipo de obra no estilo de cartão e orientação de edição curta. |
| Flyout da exigência · Ajustando…; erros e resultado na caixa | Secundário para progresso; `PmvniTextoErroStyle` para erro | P17: nome e anúncio de progresso e erro; dica e ícone acompanham Ajustar/Cancelar. |
| Conferência · 3 · Conferência ou Gerar PDF · arquivo; fontes, assinatura, número de páginas e «Conferi a página N» | Subtitle; fontes secundárias; número sem estilo; caixas nativas | P18: título de página, ajuda secundária, identificação das imagens e borda de tema ao redor do PDF. |
| Conferência · «O despacho gerado não passou na validação», «Sem PDF»; bloqueios do modelo, seleção de processo, automação e erros da IA | `InfoBar`, títulos nativos; mensagens dinâmicas | Conforme. Mantidos títulos/explicações de validação e severidades. |
| Espera e rodapé · geração/gravação; «Falta preencher:» e itens | Texto de espera sem estilo com anúncio Polite; BodyStrong e pincel crítico local | P19: estilos de ajuda e erro existentes, explicações acessíveis para as ações bloqueadas. |
| Aprovação · título da janela; «PRANCHA ARQUITETÔNICA · Visualização do PDF original»; página N de N, porcentagem e orientações de navegação/posicionamento | Título de janela nativo; BodyStrong e `Symbol.Document`; corpo padrão ou BodyStrong | P20: capitalização, seção e ícone de catálogo; estado, zoom e instruções no estilo secundário. |
| Aprovação · Adicionar observações; lista de observações; Criar nova observação; Referência (opcional) | Título nativo do `ContentDialog`, checkboxes e Header nativos | Conforme. Observações que aparecem sobre a prancha integram a visualização documental. |

## Botões e controles de ação

P = primário, N = padrão nativo, T = tonal. A ordem é a do grupo original, da esquerda para a direita ou de cima para baixo. «Implícito» indica nome derivado do texto do controle.

| Tela / grupo e posição | Texto e estilo da base | Ícone, dica e nome da base | Conferência / resultado |
|---|---|---|---|
| Processos e Despachos · ação do aviso de pasta | Abrir Ajustes, N | Sem ícone; abre a pasta nos Ajustes; implícito | Conforme. |
| Processos · detalhe 1–3 | Classificar → Agrupar → Visualizar, N, espaçamento 6 | Três vetores 16; dicas de ordem, agrupamento e modo; nomes explícitos iguais aos rótulos | P21: catálogo, espaçamento 8 e «Ordenar» como na faixa. Nome acessível original preservado. |
| Miniaturas · cabeçalho de cada grupo | Título dinâmico, N transparente | E76C/E70D 10; dica e nome dinâmicos para expandir/recolher | P22: catálogo e 16; sem mudar a ação de expandir/recolher. |
| Despachos · aviso de gravação 1–3 | Abrir no Word → Abrir PDF → Mostrar no Explorador; N; espaçamento 8 | Sem ícones; dicas de abertura/local e nomes implícitos | P23: altura comum com os demais grupos; três ações disponíveis conforme os arquivos entregues. |
| Novo despacho · cabeçalho | Fechar, só ícone, N | Vetor 16; dica simples e nome «Fechar novo despacho» | P24: Segoe de catálogo, 32 × 32, dica com o estado. |
| Origem · processo 1–2 | Novo processo… → Sem processo, N | Sem ícone; dicas de criação e vínculo; nomes «Criar um processo novo» e «Despacho sem processo vinculado» | P25: «Criar processo…» e «Prosseguir sem processo», na mesma grade e com altura comum. |
| Origem · relatório e pasta, cada ação junto ao seletor | Escolher… / Nova…, N | E8E5/E8F4 a 14; dicas de arquivo/pasta; nomes «Escolher o arquivo do relatório» e «Criar pasta nova» | P26: rótulos explícitos, glifos de catálogo a 16 e altura comum. |
| Origem · ação abaixo dos cartões | Criar automaticamente a partir do relatório, P | Raio vetorial 16; dica de correção automática e nome explícito | P27: glifo de catálogo e destaque fica com o rodapé na edição. |
| Conteúdo · exigências e aprovação | Adicionar exigência, N; caixas incluir item e tipo de obra | E710 a 14 no botão; dica de exigência em branco e nome explícito; caixas com conteúdo textual | P28: «Adicionar exigência…», glifo de catálogo a 16; seleção intacta. |
| Conteúdo · cada exigência, após sua caixa (2–3) | Mover acima → Mover abaixo; `PmvniBarraButtonStyle` | E70E/E70D a 12; dicas e nomes explícitos | P29: catálogo a 16 em 32 × 32; dica explica primeiro/último item quando bloqueado. |
| Prévia · envio de instrução | Apenas ícone, P, 32 × 32 | E724 a 14; «Pedir à IA», Enter, nome «Aplicar a instrução» | P15: glifo de aplicação e tamanho 16; ação principal da barra mantida. |
| Prévia · edição da folha | Links dos campos nas posições do modelo | Sem ícone; dica de edição e nome dinâmico «Editar …» | Conforme. Edição por Enter/Esc no flyout conforme o campo. |
| Prévia · ajuste de uma exigência | Ajustar com IA → Cancelar, N | E945 14 em ambos os estados; dica de ajuste estática; nomes explícitos por estado | P17: dica, ícone e anúncio coerentes com a ação disponível. |
| Conferência · acima da pilha | Marcar todas, N; Conferi a página N em cada página | Sem ícones; dica de marcar e nomes implícitos | P30: rótulo indica o que é marcado. Caixa de confirmação nativa mantida. |
| Espera · abaixo do anel | Cancelar geração, N | Sem ícone; dica de interrupção e nome implícito | Conforme. Botão continua disponível somente quando pode interromper. |
| Rodapé · Cancelar → Voltar e editar → Gerar e conferir / Gravar na pasta do processo ou Gravar só o Word (.docx) | N → N → P, 8 de espaçamento | Gerar com E72A a 12 à direita; demais sem ícone; dicas em todos; nome explícito em Gerar e implícito nos demais | P31: ação principal primeiro, cancelar por último, altura comum e glifo de 16 à esquerda. |
| Aprovação · navegação e zoom, 1–4 com percentual entre 3 e 4 | ← Anterior → Próxima → − → +; N, altura mínima 38 | Setas e sinais tipográficos; sem dica; símbolos lidos como conteúdo | P32: glifos Segoe, dica e nome explícitos; zoom 32 × 32. Motivos de anterior/próxima nas extremidades. |
| Aprovação · revisão 5–6 | Adicionar carimbo ou observações → Aceitar esta prancha, N | Sem ícones e dicas; nomes implícitos | P33: Aceitar prancha em primeiro e com destaque; ferramentas secundárias com dicas. |
| Aprovação · marcação 5–8 | Carimbo aprovado (P) → Adicionar observações → Desfazer → Confirmar prancha (N) | Sem ícones ou dicas, nomes implícitos | P34: confirmar primeiro e com destaque, comando de carimbo no infinitivo, ações identificadas e motivo ao bloquear confirmação. |
| Aprovação · diálogo de observações | Aplicar → Cancelar, botões do `ContentDialog` | Sem ícone, nome pelo texto; aplicar é primário | Conforme. |
| Novo despacho e Aprovação · ações primárias locais | `PmvniBotaoPrimarioStyle` habilita o halo de composição | Efeito do estilo compartilhado | P35: halo desativado nesses controles com a propriedade existente. |

## Menus, flyouts e diálogos

| Superfície | Itens na base, divisões e atalhos | Resultado |
|---|---|---|
| Processos · Classificar | Nome / Data / Tipo → separador → Crescente / Decrescente. Todos `ToggleMenuFlyoutItem`, sem ícone nem atalho. | P21: rótulo Ordenar na abertura. Seleções e separador conferidos. |
| Processos · Agrupar | Etapa ou pasta / Tipo de arquivo / Data → separador → Não agrupar. Toggle sem ícones nem atalhos. | Conforme. |
| Processos · Visualizar | Lista (E8FD) / Miniaturas (ECA5), toggles, sem separador/atalho. | P21: os dois itens passam a usar glifos do catálogo de 16. |
| Processos · contexto em arquivos de lista/miniatura | Abrir relatório / Importar análise e abrir / Abrir arquivo (Abrir), Mostrar no Explorador (Explorador), Copiar caminho (Copiar); sem separador ou tecla exibida. A lista já abre com Enter e mostra o menu com a tecla de contexto ou Shift+F10. | P21: divisão antes de Copiar caminho, tamanho 16 e indicação de Enter. Importar análise usa rótulo e glifo da faixa. Enter continua tratado na lista, com escopo local. |
| Origem · escolhas e caixas | ComboBox de tipo (Correção/Aprovação), processo, relatório (inclusive sem relatório e escolhidos à mão) e etapa. `FileOpenPicker` para relatório/JSON. «Criar processo» com número, ano, requerente, ajuda, Criar e Cancelar; «Nova pasta do despacho» com campo e Criar/Cancelar. | P25/P26: rótulos nos pontos de entrada e título «Criar pasta da etapa». Seletores e diálogos nativos. |
| Conteúdo · diálogos | Adicionar exigência: Texto da exigência, Criar/Cancelar. Emitir a aprovação?: Emitir mesmo assim/Voltar. Substituir o despacho pelo relatório?: confirmação com retorno seguro. Descartar o despacho em edição?: Descartar/Continuar editando (padrão seguro). | P28: confirmação da inclusão passa a Adicionar. Botões padrão dos diálogos preservados. |
| Folha · Flyout nativo por campo | Tipo de obra em checkboxes, caixa Outro; outros campos em TextBox com Enter e Esc; ajuste de exigência com botão, anel, mensagem e erro. | P16/P17. Esc que descarta é específico do flyout de texto; tipo de obra aplica no fechamento, conforme o fluxo existente. |
| Aprovação · emissão de prancha | Seletor de PDFs; Habilitar emissão neste Windows (senha de inscrição), eventual solicitação do certificado pessoal; Petição da prancha (número); seletor da pasta de destino; Substituir a cópia aprovada? (Substituir/Cancelar); Emitir as pranchas aprovadas? (Emitir/Cancelar). Janela de revisão ou marcação; Adicionar observações (Aplicar/Cancelar); Esc cancela o modo de posicionar. | Seletores, diálogos e fluxo nativos. Barra da janela auditada em P20/P32–P34. |

## Glifos e superfícies compartilhadas

| Origem | Inventário e decisão |
|---|---|
| Processos | Vetores de acervo, Ordenar, Agrupar, Visualizar, Relatórios, Despachos e Projeto/documentos a 16 → Pasta, Ordenar, Lista, Painel, Relatorio, Despacho e Documento. Pesquisa E721 a 15 → Pesquisar. Menu E8FD/ECA5, tamanho implícito → Lista/Figuras. Cabeçalhos E76C/E70D a 10 → Avancar/Proximo. Os glifos dessas superfícies estão a 16. `IconeArquivo` e `MiniaturaArquivo` mostram a associação e a prévia dos próprios arquivos do Windows. São conteúdo do acervo. |
| Novo despacho | Vetores Fechar e automático a 16 → Fechar e Relatorio. E8E5, E8F4, E710 e E724 a 14 → Abrir, Adicionar, Adicionar e Avancar. E946 e E945 a 16 → Sobre e Ia. E8A5 a 13 → Relatorio. E72A a 12 → Conferir. E70E/E70D a 12 → Anterior/Proximo. E945 a 14 no flyout → Ia/Fechar. P14 acrescenta Pendente em Exigências. Glifos a 16. |
| Aprovação | `Symbol.Document` de tamanho implícito passa a Documento. Setas e sinais tipográficos passam a Voltar, Avancar, Diminuir e Aumentar. Glifos a 16. |
| Faixa · Processos | Tonal Novo relatório (Adicionar), secundário Novo processo (Adicionar), ambos antes da faixa. Página Inicial, em ordem: Abrir relatório (Abrir), Importar análise (ImportarAnalise), Novo despacho (Editar), Abrir arquivo (Abrir), Imprimir (Imprimir, Ctrl+P), Abrir pasta (Explorador), Atualizar (Atualizar), Ano (Ano). Exibir: Ordenar (Ordenar). Ano: Todos os anos e anos disponíveis. Ordenar: Número, Data, Requerente. Novo despacho: Correção, Correção automática, Aprovação. Itens textuais sem atalho. Rótulos, ícones, dicas/nome gerados pela faixa e estados dinâmicos pertencem à frente SHELL. |
| Faixa · Despachos | Tonal Novo despacho (Editar), com menu igual ao de Processos. Página Inicial: Abrir (Abrir), Abrir PDF (Exportar), Gerar PDF (Exportar), Imprimir (Imprimir, Ctrl+P), Mostrar no Explorador (Explorador), Ir ao processo (IrPara), Atualizar lista (Atualizar), Tipo (Filtro) e Só do processo (Pasta). Exibir: Agrupar (Lista) e Ordenar (Ordenar). Tipo: Todos, Correção, Aprovação. Agrupar: Por processo, Por data. Ordenar: Mais recentes primeiro, Mais antigos primeiro. Itens textuais sem atalho. P36: duplicidade de Exportar e variantes como Atualizar/Atualizar lista ficam na frente SHELL. |
| Faixa · comandos comuns | Arquivo: Importar análise da IA… (Importar, Ctrl+I), Abrir (Abrir, Ctrl+O), Recentes (Recentes, arquivos dos Ajustes). Ajuda: Atalhos de teclado (Atalhos), Sobre (Sobre). Emitir Processo Aprovado (Exportar) vem da faixa de Relatórios. A faixa gera nomes acessíveis a partir da definição e dicas a partir de `DicasComandos`. Menus estáticos dos módulos têm itens em texto e o componente comum monta os itens dinâmicos e marcas de seleção. |
| Estado vazio compartilhado | Processos: Nenhum processo, frase conforme o filtro, Atualizar lista (tonal, Atualizar) e Abrir Ajustes (padrão). Despachos: Criar um despacho, instrução sobre tipos, relatório e lista dos emitidos, Despacho de correção (tonal, Adicionar) e Despacho de aprovação (padrão, Adicionar). Nomes dos botões pelo comando, glifos do host. P37: título Subtitle e ícones de módulo de 48 definidos pelo componente `EstadoVazio`, sob responsabilidade da frente SHELL. |
| Folhas / prancha | Branco, preto, cor de campos pendentes e linhas da folha reproduzem a página do modelo ou a sobreposição documental. A tipografia vem do DOCX e da escala da página. Manter a fidelidade documental. A borda externa migra para recurso de tema. |

## Brilliant e verificação

A [folha editável de hierarquia](https://brilliant.design/playground?seed=cg-d10p0ofnl6d0floew3to) usa exemplos do Novo despacho e do menu do acervo. O catálogo de ícones do Brilliant é Phosphor. Os exemplos identificam os glifos Segoe Fluent pelo nome e código, sem importar ícones ou propriedades do canvas para o WinUI. A folha usa exemplos de cores já presentes nos recursos tonais para marcar os grupos.

O Brilliant armazenou as camadas da folha e informou que serão aplicadas ao abrir o canvas. A renderização ficou sem confirmação nesta sessão.

- Build da solução no worktree com 0 avisos e 0 erros no build final. XAML lido como XML e conferido contra a base para `x:Name` e `AutomationProperties.Name`.
- A primeira execução da suíte de revisão no worktree teve 1.073 aprovações, 2 testes ignorados e 1 falha. O teste `pdfs_do_processo_ficticio_vem_da_pasta_projeto` procura “DESPACHO” no caminho absoluto e encontra a palavra em `processos-despachos` antes de chegar à pasta do processo. Com a mesma base e alterações em um caminho temporário neutro, a suíte completa teve 1.074 aprovações, 2 testes ignorados e 0 falhas. As verificações dos testes foram preservadas.
- A conferência de temas, tabulação e leitura por Narrador foi estática. O WinUI fornece o estado desabilitado e os pincéis de tema. O botão desabilitado tem a dica também no contêiner ao redor para o ponteiro. [Comportamento de ToolTip no WinUI](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tooltip.placementtarget)

## Resumo da frente

- 37 ocorrências registradas, com 35 corrigidas no worktree e 2 encaminhadas à frente SHELL.
- Estilos para promover: ícone Segoe de 16 com variantes de tema para ação e seção, e botão padrão de 36 com a variante compacta de 32 × 32 para ações só com ícone.
- Sugestão para o CHANGELOG: “Padroniza títulos, botões, ícones e menus do acervo de processos e do Novo despacho. Melhora a identificação das ações de IA, da conferência e da aprovação, com dicas para controles desabilitados.”
