# Fidelidade documental — critérios e medidas (Etapa 1, Tarefa 9)

Suíte: `python -I tools/fidelidade.py {v1|v2|v3|v4|tudo} [--caso NOME ...]`. Casos: `tests/corpus/*.json`,
`dados_modelo.json` do modelo oficial (com `--instrucoes`) e os casos locais de `tests/local.json` (ignorado pelo git;
hoje o 17098). O gerador do kit roda como na V1: `gerar_relatorio.py --rascunho --manter-html` no modo padrão (Edge
aberto por `.cmd` via `explorer.exe`), com tudo em `tests/saida/tmp/<run>/`, apagado no fim. `tests/saida/*.json`
levam só contagens, hashes, páginas e porcentagens.

Comandos do Verificador usados pela suíte (todos aceitam `--instrucoes` e `--rascunho`, as opções do kit):
`html`, `plano`, `pdf` (pelo `Exportador`; estouro → recusa, código 3, nada gravado), `texto` e `capturas --dpi N`
(a "tela": plano composto num host oculto e aplicado, sem compor, num documento recém-carregado de outro host, mídia de
tela, escala 1).

## Critérios

| | Critério (do brief; nenhum foi afrouxado) |
|---|---|
| Pixel diferente | \|Δ\| > 48 em tons de cinza (0–255), imagens a 150 dpi |
| V1 | HTML do editor = HTML do kit, byte a byte |
| V2 (tela × PDF do editor) | cada folha capturada a 150 dpi × página do PDF rasterizada pelo PyMuPDF a 150 dpi: ≤ 0,5 % de pixels diferentes; texto por folha idêntico |
| V3 (PDF do editor × PDF do kit) | mesmo número de páginas; mesmo texto em cada página (cabeçalho, área útil, rodapé); ≤ 2 % de pixels diferentes por página |
| V4 | V1–V3 em todos os casos; `estouro` só verifica a recusa da exportação |

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
  contagens, e (2) cada trecho (span) do PDF aparece inteiro no texto da folha.
- **V3, diferenças conhecidas contadas à parte** (impressas por página e gravadas em `v3.json`, nunca escondidas):
  - `fora_da_area`: caracteres visíveis com o centro além da borda direita da área (x > 196,1 mm). O kit corta tudo o
    que passa da área (recorte da página do Chromium); o editor não corta nada (especificação: "nada cortado"). Caso
    real: a etiqueta `INFORMAÇÕES DIVERGENTES` (`nowrap`) numa coluna da direita da tabela de índices: no kit sai
    "INFORMAÇÕES DIVERGEN", no editor inteira (até 202,3 mm). **O PDF do kit perde texto aqui.**
  - `sangria_rodape`: glifo de fonte > 7 pt na faixa do rodapé (o rodapé usa 6,2 pt). O Chromium do kit deixa na
    margem de baixo um fragmento de ~1 px do acento do 1º glifo da página seguinte ("Á" de um título); o editor não.

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

## Resultados (`tudo`, 14/11/2026; WebView2 do editor 157.0.4341.46; Edge do kit 158.0.4370.21)

| caso | folhas | páginas do kit | V1 | V2 texto | V2 visual máx (tolerância 1 px, informativo) | V3 | V3 visual máx | diferenças conhecidas (V3) |
|---|---|---|---|---|---|---|---|---|
| curto | 2 | 2 | igual | igual | **1,792 %** (0,123 %) | OK | 0,160 % | — |
| longo | 21 | 21 | igual | igual | **4,069 %** (0,404 %) | OK | 1,663 % | p. 1 sangria 1; p. 14 fora 3 |
| extremos | 8 | 8 | igual | igual | **6,463 %** (0,805 %) | OK | 1,349 % | p. 4/5/6/7 fora 3/12/9/6 |
| sem_opcionais | 1 | 1 | igual | igual | **1,132 %** (0,116 %) | OK | 0,167 % | — |
| estouro | recusada | 7 | igual | recusa OK | — | recusa OK | — | — |
| dados_modelo (--instrucoes) | 7 | 7 | igual | igual | **2,629 %** (0,326 %) | OK | 1,086 % | p. 2 sangria 1; p. 4 fora 3, sangria 1 |
| 17098 (local) | 11 | 11 | igual | igual | **3,712 %** (0,315 %) | OK | 1,620 % | p. 5 fora 3; p. 10 sangria 1 |

**V1 e V3 aprovados em todos os casos** (mesmas páginas e mesmo texto por página: as mesmas quebras do kit).
**V2: texto idêntico em todas as folhas; o critério visual (≤ 0,5 %) NÃO é atingido em nenhum caso.** As folhas da tela e
as páginas do PDF têm a mesma composição (sobreposição exata: o melhor alinhamento é o deslocamento 0; texto igual), mas
a tela é rasterizada pelo Chromium (DirectWrite, tons de cinza) e o PDF pelo MuPDF: as bordas dos glifos diferem em mais
de 48 níveis em ~1–6 % dos pixels de uma página de texto denso a 150 dpi. Com 1 px de tolerância de posição (métrica só
informativa, `visual_tolerante_1px_max_pct` em `v2.json`) fica em 0,04–0,8 % (a página de texto corrido de `extremos`,
folha 3, é a única acima de 0,5 %). O critério não foi mudado: fica para decisão (ver Pendência).

### Pendência
A V2 visual, como escrita, mede a diferença entre dois rasterizadores de texto, não entre tela e PDF. Opções a decidir
(nenhuma aplicada): comparar a tela com o PDF rasterizado pelo próprio Chromium; ou adotar a métrica com tolerância de
posição com um limite próprio, justificado aqui.
