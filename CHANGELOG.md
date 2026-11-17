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

### Processos
- A página do processo ganhou Classificar (nome, data, tipo), Agrupar (etapa, tipo de arquivo, data) e Visualizar (Lista ou Miniaturas). A escolha fica lembrada.
- Em Miniaturas, os arquivos aparecem numa grade como a do Explorador, com a prévia de cada arquivo e grupos que recolhem.
- Botão direito, Enter e duplo clique nos arquivos: Abrir, Mostrar no Explorador, Copiar caminho.

### Imóveis (consulta à base QGIS)
- O mapa de localização segue o do kit: balão com os dados, lote em vermelho, APP, faixas da BR-262, zonas, ruas, rio e confrontantes, com escala e norte.
- O relatório do imóvel tem o aspecto do relatório de análise: faixa do processo, quadros, mapa com legenda ao lado e a ficha em quatro grupos com selos.
- Corrigido: o perímetro da AUC não era lido e o relatório dizia sempre "fora das AUC".
