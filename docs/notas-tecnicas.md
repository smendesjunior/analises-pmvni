# Notas técnicas

## Executável único (Tarefa 1 — prova técnica, 14/11/2026)

Build: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` (.NET SDK 10.0.404,
Windows App SDK 1.8.260921001, WebView2 Runtime 157.0.4341.46).

| Item | Valor medido |
|---|---|
| Arquivo final | `Aplicativo\Editor de Relatorios PMVNI.exe` — único arquivo da pasta |
| Tamanho do .exe | 89.360.429 bytes (85,2 MB, compactado com `EnableCompressionInSingleFile`) |
| Pasta de extração | `%TEMP%\.net\Editor de Relatorios PMVNI\<id do bundle>\` (ex.: `...\Temp\.net\Editor de Relatorios PMVNI\OrCe-uqhCUAo\`), 197,6 MB, 410 arquivos; é o `AppContext.BaseDirectory` |
| Primeira abertura (extração fria + perfil do WebView2 + navegação + fechamento) | 12,1 s (13,1 s e 15,5 s em builds anteriores) |
| Aberturas seguintes (extração já feita) | 1,0 s |
| Nível de execução | manifesto embutido no .exe: `requestedExecutionLevel level="asInvoker"`, `dpiAwareness PerMonitorV2`; autoteste: `"elevado": false` |

O que foi necessário para o WinUI 3 rodar em arquivo único:

- `PublishSingleFile` + `IncludeAllContentForSelfExtract` + `SelfContained` + `WindowsAppSDKSelfContained`
  + **`EnableMsixTooling=true`** (exigido pelo `Microsoft.WindowsAppSDK.SingleFile.targets` para gerar o `.pri`).
- O inicializador automático do Windows App SDK define `MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY` com a pasta
  de extração; o manifesto gerado redireciona as DLLs do runtime para lá e o MRT procura o `.pri` nessa pasta.
- **`ProjectPriFileName=resources.pri`**: sem isso o .pri do app sai como `Pmvni.App.pri`; o MRT de app sem
  pacote procura `resources.pri` ou `<nome do exe>.pri`, e como o .exe final é renomeado para
  `Editor de Relatorios PMVNI.exe`, o `MainWindow.xaml` não era encontrado (`XamlParseException: XAML parsing
  failed` em `MainWindow.InitializeComponent`).
- Extrações de builds anteriores ficam em `%TEMP%\.net\Editor de Relatorios PMVNI\<outro id>` (uma por build,
  ~200 MB cada; o .NET não as apaga). O `tools\build-release.ps1` abre `tools\limpar-extracoes.cmd` com
  `explorer.exe` (fora do pacote do Claude); o script lê o id do bundle no cabeçalho do próprio .exe
  (assinatura do bundle .NET no apphost -> deslocamento do cabeçalho -> `bundleId`), mantém só a pasta desse id e
  grava o resultado em `tests\saida\limpeza.json`, que o build confere. Se o id não puder ser lido, apaga todas.
- Verificação reproduzível: `explorer.exe tools\autoteste.cmd` (apaga a extração para medir a abertura fria,
  roda o .exe 2x com `--autoteste`, grava `tests\saida\autoteste*.json` e `autoteste-log.txt`).

Pasta de dados: `%LOCALAPPDATA%\EditorRelatoriosPMVNI` (ou a variável `PMVNI_DADOS_DIR`); perfil do WebView2
em `<raiz>\WebView2`, igual no app e no `HostOculto`, com os controllers **InPrivate** (ver "Perfil do WebView2" abaixo);
sessões em `<raiz>\sessao\<guid>`; erros não tratados em `<raiz>\logs\erro-*.txt` (só tipo, HRESULT e pilha; nenhuma
mensagem, que pode citar conteúdo; ficam os 20 mais recentes; vale também fora do autoteste).

Sem o WebView2 Runtime (`CoreWebView2Environment.GetAvailableBrowserVersionString()` sem versão ou com erro), o app não
tenta abrir a visualização: mostra um InfoBar com a explicação simples (instalar o "Microsoft Edge WebView2 Runtime",
Evergreen, e abrir de novo) e desabilita o Abrir; no autoteste grava o erro.

## Host oculto (WebView2 sem interface)

`Pmvni.Composicao.Web.HostOculto`: thread STA própria, janela popup Win32 nunca mostrada (`CreateWindowEx`),
`CoreWebView2Environment.CreateWithOptionsAsync` e
`CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle(hwnd))`, pela
projeção WinRT do WebView2 que vem com o Windows App SDK (a mesma do controle XAML), também a partir do
console `Pmvni.Verificador`. As continuações voltam à thread STA por um `SynchronizationContext` que usa
`PostMessage` para a janela oculta.

Robustez (nenhuma operação fica pendurada):
- `ExecutarAsync(acao, tempoLimite)` (padrão 60 s) e `NavegarParaUrlAsync(webView, url, tempoLimite)` (padrão
  30 s) falham com `TimeoutException`; a navegação só aceita o `NavigationCompleted` do seu `NavigationId`.
- Escala 1 em qualquer processo: `ShouldDetectMonitorScaleChanges = false` e `RasterizationScale = 1` no controller. Achado
  na revisão final: no app (DPI por monitor, monitor a 150 %) o controller herdava a escala do monitor e o `longo` saía
  com a etiqueta além da borda na folha 13 (tela e PDF do app), contra a folha 14 do Verificador, dos testes e do kit.
- `Inutilizavel` (descartado ou processo do WebView2 caído): o app descarta o host de composição e cria outro no
  próximo Abrir; também o descarta depois de uma composição que falhou, caiu ou estourou o tempo.
- `Dispose` falha todas as operações pendentes com `ObjectDisposedException`; chamadas depois dele também.
- `ProcessFailed` (navegador, renderização encerrada ou sem resposta) falha as pendentes e as seguintes com
  `InvalidOperationException`.
- Cada trabalho da fila STA roda protegido: exceção não atravessa o WndProc; fica em `UltimoErroInterno` e
  falha as operações pendentes.
- Prova: `dotnet run --project src/Pmvni.Verificador -- autoteste-falhas`.

`Pmvni.Verificador autoteste-pdf <saida.pdf>` imprime `<p>ok</p>` via CDP `Page.printToPDF`: 1 página,
210,23 × 297,01 mm (o Chromium arredonda o papel), texto "ok".

## Núcleo web (Tarefa 6)

- `Seguranca.AplicarAsync(core, pastaSessao)`: pasta da sessão (`<dados>\sessao\<guid>`) mapeada para `doc.pmvni.invalid`
  com acesso `Deny`; filtro `WebResourceRequested` (`*`, All) responde 403 a tudo fora desse host; `NavigationStarting`
  só aceita, uma vez, a URL exata que o `DocumentoWeb` está abrindo (`CarregarAsync`): recarga (F5, `location.reload()`),
  a mesma URL de novo e outras páginas do host depois do carregamento são canceladas; janelas novas e downloads cancelados;
  menu de contexto, autofill, senhas, barra de status, objetos do host, diálogos e **atalhos do navegador**
  (`AreBrowserAcceleratorKeysEnabled = false`: Ctrl+P, F5/Ctrl+R, Ctrl+F, zoom do navegador; Ctrl+C e a navegação por
  teclado continuam) desligados nos dois hosts; DevTools só em Debug. `CarregarAsync` atualiza a data da pasta da sessão:
  a varredura de sessões órfãs (mais de 1 dia sem alteração) nunca apaga uma sessão viva. A CSP vai num `<meta>` inserido no `<head>` **só da cópia
  da sessão** (`documento.html`), junto de `<script src=".../paginador.js" defer>`; o HTML do renderizador não muda.
- A CSP barra a imagem externa antes de qualquer requisição; o filtro 403 é a segunda camada (testado sem CSP).
- Ponte: `Ponte/Mensagens.cs` (C#) e `web/paginador/src/mensagens.ts` (TS) validam o envelope `{v:1,tipo,id,dados}`;
  inválidas são descartadas e registradas (`DocumentoWeb.Descartadas`; no JS, `console.warn` e mensagem `erro` ao host).
- `DocumentoWeb.ChamarAsync` correlaciona resposta por `id`; resposta `erro` vira `ErroWebException`.
- `HostOculto.CriarAsync(l, a)`, `.Documento`, `.CdpAsync(metodo, json)`; `DisposeAsync` apaga a pasta da sessão.
- Os testes (`tests/Pmvni.Web.Testes`) definem `PMVNI_DADOS_DIR=tests\saida\dados`: o shell do Claude roda dentro do MSIX e
  virtualizaria o AppData. O `paginador.js` é gerado por `web/paginador` (`npm run build`) e fica commitado.

## Paginador A4 (Tarefa 7)

`DocumentoWeb.ComporAsync()` fixa no core (host oculto) `Emulation.setDeviceMetricsOverride {deviceScaleFactor:1}` e
`Emulation.setEmulatedMedia {media:"print"}` e manda `compor` (sha256 do HTML, refs, cabeçalho/rodapé). O paginador tira as
regras `@page` do kit, insere `folha.css` (geometria só ali), move o `<body>` para um fluxo de medida com a largura da área
(`div.folha.pmvni-medida > .area`), espera estabilidade (fontes, `decode()` das imagens, dois quadros sem mudar de altura) e
monta `section.folha` com `.area` (182 × 254 mm), cabeçalho e rodapé.

Escolha das quebras (modelo do LayoutNG): pontos entre irmãos em fluxo, entre linhas de tabela e entre linhas de texto, com
apelo 3 (perfeito), 2 (viola orphans/widows) ou 1 (viola `break-*: avoid`, inclusive de ancestral com `break-inside: avoid`
e propagado pela cadeia de último/primeiro filho). A folha termina no ponto que cabe com o maior apelo e, entre iguais, o
mais tardio; cada folha é montada e conferida no DOM (com as imagens dela carregadas) e, se não couber, tenta-se o próximo.
"Cabe" = altura ≤ área + 0,05 px (`EPS_PX`, absorve o arredondamento em LayoutUnit de 1/64 px).

Comportamentos do Chromium 157 conferidos imprimindo os mesmos HTML (`Page.printToPDF` com o `@page` do kit) e comparando o
texto de cada página com o de cada folha (casos de `tests/Pmvni.Web.Testes/casos` e corpus; tudo igual, exceto `estouro`):
- **shrink-to-fit**: se o conteúdo é mais largo que a página, o Chromium diagrama a página k vezes maior (k = largura do
  documento / largura da área, até 1,5) e a imprime reduzida. No kit isso acontece de verdade: a etiqueta "INFORMAÇÕES
  DIVERGENTES" (`white-space: nowrap`) transborda a coluna da tabela em ~50 px nos casos `longo` e `extremos` (k = 1,0723).
  O paginador mede k com k = 1 e diagrama a área em 182k × 254k mm com `transform: scale(1/k)` (`--pmvni-k`); k vai no plano
  (`Escala`), para quem aplica o plano não medir de novo. Cada bloco que transborda vira `AvisoLargura` (folha, ref, escala
  pedida) para o editor avisar "documento reduzido"; se pedir mais que 1,5, vira também `Estouro` com `LarguraMm`
  (exportação recusada: o Chromium cortaria).
- itens de flex em linha (o `.sec`) e de grade não se dividem por dentro; flex em coluna, sim.
- bloco com altura fixa e conteúdo que não é monolítico: o Chromium fatia também a sobra de altura entre páginas (o kit
  não gera esse caso). O paginador não fatia a sobra: o bloco só se divide onde qualquer bloco se divide (entre filhos
  ou entre linhas); sem filhos, ou com uma só linha, fica indivisível.
- margem de cima truncada depois de quebra não forçada; orphans/widows 2; `.refs` (slice) e `.card` (clone) como no PDF.
- divergência aceita: linha de tabela (ou bloco indivisível) maior que a área — o Chromium fatia; o paginador registra
  `Estouro` e a exportação é recusada (spec, seção 5.3).
- `WebResourceRequested` e o `Fetch` do CDP não veem as requisições do host virtual mapeado; o teste da imagem lenta usa um
  host de teste não mapeado com `Page.setBypassCSP` e `loading="lazy"` (imagem comum segura o `load`, e `CarregarAsync`
  espera o `NavigationCompleted`).
- o papel do PDF sai com 209,89 × 297,01 mm (o Chromium arredonda o tamanho A4): a calibrar na Tarefa 9.

Plano (além do brief): `PosicaoFluxo.Forcada` diz se a quebra que abre a folha é forçada (margem de cima mantida), para
que aplicar o plano não precise medir; `montarFolhaDoPlano(plano, i, cfg)` (exportado no global `Paginador`) monta uma
folha só com o plano. Se o documento não estabiliza no limite (30 s; `limiteEstabilidade` em `ComporDetalhadoAsync`),
`compor` falha com o código `instavel` (e `folhas_demais` acima de 5000 folhas), nunca devolve plano medido em layout
instável. `PlanoQuebras.DeJson` recusa versão diferente de 1 e escala fora de [1; 1,5].

## Aplicar o plano, texto por folha e exportação (Tarefa 8)

`DocumentoWeb.AplicarPlanoAsync(plano)` reconstrói as folhas só com o plano, sem medir para decidir e sem mexer na
emulação do core (serve à tela com qualquer escala de monitor): confere o sha256 do HTML (no C# e no paginador: códigos
`plano_de_outro_documento` e `plano_invalido`), aplica os refs do render, `--pmvni-k` = `plano.escala`, margem de cima pela
regra `truncaTopo`, acabamento, numeração do rodapé e extensão dos pedaços; o fluxo de medida fica guardado
(`display: none`) e as `section.folha` anteriores saem. Espera as fontes e as imagens das folhas e só então mede a altura de
cada folha para informar `FolhasQueTransbordam`. Conferido: corpus inteiro, aplicado no mesmo host e num documento
recém-carregado, e num host com `deviceScaleFactor` 1,5 e mídia de tela, dá o mesmo texto por folha da composição
(`TextoPorFolhaAsync`: `innerText` da área, espaços normalizados).

`Exportador.ExportarAsync` usa um host oculto novo: carrega, compõe, recusa se houver estouro ("Bloco 'ref' excede a página
n …", só folha, ref e medidas) e imprime o mesmo DOM com `Page.printToPDF {preferCSSPageSize, printBackground, sem
cabeçalho/rodapé, margens 0}`; confere páginas = folhas (leitor mínimo de PDF: objetos `/Type /Page` contra o `/Count` da
raiz; recusa `/ObjStm`) antes de gravar o PDF e `<nome>.recibo-editor.json`.

Exportação do app (D2: o PDF é a impressão das folhas que a tela mostra): `Exportador.ExportarFinalAsync(dados, cfg, pdf,
planoTela)` lê o dados.json **uma vez**, renderiza esses bytes sem rascunho e grava no recibo o sha256 deles. Recusa com
`ExportacaoDivergenteException`, sem gravar nada, se o sha256 do HTML final não for o `Sha256Html` do plano da tela ("O
arquivo mudou desde que foi aberto; reabra antes de exportar." — o dados.json ou uma imagem mudou) ou se a composição da
exportação não der os mesmos inícios de folha e a mesma escala do plano da tela. O recibo leva `"rascunho"`: `false` na
exportação final; `true` na rota do Verificador com `--rascunho`/`--instrucoes` (a suíte de fidelidade, como o kit). O
botão Exportar fica desabilitado, com dica explicando, quando há estouro ou quando a conferência final tem erros
(campos `[ ]` em aberto).

Conteúdo além da borda lateral da área: `aplicarPlano` devolve também `folhasQueTransbordamNaLargura` (retângulos do
`Range` do conteúdo da área, texto inclusive, além da borda esquerda/direita). A tela marca essas folhas com a mesma faixa
vermelha das que transbordam em altura e abre um InfoBar informativo (nada é cortado no editor; o kit cortaria). No
corpus: `longo` folha 14 e `extremos` folhas 4–7, exatamente as páginas de `fora_da_area` da V3.

`PlanoQuebras.DeJson` valida como o `lerPlano` do paginador: além de versão e escala, caminho não vazio com índices ≥ 0 e
deslocamento/linha ≥ 0 ou nulos.

## Perfil do WebView2 (D4)

`FabricaWebView2` é a única fábrica do ambiente (opções padrão, pasta `<dados>\WebView2`) e das opções de controller,
usada pelo controle XAML (`EnsureCoreWebView2Async(ambiente, opcoes)`) e pelo `HostOculto`
(`CreateCoreWebView2ControllerAsync(janela, opcoes)`): `IsInPrivateModeEnabled = true` nos dois. O conteúdo do relatório
(HTML, imagens, título no histórico, cache) não é gravado no perfil. Prova com o processo encerrado:
`perfil_webview2` de `tools/fidelidade.py` (marca única no processo e no requerente de uma cópia do `curto`; pdf, capturas e
texto pelo Verificador; nenhum arquivo da pasta de dados contém a marca). Controle negativo (14/11/2026): com
`IsInPrivateModeEnabled = false`, a mesma marca aparece em `EBWebView\Default\History` (o `<title>` do relatório leva o
número do processo); com InPrivate, 0 de 142 arquivos. O teste `SegurancaTestes.conteudo_do_relatorio_nao_fica_no_perfil_do_webview2`
confere o InPrivate e a ausência da marca com o navegador ainda aberto.

- Página em branco a mais com k > 1: a caixa `.area` diagramada com 254k mm passava da folha (28 + 272 mm) e a impressão
  gerava um fragmento vazio depois da última folha (longo: 22 páginas para 21 folhas). A caixa agora tem 254 mm e a
  altura útil vem do topo do rodapé (`fimDaArea`), sem mudar nenhum plano.
- Papel medido no MediaBox (Chromium 157): 209,889 × 297,011 mm em todos os casos (dentro de ±0,5 mm de A4); o mesmo pelo
  `Windows.Data.Pdf`.
- Largura (correções da Tarefa 7): bloco largo cuja folha não é achada continua gerando estouro (folha do ref, senão a 1ª);
  largura vinda só do `scrollWidth` (texto `nowrap` que transborda a própria caixa) procura o bloco responsável e gera aviso
  e, acima de 1,5, estouro.

## Linha de base de desempenho para a Etapa 2 (14/11/2026)

`TelaTestes.tempos_de_compor_e_aplicar_do_corpus` (Release, WebView2 157.0.4341.46, host 1024 × 1400; mediana de 3
rodadas; grava `tests\saida\tempos.json`). "Compor" = `CarregarAsync` + `ComporDetalhadoAsync` no host de composição;
"aplicar" = `CarregarAsync` + `AplicarPlanoAsync` na "tela" (outro host, mídia de tela, escala 1). Só mede, sem limite.

| caso | folhas | compor (mediana; rodadas) | aplicar (mediana; rodadas) |
|---|---|---|---|
| curto | 2 | 80 ms (69 / 80 / 95) | 65 ms (69 / 64 / 65) |
| longo | 21 | 348 ms (236 / 348 / 613) | 196 ms (141 / 196 / 399) |
| extremos | 8 | 340 ms (600 / 333 / 340) | 264 ms (517 / 204 / 264) |

A abertura no app soma a isso o render do kit (duas vezes: rascunho e conferência final) e o zoom inicial.
