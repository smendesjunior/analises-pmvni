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

A partir da Tarefa 16 (desenho §10): **Novo despacho ▾** (Correção, Aprovação) na Página Inicial de Despachos (primeiro comando),
de Processos (depois de "Importar análise da IA") e de Relatórios (depois de "Exportar PDF"), e **Gerar PDF** em Despachos (depois
de "Abrir PDF"). Só aparecem os papéis de despacho do registro do kit (`TEMPLATES_OFICIAIS.json`, lido da pasta do kit dos Ajustes ao
montar a faixa e a cada abertura do Novo despacho); um tipo cujo modelo falta ou não confere fica desabilitado com o motivo na dica.
A página Novo despacho abre no módulo Despachos, no lugar da lista: passo 1 (tipo, processo, relatório base `.pmvrel` e pasta da
etapa, já sugeridos pelo módulo de onde se veio), passo 2 (cabeçalho, exigências em cartões ou tipo de obra e área, com a folha de
prévia em Verdana ao lado; "[preencher: …]" em vermelho só na prévia; "Gerar e conferir ▸" só sem nada faltando) e passo 3 (DOCX,
validação e PDF numa pasta de trabalho `<dados>\despachos\<guid>`; cada página do PDF desenhada pelo `Windows.Data.Pdf` com a caixa
"Conferi a página N" e "Marcar todas"; "Gravar na pasta do processo" só com todas conferidas; sem LibreOffice, a mensagem do desenho
§10.7 e "Gravar só o DOCX"). Nada é gravado na pasta do processo antes desse clique; a gravação nunca sobrescreve (`… (2)`). O relatório
aberto com alterações é gravado antes (o despacho usa sempre a versão gravada). Trocar de módulo, abrir outro Novo despacho ou fechar o
app com a página no passo 2 ou 3 pergunta "Descartar o despacho em edição?". A página Ajustes ganhou a pasta do kit PMVNI e a situação
dos dois modelos. Desfazer e Refazer da barra de título ficam desabilitados fora de Relatórios.

A partir da Tarefa 17 (desenho §11.1–§11.3): a página Ajustes ganhou a seção **Legislação**, depois de "Pasta do kit PMVNI": o texto do
desenho, o total ("4 leis anexadas · 12,3 MB"), "Anexar PDFs…" (sempre disponível; seletor de `.pdf` com seleção múltipla; os arquivos
são lidos um por vez, com a linha "Anexando “x.pdf” — página N de M" e "Cancelar"; no fim, a InfoBar com o resumo exato) e **uma linha por
anexo**, na ordem esfera e título: título editável (vale ao sair da caixa ou com Enter; recusado volta ao anterior), esfera, "arquivo ·
tamanho · páginas · anexado em …", selo ("Sem texto pesquisável", "2 de 81 páginas sem texto", "Preparando a pesquisa…", "Cópia do
editor não encontrada"), "Substituir arquivo…" e "Excluir" (com confirmação). O editor guarda só cópias em `<dados>\legislacao` (o
original nunca muda); ao abrir os Ajustes, entradas órfãs saem, os avisos do catálogo aparecem e os índices que faltam são refeitos em
segundo plano. Catálogo de uma versão mais nova ⇒ tudo só leitura, com a frase do desenho §11.2. Versões mostra "PDF.js 6.4.299
(Apache-2.0)" e Ajuda › Sobre, a licença do PDF.js.

A partir da Tarefa 18 (desenho §11.4–§11.7): o módulo **Legislação** entra no trilho (Relatórios, Despachos, Processos, Legislação,
Ajustes). Painel com **Leis** (uma linha por anexo, agrupadas por esfera, filtro sem caixa nem acento; lista vazia ⇒ "Nenhuma lei
anexada. Anexe os PDFs das leis em Ajustes › Legislação." com "Anexar PDFs…" e "Abrir Ajustes"), **Marcadores** (`TreeView` com o
outline do PDF; desabilitada sem marcadores; link externo aparece com "(link externo)" e não faz nada) e **Resultados** (pesquisa em
todas as leis). Centro: o PDF num WebView2 InPrivate com a sandbox do host `leg.pmvni.invalid` (sem pasta mapeada, da memória, CSP do
desenho, 403 no resto, navegação só para `leitor.html`, uma vez; trocar de PDF é mensagem, e o PDF aberto fica registrado no servidor
só enquanto aberto). Rodapé "Página [N] de M", ◂ ▸ e a escala. Faixa: **Arquivo** (Anexar PDFs…, Gerenciar anexos, Importar, Abrir,
Recentes) · **Página Inicial** (Copiar, Copiar citação, ◂ Página, Página ▸, Pesquisar em ▾ Nesta lei / Em todas as leis, ◂ Ocorrência,
Ocorrência ▸) · **Exibir** (Diminuir, Aumentar, Ajustar largura, Página inteira, Marcadores, Lista de leis) · Ajuda; o que depende de lei
aberta, seleção ou marcadores fica desabilitado sem eles ("Selecione um trecho do texto."). Atalhos do leitor (`Atalhos.MapaLegislacao`,
as 10 teclas que `leitor.ts` repassa): Ctrl+F, Ctrl+C, Ctrl+Shift+C, Ctrl++, Ctrl+−, Ctrl+0, Alt+↑, Alt+↓, F3, Shift+F3; Ctrl+roda muda
a escala. A caixa de pesquisa da barra de título segue "Pesquisar em" ("Pesquisar nesta lei": PDFFindController, "k de n", "Nenhuma
ocorrência", "Este PDF não tem texto pesquisável."; "Pesquisar em todas as leis": o índice do app, 300 ms, "N resultados em K leis",
"p. 12 — …", "Mostrando os 200 primeiros de N resultados.", "Digite ao menos 2 caracteres."; clicar abre a lei na página e passa a
"Nesta lei"). Copiar põe o texto selecionado (quebras em `\r\n`); Copiar citação, `“<texto>” (<título>, página N)` ou `páginas N–M`.
Excluir a lei aberta fecha o leitor.

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
| 30 | `30_legislacao_leitor` | (Tarefa 18) Trilho [Relatórios, Despachos, Processos, Legislação, Ajustes]; módulo Legislação: aba Leis com os grupos [Federal, Estadual, Municipal, Outras], uma linha por anexo (as 4 do passo 60); filtro "nt" → só a NT; abrir a lei → URL do WebView2 exatamente `https://leg.pmvni.invalid/leitor.html` e rodapé "Página 1 de 4"; Página ▸ → 2; Alt+↓ dentro do PDF (mensagem `atalho`) → 3; Marcadores habilitada com 5; "Anexo I" → página 3; Diminuir, Aumentar, Página inteira e Ajustar largura mudam a escala do rodapé; a digitalizada → a InfoBar exata de sem texto, Marcadores e Copiar desabilitados; nenhuma requisição bloqueada nem violação de CSP; `<dados>\legislacao` com os mesmos arquivos e bytes (o leitor não grava nada); captura `30-legislacao.png`. |

| 31 | `31_processos` | (Tarefa 13) Sobre uma **cópia** da árvore fictícia (`<pasta>\processos`, feita no início do roteiro; a pasta real nunca é lida e `AberturaExterna.Simular` só registra): a lista tem os 4 processos na ordem da Tarefa 12 ("4 processos"); a pesquisa da barra ("Pesquisar processos") filtra por requerente, sem caixa nem acento ("fulano" e "FÚLANO" → só o 0001, texto "1 de 4"; "12" → só o 0012); o menu Ano (dinâmico) lista Todos os anos, 2099 e 2098 (✓ no escolhido) e 2098 deixa só o 0007; Ordenar por requerente (✓ no menu) e de volta a número; o detalhe do 0001 tem Relatórios (1: `dados_0001.json`), Despachos (2, com 3 arquivos) e, com Mostrar PROJETO e DOCUMENTOS ligado, as duas pastas; Abrir arquivo do PDF do PROJETO só registra o caminho, o `script.cmd` é recusado com "Este tipo de arquivo não é aberto pelo editor (.cmd). Use Mostrar no Explorador." sem registrar nada, e Abrir pasta registra o Explorador; **Importar análise da IA** do 0001 cria `Relatorio_0001_2099.pmvrel` na pasta do processo e abre no módulo Relatórios com 3 itens; repetir cria `Relatorio_0001_2099_2.pmvrel` (o primeiro fica igual, por sha256); o `.pmvrel` listado abre pelo Abrir relatório. |
| 32 | `32_despachos` | (Tarefa 13) Os 4 despachos de todos os processos por data (05/03/2099, 20/02/2099, 15/01/2099, 02/03/2098), "4 despachos", dica "Pesquisar despachos"; filtro Aprovação → 2 (✓ no menu), Correção → 1, Todos → 4; a pesquisa é pelo nome do arquivo ("CORREÇÃO" → 1, "1 de 4"); Abrir PDF registra o PDF da aprovação do 0001 sem abrir nada e Abrir prefere o DOCX da correção; o despacho do 0012 só tem DOCX: Abrir PDF fica desabilitado e, ao tentar, "«Abrir PDF» não está disponível agora: o despacho escolhido não tem PDF."; `AberturaExterna.Abrir` de `MODELOS\atalho.lnk` é recusado com a mensagem exata e nada é registrado; Mostrar no Explorador só registra; Agrupar por processo dá 3 grupos, Mais antigos primeiro inverte; Ir ao processo leva ao módulo Processos com o 0001 escolhido e o detalhe lido; "Só do processo escolhido" mostra os 2 despachos do 0001 ("2 despachos · só do processo 0001/2099"). |
| 33 | `33_ajustes_pastas` | (Tarefa 13) Pasta inexistente nos Ajustes: Processos e Despachos mostram "A pasta de processos não foi encontrada: <caminho>. Escolha a pasta em Ajustes." com o botão "Abrir Ajustes" e listas vazias; o botão (clicado pelo `ButtonAutomationPeer`) leva à página Ajustes, que mostra a pasta em uso e o aviso de que ela não existe; Escolher pasta (seletor com resposta fixa) vale na hora, grava `pasta_processos` em `ajustes.json`, tira o aviso e a lista volta com os 4 processos. |
| 34 | `34_faixa_e_pesquisa_por_modulo` | (Tarefa 13) Para cada módulo do trilho (Relatórios, Despachos, Processos, Ajustes): as abas são as de `Faixas.Para`, a aba aberta é a lembrada (ou a padrão), `ComandosSemManipulador()` é vazio, a dica da pesquisa é a do módulo e a caixa só fica desabilitada em Ajustes; a aba escolhida em Processos e o módulo ficam em `ajustes.json` e voltam depois de visitar outro módulo; cada módulo guarda o seu termo ("fulano" em Processos some em Despachos e volta); com Processos à vista, Excluir e Desfazer não mexem no relatório escondido (histórico e itens iguais, nenhum aviso); o termo e o "1 de N" da pesquisa do relatório voltam ao retornar a Relatórios. |
| 35 | `35_pastas_intactas` | (Tarefa 13) Com o relatório fechado, a cópia da árvore tem os mesmos arquivos e os mesmos sha256 de antes, exceto os dois `.pmvrel` criados pelas importações; nenhum `.lock`, `.bak` ou `.tmp` ficou. |
| 36 | `36_novo_despacho_correcao` | (Tarefa 16) Preparação dos passos 36–40: cópia separada da árvore fictícia em `<pasta>\processos-despacho` (a pasta de processos dos Ajustes passa a ser ela), pasta do kit = o kit real (só lido; `PMVNI_KIT_RAIZ` limpa no processo do roteiro), `Hoje` = 2099-03-10, `<pasta>\curto-0001.json` (o `curto` com `capa.processo` "0001/2099" e `capa.requerente` "FULANO DE EXEMPLO (FICTICIO)") importado para `Relatorio_0001_2099.pmvrel` do 0001. Pela faixa de Relatórios, com o pacote aberto: Novo despacho ▸ Correção leva a Despachos com o processo 0001, o relatório aberto e a etapa nova "DESPACHO 03 - 2099-03-10"; 2 exigências (ITEM 02, ITEM 03); prévia com "Protocolo nº: 0001/2099"; Gerar com o **LibreOffice real**: páginas ≥ 1, fontes com Verdana, `InfoPdf.Ler` lê o PDF, uma caixa por página, nada na pasta do processo e Gravar desabilitado até "Marcar todas"; Gravar: os 3 arquivos `Despacho_Correcao_1_2099_Fulano_De_Exemplo_Ficticio.*` na pasta nova, `ValidacaoDespacho.Comparar(modelo, docx)` vazio, sha do DOCX e do PDF = os do recibo; Despachos volta com o novo (Correção, nº 3, 2099-03-10) escolhido e o aviso "Despacho gravado" ("3 arquivos em … Recibo: ….recibo-editor.json."); "Abrir PDF" só registra o caminho. |
| 37 | `37_novo_despacho_aprovacao` | (Tarefa 16) Pela faixa de Processos, com o 0001 escolhido: o aviso de 2 itens ≠ ATENDE é confirmado ("Emitir mesmo assim", resposta fixa) e vai para `AvisosConfirmados`; `Faltando` = as 2 mensagens exatas (tipo de obra e área) e "Gerar e conferir" desabilitado; "obra nova" e área "210,52" habilitam; a etapa existente "DESPACHO 03 - 2099-03-10" mantém o que foi preenchido; Gerar, Marcar todas, Gravar: `Despacho_Aprovacao_1_2099_Fulano_De_Exemplo_Ficticio.{docx,pdf,recibo-editor.json}` na mesma pasta, os arquivos da correção com o mesmo sha, o recibo com `avisos_confirmados` = o aviso exato e a etapa existente. |
| 38 | `38_despacho_sem_libreoffice` | (Tarefa 16) Pela faixa de Despachos (0001 escolhido), com um conversor ausente: etapa nova "DESPACHO 04 - 2099-03-10"; o passo 3 mostra a mensagem exata de `EmissaoDespacho` ("O LibreOffice não está instalado neste computador. O despacho será gravado só em Word (DOCX). Para o PDF, rode o instalador do kit (INSTALAR_KIT_PMVNI.cmd) e use Despachos › Gerar PDF.") e "Gravar só o DOCX"; grava `.docx` e recibo com `sem_pdf_motivo` "SEM_LIBREOFFICE", sem `.pdf` ("2 arquivos em …"). Com o LibreOffice real de volta, o despacho escolhido habilita Despachos › Gerar PDF: a página abre direto no passo 3 com as páginas; Marcar todas, Gravar: `.pdf` com o mesmo nome base, o DOCX igual, o recibo com `pdf` preenchido e `sem_pdf_motivo` null; Gerar PDF volta a ficar desabilitado. |
| 39 | `39_despacho_nao_sobrescreve` | (Tarefa 16) Nova correção na "DESPACHO 03 - 2099-03-10": os três nomes com " (2)"; os 6 arquivos anteriores da pasta com o mesmo sha; nenhum `.tmp` na cópia. |
| 40 | `40_despacho_bloqueios` | (Tarefa 16) (a) Situação do ITEM 02 mudada para ATENDE sem justificativa no pacote aberto: o Novo despacho grava o pacote antes, mostra a mensagem exata da conferência da revisão (Tarefa 3) e fica no passo 1 (sem rascunho; Gerar não faz nada); desfeito e gravado. (b) O `dados_0001.json` da cópia (capa 9001/2026) importado para `Relatorio_0001_2099_2.pmvrel`: escolher o outro relatório abre o passo 2; escolhê-lo dá "O processo da capa (9001/2026) não é o da pasta (0001/2099). Confira se o relatório é deste processo." e volta ao passo 1. (c) Pasta do kit = `<pasta>\kit-vazio`: os dois itens do menu desabilitados com "Kit PMVNI não encontrado em <pasta>\kit-vazio. Escolha a pasta do kit em Ajustes." na dica e na página Ajustes; tentar não abre nada; o kit real volta ("Correção: disponível", "Aprovação: disponível"). (e) Com um despacho no passo 2, trocar de módulo pergunta "Descartar o despacho em edição?": "Continuar editando" (resposta fixa) fica na página; "Descartar" fecha sem gravar nada. (d) Com o relatório fechado, a cópia tem os arquivos de antes (mesmo sha) + só os gravados em 36–39, os dois `.pmvrel` importados e o `.bak` do `Relatorio_0001_2099.pmvrel` (da gravação do (a), permitida ao lado de um pacote aberto); no kit, `02 - MODELOS DE DOCUMENTOS` e a pasta `references` do plugin despacho têm a mesma lista, os mesmos tamanhos e as mesmas datas. |
| 41 | `41_edicao_aberta_concluida` | (Onda de correções 1) Texto digitado no A4 sem Enter entra em Exportar (PDF e recibo com o texto), Salvar (arquivo com o texto) e Fechar (gravado e fechado com o texto). |
| 42 | `42_recomposicao_que_falha_bloqueia_exportar` | (Onda de correções 1) Composição que falha depois do comando aplicado: aviso de folhas desatualizadas, exportar bloqueado e recusado; Recompor e exportar de novo com o recibo certo. |
| 43 | `43_batimento_toca_a_pasta_de_trabalho` | (Onda de correções 1) O batimento atualiza a data da pasta de trabalho (idade < 1 min depois de 2 dias). |
| 44 | `44_libreoffice_perfil_persistente` | (Onda de correções 1) Tempos das conversões com o perfil persistente (<dados>/lo), perfil inicializado e sem pid deixado. |
| 45 | `45_despacho_gravando_e_pasta_de_processos` | (Onda de correções 1) Trocar a pasta de processos com o despacho no passo 2 pergunta (Continuar editando mantém); gravando o despacho, trocar de módulo não descarta nem diz que nada foi gravado. |
| 46 | `46_salvar_como_com_edicao_aberta` | (Onda de correções 2, item 3) Texto digitado no A4 sem Enter e Salvar como: a cópia gravada tem o texto, passa a ser o relatório aberto (Salvo) e a edição fecha; depois, com o arquivo mudado por fora, Salvar como cópia (conflito) com outro texto aberto: a cópia ao lado tem o texto. |
| 47 | `47_fechar_com_edicao_recusada` | (Onda de correções 2, item 4) Texto só de espaços no A4 (a conferência do kit recusa): Fechar pergunta "O texto em edição não pôde ser aplicado: <motivo>. Fechar mesmo assim?"; "Voltar à edição" mantém o relatório aberto e devolve o campo à edição com o texto; "Fechar sem ele" fecha e o arquivo fica como estava. |
| 48 | `48_legislacao_seguranca` | (Tarefa 18; roda depois do 63) Anexar e abrir `COM-SCRIPT - FICTICIA.pdf`: nenhuma requisição bloqueada nem violação de CSP (o script do PDF nunca roda); clique por CDP no link → a URL continua `leitor.html`, nenhuma janela nova, `AberturaExterna.Registradas` igual; `location.href` para `pdfjs/build/pdf.min.mjs` → cancelada (1 bloqueio), leitor intacto ("script" → "1 de 1"); `fetch` de `catalogo.json` pela página → 403; excluir o anexo com o leitor aberto → leitor fechado (aba Leis, nada aberto) e a URL do PDF dele responde 403; os originais intactos e as 4 leis de antes. |
| 49 | `49_ajustes_ia` | (Tarefa 19, só o agente falso) Ajustes › Inteligência artificial: a seção com o texto fixo e os dois cartões; as peças achadas são só as do agente falso e a trava recusa um programa de verdade; Testar o ChatGPT ("Codex 0.99.0 · conectado com a conta do ChatGPT · plugin analise 2.4.0 habilitado") e o Claude ("Claude Code 2.9.0 · conectado com a conta do Claude · análise pro 1.6.1 (kit: <versão do kit real>) · Python 3.14.0"); Claude › "Usar chave de API" desabilitado, com a dica "O análise pro do kit usa somente a conta do Claude (política do kit de 14/11/2026).", e o pedido direto é recusado sem mudar nada; "Escolha um programa .exe que exista." para um programa que não existe ou não é .exe (o programa não muda), um .exe que existe vale e "Usar o padrão" volta; com `enabled = false` no `config.toml` do Codex a linha diz "plugin analise não habilitado no Codex" e, restaurado, volta. |
| 50 | `50_chave_api_protegida` | (Tarefa 19, só o agente falso) Chave falsa aleatória: ChatGPT › Usar chave de API › Guardar chave ("Chave guardada, protegida pelo Windows para este usuário."), `<dados>\segredos\chatgpt.dpapi` existe e não contém a chave (UTF-8, UTF-16, base64), `ajustes.json` só com `"modo": "chave"`, caixa de senha vazia; Testar acrescenta "Chave guardada; ela só é conferida pelo provedor na primeira análise."; Remover (Cancelar mantém; "Remover" apaga o arquivo); varredura de `tests\saida\roteiro` e `tests\saida\dados-app` sem nenhuma ocorrência da chave; volta para Usar minha conta. |
| 51 | `51_novo_relatorio_anexos` | (Tarefa 20, só o agente falso) "Novo relatório" pela faixa de Relatórios abre a página no lugar das folhas; Criar processo… (resposta fixa 9998/2099 "Fulána de Tál") cria `2099\9998 - 2099 - FULANA DE TAL` com `DOCUMENTOS` e `PROJETO\INICIAL` e o escolhe; de novo, nenhuma pasta nova e "O processo 9998/2099 já existe: …"; no 0001, Anexar arquivos… (planta, requerimento, memorial) e arrastar (foto.heic, a pasta `fotos`, `atalho.lnk`; o mesmo caminho duas vezes entra uma vez) ⇒ 4 linhas e as recusas "Arraste arquivos, não pastas: fotos." e "atalho.lnk é um atalho; anexe o arquivo de destino."; destino padrão "Projeto — primeira apresentação" (prévia `PROJETO\INICIAL\foto.heic`), sem "juntar à REVISÃO NN atual"; planta e memorial em reapresentação ⇒ `PROJETO\REVISÃO 01\…`, requerimento em Documento ⇒ `DOCUMENTOS\requerimento-ficticio.pdf`; lista e sha da cópia iguais aos de antes (nada gravado antes de Analisar). |
| 52 | `52_novo_relatorio_chatgpt` | (Tarefa 20) Cenário `ok`: pedido de 2 linhas; botão sem motivo; Analisar com o ChatGPT pede o aviso uma vez ("Concordo e analisar") e grava `aviso_versao` 1; vista da execução e o tempo na barra de título ("Analisando com o ChatGPT · 0001/2099 · …"); ≥ 3 linhas ("Mensagem: …", "Comando: …"); anexos copiados com o sha da origem (REVISÃO 01 com 2 arquivos, `PROJETO\INICIAL\foto.heic`, `DOCUMENTOS\requerimento-ficticio.pdf`); registro `.pmvni-ai\editor\<id>` com `pedido.md` == `PedidoAgente.Codex(…)`, `anexos.json` (4), `saida.jsonl` e `execucao.json` (`concluida`, `conta`, `.pmvrel`); o agente falso recebe a linha exata, na pasta do processo, com o pedido pela entrada padrão; `Relatorio_0001_2099*.pmvrel` importado e aberto com 3 itens e a InfoBar exata; uma segunda análise com outro anexo não pede o aviso de novo; no fim, nada em andamento. |
| 53 | `53_novo_relatorio_chatgpt_chave` | (Tarefa 20) Cenário `chave`, com `OPENAI_API_KEY=falsa-herdada` no ambiente do editor: na conta o agente recebe `CHAVE_AUSENTE`; com uma chave falsa aleatória guardada (Usar chave de API), `CHAVE_PRESENTE <8 hex do sha256 da chave>` e `execucao.json` com `"modo": "chave"`; varredura de `tests\saida` (menos o `.dpapi`) sem a chave nem `falsa-herdada`; Remover chave ⇒ o motivo exato no botão; volta para a conta. |
| 54 | `54_novo_relatorio_claude` | (Tarefa 20) Sob "Analisar com o Claude", o texto fixo do pedido escrito; cenário `claude_ok`: aviso do Claude uma vez; o agente falso recebe `-B -X utf8 <pro_runtime.py fictício> --case <pasta> iniciar` uma vez e `situacao` várias; as etapas passam de Preparo a Revisão final; linhas do `auto-log.txt`; `situacoes.jsonl` e `pedido.md` no registro; `CONCLUIDO` ⇒ o `relatorio_dados.json` de `.pmvni-ai\analise-pro` importado e aberto; `execucoes_claude` vazio no fim; cenário `objecoes` ⇒ InfoBar "parou com objeções da revisão final: A revisão final apontou objeções ao relatório." e "Continuar a análise", que dá outro `iniciar`. |
| 55 | `55_novo_relatorio_segundo_plano_e_cancelar` | (Tarefa 20; onda final: a segunda checagem do fechamento, na vez da fila, só pergunta pelas execuções novas — `segunda_checagem_so_novas`) ChatGPT `lento` no 0001: trocar para Processos e voltar não interrompe; o botão diz "Já há uma análise em andamento neste processo." no 0001 e nada no 0012 (a árvore fictícia não tem 0002); fechar com "Continuar" não fecha; Cancelar análise (confirmado) ⇒ em ≤ 5 s nenhum processo do agente falso vivo, `cancelada`, nenhum `.pmvrel` novo e `execucao.lock` apagado; Claude `claude_lento` no 0012: fechar só informa (texto exato) e a análise fica em `execucoes_claude`; o editor deixa de acompanhar (o que o fechamento faz) e o kit segue; `RetomarExecucoesClaudeAsync` volta a acompanhar; Cancelar ⇒ pid do kit morto, InfoBar "interrompida…" e "Continuar a análise"; um `execucoes_claude` editado à mão com o registro fora de `<processo>\.pmvni-ai\editor` é descartado com o motivo e nada é gravado nele; um Claude que termina com o editor "fechado" ⇒ reaberto, "A análise com o Claude de 0012/2099 terminou." com "Importar relatório"; a 1ª tentativa cancelada (texto recusado no A4, "Voltar à edição") deixa a pendência e o botão; a 2ª importa e só então a pendência sai. |
| 56 | `56_novo_relatorio_falhas` | (Tarefa 20) `sem_resultado` ⇒ "A análise terminou, mas não deixou relatório novo (delivery.json não foi atualizado). Veja o registro: …"; `falha` ⇒ "O Codex terminou com o código 2. Veja o registro: …"; `json_invalido` ⇒ "O delivery.json da análise não pôde ser lido: …"; `fora_do_processo` ⇒ "O relatório indicado pela análise está fora da pasta do processo: …"; nenhum `.pmvrel` novo; Codex ausente ⇒ o motivo exato e o botão desabilitado; aviso recusado ⇒ nada copiado, nenhum registro, nenhum processo iniciado; no fim, a cópia da árvore só ganhou os anexos copiados, `.pmvni-ai\editor`, `.pmvni-ai\agente-falso`, as saídas do agente falso em `.pmvni-ai\analise(-pro)` e os `.pmvrel` importados (nada que existia mudou); anexos originais com o mesmo sha, tamanho, datas e atributos; a trava de programas só deixa o agente falso. |
| 57 | `57_legislacao_anexar_dois` | (Tarefa 17) `<dados>\legislacao` apagada antes; Ajustes à vista; "Anexar PDFs…" com [lei, NT] (seletor com lista fixa) → resumo `"2 PDFs anexados."` na InfoBar; **2 linhas, uma por anexo**, na ordem [NT (Estadual), lei (Municipal)], cada uma com título, esfera, arquivo, tamanho, "2 páginas" / "3 páginas" e "anexado em dd/MM/aaaa HH:mm"; total "2 leis anexadas · …"; `<dados>\legislacao` com os 2 `<sha>.pdf` (bytes = originais), os 2 índices e o `catalogo.json`; `.entrada` vazia; originais intactos. |
| 58 | `58_legislacao_recusas` | (Tarefa 17; onda final: a partir do módulo **Legislação**, com o andamento à vista nele, um segundo "Anexar PDFs…" respondido com "Uma anexação já está em andamento." e o resumo com as recusas na InfoBar do próprio módulo) [lei, NAO-E-PDF, SENHA, CORROMPIDO, DIGITALIZADA] → resumo exato `"1 PDF anexado; 4 recusados:"` + as 4 mensagens (repetido, não é PDF, senha, danificado); 3 linhas; a digitalizada (Outras, "1 página") com o selo "Sem texto pesquisável"; 3 cópias; nada em `.entrada`. |
| 59 | `59_legislacao_excluir_e_anexar_novos` | (Tarefa 17) Excluir a NT com a resposta "Excluir": a pergunta `Excluir “NT 99-2099 - FICTICIA”?` com o texto exato do desenho §11.1 (Excluir · Cancelar); linhas [lei, digitalizada]; cópia e índice da NT apagados; os da lei e da digitalizada com os mesmos bytes e datas; `IndiceAsync().Pesquisar("afastamento")` → 3 resultados, todos da lei (páginas 1, 2, 3). Depois [FEDERAL, NT] → `"2 PDFs anexados."`, 4 linhas [Federal, NT, lei, digitalizada], a NT com id novo, pesquisa com 4. Excluir a federal com "Cancelar" → linhas e arquivos iguais. |
| 60 | `60_legislacao_editar_e_substituir` | (Tarefa 17) Renomear a lei para "Lei nº 9.999/2099 (fictícia)" → null; `""` → "O título não pode ficar vazio." na InfoBar e o título continua o novo; esfera da digitalizada → Municipal (gravada) e de volta a Outras; Substituir arquivo… com a versão 2 → null, mesmo id, título e esfera, "4 páginas", `Pesquisar("versao 2")` → 1 resultado na página 4, cópia e índice antigos apagados; um `AcervoLegislacao` novo sobre `<dados>\legislacao` lista as mesmas 4 linhas com os mesmos campos (gravado de verdade); captura `60-ajustes-legislacao.png`. |
| 61 | `61_legislacao_pesquisa_na_lei` | (Tarefa 18) Lei aberta, dica "Pesquisar nesta lei": "AFASTAMENTO" → "1 de 3"; Enter → "2 de 3"; Shift+Enter duas vezes → "3 de 3"; "ocupacao" → "1 de 2"; "inexistentexyz" → "Nenhuma ocorrência"; caixa vazia → nenhum `.highlight` no DOM; digitalizada + "x" → "Este PDF não tem texto pesquisável.". |
| 62 | `62_legislacao_pesquisa_em_todas` | (Tarefa 18) "Pesquisar em › Em todas as leis" (dica "Pesquisar em todas as leis"): "afastamento" → aba Resultados, "4 resultados em 2 leis", [NT: "p. 2 — 5.1 O afastamento entre edificações fictícias é de 4,00 m."; lei: p. 1, p. 2, p. 3]; clicar lei p. 3 → lei aberta na página 3, escopo "Nesta lei", contador "3 de 3"; caixa vazia → aba Leis; "a" → "Digite ao menos 2 caracteres.". |
| 63 | `63_legislacao_copiar_e_citar` | (Tarefa 18) Lei na página 2: sem seleção, Copiar e Copiar citação desabilitados com a dica "Selecione um trecho do texto."; seleção por Range na camada de texto da pág. 2 → habilitados; Copiar → "O afastamento frontal mínimo é de 3,00 m."; Copiar citação → "“O afastamento frontal mínimo é de 3,00 m.” (Lei nº 9.999/2099 (fictícia), página 2)"; Ctrl+Shift+C dentro do PDF → o mesmo texto de novo; seleção da pág. 2 à 3 → a citação termina com "páginas 2–3)". A área de transferência só registra (`AreaTransferencia.Simular`). |
| 64 | `64_fluxo_completo` | (Tarefa 14) Sequência única sobre outra cópia da árvore fictícia (`<pasta>\fluxo\processos`) e um pacote novo: **Novo relatório** pela faixa de Relatórios no 0001/2099 com `planta-ficticia.pdf` em "Projeto — reapresentação" (prévia `PROJETO\REVISÃO 01\planta-ficticia.pdf`) e Analisar com o ChatGPT pelo agente falso (cenário `ok`, o `curto` com a capa do 0001/2099) → `.pmvrel` importado e aberto → editar a verificação do ITEM 01 no A4 (Enter) → ITEM 02 em ATENDE com justificativa → excluir o ITEM 03 e desfazer → salvar, fechar e reabrir (mesmo estado e mesmo número de registros do histórico) → exportar PDF (páginas = folhas; `sha256_pacote` do recibo = sha do arquivo) → pesquisar "fluxo completo" ("1 de N", no ITEM 01) → tema escuro e de volta a "sistema" → Recentes com o pacote no topo → Comparar: 2 itens alterados → **Novo despacho › Correção** pela faixa de Relatórios (processo 0001 e o pacote pré-escolhidos, etapa nova "DESPACHO 03 - 2099-03-10"), exigências = itens ≠ ATENDE do estado atual (só o ITEM 03), páginas conferidas com o LibreOffice real, gravar (DOCX + PDF + recibo; `relatorio.sha256_pacote` = sha do pacote) → Despachos lista o novo como Correção → **Ajustes › Legislação** esvaziada pelo próprio app (Excluir de cada linha), Anexar PDFs… com a lei fictícia (cópia de `tests\corpus\legislacao` em `<pasta>\fluxo\legislacao`) → 1 linha → título "Lei nº 9.999/2099 (fictícia)" → **Legislação**: "afastamento frontal" em todas as leis → "2 resultados em 1 lei" (p. 1 e p. 2) → abrir o da p. 2 → "2 de 2" → selecionar "O afastamento frontal mínimo é de 3,00 m." → Copiar citação = "“O afastamento frontal mínimo é de 3,00 m.” (Lei nº 9.999/2099 (fictícia), página 2)" → Excluir (confirmado): nenhuma linha, cópia e índice apagados, original com o mesmo sha, tamanho, data e atributos. Por fim: `execucao.json` do início com `situacao` "concluida" e o `.pmvrel` criado; o anexo original intacto e a cópia na REVISÃO 01 com o mesmo sha; nenhuma das chaves falsas do roteiro (passos 50 e 53 e o valor herdado) em arquivo algum de `tests\saida` (o `.dpapi` fica de fora); nenhum arquivo do repositório gravado fora de `tests\saida\roteiro`, `dados-app` e `agente-falso` desde o início do roteiro; só o agente falso e o LibreOffice (a lista da trava) iniciados. Cada conferência fica em `conferencias`. |

(Passo 64, Tarefa 14: o brief previa `57_fluxo_completo`, mas 57 a 63 já são da legislação (Tarefas 17 e 18); o final é o último + 1.
Desde a Tarefa 14 a trava de programas vale **desde o passo 1** (antes só a partir do 49) e, desde a onda final, para tudo o que o editor
inicia (`TravaDeProgramas`): o agente pelo `ExecucaoProcesso` e o LibreOffice dos despachos pelo `ConversorLibreOffice`. A lista do
roteiro é a pasta `tests\saida\agente-falso` e o `soffice` exato que `ConversorLibreOffice.Localizar()` acha; qualquer outro programa é
recusado antes de iniciar. Cada programa que de fato partiu é registrado: o resultado traz `programas_iniciados` (programas, a lista
permitida, número de inícios e `fora_do_agente_falso`, que conta as partidas do LibreOffice dos passos 44 e 64). O 64 confere que só
programas da lista partiram e que o LibreOffice aparece nela. Independência: os passos 58–63, 30 e 48 conferem no início se o estado deixado pelo anterior é o
esperado e, se não for (o anterior falhou), refazem-no pelo próprio app (excluir tudo e anexar de novo, renomear a lei) e registram
`estado_refeito`; o 52 refaz o que o 51 deixa (0001 escolhido, 4 anexos e destinos). Os outros passos já importam ou abrem o próprio
pacote. O `tools\roteiro.ps1` apaga o `bin\` e o `obj\` que o `dotnet publish` do agente falso deixa em `tests\AgenteFalso`, para nada
ficar fora de `tests\saida`. A conferência "nada gravado fora" percorre o repositório e só aceita `tests\saida\roteiro`,
`tests\saida\dados-app` e `tests\saida\agente-falso`; ignora só, na raiz, o `.git` e o `.superpowers` (progresso e relatórios dos agentes
de desenvolvimento, que podem ser gravados enquanto o roteiro roda), o `web\paginador\node_modules` e o `desktop.ini` do OneDrive.
As fotografias de atributos (anexos dos passos 51–56 e 64, originais da legislação) ignoram os bits de placeholder do OneDrive (fixado, não
fixado, recuperar ao abrir/acessar): `tests\saida` fica no OneDrive e ele os muda sozinho; conteúdo, tamanho, datas e os demais atributos
continuam conferidos.)

(Passos 30, 61, 62, 63 e 48, Tarefa 18, nessa ordem, depois do 60: o brief previa 30 e 45–48, mas 41–47 já são das ondas de
correções; o 30 e o 48 eram reservados à Legislação e os outros três seguem o último número em uso. O leitor é o WebView2 do módulo, com
o PDF.js real; os testes sem a janela estão em `tests\Pmvni.Web.Testes\LeitorLegislacaoTestes.cs` (controller oculto) e
`tests\Pmvni.Revisao.Testes\{FaixasTestes,AtalhosTestes,ComandosLegislacaoTestes}.cs`.)

(Passos 57 a 60, Tarefa 17: o brief previa 41 a 44, mas 41 a 47 já são das ondas de correções e 49 a 56 das Tarefas 19 e 20; o 30 e
o 48 ficam reservados à Tarefa 18. O roteiro usa **só os PDFs fictícios** de `tests\corpus\legislacao` (gerados por `FixturesTestes`),
copiados para `tests\saida\roteiro\legislacao-originais` (a NT marcada só leitura); a pasta real de legislação do usuário nunca é lida.
Sha, tamanho, `LastWriteTimeUtc` e atributos de cada original são registrados antes e conferidos no fim de cada passo
(`originais_intactos`). O seletor múltiplo responde listas fixas (`PerguntasFixas.ListasParaAbrir`), o de um arquivo responde
`ArquivoParaAbrir` (Substituir) e as perguntas de Excluir têm resposta por título; cada pergunta fica registrada. O extrator é o real
(PDF.js num WebView2 oculto e InPrivate sobre a janela do app). A leitura dos PDFs corre fora da fila de comandos: só o seletor e as
alterações do catálogo entram nela.)
(Passos 51 a 56, Tarefa 20: Novo relatório sobre outra cópia da árvore fictícia, em `tests\saida\roteiro\processos-agentes` (Ajustes › Pasta de processos
aponta para ela), com anexos fictícios gerados pelo próprio roteiro em `tests\saida\roteiro\anexos`. O roteiro só usa o agente falso: a execução real, com
o Codex e o Claude Code do computador, é a conferência humana da Tarefa 14 (Step 7). O orquestrador do roteiro lê o cenário por `AGENTE_FALSO_CENARIO` e
consulta a situação e o log a cada 200 ms. O fechamento com o Claude rodando é exercitado sem fechar a janela: `PodeFecharComAnalisesAsync` e, em seguida, o
que o fechamento deixa (o editor para de acompanhar; a análise do kit segue).)

(Passos 49 e 50, Tarefa 19; o 48 fica reservado. Os passos 49 a 56 usam o **agente falso** (`tests\AgenteFalso`, publicado por `tools\roteiro.ps1` com
`dotnet publish -c Release -o tests\saida\agente-falso` antes de abrir o app): o mesmo `AgenteFalso.exe` faz o papel do `codex.exe`, do `claude.exe`
(uma cópia dele na mesma pasta) e do `python.exe`, sem rede e sem IA. O roteiro troca `MainWindow.OpcoesAgentes` para apontar para ele, com o
`CODEX_HOME` numa pasta do roteiro (`config.toml` com `[plugins."analise@pmvni-kit"]` `enabled = true`, `plugins\cache\pmvni-kit\analise\2.4.0` e um
`pro_runtime.py` fictício de `analise-pro\1.6.1` com a âncora `API paga desativada no analise-pro.`); o kit real só é lido para a versão do `plugin.json`.
`ExecucaoProcesso.SomenteExecutaveisEm` aponta para a pasta do agente falso: qualquer programa fora dela é recusado antes de iniciar, então o roteiro nunca toca
o codex nem o claude de verdade nem guarda uma chave real. Os comandos de Ajustes entram na mesma fila de comandos dos outros módulos. A chave do passo 50 é
falsa e aleatória, nunca aparece no resultado; o `.dpapi` fica em `<dados>\segredos`, dentro de `tests\saida\dados-app`.)

(Numeração da Tarefa 16: o plano previa 34 a 38, mas 34 e 35 já são da Tarefa 13; os passos do Novo despacho são 36 a 40. O
roteiro usa o kit no lugar (só lê o registro, os modelos e as referências do plugin; nada é copiado) e o LibreOffice real
(`ConversorLibreOffice.Localizar()`, com perfil e `TEMP`/`TMP` na pasta de trabalho dentro de `tests\saida\dados-app\despachos`): o
roteiro roda fora do pacote do Claude, aberto pelo `explorer.exe`. O LibreOffice aborta (0xC0000409) quando o perfil fica fundo demais
(o arquivo mais fundo do perfil fica 134 caracteres abaixo dele e o Windows corta em 260); a pasta de dados do roteiro é funda (dentro
do projeto no OneDrive), então o conversor passa ao LibreOffice o nome curto (8.3) do perfil e do TEMP e recusa, com o motivo, um perfil
que ainda passe de 120 caracteres. Os testes (`dotnet test`) nunca abrem o LibreOffice: usam conversores
falsos.)

(Numeração: o plano previa 28 e 29 para Processos e Despachos e 31 a 33 para o resto; 28 e 29 já são Recentes e Comparar e o 30
fica reservado à Legislação (Tarefa 18). Os passos 31 a 35 são os da Tarefa 13.)

(Os passos 26 a 29 são os da Tarefa 11; o plano os previa como 24 a 27, mas 24 e 25 já eram a trava e o conflito da Tarefa 10.
Cada etapa que começa e termina fica em `tests\saida\roteiro\progresso.log`, para achar onde um roteiro travou.)

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
antiga de legislação ignorada); Tarefa 19: `AjustesTestes.ajustes_dos_agentes` (modo, programa e aviso dos agentes guardados em `ajustes.json`, valor inválido volta ao padrão, nunca a chave de API). `PastasTestes` (filtro de despachos pelo nome do arquivo). Tarefa 16: `FaixasTestes` ganhou `novo_despacho_nos_tres_modulos_com_os_dois_tipos`
e `gerar_pdf_so_em_despachos`; `AjustesKitTestes` (pasta do kit: `PMVNI_KIT_RAIZ` > Ajustes > padrão, guardada em `ajustes.json`);
`FluxoNovoDespachoTestes` (origem sugerida por Relatórios, Processos e Despachos; motivo do tipo desabilitado; poder gerar e gravar;
trocar a etapa mantém o que foi editado).

## O que ainda precisa de olhar humano

- O Novo despacho com o mouse e o teclado de verdade (cartões das exigências, mover acima/abaixo, a folha de prévia em Verdana nos dois
  temas, as páginas do PDF na escala do monitor) e os diálogos "Emitir a aprovação?" e "Descartar o despacho em edição?" (o roteiro
  responde fixo); o seletor de pasta do kit nos Ajustes; a dica do item desabilitado do menu ao passar o mouse.
- Fechar o app pelo X depois de conferir as páginas de um despacho: com o driver de vídeo da Intel, a descarga das DLLs girava sem fim
  depois de o `Windows.Data.Pdf` desenhar páginas (visto no roteiro); o app agora termina o processo direto no fim, só nesse caso. Vale
  conferir no Gerenciador de Tarefas que nenhum "Editor de Relatorios PMVNI" sobra depois de fechar.

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
