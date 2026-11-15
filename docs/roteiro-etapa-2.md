# Roteiro da tela (Etapa 2)

A partir da Tarefa 8 a janela é um **shell**: barra de título própria (`TitleBar` do Windows App SDK 1.8, com ícone, título
"Proc. NNNN/AAAA · Requerente" e o estado de gravação "Salvo HH:MM" / "Salvando…" / "Não salvo" / "Conflito" / "Somente
leitura"), faixa de opções do módulo ativo (abas num `SelectorBar` e comandos num `CommandBar`, montados de `Faixas.Para`;
rótulos recolhidos com a janela abaixo de 1000 DIP), trilho de módulos à esquerda (64 px, ícone + rótulo, indicador 3 × 16; só os
módulos de `Faixas.Disponiveis`) e as páginas dos módulos, criadas uma vez e trocadas por Visibility (o WebView2 nunca é recriado).
Módulo e aba ativos ficam em `<dados>\ajustes.json`.

Relatórios trabalha sobre o pacote `.pmvrel`: **Arquivo** (Importar dados.json, Abrir, Salvar, Salvar como, Exportar PDF,
Fechar), **Página Inicial** (aba padrão; Situação ▾ com as 4 situações de item do kit, Excluir/Restaurar item, Editar texto,
Original ⇄, Restaurar original, ◂ Item, Item ▸, Próximo pendente, Exportar PDF), **Revisão** (Marcas de revisão, Somente
alterados, Conferência), **Exibir** (−, +, Ajustar largura, Página inteira, Painel lateral, Propriedades) e **Ajuda** (Atalhos de
teclado, Sobre). Importar
pede o `dados.json` e depois onde gravar o `.pmvrel` novo, com o nome `Relatorio_NNNN_AAAA.pmvrel` do processo da capa (nunca
sobrescreve um arquivo existente). Na linha de comando, um `.pmvrel` abre e um `dados.json` é importado do mesmo jeito. Exportar é
bloqueado com erros finais (campos `[ ]`, conferência da revisão), pede confirmação com avisos (citações a itens excluídos) e recusa
se o pacote mudou no disco desde a abertura ou a última gravação. O rodapé (zoom, situação da composição, "folha X de Y") continua.

A partir da Tarefa 9 os itens são revisados no app. Todo comando segue o mesmo fluxo (desenho §8.4): conclui a edição aberta no A4,
testa o comando numa cópia do projeto (`SessaoRevisao.Testar`), prepara o candidato com o kit (`Recompositor.Preparar`; recusa ⇒
InfoBar "A alteração não foi aplicada" com os erros, nada muda), aplica e recompõe; o estado vira "Não salvo". A página recebe
`estadoEditor` sempre (também quando nada foi aplicado). Os comandos (faixa, atalhos, Propriedades, menus, edição no A4, seleção
pela lista) passam por uma fila só: o que chega durante outro espera a vez e é testado contra o projeto de então; nada é descartado,
e um comando indisponível diz por quê ("Comando indisponível"). Clique numa folha seleciona o item (Propriedades e faixa acompanham);
duplo clique ou F2 abre o campo para edição no próprio A4 com o texto efetivo (F2 usa o último campo clicado ou a verificação). O
painel lateral tem **Seções** (ponto na cor da situação e marcador "alterado"; "Somente alterados" filtra), **Excluídos (n)**
(Restaurar em cada linha) e **Histórico** (mais recente primeiro, "14:32 · usuário · …"). **Propriedades** (320 px, recolhível):
número e id, No PDF, situação original e revisada, justificativa (aplica ao sair da caixa ou com Enter), os 5 campos com
"alterado" e Restaurar por campo, Original ⇄ lado a lado (nunca no A4), Restaurar original e o histórico do item. Botão direito
numa folha: Situação ▸, Editar texto, Excluir/Restaurar, Original ⇄, Propriedades; clique na marca "◦ excl. NN": título e
Restaurar. Desfazer e Refazer ficam na barra de título. Conferência mostra os erros finais e os avisos (citações a itens excluídos e
citações literais com a numeração mudada). Os atalhos (`Atalhos.Mapa`, as 16 teclas que a página repassa) valem na janela
(`KeyboardAccelerator`; numa caixa de texto, Delete, Ctrl+Z e Ctrl+Y são dela) e nas folhas (mensagem `atalho`); Ctrl+F só ganha
ação com a pesquisa (Tarefa 11).

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
| 13 | `13_selecionar_e_editar_no_a4` | Clique (CDP) no título do ITEM 03: `Selecionado` e Propriedades "ITEM 03"; `data-campo` nas folhas; duplo clique abre a edição no A4 e Esc cancela; `EditarTextoAsync` muda a verificação (modelo e `TextoPorFolha`); o HTML do `Preparado` não tem `data-campo`; estado "Não salvo"; a lista marca o item como alterado; F2 edita o último campo clicado (título). |
| 14 | `14_situacao_e_justificativa` | ATENDE sem justificativa: `ErrosFinais` tem a mensagem exata da Tarefa 3 ("ITEM 03: a situação mudou de … para ATENDE sem justificativa"), sem repetição; Exportar desabilitado e `ExportarParaAsync` recusa (`ConferenciaFinalException`, sem PDF); a Conferência mostra o erro; com justificativa, nenhum erro final e Exportar liberado. |
| 15 | `15_excluir_e_marca` | Excluir (pela faixa) o ITEM 05: 59 itens; o antigo 06 imprime "ITEM 05"; aba "Excluídos (1)" com 1 linha; marca "◦ excl. 05" na camada fora das folhas; o botão vira "Restaurar item". |
| 16 | `16_desfazer_refazer` | Desfazer 4× (edição, situação, justificativa, exclusão) volta ao mesmo `Plano.ParaJson()` da importação e ao item sem revisão; refazer 4× volta ao plano anterior; 4 registros `desfeito` e 4 `refeito`; o Histórico na tela tem todos os registros, o mais recente primeiro. |
| 17 | `17_salvar_reabrir_exportar` | Ctrl+S (CDP, nas folhas: mensagem `atalho`) salva; fechar e reabrir: mesmas alterações e o histórico com os `desfeito`/`refeito` (cada `Ref` = `N` do comando original, `{tipo, valor}`); o PDF exportado tem as mesmas folhas da tela. |
| 18 | `18_menu_de_contexto_e_atalhos` | Botão direito (CDP) no ITEM 10: o menu abre com 5 entradas; Delete nas folhas exclui; Alt+↓ seleciona o próximo incluído; desfazer; clique na marca "◦ excl." abre o flyout; Somente alterados filtra, Marcas tira e põe a camada, Propriedades recolhe; todo atalho (menos Ctrl+F) e todo comando da faixa têm manipulador. |
| 19 | `19_latencia` | Mediana de 5 comandos de situação no longo em `latencia_ms` (meta 1,5 s; só registra). |
| 20 | `20_comandos_em_sequencia` | Justificativa e, sem esperar, Situação e Excluir pela faixa: os três entram na fila e são aplicados na ordem; uma situação e, sem esperar, um `editar` da página: a edição entra (modelo e folhas); tudo desfeito depois. |

Testes sem a janela (`tests\Pmvni.Revisao.Testes`): `FaixasTestes` (toda faixa começa em Arquivo e termina em Ajuda, aba padrão,
ids únicos e conhecidos), `AjustesTestes` (lembrar módulo e aba; arquivo corrompido guardado e padrão), `NomesArquivoTestes`,
`GravadorPacoteTestes` (gravações simultâneas sem conflito), `LogicaAppTestes` (título da janela, estado de gravação) e
`AtalhosTestes` (as 16 teclas de `Atalhos.Mapa` = o `Set` ATALHOS de `edicao.ts`, todo atalho aponta para um comando conhecido,
Relatórios abre em Página Inicial).

## O que ainda precisa de olhar humano

- Acabamento da barra de título (ícone, título e estado ao lado dos botões da janela), da faixa e do trilho em outros monitores e
  no tema escuro (as capturas XAML não incluem o Mica).
- Os seletores do Windows (Importar, Abrir, Salvar como, Exportar) e os diálogos (Atalhos, Sobre, Conferência): o roteiro usa
  respostas fixas.
- O menu de contexto e o flyout do excluído na posição do clique, o painel de Propriedades (Original ⇄ lado a lado) e os atalhos
  com o teclado de verdade (o roteiro manda as teclas pelo CDP, direto à página; os `KeyboardAccelerator`s da janela só são contados).
