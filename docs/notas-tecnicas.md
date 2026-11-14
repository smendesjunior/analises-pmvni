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
  ~200 MB cada). O `tests\saida\autoteste.ps1` (rodado fora do pacote) apaga essa pasta antes de medir a
  primeira abertura.

Pasta de dados: `%LOCALAPPDATA%\EditorRelatoriosPMVNI` (ou a variável `PMVNI_DADOS_DIR`); perfil do WebView2
em `<raiz>\WebView2`, igual no app e no `HostOculto`.

## Host oculto (WebView2 sem interface)

`Pmvni.Composicao.Web.HostOculto`: thread STA própria, janela popup Win32 nunca mostrada (`CreateWindowEx`),
`CoreWebView2Environment.CreateWithOptionsAsync` e
`CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle(hwnd))`, pela
projeção WinRT do WebView2 que vem com o Windows App SDK (a mesma do controle XAML), também a partir do
console `Pmvni.Verificador`. As continuações voltam à thread STA por um `SynchronizationContext` que usa
`PostMessage` para a janela oculta.

`Pmvni.Verificador autoteste-pdf <saida.pdf>` imprime `<p>ok</p>` via CDP `Page.printToPDF`: 1 página,
210,23 × 297,01 mm (o Chromium arredonda o papel), texto "ok".
