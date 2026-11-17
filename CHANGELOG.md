# Novidades do Editor de Relatórios PMVNI

Cada versão tem a sua seção; o texto da versão publicada aparece no aviso de atualização e na página da Release no GitHub.

## 0.5.0

### Despachos
- **PDF em 2 segundos e sem a janela "Aguardando conexão de impressora":** o LibreOffice não consulta mais a impressora do Windows ao converter o Word em PDF (antes levava cerca de 50 s). O PDF sai igual.
- **Pasta de hoje:** o despacho vai sempre para a pasta DESPACHO NN com a data do dia. A pasta da análise só é reaproveitada se for de hoje.
- **Gerar despacho** abre a pasta onde ele foi gravado, com o arquivo selecionado.
- **Ajustar com IA:** botão novo na caixa "Texto da exigência" para melhorar o texto antes de gravar.
- **Correção automática:** não continua mais com um rascunho antigo quando você escolhe "Continuar editando".

### Relatórios
- **Exportar PDF** grava o PDF ao lado do relatório (.rpmvni), na pasta do processo, e abre a pasta com ele selecionado.
- **Índices urbanísticos:** ao mudar um valor à mão, as contas que dependem dele (taxa, coeficiente, situação, área do lote no diagrama) se refazem na hora; um Desfazer volta tudo.
- **Diagrama dos índices:** um item pedido para a volumetria vai para a volumetria (lista e desenho), não mais para a planta.
- **Figuras:** "Adicionar figura…" no menu Figuras abre o PDF do projeto. No editor de figuras, clicar sem arrastar cria a região, os avisos ficam sempre à vista e o app explica quando um número desenhado na imagem não pode ser removido.
- **Tudo que se clica no relatório** mostra o contorno azul ao passar o mouse, como os itens.
- **Filtros das seções:** mais compactos, com a bolinha da cor de cada situação.

### Acompanhamento de processos
- **Colunas:** escolha quais aparecem (botão "Colunas") e mude a largura arrastando a borda do título; o app lembra.
- **Status e etiquetas** centralizados nas colunas; Status como a etiqueta ATENDE.
- **Colunas fixas** com a mesma cor da tabela e um traço de separação.

### Imóveis
- **Mapa sem sobreposição:** rótulos, cartão do lote e cotas procuram lugar livre por prioridade; o que não cabe some.
- **Cotas:** lados do terreno, faixa de domínio do DNIT (40 m do eixo, a confirmar) e distância do eixo ao terreno, e largura da rua medida na base. Botão "Cotas" para ligar e desligar.
- **Detectar automaticamente** (padrão) na pesquisa: reconhece inscrição, número, rua, bairro, loteamento ou nome.
- **"Sem lote no mapa":** inscrições com outro formato ou renumeradas agora acham o lote; inscrições baixadas aparecem em cinza, com o link para a inscrição atual.
- "Proprietário não identificado" aparece certo (antes saía "Proprietario Identificad").

### Todo o app
- **Imprimir (Ctrl+P)** na faixa de Relatórios, Legislação, Processos, Despachos, Imóveis e dos sites: abre a impressão com a lista de impressoras e a opção "Salvar como PDF". No relatório, sai igual ao PDF exportado.
- **Pesquisa na barra de título:** acha ajustes, funções, processos, arquivos, leis e linhas do Acompanhamento.
- **Modelos de IA** (Ajustes › Inteligência artificial): escolha a IA (Codex, Claude Code ou as APIs), o modelo e o nível de raciocínio usados na revisão e nos ajustes, com "Atualizar lista".
- **Ícones coloridos** na faixa de opções, pelo que cada comando faz.
- **Botões coloridos com brilho**, como no WinStatusBar, e botões das telas vazias no mesmo padrão.
- **Trilho:** a barrinha do módulo ativo não fica mais por cima do texto.
- **Recentes** em lista com ícone, nome e pasta.

## 0.4.0

### Gravação mais segura na planilha de acompanhamento
- **Planilha aberta no Excel:** a alteração não se perde mais. Ela fica guardada e aparece no aviso "aguardando gravação". O app grava sozinho quando a planilha for liberada, ou na hora em "Gravar agora", e isso vale mesmo que o editor seja fechado e aberto de novo.
- **Gravação segura:** a planilha é gravada num arquivo temporário, conferida e só então trocada. Uma falha no meio nunca deixa o arquivo pela metade.
- **Excluir processo:** não estraga mais as fórmulas da planilha (como "Dias sem atualização").
- **Preenchimento automático:** nunca grava por cima do que alguém digitou enquanto ele buscava.
- **Editar processo:** não regrava em maiúsculas os campos que você não mexeu e não muda a data à toa.
- **Aba da planilha:** é escolhida pelo nome e lembrada. Mudar a ordem das abas no Excel não troca mais a aba.
- **Cópias de segurança:** ficam numa pasta por planilha e nunca se sobrescrevem.

### Acompanhamento de processos
- **No notebook (1366 × 768):** o resumo fica compacto e se recolhe sozinho para a tabela aparecer, e o painel do processo abre por cima com a altura toda. O botão "Resumo" lembra a sua escolha.
- **Tabela:** Status logo depois do Processo, Nº e Processo fixos ao rolar para o lado, e colunas sem nenhum dado fora da tabela (avisadas no rodapé).
- **Resumo:** a pizza tem cores mais distintas, cada status tem a sua bolinha de cor e status sem processos saem do resumo.
- **Nomes e pesquisa:** os nomes aparecem iguais na tabela e no painel ("Enviado ao Requerente"). A pesquisa da barra de título filtra a planilha.
- **Teclado:** Enter edita, Delete exclui e a tecla Menu abre o menu do processo.
- **Comandos:** "Novo processo" é o botão principal da faixa, e "Escolher planilha…" tem um nome só.

### Interface
- **Trilho:** mais largo, com "Acompanhamento de processos" inteiro em duas linhas. Os módulos rolam em janelas baixas, e Ajustes fica sempre à vista.
- **Cor de destaque:** o verde institucional em todo o app, nos dois temas, no lugar da cor do Windows.
- **Barra de título:**
  - mostra só o que vale em cada módulo;
  - o processo aberto e o "Salvo" só aparecem em Relatórios;
  - em telas estreitas, os botões ficam só com o ícone.
- **Editor:**
  - em janelas estreitas, o painel Propriedades recolhe quando nada está selecionado;
  - "Ajustar largura" não deixa mais rolagem lateral;
  - a lista de seções mostra uma linha por item; Ajustes › Relatórios e revisão › "Lista de seções" escolhe entre Compacta e Confortável.
- **Novo relatório:**
  - os anexos de um compactado aparecem agrupados;
  - o destino aparece inteiro;
  - "Cancelar abertura" para a abertura de um compactado grande;
  - o compactado com nomes repetidos é aceito.
- **Tema:** as cores seguem o tema escolhido no app, mesmo quando é diferente do tema do Windows.
- **Textos mais claros:**
  - "Importar análise da IA…";
  - "21 folhas";
  - avisos da IA que dizem o que aconteceu;
  - "Substituir" ao trocar o despacho pelo relatório;
  - "Gravar só o Word (.docx)";
  - "Atualizar/Copiar diagnóstico".
- **Leitor de tela:** a recuperação de alterações não descarta mais com Esc, e o leitor de tela anuncia o estado de gravação e as mensagens que mudam.

### Fica para depois
- Trocar os cartões e as etiquetas antigos pelos estilos novos em todas as telas.
- Foco visível na caixa de instrução da IA do Novo despacho.
- Revisar os tamanhos fixos com o "Tamanho do texto" do Windows acima de 100%.

## 0.3.0

### Acompanhamento de processos (novo módulo no trilho)
- Página nova que mostra a planilha padrão de acompanhamento (ACOMPANHAMENTO DE PROCESSOS) com todas as colunas: Nº, datas, prioridade, processo, requerente, local, atualização, status, aprovação, profissional, dias sem atualização, observação e as demais.
- As listas (status, prioridade, aprovação) e as cores da formatação condicional vêm da própria planilha; os dias sem atualização são calculados como no Excel, com as mesmas faixas de cor.
- Cabeçalho de resumo com a contagem de cada status (clicar filtra) e gráfico de pizza do andamento, mais a contagem de aprovados e não aprovados.
- Novo processo, editar (todas as colunas), mudar status/aprovação/prioridade pelo menu "…", excluir, filtrar, buscar, ordenar por qualquer coluna e painel lateral com os dados do processo.
- Tudo o que muda no app é gravado na planilha, mexendo só nas células alteradas (formatação, listas, tabela, fórmulas e brasão continuam iguais). Antes de gravar, uma cópia de segurança vai para a pasta de dados do editor. Com a planilha aberta no Excel, o app avisa e oferece "Tentar de novo".
- Mudou a planilha no Excel: a página relê sozinha.
- Local e autor do projeto (Profissional) preenchidos sozinhos quando estão vazios: do relatório analisado (capa: Localização e Autoria), da consulta QGIS do processo (endereço no cadastro) e do carimbo das pranchas. Nunca troca o que foi digitado; o painel mostra de onde veio.
- Importar outra planilha no mesmo modelo, abrir no Excel e exportar a lista filtrada em CSV.
- Ajustes › Fontes de dados: escolher, verificar e abrir a planilha, e ligar/desligar o preenchimento automático.

### Ajustes › Sobre e suporte
- Página no padrão do WinStatusBar: Atualizações, Suporte (contato, copiar informações, abrir pasta de dados), Diagnóstico (relatório com versões, pastas e memória, com Atualizar e Copiar) e Sobre (versão e compilação, desenvolvedor, sistema, runtime e créditos). Ajuda › Sobre abre esta página.

### Correções
- Mapa de localização: a etiqueta vermelha "LOTE nº" sai de cima do lote e fica encostada nele, fora da projeção.
- Leitura padronizada da legislação: o menu do botão direito no texto segue o padrão dos menus do app (Copiar, Copiar parágrafo, Selecionar tudo).

## 0.2.1

- Novo relatório aceita arquivos compactados (.zip, .rar e .7z): o editor abre o arquivo e cada arquivo de dentro entra como anexo, com o destino pela regra do kit (matrícula, RRT/ART, requerimento, certidões e outros documentos em DOCUMENTOS; pranchas, DWG e o resto no projeto). Cada linha mostra de qual compactado e pasta o arquivo veio, e o destino continua podendo ser trocado. Compactado com senha, corrompido ou grande demais (mais de 4 GB ou 3.000 arquivos) é avisado e fica de fora.

## 0.2.0

### Janela e cabeçalho
- O editor não fecha mais ao passar a janela de um monitor para outro com escala diferente.
- Um erro inesperado na tela não fecha mais o editor: aparece um aviso e o trabalho aberto continua.
- Sumiu a dica "Ctrl+O" que ficava solta sobre a janela.
- Cabeçalho revisto: marca da Prefeitura centralizada, com mais respiro; pesquisa em pílula com lupa; Desfazer/Refazer e Anterior/Próximo agrupados no mesmo formato, com espaços iguais dos dois lados.

### Relatório
- Na ficha da Localização, rótulos longos ("INSCRIÇÃO IMOBILIÁRIA", "ÁREA DO LOTE (MATRÍCULA)") quebram linha em vez de invadir o valor. Valia para qualquer relatório com rótulo longo.
- A etiqueta longa do título de seção (zona, tipo de edificação) não sai mais da folha.
- Item excluído aparece como "ITEM EXCLUÍDO NN", com uma linha vermelha atravessando a folha onde ele estava (só na tela; o PDF não muda).

### Revisão do relatório
- "Rever com a IA" em todo item e em toda linha de tabela (índices, ambientes, chamadas): a justificativa é a instrução.
- A IA da revisão usa o ChatGPT Terra 6.1 com esforço baixo (ajustável em Ajustes › Inteligência artificial), responde em segundos, mostra o tempo e pode ser cancelada.
- O texto ajustado pela IA mantém o tamanho do original e fica coerente com a situação escolhida; mudar a justificativa avisa para reajustar o item inteiro.
- Editar à mão o valor de um índice (ex.: recuo de 3 m contra o mínimo de 3,00 m) refaz a situação e o texto da diferença na hora, sem IA.
- Novo item: cria um item de análise com os mesmos campos, logo depois do selecionado.
- Painel Propriedades em cartões (Estado, Esquadrias, Conteúdo, Histórico), com os campos editáveis no próprio painel.
- Esquadrias por ambiente, separadas em Janelas e Portas, com largura e altura editáveis e a conta (área, vão total, mínimo e resultado) refeita a cada tecla.
- Adicionar janela, Adicionar porta e Adicionar ambiente (nome, área do piso, divisor e pavimento); tudo se desfaz.
- Pedir à IA para acrescentar algo na volumetria cria a chamada numerada nova (ex.: 7), com a marca no desenho.
- Botão Figuras na Página Inicial, para abrir direto o editor da figura.
- Os botões principais de cada painel aparecem na cor de destaque.

### Despachos
- Correção automática: um clique monta o despacho com todos os itens que não atendem do relatório (situação já revisada) e vai direto para a conferência.
- Assinatura digital dos despachos (certificado .pfx/.p12 ou do Windows), com a assinatura visível acima do nome; senha pedida a cada vez ou guardada cifrada neste computador (Ajustes › Despachos e PDF).
- O texto não invade mais o cabeçalho da 2ª folha em diante, e o fecho (data e assinatura) fica inteiro na mesma folha.
- A caixa de edição da prévia cabe no cartão e na janela, com altura conforme o texto.
- Painel do Novo despacho em cartões, no mesmo padrão do painel Propriedades.

### Processos
- A página do processo ganhou Classificar (nome, data, tipo), Agrupar (etapa, tipo de arquivo, data) e Visualizar (Lista ou Miniaturas). A escolha fica lembrada.
- Em Miniaturas, os arquivos aparecem numa grade como a do Explorador, com a prévia de cada arquivo e grupos que recolhem.
- Botão direito, Enter e duplo clique nos arquivos: Abrir, Mostrar no Explorador, Copiar caminho.

### Imóveis (consulta à base QGIS)
- O mapa de localização segue o do kit: balão com os dados, lote em vermelho, APP, faixas da BR-262, zonas, ruas, rio e confrontantes, com escala e norte.
- O relatório do imóvel tem o aspecto do relatório de análise: faixa do processo, quadros, mapa com legenda ao lado e a ficha em quatro grupos com selos.
- Corrigido: o perímetro da AUC não era lido e o relatório dizia sempre "fora das AUC".
