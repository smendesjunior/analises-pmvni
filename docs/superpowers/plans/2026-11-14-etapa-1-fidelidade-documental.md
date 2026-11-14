# Etapa 1 — Fidelidade documental: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Objetivo:** mostrar o relatório completo do kit em folhas A4 sequenciais (somente leitura) e exportar
um PDF com exatamente as mesmas folhas, provando a fidelidade por testes automáticos.

**Arquitetura:** o modelo oficial (revisão 10) vai embutido no app. Um renderizador C# reproduz, byte a
byte, o HTML do `gerar_relatorio.py`. Um paginador TypeScript roda no WebView2: num host oculto
(escala 1, mídia de impressão) ele **calcula um plano de quebras**; a tela do editor e o PDF
**aplicam esse mesmo plano**, e o PDF é a impressão dessas folhas com `@page { margin: 0 }`.

**Tecnologia:** .NET 10, Windows App SDK 1.8.260921001 (WinUI 3, sem MSIX, autocontido, arquivo
único), WebView2 pela projeção WinRT do Windows App SDK, xUnit, TypeScript + esbuild (Node 26),
ferramentas de teste em Python 3.14 + PyMuPDF (só desenvolvimento).

**Especificação:** `docs/especificacao/2026-11-14-editor-relatorios-concepcao.md` (ler seções 2, 4, 5, 7 e D1–D6).

## Restrições globais

- A4 = **210 × 297 mm**; margens do modelo **28 / 14 / 15 / 14 mm** (topo/direita/baixo/esquerda); área útil **182 × 254 mm**.
- Fonte Verdana (do Windows). Zoom muda só a escala da exibição, nunca o plano de quebras.
- O kit em `C:\Users\smendesjunior\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)` é **somente leitura**. Nenhuma tarefa grava nele.
- Hashes do modelo embutido = registro rev. 10: `modelo_relatorio.html` `2f27fdb6…9eff9a`, `relatorio.css` `c4b06d4b…6b69`, `brasao.png` `93f35890…92cd`, `gerar_relatorio.py` `cec0e5e3…5674`, `dados_modelo.json` `c56df9e6…f816` (valores completos no `TEMPLATES_OFICIAIS.json`).
- **Executável único**, tudo embutido; manifesto `asInvoker`; sem admin, serviço, tarefa agendada, firewall, `HKLM` ou `Program Files`. Dados só em `%LOCALAPPDATA%\EditorRelatoriosPMVNI`.
- Sem rede: o WebView2 bloqueia tudo que não for `https://doc.pmvni.invalid/` ou `data:`.
- Nenhum dado pessoal no repositório. O caminho do `dados_17098.json` fica em `tests/local.json` (no `.gitignore`); saídas dele vão para pasta temporária, apagada ao fim; relatórios guardados têm só contagens, hashes e números de página.
- Programas do usuário (o app, o Edge do kit, os scripts `.cmd`) são abertos via `explorer.exe`, fora do pacote do Claude. Verificações de resultado gravam em pasta do projeto (OneDrive), não em AppData.
- Toda build termina apagando builds anteriores e temporários (regra do CLAUDE.md do usuário), só depois de conferir o arquivo final.

## Foco de revisão (falhas que nenhum teste de tarefa cobre sozinho)

1. **Fontes ou imagens ainda carregando** quando o paginador mede → quebras erradas. Esperado: compor só após `document.fonts.ready`, `img.decode()` e dois quadros estáveis. Teste na Tarefa 7 (`imagem_lenta_nao_altera_plano`).
2. **Escala do monitor (125–200%)** muda a quebra de linha na tela → folha da tela diferente do PDF. Esperado: tela aplica o plano calculado no host oculto e marca qualquer folha que transborde na tela. Teste na Tarefa 8 (`plano_aplicado_com_escala_150_nao_muda_conteudo_das_folhas`).
3. **Texto com `& < > " '`, emoji e quebras de linha** → HTML diferente do kit ou conteúdo injetado. Esperado: escape idêntico ao `html.escape` do Python. Teste na Tarefa 3 e no corpus `extremos`.
4. **Bloco indivisível maior que uma página** (linha de tabela gigante) → sumiço ou corte silencioso. Esperado: folha marcada com estouro e exportação recusada com mensagem. Teste na Tarefa 8 (`exportacao_recusada_com_estouro`).
5. **Zoom e redimensionamento da janela** → repaginação. Esperado: o plano não muda; só a escala. Teste na Tarefa 10 (`zoom_nao_recompoe`).

---

## Estrutura de arquivos

```
EditorRelatoriosPMVNI.sln
Directory.Build.props            versões e propriedades comuns (net10.0-windows10.0.19041.0, x64, Nullable enable)
.gitignore                       bin/ obj/ artifacts/ node_modules/ tests/local.json tests/saida/ Aplicativo/*.pdb
src/Pmvni.Composicao/            net10.0 — sem UI, sem IO além de recursos embutidos
  ModeloOficial/rev10/           cópia do modelo (html, css, brasao.png, dados_modelo.json, exemplo_*.png) — EmbeddedResource
  ModeloOficial/ModeloOficial.cs carregamento + verificação de hash
  Molde/PythonTexto.cs           str() e html.escape do Python
  Molde/Molde.cs                 mini-linguagem do kit (compilar/renderizar)
  Kit/Situacoes.cs  Kit/Icones.cs  Kit/Cabecalho.cs  Kit/Diagrama.cs
  Kit/Localizacao.cs  Kit/Conferencia.cs
  RelatorioRenderer.cs           dados.json → HTML idêntico ao do kit
src/Pmvni.Composicao.Web/        net10.0-windows10.0.19041.0, Windows App SDK (sem WinUI)
  Ponte/Mensagens.cs             envelopes e tipos das mensagens
  Seguranca.cs                   host virtual, filtro de requisições, CSP, ajustes do WebView2
  DocumentoWeb.cs                carrega documento, compõe, aplica plano, captura, imprime
  HostOculto.cs                  thread STA + janela oculta + CoreWebView2Controller
  PlanoQuebras.cs                tipos do plano (espelho dos tipos TS)
  web/paginador.js               gerado de web/paginador (EmbeddedResource)
web/paginador/                   TypeScript: src/{index,medida,fragmentar,folhas,plano,texto}.ts, package.json, build.mjs
src/Pmvni.App/                   WinUI 3: App.xaml, MainWindow.xaml(.cs), Estilos.xaml, app.manifest
src/Pmvni.Verificador/           console: html | plano | pdf | capturas | autoteste
tests/Pmvni.Composicao.Testes/   xUnit (unidade)
tests/Pmvni.Web.Testes/          xUnit (integração com WebView2 oculto)
tests/corpus/                    dados fictícios + imagens geradas (commitados)
tests/referencias/               saídas de referência das funções do kit (commitadas, sem dados pessoais)
tools/gerar_referencias.py       importa o gerador do kit (somente leitura) e grava tests/referencias
tools/gerar_corpus.py            gera tests/corpus de forma determinística
tools/fidelidade.py              V1–V4; relatório em tests/saida/ (ignorado pelo git)
tools/build-release.ps1          publica o .exe único em Aplicativo/ e limpa
recriar.json
```

Tipos compartilhados (usados por várias tarefas):

```csharp
// Pmvni.Composicao
public sealed record ModeloCarregado(string Html, string Css, byte[] Brasao, string Revisao, IReadOnlyDictionary<string,string> Sha256);
public sealed record OpcoesRender(bool Instrucoes = false);
public sealed record ResultadoRender(string Html, IReadOnlyList<RefBloco> Refs, IReadOnlyList<string> Erros);
public sealed record RefBloco(string Seletor, int Indice, string Id, string Rotulo);   // n-ésimo elemento que casa com Seletor ↔ Id
// Pmvni.Composicao.Web (espelho de web/paginador/src/plano.ts)
public sealed record PosicaoFluxo(int[] Caminho, int? DeslocamentoTexto, int? Linha);  // índices de filhos a partir do fluxo
public sealed record PlanoQuebras(int Versao, IReadOnlyList<PosicaoFluxo> InicioDasFolhas, IReadOnlyList<Estouro> Estouros, string Sha256Html);
public sealed record Estouro(int Folha, string? Ref, double AlturaMm);
public sealed record ConfigFolhas(string CabecalhoUri, string RodapeEsquerda, string RodapeDireitaModelo); // "{n}" e "{t}"
```

---

### Tarefa 1: Prova técnica — executável único com WinUI 3 + WebView2 (portão)

Se esta tarefa falhar, **parar e levar a decisão ao usuário** (spec D6). Nada mais começa antes.

**Arquivos:** `git init`; `.gitignore`; `Directory.Build.props`; `EditorRelatoriosPMVNI.sln`;
`src/Pmvni.App/` mínimo; `src/Pmvni.Composicao.Web/HostOculto.cs` mínimo; `src/Pmvni.Verificador/` mínimo;
`tools/build-release.ps1` (primeira versão).

- [ ] **Passo 1:** `Pmvni.App.csproj` com `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `SelfContained=true`, `RuntimeIdentifier=win-x64`, `PublishSingleFile=true`, `IncludeAllContentForSelfExtract=true`, `EnableCompressionInSingleFile=true`, `Microsoft.WindowsAppSDK 1.8.260921001`; `app.manifest` com `requestedExecutionLevel level="asInvoker"`, DPI `PerMonitorV2`. Janela com WebView2 XAML que carrega `<p>ok</p>`. Pasta de dados do WebView2: `%LOCALAPPDATA%\EditorRelatoriosPMVNI\WebView2`.
- [ ] **Passo 2:** argumento `--autoteste <arquivo.json>`: após `NavigationCompleted` com sucesso, grava `{ "webview2": "<versão>", "baseDir": "...", "elevado": false|true }` e fecha.
- [ ] **Passo 3:** `HostOculto` (STA, janela Win32 oculta via `CreateWindowEx`, `CoreWebView2Environment.CreateWithOptionsAsync`, `CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle(hwnd))`). `Pmvni.Verificador autoteste-pdf <saida.pdf>` imprime `<p>ok</p>` via CDP `Page.printToPDF`.
- [ ] **Passo 4:** `tools/build-release.ps1`: `dotnet publish src/Pmvni.App -c Release` → copia o .exe para `Aplicativo/Editor de Relatorios PMVNI.exe`; confere que a pasta de publicação tem **um único arquivo executável** (aceitar `.pdb` só se removido em seguida); apaga `bin`, `obj`, pasta de publicação e temporários do projeto; imprime espaço liberado.
- [ ] **Passo 5:** verificar fora do pacote: gravar `tests/saida/autoteste.cmd` que roda o .exe com `--autoteste "%~dp0autoteste.json"`; abrir com `explorer.exe`; ler o JSON.
  Esperado: `webview2` preenchido, `elevado: false`, e `Aplicativo/` contendo só o `.exe`.
- [ ] **Passo 6:** `dotnet run --project src/Pmvni.Verificador -- autoteste-pdf tests/saida/ok.pdf` → PDF de 1 página com "ok".
- [ ] **Passo 7:** registrar no `docs/notas-tecnicas.md` tamanho do .exe, tempo de primeira abertura (extração) e onde o .NET extraiu os arquivos. Commit `chore: prova técnica do executável único`.

### Tarefa 2: Modelo oficial embutido

**Arquivos:** `src/Pmvni.Composicao/ModeloOficial/rev10/*`, `ModeloOficial.cs`; teste `tests/Pmvni.Composicao.Testes/ModeloOficialTestes.cs`.
**Produz:** `static ModeloCarregado ModeloOficial.Carregar()`; `static IReadOnlyDictionary<string,string> ModeloOficial.HashesDoRegistro` (os 7 hashes da rev. 10 copiados do registro).

- [ ] **Passo 1:** copiar (leitura do kit) os 7 arquivos da pasta `MODELO HTML (USADO PELOS PLUGINS)`, exceto `gerar_relatorio.py`, e registrar seus hashes; incluir o `gerar_relatorio.py` só como referência de hash (não embutido).
- [ ] **Passo 2:** teste `cada_arquivo_embutido_confere_com_o_registro_rev10` (sha256 de cada recurso == valor do registro) e `carregar_devolve_revisao_10`. Rodar: falha.
- [ ] **Passo 3:** implementar `ModeloOficial`. Rodar `dotnet test --filter ModeloOficial`: passa. Commit.

### Tarefa 3: Mini-linguagem do kit

**Arquivos:** `Molde/PythonTexto.cs`, `Molde/Molde.cs`; teste `MoldeTestes.cs`.
**Produz:**
`static string PythonTexto.Str(object? v)` (None→"", True→"True", int→"12", float como `repr` do Python);
`static string PythonTexto.Escape(string s)` (`&`→`&amp;`, `<`→`&lt;`, `>`→`&gt;`, `"`→`&quot;`, `'`→`&#x27;`);
`sealed class Molde { static Molde Compilar(string fonte); string Renderizar(IDictionary<string,object?> ctx, IReadOnlyDictionary<string,Func<object?,object?>> filtros); }`;
`sealed record Seguro(string Valor)` (equivale ao `Seguro` do Python: não é escapado).

Semântica a copiar de `gerar_relatorio.py` (`compilar`, `valor`, `render`): comentários `{# #}`; `{{ a.b.0|f1|f2 }}`; literais entre aspas; `not `; `{% for x in lista %}` com `loop.index`, `loop.n2`, `loop.first`, `loop.last`, `loop.even` (= índice ímpar a partir de 0); `{% if %}…{% else %}…{% endif %}`; veracidade do Python (lista/dict/string vazios e 0 são falsos); marcador desconhecido e bloco sem fechamento lançam erro com a mesma mensagem.

- [ ] **Passo 1:** testes: `escape_igual_ao_python` (`"a&b<c>\"d'"` → `a&amp;b&lt;c&gt;&quot;d&#x27;`), `loop_even_e_n2`, `if_else_com_lista_vazia`, `filtro_safe_nao_escapa`, `caminho_com_indice_numerico`, `marcador_desconhecido_lanca`, `str_de_bool_e_float` (`True`, `0.1`, `1e-05`, `12.0`). Falham.
- [ ] **Passo 2:** implementar. Passam. Commit.

### Tarefa 4: Funções do kit portadas, com referências geradas pelo próprio kit

**Arquivos:** `Kit/Situacoes.cs` (`SIT`, `Pill`, `Cor`, `SIT_ITEM`, `SIT_INDICE`, `SIT_LOCAL`), `Kit/Icones.cs` (`Icone`), `Kit/Cabecalho.cs` (`CabecalhoUri(string orgao, byte[] brasao)`), `Kit/Diagrama.cs` (`PlantaSvg`, `VolumetriaSvg`, `MontarDiagrama`), `Kit/Localizacao.cs` (`Agrupar` → grupos e colunas, legenda padrão), `Kit/Conferencia.cs` (`Conferir(JsonElement d, Func<string,bool> imagemExiste, bool rascunho) → IReadOnlyList<string>`, `Resumo`); `tools/gerar_referencias.py`; `tests/referencias/*.txt|json`; teste `KitTestes.cs`.

- [ ] **Passo 1:** `tools/gerar_referencias.py` importa o módulo do kit por caminho (`importlib.util.spec_from_file_location`, `sys.dont_write_bytecode = True`, nunca escreve no kit) e grava em `tests/referencias/`: `pill_<SIT>.txt` para cada situação, `icone_<k>.txt` para cada ícone, `cabecalho_padrao.txt`, `planta_*.txt` e `volumetria_*.txt` para 4 combinações (limites confirmados/não; marcas 0/todas; `linha2_destaque` sim/não; listas de 1 e 2 elementos), `resumo_*.txt`, `conferir_*.json` (dados válidos, 4 inválidos: situação errada, figura sem imagem, 7 notas, colchetes), `localizacao_colunas_*.json`. Rodar `python -I tools/gerar_referencias.py --kit "<pasta do kit>"`.
- [ ] **Passo 2:** testes em `KitTestes.cs` comparando cada função com o arquivo correspondente **byte a byte** (mensagens de `Conferir` iguais, na mesma ordem). Falham.
- [ ] **Passo 3:** implementar. Atenção: formatação `:.1f` do Python = `ToString("F1", CultureInfo.InvariantCulture)`; base64 padrão; `html.escape` da Tarefa 3. Passam. Commit.

### Tarefa 5: Renderizador do relatório e paridade V1

**Arquivos:** `RelatorioRenderer.cs`; `tools/gerar_corpus.py`; `tests/corpus/{curto,longo,extremos,estouro,sem_opcionais}.json` + `tests/corpus/img/*.png`; `tools/fidelidade.py` (subcomando `v1`); teste `RelatorioRendererTestes.cs`.
**Consome:** Tarefas 2–4. **Produz:** `static ResultadoRender RelatorioRenderer.Renderizar(string caminhoDados, OpcoesRender op, ModeloCarregado m)`.

Contexto igual ao `main()` do kit: `instrucoes`, `css`, `cabecalho_svg`, `resumo`, `localizacao` com grupos/colunas/legenda/`imagem_uri`, `figuras` com `imagem_uri`, `diagrama` montado. Imagens lidas relativas à pasta do JSON. `Refs`: `.card` ↔ `itens[i].id` ou `item-<i+1>` quando faltar; `.fig` ↔ `figuras[i].id` ou `figura-<i+1>`; seções `.sec` ↔ `secao-<n>` com rótulo do `h2`. Os refs **não alteram o HTML** (são aplicados pelo paginador).

Corpus (fictício, determinístico, sem nomes reais): `curto` (3 itens, sem seções opcionais além de fontes/conclusão); `longo` (60 itens, 6 índices, 12 ambientes, diagrama, 8 figuras 1600×1000); `extremos` (item com verificação de 9 000 caracteres, 40 linhas de índice, figura 800×2400, textos com `& < > " '`, emoji e `\n`); `estouro` (um ambiente com 80 vãos); `sem_opcionais`.

- [ ] **Passo 1:** testes `refs_por_ordem_com_id_ausente`, `html_nao_contem_data_ref`, `erros_de_conferencia_impedem_render`. Falham → implementar → passam.
- [ ] **Passo 2:** `fidelidade.py v1`: para cada caso do corpus + `dados_modelo.json` com `--instrucoes` + (se `tests/local.json` existir) o 17098, roda o gerador do kit com `--rascunho --manter-html` (nunca grava recibo) para uma pasta em `%TEMP%`, roda `Pmvni.Verificador html`, compara os HTML **byte a byte**, apaga a pasta, grava em `tests/saida/v1.json` só `{caso, igual, sha256_kit, sha256_editor}`.
  Esperado: todos `igual: true`. Commit.

### Tarefa 6: Núcleo web — segurança, ponte tipada e host oculto

**Arquivos:** `Seguranca.cs`, `Ponte/Mensagens.cs`, `DocumentoWeb.cs`, `HostOculto.cs`; `web/paginador/src/index.ts` (ponte JS); teste `tests/Pmvni.Web.Testes/SegurancaTestes.cs`, `PonteTestes.cs`.
**Produz:**
`static Task Seguranca.AplicarAsync(CoreWebView2 core, string pastaSessao)`;
`sealed class DocumentoWeb(CoreWebView2 core) { Task CarregarAsync(ResultadoRender r, ConfigFolhas cfg); event Action<MensagemWeb> Recebida; Task<JsonElement> ChamarAsync(string tipo, object dados, TimeSpan limite); }`;
`sealed class HostOculto : IAsyncDisposable { static Task<HostOculto> CriarAsync(int larguraPx, int alturaPx); DocumentoWeb Documento { get; } Task<string> CdpAsync(string metodo, string json); }`.

Envelope: `{ "v": 1, "tipo": string, "id": string, "dados": object }`. Tipos host→web: `compor`, `aplicarPlano`, `textoPorFolha`, `rolarPara`, `definirZoom`. Web→host: `pronto`, `composto`, `planoAplicado`, `textoPorFolha`, `folhaVisivel`, `erro`. Mensagem com `v` ≠ 1, tipo desconhecido ou campo faltando: descartada e registrada nos dois lados.

Segurança: pasta da sessão `%LOCALAPPDATA%\EditorRelatoriosPMVNI\sessao\<guid>` mapeada para `doc.pmvni.invalid` (`CoreWebView2HostResourceAccessKind.Deny`), apagada no `Dispose`; `AddWebResourceRequestedFilter("*", All)` respondendo 403 a tudo que não for esse host; `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data: https://doc.pmvni.invalid; style-src 'unsafe-inline'; script-src https://doc.pmvni.invalid/paginador.js">` inserido no `<head>` só no arquivo da sessão; DevTools desligado em Release, menu de contexto, autofill, senhas, barra de status e objetos do host desligados; `NewWindowRequested` → `Handled`; `DownloadStarting` → `Cancel`.

- [ ] **Passo 1:** testes: `requisicao_externa_bloqueada` (documento com `<img src="https://exemplo.com/x.png">` → nenhuma requisição sai, evento de bloqueio registrado), `script_inline_nao_executa`, `mensagem_tipo_desconhecido_descartada`, `pasta_da_sessao_apagada_ao_fechar`. Falham.
- [ ] **Passo 2:** implementar; `web/paginador` com `build.mjs` (esbuild, `--bundle --format=iife --target=chrome120`), saída copiada para `src/Pmvni.Composicao.Web/web/paginador.js` como recurso embutido. Passam. Commit.

### Tarefa 7: Paginador — cálculo do plano de quebras

**Arquivos:** `web/paginador/src/{medida,fragmentar,folhas,plano}.ts`, `web/paginador/folha.css` (embutido no bundle); `PlanoQuebras.cs`; teste `tests/Pmvni.Web.Testes/PaginadorTestes.cs` + páginas de teste em `tests/Pmvni.Web.Testes/casos/*.html` (HTML curto com o CSS do kit).
**Consome:** Tarefa 6. **Produz:** mensagem `compor` → `composto { plano: PlanoQuebras, folhas: number, refsPorFolha: string[][] }`; `Task<PlanoQuebras> DocumentoWeb.ComporAsync()`.

O host oculto compõe com `Emulation.setDeviceMetricsOverride { deviceScaleFactor: 1 }` e `Emulation.setEmulatedMedia { media: "print" }`.

Regras (genéricas, lidas do estilo computado, não de nomes de classe):
- Remover do documento as regras `@page` do kit e inserir `folha.css`: `@page { size: 210mm 297mm; margin: 0 }`; `.folha` 210 × 297 mm, `position: relative`, `break-after: page`; `.area` absoluta em `left: 14mm; top: 28mm; width: 182mm; height: 254mm`, `display: flow-root`; cabeçalho `<img>` com `CabecalhoUri`, largura 182 mm, base em 23 mm do topo; rodapé em `top: 282mm` com as duas caixas (Verdana 6,2 pt, `#637083` e negrito `#1D3D5E`, `border-top: .6pt solid #DDE4EB`, `padding-top: 2.5mm`). **Constantes de posição em um só lugar** (`folha.css`), ajustadas na Tarefa 9.
- Esperar estabilidade: `document.fonts.ready`, `decode()` de todas as `img`, dois `requestAnimationFrame` sem mudança de `scrollHeight`.
- Encher folha a folha com os filhos do `<body>`, na ordem. Transbordou → dividir o bloco:
  `break-inside: avoid` → indivisível (vai inteiro para a próxima folha); tabela → divide entre linhas `tr`, clonando `colgroup` e `thead`; demais contêineres → divide entre filhos, clonando a "casca"; com `box-decoration-break: clone` a casca repete bordas, cantos e preenchimentos; com `slice` o 1º pedaço perde borda/cantos/preenchimento de baixo e o 2º os de cima; `<p>` maior que o espaço → divide por linha (posição por `Range.getClientRects`, `orphans`/`widows` 2).
- `break-after: avoid` no anterior ou `break-before: avoid` no seguinte → leva o anterior junto, se couber numa folha vazia; senão, quebra mesmo assim.
- Margem de cima do 1º bloco de uma folha que **não** é a 1ª: zerada (quebra não forçada, como o Chromium).
- Bloco indivisível maior que uma folha vazia: dividir no nível mais fino permitido; se ainda assim não couber, colocar e registrar `Estouro { Folha, Ref, AlturaMm }`. Nunca `overflow: hidden`.
- O plano grava, para cada folha, a posição do primeiro conteúdo (`PosicaoFluxo`: caminho de índices no fluxo original, deslocamento de texto ou índice de linha da tabela) e o `Sha256Html` do documento.

- [ ] **Passo 1:** testes (cada caso é um HTML pequeno com o CSS do kit): `blocos_simples_quebram_na_folha_seguinte`, `card_dividido_repete_borda_clone`, `refs_div_dividido_com_slice`, `tabela_repete_thead_e_nao_divide_linha`, `sec_acompanha_o_bloco_seguinte`, `paragrafo_longo_divide_por_linha_com_orphans_widows`, `margem_superior_zerada_na_folha_seguinte`, `bloco_gigante_gera_estouro`, `imagem_lenta_nao_altera_plano` (imagem servida com atraso dá o mesmo plano), `mesmo_html_mesmo_plano` (duas execuções idênticas). Falham.
- [ ] **Passo 2:** implementar. Passam. Commit.

### Tarefa 8: Aplicar o plano, texto por folha e exportação do PDF (V2)

**Arquivos:** `web/paginador/src/{plano,texto}.ts`; `DocumentoWeb.cs` (`AplicarPlanoAsync`, `TextoPorFolhaAsync`, `CapturarFolhaAsync`); `src/Pmvni.Composicao.Web/Exportador.cs`; teste `ExportacaoTestes.cs`.
**Produz:**
`Task<ResultadoAplicacao> DocumentoWeb.AplicarPlanoAsync(PlanoQuebras p)` (`record ResultadoAplicacao(int Folhas, IReadOnlyList<int> FolhasQueTransbordam)`);
`Task<IReadOnlyList<string>> DocumentoWeb.TextoPorFolhaAsync()`;
`Task<byte[]> DocumentoWeb.CapturarFolhaAsync(int folha, int dpi)` (CDP `Page.captureScreenshot`, `clip` da folha, `scale = dpi/96`, `captureBeyondViewport`);
`static Task<ResultadoExportacao> Exportador.ExportarAsync(ResultadoRender r, ConfigFolhas cfg, string caminhoPdf)` → compõe no host oculto, recusa se houver `Estouro`, imprime com CDP `Page.printToPDF { preferCSSPageSize: true, printBackground: true, displayHeaderFooter: false, marginTop/Bottom/Left/Right: 0 }`, confere páginas do PDF = folhas, grava `<pdf>.recibo-editor.json` `{ recibo_versao: 1, modelo_revisao: 10, sha256_modelo: {...}, sha256_dados, sha256_html, plano_versao, webview2, sha256_pdf, folhas, gerado_em }`.

`AplicarPlano` reconstrói as folhas pelas posições do plano **sem medir para decidir**; só mede para informar `FolhasQueTransbordam`. Se `Sha256Html` não bater com o documento, recusa.

- [ ] **Passo 1:** testes: `aplicar_plano_reproduz_as_mesmas_folhas` (texto por folha igual ao da composição), `plano_de_outro_documento_recusado`, `plano_aplicado_com_escala_150_nao_muda_conteudo_das_folhas` (host com `deviceScaleFactor: 1.5`), `exportacao_recusada_com_estouro` (corpus `estouro`, mensagem "Bloco … excede a página …"), `pdf_tem_o_mesmo_numero_de_folhas`, `recibo_gravado_com_hashes`. Falham.
- [ ] **Passo 2:** implementar. Passam. Commit.

### Tarefa 9: Verificador e suíte de fidelidade V2–V4; calibração de cabeçalho e rodapé

**Arquivos:** `src/Pmvni.Verificador/Program.cs`; `tools/fidelidade.py` (subcomandos `v2`, `v3`, `v4`, `tudo`); `docs/fidelidade.md`.
**Comandos do Verificador:** `html <dados.json> <saida.html> [--instrucoes]`; `plano <dados.json> <saida.json>`; `pdf <dados.json> <saida.pdf>`; `capturas <dados.json> <pasta> --dpi 150`; `texto <dados.json> <saida.json>`; `autoteste-pdf <saida.pdf>` (da Tarefa 1).

Critérios (iniciais; mudar só com justificativa registrada em `docs/fidelidade.md`):
- **V2 (tela × PDF do editor):** cada folha capturada a 150 dpi vs. página do PDF rasterizada pelo PyMuPDF a 150 dpi; texto por folha **idêntico** (espaços normalizados); diferença visual: pixel diferente se |Δ| > 48 em tons de cinza; folha aprovada com ≤ 0,5 % de pixels diferentes.
- **V3 (editor × gerador do kit, mesmo `dados.json`):** mesmo número de páginas e **mesmo texto por página** (logo, mesmas quebras); diferença visual ≤ 2 % por página. Registrar versões (WebView2 do editor, Edge do kit).
- **V4:** V1–V3 sobre todo o corpus + `dados_modelo.json --instrucoes` + 17098 local (exceto `estouro`, que só verifica a recusa).

- [ ] **Passo 1:** implementar o Verificador e `fidelidade.py`. O gerador do kit roda sempre com `--rascunho --manter-html` em `%TEMP%` (abre o Edge pelo `.cmd` via `explorer.exe`, como o kit já faz); tudo apagado no fim; `tests/saida/*.json` só com contagens, hashes, páginas e porcentagens.
- [ ] **Passo 2:** rodar `python -I tools/fidelidade.py v3 --caso curto`. Medir no PDF do kit (PyMuPDF: posição da imagem do cabeçalho e das linhas/textos do rodapé) e ajustar as constantes de `folha.css` até V3 visual ≤ 2 % na página 1. Registrar as medidas em `docs/fidelidade.md`.
- [ ] **Passo 3:** rodar `python -I tools/fidelidade.py tudo`. Esperado: todos aprovados. Divergência de quebra em algum caso → corrigir a regra do paginador (Tarefa 7) com um novo teste que reproduza o caso; nunca afrouxar o critério para passar. Commit.

### Tarefa 10: App WinUI — visualização A4 contínua (somente leitura)

**Arquivos:** `src/Pmvni.App/{App.xaml,MainWindow.xaml,MainWindow.xaml.cs,Estilos.xaml}`; `web/paginador/src/tela.ts` (aparência só fora das folhas); teste manual roteirizado em `docs/roteiro-etapa-1.md` + teste `tests/Pmvni.Web.Testes/TelaTestes.cs`.

- Abertura: `Abrir` (FileOpenPicker com `InitializeWithWindow`) ou arquivo passado na linha de comando; aceita `dados.json` do kit (Etapa 1).
- Fluxo: renderiza → compõe no host oculto → aplica o mesmo plano no WebView2 visível.
- Layout: Mica; barra de título própria com "Processo nº — Requerente" (de `capa`), estado ("Composto: 14 folhas" / "1 bloco excede a página") e botões Abrir e Exportar PDF; à esquerda, lista recolhível de seções e itens (`RefBloco.Rotulo`, folha); centro, folhas empilhadas em fundo cinza, sombra leve, 8 mm entre folhas; rodapé da janela com zoom (− 100 % +, Ajustar largura, Página inteira) e "folha X de Y". Estilos do trilho como no Gmail (indicador 3 × 16, `AccentFillColorDefaultBrush`).
- Zoom: `transform: scale()` no contêiner com espaçador de tamanho; **nunca** recompõe. Tema escuro muda só o fundo fora das folhas.
- Folha que transborda na tela (`FolhasQueTransbordam`): faixa vermelha fina na margem cinza, fora da folha.
- Exportar: `FileSavePicker` → `Exportador.ExportarAsync`; mensagens com `InfoBar`.

- [ ] **Passo 1:** testes `zoom_nao_recompoe` (mesmo plano e mesmo texto por folha após `definirZoom 0.5` e `2.0`) e `aparencia_de_tela_nao_entra_no_pdf` (PDF exportado após abrir na tela = PDF do Verificador, mesmo `sha256_html` e mesmo texto por página). Falham → implementar → passam.
- [ ] **Passo 2:** build com `tools/build-release.ps1`; abrir o .exe via `explorer.exe` com `tests/corpus/longo.json`; seguir `docs/roteiro-etapa-1.md` (rolagem contínua, zoom, navegação, exportação, corpus `estouro` recusado, tema escuro) e registrar o resultado. Commit.

### Tarefa 11: Entrega da etapa — build final, limpeza e Recriar PC

**Arquivos:** `tools/build-release.ps1` (versão final), `recriar.json`, `docs/entrega-etapa-1.md`; conforme a receita: `Recriar PC\sistema\MAPA-DO-AMBIENTE.md` (se mudar ligações entre apps).

- [ ] **Passo 1:** `build-release.ps1` final: testes → publicação → confere o `.exe` (existe, > 0, abre com `--autoteste`) → apaga `bin`, `obj`, publicações e executáveis anteriores, `tests/saida`, `node_modules/.cache` e temporários em `%TEMP%` criados pela build → informa o espaço liberado. Rodar; conferir que as pastas sumiram.
- [ ] **Passo 2:** ler `Recriar PC\sistema\LEIA-ME.md` (formato da receita) e escrever `recriar.json` (app de arquivo único; dependências: WebView2 Runtime do Windows; para desenvolver: .NET SDK 10, Node, Python + PyMuPDF). Rodar via `explorer.exe` `Capturar agora.cmd` e `Conferir.cmd`; ler `CONFERENCIA.md` e corrigir o que apontar.
- [ ] **Passo 3:** `docs/entrega-etapa-1.md`: o que funciona, o que foi testado (com o resultado de `fidelidade.py tudo`), o que fica pendente. Commit.

---

## Fora desta etapa

Edição de itens, `.pmvrel`, autosave, desfazer (Etapa 2); cálculos e diagramas vinculados (Etapa 3);
pranchas e marcações (Etapa 4); despachos e gestão de modelos (Etapa 5); associação de arquivo e
instalador (Etapa 6); integração ao kit (Etapa 7).
