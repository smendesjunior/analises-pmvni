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
do relatório é conteúdo não confiável; nada sai da máquina; dados reais só locais; regras do
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
  `C:\Users\smendesjunior\OneDrive\PMVNI\SIDNEY JUNIOR\PROCESSOS`, configurável em Ajustes), no
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
- **Legislação:** lista os arquivos da pasta de legislação (padrão
  `C:\Users\smendesjunior\OneDrive\PMVNI\SIDNEY JUNIOR\LEGISLAÇÃO`, configurável), com filtro
  por nome; abrir no programa padrão. Nada é copiado nem redistribuído.
  **Substituído pela seção 11** (pedido de 14/11/2026): a legislação vem compilada e embutida no
  executável; não há mais lista de pasta nem pasta de legislação.
- **Ajustes:** pastas de processos e de legislação (a de legislação sai: seção 11), tema, intervalo do salvamento automático,
  pasta de recuperação (somente leitura, informativo), versões (app, modelo, WebView2).

Abrir arquivos externos: só extensões de documento (`.pdf`, `.docx`, `.doc`, `.xlsx`, `.xls`,
`.png`, `.jpg`, `.jpeg`, `.dwg`, `.dxf`, `.txt`) pelo shell do Windows (`UseShellExecute`);
qualquer outra extensão (scripts, executáveis, atalhos) é recusada com aviso. Leitura das pastas
é feita fora da thread da interface e tolera pastas grandes e nomes fora do padrão (aparecem
com o nome da pasta como veio). Dados reais dessas pastas nunca entram em testes versionados:
os testes usam uma árvore fictícia em `tests/corpus/processos/`.

### 9.1 Faixa de opções por módulo (pedido do usuário)
A faixa de opções troca inteira quando o módulo do trilho muda (abas e comandos). "Arquivo" e
"Ajuda" existem em todos; a aba aberta por padrão é a segunda.
- **Relatórios:** Arquivo · Página Inicial · Revisão · Exibir · Ajuda (como na seção 5.5).
- **Despachos:** Arquivo · Página Inicial (Abrir, Abrir PDF, Mostrar no Explorador, Ir ao
  processo, Atualizar lista; filtro Todos/Correção/Aprovação) · Exibir (agrupar por processo ou
  por data, ordenar) · Ajuda.
- **Processos:** Arquivo · Página Inicial (Abrir relatório, Importar análise da IA, Abrir
  pasta no Explorador, Atualizar, filtro por ano) · Exibir (ordenar por número, data ou
  requerente; mostrar pastas PROJETO/DOCUMENTOS) · Ajuda.
- **Legislação:** Arquivo · Página Inicial (Abrir, Mostrar no Explorador, Atualizar, Copiar
  caminho) · Exibir (ordenar por nome ou data) · Ajuda. **Substituída pela faixa da seção 11.4**
  (e a pesquisa em Legislação passa a ser no texto das leis embutidas, não no nome do arquivo).
- **Ajustes:** sem faixa de comandos (só a página de ajustes); Arquivo e Ajuda continuam no topo.
A caixa de pesquisa da barra de título também muda de alvo: no relatório, o texto das folhas;
em Processos, número ou requerente; em Despachos e Legislação, o nome do arquivo.
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
uma **cópia** do DOCX, com perfil próprio (`-env:UserInstallation=` na pasta de trabalho, para não ser entregue a
um LibreOffice aberto) e `TEMP`/`TMP` do processo apontados para a pasta de trabalho; sem janela; prazo de 5 min
(depois encerra só a árvore desse processo); o Word **nunca** é automatizado. Depois: o DOCX não pode ter mudado
(sha), o PDF precisa abrir no leitor mínimo (`InfoPdf`: páginas) e listar a fonte **Verdana** (`/BaseFont`, sem o
prefixo de subconjunto). Localização: `PMVNI_SOFFICE` (testes) → `%ProgramFiles%\LibreOffice*\program\soffice.com`
→ `%ProgramFiles(x86)%\…` → `soffice` no PATH. **Sem LibreOffice**, com falha ou sem Verdana: grava **só o DOCX**
(e o recibo com o motivo) e mostra a mensagem clara: "O LibreOffice não está instalado neste computador. O despacho
foi gravado só em Word (DOCX). Para o PDF, rode o instalador do kit (INSTALAR_KIT_PMVNI.cmd) e use Despachos ›
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

## 11. Legislação compilada embutida (pedido do usuário, 14/11/2026)

Pedido: "A legislação tem que estar compilada." Esclarecido: "Embutir no kit mas somente o texto. Se houver imagem
por, mas comprimir para baixar o tamanho." Interpretação vinculante: o módulo Legislação mostra a legislação
**embutida no próprio executável**, em texto compilado (versões consolidadas), e não mais uma lista de PDFs de uma
pasta. Vai só o texto e as imagens que os atos contêm, comprimidas. Esta seção substitui, para a Legislação, o item
"Legislação" da seção 9, a faixa "Legislação" e o alvo da pesquisa da seção 9.1, e a "pasta de legislação" dos
Ajustes (§9). Abrir o PDF original continua possível só como ação secundária, quando a pasta do kit existe.

### 11.1 O que entra e por quê (medido no kit em 15/11/2026)

Fonte: `<kit>\03 - LEGISLACAO` (somente leitura), 61 arquivos, 93 MB. Inspeção com PyMuPDF 2.1:

| Grupo | Arquivos | Páginas | Decisão |
|---|---|---|---|
| `MUNICIPAL` — PDM Lei 1.382/2020 e Código de Obras LC 70/1990, **texto compilado** da Câmara | 2 | 81 + 32 | **Entram.** Texto extraível em todas as páginas com texto; o PDM tem 34 páginas de tabelas (anexos de parâmetros por zona, detectadas por `find_tables`: 56 × 7, 54 × 3…) e 15 imagens (mapas de zoneamento dos anexos); a pág. 79 do PDM não tem texto (só 2 mapas). |
| `MUNICIPAL\PERIMETROS URBANOS` — Leis 1.271/2017, 1.276/2017, 1.301/2018 | 3 | 21 + 11 + 8 | **Entram.** Texto extraível; 7 páginas sem texto (mapas), que ficam só como as imagens que já são. |
| `FEDERAL` — 10 leis e decretos, texto compilado do Planalto | 10 | 362 | **Entram inteiros** (o Código Civil vem completo, 194 páginas; o viewer abre no art. 1.277, como o nome do arquivo indica). Texto riscado do Planalto (redação revogada) é detectado pelo PyMuPDF (`TEXT_COLLECT_STYLES`, bit de tachado: 229 trechos no Estatuto da Cidade, 242 na Lei 6.766) e **fica marcado como revogado**, nunca como vigente. |
| `ESTADUAL - CBMES (NORMAS TECNICAS)` — 35 NTs | 35 | 892 | **Entram.** Texto extraível em todas as páginas; muitas figuras e tabelas (NT 01 parte 6 com 190 páginas). |
| `MUNICIPAL\PDM - LEIS DE ALTERACAO (ORIGINAIS)` — 6 leis de alteração (1.391/2020, 1.415/2021, 1.484/2022, 1.507/2022, 1.510/2022, 1.556/2023) | 6 | 41 | **Não entram.** Já incorporadas ao texto compilado do PDM: o compilado cita cada uma nas notas de redação (1.391 ×23, 1.415 ×55, 1.484 ×7, 1.507 ×12, 1.510 ×3, 1.556 ×7) e o índice do kit diz "Já incorporada ao texto compilado; guardar como original". São digitalizações (22 MB; a 1.484 não tem camada de texto nenhuma): embuti-las duplicaria o texto e traria versões superadas. |
| `MUNICIPAL\PDM - LEIS DE ALTERACAO (ORIGINAIS)\DÚVIDAS DO NOVO PDM.docx` | 1 | — | **Não entra:** não é ato normativo (perguntas de trabalho). |
| `NORMAS ABNT` — NBR 9050:2020, NBR 16537:2024, NBR 6492:1994 e a lista "ONDE OBTER" | 4 | — | **Não entram.** Normas da ABNT são obras protegidas; o índice do kit marca as duas vigentes como "MANTER (LICENCIADO) … Não redistribuir: cópia do acesso pessoal à ABNT", e a 6492 também é da ABNT (e está superada). A lista `.md` não é ato normativo. |

Total embutido: **50 PDFs, 1.407 páginas**, 65,6 MB de origem (ficam fora 11 arquivos, 31 MB). Motivo jurídico (registrado): leis, decretos,
regulamentos e demais atos oficiais não são objeto de direito autoral (Lei 9.610/98, art. 8º, IV). As NTs do CBMES
são regulamentos baixados por portaria do Corpo de Bombeiros do ES (ato oficial estadual); os textos compilados são
publicados pelos próprios órgãos (Câmara Municipal e Presidência da República); as imagens embutidas são parte do
próprio ato (mapas, figuras e tabelas dos anexos). A proibição do `LICENSE` do plugin (desenho §10.10) cobre os
plugins e os modelos, não os atos públicos da pasta de legislação. Cada texto embutido mostra a fonte (arquivo do
kit, órgão, data no nome do arquivo) e mantém o aviso que o próprio PDF traz, quando houver ("Este texto não
substitui o original publicado…").

Regra de inclusão (vale na regeneração): a ferramenta classifica **todo** arquivo de `03 - LEGISLACAO` pelo catálogo
(§11.2). Arquivo sem entrada no catálogo faz a geração falhar; um arquivo com "NBR" ou "ABNT" no caminho nunca pode
ser marcado para entrar (a ferramenta recusa).

### 11.2 Ferramenta de extração (tempo de build, nunca no app)

`tools/legislacao/extrair.py` (Python 3.14 + PyMuPDF + Pillow, `python -I`), rodada pelo desenvolvedor quando o kit
muda; o app nunca extrai nada em tempo de execução e o `build-release.ps1` não precisa de PyMuPDF. Entradas: a raiz do
kit e `tools/legislacao/catalogo.json` (escrito à mão, versionado: para cada arquivo, `incluir` ou `excluir` com o
motivo; para os incluídos, id, esfera, título curto, forma de citação, apelidos da pesquisa, tipo de estrutura
`artigos` ou `itens`, e opcionalmente `abrir_em`). Saída: `src/Pmvni.Legislacao/Dados/legislacao.pmvleg` e o relatório
`docs/legislacao-embutida.md` (por texto: páginas, unidades, tabelas, imagens, páginas sem texto, trechos revogados;
totais e tamanho).

Extração por página (`get_text("dict", sort=True)` com `TEXT_COLLECT_STYLES`):
- **Cabeçalhos e rodapés repetidos** (mesmo texto normalizado na faixa de 7% do topo ou da base em ≥ 50% das páginas,
  com números de página trocados por `#`) são removidos.
- **Parágrafos** = blocos do PyMuPDF; linhas unidas com espaço, hifenização de fim de linha desfeita só quando a
  palavra unida existe em outro lugar do mesmo texto; um bloco é quebrado sempre que uma linha começa um dispositivo.
- **Estrutura (tipo `artigos`)**: `TÍTULO`, `CAPÍTULO`, `SEÇÃO`, `SUBSEÇÃO` (+ linha seguinte como nome) viram nós da
  árvore; `Art. 45`, `Art. 45-A`, `Art. 1.277` viram unidades; dentro delas, blocos `§ 2º`, `Parágrafo único`,
  incisos `III –` e alíneas `a)`; `ANEXO XII` vira unidade. **Tipo `itens`** (NTs): títulos numerados `5`, `5.1`,
  `5.1.2` (até 5 níveis, número crescente) e `ANEXO A`. Nada é renumerado nem corrigido: o rótulo é o do texto.
- **Notas de redação** ("(Redação dada pela Lei nº 1.743/2025)", "(Incluído pela…)", "(Revogado pela…)") ficam como
  notas do dispositivo onde aparecem.
- **Texto riscado** (tachado no PDF) vira trecho com `riscado: true`.
- **Tabelas**: `page.find_tables()`; cada tabela vira uma tabela de células de texto (células mescladas, que o
  PyMuPDF devolve como `None`, viram `colspan`); as palavras dentro da tabela saem do fluxo de parágrafos. Tabela com
  menos de 2 colunas ou mais de 30% de células vazias não é confiável: vira **recorte** (imagem da região da página)
  com o texto da camada de texto guardado para busca e cópia.
- **Imagens raster**: cada imagem é decodificada, reduzida para no máximo **1.600 px** no lado maior e regravada em
  **WebP qualidade 60** (Pillow, `method=6`); deduplicada pelo sha256 dos bytes originais (o brasão do Planalto, por
  exemplo, entra uma vez). Imagens com menos de 16 × 16 px são descartadas (fios e marcadores).
- **Figuras vetoriais**: `page.cluster_drawings()` só sobre os desenhos que não são fios (altura ou largura < 2,5 pt:
  tachados, sublinhados, bordas) nem fundos brancos (preenchimento com todos os canais > 0,97 — o Planalto põe um
  retângulo branco atrás de cada página); grupo fora das tabelas, com área ≥ 4 cm², menos de 85% da página e menos de
  0,02 caractere por pt² vira recorte a 150 dpi, WebP 60, com o texto que estiver dentro guardado para busca. (Sem
  esses filtros, o protótipo recortou 467 "figuras", 46 MB, quase todas páginas inteiras de texto do Planalto; com eles,
  113 figuras reais das NTs.)
- **Páginas sem camada de texto: nunca OCR, nunca texto inventado.** Se uma página não tem texto, ela entra só com as
  suas imagens (ou como recorte da página inteira a 110 dpi em tons de cinza, se não houver imagem raster que a
  cubra), e o viewer mostra: "Página N do PDF original sem texto: mostrada como imagem; o conteúdo não foi
  transcrito." Página totalmente vazia é omitida.

Pacote `legislacao.pmvleg` (ZIP determinístico: entradas em ordem, data fixa 1980-01-01, Deflate 9 para JSON e
Stored para WebP; mesma entrada ⇒ mesmos bytes):
- `manifesto.json`: `formato: "pmvleg"`, `versao_formato: "1.0.0"`, `versao_extrator`, `gerado_em` (UTC),
  `kit.sha256_conjunto` (sha256 da lista ordenada `caminho\tsha256` dos incluídos), e para cada texto `id`, `esfera`,
  `titulo`, `citacao`, `apelidos`, `arquivo` (relativo a `03 - LEGISLACAO`), `sha256`, `tamanho`, `paginas`,
  `paginas_sem_texto`, `unidades`; `excluidos` com `arquivo`, `sha256` e `motivo` (para a conferência saber o que já
  era conhecido).
- `leis/<id>.json`: a árvore (nós `titulo|capitulo|secao|subsecao|artigo|item|anexo|preambulo` com `id`, `rotulo`,
  `nome`, `pagina`, `blocos`, `filhos`); blocos `p`, `dispositivo` (`par|inciso|alinea`, `rotulo`), `nota`, `tabela`
  (`linhas` de células `{texto, colspan}`, `cabecalho`), `imagem` (`src`, `w`, `h`, `pagina`), `recorte` (`src`,
  `texto`), `pagina_sem_texto` (`pagina`); todo texto é uma lista de trechos `{t, riscado?}`. Só texto puro: nenhum
  HTML vem do PDF.
- `img/<sha256 16 hex>.webp`.

Se a regeneração não mudar nenhum sha e a `versao_extrator` for a mesma, a ferramenta não regrava nada ("Nada mudou")
— o arquivo versionado só muda quando o kit ou o extrator mudam.

### 11.3 Tamanho (medido) e embutimento

Medido em 15/11/2026 sobre os 50 PDFs incluídos (protótipo da extração): texto 4,1 MB em UTF-8 → **1,06 MB**
comprimido; 398 imagens únicas (25,9 MB de origem) → **7,25 MB** em WebP 60 com 1.600 px; páginas sem texto →
**0,22 MB**; figuras vetoriais (113 recortes) → **1,22 MB**. **Estimativa do pacote: ≈ 9,8 MB** (mais a estrutura JSON, que
acrescenta pouco ao texto comprimido; até ~10,5 MB). Referência: com 1.280 px e qualidade 50
as imagens cairiam para 6,0 MB, mas o texto miúdo das figuras das NTs fica ilegível; fica 1.600/60. Orçamento: **até
11 MB** (a ferramenta avisa acima e, então, o passo é medir 1.400 px/WebP 55 antes de seguir), **12 MB no máximo** (a
ferramenta e o teste falham acima). O executável passa de ~85 MB para ~95 MB.

Embutimento: `legislacao.pmvleg` é `EmbeddedResource` (LogicalName `legislacao.pmvleg`) de um projeto novo
`src/Pmvni.Legislacao` (net10.0, sem UI e sem WebView2), referenciado por `Pmvni.App` e `Pmvni.Verificador`. O
conteúdo já é comprimido (Deflate/WebP), então a compressão do single-file não ganha nada com ele; o recurso é lido
por `GetManifestResourceStream` (memória, sem extrair para o disco, sem `%TEMP%`). O arquivo gerado **é versionado**
(≈ 10 MB): o build não depende de PyMuPDF nem da pasta do kit, e o histórico só cresce quando o kit muda (regeneração
determinística).

### 11.4 Viewer no módulo Legislação

**Decisão: árvore nativa (WinUI `TreeView`) à esquerda e o texto num WebView2 à direita**, porque o PDM tem dezenas
de tabelas largas e as NTs têm figuras entre os parágrafos: controles de texto nativos (`RichTextBlock`) não têm
tabela, e a pesquisa com realce já existe em web (Tarefa 11). Modelo de segurança da Etapa 1, com host próprio:
- WebView2 InPrivate pela `FabricaWebView2`, mesmas configurações da `Seguranca` (menus, atalhos do navegador,
  DevTools só em Debug, janelas novas e downloads cancelados).
- Host virtual `leg.pmvni.invalid` **sem pasta mapeada**: tudo é respondido da memória pelo filtro
  `WebResourceRequested` (`/lei/<id>.html`, `/legislacao.js`, `/img/<nome>.webp`); qualquer outra URL ⇒ 403;
  navegação de nível superior só para `https://leg.pmvni.invalid/lei/<id>.html`.
- CSP: `default-src 'none'; img-src https://leg.pmvni.invalid; style-src 'unsafe-inline'; script-src
  https://leg.pmvni.invalid/legislacao.js`.
- O HTML é gerado pelo host a partir do JSON (todo texto escapado; nenhum HTML vindo do PDF). O script
  `legislacao.js` (TypeScript, mesmo esbuild do paginador) só rola, realça pela CSS Custom Highlight API e informa a
  unidade e o dispositivo da seleção pela ponte `{v:1,tipo,id,dados}`.

Tela: árvore **esfera → texto → títulos/capítulos/seções → artigos/itens/anexos** (Municipal, Estadual — CBMES,
Federal; na ordem do catálogo); página inicial do módulo "Sobre a legislação embutida" (versão, data, número de
textos, o que não está embutido e por quê, resultado da conferência com o kit). Ao escolher um nó, abre o texto e rola
até ele. Trecho revogado aparece riscado e cinza (como no Planalto), com o título "Texto revogado"; Exibir › "Mostrar
texto revogado" (ligado por padrão) esconde os trechos riscados. Páginas sem texto e recortes aparecem com a nota do
§11.2. Tamanho do texto: 100/125/150%.

Faixa (substitui a da §9.1): **Arquivo · Página Inicial** (Copiar citação, Copiar texto do dispositivo, Abrir o PDF
original, Mostrar no Explorador) **· Exibir** (Tamanho do texto, Mostrar texto revogado, Recolher a árvore) **· Ajuda**.
"Abrir o PDF original" e "Mostrar no Explorador" só existem habilitados quando `<kit>\03 - LEGISLACAO\<arquivo>` existe
(senão ficam desabilitados com a dica "Pasta do kit não encontrada."); abrem por `AberturaExterna`.

**Pesquisa** (a caixa da barra de título no módulo Legislação, retarget da §9.1: "Pesquisar na legislação"):
1. Consulta de dispositivo: `art. 45 pdm`, `artigo 1.277 código civil`, `art 45-A lc 70`, `nt 10-1 5.1.2`,
   `anexo xii pdm` → vai direto à unidade (apelidos do catálogo; sem o apelido, procura em todos e lista as unidades
   com esse rótulo).
2. Texto livre: em todos os textos (sem diferenciar maiúsculas nem acentos, espaços normalizados), lista de
   resultados no painel da esquerda (no máximo 200, agrupados por texto, com o rótulo da unidade e um trecho), Enter
   abre o primeiro; dentro do texto aberto, realce e "3 de 12" como no relatório. Resultado em trecho revogado leva a
   marca "(revogado)".
O índice de busca é montado em memória, em `Task.Run`, na primeira pesquisa.

**Copiar citação**: a unidade e o dispositivo da seleção (ou, sem seleção, a unidade no topo da vista) viram texto
pela forma de citação do catálogo: `art. 45 da Lei Municipal nº 1.382/2020 (PDM)`, `art. 45, § 2º, da Lei Municipal
nº 1.382/2020 (PDM)`, `art. 12, parágrafo único, …`, `art. 3º, inciso II, alínea "a", …`, `item 5.1.2 da NT 10 —
Parte 1 (CBMES)`, `Anexo II da Lei Municipal nº 1.382/2020 (PDM)`. "Copiar texto do dispositivo" copia a citação, uma
linha em branco e o texto puro. Copiar um trecho revogado mostra o aviso "Este trecho está revogado no texto
compilado." e copia mesmo assim com " (revogado)" no fim da citação.

### 11.5 Atualização

- O texto embutido só muda com uma nova versão do editor: o desenvolvedor roda
  `python -I tools/legislacao/extrair.py gerar --kit "<raiz>"`, confere `docs/legislacao-embutida.md` e gera o build.
- **Ajustes** mostra "Legislação embutida: versão de <gerado_em, dd/MM/aaaa>, <n> textos, extrator <versão>, conjunto
  do kit <8 primeiros hex>" e o resultado da conferência.
- **Conferência com o kit** (no app, ao abrir o módulo pela primeira vez na sessão, em `Task.Run`; e no autoteste do
  executável publicado, que o `build-release.ps1` já roda fora do pacote — o build só escreve o aviso, nunca falha, e
  não precisa de Python): sha256 de cada arquivo do
  manifesto (incluídos e excluídos) contra `<kit>\03 - LEGISLACAO`, mais os arquivos novos. Diferença ⇒ InfoBar de
  aviso no módulo: "A legislação do kit mudou depois da versão embutida neste editor (diferentes: D, novos: N,
  ausentes: A). O texto mostrado é o da versão embutida de <data>." Kit ausente ⇒ sem aviso no módulo (o texto
  embutido funciona sem o kit); Ajustes diz "Pasta do kit não encontrada: a conferência da legislação não foi feita."

### 11.6 Restrições e testes

- Nada sai da máquina; textos da interface em português; nenhum botão sem função.
- A ferramenta e os testes rodados pelo shell do Claude gravam só no repositório (`src/Pmvni.Legislacao/Dados/`,
  `docs/legislacao-embutida.md`) e em `tests/saida/legislacao/`; nunca em `%APPDATA%`/`%LOCALAPPDATA%`/`%TEMP%`. O app
  não grava nada para a legislação (nem cache).
- Fixture: as leis são públicas, mas os testes não dependem do kit inteiro; usam **um PDF fictício montado pelo
  próprio teste** com PyMuPDF ("Lei nº 9.999/2099 (fictícia)", metadados e id fixos, logo bytes fixos) com todos os
  casos (estrutura, tachado, tabela, imagem, figura vetorial, página sem texto, cabeçalho repetido, hifenização) e um
  "NT fictícia" de itens; o pacote gerado dele, `tests/corpus/legislacao/ficticia.pmvleg` (versionado, poucos KB), é
  o contrato entre o Python e o C#. Um teste com o kit real (via `PMVNI_KIT_RAIZ`) confere só contagens do pacote
  embutido e que nenhum arquivo `NBR` entrou.
- Recriar PC: o pacote vai dentro do executável; nada novo a instalar. O `recriar.json` passa a citar a legislação
  embutida e a leitura opcional de `03 - LEGISLACAO` do kit (conferência e PDF original).

### 11.7 Decisões minhas (registradas)
1. Viewer em WebView2 (com host `leg.pmvni.invalid` servido da memória), árvore nativa.
2. Leis de alteração originais fora (já incorporadas; digitalizações); Código Civil inteiro (abre no art. 1.277).
3. Texto revogado do Planalto mantido e marcado, nunca apresentado como vigente; ocultável.
4. Imagens: 1.600 px, WebP 60; páginas sem texto como imagem, sem OCR.
5. Pacote gerado versionado no repositório (build sem PyMuPDF); regeneração determinística.
6. Conferência com o kit só avisa; nunca bloqueia nem troca o texto embutido.
7. Pasta de legislação dos Ajustes (§9) deixa de existir: o PDF original é procurado em `<pasta do kit>\03 -
   LEGISLACAO` (a pasta do kit da §10.10).
