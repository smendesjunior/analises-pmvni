# Roteiro da tela (Etapa 2)

A partir da Tarefa 8 a janela é um **shell**: barra de título própria (`TitleBar` do Windows App SDK 1.8, com ícone, título
"Proc. NNNN/AAAA · Requerente" e o estado de gravação "Salvo HH:MM" / "Salvando…" / "Não salvo" / "Conflito" / "Somente
leitura"), faixa de opções do módulo ativo (abas num `SelectorBar` e comandos num `CommandBar`, montados de `Faixas.Para`;
rótulos recolhidos com a janela abaixo de 1000 DIP), trilho de módulos à esquerda (64 px, ícone + rótulo, indicador 3 × 16; só os
módulos de `Faixas.Disponiveis`) e as páginas dos módulos, criadas uma vez e trocadas por Visibility (o WebView2 nunca é recriado).
Módulo e aba ativos ficam em `<dados>\ajustes.json`.

Relatórios trabalha sobre o pacote `.pmvrel`: **Arquivo** (Importar dados.json, Abrir, Salvar, Salvar como, Exportar PDF,
Fechar), **Exibir** (−, +, Ajustar largura, Página inteira, Painel lateral) e **Ajuda** (Atalhos de teclado, Sobre). Importar
pede o `dados.json` e depois onde gravar o `.pmvrel` novo, com o nome `Relatorio_NNNN_AAAA.pmvrel` do processo da capa (nunca
sobrescreve um arquivo existente). Na linha de comando, um `.pmvrel` abre e um `dados.json` é importado do mesmo jeito. Exportar é
bloqueado com erros finais (campos `[ ]`, conferência da revisão), pede confirmação com avisos (citações a itens excluídos) e recusa
se o pacote mudou no disco desde a abertura ou a última gravação. O rodapé (zoom, situação da composição, "folha X de Y") continua.

## Como rodar

1. `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` (testes, publicação do executável único em
   `Aplicativo\`, autoteste e limpeza).
2. `powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1`: apaga `tests\saida\roteiro-fim.txt`, abre
   `tools\roteiro.cmd` com o `explorer.exe` (fora do pacote do Claude), espera até 10 min, lê
   `docs\roteiro-etapa-2.resultado.json`, imprime as falhas e sai com 0 só com `ok: true`.
3. O app roda com `--roteiro`: as perguntas (seletores e diálogos) têm respostas fixas e tudo o que o roteiro cria (pacotes,
   PDFs, capturas PNG) fica em `tests\saida\roteiro`; os dados do app (WebView2, `ajustes.json`, pastas de edição) em
   `tests\saida\dados-app`. Nada disso é versionado.

## Passos (`roteiro-etapa-2.resultado.json`)

| # | Passo | O que o roteiro confere |
|---|-------|-------------------------|
| 1 | `1_importar_longo` | `ImportarParaAsync(longo.json, <pasta>\longo.pmvrel)` cria o pacote e abre; as conferências do antigo `1_abrir_longo` (folhas, escala, InfoBar de largura com o texto exato, Exportar habilitado, tela e host oculto InPrivate e sem atalhos do navegador, faixa vermelha nas folhas marcadas, InfoBar de conteúdo além da borda); título "Proc. …" igual a `TituloJanela.De` da capa; estado "Salvo HH:MM"; o `estadoEditor` chegou à página. |
| 2 | `2_rolagem_continua` | Folhas contíguas (297 mm + 8 mm) e "folha X de Y" acompanhando a rolagem. |
| 3 | `3_zoom_nao_recompoe` | `definirZoom`, os botões do rodapé e os comandos da aba Exibir: mesmo texto por folha, mesmos elementos de folha no DOM, mesmo plano recomposto. |
| 4 | `4_navegacao` | Itens da lista (capa, seções, itens) levam à folha certa; o indicador marca o item atual. |
| 5 | `5_exportar_pdf` | Páginas do PDF = folhas da tela; recibo com `sha256_html`, `sha256_pdf` e `sha256_pacote` (o `.pmvrel` em disco); mesmo plano; InfoBar de sucesso. |
| 6 | `6_estouro_recusado` | `estouro.json` importado em `<pasta>`: estouro, InfoBar de erro, Exportar desabilitado, `ExportacaoRecusadaException` sem PDF, faixa vermelha na folha do estouro. |
| 7 | `7_tema_escuro` | Reabre o pacote do longo; claro → escuro muda só o fundo fora das folhas (folhas `rgb(255, 255, 255)`), texto por folha igual. |
| 8 | `8_campos_em_aberto_bloqueiam_exportar` | Cópia do `curto` com `[Nome do requerente]` importada em `<pasta>`: erros finais, Exportar desabilitado com a dica "A exportação está bloqueada…", InfoBar "Exportação bloqueada" aberto com o motivo, `ConferenciaFinalException` sem PDF. |
| 9 | `9_pacote_mudou_recusa_exportar` | Outro `Pacote.Gravar` do mesmo pacote com o requerente trocado, depois de aberto: a exportação recusa com `ExportacaoDivergenteException.ArquivoMudou`, sem PDF nem recibo. |
| 10 | `10_faixa_por_modulo` | `ComandosSemManipulador()` vazio; trilho = `Faixas.Disponiveis`; abas = `Faixas.Para(Relatorios)`; os botões da aba à vista são os da definição; a aba escolhida fica em `ajustes.json`; Painel lateral alterna. |
| 11 | `11_reabrir_pacote` | Fechar deixa a janela sem relatório; `AbrirPacoteAsync` do pacote importado dá o mesmo `Plano.ParaJson()` da importação, estado "Salvo", título "Proc. …". |
| 12 | `12_salvar_duas_vezes_sem_conflito` | Dois Salvar pela faixa e um `GravarAsync` ao mesmo tempo: as gravações vão em fila (`GravadorPacote`), o estado termina "Salvo", nenhum aviso de erro, o disco é o que a sessão gravou e a exportação segue liberada (sem Conflito). |

Testes sem a janela (`tests\Pmvni.Revisao.Testes`): `FaixasTestes` (toda faixa começa em Arquivo e termina em Ajuda, aba padrão,
ids únicos e conhecidos), `AjustesTestes` (lembrar módulo e aba; arquivo corrompido guardado e padrão), `NomesArquivoTestes`,
`GravadorPacoteTestes` (gravações simultâneas sem conflito) e `LogicaAppTestes` (título da janela, estado de gravação).

## O que ainda precisa de olhar humano

- Acabamento da barra de título (ícone, título e estado ao lado dos botões da janela), da faixa e do trilho em outros monitores e
  no tema escuro (as capturas XAML não incluem o Mica).
- Os seletores do Windows (Importar, Abrir, Salvar como, Exportar) e os diálogos (Atalhos, Sobre): o roteiro usa respostas fixas.
