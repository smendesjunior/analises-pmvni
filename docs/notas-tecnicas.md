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

## Latência de um comando (Etapa 2, Tarefa 6)

`RecomposicaoTestes.tempos_de_recompor_um_comando_do_corpus` (Release, WebView2 157.0.4341.46, host 1024 × 1400; mediana de 3
rodadas; grava `tests\saida\tempos-comando.json`). Depois de um `DefinirCampo` (título do item 1), mede o que a tela espera:
`Recompositor.Preparar` (dados.json efetivo + render do kit em rascunho + conferência final do kit, refs de sessão) +
`ComporAsync` (carregar e compor no host de composição) + carregar e `AplicarPlanoAsync` na "tela" (outro host). O teste só
exige medidas finitas e positivas e a recomposição correta; a meta de 1,5 s no `longo` é registrada aqui, não é barreira.

| caso | folhas | comando (mediana; rodadas) | preparar | compor | aplicar | linha de base da Etapa 1 (compor / aplicar) |
|---|---|---|---|---|---|---|
| curto | 2 | 118 ms (341 / 118 / 116) | 3 ms | 63 ms | 61 ms | 66 ms / 57 ms |
| longo | 21 | 254 ms (319 / 248 / 254) | 13 ms | 151 ms | 90 ms | 159 ms / 93 ms |
| extremos | 8 | 191 ms (263 / 188 / 191) | 8 ms | 110 ms | 74 ms | 106 ms / 71 ms |

O `longo` fica em cerca de um sexto da meta de 1,5 s. A recomposição soma à linha de base da Etapa 1 só o `Preparar` (13 ms no
`longo`); a primeira rodada de cada caso é a mais lenta (aquecimento do WebView2 e do renderizador), e a mediana a descarta. A
medida não inclui o zoom inicial nem a pintura da janela do app (Tarefa 9).

## Latência de um comando (Etapa 2)

Medida de ponta a ponta na janela do app, no roteiro da entrega (`19_latencia`, 15/11/2026, executável final, monitor 150 %,
janela 1265 × 701 DIP): mediana de 5 comandos de situação no `longo` (21 folhas), do comando até a tela recomposta e aplicada.

| rodada do roteiro | medidas (ms) | mediana | meta ≤ 1,5 s |
|---|---|---|---|
| final da Tarefa 14 (todos os 64 passos ok) | 637 / 700 / 719 / 688 / 701 | **700 ms** | atingida |
| rodada anterior do mesmo dia | 1521 / 1620 / 1591 / 1724 / 1521 | 1591 ms | não atingida |

A meta de 1,5 s é **atingida** na rodada final. A rodada anterior passou da meta (a causa não foi isolada; o mesmo executável e
o mesmo corpus deram 700 ms depois, o que aponta para carga do computador); a medida não é barreira (Ruling PF9), só registro. Em comparação com a tabela da Tarefa 6 acima (254 ms no
`longo`, só preparar + compor + aplicar nos hosts de teste), a janela soma o zoom, a pintura, o diário e a fila de comandos. A
recomposição incremental (desenho §8.4) fica para depois: com 700 ms ela não é necessária agora, mas é o caminho se relatórios maiores
passarem da meta.

Na mesma rodada, as conversões do LibreOffice (perfil persistente, passo 44) levaram 60,3 / 53,8 / 50,9 / 52,0 s.

## Gravação do pacote: janela residual (Etapa 2, Tarefa 4)

`Pacote.Gravar` confere o estado do `.pmvrel` no disco (sha256 e tamanho; a data não conta) e logo depois troca o arquivo com
`File.Replace`. Entre as duas chamadas existe uma janela de milissegundos em que outro processo (OneDrive, outro PC, outra
janela) pode gravar o destino e ser sobrescrito sem aviso: o Windows não oferece "troque só se ainda for esta versão". Na prática
essa janela é coberta pela trava `.~nome.pmvrel.lock` (Tarefa 5): enquanto uma janela do editor tem o pacote aberto com a trava
viva, a segunda abre só leitura, e a conferência de conflito pega o resto (alteração de fora feita antes da gravação). Depois da
troca nada mais derruba a gravação: se o OneDrive ou o antivírus segurar o arquivo novo por um instante, `Gravar` tenta de novo
algumas vezes e, se ainda falhar, devolve o estado calculado dos bytes que acabou de gravar.

## LibreOffice com perfil persistente (Etapa 2, onda de correções 1, Ruling T16-C4)

O conversor do despacho passou a usar um perfil só, persistente, em `<dados>\lo` (nome curto 8.3, limite de 120 caracteres),
criado na primeira conversão e nunca apagado; o `TEMP` continua na pasta de trabalho de cada preparo. Uma conversão por vez (mutex
`Local\PMVNI-Editor-LibreOffice-perfil`); `<dados>\lo.pid` guarda o soffice em curso, e um órfão vivo de uma queda é encerrado antes
da conversão seguinte (com as travas `.lock` velhas do perfil).

Medido no roteiro (passo 44; perfil apagado no início do passo 36; LibreOffice do kit, este computador), tempo de cada
`Converter` (do início do `soffice.com` ao PDF lido), em ordem — correção (36), aprovação (37), Gerar PDF (38), não sobrescreve (39):

| rodada | 1ª conversão (perfil novo) | 2ª | 3ª | 4ª |
|---|---|---|---|---|
| roteiro 1 da onda | 56,3 s | 52,8 s | 49,8 s | 52,0 s |
| roteiro 2 da onda | 60,3 s | 53,5 s | 53,7 s | 51,0 s |
| roteiro 3 da onda (final) | 56,7 s | 51,1 s | 53,4 s | 50,8 s |

O perfil persistente funciona (o `user\` fica e é reaproveitado), mas **não** encurta a conversão: a 2ª e as seguintes levam quase o
mesmo que a 1ª. Os ~50–55 s não são a inicialização do perfil (hipótese da Tarefa 16); a causa continua em aberto (pouca CPU durante a
espera, como na Tarefa 16 — candidata: alguma espera do `soffice.bin` com entrada e saídas redirecionadas ou na pasta do OneDrive).

## Imagens do dados.json e espera do LibreOffice (Etapa 2, onda de correções 2)

- `CaminhoImagem.Resolver` (importação e renderizador, modo do kit): o `..` relativo só vale se o caminho resolvido ficar dentro da pasta do
  processo, isto é, o ancestral que contém `.pmvni-ai` (`PROCESSOS\<proc>`) ou, sem ele, a pasta pai da pasta do dados.json; junções e links
  simbólicos são resolvidos antes da comparação (uma junção para fora recusa o caminho). Absoluto, de rede, com `:` ou com caractere de
  controle continua recusado; o renderizador só lê `.png .jpg .jpeg .webp .gif` e confere a assinatura dos bytes.
- `ConversorLibreOffice`: depois que o `soffice` sai, a espera pelas saídas redirecionadas é limitada ao que resta do limite (mínimo de 2 s) e
  respeita o cancelamento; ao estourar, a árvore é encerrada. Um `soffice.bin` filho que segure os canais não prende mais o mutex do perfil.

## Agentes locais: opções conferidas (Etapa 2, Tarefa 19, passo 0)

Conferido em 15/11/2026 por `tools\agentes-ajuda.ps1` (fora do pacote, só `--version`, `--help`, `login status` e `auth status`, sem chamar modelo):

- **Codex** `codex-cli 0.167.0-alpha.4.1` (`%LOCALAPPDATA%\OpenAI\Codex\bin\<id>\codex.exe`). A linha de versão tem o prefixo `codex-cli ` e um sufixo de pré-lançamento (`-alpha.4.1`): a leitura da versão pega só o `major.minor.patch`. `codex exec` aceita `--skip-git-repo-check`, `-C/--cd`, `--json`, `-o/--output-last-message` e o prompt `-` (lido da entrada padrão). `codex login status` sai com 0 e a primeira linha contém "Logged in" quando há login. A ajuda do `exec` **não cita** `CODEX_API_KEY`; o binário do Codex lê essa variável (a mensagem "run `codex login` or set CODEX_API_KEY" e a lista de variáveis de autenticação `OPENAI_API_KEY`/`CODEX_API_KEY`/`CODEX_ACCESS_TOKEN` estão nele), então ela segue como a variável do modo chave. A confirmação final é a primeira análise real com chave (fora dos testes, que nunca chamam IA).
- **Claude Code** `2.1.318 (Claude Code)` (`%LOCALAPPDATA%\Microsoft\WinGet\Links\claude.exe`). `claude auth status` existe; imprime JSON com as chaves `loggedIn`, `authMethod`, `apiProvider`, `analyticsDisabled`, `projectsDirectory`, `configDirectory`, `loginExpiresAt` e sai com **1 quando não há login** (com JSON mesmo assim): a leitura da sessão usa o JSON, não só o código de saída.
- **Python** `Python 3.14.10` (`python.exe` e `py -3`).
- Windows PowerShell 5.1 não tem `ProcessStartInfo.ArgumentList`: o script do passo 0 monta `Arguments` (só argumentos sem espaços). O código C# do editor usa `ArgumentList`.

## Legislação por PDFs anexados (Etapa 2, Tarefa 17)

Núcleo (`Pmvni.Legislacao`, `Pmvni.Composicao.Web\Legislacao`, `web\paginador\src\legislacao`); a interface (Ajustes › Legislação, autoteste, roteiro, `build-release.ps1`) vem depois, no checkout principal.

- **PDF.js `pdfjs-dist@6.4.299`** (Apache-2.0), versão exata no `package.json` e no `package-lock.json` (com integridade). `build.mjs` copia para `src\Pmvni.Legislacao\web\pdfjs\` os **283 arquivos / 4.754.750 bytes** do desenho §11.4 (e falha se a lista ou o total mudarem) e grava o `VERSAO.json` (pacote, versão, licença e sha256 de cada arquivo). O `RecursosLegislacao.ConferirPdfJs` confere o embutido com esse arquivo. O `.gitattributes` marca `src/Pmvni.Legislacao/web/**` como `-text`: com `core.autocrlf=true` a conversão de fim de linha mudaria os bytes e quebraria os sha256.
- **Opções do `getDocument` na 6.4.299**: todas as do desenho existem **exceto `isEvalSupported`**, que a 6.x removeu (a biblioteca não tem mais `eval` nem `new Function`; conferido por busca no `pdf.mjs` e no `pdf.worker.mjs`). Não é passada; a CSP sem `unsafe-eval` continua sendo a garantia. O JavaScript do PDF nunca roda: não há `PDFScriptingManager` nem o sandbox (`quickjs-eval` fica fora do pacote) e `enableScripting` fica no padrão (falso). A `pdf.min.mjs` 6.4.299 ainda deixa `globalThis.pdfjsLib` (além de exportar os símbolos); por isso o `extrator.js` (ESM, só o código do extrator, sem empacotar o PDF.js) o usa, e os tipos de `pdfjs-dist` entram só no `typecheck` (`import type`).
- **CSP duplicada = dois eventos**: a página do extrator tem a mesma CSP no `<meta>` e no cabeçalho, e o Chromium dispara um `securitypolicyviolation` por política, então cada bloqueio chegava duas vezes. O `extrator.ts` conta o mesmo bloqueio (diretiva + URI) visto em até 100 ms uma vez só (`ViolacoesCsp == 1` num `fetch` para fora). O `Runtime.evaluate` do CDP **ignora** a CSP (um `eval` avaliado por ele roda): a prova de bloqueio de script nos testes usa um `<script>` em linha inserido na página.
- **`favicon.ico`**: sem `<link rel="icon" href="data:,">` a página pedia `/favicon.ico` e levava 403 (aparecia em `RequisicoesBloqueadas`).
- **PDF em fluxo**: o servidor devolve o arquivo inteiro em fluxo (`FileStream` com `FileShare.ReadWrite | Delete`, `AsRandomAccessStream`) e o 206 de `Range` (`bytes=a-b`, `a-` e `-n`) com `Content-Range`; o teste `range_e_cabecalhos_do_pdf_chegam_ao_navegador` confere os dois e o 416. O PDF.js, porém, com o PDF servido do disco, recebe o arquivo em fluxo e **não chegou a pedir `Range`** nem num PDF de 3,5 MB (o dado já estava lá quando precisou); o `Range` fica pronto para quando faltar dado.
- **Tempo de extração de 200 páginas** (`tests\saida\tempos-extracao.json`, PDF de 62 KB gerado no teste): **≈ 0,4 s**, com o WebView2 já aberto (criar o extrator, com o PDF.js carregado, soma ≈ 1 s uma única vez). O cancelamento de uma de 400 páginas leva menos que o limite de 5 s do teste.
- **Extrator no app**: o `ExtratorPdfJs` é criado na thread da interface (guarda o `SynchronizationContext`; todo acesso ao WebView2 passa por ele) sobre uma janela já existente (`hwnd`), com o controller `IsVisible = false` (não aparece sobre a janela do editor); uma extração por vez (fila); cada evento (`textoPagina`, `progresso`) leva o id da extração a que pertence, e o que sobra de uma extração cancelada é ignorado.
- **`Mensagens.cs` (Etapa 1)**: ganhou duas sobrecargas `internal` (`TentarLer` e `Serializar`, com o conjunto de tipos como parâmetro) para a `MensagensLegislacao` reaproveitar a validação sem copiá-la; o comportamento público não mudou.
- **Executável**: 85,9 MB antes do PDF.js (Tarefa 19b, último build sem `Pmvni.Legislacao`) e **88,2 MB** depois (Tarefa 17, com o PDF.js 6.4.299 embutido: 283 arquivos, 4.754.750 bytes antes da compressão do single-file; ≈ 2,3 MB a mais no executável). O `build-release.ps1` escreve, depois do autoteste, `PDF.js 6.4.299 embutido: 283 arquivos, 4,75 MB (sha conferido).`, lido do bloco `legislacao` do JSON do autoteste (que só dá `ok` com o sha de cada arquivo conferido contra o `VERSAO.json`).
- **Ajustes › Legislação no app** (Tarefa 17, parte do app): `SessaoLegislacao` (acervo, servidor, o extrator real criado na thread da interface na primeira extração e de novo se o processo dele cair, índice preguiçoso atualizado a cada mudança) e a seção da página Ajustes (`ItemsRepeater`, uma linha por anexo). O seletor e as alterações do catálogo passam pela fila de comandos; a leitura dos PDFs (cópia, sha, PDF.js) corre **fora** dela, com a linha de progresso e "Cancelar", para uma anexação longa não segurar os outros comandos; uma anexação ou substituição por vez. As releituras da lista são uma por vez, na ordem (a que termina por último leu o acervo por último). O extrator é descartado no `Closed`, antes da janela. `IPerguntas` ganhou a sobrecarga `AbrirArquivosAsync(".pdf")` (seleção múltipla com filtro; cancelado = lista vazia), ao lado da do Novo relatório.

## Leitor da legislação (Etapa 2, Tarefa 18)

- **`pdf_viewer.mjs` 6.4.299** lê `globalThis.pdfjsLib` ao carregar e deixa `globalThis.pdfjsViewer` (`EventBus`, `PDFLinkService`, `PDFFindController`, `PDFViewer`…): o `leitor.html` carrega `pdf.min.mjs`, `pdf_viewer.mjs` e `leitor.js` como módulos, nessa ordem, e o `leitor.js` (ESM gerado de `web/paginador/src/legislacao/leitor.ts`, sem empacotar o PDF.js) usa os dois globais; os tipos vêm de `pdfjs-dist/web/pdf_viewer.mjs` só no `typecheck`. Sem `l10n` o `PDFViewer` usa o `GenericL10n` com o pacote de textos embutido (não busca arquivo de idioma: nenhuma requisição fora da lista).
- **Pesquisa na lei aberta**: `PDFFindController` com `updateMatchesCountOnProgress: false` e `delay: 0`, `caseSensitive: false`, `matchDiacritics: false`, `highlightAll: true`, sempre `type: "again"` (termo novo digitado recomeça na página 1; só o `abrir {pagina, termo}`, ao clicar num resultado, começa na página pedida; o mesmo termo anda com `findPrevious`). A resposta `procurado` sai depois de o controlador varrer todas as páginas (o leitor espera `_firstPageCapability` e todos os `_extractTextPromises`, campos com `_` da 6.4.299, e conta pelo `pageMatches`/`selected` públicos, como o `#requestMatchesCount` interno). Caixa vazia manda uma busca vazia (para o mesmo termo, digitado de novo, recomeçar) e `findbarclose` (tira o realce).
- **Links externos**: `PDFLinkService.externalLinkEnabled = false` ⇒ o PDF.js põe `href=""`, `onclick` que devolve `false` e a dica "Disabled: <url>"; além disso a navegação de nível superior só aceita `leitor.html` uma vez. Links internos (destino no próprio PDF) funcionam. Sem `PDFScriptingManager`: o `/OpenAction` JavaScript do PDF fictício nunca roda (nenhuma violação de CSP, nenhuma requisição fora do host).
- **Escala**: 25% a 400% (o PDF.js vai de 10% a 2.500%; o leitor corta depois de `increaseScale`/`decreaseScale`); "Ajustar largura" = `page-width` (padrão ao abrir), "Página inteira" = `page-fit`; Ctrl+roda é do leitor (o zoom do navegador e o pinch estão desligados).
- **`LeitorPdfJs`** (`Pmvni.Composicao.Web/Legislacao`, novo, fora da lista de arquivos do brief): os comandos do leitor (abrir, fechar, página, marcador, escala, procurar, seleção) sobre um `CoreWebView2` com a sandbox da `SegurancaLegislacao`; o app o põe sobre o WebView2 da página Legislação (`SobreAsync`) e os testes, sobre um controller oculto (`CriarOcultoAsync`). Abrir registra o PDF no `ServidorLegislacao` com um token novo e tira o registro do anterior; fechar (inclusive quando a lei é excluída) tira o registro: a URL passa a 403.
- **Mensagens**: o extrator e o leitor têm conjuntos próprios em `mensagens.ts` (`TIPOS_HOST`/`TIPOS_WEB` e `TIPOS_HOST_LEITOR`/`TIPOS_WEB_LEITOR`); a `MensagensLegislacao` valida a união no host. O `extrator.js` e o `paginador.js` saíram com os mesmos bytes (o esbuild tira do pacote o que o extrator não usa).
- **Faixa e atalhos**: `Atalhos.Comando(tecla, Modulo)` (a Legislação usa `MapaLegislacao`, as 10 teclas do `leitor.ts`; os outros módulos, o `Mapa` de 16, que não mudou). Os `KeyboardAccelerator`s da janela são os das duas listas, e cada tecla vale o comando do módulo à vista; numa caixa de texto, Ctrl+C é dela.
- **Página atual**: o PDF.js recalcula a página pela parte visível depois de rolar; a última página de um PDF curto não chegava ao alto da tela e o contador voltava à anterior (visto como falha intermitente no teste do marcador "Anexo I"). O `leitor.css` dá à lista de páginas um espaço depois da última (`padding-bottom: calc(100vh - 48px)`), e `irPagina`/`irMarcador` respondem a página pedida (o marcador resolve o destino com `getDestination`/`getPageIndex`).
- **Correção rodada 1 (17b/18)**: nada de exceção escapa dos manipuladores da tela Ajustes › Legislação (mensagem em português, detalhe no registro); a lista na tela é atualizada no lugar, por id (`ObservableCollection`), e espera o usuário sair de uma caixa de título; anexos e estados vêm de `AcervoLegislacao.ListarComEstado` numa só carga; falha do leitor/extrator e de E/S chegam ao usuário com texto fixo (`MensagensAnexo.LeitorFalhou`, `LeitorIndisponivel`, `Ilegivel(nome, Exception)`, motivo pelo tipo da falha). As gravações da legislação seguem fora da fila de comandos: o acervo as serializa (todas sob o mesmo `Mutex` de sessão, inclusive `LimparEntradasOrfas`), com teste de gravações concorrentes. Um leitor que falhou ao iniciar troca o WebView2 por um novo antes de tentar de novo (a sandbox nunca entra duas vezes no mesmo core).

## Onda final de correções (Etapa 2)

- **Registro da execução nunca derruba o agente**: `RegistroExecucao.Acrescentar*`/`Descarregar` não lançam. Um arquivo do registro preso (o
  OneDrive durante o envio, disco cheio) fica pendente na memória (até 32 MB; passou disso, as linhas novas são descartadas e contadas, e uma
  linha `registro: N linha(s) não puderam ser gravadas…` entra quando o arquivo volta a aceitar gravação), com no máximo uma tentativa por
  segundo; o detalhe vai para o log do app e o `execucao.json` ganha `aviso_registro`. O `ExecucaoProcesso` separa a falha de quem recebe a
  linha da falha do canal: o callback que lança não para a leitura (um Codex que escreve num canal que ninguém lê travaria para sempre). O
  resultado continua vindo do `delivery.json` do kit; o pendente do Claude não sai dos ajustes por erro de registro.
- **Codex nunca fica órfão**: o `codex.exe` entra num Job Object do editor com `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (`TarefaDoEditor`), logo
  depois de partir: o editor que termina, até numa queda, leva junto o Codex e os filhos criados depois disso (um neto criado no primeiro
  instante, antes da atribuição, escapa do job; o cancelamento continua matando a árvore). O análise pro do Claude nunca entra no job (continua
  depois de fechar, como manda o desenho §12.7). Fechar a janela: a pergunta das análises roda de novo na vez da fila (uma análise que partiu
  enquanto o Analisar copiava os anexos também pergunta), e o `Closed` cancela todo ChatGPT e impede uma partida que ainda copiava anexos.
  Parar de acompanhar o Claude grava `acompanhamento_interrompido` no `execucao.json` (nunca `cancelada`: a análise do kit segue).
- **Fim em segundo plano** (Ruling FD-I3): o relatório é importado (o `.pmvrel` novo fica na pasta do processo e nos Recentes), mas só abre
  sozinho com o usuário na vista daquela execução, sem relatório aberto e sem despacho em edição; senão, InfoBar "Relatório pronto" com
  "Abrir relatório". As confirmações (descartar, cancelar a análise, excluir) têm o botão "não" como padrão do Enter, e os diálogos são
  mostrados um de cada vez (`Perguntas`).
- **Anexar PDFs a partir da Legislação**: o andamento, o Cancelar e o resumo (com as recusas) aparecem também no módulo Legislação; um segundo
  "Anexar PDFs…" durante uma anexação diz "Uma anexação já está em andamento.". A lei aberta substituída em Ajustes reabre sem trocar de
  módulo (fora da Legislação, ao voltar a ela). F3/Shift+F3 ficam desabilitados com "Em todas as leis".
- **Trava de programas do roteiro** (`TravaDeProgramas`, em `Pmvni.Revisao`): vale para todo programa que o editor inicia — o agente pelo
  `ExecucaoProcesso` e o LibreOffice do despacho pelo `ConversorLibreOffice`. No roteiro a lista é a pasta do agente falso e o `soffice`
  exato que o editor acha; um início só entra em `programas_iniciados` depois de partir de fato, e `fora_do_agente_falso` conta os do
  LibreOffice.
- **Troca atômica da legislação**: `catalogo.json`, cópias e índices trocam de lugar com espera limitada (até ~2 s) quando outro programa
  segura o arquivo por um instante (acesso negado ou violação de compartilhamento: o OneDrive ou um antivírus); outra falha sobe na hora.

## Interface e configurações (Etapa 2, auditoria da interface)

- **Ícones e cores por tema:** as cores próprias ficam em `ResourceDictionary.ThemeDictionaries` (`IconesModulos.xaml`, `CoresTonais.xaml`).
  Código que pinta um elemento troca o `Style` (os estilos usam `{ThemeResource}` e seguem o tema do elemento), nunca lê o pincel de
  `Application.Current.Resources`: o app muda o tema em `Raiz.RequestedTheme`, e a leitura pelo código daria a cor do tema da aplicação.
  O botão tonal ganha as cores de "sobre"/"pressionado" pelo dicionário `CoresTonais.xaml` posto num contêiner só com ele (as chaves
  `ButtonBackground*` valem para o `Button` e o `DropDownButton`).
- **Glifos com nome** (`Logica\Glifos.cs`, escritos como `\uXXXX`): o teste confere que nenhum caractere de uso privado sobrou literal em
  `Faixas.cs`, `Modulos.cs` e `Glifos.cs`.
- **Ctrl+1 … Ctrl+5** entram em `Atalhos.Mapa` e `Atalhos.MapaLegislacao` e nos `Set` ATALHOS de `edicao.ts` e `leitor.ts` (os bundles
  `paginador.js` e `leitor.js` foram gerados de novo); na janela, "1" vira `VirtualKey.Number1`/`NumberPad1` (o `Enum.TryParse` leria o
  valor numérico 1, o botão esquerdo do mouse).
- **Área de avisos:** `AreaDeAvisos` é um `StackPanel` que recolhe as InfoBars abertas além de 2 (continuam `IsOpen`, só ficam
  recolhidas atrás de "+n") e fecha as informativas e de sucesso fecháveis em 8 s; `Tag="fixo"` exclui as que têm botões ou descrevem o
  documento. No roteiro, nada fecha sozinho (as conferências leem o estado das InfoBars).
- **Ajustes:** cada caixa da página grava na hora; `Ajustes.LerOpcoes` aceita só valores das listas (o resto, inclusive a chave ausente
  de um arquivo antigo, fica no padrão) e `RestaurarPadrao` preserva recentes, agentes e análises em andamento. "Testar" o LibreOffice
  só confere o arquivo e a versão (nada é executado); "Redefinir perfil" apaga `<dados>\lo` com confirmação e nunca com um despacho em
  preparo. A janela lembrada só é usada se ainda cabe num monitor (`DisplayArea.GetFromRect`), e nunca no roteiro.

### Testes que já falharam de vez em quando

- Corrigidos nesta onda (pelo sinal, não pelo relógio): `PerfilLibreOfficeTestes.filho_que_segura…` e `cancelar_com_o_filho_preso…`
  (a prova é o `fim.txt` do filho ainda ausente quando a conversão volta; o teto de tempo, largo, só pega um travamento);
  `OrquestradorTestes.claude_iniciar_acompanhar_e_resultado` (a etapa lida é uma amostra: vale a etapa lida ou o evento "Etapa X" que o
  kit registrou); `AcervoTestes.gravacoes_concorrentes…` (a troca atômica espera o OneDrive soltar o arquivo).
- Aceitos e registrados (a revisão final os manteve): o host WebView2 que às vezes demora a subir no primeiro teste da tela, `DiarioTestes`
  e `cancelar_claude…` (dependem de relógio e de processo). Cada um passou sozinho e na rodada seguinte da suíte inteira.

### Verificações humanas pendentes

- A variável `CODEX_API_KEY` no modo chave do ChatGPT (a ajuda do `codex exec` não a cita; o binário a lê): conferir na primeira análise
  real com chave (erro claro se a chave não valer).
- `EhLink` com arquivos "só online" do OneDrive (placeholders): conferir na árvore real de PROCESSOS que um placeholder não é tratado como
  atalho (o lado seguro é recusar).
- Codex e Claude reais (o construtor nunca roda IA real): a conferência humana do `docs\entrega-etapa-2.md`.

## Linhas das tabelas editáveis (correções de 15/11/2026, frente 5)

- **O que se edita:** cada linha de ÍNDICES URBANÍSTICOS (`indices.linhas`) e de ILUMINAÇÃO E VENTILAÇÃO (`iluminacao.ambientes`) se
  revisa como um item: os textos da linha (todo texto não vazio do original, menos `icone`, `situacao` e chaves `_…`) e a situação.
  Cálculos ("711,82 ÷ 44.998,00 × 100") são texto: nada é recalculado. A linha não se exclui nem se reordena.
- **Id estável:** bloco e posição no original recebido (`indices/02`, `ambientes/03`), o mesmo número que a linha imprime ("ÍNDICE 02").
  O original nunca muda, então a posição é estável; não há id gravado no `documento` (pacotes antigos abrem sem migração).
- **Campo = caminho** do texto na linha: `calculo`, `detalhes/0`, `vaos/1/medidas`, `minimo/0/texto`, `textos/2`, `resultado/total`.
- **Comandos:** os mesmos dos itens (`DefinirSituacao`, `DefinirJustificativa`, `DefinirCampo`, `RestaurarCampo`, `RestaurarItemOriginal`)
  com o id da linha no lugar do item; `Motor` repassa a `MotorLinhas` (`src/Pmvni.Revisao/Linhas.cs`). Situações da linha são as do kit
  para tabelas (`CONTA_CONFERE`, `NAO_ATENDE`, `PENDENTE`, `DIVERGENCIA`); a tela usa as de item com o mesmo texto impresso
  (`TabelasRevisao.SituacaoDeLinha`/`SituacaoDeItem`). Situação mudada sem justificativa é erro da conferência final, como nos itens.
- **Projeto:** `Projeto.Linhas` guarda só a revisão; `projeto.json` ganha a chave `linhas` só quando há alguma (sem revisão de tabela,
  o arquivo fica byte a byte como antes). `DadosEfetivos` troca os valores no lugar (mesma ordem de chaves): sem edições, o HTML é o do kit.
- **Tela:** `web/paginador/src/linhas.ts` marca `tr[data-pmvni-linha]` e os `data-campo` só no DOM da tela. Dois textos no mesmo elemento
  ("J3 • 2,00 × 1,70 m", "ÁREA: 3,40 m²") são separados por `<pmvni-trecho>` (inline e sem estilo do kit: o CSS do kit faz
  `.vao span { display: block }`, e um `span` ali mudava a medida da tabela).
