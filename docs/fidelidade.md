# Fidelidade documental — critérios e medidas (Etapa 1, Tarefa 9)

Suíte: `python -I tools/fidelidade.py {v1|v2|v3|v4|v5|v6|tudo} [--caso NOME ...]`. Casos: `tests/corpus/*.json`,
`dados_modelo.json` do modelo oficial (com `--instrucoes`) e os casos locais de `tests/local.json` (ignorado pelo git;
hoje um caso real). O gerador do kit roda como na V1: `gerar_relatorio.py --rascunho --manter-html` no modo padrão (Edge
aberto por `.cmd` via `explorer.exe`). **Onde ficam os temporários (D4):** tudo o que a rodada grava — PDFs e HTML do
kit e do editor, capturas, textos e a pasta de dados do editor (`PMVNI_DADOS_DIR`, com o perfil do WebView2 e as
sessões) — vai para `%USERPROFILE%\.pmvni-fid\<run>\`, uma pasta por rodada, apagada no fim (e `.pmvni-fid` some se
ficar vazia). Fica fora do OneDrive (nada do processo real numa pasta sincronizada) e fora do AppData/`%TEMP%` (o Python
iniciado pelo shell do Claude roda dentro do pacote MSIX, que virtualiza o AppData; o Edge do kit, aberto via
`explorer.exe`, lê e grava a mesma pasta porque ela está no perfil real do usuário). `tests/saida/*.json` levam só
contagens, hashes, páginas e porcentagens. No fim de cada rodada, `bin`/`obj` do Verificador e dos projetos que ele
compila em Release são apagados (depois de `dotnet build-server shutdown`). Os testes automáticos (`dotnet test`) usam
`PMVNI_DADOS_DIR=tests\saida\dados`: só dados fictícios do corpus passam por ali, e o build apaga `tests\saida`.

Comandos do Verificador usados pela suíte (todos aceitam `--instrucoes` e `--rascunho`, as opções do kit):
`html`, `plano`, `pdf` (pelo `Exportador`; estouro → recusa, código 3, nada gravado), `texto` e `capturas --dpi N`
(a "tela": plano composto num host oculto e aplicado, sem compor, num documento recém-carregado de outro host, mídia de
tela, escala 1).

## Critérios

| | Critério (do brief, salvo a V2 visual, mudada por decisão registrada abaixo) |
|---|---|
| Pixel diferente | \|Δ\| > 48 em tons de cinza (0–255), imagens a 150 dpi |
| V1 | HTML do editor = HTML do kit, byte a byte |
| V2 (tela × PDF do editor) | **texto por folha idêntico (porta principal)**; cada folha capturada a 150 dpi × página do PDF rasterizada pelo PyMuPDF a 150 dpi: **≤ 2,0 % de pixels diferentes com 1 px de tolerância de posição** (limite inicial desta métrica: 1,0 %; ver as decisões abaixo) (um pixel só conta se nenhum pixel da vizinhança 3 × 3 da outra imagem estiver a \|Δ\| ≤ 48; medido nos dois sentidos — tela contra PDF e PDF contra tela — e vale o maior). O estrito (sem tolerância; limite inicial 0,5 %) é medido e registrado só como informação, e o maior ladrilho de 32 × 32 px também (sem limite: ver "Medida local por ladrilhos") |
| V3 (PDF do editor × PDF do kit) | mesmo número de páginas; mesmo texto em cada página (cabeçalho, área útil, rodapé); ≤ 2 % de pixels diferentes por página |
| V4 | V1–V3 em todos os casos; `estouro` só verifica a recusa da exportação |
| V5 (Etapa 2, Tarefa 4) | HTML dos **dados efetivos do pacote** = HTML do kit, byte a byte (ver "V5" abaixo); `tudo` = V4 + V5 |

### Como o texto é comparado (método, registrado aqui como manda o brief)

- **Sem espaços**: todo espaço em branco é removido dos dois lados. O PDF separa linhas e blocos de outro jeito que o
  `innerText` (ex.: itens flex lado a lado saem colados no `innerText`); qualquer caractere a mais, a menos ou em outra
  página continua aparecendo.
- **Só o texto visível do PDF**: um caractere conta se há tinta (cinza < 230) dentro da caixa dele na página
  rasterizada. O PDF do kit grava glifos que o próprio Chromium recorta (recorte da área útil, [14; 196] × [28; 282]
  mm); a extração do PyMuPDF não respeita esse recorte (`TEXT_CLIP` não o vê).
- **Faixas da página** pelo centro de cada caractere: cabeçalho (y < 24 mm), rodapé (y > 283,5 mm), área (o resto).
  Cabeçalho e rodapé são comparados na ordem de posição (o kit pinta as caixas de margem em outra ordem que o editor);
  a área, na ordem do conteúdo do PDF (ordem de pintura do Chromium, a mesma nos dois PDFs).
- **Texto gerado por CSS** (`content: "…"` não vazio do `relatorio.css`, hoje só `"✎ INSTRUÇÃO — "`) sai do texto do
  PDF: está no PDF, não no `innerText`.
- **V2, ordem**: o `innerText` segue o DOM, o PDF a ordem de pintura (o Chromium pinta os blocos posicionados, como
  `.card`, depois do fluxo normal). A folha é igual à página quando (1) os caracteres são os mesmos, com as mesmas
  contagens, e (2) cada trecho (span) do PDF aparece inteiro no texto da folha. **O que isso garante, exatamente:** o
  mesmo conjunto de caracteres na folha e na página (nada a mais, a menos ou em outra folha) e que cada trecho contínuo
  do PDF existe, inteiro, no texto da folha. **Não garante a ordem entre trechos**: dois trechos trocados de lugar
  dentro da mesma folha passariam (a ordem de pintura do PDF não é a do DOM, então ela não é comparável aqui; a ordem
  é coberta pela V3, PDF × PDF, e pela V2 visual).
- **V3, diferenças conhecidas FIXADAS** (impressas por página e gravadas em `v3.json`; qualquer diferença além do
  fixado reprova a página): na faixa além da borda direita da área, o texto do kit tem de estar contido no do editor, e
  cada trecho do editor com caracteres ali tem de ser exatamente a etiqueta `INFORMAÇÕESDIVERGENTES`, com a parte além da
  borda sendo um fim dela (`ROTULOS_CORTADOS_PELO_KIT`); a parte dentro da área entra na comparação exata da área. Na
  sangria: no máximo 1 caractere por página no kit e nenhum no editor (`MAX_SANGRIA_KIT`, `MAX_SANGRIA_EDITOR`).
  - `fora_da_area`: caracteres visíveis com o centro além da borda direita da área (x > 196,1 mm). O kit corta tudo o
    que passa da área (recorte da página do Chromium); o editor não corta nada (especificação: "nada cortado"). Caso
    real: a etiqueta `INFORMAÇÕES DIVERGENTES` (`nowrap`) numa coluna da direita da tabela de índices: no kit sai
    "INFORMAÇÕES DIVERGEN", no editor inteira (até 202,3 mm). **O PDF do kit perde texto aqui.**
  - `sangria_rodape`: glifo de fonte > 7 pt na faixa do rodapé (o rodapé usa 6,2 pt). O Chromium do kit deixa na
    margem de baixo um fragmento de ~1 px do acento do 1º glifo da página seguinte ("Á" de um título); o editor não.

## V5: dados efetivos × kit, via pacote (Etapa 2, Tarefa 4)

Prova que o caminho que o editor percorre não muda o relatório: o `dados.json` é importado (itens com id, citações "item NN"
convertidas em referências, imagens copiadas para `assets/<sha256>.<ext>`), gravado num `.pmvrel`, relido, e o HTML sai dos
**dados efetivos** do projeto relido (`DadosEfetivos.Gerar`: itens incluídos, texto revisado no lugar do original, citações
impressas com a numeração atual) sem nenhuma revisão. Esse HTML tem de ser igual, byte a byte, ao do gerador do kit com o
`dados.json` original (a mesma comparação da V1).

- Comando: `Pmvni.Verificador html <dados.json> <saida.html> --via-pacote [--instrucoes] [--rascunho]`. Grava, ao lado da saída,
  `<saida>.pmvrel` (o pacote gravado e relido) e extrai os assets em `<saida>-assets\`; o HTML é renderizado a partir dessa pasta.
  Só o `html` aceita `--via-pacote` (os outros comandos o recusam).
- Casos: os mesmos da V4 (corpus, `dados_modelo --instrucoes` e os casos locais de `tests/local.json`). `tests/saida/v5.json`
  leva só `{caso, igual, sha256_kit, sha256_editor}`.
- Critério: `igual` em todos os casos (código de saída 0). Qualquer byte diferente reprova.

## V6: despacho × validador do kit (Etapa 2, Tarefa 15)

Prova que o despacho que o editor gera passa no mesmo validador que o kit usa para os despachos (`validate()` do
`validate_template_fidelity.py` do plugin `despacho`), além da porta em C# dessa comparação (`ValidacaoDespacho.Comparar`) e da
conferência de conteúdo (`ValidacaoDespacho.Conteudo`) que o próprio editor faz antes de gravar.

- Comando: `Pmvni.Verificador despacho <correcao|aprovacao> <dados.json> <saida.docx> --data AAAA-MM-DD [--kit <raiz>]
  [--intervencao obra-nova|alteracao|ampliacao]... [--area 210,52]`. Importa o `dados.json` em memória (nenhum pacote gravado),
  monta o despacho com todas as exigências marcadas, congela, preenche o modelo do kit (lido no lugar pelo `TEMPLATES_OFICIAIS.json`,
  sha conferido), valida e grava só o DOCX; imprime `DESPACHO_OK <sha256>`. Faltando campo: código 4 (com a lista); validação
  reprovada: código 5 (com os erros); modelo indisponível ou dados inválidos: código 1. Não há conferência da pasta do processo
  (ela não existe aqui).
- Casos: `curto-correcao`, `curto-aprovacao` (`--intervencao obra-nova --area 210,52`) e a correção de cada caso local de
  `tests/local.json`. A data é a do dia da rodada.
- O validador é importado do kit no lugar (`importlib.util.spec_from_file_location`, `sys.dont_write_bytecode = True`): nada é
  gravado na pasta do kit. Exige `check_registry(<kit>/02 - MODELOS DE DOCUMENTOS/TEMPLATES_OFICIAIS.json)[1] == []` e
  `validate(<modelo do papel>, <docx>) == []`.
- Os DOCX ficam em `%USERPROFILE%\.pmvni-fid\<run>\`, apagados no fim; `tests/saida/v6.json` leva só
  `{caso, tipo, erros_kit, sha256_docx}`.
- Critério: zero erros do kit em todos os casos (código de saída 0). `tudo` = V1, V2, V3, V5 e V6.
- Medido em 15/11/2026: `curto-correcao`, `curto-aprovacao` e o caso local aprovados (0 erros do kit).

## Calibração do cabeçalho e do rodapé (passo 2)

Medido com PyMuPDF no `curto`, página 1 (mm, origem no alto à esquerda; PDF de 209,889 × 297,011 mm nos dois lados).

| Medida | Kit (Edge 158) | Editor antes | Editor depois |
|---|---|---|---|
| Brasão (imagem do cabeçalho), topo | 5,556 | 6,615 | 5,556 |
| Faixa colorida do cabeçalho, y | 20,902–21,828 | 21,960–22,886 | 20,902–21,828 |
| "SECRETARIA MUNICIPAL DE OBRAS": x / linha de base / corpo | 121,481 / 11,906 / 10,5 pt | 121,511 / 12,962 / 10,495 pt | 121,481 / 11,906 / 10,5 pt |
| Linha do rodapé, y | 282,310–282,575 | 282,046–282,310 | 282,310–282,575 |
| Texto do rodapé, linha de base | 287,073 | 286,808 | 287,073 |
| Fim do texto da direita do rodapé, x | 196,056 | 195,995 | 196,056 |
| Divisão entre as duas caixas do rodapé, x | 139,700 | 134,144 | 134,144 (sem efeito visível) |
| Texto da área: deslocamento x (moda) | — | −0,025 (43 de 63 trechos) | 0,000 |
| Texto da área: linhas 1 px (0,265 mm) acima | — | 12 de 63 | 0 de 63 |
| V3 visual, página 1 | — | 2,846 % | 0,160 % |

O que mudou em `web/paginador/folha.css` (e por quê):

1. **Caixa da página em pixels inteiros.** O Chromium do kit encaixa a área útil em (53 px, 106 px) — 14 mm = 52,91 px e
   28 mm = 105,83 px arredondados (a faixa do cabeçalho começa em x = 14,023 mm = 53 px; o texto do kit ficava 0,025 mm
   à direita e, com o topo fracionário, 12 de 63 linhas caíam 1 px acima no editor). `.area`: `left: 53px; top: 106px`.
   O rodapé (cujo topo é o fim da área útil para o paginador) passou para `top: 1066px` = 106 + 960: **a altura útil
   continua 960 px (254 mm)**, logo os planos não mudaram (golden de `longo` e `extremos` inalterado).
2. **Linha e texto do rodapé 1 px abaixo** (`margin-top: 1px` nas duas caixas): o kit os desenha em 1067 px.
3. **Imagem do cabeçalho no tamanho intrínseco** (`width: auto`): o kit a põe por `content: url()`, com os 688 × 64 px
   do SVG (182,03 mm), não 182 mm.
4. **Base do cabeçalho 4 px acima** (`padding-bottom: 4px` além dos 5 mm): é o vão abaixo da linha de base da caixa de
   margem do kit, onde a imagem fica apoiada (medido: 1,058 mm = 4 px).
5. **`contain: size` na área das folhas** — correção de composição, não de calibração: ver a seção seguinte.

V3 visual do `curto` p. 1 a cada passo: 2,846 % → 1,614 % (itens 2 e 4) → 0,965 % (3 e `left`) → 0,160 % (`top`/rodapé).

Não reproduzido: a divisão entre as caixas do rodapé (no kit, proporcional à largura dos textos, regra das caixas de
margem; aqui `flex: 1 1 auto`). As duas caixas têm a mesma borda e cor, emendadas: nada visível muda.

## Regra corrigida: conteúdo empurrado para fora da folha na impressão (k > 1)

Achado pela V2/V3 em `longo` (folha 2) e `extremos` (folhas 1 e 6), presente desde a Tarefa 8: com k > 1 a área é
diagramada até 28 + 254k mm (além dos 297 mm da folha) e pintada em 1/k. Ao imprimir, o Chromium fragmentava o conteúdo
da área pelo layout **não escalado** e empurrava o bloco indivisível do pé da folha (ex.: a PROVIDÊNCIA do ITEM 05 da
folha 2 de `longo`) para a página seguinte, onde ele era recortado: **sumia do PDF**, embora estivesse na tela e no
plano. Correção: `section.folha > .area { contain: size; }` (caixa monolítica; tamanho fixo, então nada muda no
layout). Teste novo: `ExportacaoTestes.pdf_com_escala_mostra_as_mesmas_linhas_que_a_tela` (longo e extremos: as linhas
com texto na área de cada folha da tela têm texto na página do PDF). Sem a correção ele falha: `longo: folha 2: 27 de
451 linhas…`, `extremos: folha 1: 59 de 466…; folha 6: 31 de 572…`.

Plano fixado: `PlanoGoldenTestes` (`tests/Pmvni.Web.Testes/golden/plano-{longo,extremos}.json`: inícios, `forcada` e
escala). Nenhuma mudança desta tarefa alterou os planos.

## Decisão sobre a V2 visual (controlador, 14/11/2026)

Critério inicial: ≤ 0,5 % de pixels com |Δ| > 48, sem tolerância. Resultado: reprovado em todos os casos (1,1 % a 6,5 %),
com a composição igual (texto idêntico em todas as folhas; o melhor alinhamento entre tela e PDF é o deslocamento 0).
Causa: a comparação põe frente a frente dois rasterizadores de texto — a captura da tela é do Chromium (DirectWrite,
antisserrilhado em tons de cinza, sem franjas de cor) e a página do PDF é rasterizada pelo MuPDF —, e as bordas dos
glifos diferem em mais de 48 níveis em ~1–6 % dos pixels de uma página de texto denso a 150 dpi. O critério estrito
mede o antisserrilhado dos renderizadores, não a composição.

**Decisão:** V2 visual = métrica com 1 px de tolerância de posição (um pixel conta como diferente só se nenhum pixel da
vizinhança 3 × 3 da outra imagem estiver a |Δ| ≤ 48), limite **≤ 1,0 % por folha** (limite SUPERADO pela 2ª decisão
abaixo: hoje 2,0 %). Depois da revisão, a métrica é
**simétrica** (tela contra PDF e PDF contra tela; vale o maior): a 1ª versão media só a tela contra o PDF e não via tinta
do PDF ausente na tela. A métrica estrita continua medida e
registrada (`visual_estrito_pct` em `v2.json`), só informativa. A identidade do texto por folha continua sendo a porta
principal da V2. Nada de trocar o rasterizador nem instalar nada. O que essa métrica ainda pega: qualquer coisa fora do
lugar por mais de 1 px, conteúdo a mais ou a menos, cor ou imagem diferente.

**Diferenças conhecidas do próprio kit, FIXADAS** (decisão do controlador): o kit corta a etiqueta `INFORMAÇÕES
DIVERGENTES` que passa da borda direita da área (`fora_da_area`) e deixa ~1 px do acento do 1º glifo da página seguinte
na margem de baixo (`sangria_rodape`). Elas **não são excluídas da conferência**: os caracteres além da borda e os da
sangria ficam fora da comparação exata do texto da área, mas são conferidos à parte contra o que foi fixado (só a
etiqueta conhecida, a parte do kit contida na do editor; sangria ≤ 1 no kit e 0 no editor). Qualquer outra diferença,
ou uma dessas além do fixado, reprova a página.

## 2ª decisão sobre a V2 visual (controlador, 14/11/2026): limite 2,0 %

Com a métrica simétrica, `extremos` folha 3 (a página de texto corrido mais densa) ficou em 1,423 %, acima do limite de
1,0 %. O resíduo é ruído de glifo entre os renderizadores (MuPDF × Chromium), concentrado na página de texto mais densa;
a identidade da composição já é provada pela conferência de texto por folha. **Decisão: limite da V2 visual = 2,0 % por
folha na métrica simétrica com 1 px de tolerância** (o mesmo teto da V3, que também compara renderizadores/versões
diferentes).

Papel de cada parte da V2: **a identidade do texto por folha é a porta da composição**; a V2 visual é uma **guarda
grosseira para o que não é texto** (imagens, caixas, linhas, posições). **Custo da decisão:** um desvio de conteúdo não
textual menor que 2 % da página (ex.: uma imagem pequena ou uma caixa fora do lugar) pode passar sem ser notado.

Valores por caso (máximo por folha; simétrica com 1 px de tolerância / estrita, informativa):

| caso | simétrica, 1 px (critério ≤ 2,0 %) | estrita (informativa) |
|---|---|---|
| curto | 0,459 % | 1,792 % |
| longo | 0,570 % | 4,069 % |
| extremos | **1,423 %** (folha 3; folhas: 0,297 / 0,115 / 1,423 / 0,553 / 0,410 / 0,406 / 0,364 / 0,099) | 6,463 % |
| sem_opcionais | 0,389 % | 1,132 % |
| dados_modelo | 0,404 % | 2,629 % |
| caso real (local) | 0,544 % | 3,712 % |
| estouro | — (exportação recusada) | — |

## Histórico (superado)

Antes da 2ª decisão, com a métrica simétrica e limite 1,0 %, `tudo` saía com 1 (V2 6/7: `extremos` folha 3 com 1,423 %;
no sentido tela → PDF eram 0,805 %, logo o excesso vinha de tinta do PDF sem par a 1 px na captura). A tabela de uma
rodada mais antiga, com a métrica de um sentido só (máximo 0,805 %, critério ≤ 1,0 %), foi retirada: não vale mais.

## Medida local por ladrilhos (pedida pelo controlador) — sem limite: nenhum separa ruído de perda

Pedido: o critério de 2 % por página pode esconder uma perda localizada que não é texto (uma pílula, um ícone, um
trecho de borda). Guarda proposta: por página, ladrilhos de 32 × 32 px a 150 dpi sobre a diferença tolerante dos dois
sentidos (pixel diferente em qualquer das duas máscaras), reprovando se algum ladrilho passar de um limite calibrado no
corpus. Implementada em `ladrilho_max` (`tools/fidelidade.py`) e medida em todas as folhas (`ladrilho_pct` em
`v2.json`); autoteste `autoteste_ladrilhos`: apaga (branco) 40 × 20 px do raster do PDF sobre uma pílula e sobre uma
borda colorida e mede de novo.

Calibração (14/11/2026; maior ladrilho por caso, sem perda nenhuma = ruído de rasterização Chromium × MuPDF):
curto 10,35 %; longo 14,16 %; extremos 10,45 %; sem_opcionais 9,47 %; dados_modelo 14,65 %; caso real 19,04 %.
Perdas sintéticas (corpus, todas as pílulas e barras escuras de todas as páginas, 40 × 20 px apagados): pílula de
8,2 % a ~16 % (mínimo 8,2 %, 47 pílulas); borda a partir de 8,2 %. No autoteste (`curto` folha 1, ruído 9,47 %): pílula 15,72 %, barra
escura da borda 17,19 % (a página inteira fica em 0,462–0,465 %: o critério de 2 % não vê nada) — pega no `curto`, mas
abaixo do ruído do `longo`, do `dados_modelo` e do caso real.

**Resultado: nenhum limite separa o ruído (até 19,04 %) de uma pílula apagada (a partir de 8,2 %).** Conforme a
instrução, o item parou aqui: `MAX_LADRILHO = None`, a medida e o autoteste ficam gravados só como informação e não
reprovam. O ruído vem das bordas dos glifos em texto denso (1 px de tolerância não absorve tudo); a pílula perde só os
glifos (o fundo #D3EFDD fica a menos de 48 níveis do branco).

Variantes medidas no mesmo corpus (5 casos fictícios; ruído máximo × menor perda), para a decisão do controlador:

| variante (ladrilho de 32 px) | ruído máx | pílula mín | borda mín |
|---|---|---|---|
| união das máscaras, passo 32 (a pedida) | 14,65 % | 8,20 % | 8,20 % |
| maior das duas máscaras, passo 32 | 13,18 % | 6,15 % | 4,69 % |
| união, janelas sobrepostas (passo 8) | 18,07 % | 15,14 % | 10,35 % |
| maior das duas, passo 8 | 13,18 % | 15,14 % | 7,32 % |
| só pixels escuros (< 128) sem par, passo 8 | 9,08 % | 10,06 % | 2,64 % |
| balanço de tinta (\|Σ escuridão tela − Σ PDF\| na janela), passo 8 | 5,02 % | 12,72 % | 2,57 % |

O balanço de tinta separa as pílulas (5,0 % × 12,7 %), mas não as bordas finas; nenhuma variante separa as duas, e o
O caso real (ruído maior) não entrou nesta tabela. Nenhuma foi adotada sem decisão.

## Resultados (`tudo`, 14/11/2026, rodada final da Etapa 1; WebView2 do editor 157.0.4341.46; Edge do kit 158.0.4370.21)

`python -I tools/fidelidade.py tudo` sai com **0**: V1 7/7, V2 7/7, V3 7/7, `perfil_webview2` OK (marca em 0 de 142
arquivos; processo/requerente dos 7 casos em 0 arquivos da pasta de dados da rodada); `autoteste_ladrilhos`
informativo. Temporários em `%USERPROFILE%\.pmvni-fid\<run>\`, apagados; bin/obj do Verificador apagados no fim.

| caso | folhas | V1 | V2 texto | V2 visual máx, simétrica 1 px (≤ 2,0 %) | ladrilho máx (informativo) | V2 estrito (informativo) | V3 | V3 visual máx | diferenças fixadas (V3) |
|---|---|---|---|---|---|---|---|---|---|
| curto | 2 | igual | igual | 0,459 % | 10,35 % | 1,792 % | OK | 0,160 % | — |
| longo | 21 | igual | igual | 0,570 % | 14,16 % | 4,069 % | OK | 1,663 % | p. 1 sangria 1; p. 14 fora 3 |
| extremos | 8 | igual | igual | **1,423 %** (folha 3) | 10,45 % | 6,463 % | OK | 1,349 % | p. 4/5/6/7 fora 3/12/9/6 |
| sem_opcionais | 1 | igual | igual | 0,389 % | 9,47 % | 1,132 % | OK | 0,167 % | — |
| estouro | recusada | igual | recusa OK | — | — | — | recusa OK | — | — |
| dados_modelo (--instrucoes) | 7 | igual | igual | 0,404 % | 14,65 % | 2,629 % | OK | 1,086 % | p. 2 sangria 1; p. 4 fora 3, sangria 1 |
| caso real (local) | 11 | igual | igual | 0,544 % | 19,04 % | 3,712 % | OK | 1,620 % | p. 5 fora 3; p. 10 sangria 1 |

V2: texto idêntico em todas as folhas e, na métrica simétrica com 1 px de tolerância, **no máximo 1,423 %**
(`extremos`, folha 3, texto corrido), dentro do limite de 2,0 %. O estrito fica em 1,1–6,5 % pela diferença entre os
rasterizadores (ver as decisões acima).
