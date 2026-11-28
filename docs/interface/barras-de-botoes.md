# Barras de botões: inventário por página

Levantamento de 2026-11-28, feito pela leitura do código (`src/Pmvni.App`), sem executar o app. É a primeira etapa da revisão das barras: o que existe hoje, em que barra, e como cada botão se comporta.

## Classes

| Sigla | Classe | Significado |
|---|---|---|
| **F** | Fixo, sempre ativo | Sempre visível e clicável naquela barra. |
| **C** | Fixo, cinza sob demanda | Sempre visível; fica cinza até a condição indicada. |
| **T** | Temporário | Só aparece durante uma função ou estado; some depois. |
| ⇄ | Alternador | Liga e desliga (pode ser F ou C). |
| ▾ | Menu | Abre uma lista de opções. |

"Ocupado" = o app está carregando ou gravando; quase todo botão F fica cinza nesse instante, e isso não é repetido nas tabelas.

## Camadas de barra na janela principal

| # | Camada | Onde | Muda com o módulo? | Fonte |
|---|---|---|---|---|
| 1 | Barra de título | Topo da janela | Não | `MainWindow.xaml:32-155` |
| 2 | Trilho de módulos | Lateral esquerda | Não | `Controles/Trilho.xaml` |
| 3 | Faixa de opções: abas | Abaixo do título | Sim | `Logica/Faixas.cs` |
| 4 | Faixa de opções: primário, extra, secundário e comandos da aba | Abaixo das abas | Sim | `Controles/FaixaDeOpcoes.xaml.cs` |
| 5 | Avisos com botões de ação | Topo da página | Sim | cada `Pagina*.xaml` |
| 6 | Barra própria da página | Cabeçalho da página ou de um painel | Sim | cada `Pagina*.xaml` |
| 7 | Rodapé da página | Base da página | Sim | Relatórios, Legislação, Novo despacho, Imóveis |

## 1. Barra de título (igual em todas as páginas)

| Grupo | Botão | Classe | Condição |
|---|---|---|---|
| Estado da análise | Pílula "Analisando com o… · processo · tempo" | T | Só com uma análise em andamento, concluída, pausada ou com erro (`MainWindow.NovoRelatorio.cs:1834`). |
| Navegação | Anterior | C | Há página anterior no histórico de páginas visitadas (`MainWindow.Navegacao.cs:28`). |
| Navegação | Próxima | C | Há página seguinte no histórico (`MainWindow.Navegacao.cs:29`). |
| Pesquisa global | Caixa "Pesquisar arquivos, funções, leis e configurações…" (Ctrl+K) | F | |
| Edição | Desfazer | C | Só em Relatórios, com relatório editável e algo a desfazer (`MainWindow.Revisao.cs:216`). |
| Edição | Refazer | C | Idem, com algo a refazer. |

Indicadores sem clique: processo aberto e requerente (T), selo "Salvo 14:32" (T).

## 2. Faixa de opções, por módulo

A aba **Ajuda** (Atalhos de teclado, Sobre: ambos F) existe em todos os módulos. A aba **Arquivo de módulo** (Importar análise da IA…, Abrir…, Recentes ▾) se repete em Despachos, Processos, Imóveis, Acompanhamento e nos três sites; Importar e Abrir são F, Recentes é C (precisa haver recentes).

### 2.1 Relatórios (também vale com "Novo relatório" aberto)

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Primário | Novo relatório | F | |
| Extra | Retomar análise | T | Só com o formulário do Novo relatório à vista. |
| Extra | Descartar pendência… | T | Idem. |
| Secundário | Abrir… | F | |
| Arquivo | Importar análise da IA… | F | |
| Arquivo | Recentes ▾ | C | Há arquivos recentes. |
| Arquivo | Salvar | C | Relatório aberto e não somente leitura. |
| Arquivo | Salvar como… | C | Relatório aberto. |
| Arquivo | Emitir processo aprovado… | F | Nenhuma condição no código: ativo mesmo sem relatório. |
| Arquivo | Exportar PDF | C | Relatório aberto, exportável e folhas atualizadas. |
| Arquivo | Imprimir | C | Idem, e fora do Novo relatório. |
| Arquivo | Fechar | C | Relatório aberto. |
| Página Inicial | Situação ▾ (5 situações) | C | Relatório editável e item incluído selecionado. |
| Página Inicial | Novo item | C | Relatório editável. |
| Página Inicial | Editar texto | C | Relatório editável e item incluído selecionado. |
| Página Inicial | Original ⇄ | C | Item ou linha selecionados. |
| Página Inicial | Restaurar | C | Item selecionado com revisão. |
| Página Inicial | Item anterior, Próximo item, Próximo pendente | C | Relatório aberto. |
| Página Inicial | Figuras ▾ | C | Relatório editável ou com figuras. |
| Página Inicial | Novo despacho ▾ (6 tipos) | F | Cinza só enquanto um despacho é gravado; cada tipo fica cinza se o modelo falta. |
| Página Inicial | Excluir item ⇄ Restaurar item (no fim da barra) | C | Item selecionado; o rótulo troca conforme o item. |
| Revisão | Marcas de revisão ⇄, Somente alterados ⇄, Conferência, Comparar | C | Relatório aberto. |
| Figuras | Inserir região da prancha | C | Relatório editável. |
| Figuras | Editar figura | C | Figura selecionada. |
| Figuras | Excluir figura (no fim da barra) | C | Relatório editável e figura selecionada. |
| Exibir | Diminuir, Aumentar, Ajustar largura, Página inteira | C | Folhas à vista. |
| Exibir | Painel lateral ⇄, Propriedades ⇄ | C | Fora do Novo relatório. |
| Exibir | Tema ▾ | F | |

### 2.2 Despachos (também vale com "Novo despacho" aberto)

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Primário | Novo despacho ▾ (Correção, Correção automática, Aprovação, Retirada, Devolução, Uso geral) | F | |
| Arquivo | Arquivo de módulo | F / C | |
| Página Inicial | Abrir… | F | |
| Página Inicial | Abrir PDF | C | Despacho escolhido tem PDF. |
| Página Inicial | Gerar PDF | C | Despacho em Word ainda sem PDF. |
| Página Inicial | Imprimir | C | Há PDF do despacho e o Novo despacho está fechado. |
| Página Inicial | Mostrar no Explorador | C | Despacho escolhido. |
| Página Inicial | Ir ao processo | C | Despacho escolhido pertence a um processo. |
| Página Inicial | Atualizar lista | F | |
| Página Inicial | Tipo ▾ (Todos, Correção, Aprovação) | F | |
| Página Inicial | Só do processo ⇄ | C | Há processo escolhido em Processos. |
| Exibir | Agrupar ▾, Ordenar ▾ | F | |

### 2.3 Processos

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Primário | Novo relatório | F | |
| Secundário | Novo processo | F | |
| Arquivo | Arquivo de módulo | F / C | |
| Página Inicial | Abrir relatório | C | Linha de relatório ou análise escolhida. |
| Página Inicial | Importar análise | C | Processo escolhido com análise a importar. |
| Página Inicial | Novo despacho ▾ | F | |
| Página Inicial | Abrir arquivo | C | Arquivo escolhido. |
| Página Inicial | Imprimir | C | Arquivo imprimível escolhido. |
| Página Inicial | Abrir pasta | C | Processo escolhido. |
| Página Inicial | Atualizar | F | |
| Página Inicial | Ano ▾ | C | Pasta de processos lida. |
| Exibir | Ordenar ▾ (Número, Data, Requerente) | F | |

### 2.4 Legislação

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Primário | Anexar PDFs… | F | Cinza só com o catálogo em somente leitura. |
| Secundário | Consultar outras leis | F | |
| Arquivo | Gerenciar anexos, Importar análise da IA…, Abrir… | F | |
| Arquivo | Recentes ▾ | C | Há recentes. |
| Página Inicial | Copiar, Copiar citação | C | Texto selecionado na lei. |
| Página Inicial | Imprimir | C | Lei aberta. |
| Página Inicial | Página anterior, Próxima página | C | Lei aberta. |
| Página Inicial | Pesquisar em ▾ (Nesta lei, Em todas as leis) | F | "Nesta lei" pede lei aberta. |
| Página Inicial | Ocorrência anterior, Próxima ocorrência | C | Lei com texto e pesquisa "Nesta lei". |
| Exibir | Diminuir, Aumentar, Ajustar largura, Página inteira | C | Lei aberta. |
| Exibir | Marcadores ⇄ | C | Lei com marcadores. |
| Exibir | Lista de leis ⇄ | F | |

### 2.5 Imóveis

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Primário | Novo processo | F | |
| Arquivo | Arquivo de módulo | F / C | |
| Página Inicial | Voltar à pesquisa, Salvar PDF, Imprimir | C | Relatório do imóvel aberto. |
| Página Inicial | Ler a base de novo | C | Pasta QGIS configurada e base não carregando. |
| Página Inicial | Pasta QGIS | F | |

### 2.6 Acompanhamento

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Primário | Novo processo (linha nova na planilha) | C | Planilha lida. |
| Arquivo | Arquivo de módulo | F / C | |
| Página Inicial | Editar processo | C | Linha escolhida. |
| Página Inicial | Detectar local e autor | C | Planilha lida e pasta de processos configurada. |
| Página Inicial | Ler a planilha de novo, Abrir no Excel | C | Há planilha. |
| Página Inicial | Escolher planilha…, Local da planilha | F | |
| Página Inicial | Excluir processo (no fim da barra) | C | Linha escolhida. |

### 2.7 Essencial, GPI e Alfresco (os três iguais)

| Posição | Botão | Classe | Condição |
|---|---|---|---|
| Arquivo | Arquivo de módulo | F / C | |
| Página Inicial | Voltar | C | O site tem página anterior. |
| Página Inicial | Avançar | C | O site tem página seguinte. |
| Página Inicial | Atualizar, Página inicial, Abrir no navegador, Imprimir | C | Site carregado. |

Sem botão primário.

### 2.8 Ajustes

Só a aba Ajuda. Sem primário, sem secundário, sem Arquivo.

## 3. Barras próprias de cada página

### 3.1 Relatórios (`PaginaRelatorios.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Avisos | Salvar como cópia, Recarregar | T | Conflito de gravação. |
| Avisos | Recompor | T | Folhas desatualizadas. |
| Painel lateral | Abas Seções, Excluídos (n), Histórico | F | |
| Painel lateral | Pesquisar seção ou item; Filtrar por situação | F | |
| Painel lateral | Ver mais itens | T | Lista com mais itens que os mostrados. |
| Propriedades | Fechar (X) | F | |
| Propriedades | Conteúdo oculto | T | Item com conteúdo oculto. |
| Propriedades | Ajustar o item com a IA, Cancelar | T | Com justificativa; Cancelar só durante a consulta. |
| Propriedades | Restaurar original | F | |
| Rodapé | Diminuir, controle de zoom, Aumentar, Ajustar largura, Página inteira | C | Relatório aberto. |
| Rodapé | Folha n de M (caixa) | T | Relatório aberto. |
| Tela vazia | Novo relatório, Abrir, recentes | T | Nada aberto. |

### 3.2 Novo relatório / Análise de projetos (`PaginaNovoRelatorio.xaml`)

Duas vistas: **formulário** e **monitor**.

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Avisos | Importar relatório ⇄ Corrigir entrega e retomar | T | Entrega pendente. |
| Avisos | Abrir registro | T | Execução terminou sem relatório. |
| Avisos | Ver registro, Retomar análise, Abrir relatório (criados em código) | T | Avisos da entrega; análise parada; relatório pronto. |
| Avisos | Remontar relatório (teste) | T | Só em build de teste; nunca na distribuição. |
| Topo do formulário | Consumo e logs | F | |
| Topo do formulário | Ver andamento | T | Há execução não abandonada. |
| Topo do formulário | Voltar ao relatório | T | Há relatório aberto. |
| Formulário | Buscar processo, Processo selecionado, Criar processo… | C | Formulário não ocupado. |
| Formulário | Cancelar abertura | T | Durante a extração de arquivo compactado. |
| Formulário | Analisar com o ChatGPT | C | Requisitos do provedor atendidos. |
| Formulário | Analisar com o Claude | C | Na distribuição fica sempre cinza. |
| Topo do monitor | Abrir registro, Nova análise | F | |
| Topo do monitor | Pausar, Cancelar | T | Análise rodando. |
| Menu do monitor | Resumo, Etapas, Consumo, Log | F | |

### 3.3 Despachos (`PaginaDespachos.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Avisos | Abrir Ajustes | T | Pasta de processos ausente. |
| Avisos | Abrir no Word, Abrir PDF, Mostrar no Explorador | T | Logo depois de gravar um despacho. |

Sem barra própria: tudo vem da faixa.

### 3.4 Novo despacho (`PaginaNovoDespacho.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Cabeçalho do painel | Fechar (X) | F | Acompanha o Cancelar. |
| Passo 1 | Criar processo…, Sem processo, Escolher arquivo…, Criar pasta… | F | |
| Passo 2 | Adicionar exigência… | T | Depois de escolhida a origem. |
| Base do painel | Criar automaticamente a partir do relatório | T | Só no tipo Correção. |
| Prévia | Caixa de instrução para a IA + enviar | F | |
| Passo 3 | Marcar todas as páginas | T | Conferência do PDF. |
| Espera | Cancelar geração | T | Durante a geração. |
| Rodapé | Gerar e conferir | C | Passos 1 e 2 preenchidos; some no passo 3. |
| Rodapé | Gravar na pasta do processo | T + C | Só no passo 3; cinza até conferir as páginas. |
| Rodapé | Voltar e editar | T | Só no passo 3. |
| Rodapé | Cancelar | F | |

### 3.5 Processos (`PaginaProcessos.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Avisos | Abrir Ajustes | T | Pasta de processos ausente. |
| Lista | Filtro "Número ou nome do processo" | F | |
| Cabeçalho do detalhe | Ordenar ▾ (Nome, Data, Tipo; Crescente, Decrescente) | F | |
| Cabeçalho do detalhe | Agrupar ▾ (Etapa ou pasta, Tipo de arquivo, Data, Não agrupar) | F | |
| Cabeçalho do detalhe | Visualizar ▾ (Lista, Miniaturas) | F | |

### 3.6 Legislação (`PaginaLegislacao.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Avisos | Cancelar (anexação) | T | Durante a anexação de PDFs. |
| Painel | Aba Leis | F | |
| Painel | Abas Marcadores, Resultados | C | PDF com marcadores; pesquisa em todas as leis feita. |
| Painel | Filtro, cartões de esfera, botões de tipo | F | |
| Painel vazio | Anexar PDFs…, Gerenciar anexos | T | Nenhuma lei anexada. |
| Topo do leitor | Leitura padronizada / PDF original | T | Lei com texto padronizado. |
| Rodapé | Página (caixa), Página anterior, Próxima página, Diminuir, Aumentar | C | Lei aberta. |
| Consulta ao Leis.org (barra inteira T) | Voltar, Avançar, Parar, Página inicial, Salvar PDF… | C | Conforme a navegação do portal. |
| Consulta ao Leis.org | Atualizar, Voltar ao acervo | F | |

### 3.7 Imóveis (`PaginaImoveis.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Topo da pesquisa | Configurar pasta QGIS | F | |
| Linha de pesquisa | Parâmetro, Condição, valor, Pesquisar | F | |
| Tela vazia | Configurar pasta QGIS | T | Base não configurada. |
| Rodapé | Anterior, Próxima | C | Há mais páginas de resultado. |
| Topo do relatório (barra inteira T) | Voltar à pesquisa, Cotas ⇄, Imprimir, Salvar PDF… | F | Enquanto o relatório do imóvel está aberto. |

### 3.8 Acompanhamento (`PaginaAcompanhamento.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Cabeçalho | Exportar ▾ (Exportar lista filtrada CSV…, Copiar lista filtrada; Abrir no Excel é T) | C | Planilha lida. |
| Cabeçalho | Filtrar ▾ | C | Planilha lida. |
| Cabeçalho | Colunas ▾, Resumo ⇄ | F | |
| Cabeçalho | Aba da planilha | T | Planilha com mais de uma aba. |
| Barra de busca | Pesquisar, Todos os status ▾, Ordenar ▾ | F | |
| Tabela vazia | Limpar filtros | T | Filtro sem resultado. |
| Tela vazia | Escolher planilha…, Abrir Ajustes | T | Sem planilha. |
| Painel do processo (inteiro T) | Fechar (X), Editar processo…, Abrir pasta | F | Com uma linha escolhida. |

### 3.9 Ajustes (`PaginaAjustes.xaml`)

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Menu lateral | Geral; Profissional e assinatura; Pastas e fontes de dados; Relatórios e revisão; Módulos; Inteligência artificial; Modelos da análise; Kit PMVNI; Sistema | F | |
| Cabeçalho da categoria | Restaurar padrões | T | Só em Modelos da análise e só em build de teste. |

Sem pesquisa própria (usa a da barra de título), sem Salvar ou Cancelar (grava sozinho).

## 4. Lista consolidada

### 4.1 Essenciais: fixos e sempre ativos

| Onde | Botões |
|---|---|
| Barra de título | Pesquisa global |
| Faixa, todo módulo | Ajuda: Atalhos de teclado, Sobre |
| Faixa, exceto Ajustes | Arquivo: Importar análise da IA…, Abrir… |
| Faixa, primário | Novo relatório (Relatórios, Processos); Novo despacho ▾ (Despachos); Anexar PDFs… (Legislação); Novo processo (Imóveis) |
| Faixa, secundário | Abrir… (Relatórios); Novo processo (Processos); Consultar outras leis (Legislação) |
| Faixa, atualizar e configurar | Atualizar lista (Despachos); Atualizar (Processos); Pasta QGIS (Imóveis); Escolher planilha…, Local da planilha (Acompanhamento) |
| Faixa, exibição | Tema ▾ (Relatórios); Tipo ▾, Agrupar ▾, Ordenar ▾ (Despachos); Ordenar ▾ (Processos); Pesquisar em ▾, Lista de leis ⇄ (Legislação) |
| Página | Ordenar ▾, Agrupar ▾, Visualizar ▾ (Processos); Colunas ▾, Resumo ⇄, Todos os status ▾, Ordenar ▾ (Acompanhamento); Configurar pasta QGIS, Pesquisar (Imóveis); Consumo e logs (Novo relatório); Cancelar (Novo despacho) |

### 4.2 Fixos que ficam cinza até serem ativados

| Ativa quando… | Botões |
|---|---|
| Há histórico de navegação | Anterior, Próxima (título); Voltar, Avançar (sites e Leis.org) |
| Há algo a desfazer | Desfazer, Refazer (título) |
| Há arquivos recentes | Recentes ▾ |
| Há um documento aberto | Salvar, Salvar como…, Exportar PDF, Imprimir, Fechar, zoom (4 botões, na faixa e no rodapé), Item anterior, Próximo item, Próximo pendente, toda a aba Revisão, Painel lateral ⇄, Propriedades ⇄ (Relatórios); Página anterior, Próxima página, zoom, Imprimir (Legislação); Voltar à pesquisa, Salvar PDF, Imprimir (Imóveis); Atualizar, Página inicial, Abrir no navegador, Imprimir (sites) |
| Há um item selecionado | Situação ▾, Editar texto, Original ⇄, Restaurar, Excluir item, Editar figura, Excluir figura (Relatórios); Abrir PDF, Gerar PDF, Mostrar no Explorador, Ir ao processo (Despachos); Abrir relatório, Importar análise, Abrir arquivo, Abrir pasta (Processos); Editar processo, Excluir processo (Acompanhamento) |
| Há texto selecionado | Copiar, Copiar citação (Legislação) |
| A fonte de dados está configurada e lida | Ano ▾ (Processos); Só do processo ⇄ (Despachos); Ler a base de novo (Imóveis); Novo processo, Detectar local e autor, Ler a planilha de novo, Abrir no Excel, Exportar ▾, Filtrar ▾ (Acompanhamento); Marcadores ⇄, Ocorrência anterior, Próxima ocorrência (Legislação) |
| O formulário está completo | Gerar e conferir, Gravar na pasta do processo (Novo despacho); Analisar com o ChatGPT (Novo relatório) |

### 4.3 Temporários: aparecem durante a função

| Função ou estado | Botões |
|---|---|
| Análise em curso ou encerrada | Pílula da análise (título); Pausar, Cancelar (monitor); Ver andamento; Retomar análise, Descartar pendência… (faixa); Importar relatório / Corrigir entrega e retomar; Abrir registro; Abrir relatório |
| Relatório aberto por trás de outra página | Voltar ao relatório (Novo relatório) |
| Erro ou conflito | Salvar como cópia, Recarregar, Recompor (Relatórios); Abrir Ajustes (pasta ausente, em Despachos e Processos) |
| Depois de gravar | Abrir no Word, Abrir PDF, Mostrar no Explorador (Despachos) |
| Passo de um assistente | Adicionar exigência…, Criar automaticamente a partir do relatório, Marcar todas as páginas, Voltar e editar, Gravar na pasta do processo (Novo despacho) |
| Operação demorada | Cancelar geração (Novo despacho); Cancelar anexação (Legislação); Cancelar abertura (Novo relatório); Cancelar consulta à IA (Propriedades) |
| Vista secundária aberta | Barra do Leis.org (Legislação); barra do relatório do imóvel (Imóveis); painel do processo (Acompanhamento); Leitura padronizada / PDF original (Legislação) |
| Tela vazia | Novo relatório, Abrir, recentes (Relatórios); Anexar PDFs…, Gerenciar anexos (Legislação); Configurar pasta QGIS (Imóveis); Escolher planilha…, Abrir Ajustes, Limpar filtros (Acompanhamento) |

## 5. Pontos para a revisão

Achados da leitura, ainda sem decisão.

### Mesma ação em duas barras

| Ação | Onde aparece |
|---|---|
| Zoom (Diminuir, Aumentar, Ajustar largura, Página inteira) | Faixa › Exibir e rodapé, em Relatórios e em Legislação. |
| Página anterior / Próxima página | Faixa e rodapé da Legislação. |
| Voltar à pesquisa, Imprimir, Salvar PDF | Faixa de Imóveis e barra do relatório do imóvel. |
| Abrir no Excel | Faixa do Acompanhamento e menu Exportar ▾. |
| Editar processo | Faixa do Acompanhamento e painel do processo. |
| Abrir registro | Monitor, aviso e um terceiro "Ver registro". |
| Retomar análise | Faixa (abre a escolha) e aviso (abre o histórico): destinos diferentes. |
| Pasta QGIS / Configurar pasta QGIS | Faixa, topo da página e tela vazia de Imóveis, com dois rótulos. |

### Mesmo rótulo, ações diferentes

| Rótulo | Significados |
|---|---|
| Abrir… | Em Despachos há dois: Arquivo › Abrir… (relatório) e Página Inicial › Abrir… (despacho). |
| Ordenar | Em Processos: faixa › Exibir ordena os processos; o da página ordena os arquivos (nome acessível "Classificar"). |
| Novo processo | Em Processos e Imóveis cria a pasta do processo; em Acompanhamento cria uma linha na planilha. |
| Anterior / Próxima | Título: histórico de páginas do app (nome acessível "Página anterior"). Legislação: página do PDF. Imóveis: página de resultados. |
| Atualizar | "Atualizar lista" (Despachos), "Atualizar" (Processos), "Ler a base de novo" (Imóveis), "Ler a planilha de novo" (Acompanhamento) fazem a mesma coisa. |

### Estado suspeito

| Achado | Fonte |
|---|---|
| "Emitir processo aprovado…" fica na aba Arquivo de Relatórios, mas não usa o relatório aberto: pede os PDFs das pranchas. Por isso está sempre ativo (correto); o problema é o lugar, e ele é verde. | `MainWindow.Aprovacao.cs:8-14` |
| "Retomar análise" e "Descartar pendência…" aparecem na faixa mesmo sem nada a retomar, e podem aparecer em qualquer módulo antes da primeira abertura do Novo relatório (não conferido em execução). | `PaginaNovoRelatorio.xaml.cs:86-96` |
| "Descartar análise" existe no código e nas mensagens, mas está num painel escondido para sempre. | `PaginaNovoRelatorio.xaml:85-88` |
| "Analisar com o Claude" fica sempre cinza na distribuição; "Analisar com o Gemini" não tem ação. | `MainWindow.NovoRelatorio.cs:855-856` |
| Desfazer e Refazer ficam na barra de título de todos os módulos, mas só funcionam em Relatórios. | `MainWindow.Revisao.cs:216-221` |
| O filtro Tipo ▾ de Despachos só tem Correção e Aprovação; Novo despacho ▾ cria seis tipos. | `Logica/Faixas.cs:42-50, 155-160` |
| Dois botões verdes lado a lado na faixa: "Novo relatório" e "Retomar análise". | `FaixaDeOpcoes.xaml:19-21` |
| O Novo relatório não tem botão de fechar próprio; o Novo despacho tem X e Cancelar. | `PaginaNovoRelatorio.xaml`, `PaginaNovoDespacho.xaml:36, 260` |
| Com o Leis.org aberto ficam duas barras de comando empilhadas: a faixa da Legislação (quase toda cinza) e a do portal. | `PaginaLegislacao.xaml:173-197` |

### Posição do botão principal varia

| Página | Onde fica a ação principal |
|---|---|
| Relatórios, Processos, Despachos, Legislação | Primário da faixa, à esquerda. |
| Novo despacho | Rodapé, à direita (Gerar e conferir, Gravar). |
| Novo relatório | Corpo do formulário (Analisar com o…). |
| Imóveis | Linha de pesquisa (Pesquisar) e, no relatório, topo à direita (Salvar PDF…). |
| Acompanhamento | Primário da faixa (Novo processo) e painel lateral (Editar processo…). |

## 6. Trilho de módulos (`Controles/Trilho.xaml.cs`)

Dez itens, todos **F**: Relatórios, Despachos, Processos, Legislação, Acompanhamento de processos, Imóveis, Essencial, GPI, Alfresco e, fixo no pé, Ajustes. Nenhum fica cinza nem some; só o indicador do módulo ativo muda.

## 7. Janelas secundárias

### 7.1 Editar figura (`Figuras/JanelaFigura.cs`): barra no topo

| Botão | Classe | Condição |
|---|---|---|
| Inserir região da prancha… | C | Relatório não somente leitura. |
| Selecionar ⇄, Marcar região ⇄, Seta ⇄, Texto ⇄ | C | Figura incluída e relatório editável. |
| Desfazer, Refazer | C | Há histórico. |
| Diminuir o zoom, Aumentar o zoom, Figura inteira | F | |
| Concluir (principal, topo à direita) | F | |
| Remover marcação (painel lateral) | T + C | Só com marcação selecionada; cinza se ela já foi gravada. |

### 7.2 Inserir região da prancha (`Figuras/JanelaPrancha.cs`): topo, barra e rodapé

| Barra | Botão | Classe | Condição |
|---|---|---|---|
| Topo | Lista de PDFs, Escolher arquivo… | C | Janela livre (não ocupada). |
| Barra | Página anterior, Próxima página | C | PDF carregado e há página nesse sentido. |
| Barra | Diminuir o zoom, Aumentar o zoom, Página inteira | C | PDF carregado. |
| Menu "…" | Selecionar página | C | Página pronta. |
| Menu "…" | Limpar recorte | C | Há recorte. |
| Centro | Escolher arquivo… (principal) | T | Nenhum PDF carregado. |
| Rodapé | Inserir no relatório (principal) | C | Recorte válido e prévia pronta. |
| Rodapé | Cancelar | F | |

### 7.3 Emitir processo aprovado (`Despachos/JanelaAprovacao.cs`): barra no topo, dois modos

| Modo | Botão | Classe | Condição |
|---|---|---|---|
| Ambos | Página anterior, Próxima página | C | Há página nesse sentido. |
| Ambos | Diminuir o zoom, Aumentar o zoom | F | |
| Revisão | Aceitar prancha (principal), Adicionar carimbo ou observações | T | Só neste modo. |
| Marcação | Confirmar prancha (principal) | T + C | Só neste modo; cinza até carimbo ou notas estarem nesta página. |
| Marcação | Posicionar carimbo, Adicionar observações…, Desfazer | T | Só neste modo; nunca ficam cinza. |

Sem Cancelar nem Fechar: só o X da janela.

### 7.4 Imprimir (`Impressao/JanelaImpressao.cs`): barra no topo

| Botão | Classe | Condição |
|---|---|---|
| Imprimir… (principal) | C | Arquivo carregado; se o carregamento falha, fica cinza até fechar. |
| Fechar | F | |

### 7.5 Sem barra própria

| Tela | Observação |
|---|---|
| Popup dos sites (`Sites/JanelaPopupSite.cs`) | Só o navegador. |
| Boletim cadastral (`MainWindow.Bci.cs:280`) | Só o PDF, com a barra nativa do visualizador. |
| Página dos sites (`Paginas/PaginaSites.cs`) | Usa a faixa; só avisos temporários. |
| Escolha de versão das peças (diálogo) | "Abrir o PDF" por versão (C); "Usar esta versão" (C, até escolher) e "Cancelar" (F). |

## 8. Dentro do conteúdo (folhas e leitor)

| Onde | Botão | Classe | Condição |
|---|---|---|---|
| Folhas do relatório | Etiqueta "ITEM EXCLUÍDO nn" → Restaurar item | T | Marcas de revisão ligadas e item excluído. |
| Folhas, botão direito no item | Propriedades, Original ⇄, Situação ▸, Editar texto, Novo item abaixo, Excluir item / Restaurar item | T | Menu de contexto; cada opção segue o estado do botão igual na faixa. |
| Folhas, botão direito na linha de tabela | Propriedades, Original ⇄, Situação ▸, Editar texto, Restaurar original | T | Idem. |
| Textos do app | Selecionar tudo, Copiar / Copiar parágrafo | T | Menu de contexto. |

Não há barra flutuante de formatação nas folhas (Enter confirma, Esc cancela) nem barra do visualizador no leitor de leis: lá tudo vem da faixa e do rodapé.

## 9. Pontos para a revisão: janelas secundárias

| Achado | Onde |
|---|---|
| A ação principal muda de lugar: topo à direita (Figura, Impressão), rodapé (Prancha), meio da barra (Aprovação). | 7.1 a 7.4 |
| Quatro formas de sair: "Concluir", "Cancelar", "Fechar" e nenhuma (Aprovação). | 7.1 a 7.4 |
| Zoom em três formatos: só ícone na barra (Figura), com rótulo (Prancha), botões soltos (Aprovação); só na Prancha fica cinza sem documento. | 7.1 a 7.3 |
| "Figura inteira" e "Página inteira" para o mesmo ajuste; a Aprovação não tem. | 7.1, 7.2 |
| Página anterior / Próxima com ícones diferentes na Prancha e na Aprovação. | 7.2, 7.3 |
| "Desfazer" é histórico na Figura (com atalho, fica cinza) e "remover marcação da página" na Aprovação (sempre ativo). | 7.1, 7.3 |
| Na Prancha, "Selecionar página" e "Limpar recorte" ficam escondidos no menu "…"; e há dois "Escolher arquivo…" ao mesmo tempo sem PDF. | 7.2 |
| Na Impressão, se o arquivo não abre, não há como tentar de novo. | 7.4 |
| Boletim cadastral e popup dos sites mostram PDF ou página sem "Imprimir / Fechar", ao contrário da janela de impressão. | 7.5 |
| No trilho, a tecla de acesso não segue a ordem visual (Acompanhamento é o 5º e tem "10"; Ajustes é o último e tem "5"). | 6 |
| Durante a edição de texto nas folhas, pelo código nenhum menu de recortar, copiar e colar aparece (não conferido em execução). | 8 |

## 10. Revisão do inventário (2026-11-28)

Os achados mais sensíveis foram conferidos de novo no código.

| Achado | Resultado | Fonte |
|---|---|---|
| Retomar análise / Descartar pendência… sem condição própria | Confirmado: só dependem de o formulário estar à vista. | `PaginaNovoRelatorio.xaml.cs:86-87` |
| Descartar análise escondido para sempre | Confirmado: pai `Visibility="Collapsed"` sem nome. | `PaginaNovoRelatorio.xaml:85-88` |
| Analisar com o Claude sempre cinza na distribuição | Confirmado. | `MainWindow.NovoRelatorio.cs:855-856` |
| Desfazer/Refazer só funcionam em Relatórios | Confirmado. | `MainWindow.Revisao.cs:215-221` |
| Filtro Tipo ▾ com só 2 dos 6 tipos | Confirmado. | `Logica/Faixas.cs:42-50, 155-160` |
| Tecla de acesso do trilho fora da ordem | Confirmado: usa o número do módulo, não a posição. | `Trilho.xaml.cs:98-102` |
| Emitir processo aprovado "nunca fica cinza" | **Corrigido**: não é defeito, ele não depende do relatório. Ver seção 5. | `MainWindow.Aprovacao.cs:8-14` |
| Faltavam botões verdes dentro do conteúdo | **Acrescentados** na seção 11: Adicionar ambiente…, Aplicar (diagrama), Relatório e Gerar boletim… (linhas de Imóveis), Ver registros e Evidências (monitor), Salvar (resumo da lei), Abrir PDF (boletim). | arquivos citados na seção 11 |

## 11. Cores: verde ou normal

O "verde" do app é o estilo de destaque (`PmvniBotaoPrimarioStyle`, `PmvniFaixaMenuPrimarioStyle`, `AccentButtonStyle`) e o tonal (`PmvniBotaoTonalStyle`): os dois saem verde suave (`Recursos/IconesModulos.xaml:39-58`). Desabilitado, o verde vira cinza.

**Aplicado na versão 5.1.4**, com uma exceção pedida: **Emitir processo aprovado… continua verde** (aba Arquivo de Relatórios).

### Regras

1. **Um verde à vista por vez** na janela.
2. **Verde é a ação que cria o trabalho do módulo** (primário da faixa) **ou que conclui o fluxo aberto** (assistente, janela secundária, diálogo).
3. **Com um fluxo aberto, o verde do fluxo vence**: o primário da faixa passa a normal enquanto o fluxo estiver na tela.
4. **Tela vazia**: verde só se o botão for a única saída e a faixa não tiver a mesma ação em verde.
5. **Nunca verde**: Cancelar, Excluir, Descartar, Fechar, Voltar, navegação, zoom, exibição, filtros, abrir pasta ou Explorador, configurar.
6. **Ajustes grava sozinho**: verde só para ação que conclui algo (Salvar dados, Reiniciar agora).

### Faixa de opções

| Módulo | Verde | Hoje | Passa a normal |
|---|---|---|---|
| Relatórios | **Novo relatório**; **Emitir processo aprovado…** (exceção) | verde | Retomar análise (hoje verde); Abrir… |
| Despachos | **Novo despacho ▾** | verde | |
| Processos | **Novo relatório** | verde | Novo processo (já normal) |
| Legislação | **Anexar PDFs…** | verde | Consultar outras leis (já normal) |
| Imóveis | nenhum na faixa: o verde é Pesquisar, na página | Novo processo verde | Novo processo (fica normal ou sai daqui) |
| Acompanhamento | **Novo processo** | verde | |
| Essencial, GPI, Alfresco | nenhum | nenhum | |
| Ajustes | nenhum | nenhum | |

### Páginas

| Página | Verde | Normal (hoje verde) |
|---|---|---|
| Relatórios | só o primário da faixa | Salvar como cópia, Recompor (avisos); Ajustar o item com a IA, Adicionar ambiente… (Propriedades); Restaurar item (etiqueta nas folhas); Novo relatório da tela vazia |
| Novo relatório, formulário | **Analisar com o ChatGPT** (e o primário da faixa vira normal, regra 3) | Retomar análise; Importar relatório pendente (verde só quando ele é a única saída do aviso, com a análise já entregue) |
| Novo relatório, monitor | nenhum, ou **Importar relatório** quando a entrega fica pronta | Nova análise, Ver registros, Evidências por PDF e página |
| Despachos | só o primário da faixa | |
| Novo despacho | **Gerar e conferir** (passos 1-2) → **Gravar na pasta do processo** (passo 3), um de cada vez; o primário da faixa vira normal | Botão de enviar a instrução à IA |
| Processos | só o primário da faixa | |
| Legislação | só o primário da faixa | Anexar PDFs… da tela vazia (repete a faixa); Salvar do resumo (fica verde só dentro do flyout, que é um diálogo) |
| Imóveis, pesquisa | **Pesquisar** | Configurar pasta QGIS da tela vazia; Relatório e Gerar boletim… de cada linha (hoje um verde por linha) |
| Imóveis, relatório | **Salvar PDF…** | Abrir PDF do aviso do boletim |
| Acompanhamento | só o primário da faixa; **Escolher planilha…** na tela vazia (o primário está cinza nesse caso) | Editar processo… (painel) |
| Ajustes | **Salvar dados** (Profissional); **Reiniciar agora** (quando aparece) | Substituir arquivo…, Escolher arquivo…, Alterar… (pasta e kit), Selecionar pasta…, Selecionar planilha…, Escolher… (LibreOffice), Verificar atualizações, Copiar informações, Atualizar diagnóstico, Anexar PDFs… (tonal) |

### Janelas secundárias e diálogos

| Janela | Verde | Normal |
|---|---|---|
| Editar figura | **Concluir** | todo o resto |
| Inserir região da prancha | **Inserir no relatório** | Escolher arquivo… do centro (hoje verde), Cancelar |
| Emitir processo aprovado | **Confirmar prancha** (marcação) / **Aceitar prancha** (revisão) | Posicionar carimbo, Adicionar observações…, Desfazer |
| Imprimir | **Imprimir…** | Fechar |
| Diálogos (Usar esta versão, Aplicar, Adicionar, Emitir…) | botão principal do diálogo | Cancelar |
| Diagrama (pedido à IA) | **Aplicar** (é um painel de pedido; só ele na área) | |
