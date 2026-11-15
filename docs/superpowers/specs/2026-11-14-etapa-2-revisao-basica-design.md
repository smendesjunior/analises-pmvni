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
- **Legislação:** leitor dos PDFs de leis que o próprio usuário anexa em Ajustes › Legislação, com
  pesquisa, seleção, cópia e citação. **Ver a seção 11** (pedido de 15/11/2026), que substitui a lista
  de arquivos de uma pasta de legislação (primeira versão desta seção) e a legislação compilada
  embutida (versão de 14/11/2026): não há pasta de legislação, pacote embutido nem leitura do kit.
- **Ajustes:** pasta de processos, pasta do kit (seção 10), **Legislação** (anexar, listar, editar,
  substituir e excluir os PDFs: seção 11.1), tema, intervalo do salvamento automático, pasta de
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
- **Relatórios:** Arquivo · Página Inicial · Revisão · Exibir · Ajuda (como na seção 5.5).
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
