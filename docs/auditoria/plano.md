# Lista consolidada de pendências — Editor de Relatórios PMVNI 0.4.0

Atualizada em 17/11/2026. Junta:
- o plano da 1ª auditoria ([interface.md](interface.md), I-n; [bugs.md](bugs.md), B-n);
- a 1ª e a 2ª rodada da revisão de código e a revisão de segurança;
- a 2ª revisão de interface: capturas novas em 1366 × 768 a 125% e maximizada, nos dois temas, com auditorias de design system, acessibilidade e textos.

**Situação:** ✅ feito nesta entrega · ◐ feito em parte (o que falta está escrito) · ⏳ fica para depois.
**Prioridade:** C = crítico · I = importante · D = detalhe.

## 1. Gravação e dados (planilha, despacho, compactados)

| # | Prio | Tela / arquivo | Problema | O que foi feito | Situação |
|---|---|---|---|---|---|
| 1 | C | Acompanhamento | Alteração perdida com a planilha aberta no Excel (B-4, I-4) | Fila de alterações pendentes: grava uma por vez, fica guardada em disco (sobrevive a fechar o editor) e tenta de novo a cada 15 s. Aviso fixo "N alterações aguardando gravação" com "Gravar agora". | ✅ |
| 2 | C | GravadorPlanilha | Excluir apagava a fórmula mestra compartilhada (B-1) | A exclusão não mexe em células com fórmula. Gravar valor na mestra passa a fórmula para cada célula do grupo. | ✅ |
| 3 | C | Acompanhamento | Preenchimento automático podia gravar por cima do digitado (B-2) | Cada valor só entra se a célula ainda estiver vazia na hora de gravar. A nota "Preenchido sozinho" só aparece para o que de fato entrou. | ✅ |
| 4 | C | GravadorPlanilha | Regravava no próprio arquivo, com risco de corromper (B-5) | Grava num `.tmp`, confere relendo e só então troca (`File.Replace`). Temporários de quedas antigas são limpos. | ✅ |
| 5 | I | Acompanhamento | "Editar processo" regravava em MAIÚSCULAS e mudava a data (B-3) | Compara sem diferenciar maiúsculas só nos campos que o formulário põe em maiúsculas. | ✅ |
| 6 | I | Acompanhamento | Gravações simultâneas davam "aberta no Excel" sem o Excel aberto (B-8) | Uma gravação por vez. | ✅ |
| 7 | I | Acompanhamento | Cópias de segurança no mesmo segundo se sobrescreviam (B-12) | Nome com milissegundos e contador, uma pasta por planilha, e as cópias da 0.3.0 entram na limpeza. | ✅ |
| 8 | I | Acompanhamento | Sempre a primeira aba (B-14) | A aba é escolhida pelo nome e lembrada, com seletor quando há mais de uma. Se a aba sumir, o app só mostra (não grava) e avisa. | ✅ |
| 9 | I | Acompanhamento | Erro passageiro (OneDrive) descartava a alteração | Erro de disco deixa a alteração na fila; só um conflito (linha ou aba sumiu) descarta, com o aviso "Alteração não gravada". | ✅ |
| 10 | I | Acompanhamento | Pendentes guardadas ignoravam a aba | Cada pendente grava na aba em que foi feita. | ✅ |
| 11 | I | Novo despacho | Trocar tipo/relatório/"Sem relatório" apagava o texto escrito (B-6) | O texto escrito passa para o rascunho novo, e o resumo automático da IA não passa por cima dele. | ✅ |
| 12 | I | Novo despacho | "Nova pasta" aceitava `..`, `.` ou vazio (B-7) | Esses nomes são recusados e o caminho final é conferido. | ✅ |
| 13 | I | Novo relatório | A limpeza de 1 dia apagava compactados ainda em uso (B-9) | A pasta da sessão aberta nunca é limpa. | ✅ |
| 14 | I | Compactados | .zip com nomes repetidos era recusado inteiro (B-10) | O segundo vira "nome (2)". | ✅ |
| 15 | I | Compactados | Limite de 4 GB só checado depois de extrair (B-11) | No .zip conta os bytes escritos; no .rar/.7z mede a pasta a cada 3 s e para o tar. | ✅ |
| 16 | D | GravadorPlanilha | LOG10 virava LOG34 ao copiar fórmula (B-13) | Corrigido; testado. | ✅ |
| 17 | D | Acompanhamento | Vigia da planilha silenciado por 4 s depois de falha (B-15) | Só conta gravação que deu certo. | ✅ |
| 18 | D | Acompanhamento | Dois menus por linha a cada redesenho (B-17) | O menu é montado só quando é pedido. | ✅ |
| 19 | D | GravadorPlanilha | Gravação segura num código comum com o relatório (B-16) | Mesmo mecanismo (`.tmp` + conferência + troca), mas sem um código compartilhado entre os dois projetos. | ◐ |

## 2. Interface: telas e tamanhos

| # | Prio | Tela | Problema | O que foi feito | Situação |
|---|---|---|---|---|---|
| 20 | C | Acompanhamento | Em 1366 × 768 a tabela sumia (I-1). Na 2ª rodada ainda ficavam 1 ou 2 linhas. | Ações no cabeçalho; resumo compacto abaixo de 720 de altura e recolhido sozinho abaixo de 560; tabela com altura mínima; botão "Resumo" lembrado nos Ajustes. | ✅ |
| 21 | C | Acompanhamento | Painel do processo sobreposto ficava só com o título e os botões no notebook (nova) | Sobreposto, o painel cobre dos avisos até o rodapé. | ✅ |
| 22 | C | Editor | Folha a 25% em 1366 × 768 (I-2) | Propriedades recolhe sem seleção (e volta ao selecionar item ou linha); a lista de seções estreita e volta ao maximizar; "Ajustar largura" sem rolagem lateral. | ✅ |
| 23 | C | Trilho | "Acompanhamento" cortado e Ajustes sumindo (I-3) | Trilho mais largo (104), "Acompanhamento de processos" em duas linhas, módulos com rolagem e Ajustes fixo. | ✅ |
| 24 | I | Acompanhamento | O painel escondia Status e Aprovação (I-5) | Colunas na ordem Nº, Processo, Status, Requerente…; colunas vazias saem (aviso no rodapé); Nº e Processo fixos ao rolar para o lado; painel sobreposto em telas estreitas. | ✅ |
| 25 | I | Barra de título | Comandos cortados em 1366 px (I-6) | Abaixo de 1300 ficam só os ícones, o selo fica só com o ícone e o requerente sai do cabeçalho (fica na dica). | ✅ |
| 26 | I | Barra de título | Processo e "Salvo" do relatório apareciam no Novo despacho de outro processo (nova) | Só aparecem no módulo Relatórios. | ✅ |
| 27 | I | Barra de título / faixa | Controles que não valiam no módulo; duas buscas no Acompanhamento (I-8) | Desfazer/Refazer só em Relatórios, Anterior/Próxima em Relatórios e Legislação, pesquisa escondida onde não há; a pesquisa da barra filtra a planilha; Ajustes sem a aba Arquivo. | ✅ |
| 28 | I | Acompanhamento | Comandos repetidos e com nomes diferentes (I-7) | "Novo processo" é o botão principal da faixa; "Escolher planilha…" com um nome só; Exportar e Filtrar ao lado do título. | ✅ |
| 29 | I | Todas | Cor de destaque mudando entre telas e temas (I-9) | Verde institucional como destaque do app (App.xaml). | ✅ |
| 30 | I | Acompanhamento | Pizza sem legenda; status zerado ocupando cartão (I-10) | Cores mais distintas, bolinha de cor em cada cartão, zerados fora, valores na dica. | ✅ |
| 31 | I | Novo relatório | Destino cortado e "Escolha o processo." repetido (I-11) | Aviso único, coluna de destino mais larga com dica, lixeira, agrupado por compactado e botão "Cancelar abertura". | ✅ |
| 32 | I | Editor | Lista de itens pouco densa (I-12) | Uma linha por item; Ajustes › "Lista de seções": Compacta ou Confortável. | ✅ |
| 33 | I | Várias | Cores lidas do tema do Windows em código: com o app noutro tema, fundos e textos trocados (design system, crítico) | Estilos novos em Estilos.xaml: cartão, selos, texto de destaque e de erro, ícones, fundo de janela, divisórias. Aplicados em Novo relatório, Novo despacho, Imóveis, Acompanhamento, editor de figuras, lista de leis, pizza, pílulas e seleção da lista. | ✅ |
| 34 | I | Processos | Vazio sem orientação (I-15) | Frase maior e centralizada. | ✅ |
| 35 | I | Acompanhamento | Filtro sem resultado e planilha vazia só no rodapé | Frase e "Limpar filtros" na área da tabela. | ✅ |
| 36 | D | Ajustes | "Verificar atualizações" desativado com pouco contraste (I-17) | Estilo padrão. | ✅ |
| 37 | D | Várias | Títulos de página com 5 tamanhos | `PmvniTituloPaginaStyle` em Acompanhamento, Novo relatório, Novo despacho e Imóveis. | ✅ |
| 38 | D | Estilos | Cartões em 3 estilos duplicados, etiquetas em 5 versões, estados vazios com 2 aparências | Criados `PmvniCartaoStyle` e os selos; a troca em todas as telas fica para depois. | ◐ |
| 39 | D | Editor | Itens excluídos na folha no tema escuro (I-21) | Revisto no código: branco sobre #C42B1C, contraste 5,7:1. Nada a mudar. | ✅ |

## 3. Acessibilidade e textos

| # | Prio | Tela | Problema | O que foi feito | Situação |
|---|---|---|---|---|---|
| 40 | C | Diálogo de recuperação | Esc descartava as alterações recuperáveis | Esc recupera; só o botão "Descartar" descarta (e o texto diz que guarda na pasta de recuperação). | ✅ |
| 41 | C | Acompanhamento | Conflito descartava sem dizer | Aviso "Alteração não gravada" com o motivo. | ✅ |
| 42 | I | Acompanhamento | Aviso de pendentes contraditório e como erro | Aviso (amarelo): "Feche-a: o app grava sozinho em até 15 segundos". | ✅ |
| 43 | I | Acompanhamento | Menu da linha sem teclado; Narrador lia a linha mal; ordenação só por ícone | Enter edita, Delete exclui, tecla Menu/Shift+F10 abre o menu; nome da linha no item; ordenação com nome e dica ("em ordem crescente…"). | ✅ |
| 44 | I | Acompanhamento | "Enviado Ao Requerente" | Preposições em minúsculas. | ✅ |
| 45 | I | Várias | Erros técnicos crus (em inglês) | Mensagens traduzidas no Acompanhamento, no Novo relatório e nos avisos da IA. | ✅ |
| 46 | I | Diálogos | "Editar processo" sem foco no erro; "Criar processo" sem motivo; excluir com jargão | Erro anunciado e foco no campo; motivo visível; "Excluir o processo X da planilha?" com "Excluir processo" / "Manter". | ✅ |
| 47 | I | Novo despacho | "Criar automaticamente" substituía com Enter; títulos "IA"; jargão DOCX/.RPMVNI | Confirmação destrutiva ("Substituir"); títulos que dizem o que houve; "Word (.docx)", "RELATÓRIO BASE", "3 · Conferência", "Cancelar geração". | ✅ |
| 48 | I | Barra de título | Narrador lia "Processo aberto" no lugar do número; mudança de "Salvo" não anunciada | Nomes com o conteúdo; selo anunciado (LiveSetting). | ✅ |
| 49 | I | Novo relatório / Ajustes | Mensagens que mudam sem anúncio | LiveSetting nas recusas, no andamento da análise e no estado da atualização. | ✅ |
| 50 | I | Ajustes | "Restaurar" não avisava que limpa pastas e assinatura; "Copiar relatório" confundia | Texto completo; "Atualizar/Copiar diagnóstico" com "Copiado". | ✅ |
| 51 | I | Editor / textos | ID interno, "Composto", "Importar dados.json", "plugin analise", "Concordo e analisar", concordância "chegou" | Trocados (ID na dica; "21 folhas"; "Importar análise da IA…"; "Pelo Codex · kit análise"; "Concordar e analisar"; "chegaram"). | ✅ |
| 52 | D | Acompanhamento e outros | Botões menores que 32 × 32 | Fechar o painel e "…" com 36 × 36; outros botões de ícone ficam para depois. | ◐ |
| 53 | D | Novo despacho | Caixa da instrução para a IA sem foco visível; espera não bloqueia o Tab | Não feito. | ⏳ |
| 54 | D | Geral | Tamanhos fixos que cortam com "Tamanho do texto" do Windows acima de 100% | Trilho e cartões passaram para altura mínima; o resto não foi revisto. | ◐ |

## 4. Não verificado nesta entrega
- Escala do Windows em 175% e dois monitores: não há como simular nesta máquina.
- Navegação só pelo teclado e o Narrador: implementados pelo código, não percorridos com o leitor de tela.
