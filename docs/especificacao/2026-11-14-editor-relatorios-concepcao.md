# Editor de Relatórios Técnicos — PMVNI
## Estudo de concepção para aprovação (14/11/2026)

> **Situação deste documento:** PROPOSTA. Nada aqui foi implementado nem testado.
> Legenda usada no texto: **[VERIFICADO]** = conferido nos arquivos do kit em 14/11/2026, somente leitura;
> **[PROPOSTO]** = recomendação deste estudo; **[DECISÃO SUA]** = depende do usuário;
> **[PREMISSA]** = suposição que ainda precisa ser confirmada.

Projeto separado do kit: `OneDrive\Aplicativos IA\Editor de Relatorios PMVNI`. O kit
(`PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)`) não foi alterado. A integração ao kit
("merge") fica para o fim, como etapa explícita (seção 13).

---

## 1. Compreensão do produto e do fluxo

**O que é:** um editor e ambiente de revisão humana entre a análise feita pela IA e a emissão dos
documentos finais. Não analisa projetos, não decide classificações e não escolhe o tipo de despacho.

**Fluxo atual [VERIFICADO]:**
plugin `analise`/`analise-pro` → `dados.json` (formato do kit) + figuras já marcadas em PNG
(`prepare_visual_evidence.py`, política visual 4) → `gerar_relatorio.py` preenche
`modelo_relatorio.html` + `relatorio.css` → Edge headless imprime o PDF → recibo
`DADOS.recibo.json` → `validate_template_fidelity.py` aceita o PDF só se o recibo conferir com o
registro `TEMPLATES_OFICIAIS.json`.

**Fluxo proposto [PROPOSTO]:**
IA → **pacote editável `.pmvrel`** (ou, na transição, o `dados.json` atual + figuras) → o editor
abre → revisão humana sobre o A4 contínuo → o editor gera PDF do relatório e, quando eu escolher,
o despacho DOCX/PDF.

**Usuário:** o arquiteto analista, em um único PC Windows, trabalhando offline com dados pessoais
de processos.

---

## 2. O que foi encontrado no kit (estudo somente leitura)

| Item | Achado | Consequência para o editor |
|---|---|---|
| Registro de templates | `TEMPLATES_OFICIAIS.json` **revisão 10, 13/11/2026** [VERIFICADO]. Papéis: `report` (HTML), `correction` (DOCX), `approval_simple` (DOCX). Hashes dos 7 arquivos do modelo HTML **conferem** com os arquivos atuais. | O editor lê o registro e os hashes; nunca escreve no kit. |
| Modelos de despacho | Existem: **correção** e **aprovação simples** (pasta `DESPACHO`); **aprovação completa** só como exemplo empacotado no plugin `despacho`. **Não existem modelos de indeferimento nem de circular.** [VERIFICADO] | Indeferimento e circular ficam desabilitados ("modelo ausente") até você fornecer os modelos. |
| DOCX | Não há marcadores de campo; os campos são rótulos seguidos de espaço ("Protocolo nº:", "Requerente:", "Assunto: …", "Venda Nova do Imigrante/ES, dia de mês de ano"). Correção tem cabeçalho de grupo ("GERAL E DOCUMENTAÇÃO"). Assinatura e identificação funcional já vêm fixas no modelo. [VERIFICADO] | O preenchimento será por âncoras de texto, preservando runs e estilos; validado pelo comparador DOCX que o kit já tem. |
| Template HTML | Mini-linguagem própria (`{{ }}`, `{% for/if %}`, filtros `pill`, `cor`, `icone`, `n2`), renderizada pelo Python do kit. CSS com `@page { size: A4; margin: 28mm 14mm 15mm 14mm }`, Verdana 8,1 pt, regras de quebra (`break-inside: avoid`, `box-decoration-break: clone` nos cartões, `thead` repetido nas tabelas, seção de localização indivisível). [VERIFICADO] | Área útil do conteúdo: **182 × 254 mm**. O cabeçalho é um SVG de 688 × 64 px (= 182 mm de largura) em `@top-center`; rodapé em `@bottom-left`/`@bottom-right` com "PÁGINA X DE Y". |
| Cabeçalho/rodapé | Feitos com **caixas de margem do CSS de impressão** (`@page @top-center…`). [VERIFICADO] | Essas caixas **só existem na impressão**; na tela o Chromium não as desenha. É o principal problema técnico (seção 5). |
| Situações | Itens: `ATENDE`, `NAO_ATENDE`, `NAO_COMPROVADO`, `DIVERGENCIA_INTERNA` (impressos ATENDE, NÃO ATENDE, FALTA COMPROVAR, INFORMAÇÕES DIVERGENTES). Índices/ambientes: `CONTA_CONFERE`, `NAO_ATENDE`, `PENDENTE`, `DIVERGENCIA`. [VERIFICADO] | **NÃO SE APLICA não existe no template.** Ver decisão D1. |
| Itens | Não têm id no modelo em branco; "nos plugins, cada item leva também a chave `id` (ID interno do manifesto), que não aparece no PDF". Numeração ITEM 01… e Figura 1… pela ordem do JSON. [VERIFICADO] | O id interno do manifesto será reaproveitado como identificador permanente quando existir. |
| Índices | Campos **textuais** prontos (`calculo`, `projeto`, `limite`, `texto`, `projeto_destaque`). [VERIFICADO] | O editor gera esses textos a partir de campos estruturados; o template não muda. |
| Diagrama | SVG **esquemático, sem escala**, com geometria fixa; só os **rótulos** vêm dos dados; lote tracejado quando `limites_confirmados = false`. [VERIFICADO] | Bom ponto de partida: os rótulos podem ser vinculados aos cálculos sem inventar geometria. |
| Figuras | PNG com marcações **gravadas na imagem** pelo `prepare_visual_evidence.py`. As marcações têm coordenadas **normalizadas 0–1** da imagem (`target`, `target_box`), no máximo 6 por figura, números nas margens laterais, seta + contorno hachurado vermelho para pendências. [VERIFICADO] | O editor adota a mesma convenção normalizada e porta o algoritmo de posicionamento para SVG. Figuras recebidas só como PNG marcado não serão tratadas como editáveis (seção 9). |
| Recibo/validador | O validador exige que o PDF tenha saído do gerador oficial (hash). [VERIFICADO] | PDF gerado pelo editor **não passa** no validador atual até a integração (decisão D2). |
| Ambiente do PC | .NET SDK 10.0.403/404; **WebView2 Runtime 157.0.4341.46**; **Edge 158.0.4370.21 (beta)**, que é o que o gerador do kit usa. [VERIFICADO] | Editor (WebView2 157) e gerador do kit (Edge beta 158) usam builds diferentes do Chromium: pode haver diferenças de quebra. A comparação precisa registrar versões. |
| Projeto Gmail | WinUI 3, .NET 10, Windows App SDK 1.8, sem empacotamento MSIX, autocontido; usa WebView2 com `bridge.js`; estilos de trilho no padrão NavigationView (indicador 3 × 16, cantos 1,5), texto secundário com `TextFillColorSecondaryBrush`. [VERIFICADO, leitura superficial] | Reaproveitar a mesma base técnica e os estilos do trilho e do texto secundário. |

**Não verificado:** as sete imagens de referência da conversa original **não estão disponíveis**
e não foram examinadas. O exemplo real (processo 17098/2026) existe só na pasta do processo e
**não foi aberto**, porque contém dados pessoais (ver decisão D4). O PDF de instruções e o modelo
Word de apoio não foram abertos. Os outros validadores do kit (`validate_analysis.py` etc.) foram
apenas listados.

---

## 3. Interface centrada no A4 contínuo [PROPOSTO]

```
┌──────────────────────────────────────────────────────────────────────────────────────┐
│ ◧  Proc. 1234/2026 · Requerente FICTÍCIO   ● Salvo 14:32   ↶ ↷   [Gerar despacho ▾] [Exportar PDF] │
├────────┬─────────────────────────────────────────────────────────────┬───────────────┤
│ SEÇÕES │░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░│ PROPRIEDADES ✕│
│ Capa   │░░░┌───────────────────────────────────────────┐░░░░░░░░░░░░░░│ ITEM 03       │
│ Local. │░░░│ [cabeçalho institucional]                 │░░░░░░░░░░░░░░│ (id 01J…7Q)   │
│ Análise│░░░│ ITEM 02 — CIRCULAÇÕES …        [ATENDE]   │░░░░░░░░░░░░░░│ ☑ No PDF      │
│  01 ✓  │░░░│ ┌ ITEM 03 — … ───────────── [FALTA COMP.]┐│◂ seleção     │ Situação ▾    │
│  02 ✓  │░░░│ │ VERIFICAÇÃO TÉCNICA …                  ││░░░░░░░░░░░░░░│ Justificativa │
│  03 !  │░░░│ └────────────────────────────────────────┘│░░░░░░░░░░░░░░│ Original ⇄    │
│ Índices│░░░│ [rodapé]          PÁGINA 2 DE 9           │░░░░░░░░░░░░░░│ Histórico     │
│ Diagr. │░░░└───────────────────────────────────────────┘░░░░░░░░░░░░░░│               │
│ Figuras│░░░┌───────────────────────────────────────────┐ ◦ excl. 07    │               │
│ Fim    │░░░│ [cabeçalho]  … página 3 …                 │░░░░░░░░░░░░░░│               │
│────────│                                                               │               │
│Excluí- │  − 100% +  Ajustar largura · Página inteira   pág. 2/9        │               │
│dos (2) │                                                               │               │
└────────┴─────────────────────────────────────────────────────────────┴───────────────┘
```

- **Centro (predominante):** páginas A4 empilhadas verticalmente, fundo cinza neutro, sombra leve,
  espaçamento entre folhas; zoom 50–300%, "ajustar largura", "página inteira". O zoom é só escala
  de visualização (CSS `zoom`/transform do contêiner), nunca muda a composição.
- **Esquerda (recolhível, ~220 px):** árvore de seções e itens com o número **visível atual**,
  ícone da situação e marcador "revisado"/"alterado". Clique rola até o elemento na página.
  Grupo "Excluídos (n)" no fim da árvore.
- **Direita (recolhível, ~320 px):** propriedades do elemento selecionado (item, linha de índice,
  diagrama, figura, marcação, assinatura). Ferramentas focadas (editor de marcações, recorte de
  prancha) abrem em **janela/painel sobreposto**; ao fechar, voltam para o mesmo ponto do A4.
- **Barra superior:** protocolo/processo, estado de gravação (Salvo / Salvando / Alterações não
  salvas / Conflito), desfazer/refazer, Gerar despacho, Exportar. Configurações no menu ⋯.
- **Fluent/WinUI 3:** Mica na janela, `TitleBar` personalizada, `NavigationView`-like à esquerda
  (estilo do trilho reaproveitado do projeto Gmail), `CommandBar`, `InfoBar` para avisos,
  `TeachingTip`/`Flyout` contextuais. Tema claro/escuro só afeta o "chrome"; as páginas são sempre
  brancas e com as cores do `relatorio.css`.
- **Sem** dashboard, cartões soltos, grades inferiores fixas ou formulários no centro.

**Itens excluídos:** nunca entram no fluxo do A4 (para não mudar a paginação). Aparecem:
(a) no grupo "Excluídos" da navegação, com "Restaurar"; (b) como um pequeno marcador "◦ excl. 07"
**na margem cinza fora da folha**, na altura onde estavam, visível só com "Mostrar marcas de
revisão" ligado. Clicar abre o conteúdo num flyout com "Restaurar". A exclusão é reversível;
o item nunca é apagado do arquivo.

---

## 4. Arquitetura e responsabilidades [PROPOSTO]

Base confirmada como viável: **WinUI 3 (Windows App SDK 1.8) + .NET 10 + WebView2**, sem MSIX
(mesmo padrão do Gmail). Electron/web foram descartados conforme a direção já escolhida; não há
motivo técnico para trocá-los.

| Módulo (projeto .NET) | Responsabilidade | Depende de |
|---|---|---|
| `Pmvni.Dominio` | Entidades (Processo, Item, Classificação, Revisão, Índice, Campo vinculado, Figura, Marcação, Recorte, Despacho), regras de numeração visível e referências cruzadas, comandos de edição (desfazer/refazer). Sem UI, sem IO. | — |
| `Pmvni.Calculo` | Motor determinístico de fórmulas versionadas, `decimal`, unidades, arredondamento, divergências e sugestões. Offline, sem IA. | Domínio |
| `Pmvni.Pacote` | Leitura/gravação do `.pmvrel`, validação por JSON Schema, migração de versão de esquema, gravação atômica, diário de recuperação, trava de instância, importação do `dados.json` do kit. | Domínio |
| `Pmvni.Modelos` | Registro local de templates (versões, hashes, histórico, ativação, restauração), leitura somente leitura do kit. | Pacote |
| `Pmvni.Composicao` | Port do renderizador do kit (mini-linguagem, `pill`, `icone`, `cabecalho`, `planta_svg`, `volumetria_svg`, `resumo`) gerando **o mesmo HTML** do `gerar_relatorio.py`, mais atributos `data-ref` para seleção. | Domínio, Modelos |
| `paginador` (TypeScript, roda no WebView2) | Fragmentação do fluxo em folhas A4, cabeçalho/rodapé, contagem de páginas, relatório de estouro, hit-testing. | — |
| `Pmvni.Exportacao` | PDF do relatório (impressão do mesmo DOM paginado), DOCX dos despachos (Open XML SDK), PDF dos despachos (Word), recibo de exportação. | Composição, Modelos |
| `Pmvni.Pranchas` | Abertura de PDFs de projeto, renderização de páginas e recortes, transformações de coordenadas. | — |
| `Pmvni.App` (WinUI) | Janela, navegação, painéis, ferramentas focadas, ponte tipada com o WebView2, configurações. | todos |
| `Pmvni.Testes` | Testes unitários, de paridade com o kit, de paginação e de equivalência tela × PDF. | todos |

**Ponte WinUI ↔ WebView2:** mensagens JSON `{ "v": 1, "tipo": "...", "id": "...", "dados": {...} }`,
com esquema por tipo validado **nos dois lados**; mensagens desconhecidas são descartadas e
registradas. Exemplos: host→web `carregarDocumento`, `substituirBloco`, `selecionar`, `zoom`;
web→host `selecao`, `edicaoTexto`, `paginacaoConcluida {paginas, estouros, versao}`,
`erroComposicao`. O WebView nunca altera o modelo; ele propõe edições e o host aplica comandos.

**Isolamento do conteúdo [PROPOSTO]:** host virtual `https://doc.pmvni.invalid/` mapeado para uma
pasta temporária só com os assets do documento; `WebResourceRequested` bloqueia qualquer outra
origem; CSP `default-src 'none'; img-src data: https://doc.pmvni.invalid; style-src 'unsafe-inline';
script-src` apenas o paginador do app (por nonce); sem `eval`, sem DevTools em produção, sem
downloads, sem `window.open`, sem acesso a arquivos. Texto vindo do pacote é sempre escapado
(o template já faz isso). SVG recebido passa por lista branca de elementos/atributos (sem
`script`, `foreignObject`, `on*`, `href` externo). Nenhum script do pacote é executado.

---

## 5. Paginação, edição e exportação consistentes [PROPOSTO]

### 5.1 O problema real
O relatório atual depende da **paginação da impressão do Chromium** (caixas de margem e regras de
quebra). A tela do WebView2 não pagina: mostra um rolo contínuo sem cabeçalho/rodapé. Pôr
"aparência de papel" sobre esse rolo daria quebras diferentes do PDF — exatamente o que deve ser
evitado.

### 5.2 Alternativas consideradas

| | Como funciona | Fidelidade tela × PDF | Edição sobre o A4 | Veredito |
|---|---|---|---|---|
| **A. Paginador próprio, DOM paginado é a fonte única** | O HTML do template é fragmentado em `<section class="folha">` de 210 × 297 mm, cada uma com cabeçalho, área útil 182 × 254 mm e rodapé. O PDF é a **impressão desse mesmo DOM** com `@page { size: A4; margin: 0 }` e uma folha por página. | Igual por construção (mesmo motor, mesmo DOM, mesmas fontes); conferido por teste de pixels. | Direta: clicar, selecionar, editar texto no próprio A4. | **Recomendado** |
| B. PDF nativo do Chromium como verdade, exibido como imagem | Cada alteração gera o PDF (como o kit) e a tela mostra as páginas renderizadas, com camada de cliques por cima. | Igual ao gerador do kit. | Indireta (sobre imagem), lenta (0,5–2 s por alteração), mapeamento de cliques frágil. | Fica como **verificação**, não como editor. |
| C. Biblioteca Paged.js | Polyfill de paged media. | Boa, mas com falhas conhecidas em grid/flex, `box-decoration-break: clone` e tabelas. | Direta. | Usada só como referência de ideias. |

### 5.3 Regras do paginador (A)
O template tem um conjunto fechado de blocos, então o paginador é específico e determinístico:

- **Medidas em unidades documentais** (mm/pt). A folha tem sempre 210 × 297 mm; o zoom é aplicado
  ao contêiner externo.
- **Cabeçalho e rodapé** reproduzem exatamente o conteúdo das caixas de margem do kit: mesmo SVG
  de cabeçalho (688 × 64 px = 182 mm, alinhado à base, 5 mm acima do fim da margem de 28 mm),
  mesmos textos, fonte Verdana 6,2 pt, linha `.6pt #DDE4EB`, "RELATÓRIO TÉCNICO • PÁGINA X DE Y".
  As posições finas serão **medidas no PDF do kit** e fixadas no teste de equivalência.
- **Espera de estabilidade:** compor só após `document.fonts.ready`, `img.decode()` de todas as
  imagens e dois quadros sem mudança de tamanho (`ResizeObserver`). Exportação só com
  `paginacaoConcluida` do mesmo número de versão do modelo.
- **Quebras (espelhando o CSS do kit):** `.sec` fica com o bloco seguinte; `.locblk`, `.fig`,
  `.panel`, `.obs`, `.concl`, `.sign`, `.rf` e `.ins` não se dividem; `.card` pode dividir **entre
  seus filhos** (cabeçalho, verificação, providência, base legal), repetindo borda/arredondamento
  (equivalente ao `clone`); tabelas dividem **entre linhas**, repetindo o `thead`; linha nunca se
  divide; parágrafo muito longo divide **por linha** (medida por `Range.getClientRects`).
- **Maior que uma página:** se um bloco indivisível não cabe numa folha vazia, ele é dividido no
  nível mais fino permitido; se ainda assim não couber (ex.: linha de tabela gigante), a folha
  **mostra o estouro** com faixa vermelha no editor e a exportação é **bloqueada** com aviso
  ("Bloco X excede a página"). Nada é escondido com `overflow: hidden`.
- **Repaginação incremental:** ao editar o bloco N, só se recompõe da folha que contém N em diante;
  meta < 150 ms para um relatório de ~30 páginas [PREMISSA a medir].

### 5.4 Edição no A4
- Clique seleciona pelo `data-ref` do bloco (id interno, não o número).
- Texto livre (verificação, providência, base legal, referência, títulos, notas): edição no
  lugar com `contenteditable="plaintext-only"` restrito ao campo; cada alteração vira comando
  no host (desfazer/refazer), com reaplicação e repaginação.
- Campos estruturados (classificação, inclusão, valores de cálculo) só pelo painel direito.
- Números vinculados aparecem no texto como **"chips" discretos** só no editor (sublinhado
  pontilhado azul, desaparecem na exportação); clicar abre o cálculo. Número digitado à mão num
  parágrafo é só texto: o editor não tenta adivinhar fórmula.

### 5.5 Exportação
1. Congela a versão do modelo; 2. compõe com o mesmo paginador; 3. espera estabilidade;
4. `CoreWebView2.PrintToPdfAsync` com A4, margens 0, fundos ligados, sem cabeçalho/rodapé do
navegador; 5. confere número de páginas do PDF = número de folhas; 6. grava recibo
(hash do pacote, versão do template, versão do WebView2, hash do PDF).

### 5.6 Verificações de equivalência (não basta "é o mesmo HTML")
- **V1 Paridade de HTML:** para um conjunto de `dados.json` de teste, o renderizador C# deve
  produzir **o mesmo HTML, byte a byte**, que o `gerar_relatorio.py` (rodado só nos testes).
- **V2 Tela × PDF do editor:** cada folha capturada na tela (CDP `Page.captureScreenshot`, 150 dpi)
  comparada com a página correspondente do PDF rasterizada no mesmo dpi; tolerância de pixels
  definida na Etapa 1 (antialiasing), zero diferença de texto por página.
- **V3 Editor × gerador oficial do kit:** mesmo `dados.json` → mesmo número de páginas, **mesmo
  texto em cada página** (quebras nos mesmos pontos) e diferença visual dentro da tolerância.
  Diferenças conhecidas (ex.: build 157 × 158 do Chromium) são registradas, não escondidas.
- **V4 Corpus de paginação:** relatórios curtos, longos (60+ itens), item com texto enorme,
  tabela de índices com muitas linhas, figuras altas, sem seções opcionais, com instruções.

---

## 6. Contrato inicial de dados — pacote `.pmvrel` [PROPOSTO]

ZIP com extensão própria (um arquivo só, fácil de baixar/sincronizar):

```
manifesto.json            esquema, versões, hashes, gerador (IA/app), template usado
projeto.json              estado revisado (fonte da verdade para edição)
original/recebido.json    o que a IA entregou, imutável (para "ver original")
historico.jsonl           uma linha por alteração (append-only)
assets/<sha256>.png|jpg   imagens e recortes (nome = hash)
assets/<sha256>.svg       SVG saneado
fontes/<sha256>.pdf       PDFs de projeto incorporados (opcional; senão, referência por caminho + hash)
template/                 cópia do template usado (≈ 260 KB) para reabrir igual no futuro
```

- Validação por **JSON Schema** publicado (`schema/pmvrel-1.json`); `versao_esquema` semver;
  leitura de versões anteriores por migradores; versão futura desconhecida abre **só leitura**.
- Limites de segurança: caminhos normalizados (sem `..`, absolutos ou `:`), tamanho máximo por
  arquivo e total, taxa de compressão máxima (zip bomb), tipos permitidos por extensão **e**
  assinatura de bytes; nada executável é aceito.
- Gravação segura: grava `nome.pmvrel.tmp` na mesma pasta → `FlushFileBuffers` → valida reabrindo
  → `ReplaceFile` com backup `nome.pmvrel.bak`.
- Autosave e recuperação: diário de comandos em `%LOCALAPPDATA%\EditorRelatoriosPMVNI\recuperacao\`
  a cada alteração (rápido); gravação do pacote a cada 30 s ociosos e ao sair. Ao abrir após
  queda, oferece recuperar.
- Conflitos: arquivo de trava ao lado (`.~nome.pmvrel.lock` com máquina, usuário, PID, batimento);
  segunda instância abre **só leitura**. Antes de gravar, confere hash/data do arquivo em disco;
  se mudou (OneDrive, outra máquina), não sobrescreve: oferece "salvar como cópia" e comparação.

**Exemplo FICTÍCIO** (nenhum dado real; nomes inventados):

```json
{
  "versao_esquema": "1.0.0",
  "template": { "papel": "report", "revisao_registro": 10, "modelo_versao": "2026-11-13",
                "sha256_css": "c4b06d4b…69" },
  "processo": { "numero": "0000/2099", "protocolo": "01/01/2099",
                "requerente": "FULANO DE EXEMPLO (FICTÍCIO)",
                "autor_projeto": { "nome": "PROFISSIONAL FICTÍCIO", "registro": "CAU A000000-0" },
                "responsavel_analise": { "nome": "ANALISTA FICTÍCIO", "cargo": "ARQUITETO E URBANISTA" },
                "assunto": "SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA" },
  "itens": [
    { "id": "it_01JD3Q7Z8K", "ordem": 1, "incluido": true,
      "situacao": { "original": "NAO_COMPROVADO", "revisada": "ATENDE",
                    "justificativa": "Cota localizada na ARQ 02/03 após nova leitura." },
      "campos": {
        "titulo":      { "original": "Dormitórios e banheiros", "revisado": null },
        "verificacao": { "original": "O dormitório 1 tem 3,00 × 3,20 m …",
                         "revisado": "O dormitório 1 tem {{calc:area_dorm1}} m² …" },
        "providencia": { "original": "Cotar o dormitório 1.", "revisado": "Nenhuma correção nessas medidas." },
        "base_legal":  { "original": "Código de Obras (LC nº 70/1990), art. [conferir]", "revisado": null },
        "referencia":  { "original": "ARQ 02/03, planta baixa. {{ref:fig_01JD3R}}", "revisado": null } } }
  ],
  "calculos": [
    { "id": "area_dorm1", "formula": "area_retangulo@1",
      "entradas": { "largura": { "valor": "3,00", "unidade": "m", "fonte": "ARQ 02/03, cota" },
                    "comprimento": { "valor": "3,20", "unidade": "m", "fonte": "ARQ 02/03, cota" } },
      "informado_no_projeto": { "valor": "9,80", "unidade": "m²", "fonte": "ARQ 02/03, quadro" },
      "arredondamento": { "casas": 2, "modo": "meio_para_cima" },
      "limite": null }
  ],
  "figuras": [ { "id": "fig_01JD3R", "recorte": "rc_01", "marcacoes": [ "mk_01", "mk_02" ] } ],
  "excluidos_ordem": [], "despachos": []
}
```

Referências cruzadas no texto usam tokens com id (`{{ref:it_…}}`, `{{ref:fig_…}}`,
`{{calc:…}}`) e são impressas como "item 12", "Figura 3", "9,60". Se o alvo for excluído, o
editor avisa e a exportação pede confirmação. Ao importar textos antigos com "item 12" literal,
o importador converte só quando a correspondência é inequívoca e marca o que converteu.

**Integração com as IAs:** um documento de contrato (`docs/contrato-ia.md`) + o JSON Schema +
um validador de linha de comando (`pmvrel validar arquivo.pmvrel`). Na transição, o editor também
**importa o `dados.json` atual do kit** (com figuras e, quando houver, o plano de marcações do
`prepare_visual_evidence.py`). A IA na nuvem não abre o app: o arquivo chega por download ou
sincronização; depois, associação da extensão `.pmvrel` e, no futuro, pasta de entrada monitorada.

---

## 7. Integração e preservação dos templates [PROPOSTO]

- O template do relatório **não é redesenhado**: `modelo_relatorio.html` e `relatorio.css` são
  usados como estão, a partir de uma cópia importada do kit com hash conferido no registro.
- **Adaptações necessárias, uma a uma:**
  1. Atributos `data-ref` invisíveis nos blocos (para seleção). Não alteram layout. Verificação: V1
     compara o HTML com esses atributos removidos.
  2. Cabeçalho/rodapé saem das caixas de margem e passam a ser elementos de cada folha, com o
     mesmo conteúdo. Verificação: V3 (posição medida no PDF do kit).
  3. Impressão com `@page margin: 0` sobre folhas já paginadas. Verificação: V2 e V3.
  4. Mini-linguagem e funções Python portadas para C#. Verificação: V1 (byte a byte).
- Registro local de templates: `%LOCALAPPDATA%\EditorRelatoriosPMVNI\modelos\<papel>\<revisão>-<hash>\`,
  com identificação, versão, campos/recursos exigidos, compatibilidade por tipo de documento,
  histórico e restauração. "Atualizar do kit" lê o kit (somente leitura), valida hashes contra o
  `TEMPLATES_OFICIAIS.json`, mostra prévia com um documento de teste e só então ativa.
- Cada projeto grava a versão e guarda uma cópia do template usado; atualizar o template **não muda
  relatórios antigos**. Migrar um projeto exige a ação "Migrar para o modelo X", com prévia lado a
  lado, relatório de campos incompatíveis e registro no histórico.

---

## 8. Cálculos, divergências e diagramas [PROPOSTO]

- **Fórmulas versionadas** (`taxa_ocupacao@1`, `coef_aproveitamento@1`, `taxa_permeabilidade@1`,
  `area_retangulo@1`, `vao_iluminacao@1` etc.), código C# testado, sem IA, offline.
- Cada cálculo separa: **valor informado no projeto**, **entradas**, **fórmula + versão**,
  **resultado**, **unidade e arredondamento**, **limite normativo com fonte** (só se fornecido;
  nunca preenchido pelo app), **fonte de cada dado** e **divergência informado × calculado**.
- `decimal`, vírgula decimal pt-BR na entrada e saída; unidades verificadas (m, m², %); entrada
  ausente → resultado "não calculado" (não zero); divisão por zero → erro explícito; comparação
  com o limite feita **antes** do arredondamento de exibição, com aviso quando o arredondamento
  muda a leitura.
- **Três camadas distintas:** resultado objetivo (a conta) → **sugestão de verificação**
  ("a conta excede o limite", "informado diverge do calculado") → **decisão de revisão** (a
  classificação, sempre sua). Recalcular nunca altera a classificação; se a sugestão contrariar
  a decisão, aparece um indicador âmbar só no editor.
- Propagação: memória de cálculo, colunas da tabela de índices (`calculo`, `projeto`,
  `projeto_destaque`), tokens `{{calc:…}}` nos parágrafos e rótulos do diagrama atualizam juntos.
- **Diagramas:** mantêm o esquema do kit (sem escala); só rótulos e destaques são vinculados.
  Medida não comprovada sai como "não confirmado/não cotado"; o lote continua tracejado sem
  limites confirmados. Diagrama recebido só como imagem é preservado como imagem, marcado
  "sem vínculos"; a conversão para diagrama vinculado só ocorre se houver os dados necessários.

---

## 9. Pranchas, recortes e marcações [PROPOSTO]

- **Inserir região da prancha:** ferramenta dedicada abre o PDF do projeto (renderização local por
  `Windows.Data.Pdf`; PDFium como alternativa se a qualidade não bastar), navega páginas, amplia,
  o usuário arrasta um retângulo; o recorte é renderizado em PNG sem perdas na resolução escolhida
  (padrão 200 dpi [PREMISSA]).
- **Origem registrada:** hash e nome do PDF, página (base 1), rotação da página (`/Rotate`),
  retângulo em pontos PDF **no espaço da página não rotacionada**, dpi, tamanho em pixels. Botão
  "Ir para a origem". O PDF original nunca é alterado.
- **Coordenadas das marcações:** normalizadas 0–1 **em relação ao recorte** (mesma convenção do
  kit). Transformações documentadas: PDF (pontos, origem embaixo) → página rotacionada → recorte →
  imagem exibida → folha. Assim a marcação acompanha ampliação, redimensionamento e exportação.
  Novo recorte da mesma região: as marcações são reprojetadas pela transformação; se ficarem fora
  do recorte, o editor avisa.
- **Camada vetorial SVG** separada da imagem: setas, marcadores numerados, caixas vermelhas
  (contorno + hachura translúcida), textos; cada uma com id estável e vínculo ao item. O número
  visível é recalculado pela ordem das figuras e itens incluídos. Posicionamento automático
  porta a política visual 4 do kit (números nas margens, setas curtas sem cruzamento, até 6); o
  usuário pode mover manualmente.
- **Figuras antigas já marcadas em PNG:** preservadas como estão, rótulo "marcações gravadas na
  imagem". Se vier o plano do `prepare_visual_evidence.py` e a imagem sem marcas, importam como
  vetoriais; sem isso, não se finge editabilidade.
- **Privacidade:** tudo local; nenhum envio externo; nenhuma telemetria.

---

## 10. Despachos DOCX/PDF [PROPOSTO]

- Tipo escolhido por você (Aprovação simples, Aprovação completa, Correção; Indeferimento e
  Circular desabilitados até existirem modelos). O app **não sugere** tipo a partir das
  classificações.
- Campos preenchidos do projeto revisado: protocolo, requerente, assunto, autor do projeto /
  responsável técnico, responsável pela análise, data por extenso, pendências escolhidas por
  você (lista com caixas de seleção, pré-marcadas só como conveniência para NÃO ATENDE, FALTA
  COMPROVAR e INFORMAÇÕES DIVERGENTES, como a regra do plugin `despacho`, sempre editável).
- Revisão em tela antes de emitir (prévia do DOCX renderizado em PDF).
- DOCX: **Open XML SDK**, preenchendo âncoras do modelo sem reaplicar formatação. Validação com a
  mesma comparação de componentes do `validate_template_fidelity.py`.
- PDF: Word instalado, via automação (`ExportAsFixedFormat`) [PREMISSA: Word disponível no PC].
- Assinatura/identificação funcional vem do modelo; o app avisa que isso **não é assinatura digital**.

---

## 11. Plano de desenvolvimento por etapas

| Etapa | Entrega | Prova de pronto |
|---|---|---|
| **1 — Fidelidade documental** | Solução .NET, renderizador C#, paginador, visualização A4 contínua só leitura, exportação PDF, testes V1–V4. | V1 byte a byte; V2 sem diferença de texto e dentro da tolerância; V3 com mesmas quebras no corpus; relatórios curtos e longos. |
| **2 — Revisão básica** | Pacote `.pmvrel`, importação do `dados.json`, edição de itens/classificação/inclusão, original × revisado, excluídos, histórico, autosave, desfazer/refazer, travas. | Testes de pacote (gravação interrompida, conflito, recuperação), excluídos recuperáveis, numeração e referências consistentes. |
| **3 — Cálculos e diagramas** | Motor de cálculo, painel de índices, tokens vinculados, divergências, rótulos do diagrama. | Testes de fórmulas (ausentes, zero, unidades, vírgula, arredondamento), propagação, classificação nunca alterada pelo recálculo. |
| **4 — Pranchas e evidências** | Visualizador de PDF, recorte, camada de marcações, vínculos, renumeração. | Alinhamento em zoom/rotação/exportação (teste de pixel com marcas de referência). |
| **5 — Despachos e templates** | Despachos DOCX/PDF, gestão de versões de templates, migração explícita. | Validador DOCX do kit passando; prévia antes da ativação; restauração de versão. |
| **6 — Robustez e distribuição** | Segurança do pacote, compatibilidade de esquema, instalador, `recriar.json`, limpeza de builds. | Testes de segurança (zip slip, SVG malicioso), instalação limpa, Recriar PC conferido. |
| **7 — Integração ao kit ("merge")** | Plugins passam a gerar `.pmvrel`; gerador oficial passa a usar o mesmo paginador; registro revisão 11; validador aceita o recibo do editor. | Feita só com sua autorização, com backup e testes do kit. |

Cada entrega informará: funcional, testado, pendente.

---

## 12. Critérios de aceite e riscos

**Aceite:** os 11 critérios da seção 18 do pedido, verificados por teste quando possível:
A4 vertical contínuo como área principal; PDF = prévia (V2); nada cortado (estouro bloqueia
exportação); excluídos recuperáveis; original × revisado distinguíveis; informado nunca
substituído; propagação de cálculos; marcações alinhadas e renumeradas; despacho só por escolha
e revisão; nada inventado; template do kit preservado (V1/V3).

**Riscos concretos:**
1. **Quebras diferentes do gerador do kit** (paginador próprio × fragmentação nativa do Chromium).
   Mitigação: V3 desde a Etapa 1; regras espelham o CSS; diferenças documentadas.
2. **Builds diferentes do Chromium** (WebView2 157 × Edge beta 158; atualizações automáticas).
   Mitigação: versão no recibo; suíte de regressão ao detectar runtime novo. Runtime fixo
   (Fixed Version) dá determinismo, mas perde atualizações de segurança — não recomendado.
3. **`contenteditable`** é sensível (cursor, colar, IME). Mitigação: só texto puro por campo.
4. **Desempenho** em relatórios longos com muitas imagens. Mitigação: repaginação incremental,
   imagens decodificadas uma vez, medição na Etapa 1.
5. **OneDrive** sincronizando o pacote durante a gravação. Mitigação: gravação atômica, trava,
   checagem de hash, diário local.
6. **Word ausente ou bloqueado** para gerar PDF do despacho. Mitigação: detectar e avisar.
7. **Validador do kit** recusa PDFs do editor até o merge (decisão D2).
8. **Figuras antigas rasterizadas** não terão marcações editáveis.

---

## 13. Decisões que dependem de você

- **D1 — NÃO SE APLICA. DECIDIDO em 14/11/2026: sem etiqueta no PDF.** A classificação existe
  só no arquivo de trabalho e no editor. Item marcado NÃO SE APLICA fica automaticamente fora do
  PDF (como um excluído, recuperável, com a classificação preservada no histórico); se o usuário
  tentar incluí-lo, o editor pede outra classificação antes. O template do kit não muda.
- **D2 — Qual PDF vale como oficial antes do merge. DECIDIDO em 14/11/2026: o PDF do editor.**
  O documento emitido é sempre a impressão das mesmas folhas revisadas na tela, com o recibo do
  editor (hash do pacote, versão do template, versão do WebView2, hash do PDF). Até a integração
  (Etapa 7), esse PDF não passa no `validate_template_fidelity.py` dos plugins; isso é esperado e
  não deve ser contornado. O gerador do kit continua sendo usado apenas nos testes de fidelidade
  (V3), não como caminho de emissão.
- **D3 — Modelos de indeferimento e circular.** Precisam ser fornecidos por você; até lá, ficam
  desabilitados.
- **D4 — Corpus real de teste. DECIDIDO em 14/11/2026: sim, só local.** O `dados_17098.json` pode
  ser usado nos testes de fidelidade (V1–V4) **lido diretamente da pasta do processo**, sem cópia
  para este projeto, para exemplos, documentação ou repositório. Saídas geradas a partir dele
  (HTML, PDFs, capturas, relatórios de diferença) ficam em pasta temporária e são apagadas ao fim
  de cada execução; os relatórios de teste guardados registram só contagens, hashes e números de
  página, nunca texto do processo.
- **D5 — Imagens da conversa original.** Se quiser que eu as considere, envie-as; hoje não as vi.

- **D6 — Distribuição. DECIDIDO em 14/11/2026 (requisito do usuário):** o app é **um executável
  único, com tudo embutido**, e **não pede permissões desnecessárias**. Consequências:
  - publicação .NET autocontida em arquivo único (`PublishSingleFile` + `IncludeAllContentForSelfExtract`),
    com Windows App SDK autocontido; os recursos do WinUI (`.pri`, `.xbf`) e as DLLs nativas vão
    dentro do .exe e são extraídos pelo próprio .NET para a pasta temporária do usuário;
  - paginador, folhas de estilo do editor e uma **cópia do modelo oficial (revisão 10)** vão
    embutidos como recursos; o kit só é consultado em "Atualizar modelos" (somente leitura);
  - manifesto `asInvoker`: sem administrador, sem serviço, sem tarefa agendada, sem regra de
    firewall, sem gravação em `Program Files` ou `HKLM`; dados só em `%LOCALAPPDATA%\EditorRelatoriosPMVNI`
    e onde o usuário salvar; associação de `.pmvrel` (Etapa 6) só em `HKCU` e opcional;
  - sem acesso à rede: o WebView2 bloqueia toda requisição externa; o app não tem atualizador online;
  - **dependências do sistema que não dá para embutir:** o WebView2 Runtime (já faz parte do
    Windows 11; o app verifica e explica se faltar) e, na Etapa 5, o Word para converter despacho
    em PDF (alternativa a avaliar lá);
  - **risco:** o WinUI 3 em arquivo único não está comprovado neste PC (o Gmail é publicado em
    pasta). A Tarefa 1 do plano da Etapa 1 prova isso primeiro; se falhar, a decisão volta para você.

**Decisões que tomei (pode corrigir):** pasta do projeto em `Aplicativos IA`; .NET 10 + Windows
App SDK 1.8 sem MSIX (igual ao Gmail); extensão `.pmvrel`; PDFs de projeto referenciados por
caminho + hash, com opção de incorporar; recortes a 200 dpi; `Windows.Data.Pdf` primeiro.
