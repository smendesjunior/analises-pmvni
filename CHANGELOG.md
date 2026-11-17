# Novidades do Editor de Relatórios PMVNI

Cada versão tem a sua seção; o texto da versão publicada aparece no aviso de atualização e na página da Release no GitHub.

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
