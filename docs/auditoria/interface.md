# Auditoria de interface — Editor de Relatórios PMVNI

Data: 17/11/2026 · versão avaliada: 0.3.0 (branch `integracao-2026-11-15`, compilação Debug) · método: plugin UI Design Review (`ui-review`), sem alterar código.

## Para que serve e para quem

- **O que é:** app desktop do Windows (WinUI 3 com WebView2 para a folha; o pedido falava em WPF, mas o código é WinUI 3: `Microsoft.UI.Xaml`, `TitleBar`, `MicaBackdrop`).
- **Quem usa:** analista da Secretaria de Obras de Venda Nova do Imigrante, várias horas por dia, num notebook ou num monitor de escritório.
- **O que precisa fazer sem falhar:** abrir o processo, revisar e gravar o relatório técnico e manter a planilha de acompanhamento em dia, rápido, com leitura clara e sem perder nada.

## Como foi visto

Rodei o próprio modo de captura do app (`--captura`, aberto pelo `explorer.exe`, fora do pacote) com dados fictícios: a planilha de amostra dos testes, o `tests/corpus/longo.json` e um `.zip` de teste com 4 arquivos. A folha em HTML (WebView2) não sai nessa captura, então abri o app com o relatório de exemplo e fotografei a janela.

| Situação | Tamanho útil (DIP) | Equivale a | Tema | Visto |
|---|---|---|---|---|
| A | 1585 × 923 | monitor 1920 px a 125% | escuro | Acompanhamento, painel, editor, Novo relatório (.zip), Ajustes, Processos |
| B | 1093 × 614 | notebook 1366 × 768 a 125% (ou 1640 × 922 a 150%) | claro | as mesmas telas |
| C | 1707 × 990 (janela maximizada, tela 2560 × 1600 a 150%) | monitor 1920 px a ~110% | escuro | editor com a folha HTML |
| D | 1093 × 614 | 1366 × 768 a 125% | claro | editor com a folha HTML |

As capturas desta auditoria ficaram em `tests/saida/captura` (claro), `tests/saida/captura-escuro` (escuro) e `tests/saida/captura/editor-*.png`. Essa pasta é ignorada pelo git. A captura da lista de Processos mostra nomes reais da pasta de processos deste computador, então não deve ir para `capturas/` nem para o repositório.

**O que não foi visto, só lido no código (marcado com «código» nos achados):** 1366 px a 100% (ficaria entre A e B), 1920 px a 100% (a tela daqui não chega a essa largura em DIP), escala de 150% num monitor de 1920 px, o estado "planilha aberta no Excel", o carregamento lento, a folha com itens excluídos e o menu "…" de cada linha do Acompanhamento.

O UXKIN (exemplos reais) não está conectado. As comparações usam produtos conhecidos: Excel, Smartsheet e Airtable para tabelas densas; Word, Google Docs e Acrobat para editores de documento; e as Configurações do Windows 11 para a tela de Ajustes.

## O que já funciona bem

- O editor tem uma linguagem clara de estado: filtros com contagem ("Não atende 15", "Falta comprovar 15"), etiquetas coloridas em cada item, "Salvo 03:36" sempre à vista e o aviso "2 avisos de layout" na barra de status.
- O Novo relatório em 4 passos numerados é fácil de seguir, e o aviso do compactado explica para onde vai cada arquivo. Ajustes segue o padrão das Configurações do Windows 11 e mostra "Alterações salvas automaticamente". O rodapé do Acompanhamento ("19 processos • Última atualização … • lida às 03:36") dá confiança sobre a origem dos dados.

---

## Precisa corrigir (atrapalha a tarefa principal ou arrisca perder trabalho)

### I-1. Acompanhamento: em 1366 × 768 a tabela some
- **Onde:** `Paginas/PaginaAcompanhamento.xaml`, linhas 8–16 (título, resumo e ações em linhas `Auto`; a tabela fica só com o resto).
- **Visto (B):** título + cartões em 2 linhas + barra de ações ocupam toda a altura. Sobra uma faixa de 4 px onde deveria estar a tabela: **nenhum processo aparece**. Em A cabem só 7 de 19 linhas.
- **Por que importa:** a tabela é o motivo da tela. Num notebook comum o analista não consegue trabalhar.
- **Sugestão:** deixar o resumo recolhível ("Resumo ▾", lembrado nos Ajustes) e fazer ele virar automaticamente uma faixa de uma linha (status: número · número …) abaixo de ~800 DIP de altura, como os KPIs do Smartsheet e do Airtable. Dar à tabela altura mínima (ex.: 6 linhas) e deixar a página rolar quando não couber.
- **Tamanho:** mudança média.

### I-2. Editor: em 1366 × 768 a folha cai para 25% e não dá para ler
- **Onde:** `MainWindow.xaml` e `Paginas/PaginaRelatorios.xaml` (lista de seções com ~640 DIP fixos e Propriedades com ~460 DIP).
- **Visto (D):** a folha fica com ~340 DIP de largura, o zoom vai para **25%** e "Ajustar largura" ainda deixa rolagem horizontal. Com nada selecionado, o painel Propriedades fica aberto e vazio ("Selecione um item…") ocupando ~40% da janela.
- **Por que importa:** o editor é a tela mais usada, e ler a folha é o trabalho.
- **Sugestão:** (a) Propriedades só abre quando há item selecionado, ou vira um painel sobreposto abaixo de ~1400 DIP (como o painel de comentários do Word); (b) lista de seções com largura ajustável e recolhível (como o Painel de Navegação do Word), com padrão de ~320 DIP; (c) "Ajustar largura" não pode deixar rolagem horizontal.
- **Tamanho:** mudança média.

### I-3. Trilho: "Acompanhamento" aparece cortado e Ajustes some em telas baixas
- **Onde:** `Controles/Trilho.xaml` e `Trilho.xaml.cs`.
- **Visto (A, B, C, D):** o rótulo aparece como "ompanhamer" ou ":ompanhamen" nos dois temas e em qualquer largura. Em B o trilho não rola: "Essencial" fica cortado e **Ajustes, GPI e Alfresco somem**.
- **Por que importa:** é a navegação principal. Um rótulo cortado parece defeito, e um módulo que some não pode ser aberto.
- **Sugestão:** rótulo curto "Acompanh." ou "Planilha" (ou duas linhas com quebra), e um `ScrollViewer` vertical no grupo de módulos, com Ajustes sempre fixo no rodapé. Quando faltar altura, os módulos de sites (Essencial, GPI, Alfresco) podem ir para um botão "Mais".
- **Tamanho:** rápido.

### I-4. Aviso "planilha aberta no Excel" pode ser fechado e a alteração se perde
- **Onde:** `MainWindow.Acompanhamento.cs:612-617` (aviso com "Tentar de novo") e `:125` (avisos de erro são fechados sozinhos em outra ação); `PaginaAcompanhamento.xaml:84` (`IsClosable="True"`). «código»
- **Problema:** as mudanças pendentes ficam só dentro do botão "Tentar de novo". Se o aviso for fechado (X, ou outra ação que limpa os erros), a edição some sem pergunta, e nada na tela indica que algo ficou para trás.
- **Por que importa:** o pedido é "nenhum erro ao gravar". Esse é o caso de erro mais comum (o Excel aberto na mesma planilha).
- **Sugestão:** guardar as alterações pendentes numa fila visível ("1 alteração não gravada · Tentar de novo"), num aviso que não fecha sozinho, e tentar de novo automaticamente quando o arquivo for liberado. É o padrão do Office com "Upload pendente".
- **Tamanho:** mudança média. Aparece também em bugs.md (B-4).

### I-5. Acompanhamento: o painel lateral esconde Status e Aprovação
- **Onde:** `PaginaAcompanhamento.xaml:89-149`.
- **Visto (A):** ao abrir o painel, as colunas Atualização, Status e Aprovação saem da tela. Sem o painel, "Aprovação" já aparece cortada ("Aprov"). A barra de rolagem horizontal fica no fim da lista e não é vista.
- **Por que importa:** o status é o dado que o analista mais procura, e vai parar na última coluna.
- **Sugestão:** fixar Nº + Processo à esquerda (congelar painéis, como no Excel), trazer Status logo depois do Processo e permitir esconder colunas vazias (Recebido e Prioridade estão "—" e "Baixa" em todas as linhas da amostra). Abaixo de ~1400 DIP, o painel pode se sobrepor à tabela em vez de encolhê-la.
- **Tamanho:** mudança média.

### I-6. Barra de título corta comandos em 1366 px
- **Onde:** `MainWindow.xaml:19+` (TitleBar).
- **Visto (B, D):** "Próximo" vira "Próxim", e a pesquisa vira "Pes" (no editor). O grupo da direita é cortado sem reticências nem menu de excesso.
- **Sugestão:** abaixo de ~1300 DIP, esconder o texto de Desfazer/Refazer/Anterior/Próximo (só ícone, com dica) e deixar a pesquisa encolher até um botão de lupa.
- **Tamanho:** rápido.

## Deveria corrigir (incoerente ou parece inacabado)

### I-7. Comandos repetidos e com nomes diferentes no Acompanhamento
- **Visto (A):** "Novo processo" aparece duas vezes (faixa e página). "Importar planilha" (faixa) e "Importar Excel" (página) são a mesma coisa, assim como "Abrir no Excel" (faixa) e "Exportar › Abrir a planilha no Excel" (página).
- **Sugestão:** escolher um lugar. Ações da tabela na barra da página e ações da planilha na faixa, com um só nome para cada ação ("Importar planilha…").
- **Tamanho:** rápido.

### I-8. Barra de título e faixa mostram coisas que não valem para a tela
- **Visto (A, B):** em Acompanhamento e Ajustes aparecem Desfazer/Refazer, Anterior/Próximo e "Pesquisar no relatório". Em Ajustes, a faixa mostra "Arquivo: Importar dados.json, Abrir, Recentes". Em Processos, a pesquisa diz "Pesquisar processos", mas o Acompanhamento tem uma segunda busca própria.
- **Por que importa:** confunde o que a pesquisa vai procurar e o que "Desfazer" desfaz (desfaz a planilha? não).
- **Sugestão:** esconder ou desativar por módulo (Ajustes sem faixa de arquivo; no Acompanhamento, a pesquisa da barra de título vira a busca da tabela, e a caixa da página sai).
- **Tamanho:** rápido.

### I-9. Cor de destaque muda entre telas e temas
- **Visto:** no editor (claro) o botão primário é verde-claro, mas o chip "Todos 60", o selo dos passos e a marca do trilho são marrons. No escuro, os mesmos elementos ficam amarelo-mostarda. No Acompanhamento, "Novo processo" é marrom (claro) ou mostarda (escuro). Ajustes usa marrom.
- **Por que importa:** o primário não é reconhecível. Mostarda e marrom lembram aviso, e o analista vê "atenção" onde não há.
- **Sugestão:** uma cor de destaque só (o verde institucional já usado em "Novo relatório"), com as cores por módulo restritas ao ícone do trilho.
- **Tamanho:** rápido (tokens em `Estilos.xaml`/`CoresTonais.xaml`).

### I-10. Pizza sem legenda e com cores parecidas
- **Visto (A, B):** a pizza não tem rótulos nem legenda. Os tons pastel (azul-acinzentado, verde-acinzentado, oliva) ficam muito próximos, e a ligação com os cartões é só a fina linha colorida embaixo de cada um. "Arquivado 0" ocupa um cartão inteiro.
- **Sugestão:** trocar a pizza por uma barra horizontal empilhada com rótulos, ou mantê-la com valores ao passar o mouse e destaque ao clicar no cartão. Colocar uma bolinha de cor no título de cada cartão, esconder os status zerados (ou deixá-los esmaecidos) e dar aos cartões aparência de botão (passar o mouse, foco), já que eles filtram.
- **Tamanho:** rápido.

### I-11. Novo relatório: destino cortado e mensagem repetida em cada anexo
- **Visto (A):** o destino aparece cortado ("Projeto — primeira apresentaçã"). "Escolha o processo." se repete em cada linha de anexo. "Remover da lista" é um botão grande repetido.
- **Sugestão:** um aviso só no topo da lista ("Escolha o processo no passo 1 para ver onde cada arquivo será gravado"), combo de destino mais largo ou com nome curto e o caminho na dica, e "Remover" como ícone de lixeira com nome acessível. Também agrupar os arquivos vindos do mesmo compactado ("Envio do requerente.zip — 4 arquivos ▾").
- **Tamanho:** rápido.

### I-12. Lista de itens do editor pouco densa
- **Visto (C):** cada item usa duas linhas (título + etiqueta), e cabem ~8 de 60 itens na lista.
- **Sugestão:** uma linha por item, com uma bolinha de status antes do número (a etiqueta completa fica na dica e no filtro), mais uma opção "Compacto/Confortável", como no Outlook e no Teams.
- **Tamanho:** rápido.

### I-13. Termos técnicos à vista
- **Visto:** o ID interno "it_01M7M3HTDCXZNB4RH5KVH3WGFE" logo abaixo de "ITEM 01" (Propriedades), "Composto: 21 folhas" (barra de status), "Importar dados.json" e "Codex · plugin analise" no Novo relatório.
- **Sugestão:** esconder o ID (ou movê-lo para "Detalhes técnicos"), trocar por "21 folhas", "Importar análise da IA…" e "ChatGPT (Codex)".
- **Tamanho:** rápido.

### I-14. Maiúsculas e minúsculas diferentes entre tabela e painel
- **Visto (A):** a tabela mostra "Fulano de Tal / Baixa", e o painel mostra "FULANO DE TAL / BAIXA". As etiquetas de status estão em maiúsculas por toda parte.
- **Sugestão:** mostrar o texto como está na planilha nos dois lugares. Maiúsculas só nos rótulos de seção.
- **Tamanho:** rápido.

### I-15. Processos: grande vazio sem orientação
- **Visto (A):** "Escolha um processo na lista." em letra pequena no canto de uma área de ~1600 DIP. Na versão antiga em `capturas/` esse texto se sobrepunha ao título "Relatórios", já corrigido.
- **Sugestão:** usar o `EstadoVazio` que já existe (ícone + frase + atalhos "Novo relatório" e "Novo despacho") centralizado.
- **Tamanho:** rápido.

## Bom ter (acabamento)

- **I-16.** No editor maximizado (C), a folha a 100% mostra rolagem horizontal mesmo cabendo. A margem das marcas de revisão empurra a largura. Ajustar a "gola" no cálculo de "Ajustar largura".
- **I-17.** "Verificar atualizações" desativado aparece como botão cheio cinza-claro com texto branco (contraste baixo, B). Usar o estilo padrão desativado e deixar o motivo ao lado, que já existe.
- **I-18.** Busca da tabela com dica longa ("Buscar número, requerente, local ou profissional") e largura fixa de 320. No escuro, a dica fica quase do mesmo tom do fundo. Encurtar para "Buscar processo" e explicar o resto na dica.
- **I-19.** No Acompanhamento, "Detectar local e autor" aparece desativado sem dizer por quê (falta a pasta de processos). Mostrar o motivo na dica.
- **I-20.** Etiquetas da coluna Status muito largas ("AGUARDANDO DOCUMENTOS"). Com bolinha + texto normal a coluna cai pela metade.
- **I-21.** Itens excluídos na folha «código»: aparecem como etiquetas vermelhas "ITEM EXCLUÍDO 03" na margem (`paginador.js:1359-1466`). Não foram vistas, porque o exemplo não tinha excluídos. Conferir o contraste do vermelho `#a32418` no tema escuro da área ao redor da folha.

## Contraste nos dois temas (visto)

- **Claro:** textos principais e secundários legíveis. As etiquetas (fundo pastel + texto escuro) parecem passar de 4,5:1. Ponto fraco: botões desativados da faixa (cinza #A0A0A0 sobre #F3F3F3, só legível de perto) e o botão "Verificar atualizações" (I-17).
- **Escuro:** etiquetas de status e chips legíveis. Pontos fracos: dica da busca (I-18), "Desfazer/Refazer" desativados e os tons pastel da pizza (I-10). A folha continua branca no tema escuro, o que é o certo para um documento.
- Medição com conta-gotas não foi feita. Os valores acima são de leitura visual da captura.

## Comparação com produtos que resolvem o mesmo

| Questão | O que eles fazem | Aqui |
|---|---|---|
| Tabela larga (Excel, Smartsheet, Airtable) | Congelam a 1ª coluna, deixam esconder e reordenar colunas, têm densidade compacta e resumo recolhível | Nada disso. Status fica na última coluna (I-5, I-1) |
| Painel de detalhe (Airtable, Linear) | O registro abre por cima da lista ou numa coluna estreita, e a lista não perde as colunas-chave | O painel encolhe a tabela e esconde o Status (I-5) |
| Editor de documento (Word, Docs, Acrobat) | Painéis laterais recolhíveis. Propriedades só com seleção. "Ajustar à largura" sem rolagem horizontal | Os dois painéis ficam sempre abertos e a folha fica a 25% (I-2) |
| Configurações (Windows 11) | Faixa de comandos não aparece nas Configurações | Faixa "Arquivo" em Ajustes (I-8) |
