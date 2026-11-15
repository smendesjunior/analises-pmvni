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
