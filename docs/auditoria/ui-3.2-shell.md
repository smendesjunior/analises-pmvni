# Auditoria de interface 3.2 — SHELL

## Inventário anterior à revisão

A árvore nativa está em `MainWindow.xaml`, nos componentes em `Controles` e nos parciais da janela. A faixa é gerada por `Faixas.Para`. Entradas dependentes de dados são inventariadas por modelo. A barra de estado pertence a Relatórios, em `PaginaRelatorios.xaml`. A entrada Sobre abre Ajustes › Sobre e suporte. A licença é um diálogo separado.

| Superfície / elemento em ordem visual | Antes da revisão | Resultado |
|---|---|---|
| Marca da Prefeitura / Análise de Projetos | Identidade em SVG claro/escuro de 79,2 px, imagem Raw e nome acessível no contêiner | Conforme como identidade institucional |
| Processo aberto / requerente | Título de painel 15 px bold e ajuda 12 px Secondary avulsos. Nome e dica dinâmicos | PmvniAjusteTitulo e PmvniSecondaryTextStyle, respectivamente. |
| Selo Salvo / Salvando / Não salvo / Conflito / Somente leitura | Texto 13 px, ícone 14 px (E930/E895/E7BA/EA39/E72E), tons por recurso, nome/dica dinâmicos e anúncio Polite | Caption secundária e glifo de 12 px, com os mesmos cinco estados e anúncio. |
| Análise em andamento / concluída / pausada / erro | Único botão da área, estilo Barra, nome e dica. Título semibold e detalhe de 12 px avulsos. ProgressRing nativo, E930 18, E769 16, EA39 18 | Título e detalhe pelos estilos comuns. Glifos de 16 px e cores por ThemeResource. |
| Navegação — Anterior, Próxima | Rótulo de grupo maiúsculo em PmvniPainelRotulo. Botões de grupo padrão, nomes Página anterior/Próxima página, setas literais E76B/E76C de 12 px e dica inicial ausente | Navegação em caixa de frase, Voltar/Avançar a 16 px, ícone à esquerda, modo compacto 32×32 e dicas com o motivo. Botões mantêm nomes e posição. |
| Pesquisa global — caixa, Ctrl + K, resultado | Grupo maiúsculo em PmvniPainelRotulo. Caixa com nome/dica explícitos, atalho Caption, resultado Secondary. E721 literal, 15 px, Secondary avulso | Pesquisa global em caixa de frase. Pesquisar a 16 px pelo catálogo. Atalho e resultado usam estilo secundário. O foco e seu anúncio continuam no campo. |
| Edição — Desfazer, Refazer | Grupo maiúsculo em PmvniPainelRotulo. Botões de grupo com nomes e dicas Ctrl+Z/Ctrl+Y. E7A7/E7A6 literais, 16 px, sem motivo quando desabilitados | Edição em caixa de frase e Desfazer/Refazer a 16 px, espaçamento 8 px, modo compacto 32×32, motivo quando indisponíveis. |
| Rodapé Relatórios — Menos, Mais, Ajustar largura, Página inteira, avisos, composição e folhas | Menos/Mais na barra, 32 px e nomes explícitos. Ajustar largura/Página inteira com rótulo. Dicas da janela pelo mapa. Texto de estado e contador são da página | Glifos Diminuir/Aumentar de 16 px no controle efetivo. Nomes e dicas explícitos nos quatro botões. Slider e NumberBox nativos. |
| Atalhos de teclado | ContentDialog nativo, título e Fechar nativos, linhas de comando Body e teclas Secondary. Mapa contextual com alias Ctrl+K omitido | Ctrl+K e Ctrl+F aparecem juntos na linha de pesquisa. Os accelerators continuam registrados na raiz. |
| Ajuda › Sobre / Sobre e suporte | Suporte, Diagnóstico, Sobre, créditos e controles ficam em PaginaAjustes. Contato, Copiar informações, Atualizar diagnóstico e Licença do PDF.js ligados por MainWindow.Sobre | Contato ganhou dica e a confirmação de cópia passou a ser anunciada. Inventário da categoria na tabela própria. |
| Licença do PDF.js | ContentDialog nativo, título e Fechar nativos, texto selecionável de 12 px avulso | Texto pelo estilo secundário. MenuTexto para Selecionar tudo e Copiar. Título, seleção e Fechar permanecem nativos. |
| Pesquisa — Arquivos, Processos, Legislação, Configurações, Funções e Módulos | Cabeçalhos de lista em maiúsculas, 12 px semibold e Secondary avulsos. Heading nível 2 | PmvniPainelCartaoTitulo, caixa de frase e heading preservado. |
| Pesquisa — Procurando… / Nada encontrado | Ajuda 12 px avulsa, sem heading | PmvniSecondaryTextStyle, sem heading. |
| Pesquisa — título, categoria, local | Título Body com negrito para ocorrência. Categoria 11 px e local 12 px Secondary avulsos. Glifo de tipo/comando 16 px com Secondary avulso. Dica e nome completo na linha | PmvniAjusteTitulo no nome e PmvniSecondaryTextStyle no local/categoria. Glifo de 16 px no estilo de ícone secundário, coerente com faixa e trilho. |
| Pesquisa — instrução e moldura | 12 px Tertiary avulso, ↑↓/Enter/Esc e aviso de procura. Border e ThemeShadow nativa. Setas, Enter, ponteiro e clique | PmvniSecondaryTextStyle nas instruções. Moldura e elevação nativas. Teclas e seleção preservadas. |
| EstadoVazio — identidade, título e frase | Ícone de módulo 48 px, título Subtitle, ajuda Secondary | PmvniTituloPaginaStyle com heading e frase secundária. Ilustração de identidade de 48 px. |
| EstadoVazio — dois botões | Ambos tonais, 16 px e 8 px entre ícone e texto, dica central e nome pelo rótulo. Glifo do comando, secundário usa Abrir quando omitido | Primeiro em PmvniBotaoPrimarioStyle, segundo padrão. Altura uniforme 36 px, dica/nome e glifos de 16 px. |
| EstadoVazio — Recentes | RECENTES Caption com peso/cor avulsos. Título do arquivo semibold avulso e pasta Caption com opacidade. Botão padrão com Relatorio 20 e E76C 12, nome Abrir + arquivo e dica com caminho | Recentes em PmvniTituloSecao com heading e ícone 16 px. Nome em PmvniAjusteTitulo, pasta secundária. Glifos de 16 px. |
| SeloEstado | Texto de estado dinâmico, nome no selo, E73E/E711 literais de 11 px. Tipografia e brushes avulsos. Sem ações | Marca/Não configurado a 12 px via estilo do selo, com estilos de texto e cores por tema. |
| EtiquetaSituacao | Botão de situação com menu fornecido pela página. Ponto compacto 10 px e completo 7 px, caixa alta 10,5 px bold, nome/dica pelo estado. Cores do kit em ARGB claro/escuro, sem alternativa de alto contraste | Mesma classificação do kit e mesmo nome/dica, com tipografia de estilo e cores semânticas do tema. |
| IndicadorAvisos | DropDownButton padrão, resumo dinâmico com nome e dica, E7BA literal 14 px. Flyout de texto e links Ir à folha N com nome/dica. Sem atalhos | Avisos a 16 px e resumo por estilos semânticos. Links e comportamento da lista preservados. |
| AreaDeAvisos | InfoBars e botão Fechar nativos. Link +n avisos/Mostrar menos no fim, sem ícone ou dica e com nome dinâmico | O link agora tem dica de expandir/recolher, e mantém o nome dinâmico. Fechar nativo. |
| Pilulas | ToggleButton e AppBarToggleButton nativos. Nome, dica, ação e ícone vêm do consumidor. Primeiro tipo habilita Brilho | Estados nativos, sem o brilho do ToggleButton comum. |
| MiniaturaArquivo | Imagem nativa de 104 px ou ícone de tipo fornecido pelo Windows com reserva Fluent de 16 px. Visual Raw e nome/dica no bloco de arquivo | Conforme para prévia de conteúdo |
| MenuTexto | Copiar/Copiar parágrafo, Ctrl+C e Glifos.Copiar. Selecionar tudo, Ctrl+A e E8B3 literal. Sem separador | Glifos SelecionarTudo e Copiar a 16 px, com separador entre edição e cópia. Ctrl+A/Ctrl+C continuam nativos no texto. |

## Trilho lateral

Todos os botões usam PmvniTrilhoItemStyle, texto PmvniTrilhoRotuloStyle (variante ativa em semibold), fundo e indicador de seleção pelo tema. Dica por comando, nome pelo módulo e status “Selecionado” no ativo. Um ponto de Tab e setas nativas. Antes todos usavam Canvas/Viewbox de 30 px com preenchimentos fixos. A nova escala do trilho é 20 px.

| Posição | Botão / nome acessível | Dica e tecla | Glifo aplicado |
|---:|---|---|---|
| 1 | Relatórios | Abrir Relatórios, Ctrl+1 | Relatorio E8A5 |
| 2 | Despachos | Abrir Despachos, Ctrl+2 | Despacho E715 |
| 3 | Processos | Abrir Processos, Ctrl+3 | Pasta E8B7 |
| 4 | Legislação | Abrir Legislação, Ctrl+4 | Livro E82D |
| 5 | Acompanhamento (rótulo em duas linhas) | Acompanhamento de processos, sem Ctrl numérico | Grafico EB05 |
| 6 | Imóveis | Consulta à base QGIS, sem Ctrl numérico | Mapa E707 |
| 7 | Essencial | Processos e protocolos, sem Ctrl numérico | Globo E774 |
| 8 | GPI | Gestão pública integrada, sem Ctrl numérico | GestaoMunicipal EC06 |
| 9 | Alfresco | Documentos de Obras, sem Ctrl numérico | Documento E7C3 |
| 10, no pé | Ajustes | Abrir Ajustes, Ctrl+5 | Engrenagem E713 |

Key tips de Alt vêm de Modulos.Numero. Acompanhamento é o décimo do enum e conserva o key tip 10. Ajustes é o quinto do enum e conserva o key tip 5. O Narrador agora anuncia Ctrl+1…5 só nos cinco módulos com accelerator registrado.

## Sobre e suporte em Ajustes

| Área | Inventário de títulos, textos e ações | Resultado |
|---|---|---|
| Suporte | Título PmvniTituloSecao com E897 a 16 px. Contato, Relatar um problema, Atalhos de teclado e Restaurar os ajustes usam PmvniAjusteTitulo/PmvniAjusteAjuda. Link de e-mail por nome explícito. Na área de relato, Copiar informações primário e Abrir pasta de dados padrão, ambos sem glifo e com dica. Ver atalhos e Restaurar… padrão, sem glifo, com dica. | Dica no contato. TextoCopiado anunciado. Posições preservadas. |
| Diagnóstico | Título PmvniTituloSecao com E9D9 a 16 px e dois textos de orientação PmvniAjusteAjuda. Atualizar diagnóstico primário, Copiar diagnóstico padrão, ambos sem glifo e com dica. Saída selecionável em fonte monoespaçada a 12,5 px. | XAML da frente Ajustes. |
| Sobre | Título PmvniTituloSecao com E946 a 16 px, imagem da marca, nome do editor de 20 px semibold avulso, versão em PmvniAjusteAjuda, descrição selecionável. Desenvolvedor, Direitos autorais, Sistema, Runtime, Pasta de dados, Tecnologias, Ícones e Componentes usam PmvniAjusteTitulo. Ajuda de Pasta de dados, Ícones e Componentes usa PmvniAjusteAjuda. Licença do PDF.js é botão padrão, sem ícone, com dica. | XAML da frente Ajustes. Diálogo da licença tratado nesta frente. |

## Regras comuns da faixa

- A coluna de posição reinicia em cada aba. Os dois primeiros botões tonais, quando presentes, ocupavam a mesma área. O primeiro usa agora PmvniBotaoPrimarioStyle ou sua variante para DropDownButton nativo. O segundo usa o estilo padrão. Ambos têm 32 px de altura, com 8 px entre eles.
- Comandos comuns e de alternância usam AppBarButton e AppBarToggleButton. Ícones em 16 px na faixa atual, inclusive no menu de excedentes. Cada item tem nome e dica da tabela DicasComandos. Motivos específicos da janela são mantidos e, na ausência de detalhe, a dica indica indisponibilidade no contexto atual.
- O menu nativo Mais comandos contém o subconjunto variável de comandos que não cabe na faixa. Herda texto, ícone, estado e accelerator da tabela. A ação perigosa fica no fim das abas Página Inicial de Relatórios e Acompanhamento. Em Figuras, Excluir figura já estava no fim. Nenhuma aba ou módulo mudou de posição.
- As opções de cada MenuFlyout abaixo pertencem ao mesmo grupo. Opções selecionáveis usam a marca do controle nativo. Os atalhos continuam centralizados no mapa da janela e são exibidos pelo próprio item quando houver correspondência; os itens de menu definidos hoje não têm atalho próprio no mapa.

## Faixa de opções por módulo e aba

### Relatórios

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| Fixo 1 | Novo relatório | Tonal | Adicionar E710 | — | Novo relatório | Regra comum |
| Fixo 2 | Abrir | Tonal | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| 3 | Salvar | Padrão | Salvar E74E | Ctrl+S | Salvar | Regra comum |
| 4 | Salvar como | Padrão | SalvarComo E792 | Ctrl+Shift+S | Salvar como | Salvar como… |
| 5 | Emitir Processo Aprovado | Padrão | Exportar EA90 | — | Emitir Processo Aprovado | Emitir processo aprovado… / EmitirProcessoAprovado |
| 6 | Exportar PDF | Padrão | Exportar EA90 | Ctrl+E | Exportar PDF | Regra comum |
| 7 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| 8 | Fechar | Padrão | Fechar E8BB | — | Fechar | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Situação | Padrão + menu | Situacao E73E | — | Situação | Situacao EA81 |
| 2 | Novo item | Padrão | NovoItem ECC8 | — | Novo item | Regra comum |
| 3 | Excluir item | Padrão | Excluir E74D | Del | Excluir item | Último da aba. Restauração mostra Restaurar |
| 4 | Editar texto | Padrão | Editar E70F | F2 | Editar texto | Regra comum |
| 5 | Original | Padrão · alternância | Original E8AB | — | Original | Regra comum |
| 6 | Restaurar | Padrão | Desfazer E7A7 | — | Restaurar o original | Restaurar |
| 7 | Item anterior | Padrão | Anterior E70E | Alt+↑ | Item anterior | Regra comum |
| 8 | Próximo item | Padrão | Proximo E70D | Alt+↓ | Próximo item | Regra comum |
| 9 | Próximo pendente | Padrão | Pendente E7C1 | — | Próximo pendente | Regra comum |
| 10 | Figuras | Padrão + menu | Figuras EB9F | — | Abrir uma figura no editor de figuras | Regra comum |
| 11 | Novo despacho | Padrão + menu | Editar E70F | — | Novo despacho | NovoDespacho |
| — | **Revisão** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Marcas de revisão | Padrão · alternância | MarcasRevisao E70B | — | Marcas de revisão | Regra comum |
| 2 | Somente alterados | Padrão · alternância | Alterados E71C | — | Somente alterados | Alterados E8BC |
| 3 | Conferência | Padrão | Conferir E9D5 | — | Conferência | Regra comum |
| 4 | Comparar | Padrão | Original E8AB | — | Comparar com o original | Comparar |
| — | **Figuras** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Inserir região da prancha | Padrão | E7A8 literal | — | Inserir no relatório uma região de uma prancha do projeto (PDF) | Recortar |
| 2 | Editar figura | Padrão | Editar E70F | — | Editar as marcações da figura selecionada (setas, regiões, textos) | Regra comum |
| 3 | Excluir figura | Padrão | Excluir E74D | — | Tirar do relatório a figura selecionada | Regra comum |
| — | **Exibir** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Diminuir | Padrão | Diminuir E71F | Ctrl+− | Diminuir | Regra comum |
| 2 | Aumentar | Padrão | Aumentar E8A3 | Ctrl++ | Aumentar | Regra comum |
| 3 | Ajustar largura | Padrão | Ajustar E745 | Ctrl+0 | Ajustar largura | Regra comum |
| 4 | Página inteira | Padrão | PaginaInteira E9A6 | — | Página inteira | Regra comum |
| 5 | Painel lateral | Padrão · alternância | Painel E8A0 | — | Painel lateral | Regra comum |
| 6 | Propriedades | Padrão · alternância | Propriedades E7C4 | — | Propriedades | Regra comum |
| 7 | Tema | Padrão + menu | Tema E790 | — | Tema | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Despachos

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| Fixo 1 | Novo despacho | Tonal + menu | Editar E70F | — | Novo despacho | NovoDespacho |
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Abrir | Padrão | Abrir E8E5 | — | Abrir | Abrir… |
| 2 | Abrir PDF | Padrão | Exportar EA90 | — | Abrir PDF | Abrir |
| 3 | Gerar PDF | Padrão | Exportar EA90 | — | Gerar PDF | Regra comum |
| 4 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| 5 | Mostrar no Explorador | Padrão | Explorador E838 | — | Mostrar no Explorador | Explorador E8F1 |
| 6 | Ir ao processo | Padrão | IrPara E72A | — | Ir ao processo | IrPara E8AD |
| 7 | Atualizar lista | Padrão | Atualizar E72C | — | Atualizar lista | Regra comum |
| 8 | Tipo | Padrão + menu | Filtro E71C | — | Tipo | Regra comum |
| 9 | Só do processo | Padrão · alternância | Pasta E8B7 | — | Só os despachos do processo escolhido | Regra comum |
| — | **Exibir** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Agrupar | Padrão + menu | Lista E8FD | — | Agrupar | Regra comum |
| 2 | Ordenar | Padrão + menu | Ordenar E8CB | — | Ordenar | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Processos

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| Fixo 1 | Novo relatório | Tonal | Adicionar E710 | — | Novo relatório | Regra comum |
| Fixo 2 | Novo processo | Tonal | Adicionar E710 | — | Novo processo | Regra comum |
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Abrir relatório | Padrão | Abrir E8E5 | — | Abrir relatório | Regra comum |
| 2 | Importar análise | Padrão | ImportarAnalise E896 | — | Importar a análise da IA | Regra comum |
| 3 | Novo despacho | Padrão + menu | Editar E70F | — | Novo despacho | NovoDespacho |
| 4 | Abrir arquivo | Padrão | Abrir E8E5 | — | Abrir arquivo | Regra comum |
| 5 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| 6 | Abrir pasta | Padrão | Explorador E838 | — | Abrir a pasta no Explorador | AbrirPasta |
| 7 | Atualizar | Padrão | Atualizar E72C | — | Atualizar | Regra comum |
| 8 | Ano | Padrão + menu | Ano E787 | — | Ano | Regra comum |
| — | **Exibir** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Ordenar | Padrão + menu | Ordenar E8CB | — | Ordenar | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Legislação

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| Fixo 1 | Anexar PDFs… | Tonal | Anexar E723 | — | Anexar PDFs… | Regra comum |
| Fixo 2 | Consultar outras leis | Tonal | Pesquisar E721 | — | Consultar outras leis | Regra comum |
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Gerenciar anexos | Padrão | Gerenciar E713 | — | Gerenciar anexos | Gerenciar E8A9 |
| 2 | Importar análise da IA… | Padrão | Importar E8B5 | — | Importar análise da IA… | Regra comum |
| 3 | Abrir | Padrão | Abrir E8E5 | — | Abrir | Abrir… |
| 4 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Copiar | Padrão | Copiar E8C8 | Ctrl+C | Copiar | Regra comum |
| 2 | Copiar citação | Padrão | Citacao E9B2 | Ctrl+Shift+C | Copiar citação | Regra comum |
| 3 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| 4 | Página anterior | Padrão | Anterior E70E | Alt+↑ | Página anterior | Regra comum |
| 5 | Próxima página | Padrão | Proximo E70D | Alt+↓ | Próxima página | Regra comum |
| 6 | Pesquisar em | Padrão + menu | Pesquisar E721 | — | Pesquisar em | Regra comum |
| 7 | Ocorrência anterior | Padrão | Ocorrencia E74A | Shift+F3 | Ocorrência anterior | Regra comum |
| 8 | Próxima ocorrência | Padrão | OcorrenciaProxima E74B | F3 | Próxima ocorrência | Regra comum |
| — | **Exibir** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Diminuir | Padrão | Diminuir E71F | Ctrl+− | Diminuir | Regra comum |
| 2 | Aumentar | Padrão | Aumentar E8A3 | Ctrl++ | Aumentar | Regra comum |
| 3 | Ajustar largura | Padrão | Ajustar E745 | Ctrl+0 | Ajustar largura | Regra comum |
| 4 | Página inteira | Padrão | PaginaInteira E9A6 | — | Página inteira | Regra comum |
| 5 | Marcadores | Padrão · alternância | Marcadores E8A4 | — | Marcadores | Regra comum |
| 6 | Lista de leis | Padrão · alternância | Lista E8FD | — | Lista de leis | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Acompanhamento

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| Fixo 1 | Novo processo | Tonal | Adicionar E710 | — | Novo processo | Regra comum |
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Editar processo | Padrão | Editar E70F | — | Editar processo | Regra comum |
| 2 | Excluir processo | Padrão | Excluir E74D | — | Excluir processo | Último da aba |
| 3 | Detectar local e autor | Padrão | Conferir E9D5 | — | Detectar local e autor | Regra comum |
| 4 | Ler a planilha de novo | Padrão | Atualizar E72C | — | Ler a planilha de novo | Regra comum |
| 5 | Abrir no Excel | Padrão | Abrir E8E5 | — | Abrir no Excel | Regra comum |
| 6 | Escolher planilha… | Padrão | Importar E8B5 | — | Escolher planilha… | Regra comum |
| 7 | Local da planilha | Padrão | Pasta E8B7 | — | Local da planilha | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Imóveis

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| Fixo 1 | Novo processo | Tonal | Adicionar E710 | — | Novo processo | Regra comum |
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Voltar à pesquisa | Padrão | Voltar E72B | — | Voltar à pesquisa | Regra comum |
| 2 | Salvar PDF | Padrão | Exportar EA90 | — | Salvar PDF | Regra comum |
| 3 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir | Regra comum |
| 4 | Ler a base de novo | Padrão | Atualizar E72C | — | Ler a base de novo | Regra comum |
| 5 | Pasta QGIS | Padrão | Pasta E8B7 | — | Pasta QGIS | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Essencial

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Voltar | Padrão | Voltar E72B | — | Voltar | Regra comum |
| 2 | Avançar | Padrão | Avancar E72A | — | Avançar | Regra comum |
| 3 | Atualizar | Padrão | Atualizar E72C | — | Atualizar | Regra comum |
| 4 | Página inicial | Padrão | Inicio E80F | — | Página inicial | Regra comum |
| 5 | Abrir no navegador | Padrão | AbrirEmOutraJanela E8A7 | — | Abrir no navegador | Regra comum |
| 6 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### GPI

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Voltar | Padrão | Voltar E72B | — | Voltar | Regra comum |
| 2 | Avançar | Padrão | Avancar E72A | — | Avançar | Regra comum |
| 3 | Atualizar | Padrão | Atualizar E72C | — | Atualizar | Regra comum |
| 4 | Página inicial | Padrão | Inicio E80F | — | Página inicial | Regra comum |
| 5 | Abrir no navegador | Padrão | AbrirEmOutraJanela E8A7 | — | Abrir no navegador | Regra comum |
| 6 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Alfresco

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| — | **Arquivo** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Importar análise da IA… | Padrão | Importar E8B5 | Ctrl+I | Importar análise da IA… | Regra comum |
| 2 | Abrir | Padrão | Abrir E8E5 | Ctrl+O | Abrir | Abrir… |
| 3 | Recentes | Padrão + menu | Recentes E81C | — | Recentes | Regra comum |
| — | **Página Inicial** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Voltar | Padrão | Voltar E72B | — | Voltar | Regra comum |
| 2 | Avançar | Padrão | Avancar E72A | — | Avançar | Regra comum |
| 3 | Atualizar | Padrão | Atualizar E72C | — | Atualizar | Regra comum |
| 4 | Página inicial | Padrão | Inicio E80F | — | Página inicial | Regra comum |
| 5 | Abrir no navegador | Padrão | AbrirEmOutraJanela E8A7 | — | Abrir no navegador | Regra comum |
| 6 | Imprimir | Padrão | Imprimir E749 | Ctrl+P | Imprimir ou salvar como PDF | Regra comum |
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Ajustes

| Posição anterior | Rótulo anterior | Estilo anterior | Glifo anterior | Atalho | Nome acessível e título-base da dica | Resultado adicional |
|---:|---|---|---|---|---|---|
| — | **Ajuda** | SelectorBarItem | — | — | Nome nativo pela aba | Nome explícito e dica da aba |
| 1 | Atalhos de teclado | Padrão | Atalhos E765 | — | Atalhos de teclado | Regra comum |
| 2 | Sobre | Padrão | Sobre E946 | — | Sobre | Regra comum |

### Menus de abertura por botão da faixa

| Menu | Itens anteriores em ordem (glifo e atalho) | Separadores e resultado |
|---|---|---|
| Recentes | Nome do relatório recente e dica com caminho. Se vazio, Nenhum item. Sem glifo ou atalho | Sem separador. Nome explícito e dica, inclusive no estado vazio |
| Situação | ATENDE (SituacaoItem EA3B; —) · NÃO ATENDE (SituacaoItem EA3B; —) · FALTA COMPROVAR (SituacaoItem EA3B; —) · INFORMAÇÕES DIVERGENTES (SituacaoItem EA3B; —) | Sem separador. Todos usam SituacaoItem a 16 px. Nome e dica explícitos |
| Figuras | Figuras do relatório aberto. Se vazio, Nenhum item. Sem glifo ou atalho | Sem separador. Nome explícito e dica, inclusive no estado vazio |
| Novo despacho | Correção (—; —) · Correção automática (itens pendentes do relatório) (—; —) · Aprovação (—; —) | Sem separador. Alternativas do mesmo comando, todas sem ícone. Nome e dica explícitos |
| Tema | Acompanhar o Windows (—; —) · Claro (—; —) · Escuro (—; —) | Sem separador. Opções sem glifo de ação; seleção marcada pelo controle nativo. Nome e dica explícitos |
| Tipo | Todos (—; —) · Correção (—; —) · Aprovação (—; —) | Sem separador. Opções sem glifo de ação; seleção marcada pelo controle nativo. Nome e dica explícitos |
| Agrupar | Por processo (—; —) · Por data (—; —) | Sem separador. Opções sem glifo de ação; seleção marcada pelo controle nativo. Nome e dica explícitos |
| Ordenar | Mais recentes primeiro (—; —) · Mais antigos primeiro (—; —) | Sem separador. Opções sem glifo de ação; seleção marcada pelo controle nativo. Nome e dica explícitos |
| Ano | Anos das pastas. Se vazio, Nenhum item. Sem glifo ou atalho | Sem separador. Nome explícito e dica, inclusive no estado vazio |
| Ordenar | Número (—; —) · Data (—; —) · Requerente (—; —) | Sem separador. Opções sem glifo de ação; seleção marcada pelo controle nativo. Nome e dica explícitos |
| Pesquisar em | Nesta lei (—; —) · Em todas as leis (—; —) | Sem separador. Opções sem glifo de ação; seleção marcada pelo controle nativo. Nome e dica explícitos |

## Colisões no catálogo

| Nomes | Antes | Resultado |
|---|---|---|
| Alterados / Filtro | E71C / E71C | E8BC / E71C |
| Explorador / AbrirPasta | E838 / E838 | E8F1 / E838 |
| Engrenagem / Gerenciar | E713 / E713 | E713 / E8A9 |
| Marca / Situacao | E73E / E73E | E73E / EA81 |
| Avancar / IrPara | E72A / E72A | E72A / E8AD |

Os valores originais continuam no comando ao qual a imagem correspondia. A faixa aplica os ajustes de ação no ponto de apresentação, preservando as definições compartilhadas por outras frentes. A pesquisa aplica a mesma associação. O teste do catálogo confere a unicidade de todas as constantes públicas.

## Folha de hierarquia

| Exemplo em Relatórios | Referência confirmada |
|---|---|
| Relatórios, Recentes, Processo aberto, ajuda do processo | PmvniTituloPaginaStyle (24), PmvniTituloSecao (20 mais ícone de 16), PmvniAjusteTitulo (BodyStrong), PmvniSecondaryTextStyle (Caption) |
| Novo relatório, Abrir…, Desfazer compacto | Button de destaque, Button padrão e botão de cabeçalho 32×32. Ações Adicionar, Abrir e Desfazer do catálogo |
| Situação | MenuFlyout, quatro MenuFlyoutItem, todos com SituacaoItem EA3B a 16 px. As quatro situações vêm do kit. |
| Trilho, ação/menu e selo | Relatorio E8A5 a 20, Pesquisar E721 a 16 e Concluido E930 a 12 |

O Brilliant recebeu as camadas editáveis da folha. A fonte Segoe Fluent e os códigos foram conferidos no catálogo do repositório e do Windows. Ícones Phosphor, controles web, fontes externas e valores de cor próprios foram descartados porque não representam a implementação WinUI. O link abre a folha conceitual, sem redefinir a grade da janela.

## Pendências e pontos de junção

- A tipografia avulsa do nome do produto e do relatório monoespaçado em Sobre continua em PaginaAjustes.xaml, de outra frente. A página fornece os rótulos, os estilos das ações e os metadados da área.
- Os rótulos e ícones canônicos em Faixas.cs e as entradas de menu que dependem de outras páginas continuam compartilhados. Os ajustes de apresentação desta frente entram em FaixaDeOpcoes e na pesquisa. A cor de destaque padrão do Windows volta a valer para toda a aplicação após a remoção das substituições em App.xaml.
- Estilo para promover quando outra tela precisar da mesma solução: PmvniFaixaMenuPrimarioStyle, variante do destaque que mantém o template nativo de DropDownButton.

## Balanço dos problemas

Cada linha agrupa ocorrências com a mesma correção. As cinco colisões foram contadas separadamente.

| Área | Identificados | Corrigidos |
|---|---:|---:|
| Cabeçalho, barra de estado, atalhos e Sobre (incluindo licença) | 10 | 10 |
| Lista da pesquisa global | 4 | 4 |
| Controles comuns e formato das dicas | 10 | 10 |
| Faixa e menus | 8 | 8 |
| Trilho | 2 | 2 |
| Substituições fixas de destaque no App | 1 | 1 |
| Colisões do catálogo | 5 | 5 |
| Tipografia do nome do produto e da saída monoespaçada no XAML de Ajustes | 2 | 0 |
| **Total** | **42** | **40** |

## Verificação

- Build da solução WinUI sem erros. Na compilação inicial foram identificados dois avisos xUnit1031 preexistentes. A origem NuGet e a escrita em bin/obj foram liberadas para a verificação.
- Suíte Pmvni.Revisao.Testes em x64: 1.075 aprovados, 2 ignorados e 0 falhas. Inclui a regressão de unicidade do catálogo.
- Autotestes nativos com perfis isolados retornaram ok em Relatórios e em Despachos com tema escuro. O teste de Despachos detectou um recurso de estilo indisponível no primeiro ensaio. O estilo foi ajustado para usar o template padrão do controle e o teste passou. A árvore XAML e os nomes existentes foram conferidos contra HEAD. A reprodução visual completa em alto contraste não foi executada.
- O Brilliant armazenou as duas edições com texto, grupos, menu e amostras. A renderização depende da abertura da folha no cliente e não foi confirmada.

- Intermediários de build e perfis isolados desta tarefa foram enviados à Lixeira após a verificação de processos, atalhos, serviços, tarefas e início automático.

## Referências

- `PaginaAjustes.xaml`, `Estilos.xaml`, `docs/auditoria/ajustes-3.1.md`, comandos de `Faixas.cs` e componentes da moldura.
- [Segoe Fluent Icons, Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font).
- [Folha de hierarquia no Brilliant](https://brilliant.design/playground?seed=cg-4hn6gd7c7m4qnxpvg9lw).

