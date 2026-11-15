# Roteiro da tela (Etapa 2)

A partir da Tarefa 8 a janela é um **shell**: barra de título própria (`TitleBar` do Windows App SDK 1.8, com ícone, título
"Proc. NNNN/AAAA · Requerente" e o estado de gravação "Salvo HH:MM" / "Salvando…" / "Não salvo" / "Conflito" / "Somente
leitura"), faixa de opções do módulo ativo (abas num `SelectorBar` e comandos num `CommandBar`, montados de `Faixas.Para`;
rótulos recolhidos com a janela abaixo de 1000 DIP), trilho de módulos à esquerda (64 px, ícone + rótulo, indicador 3 × 16; só os
módulos de `Faixas.Disponiveis`) e as páginas dos módulos, criadas uma vez e trocadas por Visibility (o WebView2 nunca é recriado).
Módulo e aba ativos ficam em `<dados>\ajustes.json`.

Relatórios trabalha sobre o pacote `.pmvrel`: **Arquivo** (Importar dados.json, Abrir, Recentes, Salvar, Salvar como, Exportar PDF,
Fechar), **Página Inicial** (aba padrão; Situação ▾ com as 4 situações de item do kit, Excluir/Restaurar item, Editar texto,
Original ⇄, Restaurar original, ◂ Item, Item ▸, Próximo pendente, Exportar PDF), **Revisão** (Marcas de revisão, Somente
alterados, Conferência, Comparar com o original), **Exibir** (−, +, Ajustar largura, Página inteira, Painel lateral, Propriedades, Tema) e **Ajuda** (Atalhos de
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
(`KeyboardAccelerator`; numa caixa de texto, Delete, Ctrl+Z e Ctrl+Y são dela) e nas folhas (mensagem `atalho`); Ctrl+F foca a
pesquisa (Tarefa 11).

A partir da Tarefa 10 o trabalho não se perde (desenho §5.1, §8.6). Abrir trava o arquivo (`.~nome.pmvrel.lock` ao lado,
batimento a cada 30 s): com a trava viva de outra janela, máquina ou usuário o relatório abre **somente leitura** ("Aberto por
{usuário} em {máquina} desde HH:mm: somente leitura.", num InfoBar; os comandos de edição ficam desabilitados); sem como travar
(pasta sem escrita) também; trava perdida num batimento ⇒ somente leitura, com as alterações guardadas no diário. Uma trava sem
batimento há mais de 2 min (ou do mesmo usuário e máquina com o processo encerrado) é retomada. Cada comando vai para o diário de
recuperação (`<dados>\recuperacao\<sha do caminho>.jsonl`); falha nele (disco cheio) aparece num InfoBar de erro. O pacote é gravado
sozinho depois do intervalo dos ajustes sem comando (15/30/60/120 s; padrão 30), ao trocar de módulo ou de arquivo e ao fechar a
janela (o fechamento espera na fila de comandos, grava e fecha; só pergunta se a gravação falhar: "Salvar como…", "Fechar sem
salvar (a recuperação fica guardada)" ou "Cancelar"). Gravou ⇒ "Salvo HH:MM" e o diário recomeça com o sha novo (o que chegou
durante a gravação vai para o diário novo). Gravação que falha (arquivo preso pelo OneDrive, disco cheio) ⇒ "Não salvo", InfoBar
de erro, arquivo antigo intacto, diário com tudo e nova tentativa no intervalo seguinte. Na abertura, um diário da mesma versão do
arquivo com comandos que ele não tem ⇒ pergunta Recuperar (reaplica, "Não salvo") ou Descartar (o diário é guardado como órfão
`<sha>.orfao-AAAAMMDDhhmmss.jsonl`, nunca apagado); de outra versão ou divergente do histórico ⇒ guardado como órfão sem aplicar,
com um InfoBar que diz o nome. Órfãos parados há mais de 30 dias saem na abertura do app. Falha do diário grava o pacote na hora (pela fila) e o diário recomeça no comando seguinte. Exportar com alterações que o arquivo não tem e não pode receber (trava perdida, conflito) é recusado. Fechar em conflito não promete recuperação (o arquivo é outra versão) e oferece "Salvar como cópia". Arquivo mudado por outro programa ⇒ "Conflito": nada é sobrescrito, a exportação recusa e o InfoBar
oferece **Salvar como cópia** (`<nome> (cópia AAAA-MM-DD HH-MM).pmvrel` ao lado, sem seletor; a cópia passa a ser o arquivo aberto,
com a trava dela) e **Recarregar** (confirma; abre o arquivo do disco e guarda o diário desta sessão como órfão).

A partir da Tarefa 11 (desenho §5.5, §8.5, §9): **Pesquisa** na barra de título ("Pesquisar no relatório", "3 de 12" ao lado,
Enter = próxima, Shift+Enter = anterior, Esc limpa e volta às folhas, Ctrl+F foca a caixa). A página recebe `pesquisar {termo,
passo}` e responde `pesquisado {total, atual, ref}`; o realce usa a CSS Custom Highlight API (`CSS.highlights`,
`::highlight(pmvni-busca)` e `::highlight(pmvni-busca-atual)`, só em `@media screen`): nenhum elemento, atributo ou nó de texto é
criado nas folhas, então nada do realce entra no PDF nem no HTML exportado. A busca é feita dentro de cada nó de texto da área útil,
sem diferenciar maiúsculas nem acentos (NFD, um caractere por caractere); depois de cada recomposição o host refaz a pesquisa com a
mesma ocorrência atual (`pesquisar` com `atual`, sem rolar). **Tema** (Exibir › Tema: Sistema, Claro, Escuro; guardado em
`ajustes.json`; só a janela muda, as folhas continuam brancas). **Recentes** (Arquivo › Recentes: os 10 últimos `.pmvrel` abertos ou
gravados, o mais novo primeiro; só aparece o que ainda existe, nada fica desabilitado). **Comparar com o original** (Revisão: só os
itens alterados, na ordem do relatório, com a situação original → revisada, a justificativa e cada campo com o original e o
revisado como são impressos hoje, mais os excluídos; numa seção à parte, **todas** as citações "item NN" que a importação converteu,
inclusive as de itens sem alteração). O módulo **Ajustes** (embaixo no trilho) tem o tema, o intervalo do salvamento automático
(15/30/60/120 s), a pasta de recuperação (só o caminho) e as versões (app, modelo rev10, WebView2). Abrir ou importar com os Ajustes à
vista leva ao módulo Relatórios.

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
| 18 | `18_menu_de_contexto_e_atalhos` | Botão direito (CDP) no ITEM 10: o menu abre com 5 entradas; Delete nas folhas exclui; Alt+↓ seleciona o próximo incluído; desfazer; clique na marca "◦ excl." abre o flyout; Somente alterados filtra, Marcas tira e põe a camada, Propriedades recolhe; todo atalho (Ctrl+F inclusive, desde a Tarefa 11) e todo comando da faixa têm manipulador. |
| 19 | `19_latencia` | Mediana de 5 comandos de situação no longo em `latencia_ms` (meta 1,5 s; só registra). |
| 20 | `20_comandos_em_sequencia` | Justificativa e, sem esperar, Situação e Excluir pela faixa: os três entram na fila e são aplicados na ordem; uma situação e, sem esperar, um `editar` da página: a edição entra (modelo e folhas); tudo desfeito depois. |
| 21 | `21_autosave` | `curto.json` importado em `<pasta>\curto-auto.pmvrel`; intervalo de 15 s nos ajustes do roteiro (definido no início); 1 comando: "Não salvo" e o registro no diário; sozinho, entre 14 e 25 s, o estado vira "Salvo HH:MM", o pacote em disco tem o registro e o diário só o cabeçalho com o sha novo. |
| 22 | `22_gravacao_falha_sem_perder` | 1 comando; o roteiro prende o pacote (`FileShare.None`) e chama `GravarAgoraAsync`: false, "Não salvo", InfoBar de erro, bytes do pacote iguais aos de antes, sem `.tmp`, o diário com o registro; solto, `GravarAgoraAsync` grava e o pacote tem o registro. |
| 23 | `23_recuperacao_apos_queda` | 2 comandos e `SimularQuedaParaRoteiro` (solta pacote, trava e temporizadores sem gravar; o diário fica com os 2); reabrir pergunta "recuperar 2"; Recuperar dá o mesmo `ProjetoJson` e o mesmo histórico, "Não salvo", diário novo com os 2 pendentes; grava depois. |
| 24 | `24_trava_e_somente_leitura` | Trava viva de `OUTRA-MAQUINA` gravada pelo roteiro: "Somente leitura", InfoBar "Aberto por outro.usuario em OUTRA-MAQUINA desde HH:mm: somente leitura.", Excluir e Salvar desabilitados, Excluir não muda nada, a trava do outro fica intacta ao fechar; trava com batimento de 3 min atrás: abre editável, Excluir habilitado, a trava passa a ser deste processo; 1 comando e a trava apagada por fora: o batimento vira "Somente leitura" (InfoBar da trava perdida, Excluir desabilitado); exportar é recusado (`ExportacaoDivergenteException` com `TextosGravacao.ExportarSemGravar`, que aponta Salvar como; sem PDF nem recibo); fechar pergunta e "Fechar sem salvar" guarda o diário; reabrir pergunta "recuperar 1" e recupera; grava. |
| 25 | `25_conflito_nao_sobrescreve` | Outro `Pacote.Gravar` depois de abrir; comando + `GravarAgoraAsync`: false, "Conflito", InfoBar do conflito, bytes do disco = os do outro, exportação recusada; "Salvar como cópia" cria `curto-auto (cópia AAAA-MM-DD HH-MM).pmvrel` com a alteração, o original continua o do outro, a trava passa para a cópia, "Salvo"; outro programa muda a cópia e "Recarregar" abre o que está no disco ("Salvo") e guarda o diário desta sessão como órfão; outro conflito e fechar: a pergunta vem marcada como conflito, não promete recuperação ("não serão oferecidas de novo") e "Salvar como cópia" (principal) grava a cópia e fecha. A exportação em conflito recusa apontando Salvar como cópia. |

| 26 | `26_pesquisa` | (Tarefa 11) Ctrl+F foca a caixa; o título do ITEM 10 dá ao menos uma ocorrência, "1 de N", `ref` = ITEM 10 e a ocorrência atual está dentro do card do ITEM 10 e dentro da janela; "VERIFICAÇÃO" e "verificacao" dão o mesmo total, igual ao de um contador independente em JS; Enter avança ("2 de N"), Shift+Enter volta, com volta ao fim e ao começo; nº de elementos do `body` e `TextoPorFolha` iguais; um comando recompõe e a pesquisa é refeita na mesma ocorrência; o PDF exportado com a pesquisa ativa tem as mesmas folhas e o HTML não tem realce; termo vazio limpa. |
| 27 | `27_tema_chrome` | Exibir › Tema Escuro e Claro: `ActualTheme` do app, item marcado na faixa, opção marcada na página Ajustes, `ajustes.json` com o tema, folha `rgb(255, 255, 255)` nos dois, fundo fora das folhas diferente, texto por folha igual; pela página Ajustes e de volta a Sistema. Página Ajustes (módulo no trilho, embaixo; a caixa de pesquisa fica desabilitada): versões (app, `rev10`, WebView2), pasta de recuperação e intervalo do salvamento (15 → 60 → 15 s, guardado em `ajustes.json`); volta ao módulo Relatórios. |
| 28 | `28_recentes` | Dois pacotes abertos: o menu Recentes lista os dois, o mais recente primeiro, e `ajustes.json` guarda; escolher o A no menu abre o A e o leva ao topo; apagar o B por fora o tira do menu e abrir o item velho avisa "O arquivo não existe mais"; no máximo 10, sem repetir. |
| 29 | `29_comparar_com_o_original` | No longo (ITEM 03 editado, ATENDE com justificativa e ITEM 05 excluído): o diálogo "Comparar com o original" tem exatamente os dois itens, a situação original → revisada, a justificativa e os textos original e revisado da verificação, e o excluído; num `curto` com duas citações no ITEM 03 e nenhuma alteração: "Nenhum item foi alterado" e as duas citações convertidas na seção própria. |

| 31 | `31_processos` | (Tarefa 13) Sobre uma **cópia** da árvore fictícia (`<pasta>\processos`, feita no início do roteiro; a pasta real nunca é lida e `AberturaExterna.Simular` só registra): a lista tem os 4 processos na ordem da Tarefa 12 ("4 processos"); a pesquisa da barra ("Pesquisar processos") filtra por requerente, sem caixa nem acento ("fulano" e "FÚLANO" → só o 0001, texto "1 de 4"; "12" → só o 0012); o menu Ano (dinâmico) lista Todos os anos, 2099 e 2098 (✓ no escolhido) e 2098 deixa só o 0007; Ordenar por requerente (✓ no menu) e de volta a número; o detalhe do 0001 tem Relatórios (1: `dados_0001.json`), Despachos (2, com 3 arquivos) e, com Mostrar PROJETO e DOCUMENTOS ligado, as duas pastas; Abrir arquivo do PDF do PROJETO só registra o caminho, o `script.cmd` é recusado com "Este tipo de arquivo não é aberto pelo editor (.cmd). Use Mostrar no Explorador." sem registrar nada, e Abrir pasta registra o Explorador; **Importar análise da IA** do 0001 cria `Relatorio_0001_2099.pmvrel` na pasta do processo e abre no módulo Relatórios com 3 itens; repetir cria `Relatorio_0001_2099_2.pmvrel` (o primeiro fica igual, por sha256); o `.pmvrel` listado abre pelo Abrir relatório. |
| 32 | `32_despachos` | (Tarefa 13) Os 4 despachos de todos os processos por data (05/03/2099, 20/02/2099, 15/01/2099, 02/03/2098), "4 despachos", dica "Pesquisar despachos"; filtro Aprovação → 2 (✓ no menu), Correção → 1, Todos → 4; a pesquisa é pelo nome do arquivo ("CORREÇÃO" → 1, "1 de 4"); Abrir PDF registra o PDF da aprovação do 0001 sem abrir nada e Abrir prefere o DOCX da correção; o despacho do 0012 só tem DOCX: Abrir PDF fica desabilitado e, ao tentar, "«Abrir PDF» não está disponível agora: o despacho escolhido não tem PDF."; `AberturaExterna.Abrir` de `MODELOS\atalho.lnk` é recusado com a mensagem exata e nada é registrado; Mostrar no Explorador só registra; Agrupar por processo dá 3 grupos, Mais antigos primeiro inverte; Ir ao processo leva ao módulo Processos com o 0001 escolhido e o detalhe lido; "Só do processo escolhido" mostra os 2 despachos do 0001 ("2 despachos · só do processo 0001/2099"). |
| 33 | `33_ajustes_pastas` | (Tarefa 13) Pasta inexistente nos Ajustes: Processos e Despachos mostram "A pasta de processos não foi encontrada: <caminho>. Escolha a pasta em Ajustes." com o botão "Abrir Ajustes" e listas vazias; o botão (clicado pelo `ButtonAutomationPeer`) leva à página Ajustes, que mostra a pasta em uso e o aviso de que ela não existe; Escolher pasta (seletor com resposta fixa) vale na hora, grava `pasta_processos` em `ajustes.json`, tira o aviso e a lista volta com os 4 processos. |
| 34 | `34_faixa_e_pesquisa_por_modulo` | (Tarefa 13) Para cada módulo do trilho (Relatórios, Despachos, Processos, Ajustes): as abas são as de `Faixas.Para`, a aba aberta é a lembrada (ou a padrão), `ComandosSemManipulador()` é vazio, a dica da pesquisa é a do módulo e a caixa só fica desabilitada em Ajustes; a aba escolhida em Processos e o módulo ficam em `ajustes.json` e voltam depois de visitar outro módulo; cada módulo guarda o seu termo ("fulano" em Processos some em Despachos e volta); com Processos à vista, Excluir e Desfazer não mexem no relatório escondido (histórico e itens iguais, nenhum aviso); o termo e o "1 de N" da pesquisa do relatório voltam ao retornar a Relatórios. |
| 35 | `35_pastas_intactas` | (Tarefa 13) Com o relatório fechado, a cópia da árvore tem os mesmos arquivos e os mesmos sha256 de antes, exceto os dois `.pmvrel` criados pelas importações; nenhum `.lock`, `.bak` ou `.tmp` ficou. |

(Numeração: o plano previa 28 e 29 para Processos e Despachos e 31 a 33 para o resto; 28 e 29 já são Recentes e Comparar e o 30
fica reservado à Legislação (Tarefa 18). Os passos 31 a 35 são os da Tarefa 13.)

(Os passos 26 a 29 são os da Tarefa 11; o plano os previa como 24 a 27, mas 24 e 25 já eram a trava e o conflito da Tarefa 10.
Cada etapa que começa e termina fica em `tests\saidaoteiro\progresso.log`, para achar onde um roteiro travou.)

Testes sem a janela (`tests\Pmvni.Revisao.Testes`): `FaixasTestes` (toda faixa começa em Arquivo e termina em Ajuda, aba padrão,
ids únicos e conhecidos; Ajustes no trilho; Recentes, Tema e Comparar), `AjustesTestes` (lembrar módulo e aba; arquivo corrompido
guardado e padrão; recentes: no máximo 10, sem repetir, somem quando o arquivo some), `ComparacaoTestes` (só o que mudou, textos
impressos com os números de hoje, citações convertidas em seção própria, texto da janela), `NomesArquivoTestes`,
`GravadorPacoteTestes` (gravações simultâneas sem conflito), `AutosaveTestes` (Tarefa 10: o diário da sessão recomeça só com
o cabeçalho depois de gravar, o que chegou durante a gravação fica no diário novo e o antigo vira órfão, Salvar como leva o diário,
fechar guarda o diário com trabalho, falha de disco informada; decisão de recuperação na abertura; texto do somente leitura),
`LogicaAppTestes` (título da janela, estado de gravação) e
`AtalhosTestes` (as 16 teclas de `Atalhos.Mapa` = o `Set` ATALHOS de `edicao.ts`, todo atalho aponta para um comando conhecido,
Relatórios abre em Página Inicial); Tarefa 13: `FaixasTestes` ganhou `cada_modulo_tem_a_faixa_do_desenho` (abas e comandos de Despachos, Processos
e Ajustes), `trilho_na_ordem_do_desenho` (Relatórios, Despachos, Processos, Ajustes) e `comandos_do_relatorio_nao_valem_nos_outros_modulos`;
`ListasPastasTestes` (processos por número, data e requerente, filtro por ano e termo; despachos por tipo, nome de arquivo, agrupados por mês ou
por processo, mais recentes ou mais antigos primeiro; as linhas do detalhe do processo); `AjustesTestes` (pasta de processos efetiva e a chave
antiga de legislação ignorada); `PastasTestes` (filtro de despachos pelo nome do arquivo).

## O que ainda precisa de olhar humano

- Acabamento da barra de título (ícone, título e estado ao lado dos botões da janela), da faixa e do trilho em outros monitores e
  no tema escuro (as capturas XAML não incluem o Mica).
- O seletor de pastas dos Ajustes (Escolher pasta…) e Mostrar no Explorador / Abrir arquivo com o programa padrão de verdade (o roteiro só
  registra); as listas de Processos e Despachos com a pasta real de processos (centenas de pastas no OneDrive, arquivos só-na-nuvem) e a
  aparência delas nos temas claro e escuro.
- Os seletores do Windows (Importar, Abrir, Salvar como, Exportar) e os diálogos (Atalhos, Sobre, Conferência, Recuperar
  alterações, fechar com gravação que falhou, Recarregar): o roteiro usa respostas fixas.
- Fechar a janela pelo X com alterações não gravadas (o roteiro fecha o relatório pelos mesmos caminhos, mas não clica no X) e o
  batimento pelo temporizador de 30 s (o roteiro chama o batimento direto).
- A caixa de pesquisa com o teclado de verdade (Enter, Shift+Enter, Esc; o roteiro chama os mesmos métodos, não digita), o encaixe
  da caixa na barra de título em janelas estreitas e a aparência do diálogo "Comparar com o original".
- O menu de contexto e o flyout do excluído na posição do clique, o painel de Propriedades (Original ⇄ lado a lado) e os atalhos
  com o teclado de verdade (o roteiro manda as teclas pelo CDP, direto à página; os `KeyboardAccelerator`s da janela só são contados).
