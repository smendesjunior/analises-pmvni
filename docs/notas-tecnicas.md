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
em `<raiz>\WebView2`, igual no app e no `HostOculto`.

## Host oculto (WebView2 sem interface)

`Pmvni.Composicao.Web.HostOculto`: thread STA própria, janela popup Win32 nunca mostrada (`CreateWindowEx`),
`CoreWebView2Environment.CreateWithOptionsAsync` e
`CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle(hwnd))`, pela
projeção WinRT do WebView2 que vem com o Windows App SDK (a mesma do controle XAML), também a partir do
console `Pmvni.Verificador`. As continuações voltam à thread STA por um `SynchronizationContext` que usa
`PostMessage` para a janela oculta.

Robustez (nenhuma operação fica pendurada):
- `ExecutarAsync(acao, tempoLimite)` (padrão 60 s) e `NavegarParaHtmlAsync(webView, html, tempoLimite)` (padrão
  30 s) falham com `TimeoutException`; a navegação só aceita o `NavigationCompleted` do seu `NavigationId`.
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
  fora do host é cancelado; janelas novas e downloads cancelados; menu de contexto, autofill, senhas, barra de status,
  objetos do host e diálogos desligados; DevTools só em Debug. A CSP vai num `<meta>` inserido no `<head>` **só da cópia
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
  (`Escala`), para quem aplica o plano não medir de novo.
- itens de flex em linha (o `.sec`) e de grade não se dividem por dentro; flex em coluna, sim.
- bloco com altura fixa e conteúdo que não é monolítico é fatiado pelo Chromium entre páginas (o kit não gera esse caso);
  o paginador o trata como indivisível.
- margem de cima truncada depois de quebra não forçada; orphans/widows 2; `.refs` (slice) e `.card` (clone) como no PDF.
- divergência aceita: linha de tabela (ou bloco indivisível) maior que a área — o Chromium fatia; o paginador registra
  `Estouro` e a exportação é recusada (spec, seção 5.3).
- `WebResourceRequested` e o `Fetch` do CDP não veem as requisições do host virtual mapeado; o teste da imagem lenta usa um
  host de teste não mapeado com `Page.setBypassCSP` e `loading="lazy"` (imagem comum segura o `load`, e `CarregarAsync`
  espera o `NavigationCompleted`).
- o papel do PDF sai com 209,89 × 297,01 mm (o Chromium arredonda o tamanho A4): a calibrar na Tarefa 9.
