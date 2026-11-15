# Etapa 2 — Revisão básica e controles do app (desenho)

Data: 14/11/2026. Base: especificação `docs/especificacao/2026-11-14-editor-relatorios-concepcao.md`
(seções 3, 5.4, 6 e 11) e a Etapa 1 concluída na branch `etapa-1/fidelidade-documental`.
Aprovado pelo usuário em 14/11/2026 ("Continue até o final agora", após ver o esboço dos controles).

## 1. Intenção

A Etapa 1 mostra e exporta o relatório da IA exatamente como o kit. A Etapa 2 permite **revisar**:
incluir/excluir itens, mudar a situação, corrigir textos, comparar com o original, desfazer, salvar
com histórico e recuperar após queda. Para isso o app ganha os **controles** que faltam, no estilo
do Outlook novo e da Microsoft Store (pedido do usuário, com imagens de referência): faixa de opções
em uma linha com abas, trilho de ícones à esquerda, painel de conteúdo ao lado, propriedades à
direita. As folhas A4 continuam sendo o centro e a coisa mais importante da tela.

## 2. Objetivos, não objetivos, anti-objetivos

Objetivos:
- Pacote `.pmvrel` (ZIP) com o original imutável, o estado revisado e o histórico.
- Importar o `dados.json` do kit (o formato atual) para um `.pmvrel` novo.
- Editar: situação, inclusão/exclusão (reversível), textos livres do item no próprio A4,
  justificativa da mudança de situação, ver/restaurar original.
- Desfazer/refazer ilimitado na sessão; histórico permanente (append-only) no pacote.
- Autosave + diário de recuperação; trava contra edição simultânea; detecção de arquivo mudado
  no disco (OneDrive) sem sobrescrever.
- Numeração de itens sempre coerente após excluir/restaurar; citações "item NN" acompanham.
- Controles: barra de título, faixa de opções com abas, trilho, painéis, menu de contexto, atalhos.
- A exportação continua idêntica à tela e ao kit para os mesmos dados efetivos (V1–V4 seguem verdes).

Não objetivos (outras etapas): cálculos e tokens `{{calc:}}` (3), diagramas e pranchas (3–4),
despachos (5), JSON Schema público/validador de linha de comando e testes de segurança completos
do pacote como zip slip avançado e SVG (6), merge no kit (7). Editar capa, fontes, conclusão e
assinatura fica para depois: nesta etapa só os itens são editáveis (o resto é exibido como veio).

Anti-objetivos (seria fracasso mesmo funcionando):
- Qualquer mudança de paginação causada pelo editor que não venha dos dados (marcas de revisão,
  chips e seleção nunca entram no fluxo do A4 nem no PDF).
- Perder trabalho: queda, conflito ou erro de gravação nunca apagam a última versão boa.
- Botões que não fazem nada ("em breve"): só aparece o que funciona.
- Alterar o original recebido da IA.
- Inventar dados ou fundamentos (regra do kit): o editor não preenche nada sozinho.

## 3. Restrições herdadas

Executável único self-contained, asInvoker; WebView2 InPrivate; host virtual com Deny; todo HTML
do relatório é conteúdo não confiável; nada sai da máquina (emenda só para o Novo relatório: §12); dados reais só locais; regras do
CLAUDE.md do usuário (nada em pastas virtualizadas, explorer.exe, Recriar PC, limpeza de builds).
Exportação final continua recusando campos `[ ]`, estouro e arquivo mudado desde a abertura.

## 4. Abordagens consideradas

A. **Estado revisado → `dados.json` efetivo → renderizador da Etapa 1** (escolhida). O pacote guarda
original + revisões por campo; uma função pura aplica as revisões e produz um `dados.json` no
formato do kit (itens excluídos removidos da lista). Esse JSON passa pelo mesmo `RelatorioRenderer`
e paginador. Fidelidade garantida por construção: mesmos dados efetivos ⇒ mesmo HTML que o kit.
Cada edição recompõe (o paginador já mede o custo: baseline em notas-tecnicas).

B. Editar o DOM do relatório e serializar de volta. Rejeitada: quebra V1 (HTML byte a byte), mistura
conteúdo não confiável com estado, e o kit não teria como reproduzir.

C. Novo modelo de documento próprio com renderizador novo. Rejeitada: refaz a Etapa 1 e perde a
paridade com o kit antes do merge.

Controles: **faixa de opções com abas** (escolhida) em vez de uma `CommandBar` única — são ~20
comandos; numa barra só ficariam no menu de estouro. Referência visual: Outlook novo.

## 5. O que foi decidido

### 5.1 Pacote `.pmvrel`
ZIP com: `manifesto.json` (versao_esquema "1.0.0", app/versão, template rev10 + sha do css,
sha256 de cada parte), `original/recebido.json` (bytes exatos importados, imutável),
`projeto.json` (estado revisado), `historico.jsonl` (uma linha por comando aplicado: data UTC,
usuário Windows, tipo, alvo, antes, depois), `assets/<sha256>.<ext>` (imagens referenciadas pelo
dados.json, copiadas na importação; caminhos reescritos para o asset).
`projeto.json`: `itens[]` com `id` estável (`it_` + 26 caracteres ULID), `ordem`, `incluido`,
`situacao {original, revisada, justificativa}`, `campos {titulo, verificacao, providencia,
base_legal, referencia: {original, revisado|null}}` e os demais campos do item copiados como
vieram (não editáveis); o resto do dados.json (config, capa, fontes, conclusão, assinatura, etc.)
fica em `projeto.json.documento` como veio.
Leitura segura: só entradas conhecidas; nomes normalizados (sem `..`, absolutos, `:` ou `\`);
limite por entrada (50 MB) e total (500 MB); razão de compressão ≤ 100; imagens conferidas por
assinatura de bytes (PNG/JPEG/WebP/GIF). Versão de esquema maior que a conhecida ⇒ só leitura.
Gravação segura: `nome.pmvrel.tmp` na mesma pasta → flush → reabrir e validar → `File.Replace`
com `nome.pmvrel.bak`. Antes de gravar compara sha/data do arquivo no disco com o que foi aberto;
se mudou, não sobrescreve: InfoBar "Conflito" com "Salvar como cópia" e "Recarregar".
Trava: `.~nome.pmvrel.lock` ao lado (máquina, usuário, PID, batimento a cada 30 s; trava com
batimento > 2 min é considerada abandonada). Segunda instância abre só leitura.
Autosave: diário de comandos em `<dados>\recuperacao\<sha do caminho>.jsonl` a cada comando;
gravação do pacote após 30 s sem edição, ao trocar de arquivo e ao fechar. Na abertura, diário
mais novo que o pacote ⇒ oferece recuperar (aplica os comandos) ou descartar.

### 5.2 Dados efetivos e numeração
`DadosEfetivos(projeto)`: itens com `incluido=true` na `ordem`, cada campo = revisado ?? original,
situação = revisada ?? original; resto do documento como veio. Resultado serializado e entregue
ao renderizador exatamente como um dados.json do kit. Teste: projeto recém-importado ⇒ dados
efetivos geram HTML byte-idêntico ao do dados.json original.
Numeração: o modelo numera por posição (ITEM 01…). Ao importar, citações inequívocas
"item NN"/"ITEM NN" (NN dentro do intervalo de itens) nos textos dos itens e na conclusão viram
token interno `{{ref:it_…}}`; nos dados efetivos o token é impresso com o número atual
("item 07" mantendo a caixa e os 2 dígitos do original). Faixas ("itens 03 a 07"), listas e
casos ambíguos ficam literais; quando uma exclusão/restauração muda numeração, a aba Revisão
lista esses textos literais afetados como aviso (não bloqueia). Citação de item excluído ⇒ aviso
na conferência e confirmação na exportação.

### 5.3 Comandos, desfazer e histórico
Toda alteração é um comando (`DefinirSituacao`, `DefinirJustificativa`, `DefinirCampo`,
`RestaurarCampo`, `Excluir`, `Restaurar`, `RestaurarItemOriginal`) com aplicar/desfazer puro sobre
o modelo. Pilhas de desfazer/refazer na sessão; histórico permanente no pacote. Situação revisada
diferente da original exige justificativa antes de exportar (vira erro da conferência final, não
bloqueio da edição).

### 5.4 Edição no A4
Clique numa folha seleciona o bloco pelo `data-ref` (já existe no paginador) e envia
`selecionar {ref}` ao host. Duplo clique (ou "Editar texto") num campo de texto livre do item
selecionado entra em edição: a página marca o elemento com `contenteditable="plaintext-only"`,
Enter confirma, Esc cancela; a página envia `editar {ref, campo, texto}`; o host valida, cria o
comando e recompõe. Campos estruturados (situação, inclusão) só pelo painel e pela faixa.
O mapeamento elemento ↔ campo vem de um atributo `data-campo` que o editor acrescenta **só na
cópia de sessão** (não no HTML exportado; V1 compara o HTML de exportação).
Marcas de revisão (ligáveis): item alterado ganha um traço azul na margem cinza fora da folha;
item excluído aparece como "◦ excl. 07" na margem, na altura onde estava. Nada disso entra no PDF.

### 5.5 Controles
- **Barra de título** (TitleBar própria, Mica): ícone, "Proc. NNNN/AAAA · Requerente", estado de
  gravação (Salvo HH:MM / Salvando… / Não salvo / Conflito / Somente leitura), Desfazer, Refazer,
  caixa "Pesquisar no relatório" (realça e navega ocorrências nas folhas, Enter = próxima).
- **Faixa de opções** (uma linha, abas, ícones Segoe Fluent + rótulo; colapsa rótulos em
  janelas estreitas):
  - Arquivo: Importar dados.json, Abrir, Salvar, Salvar como, Exportar PDF, Recentes (10), Fechar.
  - Página Inicial: Situação ▾, Excluir/Restaurar item, Editar texto, Original ⇄ (alterna a
    visão do item para o original, só leitura), Restaurar original, ◂ Item, Item ▸,
    Próximo pendente (situação ≠ ATENDE ou justificativa faltando), Exportar PDF.
  - Revisão: Marcas de revisão (alternar), Somente alterados (filtra o painel), Comparar com o
    original (lista de diferenças por item), Conferência (erros finais + avisos de numeração).
  - Exibir: −, +, Ajustar largura, Página inteira, Painel lateral, Propriedades, Tema (Sistema/
    Claro/Escuro — só o "chrome"; folhas sempre brancas).
  - Ajuda: Atalhos de teclado, Sobre (versões do app, modelo rev10, WebView2).
- **Trilho** (estilo Outlook/Store, ícone + rótulo): Relatório (seções e itens, como hoje, com
  ícone de situação e marcador "alterado"), Itens (lista plana com filtros), Excluídos (n) com
  Restaurar, Histórico (lista do historico.jsonl, mais recente primeiro); Configurações embaixo
  (tema, pasta de recuperação, intervalo de autosave). Clicar troca o painel de conteúdo.
- **Propriedades** (direita, recolhível, ~320 px): item selecionado: número atual e id, No PDF,
  Situação (original e revisada), Justificativa, campos com indicador "alterado" e botão
  restaurar por campo, histórico do item.
- **Menu de contexto** na folha (botão direito sobre item): Situação ▸, Editar texto, Excluir,
  Original ⇄, Propriedades.
- **Atalhos**: Ctrl+O abrir, Ctrl+I importar, Ctrl+S salvar, Ctrl+Shift+S salvar como,
  Ctrl+E exportar PDF, Ctrl+Z/Ctrl+Y, Ctrl+F pesquisar, Alt+↑/↓ item anterior/próximo,
  Delete excluir item, F2 editar texto, F6 alternar regiões, Ctrl+0 ajustar largura,
  Ctrl+ +/− zoom. Os atalhos do navegador seguem desligados; a página repassa ao host só os
  atalhos desta lista.
- Fechar com alterações não gravadas: salva sozinho (autosave); só pergunta se a gravação falhar.

## 6. Prova de pronto
Testes de pacote (gravação interrompida no meio ⇒ arquivo antigo intacto; conflito detectado;
recuperação do diário; trava abandonada; zip com `..` recusado), comandos com desfazer/refazer,
dados efetivos byte-idênticos após importação, numeração e tokens após excluir/restaurar,
excluídos recuperáveis, V1–V4 da Etapa 1 verdes, roteiro no app real cobrindo importar → editar →
excluir → desfazer → salvar → reabrir → exportar, autoteste e build único verdes, Recriar PC
conferido.

## 7. Deixado para o construtor (decisões dele)
Estrutura interna de classes, nomes, como a faixa de opções é montada em XAML (controle próprio
sobre `SelectorBar` + `CommandBar` é aceitável), ícones exatos, a forma de realçar a pesquisa, e
o formato exato das mensagens novas da ponte (seguindo o envelope `{v:1,tipo,id,dados}`).

## 8. Respostas à conferência do construtor (decisões minhas, registradas)

1. **Texto editado é texto puro.** O modelo imprime os campos com `html.escape` (sem marcação
   interna). Edição devolve string pura; quebras de linha do `plaintext-only` viram espaço simples
   (o modelo não tem `<br>`); espaços nas pontas são removidos; texto vazio é permitido só em
   `providencia`/`base_legal`/`referencia` (a conferência do kit decide se é erro). Nada de HTML.
2. **Campos ausentes.** Só os 5 campos de texto (`titulo`, `verificacao`, `providencia`,
   `base_legal`, `referencia`) são editáveis. Campo ausente no original pode ser criado pela
   edição. Sem revisão, `DadosEfetivos` reproduz exatamente a forma original (ausente continua
   ausente, null continua null, ordem das chaves preservada) — é o que garante o byte-idêntico.
   A forma `{original, revisado}` existe só no `projeto.json`, nunca no dados efetivo.
3. **Original ⇄ e marca de excluído.** "Original ⇄" mostra o original **no painel de
   Propriedades** (lado a lado com o revisado), nunca no A4. A marca "◦ excl. 07" é desenhada
   pela página na margem cinza, ancorada no `data-ref` do **item incluído seguinte** (ou do
   anterior, se for o último); isso é um acréscimo pequeno no `tela.ts`, fora das folhas,
   sem tocar no fluxo nem no plano.
4. **Ponte e estado.** O host é o dono do estado. A cada comando: recompõe por inteiro (o
   relatório recarrega a cópia de sessão), e a página restaura rolagem pela âncora (data-ref
   + deslocamento dentro dele) e a seleção pelo data-ref. Meta de latência: ≤ 1,5 s para o
   longo no PC atual, medida e registrada em notas-tecnicas; recomposição incremental fica
   para depois se a meta falhar. Edição em andamento: enquanto um campo está em edição não há
   recomposição.
5. **Citações.** Conversão conservadora por padrão fechado: só `item NN` / `ITEM NN` / `Item NN`
   com NN de 1 a 3 dígitos, palavra inteira, **não** seguido de `do`/`da`/`de`/`dos`/`das` + norma,
   artigo, lei, NBR, anexo ou tabela (ex.: "item 3 do art. 5" fica literal), e NN ≤ número de
   itens. Tudo o mais fica literal. O `original/recebido.json` nunca muda; o token existe só no
   `projeto.json`; a lista do que foi convertido fica no histórico (comando `ImportarCitacoes`)
   e é visível na aba Revisão > Comparar.
6. **OneDrive.** O usuário guarda nas pastas de processos do OneDrive; aceito. `.tmp` e `.lock`
   ficam ao lado (o `File.Replace` exige o mesmo volume) e são marcados como ocultos; o `.tmp`
   vive só segundos. O `.bak` é mantido (uma versão). Conflito do OneDrive ("arquivo (1)")
   não é tratado além da detecção de mudança no disco.
7. **Ordem e corte.** Ordem do plano: pacote + importação → modelo/comandos/dados efetivos →
   ponte de seleção e edição no A4 → controles (faixa, trilho, painéis, propriedades, atalhos,
   menu de contexto) → autosave/recuperação/trava → pesquisa, tema, recentes, comparar →
   roteiro, build e entrega. Mínimo para "pronto": tudo da seção 6; pesquisa, tema, Recentes e
   Comparar entram no roteiro também (não ficam fora).
8. **Menores.** Histórico grava o nome de usuário do Windows (sem domínio) — o arquivo é
   documento interno da prefeitura e o autor da revisão é informação útil. Voltar a situação
   ao valor original zera `revisada` e a exigência de justificativa (a justificativa digitada
   fica guardada mas não é exigida). Desfazer grava no histórico um registro `desfeito` que
   aponta para o comando original (append-only; nada é apagado).

## 9. Trilho principal com os módulos do app (pedido do usuário, 14/11/2026)

Substitui o trilho da seção 5.5. O trilho à esquerda tem os **módulos principais**, como Email/
Calendário no Outlook: **Relatórios, Despachos, Processos, Legislação** e, embaixo, **Ajustes**.
Cada módulo troca a área principal inteira; a faixa de opções mostra as abas do módulo ativo.

- **Relatórios:** o editor desenhado nas seções 5.1–5.5. O painel ao lado das folhas ganha três
  abas no topo: Seções (árvore de seções e itens), Excluídos (n), Histórico.
- **Processos:** lista as pastas de processo da pasta de processos (padrão
  `%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\PROCESSOS`, configurável em Ajustes), no
  formato `<ano>\<nº> - <ano> - <requerente>` (aceita também `<nº>-<ano> - <requerente>`), mais
  recentes primeiro, com filtro por número ou nome. Ao escolher um processo, mostra: relatórios
  encontrados (`*.pmvrel` na pasta do processo e `dados.json` dentro de `.pmvni-ai\*\`), despachos
  (subpastas `DESPACHO NN - AAAA-MM-DD` com seus DOCX/PDF) e as pastas PROJETO/DOCUMENTOS.
  Ações: Abrir relatório (vai ao módulo Relatórios; `dados.json` é importado para um `.pmvrel`
  novo gravado na pasta do processo, com nome `Relatorio_<nº>_<ano>.pmvrel`), Abrir arquivo,
  Mostrar no Explorador. Somente leitura sobre a pasta: o app nunca move, renomeia ou apaga nada.
- **Despachos:** lista os despachos existentes (do processo selecionado ou de todos), com tipo
  (Correção/Aprovação pelo nome do arquivo), data e número; abrir DOCX/PDF no programa padrão.
  Emitir despacho: ver a seção 10 (pedido de 14/11/2026, que substitui a frase "a emissão continua
  sendo a Etapa 5").
- **Legislação:** leitor dos PDFs de leis que o próprio usuário anexa em Ajustes › Legislação, com
  pesquisa, seleção, cópia e citação. **Ver a seção 11** (pedido de 15/11/2026), que substitui a lista
  de arquivos de uma pasta de legislação (primeira versão desta seção) e a legislação compilada
  embutida (versão de 14/11/2026): não há pasta de legislação, pacote embutido nem leitura do kit.
- **Ajustes:** pasta de processos, pasta do kit (seção 10), **Legislação** (anexar, listar, editar,
  substituir e excluir os PDFs: seção 11.1), **Inteligência artificial** (conta ou chave de API, programas e
  situação do ChatGPT/Codex e do Claude: seção 12.8), tema, intervalo do salvamento automático, pasta de
  recuperação (somente leitura, informativo), versões (app, modelo, WebView2, PDF.js).

Abrir arquivos externos: só extensões de documento (`.pdf`, `.docx`, `.doc`, `.xlsx`, `.xls`,
`.png`, `.jpg`, `.jpeg`, `.dwg`, `.dxf`, `.txt`) pelo shell do Windows (`UseShellExecute`);
qualquer outra extensão (scripts, executáveis, atalhos) é recusada com aviso. Leitura das pastas
é feita fora da thread da interface e tolera pastas grandes e nomes fora do padrão (aparecem
com o nome da pasta como veio). Dados reais dessas pastas nunca entram em testes versionados:
os testes usam uma árvore fictícia em `tests/corpus/processos/`.

### 9.1 Faixa de opções por módulo (pedido do usuário)
A faixa de opções troca inteira quando o módulo do trilho muda (abas e comandos). "Arquivo" e
"Ajuda" existem em todos; a aba aberta por padrão é a segunda.
- **Relatórios:** Arquivo · Página Inicial · Revisão · Exibir · Ajuda (como na seção 5.5), com "Novo relatório" no
  início de Arquivo e de Página Inicial (seção 12.2; também em Processos › Página Inicial).
- **Despachos:** Arquivo · Página Inicial (Abrir, Abrir PDF, Mostrar no Explorador, Ir ao
  processo, Atualizar lista; filtro Todos/Correção/Aprovação) · Exibir (agrupar por processo ou
  por data, ordenar) · Ajuda.
- **Processos:** Arquivo · Página Inicial (Abrir relatório, Importar análise da IA, Abrir
  pasta no Explorador, Atualizar, filtro por ano) · Exibir (ordenar por número, data ou
  requerente; mostrar pastas PROJETO/DOCUMENTOS) · Ajuda.
- **Legislação:** a faixa da seção 11.5 (Arquivo · Página Inicial · Exibir · Ajuda, com os
  comandos do leitor de PDF).
- **Ajustes:** sem faixa de comandos (só a página de ajustes); Arquivo e Ajuda continuam no topo.
A caixa de pesquisa da barra de título também muda de alvo: no relatório, o texto das folhas;
em Processos, número ou requerente; em Despachos, o nome do arquivo; em Legislação, o texto da
lei aberta ou de todas as leis anexadas (seção 11.6).
O módulo e a aba ativos são lembrados entre sessões (Ajustes locais, não no pacote).

## 10. Novo despacho (pedido do usuário, 14/11/2026)

Pedido: no módulo Despachos, um botão **"Novo despacho"** em que o usuário escolhe o tipo ("Criar novo
despacho, e escolher que tipo"). A emissão sai da Etapa 5 e entra na Etapa 2. Esta seção substitui, para os
despachos, o não objetivo "despachos (5)" da seção 2 e a frase da seção 9 sobre a Etapa 5; substitui também, na
especificação geral §10, o PDF pela automação do Word (o kit proíbe: o PDF sai pelo LibreOffice) e a lista de tipos
(vale só o que o registro do kit tem). Continuam na Etapa 5: gestão de versões dos modelos, migração explícita e
qualquer tipo novo de despacho.

### 10.1 Tipos oferecidos
Exatamente os papéis de despacho do registro oficial do kit
`02 - MODELOS DE DOCUMENTOS\TEMPLATES_OFICIAIS.json` (revisão **10**, 13/11/2026; sha256 do arquivo do registro
conferido em 14/11/2026: `62da5040ffec7557b8050c099df6e135952e1bdef86f857f3dc74cfa48c2bd43`):

| Rótulo na interface | Papel | Modelo (relativo à raiz do kit) | sha256 do modelo |
|---|---|---|---|
| **Correção** | `correction` | `02 - MODELOS DE DOCUMENTOS/DESPACHO/MODELO DESPACHO CORREÇÃO.docx` | `856dcf9ee49da68f5a7ea440bda43dc5a6f5ab06453c501b94b9a222f4039d61` |
| **Aprovação** | `approval_simple` | `02 - MODELOS DE DOCUMENTOS/DESPACHO/MODELO DE DESPACHO DE APROVAÇÃO.docx` | `f5598577e2954726e3a2ce99d06fc728e6447dc461101e6c2df2280d82e152b7` |

O registro é a autoridade: o app lê o registro a cada abertura do "Novo despacho", exige revisão ≥ 10, resolve o
caminho **dentro** da raiz do kit e confere o sha256 do arquivo com o do registro; a revisão, o sha do registro e o
sha do modelo vão para o recibo. Papéis que não são de despacho (`report`) ou que não estão no registro do kit
(`approval_complete` existe só no registro empacotado do plugin; indeferimento e circular não existem) **não
aparecem**: nada é inventado, nada aparece como "em breve". Tipo cujo modelo falta, não confere ou não tem as
âncoras da seção 10.5 fica **desabilitado** no menu, com o motivo na dica. O app não sugere o tipo pelas situações.

### 10.2 Onde fica e como é o fluxo
- **Entradas:** "Novo despacho ▾" (Correção, Aprovação) na Página Inicial de **Despachos** (primeiro comando), de
  **Processos** (depois de "Importar análise da IA") e de **Relatórios** (depois de "Exportar PDF"). Todas abrem a
  mesma página **Novo despacho** dentro do módulo Despachos (o trilho passa a Despachos).
- **Origem (passo 1):** tipo (escolhido no menu, trocável), **processo** (pré-escolhido: o selecionado em
  Processos/Despachos, ou o processo cuja pasta contém o `.pmvrel` aberto em Relatórios; senão o usuário escolhe na
  lista de `Processos.Listar`), **relatório base** (um `*.pmvrel` da pasta do processo; o aberto em Relatórios vem
  marcado) e **pasta da etapa**. O despacho sempre usa a versão **gravada** do pacote: se o relatório base está
  aberto com alterações, o app grava antes (autosave); se a gravação falha, para com a mensagem.
- **Pasta da etapa:** regra do kit ("despacho da mesma análise reutiliza a pasta; etapa nova usa o número
  seguinte"). Sugerida: a última `DESPACHO NN - AAAA-MM-DD` que ainda não tem despacho (nenhum `.docx`/`.pdf` cujo
  nome comece por `Despacho_` ou `DESPACHO_`, sem caixa); se não houver, uma **nova** `DESPACHO NN - <hoje>` com
  NN = maior número de subpasta `^DESPACHO\s+(\d+)\b` (sem caixa) + 1, com 2 dígitos. O usuário pode trocar entre a
  nova e qualquer existente. A **data do despacho** é a da pasta (nova = hoje, data local; existente = a do nome).
- **Conteúdo (passo 2):** a prévia editável (10.4). **Conferência (passo 3):** o app gera DOCX e PDF numa pasta de
  trabalho, valida e mostra **todas** as páginas do PDF; o usuário marca "Conferi as N páginas" e só então
  **Gravar na pasta do processo**. Nada é criado na pasta do processo antes desse clique.

### 10.3 Conteúdo derivado do relatório revisado (determinístico, sem IA)
Fonte única: `DadosEfetivos.Gerar(projeto)` do pacote gravado (itens incluídos na ordem, situação e textos
efetivos, citações já impressas). Nada vem de outro lugar além do modelo DOCX e dos textos fixos do kit.
- **Cabeçalho:** Protocolo nº = `capa.processo` (como está, ex.: "9001/2026"); Requerente = `capa.requerente`;
  Assunto = o texto em negrito do modelo ("SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA"), editável; data por extenso
  ("14 de novembro de 2026"; dia 1 = "1º", Manual de Redação da Presidência).
- **Correção:** uma exigência por item incluído com situação efetiva **≠ ATENDE** (NÃO ATENDE, FALTA COMPROVAR,
  INFORMAÇÕES DIVERGENTES), na ordem do relatório, todas marcadas (o usuário pode desmarcar). Texto =
  `providencia` efetiva; fundamento = `base_legal` efetiva; impressas como no despacho real do kit: o texto e, em
  seguida, " (fundamento)"; o ponto final da providência passa para depois do parêntese e o ponto final do
  fundamento sai. Grupo de cada exigência = o título de grupo do modelo ("GERAL E DOCUMENTAÇÃO"), editável (ex.:
  "PRANCHA 01"); os grupos saem na ordem da primeira aparição. Depois das exigências, o parágrafo de fechamento do
  kit (prazo e encaminhamento). ATENDE e excluídos nunca entram.
- **Aprovação:** os quatro parágrafos fixos do kit ("Considerando…" ×3 e "Encaminho…"). Duas lacunas são campos do
  usuário, **nunca** preenchidos pelo app: o tipo de obra (caixas "obra nova", "alteração de obra", "ampliação de
  obra"; 1 = "x", 2 = "x e y", 3 = "x, y e z", na ordem das caixas) e a **área total a construir** (m², formato
  brasileiro com 2 decimais, ex.: "210,52"; os cálculos são a Etapa 3, então nada é lido do relatório).
- **Textos fixos:** o DOCX do kit não traz o corpo nem o fechamento; o kit os mantém nas referências do plugin
  `despacho` (`references\modelo-despacho-aprovacao-gabinete.md`, sha256 em 14/11/2026
  `0a0da0207f9ce04e478b2e1abd5e3f0e1a86a8f54fc04035fddc6e539eb4dbe7`, e `references\modelo-despacho-analise-correcao.md`,
  `465d6432580f867ef60b0537931fc77c44b54cc9e63c3e1fe92bf007ed441e21`). O app os **lê no lugar** a cada uso, pela
  seção "## Conteúdo na ordem do documento" (um parágrafo por bloco `<!-- estilo: … -->`): aprovação = os
  parágrafos que começam por "Considerando" (3) e "Encaminho" (1), com o trecho "obra nova/alteração de
  obra/ampliação de obra" e o número antes de "m²" trocados pelas lacunas; correção = o parágrafo que começa por
  "Para prosseguimento". Fora desse formato ⇒ o tipo fica indisponível com o motivo (o app não escreve texto
  institucional). O sha de cada referência vai para o recibo. Os textos fixos **não são editáveis** nesta etapa
  (quem precisar mudar edita o DOCX gravado no Word).
- **Campos que faltam** (o editor não preenche nada sozinho): Protocolo, Requerente ou Assunto vazios; exigência
  marcada com texto ou grupo vazio; correção sem nenhuma exigência marcada; aprovação sem tipo de obra ou com área
  vazia/inválida. Cada um aparece como campo a preencher (realçado na prévia como "[preencher: …]", só na tela) e
  "Gerar e conferir" fica bloqueado enquanto a lista não estiver vazia.
- **Bloqueios** (não deixam abrir o passo 2): conferência da revisão com erros (`ConferenciaRevisao.Erros`, ex.:
  situação mudada sem justificativa); número/ano da capa diferente do da pasta do processo; pacote somente leitura
  (esquema maior); correção sem nenhum item ≠ ATENDE; processo sem `.pmvrel`. **Aviso com confirmação:** aprovação
  com itens ≠ ATENDE (a lista vai para o recibo).

### 10.4 Prévia editável (onde fica)
Página **Novo despacho** no módulo Despachos, no lugar da lista enquanto está aberta (botão "Cancelar" volta; a
faixa continua a do módulo, com "Novo despacho" desabilitado). Coluna esquerda (~380 px): passos 1–2 (origem,
cabeçalho e, na correção, a lista de exigências em cartões: marcar/desmarcar, "ITEM 05 · NÃO ATENDE · título",
grupo, texto e fundamento editáveis em texto puro de uma linha (`TextoEditado.Normalizar`), mover acima/abaixo, e o
original do relatório ao lado para consulta; na aprovação, as caixas do tipo de obra e a área). Direita: a
**folha** de prévia (branca, proporção A4, Verdana), com os parágrafos na ordem do DOCX (textos do modelo, valores,
grupos, exigências, textos fixos, data e bloco de assinatura lidos do próprio modelo), atualizada a cada tecla. É
uma prévia de texto, não de paginação; a paginação real é conferida no passo 3 sobre o PDF do LibreOffice (páginas
desenhadas pelo `Windows.Data.Pdf`). Rodapé da página: "Cancelar", "Gerar e conferir ▸" (passo 3: "Voltar e
editar", "Gravar na pasta do processo").

### 10.5 Preenchimento do DOCX
**Biblioteca: `System.IO.Compression` (`ZipArchive`) + `System.Xml.Linq`, sem Open XML SDK e sem
`System.IO.Packaging`.** Motivo: a regra do kit é copiar o modelo e editar **só** `word/document.xml`, regravando as
demais partes byte a byte; o `System.IO.Packaging` (base do Open XML SDK) regrava `[Content_Types].xml` e os
`.rels` ao salvar, e o SDK acrescentaria alguns MB ao executável único sem ganho (o preenchimento é por âncoras,
não por modelo de objetos). A saída mantém nomes, ordem e datas das entradas e o conteúdo descomprimido de toda
parte ≠ `word/document.xml` idêntico ao do modelo; o `document.xml` é lido com `PreserveWhitespace` e gravado sem
reformatar, com a mesma declaração XML.
Regras (porta das regras do kit em `uso-templates.md`/`emissao-e-fidelidade.md`, âncoras medidas nos dois modelos
da revisão 10):
- Parágrafo cujo texto é "Protocolo nº:" / "Requerente:" (comparação após aparar): acrescenta um run com o `rPr`
  do rótulo + `<w:b/>` e o texto `" " + valor` quando o rótulo não termina em espaço (como no despacho real do kit).
- "Assunto: " + run em negrito: troca só o texto do run em negrito.
- Data: o trecho literal "dia de mês de ano" (run próprio na correção; dentro do run na aprovação) vira a data
  por extenso; o resto do run fica.
- Corpo: os parágrafos vazios justificados (`jc=both`) consecutivos entre o destinatário ("AO …:") e a data são
  as **vagas** (4 em cada modelo). Exigências, fechamento e parágrafos da aprovação ocupam as vagas na ordem;
  faltando vaga, clona-se a primeira (mesmo `pPr`); vagas que sobram são removidas. O texto vai em runs com o
  `rPr` do rótulo ("Protocolo nº:", `sz 24`); o fundamento vai num segundo run igual. Grupos (correção): o
  primeiro reaproveita o parágrafo "GERAL E DOCUMENTAÇÃO"; os demais são clones dele com o texto trocado.
  Clones perdem `w14:paraId`/`w14:textId` (nunca há id repetido).
- Nada mais muda: cabeçalho/rodapé, brasão, estilos, numeração, margens, destinatário, assinatura e o parágrafo
  vazio antes dela ("uma linha em branco antes da assinatura", regra do kit). Âncora ausente ou repetida ⇒
  "modelo fora do padrão" (o tipo fica indisponível; nunca se improvisa).

### 10.6 Validação (como o kit)
Antes de qualquer gravação, o DOCX gerado passa por: (1) **porta em C# da comparação de componentes do
`validate_template_fidelity.py`** (`styles.xml`, `fontTable.xml`, `numbering.xml`, `theme/*`, `media/*`,
`header*.xml`, `footer*.xml` idênticos; `sectPr` preservados; fontes e tamanhos diretos `rFonts`, `sz`, `szCs` só
com valores que já existem no modelo; tabelas iguais), com as mesmas mensagens do kit; (2) conferência de
conteúdo: todos os valores congelados presentes na ordem, nenhum resto do modelo ("dia de mês de ano", `XX`,
`[ ]`, `{{`), parágrafo vazio imediatamente antes da linha de assinatura, todas as outras partes byte a byte
iguais; (3) releitura do DOCX gravado na pasta de trabalho. Além disso, uma verificação **V6** no
`tools/fidelidade.py` roda o próprio validador do kit (importado do kit, somente leitura, sem bytecode) sobre
despachos gerados pelo `Pmvni.Verificador` e exige zero erros. A inspeção visual de todas as páginas que o kit
exige é o passo 3 (o usuário confere cada página antes de gravar; as páginas conferidas vão para o recibo).

### 10.7 PDF pelo LibreOffice
Como o kit (`programas_pmvni.py docx-pdf`): `soffice.com --headless --norestore --convert-to pdf --outdir …` sobre
uma **cópia** do DOCX, com perfil próprio do editor (`-env:UserInstallation=` em `<dados>\lo`, um só e persistente — Ruling T16-C4: a 1ª
inicialização de um perfil leva perto de um minuto —, com uma conversão por vez; para não ser entregue a
um LibreOffice aberto) e `TEMP`/`TMP` do processo apontados para a pasta de trabalho; sem janela; prazo de 5 min
(depois encerra só a árvore desse processo); o Word **nunca** é automatizado. Depois: o DOCX não pode ter mudado
(sha), o PDF precisa abrir no leitor mínimo (`InfoPdf`: páginas) e listar a fonte **Verdana** (`/BaseFont`, sem o
prefixo de subconjunto). Localização: `PMVNI_SOFFICE` (testes) → `%ProgramFiles%\LibreOffice*\program\soffice.com`
→ `%ProgramFiles(x86)%\…` → `soffice` no PATH. **Sem LibreOffice**, com falha ou sem Verdana: grava **só o DOCX**
(e o recibo com o motivo) e mostra a mensagem clara: "O LibreOffice não está instalado neste computador. O despacho
será gravado só em Word (DOCX). Para o PDF, rode o instalador do kit (INSTALAR_KIT_PMVNI.cmd) e use Despachos ›
Gerar PDF." O comando **Gerar PDF** (Despachos) completa depois um despacho do editor que ficou sem PDF, com a
mesma conferência de páginas.

### 10.8 Onde os arquivos são gravados
Na pasta do processo, dentro da pasta da etapa escolhida (criada só no "Gravar", se for nova; nunca renomeada):
`Despacho_<Tipo>_<nº>_<ano>_<Nome>.docx`, `.pdf` e `.recibo-editor.json`, como o kit (`Tipo` = `Correcao` ou
`Aprovacao`; `nº` sem zeros à esquerda, da pasta do processo; `Nome` = requerente sem acentos, cada palavra com
inicial maiúscula, separadas por `_`, até 60 caracteres cortando em palavra). **Nunca sobrescreve:** se qualquer
dos três nomes existe, usa `… (2)`, `… (3)`… para os três; cada arquivo é gravado num `.tmp` oculto da mesma pasta
e movido sem sobrescrever. A pasta de trabalho é `<dados>\despachos\<guid>` (`PastaDados.Raiz`), apagada depois
da gravação ou do cancelamento; órfãs com mais de 1 dia são apagadas ao abrir. O app não grava `.pmvni-ai\` (são
controles das IAs do kit) e não altera o `.pmvrel`.

### 10.9 Recibo
`<nome>.recibo-editor.json` ao lado, como o do relatório: `recibo_versao` 1, tipo e papel, registro (revisão e
sha256), modelo (caminho relativo e sha256), textos fixos (arquivo e sha256), relatório base (nome do `.pmvrel`,
sha256 do pacote gravado, sha256 dos dados efetivos, número do último registro do histórico), processo (nº, ano,
nome da pasta), etapa (pasta, nº, data, nova ou existente), exigências (id, número, situação, grupo, se o texto foi
editado), campos editados, avisos confirmados, DOCX (nome, sha256, "validação OK"), PDF (nome, sha256, páginas,
fontes, versão do LibreOffice) ou `sem_pdf_motivo` (`SEM_LIBREOFFICE`, `CONVERSAO_FALHOU`, `SEM_VERDANA`),
páginas conferidas (lista, usuário, data UTC), usuário do Windows, versão do app e `gerado_em`. Sem dados além dos
que já estão no próprio despacho. "Gerar PDF" atualiza o mesmo recibo (temporário + troca).

### 10.10 Restrições, testes e Recriar PC
- Todas as restrições globais valem: executável único `asInvoker`; nada sai da máquina; o kit é **somente
  leitura**; Word nunca automatizado; textos da interface em português; nenhum botão sem função (tipo sem modelo
  fica desabilitado com o motivo). As pastas de processos deixam de ser só leitura **apenas** para o que o usuário
  pede ao gravar um despacho (a pasta da etapa e os três arquivos), sem mover, renomear ou apagar nada.
- **Licença do kit:** o `LICENSE` do plugin proíbe copiar ou redistribuir o plugin "no todo ou em parte" sem
  autorização do autor, e os modelos são da Prefeitura. Por isso nem os DOCX nem os textos fixos entram no
  repositório: são lidos no lugar, em tempo de uso, pela pasta do kit (Ajustes › "Pasta do kit PMVNI", padrão
  `%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)`; `PMVNI_KIT_RAIZ` nos testes). O
  código guarda só as âncoras curtas necessárias para localizar o texto. Os testes leem os modelos do kit no lugar
  (como a Etapa 1 lê o gerador do kit) e usam DOCX e PDF **fictícios montados no próprio teste** para os casos de
  erro; despachos gerados em teste ficam só em `tests/saida`.
- Dados reais nunca em teste: árvore fictícia `tests/corpus/processos/` e relatório fictício (o `curto` com a capa
  trocada no teste). Pelo shell do Claude nada grava em `%APPDATA%`/`%TEMP%`: os testes usam um conversor falso; o
  LibreOffice real só roda no roteiro (aberto por `explorer.exe`, fora do pacote), com perfil e `TEMP` na pasta de
  trabalho.
- **recriar.json** (na entrega, Tarefa 14): em `conferir`, como o Python, um item de aviso
  `{ "tipo": "arquivo", "caminho": "%ProgramFiles%\\LibreOffice\\program\\soffice.com", "nome": "LibreOffice (PDF dos despachos; instalado pelo instalador do kit PMVNI)", "aviso": true }`;
  em `usadoPor`, que o app lê os modelos de despacho do kit e cria as pastas `DESPACHO NN` com DOCX/PDF. O
  LibreOffice não ganha passo próprio: quem o instala é o instalador do kit (receita `32-kit-pmvni`).

### 10.11 Decisões minhas (registradas)
1. Rótulos "Correção" e "Aprovação" (só a aprovação simples existe no registro do kit).
2. Pasta da etapa sugerida = última `DESPACHO NN` ainda sem despacho, senão nova (regra do kit de reaproveitar a
   pasta da mesma análise); data = a da pasta.
3. Forma da exigência: providência + " (fundamento)", com o ponto final no fim (padrão do despacho real do kit);
   o título e a verificação do item não entram (o kit pede não repetir o texto técnico inteiro).
4. Grupo padrão = o título de grupo do modelo; o usuário renomeia por exigência; sem numeração automática (as
   vagas do modelo da revisão 10 não são numeradas).
5. Textos fixos do kit não editáveis nesta etapa; a área total nunca é lida do relatório.
6. Aprovação com itens ≠ ATENDE é permitida com confirmação (o tipo é decisão do usuário; o app não sugere),
   registrada no recibo; correção sem item ≠ ATENDE é bloqueada.
7. Número/ano da capa diferente do da pasta bloqueia (protege contra relatório no processo errado).
8. Nada é gravado na pasta do processo antes da conferência de todas as páginas; sem PDF, só o DOCX, com "Gerar
   PDF" para completar depois.
9. O despacho não altera o `.pmvrel` nem grava `.pmvni-ai\`; o vínculo fica no recibo.

## 11. Legislação: PDFs anexados e leitor (pedido do usuário, 15/11/2026)

Pedido (vinculante): "Não vamos embutir as leis no kit. Crie uma opção em ajustes onde eu consiga anexar pdfs das leis. Esse
PDF será aberto na sessão legislação como leitor (crie ferramentas para esse leitor, como busca e seleção)." E, em seguida:
"Cada anexo deverá ser listado individualmente e eu posso deletar tb se quiser, assim como por novos."

Esta seção **substitui inteira** a versão anterior (legislação compilada embutida, 14–15/11/2026). Saem: o pacote
`legislacao.pmvleg`, o projeto com o recurso de ≈ 10 MB, a ferramenta `tools/legislacao` (Python, PyMuPDF, Pillow), o
catálogo dos arquivos do kit, a árvore de artigos, a conferência de sha com `<kit>\03 - LEGISLACAO` e o aviso no build. O
app **não lê o kit** para a legislação. Também continua fora a pasta de legislação configurável da primeira versão da §9:
o usuário escolhe os PDFs um a um (ou vários de uma vez), e o app trabalha só sobre as **cópias** que guarda.

### 11.1 Ajustes › Legislação: anexar, listar, editar, substituir e excluir

Seção nova da página Ajustes, depois de "Pasta do kit PMVNI":
- Texto: "Anexe os PDFs das leis que você consulta. O editor guarda uma cópia de cada um na pasta de dados dele e nunca
  altera o arquivo original. Nada é enviado para fora do computador." e a linha de total ("4 leis anexadas · 12,3 MB").
- Botão **"Anexar PDFs…"**, sempre disponível (também com a lista vazia e depois de excluir): seletor de arquivos `.pdf`
  com seleção múltipla. Os arquivos são processados um por vez, na ordem do seletor, com uma linha de progresso
  ("Anexando “LEI 1.382-2020.pdf” — página 12 de 81") e "Cancelar" (o arquivo em andamento não entra; os já anexados ficam).
  No fim, uma InfoBar com o resumo exato ("2 PDFs anexados." / "1 PDF anexado; 4 recusados:" e uma linha por recusa).
- **Lista com uma linha por anexo** (nunca agrupada nem resumida), na ordem esfera (Federal, Estadual, Municipal, Outras)
  e título. Cada linha tem: **título** (caixa de texto editável; vale ao sair da caixa ou com Enter; vazio é recusado com
  "O título não pode ficar vazio."; até 200 caracteres), **esfera** (lista: Federal, Estadual, Municipal, Outras), nome
  do arquivo original, tamanho ("2 KB", "1,2 MB"), páginas ("3 páginas"), data em que foi anexado ("anexado em
  15/11/2026 14:32"), selo quando couber ("Sem texto pesquisável", "2 de 81 páginas sem texto", "Preparando a
  pesquisa…", "Cópia do editor não encontrada") e os botões **"Substituir arquivo…"** e **"Excluir"**.
- **Excluir** (por linha): `Excluir “<título>”?` / "A cópia do PDF guardada pelo editor e o seu índice de pesquisa serão
  apagados. O arquivo original (<nome>) não é alterado." / Excluir · Cancelar. Apaga a linha do catálogo, a cópia
  `<sha>.pdf` e o índice; os outros anexos não mudam e continuam pesquisáveis. O arquivo original do usuário nunca é
  tocado. Excluir a lei aberta no módulo fecha o leitor.
- **Substituir arquivo…** (nova versão da mesma lei): escolhe um PDF; mesmas verificações do anexar; mantém id, título e
  esfera da linha; troca o arquivo, as páginas e o índice; grava `substituido_em`; a cópia anterior é apagada depois que o
  catálogo novo está gravado. Mesmo arquivo ⇒ "O arquivo escolhido é igual ao já anexado."; arquivo já anexado em outra
  linha ⇒ a mensagem de repetido.

Verificações ao anexar (todas antes de qualquer gravação no catálogo; mensagens exatas, com o nome do arquivo):

| Caso | Regra | Mensagem |
|---|---|---|
| Vazio | 0 byte | `“<nome>” está vazio.` |
| Grande demais | **> 200 MB** (209.715.200 bytes), conferido antes de ler | `“<nome>” tem 201,0 MB; o limite é 200 MB por arquivo.` |
| Não é PDF | `%PDF-` não aparece nos primeiros 1.024 bytes (a mesma tolerância do PDF.js) | `“<nome>” não é um arquivo PDF.` |
| Repetido | sha256 igual ao de um anexo | `“<nome>” já está anexado como “<título>”.` |
| Senha de abertura | o PDF.js pede senha (`PasswordException`) | `“<nome>” está protegido por senha e não pode ser aberto pelo editor. Anexe uma cópia sem senha.` |
| Danificado | o PDF.js não abre (`InvalidPDFException` e afins) | `“<nome>” não pôde ser lido como PDF (arquivo danificado ou incompleto).` |
| Ilegível | erro de E/S ao ler | `“<nome>” não pôde ser lido: <mensagem do sistema>.` |

PDF com senha só de proprietário (restrição de impressão ou cópia, sem senha para abrir) abre normalmente no PDF.js e é
aceito. Valores iniciais: título = nome do arquivo sem `.pdf`; esfera sugerida pelo caminho do original (o nome do arquivo
primeiro, depois as pastas, da mais próxima para a mais distante; a primeira que contém "federal" ⇒ Federal, "estadual" ou
"cbmes" ⇒ Estadual, "municipal" ⇒ Municipal, sem caixa nem acento), senão Outras. O original é aberto só para leitura
(`FileAccess.Read`, `FileShare.ReadWrite | FileShare.Delete`): nem conteúdo, nem atributos, nem datas mudam.

Fluxo de um arquivo: verificar (tamanho, assinatura, sha, repetido) → copiar para `<dados>\legislacao\.entrada\<token>.pdf`
(pasta oculta) → o PDF.js lê a cópia (páginas, senha, texto de cada página) → mover para `<sha>.pdf`, gravar o índice,
gravar o catálogo. Recusa ou cancelamento apagam a cópia de entrada; entradas com mais de 1 dia são apagadas ao abrir.

### 11.2 Onde fica (pasta de dados do app)

`<dados>\legislacao\` (`PastaDados.Raiz`, `PMVNI_DADOS_DIR` nos testes e no roteiro):
- `catalogo.json` (+ `catalogo.json.bak`, a versão anterior) — gravado de forma atômica: `catalogo.json.tmp` → flush →
  `File.Replace` com o `.bak` (a primeira vez, `File.Move`); falha antes da troca deixa o catálogo anterior intacto.

```json
{ "versao_catalogo": 1,
  "anexos": [ { "id": "lg_0f3c…(32 hex)", "sha256": "…64 hex…", "titulo": "Lei nº 9.999/2099 (fictícia)", "esfera": "Municipal",
                "arquivo_original": "LEI 9.999-2099 - FICTICIA.pdf", "tamanho": 1834, "paginas": 3, "paginas_sem_texto": [],
                "anexado_em": "2026-11-15T14:32:00Z", "substituido_em": null } ] }
```
  Só o **nome** do arquivo original é guardado (não o caminho). `versao_catalogo` maior que a conhecida ⇒ legislação só
  para leitura ("O catálogo da legislação foi gravado por uma versão mais nova do editor; os anexos ficam só para
  leitura.").
- `<sha256>.pdf` — a cópia, byte a byte igual ao original no momento em que foi anexado.
- `<sha256>.texto.json.gz` — o índice de texto (§11.3).
- `.entrada\` — cópias em processamento (oculta).

Recuperação: catálogo ilegível ⇒ renomeado para `catalogo.corrompido-<aaaa-MM-dd-HHmmss>.json` e o app tenta o `.bak`;
um `<sha>.pdf` sem linha no catálogo (catálogo perdido, ou queda entre a cópia e a gravação) **nunca é apagado
sozinho**: volta como linha "PDF recuperado <8 hex>" (esfera Outras) e uma InfoBar pede para conferir título e esfera.
Linha cujo `<sha>.pdf` sumiu fica com o selo "Cópia do editor não encontrada" e só Substituir/Excluir. As alterações do
catálogo são ler → alterar → gravar dentro de um `Mutex` nomeado da sessão do Windows (duas janelas do editor não perdem
alterações uma da outra).

### 11.3 Índice de texto (uma vez por anexo)

O texto vem do próprio PDF.js (`page.getTextContent()` em cada página: itens unidos, `hasEOL` vira quebra de linha), lido
num WebView2 oculto com a mesma sandbox do leitor (§11.4), **uma vez** ao anexar ou substituir; o índice é refeito só se
faltar, se o sha não bater ou se `versao_indice` mudar (por exemplo, depois de o Recriar PC devolver só os PDFs), em
segundo plano ao abrir o módulo, com o selo "Preparando a pesquisa…" na linha.

`<sha>.texto.json.gz` = gzip de `{ "versao_indice": 1, "sha256": "…", "leitor": "pdf.js 6.4.299", "paginas": ["…", "…"] }`
(uma string por página; página sem texto = `""`). Página "com texto" = tem ao menos uma letra ou dígito. Nenhuma página com
texto ⇒ **"Sem texto pesquisável"** (no Ajustes, na lista do módulo e numa InfoBar no leitor: "Este PDF não tem texto
pesquisável: a pesquisa e a cópia de texto não funcionam nele."). **Sem OCR, nunca texto inventado.** Parte das páginas sem
texto ⇒ o selo "k de n páginas sem texto" e a pesquisa cobre só as que têm.

### 11.4 Leitor no módulo Legislação

Trilho: Relatórios, Despachos, Processos, **Legislação**, Ajustes. Tela:
- **Painel à esquerda** com abas **Leis** · **Marcadores** · **Resultados**:
  - *Leis*: os mesmos anexos do Ajustes, **um por linha**, agrupados por esfera (Federal, Estadual, Municipal, Outras;
    grupo vazio não aparece), com caixa de filtro (título e nome do arquivo, sem caixa nem acento); linha = título, nome do
    arquivo, páginas e selo. Lista vazia ⇒ "Nenhuma lei anexada. Anexe os PDFs das leis em Ajustes › Legislação." com os
    botões "Anexar PDFs…" (o mesmo fluxo do §11.1) e "Abrir Ajustes".
  - *Marcadores*: a estrutura (outline) do PDF, numa `TreeView` nativa (títulos como texto puro, até 300 caracteres);
    clicar vai ao destino. Desabilitada quando o PDF não tem marcadores. Marcador que é link externo aparece com
    "(link externo)" e não faz nada.
  - *Resultados*: só durante uma pesquisa em todas as leis (§11.6).
- **Centro**: o PDF num WebView2, páginas em rolagem contínua, com camada de texto selecionável.
- **Rodapé**: "Página [N] de M" (caixa numérica; Enter vai; valor fora do intervalo é ajustado ao limite), ◂ ▸ e a escala
  ("125%").

PDF.js: **pdfjs-dist 6.4.299** (publicado em 03/10/2026; Apache-2.0), versão exata no `package.json` (sem `^`) e no
`package-lock.json` com integridade; os arquivos usados são **vendorizados** no repositório (`src/Pmvni.Legislacao/web/pdfjs/`,
na mesma estrutura de pastas do pacote, com `VERSAO.json` = versão, licença e sha256 de cada arquivo) e embutidos no
executável como recursos. Entram 283 arquivos, **4.754.750 bytes (≈ 4,75 MB antes da compressão do single-file)**:
`build/pdf.min.mjs`, `build/pdf.worker.min.mjs`, `web/pdf_viewer.mjs`, `web/pdf_viewer.css` e `web/images/`,
`standard_fonts/`, `cmaps/`, `iccs/`, `wasm/openjpeg.wasm`, `wasm/jbig2.wasm`, `wasm/qcms_bg.wasm` e **todos os arquivos de
licença** (`LICENSE` do pdf.js, `LICENSE_FOXIT`, `LICENSE_LIBERATION`, licenças do OpenJPEG, JBIG2, QCMS e do ICC).
Ficam fora: o leitor completo do Firefox (`web/viewer.*`), o `quickjs-eval` (sandbox de JavaScript dos PDFs), os
`*_nowasm_fallback.js` (o WebView2 tem WebAssembly), as versões não minificadas e os `.map`. Ajuda › Sobre mostra "PDF.js
6.4.299 (Apache-2.0)" e o texto da licença.

Segurança (o modelo da Etapa 1, com host próprio):
- WebView2 **InPrivate** pela `FabricaWebView2`; as configurações da `Seguranca` (menus padrão, preenchimento e senhas
  desligados, `AreHostObjectsAllowed` falso, diálogos de script desligados, **atalhos do navegador desligados**, DevTools só
  em Debug) e, aqui, também `IsZoomControlEnabled` falso (a escala é do leitor).
- Host virtual **`leg.pmvni.invalid` sem pasta mapeada**: o filtro `WebResourceRequested` (`*`, todos os contextos)
  responde da memória só `/leitor.html`, `/leitor.js`, `/leitor.css`, `/extrator.html`, `/extrator.js`, os arquivos
  listados no `VERSAO.json` em `/pdfjs/…` e `/pdf/<token>.pdf` (token de 32 hex sorteado pelo host para cada PDF aberto;
  o caminho do disco nunca vai para a página; bytes lidos da cópia, só leitura, com suporte a `Range` → 206); **qualquer
  outra URL ⇒ 403** (outro host, `http:`, query, `..`, token desconhecido, método diferente de GET).
- Navegação de nível superior só para a URL exata da página daquela instância (`https://leg.pmvni.invalid/leitor.html` ou
  `/extrator.html`), uma vez; trocar de PDF é mensagem, não navegação. Janelas novas e downloads cancelados.
- CSP (meta na página e cabeçalho em toda resposta HTML): `default-src 'none'; script-src
  https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs https://leg.pmvni.invalid/pdfjs/web/pdf_viewer.mjs
  https://leg.pmvni.invalid/leitor.js 'wasm-unsafe-eval'; worker-src https://leg.pmvni.invalid/pdfjs/build/pdf.worker.min.mjs;
  connect-src https://leg.pmvni.invalid; img-src https://leg.pmvni.invalid data: blob:; font-src https://leg.pmvni.invalid
  data: blob:; style-src https://leg.pmvni.invalid 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-src 'none';
  object-src 'none'` (o extrator troca `leitor.js` por `extrator.js` e não carrega o `pdf_viewer`); o worker recebe no
  cabeçalho `default-src 'none'; connect-src https://leg.pmvni.invalid; script-src 'wasm-unsafe-eval'`. Violação de CSP é
  informada ao host (`cspViolacao`) e contada.
- **JavaScript dos PDFs nunca roda**: sem `PDFScriptingManager` nem sandbox, `isEvalSupported: false`, `enableXfa: false`;
  anotações só desenhadas (`AnnotationMode.ENABLE`, sem formulários editáveis), editor de anotações desligado.
  **Links externos desligados** (`externalLinkEnabled = false`: o link não navega e mostra o endereço na dica); links
  internos (para outra página do mesmo PDF) funcionam. Decisão: o app não tem abertura de URL na lista do `AberturaExterna`
  (que só abre arquivos), e abrir sites a partir de um PDF não foi pedido.
- Ponte: envelope `{v:1,tipo,id,dados}` com tipos próprios do leitor e do extrator, validados nos dois lados (versão, tipo
  do sentido certo, campos); mensagem de origem diferente de `https://leg.pmvni.invalid/` é descartada.

Ferramentas do leitor:
1. **Páginas**: rolagem contínua; caixa "Página N de M"; ◂ Página / Página ▸ (Alt+↑ / Alt+↓).
2. **Escala**: Diminuir / Aumentar (passo do PDF.js, 25% a 400%; Ctrl+− / Ctrl++ e Ctrl+roda do mouse), **Ajustar largura**
   (padrão ao abrir; Ctrl+0) e **Página inteira**.
3. **Marcadores** (outline), quando o PDF tem.
4. **Seleção de texto** na camada de texto do PDF.js; **Copiar** (Ctrl+C) copia o texto selecionado; **Copiar citação**
   (Ctrl+Shift+C) copia `“<texto>” (<título>, página <N>)` (§11.7). Os dois ficam desabilitados sem seleção ("Selecione um
   trecho do texto.").
5. **Pesquisa na lei aberta** (§11.6): realça todas as ocorrências, Próxima/Anterior (Enter/Shift+Enter, F3/Shift+F3),
   contador "3 de 17".
6. **Pesquisa em todas as leis anexadas** (§11.6), com a lista de resultados (lei, página e trecho).
Sem miniaturas (decisão: o componente de miniaturas pertence ao leitor completo do Firefox, que fica fora; cada miniatura é
uma renderização extra, cara em PDFs de centenas de páginas; a caixa de página e os marcadores cobrem a navegação). Sem
imprimir, sem abrir a cópia em outro programa (o programa externo poderia gravar nela e quebrar o sha) e sem edição.

### 11.5 Faixa do módulo Legislação (substitui a da §9.1)

**Arquivo** (Anexar PDFs…, Gerenciar anexos — vai a Ajustes › Legislação —, Importar dados.json, Abrir, Recentes) ·
**Página Inicial** (Copiar, Copiar citação, ◂ Página, Página ▸, Pesquisar em ▾ "Nesta lei" / "Em todas as leis",
◂ Ocorrência, Ocorrência ▸) · **Exibir** (Diminuir, Aumentar, Ajustar largura, Página inteira, Marcadores, Lista de leis) ·
**Ajuda**. Aba padrão: Página Inicial. Comandos que dependem de uma lei aberta, de seleção ou de marcadores ficam
desabilitados sem eles.

### 11.6 Pesquisa (a caixa da barra de título no módulo Legislação; retarget da §9.1)

Sem diferenciar maiúsculas nem acentos, como a pesquisa do relatório (Tarefa 11). O alvo segue "Pesquisar em" (padrão:
"Nesta lei" com uma lei aberta, "Em todas as leis" sem nenhuma); a dica muda para "Pesquisar nesta lei" / "Pesquisar em
todas as leis".
- **Nesta lei**: o `PDFFindController` do PDF.js (`caseSensitive: false`, `matchDiacritics: false`, `highlightAll: true`),
  que realça na camada de texto e trata texto partido entre trechos e linhas. Contador "k de n" (quando todas as páginas
  já foram varridas), Enter/Shift+Enter navegam, "Nenhuma ocorrência" quando n = 0; caixa vazia limpa o realce. Em PDF sem
  texto: "Este PDF não tem texto pesquisável."
- **Em todas as leis**: no índice do §11.3 (em memória, carregado em `Task.Run` na primeira pesquisa e atualizado a cada
  anexo, substituição ou exclusão), depois de 300 ms sem digitar, termo normalizado com ≥ 2 caracteres (NFD sem marcas,
  minúsculas, espaços colapsados, hífen suave removido). Resultados na aba Resultados: "N resultados em K leis", agrupados
  por lei (ordem da lista), um por página com ocorrência ("p. 12 — …trecho…", até 160 caracteres em volta da primeira
  ocorrência, com o texto original), no máximo 200 ("Mostrando os 200 primeiros de N resultados."). Clicar abre a lei
  naquela página e passa a pesquisa para "Nesta lei" com o mesmo termo, a partir dessa página (a ocorrência selecionada é a
  dessa página). Esc ou caixa vazia voltam à aba Leis.

### 11.7 Copiar e citar

"Copiar" põe na área de transferência o texto selecionado (quebras de linha normalizadas para `\r\n`, pontas aparadas).
"Copiar citação" põe `“<texto com espaços colapsados>” (<título do anexo>, página <N>)`; seleção que atravessa páginas ⇒
`páginas <N>–<M>`. O título é o da linha do Ajustes (o usuário o deixa como quer citar, por exemplo "Lei nº 1.382/2020
(PDM)"); a página é a posição no PDF (1, 2, 3…), não o número impresso na folha. Nada é inventado: sem seleção, não há
citação.

### 11.8 Recriar PC

Os PDFs anexados são dados do usuário (atos públicos que ele mesmo escolheu; sem senhas, chaves ou tokens). O
`recriar.json` do editor ganha um passo `arquivo` da pasta `%LOCALAPPDATA%\EditorRelatoriosPMVNI\legislacao` com `guardado`
`editor-relatorios-pmvni\legislacao`, `filtro` `^(catalogo\.json|[0-9a-f]{64}\.pdf)$` (catálogo e cópias; índices, `.bak`,
`.tmp` e `.entrada` não vão: o app refaz os índices sozinho) e `opcional: true`. Como toda pasta guardada, o que o usuário
excluir no app sai também da cópia guardada (a anterior fica 30 dias em `guardado\_anteriores`). O restante de
`%LOCALAPPDATA%\EditorRelatoriosPMVNI` continua **não** guardado (dados de processos). Nada novo a instalar (o PDF.js vai
dentro do executável); o `MAPA-DO-AMBIENTE.md` ganha a ligação "editor ↔ PDFs de legislação anexados (cópias na pasta de
dados do editor, guardadas pelo Recriar PC)" e perde a ligação editor ↔ `03 - LEGISLACAO` do kit.

### 11.9 Restrições e testes

- Nada sai da máquina: o app só lê o arquivo escolhido e grava na sua pasta de dados (as cópias guardadas pelo Recriar PC
  ficam onde ficam todas as dele, no OneDrive do usuário). Textos em português; nenhum botão sem função.
- Testes e ferramentas rodados pelo shell do Claude gravam só em `tests/saida/…` e na pasta de `PMVNI_DADOS_DIR`; nunca em
  `%APPDATA%`/`%LOCALAPPDATA%`/`%TEMP%`.
- **Só PDFs fictícios**: `tests/corpus/legislacao/` (poucos KB cada; "LEI Nº 9.999… (FICTÍCIA)", "NORMA TÉCNICA FICTÍCIA
  Nº 99/2099"…), gerados em C# pelo próprio teste (`PdfLegislacaoFicticio`, bytes fixos, conferidos por teste) e
  versionados para o roteiro. Os PDFs reais do usuário nunca entram em teste, em `docs` nem em commit.
- O app grava a legislação só em `<dados>\legislacao`; o original nunca muda (teste e roteiro conferem sha, tamanho, datas
  e atributos).

### 11.10 Decisões minhas (registradas)
1. Limite de **200 MB** por PDF (os maiores atos usados hoje têm dezenas de MB; acima disso o WebView2 e a extração ficam
   lentos demais); sem limite de quantidade.
2. Senha de abertura é recusada; senha só de proprietário é aceita (o PDF.js abre).
3. O PDF.js entra também no anexar (contar páginas, detectar senha e extrair o texto); por isso a vendorização e a sandbox
   do host `leg.pmvni.invalid` ficam na Tarefa 17, e o leitor na 18.
4. Pesquisa na lei aberta pelo `PDFFindController` (maduro, com realce na camada de texto); pesquisa em todas as leis pelo
   índice do app (C#), com a mesma regra de caixa e acento. As contagens das duas podem diferir em texto partido no PDF;
   a da lei aberta é a do realce.
5. Sem miniaturas, sem imprimir, sem abrir a cópia em outro programa, links externos desligados.
6. Citação com a página do PDF e o título do anexo; título inicial = nome do arquivo; esfera sugerida pelo caminho.
7. Excluir apaga a cópia e o índice de verdade (não há lixeira: o original do usuário continua onde estava); PDF sem linha
   no catálogo nunca é apagado sozinho.
8. `cmaps` incluídos (1,17 MB) para não falhar em PDFs com fontes CID; o sandbox de scripts (`quickjs`) fica fora.
9. Recriar PC guarda catálogo e cópias (não os índices).

## 12. Novo relatório pelos agentes locais (ChatGPT/Codex e Claude) (pedido do usuário, 15/11/2026)

Pedido (vinculante, nas palavras do usuário):
1. "Em relatório, crie um botão novo relatório tb. Essa ferramenta deverá ser conectada ao app ChatGPT e Claude. Nela
   precisamos de um espaço para anexar arquivos (todos os tipos), que pode ser botão para anexar ou área para arrastar. Uma
   caixa de diálogo para escrever o prompt. E as opções Análise com ChatGPT (que deve usar automaticamente o plugin
   análise) e Analisar com o Claude (que deverá automaticamente usar o plugin análise pro). Assim não precisaremos mais usar
   a interface do ChatGPT e Claude separado. O próprio app que estamos criamos, conectado, já gera o relatório de análise
   mesmo."
2. "Crie opção nos ajustes de usar API ao invés da conta. Mas por regra a análise precisa acontecer localmente, não pode
   ser na nuvem, igual o ChatGPT e Claude fazem, pois acessam arquivos do computador e rede. Essa interface seria apenas um
   intermediário para o ChatGPT e Claude reais."
3. "Lembre se que o ChatGPT e Claude na análise acessam vários outros apps, tipo qgis, por isso a análise é localmente. Por
   isso tudo continua acontecendo nos apps ChatGPT e Claude na verdade. E o nosso novo app é apenas o intermediário."

Esta seção acrescenta ao §2 o objetivo "gerar o relatório da IA a partir do editor" e emenda, **só para esta função e só
quando o usuário clica em Analisar**, a restrição "nada sai da máquina" do §3, do §10.10 e do §11.9: o editor continua sem
abrir nenhuma conexão de rede; quem conversa com o provedor do modelo (OpenAI ou Anthropic) é o agente local do usuário,
exatamente como já faz fora do editor.

### 12.1 O que o editor faz e o que não faz

O editor é **só o intermediário**. Ele **não** analisa, não reimplementa nenhuma etapa do kit, não chama API de modelo,
não abre chat web, não usa tarefa na nuvem nem sandbox remoto, não escolhe peças e não interpreta o resultado. Ele:
1. arquiva os anexos na pasta do processo, pela regra do kit (12.3);
2. inicia o agente **instalado neste computador** como processo comum do usuário do Windows (o editor é `asInvoker` e
   não é empacotado; nada de MSIX, contêiner ou sandbox do editor), com a instalação e a configuração reais dele: os
   mesmos plugins e skills do kit, servidores MCP, programas do PC (QGIS, LibreOffice, AutoCAD…) e pastas de rede;
3. mostra o andamento, permite cancelar e guarda o registro da execução na pasta do processo (12.10);
4. quando o agente termina, localiza o resultado que **o próprio kit** declara (`delivery.json` → `document`), importa
   esse `relatorio_dados.json` para um `.pmvrel` novo (a importação da Tarefa 1, sem mudar o arquivo do kit) e o abre no
   módulo Relatórios para a revisão humana.

A inferência do modelo acontece na OpenAI ou na Anthropic, como hoje; leitura de arquivos, programas e rede acontecem
neste computador, pelo agente. Nada do que chega nos anexos é aberto ou executado pelo editor.

### 12.2 Onde fica e como é o fluxo

- **Entradas:** "Novo relatório" na faixa de **Relatórios**: primeiro comando de Arquivo e primeiro comando de Página
  Inicial (o "Novo despacho ▾" continua o último da Página Inicial); e na Página Inicial de **Processos**, logo depois de
  "Novo despacho ▾" (com o processo selecionado). Todas abrem a página **Novo relatório** dentro do módulo Relatórios, no
  lugar das folhas (a faixa de Relatórios fica, com os comandos das folhas desabilitados enquanto a página está à frente;
  "Voltar ao relatório" volta às folhas se houver pacote aberto).
- **Página Novo relatório**, de cima para baixo:
  1. **Processo:** lista de `Processos.Listar` com filtro (pré-escolhido: o selecionado em Processos, ou o processo cuja
     pasta contém o `.pmvrel` aberto em Relatórios) e **"Criar processo…"** (nº, ano do protocolo, requerente; 12.3).
  2. **Anexos:** área "Arraste arquivos para cá" (aceita arquivos do Explorador) e o botão **"Anexar arquivos…"**
     (seletor com seleção múltipla, filtro "Todos os arquivos"). **Qualquer tipo de arquivo.** Uma linha por anexo: nome,
     tamanho, **Destino** (12.3), prévia do caminho final ("PROJETO\REVISÃO 03\planta.pdf" ou "já está no processo (mesmo
     conteúdo)") e "Remover da lista". Nada é copiado antes do clique em Analisar.
  3. **Pedido para a IA:** caixa de texto de várias linhas (até 20.000 caracteres; opcional), com a dica "Ex.: Analise a
     reapresentação do projeto e confira as pendências do despacho anterior."
  4. Dois botões: **"Analisar com o ChatGPT"** (subtítulo "Codex · plugin analise") e **"Analisar com o Claude"**
     (subtítulo "Claude Code · plugin análise pro"). Cada um fica desabilitado com o motivo exato na dica (12.11) quando
     falta algo.
- **Aviso antes da primeira execução de cada provedor** (texto exato em 12.8; `ConfirmarAsync` com "Concordo e analisar"
  / "Cancelar"). O aceite fica nos ajustes locais com a versão do texto e a data UTC; texto novo (versão maior) pede de
  novo. Recusar não copia nada e não inicia nada.
- **Depois do clique:** arquiva os anexos (12.3), grava o registro (12.10), inicia o agente (12.4 ou 12.5) e troca a
  página para a **vista da execução**: provedor, processo, tempo decorrido, andamento ao vivo, "Cancelar análise",
  "Mostrar registro no Explorador". Ao terminar com resultado: importa e abre o `.pmvrel` (12.6), com a InfoBar de
  sucesso. Sem resultado ou com erro: InfoBar com a mensagem exata e o caminho do registro; o formulário volta preenchido.

### 12.3 Anexos e processo (regra do kit, `organizacao-arquivos.md` e `pasta_processo.py`, só leitura)

O editor aplica a mesma regra que o kit aplica a "arquivo enviado no chat ou anexado", reescrita em C# (o script do kit
não é executado pelo editor):
- **Destino** de cada anexo, escolhido pelo usuário na linha: "Documento (DOCUMENTOS)", "Projeto — primeira apresentação
  (PROJETO\INICIAL)", "Projeto — reapresentação (nova REVISÃO NN)" e, se já houver revisão, "Projeto — juntar à
  REVISÃO NN atual". Padrão: primeira apresentação se `PROJETO\INICIAL` não existe ou está vazia; senão, reapresentação
  nova. Todos os anexos marcados como reapresentação nova vão para **a mesma** `REVISÃO NN` nova.
- **REVISÃO NN:** maior `^REVISÃO\s+(\d+)\b` (sem caixa; se não houver, `^REVISAO\s+(\d+)\b`) de `PROJETO` + 1, com
  2 dígitos, nome `REVISÃO NN` (com acento, como o kit). Criada só no clique em Analisar.
- **Cópia, nunca movimento:** o original é aberto só para leitura (conteúdo, datas e atributos não mudam). Mesmo sha256
  já no destino com o mesmo nome ⇒ "já arquivado" (não duplica); nome igual com conteúdo diferente ⇒ `nome (2).ext`,
  `(3)`…; cada cópia vai para um `.tmp` oculto da mesma pasta e é movida **sem sobrescrever**; o sha da cópia é conferido.
- **Recusados** (mensagem exata, o resto segue): pasta ("Arraste arquivos, não pastas: <nome>."), atalho `.lnk`/`.url`
  ou link simbólico ("<nome> é um atalho; anexe o arquivo de destino."), arquivo que sumiu ou não abre ("<nome> não pôde
  ser lido: <motivo>.").
- **Criar processo…:** `<pasta de processos>\<ano>\<nº> - <ano> - <REQUERENTE>` (requerente em maiúsculas, sem acentos,
  como `nome_requerente` do kit) com `DOCUMENTOS` e `PROJETO\INICIAL`. Se já existe uma pasta desse nº/ano (inclusive
  "FINALIZADO - …" ou "98765-2099 - …", pela leitura de `Processos`), usa a existente, sem renomear nem duplicar, e avisa
  "O processo <nº>/<ano> já existe: <pasta>.".

### 12.4 Analisar com o ChatGPT (Codex CLI + plugin `analise`)

O kit instala o plugin `analise` no Codex (`INSTALAR_KIT_PMVNI.cmd`: `$PLUGINS_CODEX = analise, analise-pro, despacho`,
marketplace `pmvni-kit`). O editor inicia o **Codex CLI** do usuário em modo não interativo:
- **Onde fica o `codex.exe`** (mesma regra do `engine_path` do kit): o mais novo (data de modificação) de
  `%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe` (o CLI que o app do ChatGPT/Codex instala; neste PC
  `…\bin\9e1da79431da526b\codex.exe`); senão `codex.exe` no `PATH`; ou o programa escolhido em Ajustes. Shims `.cmd`/`.ps1`
  (npm) não são executados.
- **Linha de comando exata** (`ProcessStartInfo.ArgumentList`, sem `cmd.exe` nem shell; diretório de trabalho = a pasta do
  processo; pedido pela entrada padrão em UTF-8):
  `"<codex.exe>" exec --skip-git-repo-check --cd "<PASTA DO PROCESSO>" --json --output-last-message "<PASTA DO PROCESSO>\.pmvni-ai\editor\<id>\ultima-mensagem.md" -`
  Sem `--sandbox`, `--model`, `--ignore-user-config`, `--ephemeral` nem `-c`: vale a configuração real do usuário
  (`%USERPROFILE%\.codex\config.toml`, ou `CODEX_HOME`): modelo, `sandbox_mode` (neste PC `danger-full-access`),
  `approval_policy` (`never`), plugins, MCP e o `AGENTS.md` da pasta de processos — o mesmo que o app do ChatGPT usa.
- **Pedido** (texto exato; as linhas entre colchetes só existem quando há conteúdo):
  ```
  Use $analise para analisar o processo PMVNI desta pasta, com as 28 seções, as referências do índice do kit e o relatório oficial em PDF.
  Pasta do processo: <PASTA DO PROCESSO>
  Processo: <nº>/<ano> — <requerente>
  [Arquivos que o usuário anexou agora, já copiados na pasta do processo pelo Editor de Relatórios PMVNI:]
  [- <caminho relativo ao processo> (<situação: copiado | já estava no processo>)]
  [Pedido do usuário:]
  [<texto da caixa, como foi digitado>]
  ```
  A primeira linha é o `defaultPrompt` do `plugin.json` do `analise` (2.4.0). O pedido é guardado em `pedido.md` (12.10).
- **Andamento:** a saída `--json` (uma linha JSON por evento) vai inteira para `saida.jsonl`; a tela mostra as últimas
  2.000 linhas interpretadas de forma tolerante: `item.completed` com `agent_message` ⇒ "Mensagem: <texto>",
  `command_execution` ⇒ "Comando: <comando>", `file_change` ⇒ "Arquivos alterados: <n>", `mcp_tool_call` ⇒ "Ferramenta:
  <servidor>/<ferramenta>", `error` ou `turn.failed` ⇒ "Erro: <mensagem>"; os demais eventos não aparecem; linha que não
  é JSON aparece como veio (cortada em 2.000 caracteres). A saída de erro vai para `erros.txt`.
- **Fim:** código de saída 0 ⇒ procurar o resultado (12.6) em `.pmvni-ai\analise\delivery.json` (pasta de controle do
  `analise_runtime.py`); código ≠ 0 ⇒ "O Codex terminou com o código <n>. Veja o registro: <pasta>.". A
  `ultima-mensagem.md` aparece num expansor "Última mensagem do ChatGPT".

### 12.5 Analisar com o Claude (plugin `analise-pro`, pelo controlador do kit)

Reconciliação com o kit (lido em 15/11/2026): o `analise-pro` 1.6.1 existe **só no Codex** (`$PLUGINS_CLAUDE = analise,
despacho`; não há `.claude-plugin` nele) e o `CLAUDE.md` da pasta de processos diz que ele "é acionado pelo Codex (perfil
somente Claude: Sonnet 5.5 + Opus 5.5, chamados pelo controlador)". O `SKILL.md` dele define o **modo automático** como o
padrão: o modelo da conversa "SÓ dá a partida" com um único comando (`pro_runtime.py --case "<PROCESSO>" iniciar`),
"qualquer modelo da conversa serve", e todo o trabalho (peças atuais, seis etapas, cota, validadores e gate) é feito pelo
controlador chamando o **Claude Code** (`claude --print --model claude-sonnet-5-5|claude-opus-5-5 …`) com a conta do
usuário. Por isso **"Analisar com o Claude" = o editor dá essa mesma partida**, sem uma conversa do Codex no meio (que
gastaria um modelo GPT só para rodar um comando e contrariaria o "somente Claude"); quem analisa é o Claude, pelo kit.
- **Onde ficam as peças:** o `pro_runtime.py` **instalado** (o mesmo que o Codex rodaria):
  `<CODEX_HOME ou %USERPROFILE%\.codex>\plugins\cache\pmvni-kit\analise-pro\<maior versão>\skills\analise-pro\scripts\pro_runtime.py`
  (versões comparadas por número). A versão do kit (`<pasta do kit>\01 - PLUGINS\plugins\analise-pro\.codex-plugin\plugin.json`)
  só gera aviso se for maior ("O kit tem o análise pro <x>; instalado <y>. Atualize pelo INSTALAR_KIT_PMVNI.cmd."). O
  **Python**: `python.exe` do `PATH` que não seja o atalho da Microsoft Store (`%LOCALAPPDATA%\Microsoft\WindowsApps`),
  senão `py.exe -3`, ou o escolhido em Ajustes. O `claude.exe` é o que o kit acha pelo `PATH`
  (`shutil.which`; neste PC `%LOCALAPPDATA%\Microsoft\WinGet\Links\claude.exe`); o editor só o usa para o diagnóstico.
- **Linhas de comando exatas** (mesmas regras de `ArgumentList` e diretório de trabalho):
  `"<python.exe>" -B -X utf8 "<pro_runtime.py>" --case "<PASTA DO PROCESSO>" iniciar` (uma vez) e
  `"<python.exe>" -B -X utf8 "<pro_runtime.py>" --case "<PASTA DO PROCESSO>" situacao` (a cada 10 s). Com `py.exe`, o
  primeiro argumento é `-3`. O `iniciar` imprime um JSON (`situacao` `INICIADA` ou `JA_EM_ANDAMENTO`, `aviso`) e o próprio
  kit abre `.pmvni-ai\analise-pro\executar-auto.cmd` pelo `explorer.exe`, numa janela minimizada; a análise segue sozinha,
  **fora do editor**, e no fim o kit avisa pelo Windows e abre o PDF (comportamento do kit, que o editor não muda).
- **Andamento:** o JSON de `situacao` (`situacao`, `rotulo`, `etapa`, `erro`, `pdf`, `eventos`) e o fim do
  `.pmvni-ai\analise-pro\auto-log.txt` (lido a cada 2 s, só leitura, últimas 2.000 linhas). As etapas aparecem com os
  nomes "Preparo", "Análise", "Auditoria técnica", "Produção do relatório", "Auditoria cruzada" e "Revisão final"
  (`prepare` … `final_review`); a situação, com o `rotulo` do kit. Cada mudança de situação vai para `situacoes.jsonl`.
- **Pedido escrito:** o modo automático do `analise-pro` 1.6.1 não recebe instruções (as peças são escolhidas pelo
  Claude). O texto da caixa é guardado em `pedido.md` e a página mostra, sob o botão, "O análise pro do kit roda sozinho e
  não recebe o pedido escrito; ele fica guardado no registro da execução." Os anexos valem: são arquivados antes da
  partida e o kit os encontra na pasta do processo.
- **Fim:** `CONCLUIDO` ⇒ resultado em `.pmvni-ai\analise-pro\delivery.json` (12.6); `FALHOU`, `OBJECOES`,
  `PAUSADO_COTA`, `AGUARDANDO_COTA`, `INTERROMPIDA`, `NAO_PARTIU` ⇒ a InfoBar mostra o `rotulo` e o `erro` do kit como
  vieram e o botão **"Continuar a análise"** (roda `iniciar` de novo, que o kit retoma de onde parou; em `AGUARDANDO_COTA`
  só acompanha). `JA_EM_ANDAMENTO` ⇒ só acompanha a execução que já existe.
- **Cancelar:** com confirmação ("Cancelar a análise com o Claude?", "O kit guarda o que já foi feito; Continuar a
  análise retoma de onde parou.", "Cancelar análise", "Voltar"), o editor encerra a árvore do processo `pid` do
  `situacao` (só se estiver vivo, se for `python*.exe` ou `py.exe` e se tiver começado depois de `inicio` − 1 min); o
  kit passa a "interrompida".

### 12.6 Resultado e importação

- **Onde:** `<processo>\.pmvni-ai\analise\delivery.json` (ChatGPT) ou `<processo>\.pmvni-ai\analise-pro\delivery.json`
  (Claude), escritos pelo kit no fim (`analise_runtime.py`/`pro_runtime.py`: chaves `registry`, `document`, `pdf`,
  `visual_audit`, caminhos absolutos). O documento é o `document` (o `relatorio_dados.json` do kit).
- **Conferências** (mensagem exata quando falha; nada é importado): `delivery.json` ausente ou não modificado desde o
  início da execução (tolerância 2 s) ⇒ "A análise terminou, mas não deixou relatório novo (delivery.json não foi
  atualizado). Veja o registro: <pasta>."; JSON inválido ou sem `document` ⇒ "O delivery.json da análise não pôde ser
  lido: <motivo>."; `document` fora da pasta do processo, link ou junção ⇒ "O relatório indicado pela análise está fora
  da pasta do processo: <caminho>."; ausente ⇒ "O relatório indicado pela análise não foi encontrado: <caminho>.";
  maior que 50 MB ⇒ o limite da importação da Tarefa 4.
- **Importar:** `ImportarAnaliseAsync(processo, document)` (Tarefa 13: nome `Relatorio_<nº>_<ano>.pmvrel`, `_2`… sem
  sobrescrever; a capa decide o nome), abre no módulo Relatórios e mostra "Relatório da análise com o <ChatGPT|Claude>
  importado: <nome>.pmvrel. O PDF do kit continua em <pdf>." Erro do importador ⇒ a mensagem dele, sem `.pmvrel`.
- `Processos.Detalhar` passa a listar também `relatorio_dados*.json` das subpastas de `.pmvni-ai` (o nome atual do kit;
  hoje só `dados*.json`), para que "Importar análise da IA" ache também os resultados gerados fora do editor.

### 12.7 Segundo plano, uma por processo, fechar o app

- A execução pertence à janela (não à página): trocar de módulo, abrir outro relatório ou voltar ao formulário não a
  interrompe. Enquanto houver execução, a barra de título mostra "Analisando com o <ChatGPT|Claude> · <nº>/<ano> · <mm>
  min" e clicar nela abre a vista da execução. Mais de um processo ao mesmo tempo é permitido; **um por processo**: a trava
  `<processo>\.pmvni-ai\editor\execucao.lock` (pid do editor, provedor, início; abandonada se o pid não vive) e, no Claude,
  também a do kit (`JA_EM_ANDAMENTO`). Segunda tentativa no mesmo processo ⇒ botão desabilitado com "Já há uma análise em
  andamento neste processo.".
- **Sem tempo limite** (uma análise completa leva horas; o kit espera a cota). O diagnóstico (12.8) tem 15 s por comando.
- **Fechar o editor:** com execução do ChatGPT (processo filho do editor) ⇒ `ConfirmarAsync("Cancelar a análise e
  fechar?", "A análise com o ChatGPT em <nº>/<ano> será interrompida.", "Cancelar análise e fechar", "Continuar")`;
  com execução do Claude ⇒ só informa ("A análise com o Claude continua sozinha na janela minimizada do kit. Ao abrir o
  editor de novo, o resultado é importado.") e fecha. As execuções do Claude em andamento ficam nos ajustes locais
  (`execucoes_claude`: pasta do processo e pasta do registro); ao abrir, o editor roda `situacao` de cada uma e, se
  `CONCLUIDO`, mostra a InfoBar "A análise com o Claude de <nº>/<ano> terminou." com "Importar relatório".

### 12.8 Ajustes › Inteligência artificial

Seção nova da página Ajustes, depois de "Legislação". Texto: "O editor não faz a análise: ele inicia o ChatGPT (Codex) ou
o Claude instalados neste computador, com a configuração, os plugins do kit PMVNI, os programas (QGIS…) e as pastas de rede
deles, e importa o relatório no fim." Um cartão por provedor:
- **ChatGPT (Codex · plugin analise):** programa (caminho, "Escolher programa…", "Usar o padrão"); situação
  ("Codex <versão> · conectado com a conta do ChatGPT · plugin analise <versão> habilitado", com cada parte trocada por
  "não encontrado", "não conectado", "não verificado: <1ª linha>" ou "plugin analise não habilitado no Codex"); acesso.
- **Claude (Claude Code · plugin análise pro):** Python (caminho e versão, "Escolher programa…"); "Claude Code <versão> ·
  conectado com a conta do Claude"; "análise pro <versão instalada> (kit: <versão do kit>)"; acesso.
- **Acesso**, por provedor: (•) **"Usar minha conta (login do programa)"** ou ( ) **"Usar chave de API"** com uma
  `PasswordBox`, "Guardar chave", "Remover chave" e a linha "Chave guardada, protegida pelo Windows para este usuário."
  Para o Claude, "Usar chave de API" fica **desabilitado** enquanto o `pro_runtime.py` instalado contiver a âncora
  `API paga desativada` (o kit: "Somente conta padrão do Claude: sem API paga, chaves…"), com a dica "O análise pro do kit
  usa somente a conta do Claude (política do kit de 14/11/2026)." Se o kit mudar, a opção passa a funcionar sem mudar o
  editor (12.13, decisão 4).
- **"Testar"** por provedor: roda só comandos locais de cada programa, sem pedir nada ao modelo:
  `codex --version`, `codex login status`; `claude --version`, `claude auth status`; `python --version`; lê
  `config.toml` (`[plugins."analise@pmvni-kit"]` com `enabled = true`) e a pasta `plugins\cache\pmvni-kit`. Cada comando
  com 15 s; o resultado substitui a linha de situação. No modo chave, acrescenta "Chave guardada; ela só é conferida pelo
  provedor na primeira análise." A sessão só informa: login desconhecido não bloqueia a análise.
- **Aviso** (ver §12.2), com "Rever aviso". Texto, versão 1:
  - ChatGPT: "Analisar com o ChatGPT inicia o Codex instalado neste computador, com a sua configuração e o plugin analise
    do kit PMVNI. O Codex lê os arquivos do processo, usa os programas e as pastas de rede deste computador e envia à
    OpenAI o pedido e o conteúdo de que precisar, como faz no app do ChatGPT. O editor não envia nada por conta própria:
    ele só inicia o Codex, mostra o andamento e importa o relatório no fim."
  - Claude: "Analisar com o Claude inicia o análise pro do kit PMVNI, que roda sozinho numa janela minimizada e chama o
    Claude Code deste computador com a sua conta do Claude (Sonnet 5.5 e Opus 5.5, como o kit define). O Claude Code lê os
    arquivos do processo, usa os programas e as pastas de rede deste computador e envia à Anthropic o conteúdo de que
    precisar. O editor não envia nada por conta própria: ele só dá a partida, mostra o andamento e importa o relatório
    no fim."

### 12.9 Chave de API

- Guardada **por usuário do Windows** com DPAPI (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`, entropia
  fixa `"PMVNI-Editor-Agentes-v1"`) em `<dados>\segredos\<chatgpt|claude>.dpapi` (`PastaDados.Raiz`; gravação atômica,
  tmp → troca). Nunca em `ajustes.json`, no registro da execução, em log, no resultado do roteiro, no `.pmvrel`, em
  receita ou no `guardado` do Recriar PC. A tela nunca mostra a chave guardada (só "Chave guardada…").
- Entregue **só como variável de ambiente do processo filho**: ChatGPT ⇒ `CODEX_API_KEY` (a variável que o `codex exec`
  aceita para usar a chave; conferida na Tarefa 19, passo 0); Claude ⇒ `ANTHROPIC_API_KEY` (hoje sem uso: 12.8).
- **Em todo processo filho** o editor remove do ambiente herdado `OPENAI_API_KEY`, `CODEX_API_KEY`, `ANTHROPIC_API_KEY`
  e `ANTHROPIC_AUTH_TOKEN` e só põe a do provedor no modo chave: no modo conta, vale só o login do programa (como o kit
  pede: "variáveis de chaves são removidas dos processos filhos").
- Escolha DPAPI em vez do Gerenciador de Credenciais: sem P/Invoke, o arquivo fica na pasta de dados (os testes usam
  `PMVNI_DADOS_DIR`, sem tocar no cofre do Windows) e a chave não vai para outro PC (depois de formatar, digita-se de novo).

### 12.10 Registro da execução (pasta do processo)

Como o kit guarda os controles em `.pmvni-ai\<etapa>`, o editor guarda o seu em `<processo>\.pmvni-ai\editor\<AAAA-MM-DD_HHMMSS>-<chatgpt|claude>\`
(criada no clique; nunca reutilizada; `_2`… se colidir): `pedido.md` (o pedido exato), `anexos.json` (por anexo: nome de
origem, destino relativo, sha256, tamanho, `COPIADO` ou `JA_ARQUIVADO`), `execucao.json` (provedor, modo `conta` ou
`chave` — **nunca a chave** —, programa e versão, linha de comando com o pedido trocado por `<pedido.md>`, início e fim
UTC, código de saída, situação final `concluida|falhou|cancelada|sem_resultado`, `delivery.json` e `document` usados,
`.pmvrel` criado, usuário do Windows, versão do editor; gravado de forma atômica a cada mudança), `saida.jsonl` e
`erros.txt` (ChatGPT) ou `situacoes.jsonl` (Claude), e `ultima-mensagem.md` (ChatGPT). O editor não grava nada dentro das
pastas de controle do kit (`.pmvni-ai\analise`, `.pmvni-ai\analise-pro`): elas são do agente.

### 12.11 Botões desabilitados (motivos exatos)

- Sem processo: "Escolha o processo."; execução no processo: "Já há uma análise em andamento neste processo.".
- ChatGPT: "O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes ›
  Inteligência artificial."; "O plugin analise do kit PMVNI não está habilitado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd
  (fora do editor)."; modo chave sem chave: "Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua
  conta.".
- Claude: "O Python não foi encontrado neste computador (o análise pro do kit precisa dele)."; "O plugin análise pro do
  kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor)."; "O Claude Code não foi
  encontrado neste computador (claude.exe)."; modo chave: "O análise pro do kit usa somente a conta do Claude (política do
  kit de 14/11/2026). Em Ajustes › Inteligência artificial, use Usar minha conta.".

### 12.12 Restrições, segurança e testes

- O editor só inicia **dois programas** para esta função: o `codex.exe` localizado (só `exec`, `--version` e `login
  status`) e o Python com o `pro_runtime.py` instalado do kit (só `iniciar` e `situacao`), além de `claude --version`/`auth
  status` e `python --version` no diagnóstico. Nunca `cmd.exe`, PowerShell, `UseShellExecute` ou texto de linha de comando
  montado à mão: sempre `ArgumentList`. Nunca o executável de um anexo.
- O editor precisa ser aberto pelo `explorer.exe` (regra do usuário) para que os filhos herdem o Windows real; aberto pelo
  shell do app Claude, eles herdariam o pacote MSIX.
- Gravações nas pastas de processos (emenda o §10.10 e as restrições globais): só os anexos copiados (e as pastas
  `DOCUMENTOS`, `PROJETO\INICIAL`, `PROJETO\REVISÃO NN` e a do processo, se novas), `.pmvni-ai\editor\…` e o `.pmvrel` da
  importação; sem nunca mover, renomear, apagar ou sobrescrever. O que o agente grava é do agente (o editor não confere nem
  desfaz).
- **Testes nunca chamam IA real**: um programa de teste, `tests/AgenteFalso` (console net10.0), faz o papel do `codex.exe`
  (eventos `--json`, `--output-last-message`, `delivery.json` e um `relatorio_dados.json` fictício, código de saída), do
  Python com o `pro_runtime.py` (`iniciar` que parte um `auto` desanexado, `auto-status.json`, `auto-log.txt`, `situacao`)
  e responde `--version`, `login status` e `auth status`; o cenário vem de um JSON indicado por `AGENTE_FALSO_CENARIO`.
  O orquestrador tem uma trava de teste (`SomenteExecutaveisEm`) que recusa qualquer programa fora da pasta do agente
  falso. O roteiro usa o mesmo agente falso (publicado em `tests\saida\agente-falso`). A execução real fica para a
  conferência humana da Tarefa 14.
- Só pastas fictícias (`tests/corpus/processos`, copiado para `tests/saida/…`) e chaves falsas geradas no teste; os testes
  conferem que a chave não aparece em nenhum arquivo gravado (UTF-8, UTF-16 e base64).

### 12.13 Decisões minhas (registradas)

1. "Analisar com o Claude" dá a partida do `analise-pro` pelo `pro_runtime.py … iniciar` instalado, sem conversa do Codex
   no meio (o kit diz que a conversa só dá a partida e que qualquer modelo serve; assim só modelos Claude trabalham).
2. "Analisar com o ChatGPT" usa `codex exec` com a configuração real do usuário (sem `--sandbox`/`--ignore-user-config`),
   para ter o mesmo acesso a arquivos, programas, MCP e rede que o app do ChatGPT tem.
3. O resultado é o que o kit declara no `delivery.json` (`document`), nunca uma busca por nome de arquivo; o editor não
   valida o mérito (o gate do kit já fez isso) e o `.pmvrel` é revisado por pessoa.
4. Chave de API do Claude: guardável, mas a opção fica desabilitada enquanto o `analise-pro` instalado proibir API (âncora
   `API paga desativada` no `pro_runtime.py`). O pedido do usuário ("usar API ao invés da conta") vale integralmente para
   o ChatGPT; para o Claude, a regra do kit prevalece e o motivo aparece na tela.
5. ChatGPT roda como filho do editor (para receber a chave por variável e poder cancelar); o Claude roda na janela do kit,
   fora do editor, e sobrevive ao fechamento do editor.
6. Anexos: o usuário escolhe o destino por linha (padrão pela regra do kit); a cópia segue o `copiar` do `pasta_processo.py`
   (sha, `(2)`), sem executar o script do kit; pastas e atalhos recusados.
7. Registro em `.pmvni-ai\editor\…` (ao lado dos controles do kit, como o kit pede para logs), sem tocar nas pastas de
   controle do kit.
8. Diagnóstico sem pedir nada ao modelo ("Testar" não gasta cota); a chave só é conferida na primeira análise.
9. Sem tempo limite para a análise; uma execução por processo; várias em processos diferentes.
