# Etapa 2 — Revisão básica e controles: plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Objetivo (Goal):** revisar o relatório da IA dentro do app — pacote `.pmvrel` com original imutável, edição de situação,
inclusão/exclusão e textos dos itens no próprio A4, desfazer/refazer, histórico, autosave/recuperação/trava — com os
controles do app (barra de título, trilho de módulos, faixa de opções por módulo, painéis) e os módulos Processos,
Despachos, Legislação e Ajustes, sem perder a fidelidade da Etapa 1.

**Arquitetura (Architecture):** o host é o dono do estado. Um projeto novo, `Pmvni.Revisao` (net10.0 puro), guarda o
modelo revisado, os comandos, o histórico, o pacote ZIP, a trava, o diário e a leitura das pastas. Cada comando gera os
**dados efetivos** (um `dados.json` no formato do kit) que passam pelo mesmo `RelatorioRenderer` + paginador da Etapa 1
(abordagem A do desenho §4): mesmos dados efetivos ⇒ mesmo HTML ⇒ mesmo PDF. A página web só desenha seleção, marcas,
edição e pesquisa **fora do fluxo** das folhas e conversa com o host pelo envelope `{v:1,tipo,id,dados}`.

**Tecnologia (Tech Stack):** .NET 10, Windows App SDK 1.8.260921001 (WinUI 3: `TitleBar`, `SelectorBar`, `CommandBar`,
`InfoBar`, `MenuFlyout`, `ContentDialog`), WebView2 (projeção WinRT), `System.IO.Compression`, `System.Text.Json.Nodes`,
xUnit 2.9.3, TypeScript + esbuild (`web/paginador`), Python 3.14 + PyMuPDF (só `tools/fidelidade.py`); despachos:
`System.IO.Compression` + `System.Xml.Linq` (DOCX), LibreOffice instalado pelo kit (`soffice.com`, só o PDF do despacho),
`Windows.Data.Pdf` (páginas na conferência); legislação embutida (T17–T18): Python + PyMuPDF + Pillow só em
`tools/legislacao` (rodado pelo desenvolvedor quando o kit muda; o build não precisa deles), projeto `Pmvni.Legislacao`.

**Especificação (Spec):** `docs/superpowers/specs/2026-11-14-etapa-2-revisao-basica-design.md` (vinculante, inclusive §8,
§9, §9.1, §10 e §11); contexto em `docs/especificacao/2026-11-14-editor-relatorios-concepcao.md` §3, §5.4, §6, §10, §11. Formato e
convenções: `docs/superpowers/plans/2026-11-14-etapa-1-fidelidade-documental.md`.

**Ordem de execução:** Tarefas 1 a 13, depois **15 e 16** (novo despacho, desenho §10), depois **17 e 18** (legislação
compilada embutida, desenho §11) e, **por último, a 14** (roteiro completo, entrega, Recriar PC), que cobre também o que
as Tarefas 15 a 18 acrescentam.

## Restrições globais (Global Constraints)

- **Executável único** self-contained (`Aplicativo\Editor de Relatorios PMVNI.exe`), manifesto `asInvoker`; sem admin, serviço, tarefa agendada, `HKLM`, `Program Files`, associação de arquivo ou permissão nova.
- WebView2 **InPrivate**, host virtual `doc.pmvni.invalid` com `CoreWebView2HostResourceAccessKind.Deny`, CSP e filtro 403 da Etapa 1 intactos; atalhos do navegador desligados. **Todo HTML do relatório é conteúdo não confiável**: texto editado é texto puro, nunca HTML.
- Nada sai da máquina (sem rede, telemetria ou atualização).
- Dados reais (17098, caminho em `tests/local.json`, ignorado pelo git) só locais: nunca em `tests/corpus`, `tests/referencias`, docs ou commits; saídas guardadas só com contagens, hashes e páginas. Pastas reais de PROCESSOS nunca entram em teste versionado: os testes usam `tests/corpus/processos/` (fictício). Legislação: os testes usam só os PDFs fictícios de `tests/corpus/legislacao/` (montados por `tools/legislacao/ficticios.py`) e o pacote gerado deles; o kit real só é lido no lugar (geração do pacote, conferência, roteiro).
- Testes e ferramentas rodados pelo shell do Claude **não gravam** em `%APPDATA%`, `%LOCALAPPDATA%` nem `%TEMP%` (virtualização MSIX): pasta de dados por `PMVNI_DADOS_DIR` (`PastaDados.Raiz`); temporários de teste em `tests/saida/...` (ignorada pelo git). Testes novos não usam `Path.GetTempPath()`.
- O app e o roteiro só são abertos por `explorer.exe` com `.cmd` (`tools/roteiro.cmd`, `tools/autoteste.cmd`); verificações leem o resultado por arquivo em `tests\saida` / `docs\`.
- Fidelidade: `python -I tools/fidelidade.py tudo` continua aprovando V1–V4 (e as novas V5, Tarefa 4, e V6, despachos × validador do kit, Tarefa 15). O HTML exportado nunca leva `data-campo`, seleção, marcas, chips ou realce: tudo isso existe só no DOM da tela.
- Pacote: `versao_esquema` **"1.0.0"**; limites **50 MB por entrada, 500 MB no total, razão de compressão ≤ 100**; imagens só PNG/JPEG/WebP/GIF conferidas por assinatura; esquema maior que o conhecido ⇒ **somente leitura**.
- Trava: batimento a cada **30 s**; abandonada com batimento **> 2 min**. Autosave: **30 s** sem edição (opções 15/30/60/120 s), ao trocar de arquivo e ao fechar. Recentes: **10**. Meta de latência por comando: **≤ 1,5 s** no `longo` (medida e registrada; não é assert).
- O original recebido (`original/recebido.json`) nunca muda. O editor não preenche nada sozinho. Pastas de processos: leitura apenas; o app só cria nelas o `.pmvrel` de uma importação pedida pelo usuário (e `.bak`/`.tmp`/`.lock` ao lado de um pacote que ele abriu) e, ao gravar um despacho pedido pelo usuário (Tarefas 15–16), a pasta `DESPACHO NN - AAAA-MM-DD` (se nova) com o DOCX, o PDF e o `.recibo-editor.json`, **sem nunca sobrescrever, mover, renomear ou apagar**.
- Kit PMVNI **somente leitura**: modelos DOCX (pelo `TEMPLATES_OFICIAIS.json`, sha conferido) e textos fixos do plugin `despacho` são lidos no lugar, em tempo de uso, e **nunca copiados para o repositório** (licença do plugin; desenho §10.10); o código guarda só âncoras curtas. Testes leem o kit no lugar por `PMVNI_KIT_RAIZ` (padrão `%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)`; ausente ⇒ `Assert.Fail`, como na Etapa 1) e montam DOCX/PDF fictícios no próprio código. O Word **nunca** é automatizado; o PDF do despacho sai pelo LibreOffice sobre uma cópia, com perfil e `TEMP`/`TMP` na pasta de trabalho; LibreOffice real **só no roteiro** (fora do pacote), nunca em `dotnet test`.
- **Legislação embutida** (desenho §11; T17–T18): os atos públicos de `<kit>\03 - LEGISLACAO` (Lei 9.610/98, art. 8º, IV) entram no executável como `legislacao.pmvleg` (recurso de `Pmvni.Legislacao`), **gerado pelo desenvolvedor** com `tools/legislacao/extrair.py` e **versionado**; o `build-release.ps1` não exige Python nem PyMuPDF e só **avisa** quando o kit mudou. Só texto e imagens dos próprios atos (WebP 60, ≤ 1.600 px); **sem OCR e sem texto inventado** (página sem texto fica como imagem, com aviso); texto riscado (revogado) marcado, nunca apresentado como vigente. Normas ABNT **nunca** entram (a ferramenta recusa). Orçamento do pacote: **≤ 11 MB** (aviso; medido ≈ 9,8 MB), **12 MB** no máximo (falha). Viewer: WebView2 InPrivate no host `leg.pmvni.invalid` servido da memória (sem pasta mapeada, CSP própria, resto 403); o app não grava nada da legislação em disco. A ferramenta e os seus testes gravam só em `src/Pmvni.Legislacao/Dados/`, `docs/legislacao-embutida.md` e `tests/saida/legislacao/` (`sys.dont_write_bytecode`, sem `__pycache__`).
- Abrir arquivo externo: só `.pdf .docx .doc .xlsx .xls .png .jpg .jpeg .dwg .dxf .txt`, pelo shell (`UseShellExecute`); o resto é recusado com aviso. No modo `--roteiro`, aberturas externas são só registradas.
- Toda build termina limpando builds anteriores e temporários (já no `tools/build-release.ps1`); tarefas de interface terminam com `build-release.ps1` + roteiro. `recriar.json` e Recriar PC atualizados na entrega (Tarefa 14).
- **Todo texto da interface em português do Brasil.** Nenhum botão "em breve" nem placeholder: um módulo, aba ou comando só aparece quando funciona (desabilitar por contexto é permitido).
- Commits: mensagens em português no estilo do repositório (`feat:`, `fix:`, `docs:`, `test:`), terminando com a linha `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Foco de revisão (Review Focus)

1. **Gravação interrompida** (OneDrive segurando o arquivo, disco cheio, queda no meio do `File.Replace`) → o `.pmvrel` antigo fica intacto, o `.tmp` some, o estado vira "Não salvo" e o diário continua com tudo. Testes na Tarefa 4 (`gravacao_que_falha_antes_da_troca_deixa_o_antigo_intacto`, `destino_preso_por_outro_processo_nao_corrompe`) e na Tarefa 10 (roteiro `20_gravacao_falha_sem_perder`).
2. **Texto editado com quebras de linha, espaços nas pontas, `& < > " '`, emoji e HTML colado** → vira texto puro, uma linha, escapado pelo kit; nada vira marcação. Testes na Tarefa 3 (`normalizar_texto_editado`) e na Tarefa 7 (`colar_html_vira_texto_puro`).
3. **Excluir o último item incluído ou esvaziar um campo que o kit exige** → comando recusado com a mensagem do kit; a tela e o modelo não mudam. Testes na Tarefa 3 (`excluir_o_ultimo_incluido_e_recusado`) e na Tarefa 6 (`edicao_que_a_conferencia_do_kit_recusa_lanca_recomposicao_recusada`).
4. **Citações que parecem "item NN" mas não são** ("item 3 do art. 5", "itens 03 a 07", "item 99" com 60 itens, "subitem 2", "item 3.2") → ficam literais; só "item NN" inequívoco vira token, e a impressão mantém caixa e dígitos. Teste na Tarefa 1 (`citacoes_ambiguas_ficam_literais`).
5. **Mesmo pacote aberto em duas janelas / alterado pelo OneDrive enquanto aberto** → a segunda abre só leitura; a gravação detecta a mudança e não sobrescreve (Conflito). Testes na Tarefa 4 (`conflito_detectado_nao_sobrescreve`), Tarefa 5 (`trava_viva_impede_segunda_instancia`) e roteiro `22_trava_e_somente_leitura`/`23_conflito_nao_sobrescreve` (Tarefa 10).

---

## Estrutura de arquivos (File Structure)

```
src/Pmvni.Revisao/                        NOVO, net10.0 puro (referencia Pmvni.Composicao); sem UI, sem WebView2
  Pmvni.Revisao.csproj                    <InternalsVisibleTo Include="Pmvni.Revisao.Testes" />
  JsonFiel.cs                             JSON ↔ JsonNode com a semântica do json.loads (números crus, chave repetida)
  IdItem.cs                               "it_" + ULID (26, Crockford)
  Modelo.cs                               Projeto, ItemRevisao, SituacaoItem, CampoTexto, Campos
  ProjetoJson.cs  Manifesto.cs            (de)serialização de projeto.json e manifesto.json
  Citacoes.cs                             "item NN" → {{ref:it_…:L}} e impressão
  Importador.cs                           dados.json do kit → ConteudoPacote
  DadosEfetivos.cs                        Projeto → dados.json efetivo; refs de sessão
  Historico.cs                            RegistroHistorico, historico.jsonl, DescricaoHistorico
  Comandos.cs  SessaoRevisao.cs           comandos, desfazer/refazer, reaplicação
  TextoEditado.cs  ConferenciaRevisao.cs  Pendencias.cs  Comparacao.cs
  Pacote.cs                               ZIP: leitura segura, gravação segura, EstadoDisco, ConteudoPacote
  Trava.cs  Diario.cs                     .~nome.pmvrel.lock; <dados>\recuperacao\<sha>.jsonl
  Pastas/NomesArquivo.cs                  nomes de importação e de cópia sem sobrescrever
  Pastas/Processos.cs  Pastas/Despachos.cs  Pastas/AbrirExterno.cs   (Pastas/Legislacao.cs saiu: desenho §11)
  Despachos/RegistroModelos.cs            NOVO (T15): TEMPLATES_OFICIAIS.json → modelos de correção e aprovação (sha conferido)
  Despachos/TextosFixos.cs                NOVO (T15): parágrafos fixos lidos das referências do plugin despacho (no lugar)
  Despachos/ConteudoDespacho.cs           NOVO (T15): Projeto → RascunhoDespacho (cabeçalho, exigências, lacunas), Faltando, Bloqueios
  Despachos/Etapas.cs  Despachos/NomesDespacho.cs   NOVOS (T15): DESPACHO NN - AAAA-MM-DD, data por extenso, nomes sem sobrescrever
  Despachos/PreenchimentoDocx.cs          NOVO (T15): ZipArchive + XLinq; só word/document.xml muda
  Despachos/ValidacaoDespacho.cs          NOVO (T15): porta da comparação do validate_template_fidelity.py + conteúdo
  Despachos/ConversorPdf.cs               NOVO (T15): IConversorPdf, ConversorLibreOffice (soffice.com sobre cópia)
  Despachos/EmissaoDespacho.cs  Despachos/ReciboDespacho.cs   NOVOS (T15): preparar (trabalho) → gravar (pasta do processo) + recibo
src/Pmvni.Legislacao/                     NOVO (T17), net10.0 puro (sem UI, sem WebView2)
  Pmvni.Legislacao.csproj                 EmbeddedResource Dados\legislacao.pmvleg e web\legislacao.js; InternalsVisibleTo testes
  Modelo.cs  PacoteLegislacao.cs          NOVOS (T17): manifesto, leis, blocos; leitura segura do ZIP (memória)
  ConferenciaKit.cs                       NOVO (T17): sha do kit × manifesto; Aviso e Resumo
  Busca.cs  Citacao.cs                    NOVOS (T18): dispositivo ("art. 45 pdm"), texto livre, citação
  HtmlLei.cs  ServidorLegislacao.cs       NOVOS (T18): JSON → HTML escapado; respostas do host leg.pmvni.invalid
  Dados/legislacao.pmvleg                 GERADO por tools/legislacao (versionado, ≈ 10 MB)
  web/legislacao.js                       GERADO por web/paginador (versionado)
src/Pmvni.Composicao/InfoPdf.cs           MOVIDO (T15) de Pmvni.Composicao.Web/Exportador.cs, + InfoPdf.Fontes
src/Pmvni.Composicao.Web/
  PastaTrabalho.cs                        NOVO: <dados>\edicao\<guid> com os assets do pacote
  Recompositor.cs                         NOVO: Projeto → render (refs de sessão) → composição
  Exportador.cs  ConfigFolhas.cs  Ponte/Mensagens.cs   (alterados)
src/Pmvni.Composicao.Web/web/paginador.js gerado (commitado)
web/paginador/src/
  mensagens.ts                            tipos novos
  edicao.ts                               NOVO: seleção, data-campo, edição no A4, âncora, atalhos, menu de contexto
  marcas.ts                               NOVO: camada de marcas de revisão fora das folhas
  pesquisa.ts                             NOVO: realce por CSS Custom Highlight API
  tela.ts  index.ts                       (registram os novos manipuladores)
  legislacao/index.ts  legislacao/mensagens.ts   NOVOS (T18): rolar, realçar e seleção no viewer da legislação
web/paginador/build.mjs                   (T18) segunda entrada → src/Pmvni.Legislacao/web/legislacao.js
src/Pmvni.App/
  Logica/*.cs                             NOVO, sem tipos WinUI (compilados também nos testes): Modulos, Faixas,
                                          ComandosApp, Ajustes, Atalhos, TituloJanela, EstadoGravacao, FluxoNovoDespacho (T16),
                                          ArvoreLegislacao (T18)
  Controles/FaixaDeOpcoes.xaml(.cs)       NOVO: SelectorBar + CommandBar montados de uma DefinicaoFaixa
  Controles/Trilho.xaml(.cs)              NOVO: trilho de módulos (ícone + rótulo, estilo Outlook/Store)
  Paginas/PaginaRelatorios.xaml(.cs)      NOVO: painel (Seções | Excluídos | Histórico), WebView2, Propriedades
  Paginas/PaginaProcessos|PaginaDespachos|PaginaAjustes.xaml(.cs)   NOVOS (Tarefas 11 e 13)
  Paginas/PaginaLegislacao.xaml(.cs)      NOVO (T18): árvore nativa + WebView2 do texto + resultados da pesquisa
  Legislacao/SessaoLegislacao.cs  Legislacao/SegurancaLegislacao.cs   NOVOS (T18): pacote embutido, conferência, sandbox
  AreaTransferencia.cs                    NOVO (T18): copiar citação/texto (no roteiro só registra)
  Autoteste.cs                            (T17) bloco "legislacao" no JSON do autoteste
  Paginas/PaginaNovoDespacho.xaml(.cs)    NOVO (T16): origem → prévia editável → conferência das páginas → gravar
  Despachos/SessaoNovoDespacho.cs         NOVO (T16): liga RascunhoDespacho, EmissaoDespacho, Windows.Data.Pdf e a página
  Edicao/SessaoEdicao.cs                  NOVO: liga SessaoRevisao, Recompositor, Pacote, Trava, Diário e a tela
  Perguntas.cs                            NOVO: IPerguntas (diálogos e seletores; o roteiro troca por respostas fixas)
  MainWindow.xaml(.cs)                    vira o "shell": barra de título, trilho, faixa, páginas
  Roteiro.cs  Argumentos.cs  ItemNavegacao.cs  Estilos.xaml  (alterados)
src/Pmvni.Verificador/                    html --via-pacote (Tarefa 4); despacho (Tarefa 15)
tests/Pmvni.Revisao.Testes/               NOVO, xUnit net10.0; liga (Compile Link) src/Pmvni.App/Logica/*.cs
  Ambiente.cs                             PastaDeTeste(nome) em tests/saida/revisao/<nome>-<guid>, apagada no Dispose; RaizKit (T15)
  Ficticios/DocxFicticio.cs  Ficticios/PdfFicticio.cs  Ficticios/RelatorioFicticio.cs   NOVOS (T15): montados no código
  Despachos/*Testes.cs                    NOVOS (T15): registro, textos fixos, conteúdo, etapas/nomes, DOCX, validação, emissão
tests/Pmvni.Web.Testes/                   RecomposicaoTestes.cs, EdicaoTelaTestes.cs, PesquisaTelaTestes.cs (novos)
tests/Pmvni.Legislacao.Testes/            NOVO (T17–T18), xUnit net10.0: pacote, conferência, embutido, busca, citação, HTML, servidor
tests/corpus/processos/                   árvore fictícia (Tarefa 12)
tests/corpus/legislacao/                  NOVO (T17): kit fictício (lei, NT, NBR e .docx fictícios), catalogo.json, ficticia.pmvleg
tools/legislacao/                         NOVO (T17): extrair.py (gerar|conferir), catalogo.json, ficticios.py, testes_extrair.py
docs/legislacao-embutida.md               GERADO (T17): o que entrou, contagens, o que ficou fora e por quê
tools/fidelidade.py                       subcomandos v5 e v6 (despacho × validador do kit, T15); tudo = v1..v3, v5, v6
tools/roteiro.ps1  tools/roteiro-e-esperar.ps1 (novo)
docs/roteiro-etapa-2.md  docs/roteiro-etapa-2.resultado.json  docs/entrega-etapa-2.md  docs/notas-tecnicas.md
```

`EditorRelatoriosPMVNI.sln` ganha `Pmvni.Revisao` e `Pmvni.Revisao.Testes`; `Pmvni.Composicao.Web`, `Pmvni.App` e
`Pmvni.Verificador` referenciam `Pmvni.Revisao`. O `build-release.ps1` já roda `dotnet test` na solução e limpa
`bin`/`obj` de todo projeto sob `src` e `tests` (a Tarefa 17 só acrescenta a linha de resultado e o aviso da legislação
embutida, lidos do JSON do autoteste). A Tarefa 17 acrescenta `Pmvni.Legislacao` e `Pmvni.Legislacao.Testes` à solução;
`Pmvni.App` referencia `Pmvni.Legislacao`.

Formato do `projeto.json` (decisão do plano; segue o desenho §5.1 e §8.2):

```json
{ "versao_esquema": "1.0.0",
  "itens": [ { "id": "it_01JD3Q7Z8K0000000000000000", "ordem": 1, "numero_original": 1, "incluido": true,
               "chaves": ["titulo", "situacao", "verificacao", "providencia", "base_legal", "referencia", "id"],
               "situacao": { "original": "NAO_COMPROVADO", "revisada": "ATENDE", "justificativa": "…" },
               "campos": { "titulo": { "original": "…", "revisado": null },
                           "referencia": { "revisado": "campo criado pela edição (sem \"original\" = ausente no original)" } },
               "outros": { "id": "ID-DO-MANIFESTO-DO-PLUGIN" } } ],
  "documento": { "config": {}, "capa": {}, "itens": [], "conclusao": "… item {{ref:it_…:2}} …" } }
```

`chaves` guarda a ordem original das chaves do item; `documento` é o resto do `dados.json` como veio (a chave `itens`
fica no lugar original com `[]`); `campos.<c>.original` e `documento.conclusao` já levam os tokens de citação.

---

### Task 1 (Tarefa 1): Projeto Pmvni.Revisao — modelo, JSON fiel, importação e citações

**Files:**
- Create: `src/Pmvni.Revisao/{Pmvni.Revisao.csproj,JsonFiel.cs,IdItem.cs,Modelo.cs,ProjetoJson.cs,Manifesto.cs,Citacoes.cs,Importador.cs,Historico.cs}` (Historico só com `RegistroHistorico` e jsonl nesta tarefa)
- Create: `tests/Pmvni.Revisao.Testes/{Pmvni.Revisao.Testes.csproj,Ambiente.cs,JsonFielTestes.cs,CitacoesTestes.cs,ImportacaoTestes.cs}`
- Modify: `EditorRelatoriosPMVNI.sln`

**Interfaces:**
- Consumes: `RelatorioRenderer.Renderizar(byte[], string, OpcoesRender, ModeloCarregado)`, `ModeloOficial.Carregar()`, `ModeloCarregado.Sha256`, `Situacoes.SitItem`.
- Produces:
  - `static JsonObject JsonFiel.Ler(byte[] utf8)` (aceita BOM; UTF-8 estrito; o que `JsonModelo.Ler` aceita; chave repetida: último valor na posição da primeira; números com o texto cru); `static byte[] JsonFiel.Escrever(JsonNode no)` (UTF-8 sem BOM, `UnsafeRelaxedJsonEscaping`).
  - `static string IdItem.Novo(DateTimeOffset agora, RandomNumberGenerator rng)`; `static bool IdItem.Valido(string id)` (regex `^it_[0-9A-HJKMNP-TV-Z]{26}$`).
  - `sealed class Projeto { string VersaoEsquema; List<ItemRevisao> Itens; JsonObject Documento; Projeto Clonar(); ItemRevisao? Item(string id); IReadOnlyList<ItemRevisao> Incluidos /* por Ordem */; int? NumeroAtual(string id) /* 1-based entre os incluídos */ }`.
  - `sealed class ItemRevisao { string Id; int Ordem; int NumeroOriginal; bool Incluido; List<string> Chaves; SituacaoItem Situacao; Dictionary<string, CampoTexto> Campos; JsonObject Outros }`; `sealed class SituacaoItem { JsonNode? Original; string? Revisada; string? Justificativa }`; `sealed class CampoTexto { JsonNode? Original; bool Ausente; string? Revisado }`; `static class Campos { IReadOnlyList<string> Editaveis = ["titulo","verificacao","providencia","base_legal","referencia"]; IReadOnlySet<string> PodemFicarVazios = {"providencia","base_legal","referencia"} }`.
  - `static Projeto ProjetoJson.Ler(byte[])`, `static byte[] ProjetoJson.Escrever(Projeto)` (formato acima).
  - `sealed record Manifesto(string VersaoEsquema, string AppVersao, int TemplateRevisao, string Sha256Css, DateTimeOffset CriadoEm, IReadOnlyDictionary<string,string> Partes)`, `const string Manifesto.VersaoEsquemaAtual = "1.0.0"`, `Ler(byte[])`/`Escrever()`.
  - `sealed record RegistroHistorico(int N, DateTimeOffset DataUtc, string Usuario, string Tipo, string? Item, string? Campo, JsonNode? Antes, JsonNode? Depois, int? Ref)`; `static class HistoricoJsonl { IReadOnlyList<RegistroHistorico> Ler(byte[]); byte[] Escrever(IEnumerable<RegistroHistorico>); string Linha(RegistroHistorico) }` (chaves da linha: `n, data_utc, usuario, tipo, alvo{item,campo}, antes, depois, ref`).
  - `sealed record ContextoUsuario(string Usuario, Func<DateTimeOffset> Relogio, RandomNumberGenerator Rng)`; `static ContextoUsuario ContextoUsuario.Atual()` (`Environment.UserName`, sem domínio).
  - `sealed record ConteudoPacote(Manifesto Manifesto, byte[] Recebido, Projeto Projeto, IReadOnlyList<RegistroHistorico> Historico, IReadOnlyDictionary<string, byte[]> Assets)` (chave = `assets/<sha256>.<ext>`) com `void ExtrairAssets(string pasta)`.
  - `static ConteudoPacote Importador.Importar(byte[] dadosJson, string caminhoDados, ModeloCarregado m, ContextoUsuario u)`; `sealed class ImportacaoRecusadaException(IReadOnlyList<string> Erros)`.
  - `static class Citacoes { (string Texto, IReadOnlyList<CitacaoConvertida> Convertidas) Converter(string texto, IReadOnlyList<string> idsPorNumero); string Imprimir(string texto, Func<string,int?> numeroAtual, Func<string,int> numeroOriginal); IReadOnlyList<string> LiteraisSuspeitos(string texto) }`; `sealed record CitacaoConvertida(string Trecho, int Numero, string Alvo)`. Token: `{{ref:<id>:<largura>}}` substitui **só os dígitos** (a palavra "item/ITEM/Item" fica literal, logo a caixa se mantém); `largura` = nº de dígitos do original; impressão = número atual com zeros à esquerda até `largura`; alvo excluído ⇒ imprime o `numero_original` dele.

Regra de conversão (desenho §8.5), um só lugar em `Citacoes.cs`:

```
(?<![\p{L}\p{N}])(item|ITEM|Item)\s+(\d{1,3})(?![\p{L}\p{N}])(?![.,]\d)(?!\s*[,;]?\s*(?:do|da|de|dos|das)\b)
```
com `1 ≤ NN ≤ número de itens`. `LiteraisSuspeitos` acha `(?i)\bitens?\s+\d{1,3}` que **não** virou token.

Importação: lê com `JsonFiel`; renderiza em rascunho (`OpcoesRender(false, true)`) e recusa com os erros do kit se houver;
cria um `ItemRevisao` por item (ids novos, `ordem`=`numero_original`=posição); converte citações nos 5 campos de cada
item e em `documento.conclusao` (se for string); copia cada imagem citada (`localizacao.imagem`, `figuras[].imagem`)
para `assets/<sha256>.<ext>` (extensão pela assinatura: PNG `89 50 4E 47 0D 0A 1A 0A`, JPEG `FF D8 FF`, GIF `GIF87a`/`GIF89a`,
WebP `RIFF????WEBP`; outra assinatura ⇒ recusa) e reescreve o caminho no `documento`; histórico começa com o registro
`N=1, Tipo="ImportarCitacoes"`, `Depois=[{item,campo,trecho,alvo}…]`; `Recebido` = bytes exatos recebidos.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] void numeros_preservam_o_texto()            // {"a":12.0,"b":1e-05,"c":123456789012345678901234567890}
    => Assert.Equal("{\"a\":12.0,\"b\":1e-05,\"c\":123456789012345678901234567890}", Utf8(JsonFiel.Escrever(JsonFiel.Ler(B(...)))));
[Fact] void chave_repetida_vale_a_ultima_na_posicao_da_primeira()   // {"x":1,"y":2,"x":3} → {"x":3,"y":2}
[Fact] void bom_aceito_e_utf8_invalido_recusado()
[Fact] void id_novo_tem_formato_ulid()             // Assert.Matches("^it_[0-9A-HJKMNP-TV-Z]{26}$", id); dois ids no mesmo ms diferem
[Theory] // Converter com 60 itens
[InlineData("Ver o item 07.", "Ver o item {{ref:#7:2}}.")]
[InlineData("ITEM 3 e Item 12", "ITEM {{ref:#3:1}} e Item {{ref:#12:2}}")]
void citacao_inequivoca_vira_token(string de, string para)   // #N = idsPorNumero[N-1]
[Theory]
[InlineData("item 3 do art. 5")] [InlineData("itens 03 a 07")] [InlineData("item 99")] [InlineData("subitem 2")]
[InlineData("item 3.2")] [InlineData("item 0")] [InlineData("item 4 da NBR 9050")]
void citacoes_ambiguas_ficam_literais(string texto)          // Converter devolve o mesmo texto e 0 convertidas
[Fact] void imprimir_respeita_largura_e_excluido()           // "item {{ref:A:2}}" com A atual=3 → "item 03"; A excluído com original 7 → "item 07"
[Fact] void importar_corpus_curto_cria_itens_com_ids_e_original_intacto()
    // 3 itens; Recebido == bytes do arquivo; ordem 1..3; incluido; campos.titulo.original == texto do curto; Revisado null
[Fact] void importar_extremos_preserva_chaves_e_campos_extras()   // Chaves do 1º item termina em "id"; Outros["id"] preservado
[Fact] void importar_longo_copia_imagens_para_assets_e_reescreve_caminhos()
    // 9 assets (mapa + 8 figuras); documento.figuras[0].imagem == "assets/<sha>.png"; sha do nome == sha dos bytes
[Fact] void imagem_com_assinatura_desconhecida_recusa_importacao()
[Fact] void dados_com_erro_de_conferencia_nao_importam()      // capa removida → ImportacaoRecusadaException com "Campo obrigatório ausente: capa"
[Fact] void historico_comeca_com_importar_citacoes()          // Historico[0].N == 1, Tipo == "ImportarCitacoes"
[Fact] void projeto_json_ida_e_volta()                       // Escrever(Ler(Escrever(p))) byte a byte igual
```

Testes gravam só em `Ambiente.PastaDeTeste(...)` (`tests/saida/revisao/...`); o corpus é só lido.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Pmvni.Revisao.Testes -c Debug --nologo`
Expected: FAIL (compilação: `JsonFiel`, `Importador`… não existem).

- [ ] **Step 3: Implement** os tipos acima em `src/Pmvni.Revisao` (csproj net10.0 como `Pmvni.Composicao.csproj`, sem RID) e adicionar os dois projetos à solução (`dotnet sln add`).

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Pmvni.Revisao.Testes -c Debug --nologo` → Expected: PASS (todos).
Run: `dotnet test tests/Pmvni.Composicao.Testes -c Debug --nologo` → Expected: PASS (nada da Etapa 1 mudou).

- [ ] **Step 5: Commit** — `git add src/Pmvni.Revisao tests/Pmvni.Revisao.Testes EditorRelatoriosPMVNI.sln` · `feat: projeto Pmvni.Revisao com modelo, importacao e citacoes`

### Task 2 (Tarefa 2): Dados efetivos e numeração (HTML byte-idêntico após importar)

**Files:**
- Create: `src/Pmvni.Revisao/DadosEfetivos.cs`; Test: `tests/Pmvni.Revisao.Testes/DadosEfetivosTestes.cs`

**Interfaces:**
- Consumes: Tarefa 1 (`Projeto`, `Citacoes.Imprimir`, `ConteudoPacote.ExtrairAssets`, `JsonFiel.Escrever`).
- Produces:
  - `sealed record DadosGerados(byte[] Json, IReadOnlyList<string> IdsDosItens)` (ids na ordem em que os itens saem).
  - `static DadosGerados DadosEfetivos.Gerar(Projeto p)`: itens com `Incluido` na `Ordem`; cada item com as chaves na ordem de `Chaves` (campo revisado ⇒ `Revisado`; senão o `Original` como veio, ausente continua ausente, null continua null; campo criado pela edição vai ao fim); `situacao` = `Revisada ?? Original`; tokens impressos por `Citacoes.Imprimir` nos 5 campos e na `conclusao`; `documento` como veio, com `itens` no lugar original.
  - `static string? DadosEfetivos.TextoEfetivo(Projeto p, ItemRevisao it, string campo)` (texto impresso; null se ausente).
  - `static ResultadoRender DadosEfetivos.RefsDeSessao(ResultadoRender r, IReadOnlyList<string> idsDosItens)`: troca o `Id` de cada `RefElemento` `.card` de índice i por `idsDosItens[i]` (o HTML não muda).

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory] [InlineData("curto")] [InlineData("longo")] [InlineData("extremos")] [InlineData("estouro")] [InlineData("sem_opcionais")]
void importado_gera_html_byte_identico(string caso)
    // ExtrairAssets(pasta); Renderizar(Gerar(p).Json, pasta/"dados.json", (false,true)).Html == Renderizar(corpus/caso.json, (false,true)).Html
[Fact] void dados_modelo_com_instrucoes_gera_html_byte_identico()   // imagens do modelo gravadas na PastaDeTeste; OpcoesRender(true,true)
[Fact] void excluir_renumera_e_citacao_acompanha()
    // curto + texto "ver item 03" no item 1: excluir item 2 → efetivo tem 2 itens; texto do item 1 = "ver item 02"
[Fact] void citacao_a_excluido_imprime_o_numero_original()          // excluir item 3 → "ver item 03" continua
[Fact] void campo_revisado_substitui_e_campo_criado_vai_ao_fim()     // chaves do item na ordem; "referencia" criada é a última
[Fact] void null_continua_null_e_ausente_continua_ausente()
[Fact] void refs_de_sessao_trocam_so_os_cards()                      // .sec e .fig iguais; .card[i].Id == IdsDosItens[i]; Html igual
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter FullyQualifiedName~DadosEfetivosTestes` → Expected: FAIL (`DadosEfetivos` não existe).
- [ ] **Step 3: Implement** `DadosEfetivos` (JsonNode clonado, nunca o original mutado).
- [ ] **Step 4: Run** o mesmo filtro → Expected: PASS (7 testes, 5 casos do corpus).
- [ ] **Step 5: Commit** — `feat: dados efetivos e numeracao coerente`

### Task 3 (Tarefa 3): Comandos, desfazer/refazer, histórico e conferência da revisão

**Files:**
- Create: `src/Pmvni.Revisao/{Comandos.cs,SessaoRevisao.cs,TextoEditado.cs,ConferenciaRevisao.cs,Pendencias.cs}`; Modify: `Historico.cs` (`DescricaoHistorico`)
- Test: `tests/Pmvni.Revisao.Testes/{ComandosTestes.cs,ConferenciaRevisaoTestes.cs}`

**Interfaces:**
- Consumes: Tarefas 1–2.
- Produces:
  - `abstract record Comando(string Item)`; `DefinirSituacao(string Item, string Situacao)`, `DefinirJustificativa(string Item, string? Texto)`, `DefinirCampo(string Item, string Campo, string Texto)`, `RestaurarCampo(string Item, string Campo)`, `Excluir(string Item)`, `Restaurar(string Item)`, `RestaurarItemOriginal(string Item)`; `sealed class ComandoRecusadoException(string mensagem)`.
  - `sealed class SessaoRevisao(Projeto projeto, IReadOnlyList<RegistroHistorico> historico, ContextoUsuario u)` com `Projeto Projeto`, `Projeto Testar(Comando c)` (aplica num clone, sem registro), `RegistroHistorico? Aplicar(Comando c)` (null = sem efeito, não registra), `RegistroHistorico? Desfazer()`, `RegistroHistorico? Refazer()`, `bool PodeDesfazer`, `bool PodeRefazer`, `IReadOnlyList<RegistroHistorico> HistoricoCompleto`, `IReadOnlyList<RegistroHistorico> Pendentes` (desde a última gravação), `void MarcarGravado()`, `void Reaplicar(IEnumerable<RegistroHistorico> registros)` (diário), `event Action<RegistroHistorico>? Registrado`.
  - `static string TextoEditado.Normalizar(string texto)`: cada sequência de `\r\n|\r|\n|\u2028|\u2029` vira um espaço; pontas aparadas; nada mais muda.
  - `static IReadOnlyList<string> ConferenciaRevisao.Erros(Projeto p)`; `Avisos(Projeto p)` (literais suspeitos quando a numeração mudou); `CitacoesAExcluidos(Projeto p)`.
  - `static string? Pendencias.Proximo(Projeto p, string? atual)` (próximo incluído, circular, com situação efetiva ≠ `ATENDE` ou justificativa faltando).
  - `static string DescricaoHistorico.Texto(RegistroHistorico r, Projeto p)`.

Regras (desenho §5.3, §8.1, §8.8): `DefinirSituacao` só aceita `Situacoes.SitItem`; situação igual à original ⇒ `Revisada = null` (justificativa guardada, não exigida). `DefinirCampo` só nos 5 campos; normaliza; `titulo`/`verificacao` vazios ⇒ recusa ("O título não pode ficar vazio." / "A verificação técnica não pode ficar vazia."); igual ao texto efetivo atual ⇒ sem efeito; igual ao original impresso ⇒ `Revisado = null`; depois **reconverte citações** com a numeração atual (decisão do plano: a edição devolve números, e sem isso a citação perderia o vínculo), listando-as em `Depois`. `Excluir` do último incluído ⇒ recusa "O relatório precisa de ao menos um item incluído.". Desfazer grava `Tipo="desfeito", Ref=<N do desfeito>`; refazer grava `Tipo="refeito", Ref=<N>`; nada é apagado do histórico. Erro de justificativa: `"ITEM 03: a situação mudou de FALTA COMPROVAR para ATENDE sem justificativa"` (textos de `Situacoes.Sit`). Aviso de citação: `"ITEM 01 cita o item excluído que era o item 03 (campo verificacao)"`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] void normalizar_texto_editado()   // "  Linha 1\r\n\r\nLinha 2\n " → "Linha 1 Linha 2"; "a & <b> 😀 \"x\" 'y'" inalterado
[Fact] void definir_situacao_registra_antes_e_depois()   // Antes "NAO_COMPROVADO"(efetivo), Depois "ATENDE"; Usuario == u.Usuario
[Fact] void voltar_a_situacao_original_zera_revisada_e_guarda_justificativa()
[Fact] void situacao_invalida_e_recusada()               // "CONTA_CONFERE" → ComandoRecusadoException
[Fact] void titulo_vazio_e_recusado_e_providencia_vazia_passa()
[Fact] void campo_igual_ao_atual_nao_registra()          // Aplicar devolve null; PodeDesfazer false
[Fact] void editar_de_volta_ao_original_zera_revisado()
[Fact] void excluir_o_ultimo_incluido_e_recusado()
[Fact] void desfazer_e_refazer_ilimitados_e_append_only()
    // 20 comandos, 20 desfazer, 20 refazer: Projeto igual (ProjetoJson.Escrever) ao de depois dos 20 comandos;
    // HistoricoCompleto.Count == 1 + 60; registros "desfeito" com Ref apontando para N; N estritamente crescente
[Fact] void novo_comando_limpa_o_refazer()
[Fact] void testar_nao_muda_a_sessao()
[Fact] void reaplicar_registros_reproduz_o_estado()      // outra sessão + Reaplicar(Pendentes) → ProjetoJson igual
[Fact] void restaurar_item_original_zera_campos_e_situacao()
[Fact] void justificativa_faltando_vira_erro_da_conferencia()   // mensagem exata acima
[Fact] void citacao_a_excluido_vira_aviso()
[Fact] void literais_suspeitos_avisados_so_quando_a_numeracao_muda()   // "itens 03 a 07": 0 avisos sem exclusão; 1 com
[Fact] void proximo_pendente_e_circular()
[Fact] void descricao_do_historico()   // "Situação do ITEM 03: FALTA COMPROVAR → ATENDE" ; "Excluído o ITEM 05" ; "Desfeito: …"
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter "FullyQualifiedName~ComandosTestes|FullyQualifiedName~ConferenciaRevisaoTestes"` → Expected: FAIL (tipos ausentes).
- [ ] **Step 3: Implement** (cada comando guarda o `antes` para desfazer; `Testar` usa `Projeto.Clonar()`).
- [ ] **Step 4: Run** o mesmo filtro → Expected: PASS.
- [ ] **Step 5: Commit** — `feat: comandos com desfazer, historico e conferencia da revisao`

### Task 4 (Tarefa 4): Pacote `.pmvrel` — leitura segura, gravação segura, conflito e V5

**Files:**
- Create: `src/Pmvni.Revisao/Pacote.cs`; Test: `tests/Pmvni.Revisao.Testes/PacoteTestes.cs`
- Modify: `src/Pmvni.Verificador/{Argumentos.cs,Program.cs,Pmvni.Verificador.csproj}`, `tests/Pmvni.Web.Testes/VerificadorArgumentosTestes.cs`, `tools/fidelidade.py`, `docs/fidelidade.md`

**Interfaces:**
- Consumes: Tarefas 1–3.
- Produces:
  - `sealed record EstadoDisco(long Tamanho, DateTime GravadoUtc, string Sha256)`; `static EstadoDisco EstadoDisco.Ler(string caminho)`.
  - `sealed record LimitesPacote(long PorEntrada = 52_428_800, long Total = 524_288_000, double RazaoMaxima = 100)`.
  - `sealed record PacoteAberto(ConteudoPacote Conteudo, EstadoDisco Disco, bool SomenteLeitura, string? MotivoSomenteLeitura)`.
  - `static PacoteAberto Pacote.Ler(string caminho, LimitesPacote? limites = null)`; `sealed class PacoteInvalidoException(string mensagem)`.
  - `static EstadoDisco Pacote.Gravar(string caminho, ConteudoPacote c, EstadoDisco? esperado, Action? antesDeTrocar = null)`; `sealed class ConflitoNoDiscoException(EstadoDisco atual)`.

Leitura: só `manifesto.json`, `original/recebido.json`, `projeto.json`, `historico.jsonl`, `assets/<64 hex>.(png|jpg|webp|gif)`;
nome com `..`, começando por `/`/`\`, com `:` ou `\`, repetido ou desconhecido ⇒ `PacoteInvalidoException` (exceto
`versao_esquema` maior, que abre `SomenteLeitura` ignorando o desconhecido); tamanho contado **lendo** (não pelo
cabeçalho) e cortado no limite; razão `descomprimido/comprimido` > 100 ⇒ recusa; sha256 de cada parte = `manifesto.partes`;
asset: assinatura + nome = sha dos bytes. Gravação: `nome.pmvrel.tmp` (oculto) na mesma pasta → `Flush(true)` → reabrir
com `Ler` e comparar → `antesDeTrocar?.Invoke()` (gancho de teste) → se o destino existe: confere `EstadoDisco.Ler` ==
`esperado` (senão `ConflitoNoDiscoException`, nada trocado) e `File.Replace(tmp, destino, nome.pmvrel.bak)`; senão
`File.Move`. Em qualquer exceção o `.tmp` é apagado. Devolve o novo `EstadoDisco`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] void ida_e_volta_preserva_tudo()   // Ler(Gravar(Importar(longo))) : Recebido, projeto.json, historico, 9 assets iguais
[Fact] void gravacao_que_falha_antes_da_troca_deixa_o_antigo_intacto()
    // antesDeTrocar lança IOException → sha do destino igual ao de antes; nenhum *.tmp na pasta
[Fact] void destino_preso_por_outro_processo_nao_corrompe()   // FileStream(destino, FileShare.None) aberto → exceção; antigo intacto
[Fact] void conflito_detectado_nao_sobrescreve()   // outro Gravar muda o arquivo; Gravar com o EstadoDisco antigo → ConflitoNoDiscoException; conteúdo = o do outro
[Fact] void bak_guarda_a_versao_anterior()          // depois de 2 gravações, .bak == bytes da 1ª
[Fact] void tmp_fica_oculto()                       // antesDeTrocar confere FileAttributes.Hidden no .tmp
[Theory] [InlineData("../fora.json")] [InlineData("/abs.json")] [InlineData("c:x.json")] [InlineData("assets\\a.png")] [InlineData("extra.txt")]
void entrada_proibida_recusa(string nome)
[Fact] void entrada_acima_do_limite_recusa()        // LimitesPacote(PorEntrada: 1024) com projeto de 2 KB
[Fact] void bomba_de_compressao_recusa()            // 20 MB de zeros numa entrada → razão > 100
[Fact] void hash_da_parte_diferente_recusa()
[Fact] void asset_com_assinatura_falsa_recusa()
[Fact] void esquema_maior_abre_so_leitura()         // versao_esquema "2.0.0" → SomenteLeitura, Motivo cita "2.0.0"
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter FullyQualifiedName~PacoteTestes` → Expected: FAIL.
- [ ] **Step 3: Implement** `Pacote.cs`.
- [ ] **Step 4: Run** o filtro → Expected: PASS.
- [ ] **Step 5: V5 (dados efetivos × kit, via pacote):** `Pmvni.Verificador html <dados.json> <saida.html> --via-pacote [--instrucoes]` importa, grava `<saida>.pmvrel`, relê, extrai os assets em `<saida>-assets\` e grava o HTML dos dados efetivos; teste `via_pacote_so_no_html` em `VerificadorArgumentosTestes` (aceito em `html`, recusado em `pdf`). `fidelidade.py v5`: como `caso_v1`, com `--via-pacote`, para o corpus + `dados_modelo --instrucoes` + casos de `tests/local.json`; grava `tests/saida/v5.json` só com `{caso, igual, sha256_kit, sha256_editor}`; `tudo` = v1, v2, v3, v5. Registrar a V5 em `docs/fidelidade.md`.
  Run: `python -I tools/fidelidade.py v5` → Expected: `V5 [<caso>] IGUAL` em todos os casos, código 0.
- [ ] **Step 6: Commit** — `feat: pacote pmvrel com leitura e gravacao seguras e V5`

### Task 5 (Tarefa 5): Trava e diário de recuperação

**Files:**
- Create: `src/Pmvni.Revisao/{Trava.cs,Diario.cs}`; Test: `tests/Pmvni.Revisao.Testes/{TravaTestes.cs,DiarioTestes.cs}`

**Interfaces:**
- Consumes: `RegistroHistorico`, `HistoricoJsonl.Linha`, `SessaoRevisao.Reaplicar` (Tarefa 3).
- Produces:
  - `sealed record InfoTrava(string Maquina, string Usuario, int Pid, DateTimeOffset InicioUtc, DateTimeOffset BatimentoUtc)`.
  - `sealed class Trava : IDisposable { static string CaminhoDe(string pacote) /* <pasta>\.~<nome>.pmvrel.lock */; static readonly TimeSpan Batimento = 30 s, Abandono = 2 min; static ResultadoTrava Adquirir(string pacote, InfoTrava eu, Func<DateTimeOffset> relogio); void Bater(); void Dispose() /* apaga só se for a nossa */ }`; `sealed record ResultadoTrava(Trava? Adquirida, InfoTrava? Ocupada)`.
  - `sealed class Diario : IDisposable { static string CaminhoDe(string raizDados, string pacote) /* <raiz>\recuperacao\<sha256 hex do caminho completo em minúsculas>.jsonl */; static Diario Iniciar(string raizDados, string pacote, string sha256Base); void Registrar(RegistroHistorico r) /* acrescenta + Flush(true) */; void Descartar(); static Recuperacao? Ler(string raizDados, string pacote, string sha256Atual); static string Arquivar(string raizDados, string pacote) /* renomeia para <sha>.orfao-<yyyyMMddHHmmss>.jsonl */ }`; `sealed record Recuperacao(IReadOnlyList<RegistroHistorico> Registros, DateTimeOffset UltimoUtc, bool DaMesmaVersao)`.

Trava: `FileMode.CreateNew`, arquivo oculto, JSON com os 5 campos; existente e com batimento ≤ 2 min ⇒ `Ocupada`;
> 2 min ou ilegível com `LastWriteTimeUtc` > 2 min ⇒ apagada e nova tentativa (uma vez). Diário: 1ª linha
`{"tipo":"cabecalho","pacote":…,"sha256_base":…,"iniciado_utc":…}`, depois uma linha por registro; linha final
truncada (queda no meio) é ignorada; `Ler` devolve null sem registros; `DaMesmaVersao` = `sha256_base == sha256Atual`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] void trava_viva_impede_segunda_instancia()    // 2º Adquirir → Ocupada com Usuario/Maquina/Pid do 1º
[Fact] void trava_abandonada_e_retomada()            // relógio +2 min 1 s → Adquirida
[Fact] void batimento_renova_a_trava()               // Bater em +90 s; outro Adquirir em +150 s → Ocupada
[Fact] void dispose_apaga_so_a_propria_trava()
[Fact] void arquivo_de_trava_oculto()
[Fact] void diario_registra_e_le_na_ordem()          // 3 registros → Ler.Registros.Count == 3, DaMesmaVersao
[Fact] void linha_truncada_no_fim_e_ignorada()        // anexar "{\"n\":9," → 3 registros
[Fact] void diario_de_outra_versao_e_marcado()        // sha256Atual diferente → DaMesmaVersao false
[Fact] void diario_aplicado_reproduz_a_sessao()       // 5 comandos + 1 desfazer registrados → nova SessaoRevisao + Reaplicar → ProjetoJson igual
[Fact] void caminho_do_diario_e_estavel_e_sem_dados_do_processo()   // nome = 64 hex + ".jsonl"; maiúsculas/minúsculas do caminho não mudam
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter "FullyQualifiedName~TravaTestes|FullyQualifiedName~DiarioTestes"` → Expected: FAIL.
- [ ] **Step 3: Implement.** - [ ] **Step 4: Run** o filtro → Expected: PASS.
- [ ] **Step 5: Commit** — `feat: trava do pacote e diario de recuperacao`

### Task 6 (Tarefa 6): Recomposição e exportação a partir do pacote

**Files:**
- Create: `src/Pmvni.Composicao.Web/{PastaTrabalho.cs,Recompositor.cs}`; Modify: `Exportador.cs`, `ConfigFolhas.cs`, `Pmvni.Composicao.Web.csproj` (ref. `Pmvni.Revisao`)
- Test: `tests/Pmvni.Web.Testes/RecomposicaoTestes.cs`; Modify: `docs/notas-tecnicas.md` (seção "Latência de um comando")

**Interfaces:**
- Consumes: `DadosEfetivos.Gerar/RefsDeSessao`, `ConferenciaRevisao.Erros/CitacoesAExcluidos`, `ConteudoPacote.ExtrairAssets`, `HostOculto`, `DocumentoWeb.CarregarAsync/ComporDetalhadoAsync`.
- Produces:
  - `sealed class PastaTrabalho : IDisposable { static PastaTrabalho Criar(ConteudoPacote c) /* <PastaDados.Raiz>\edicao\<guid>, órfãs > 1 dia apagadas como em DocumentoWeb.LimparOrfas */; string Caminho; string CaminhoDados /* <Caminho>\dados.json (não precisa existir) */ }`.
  - `static ConfigFolhas ConfigFolhas.PadraoDeBytes(ModeloCarregado m, byte[] dados)` (`PadraoDeDados` passa a usá-la).
  - `sealed record Preparado(byte[] Dados, string CaminhoDados, ResultadoRender Render, ConfigFolhas Config, IReadOnlyList<string> ErrosFinais, IReadOnlyList<string> AvisosExportacao)` — `Render` em rascunho com refs de sessão; `ErrosFinais` = conferência do kit sem rascunho + `ConferenciaRevisao.Erros`; `AvisosExportacao` = `CitacoesAExcluidos`.
  - `sealed class Recompositor(ConteudoPacote pacote, ModeloCarregado m) : IDisposable { Preparado Preparar(Projeto p); Task<ResultadoComposicao> ComporAsync(Preparado e, HostOculto host); string PastaTrabalho }`; `sealed class RecomposicaoRecusadaException(IReadOnlyList<string> erros)` (render em rascunho com erros do kit).
  - `Exportador.ExportarFinalAsync(byte[] bytesDados, string caminhoDados, ConfigFolhas cfg, string caminhoPdf, PlanoQuebras planoTela, ModeloCarregado? modelo = null, string? sha256Pacote = null)`: mesma rota da versão por caminho (que passa a chamá-la); recibo ganha `sha256_pacote` só quando informado; `sha256_dados` = sha dos bytes efetivos.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] async Task pacote_importado_compoe_o_mesmo_plano_que_o_dados_json()   // longo: ParaJson() igual ao do render direto
[Fact] async Task refs_de_sessao_dos_cards_sao_os_ids_estaveis()   // RefsPorFolha: 60 ids "it_…" distintos, nenhum "item-"
[Fact] async Task excluir_item_recompoe_sem_o_item()
    // curto, Excluir(item 2): texto das folhas não contém o título antigo do item 2; "ITEM 02 — " seguido do título do antigo item 3
[Fact] void edicao_que_a_conferencia_do_kit_recusa_lanca_recomposicao_recusada()
    // DefinirCampo(item 1, "providencia", "") → Preparar lança; Erros contém "ITEM 01: campo vazio providencia"
[Fact] async Task exportar_pacote_grava_sha256_do_pacote_e_dos_dados_efetivos()   // recibo: sha256_pacote == informado; sha256_dados == sha(Preparado.Dados)
[Fact] async Task exportacao_recusa_se_o_efetivo_mudou_depois_da_tela()   // plano da tela de antes de um DefinirCampo → ExportacaoDivergenteException.ArquivoMudou
[Fact] async Task tempos_de_recompor_um_comando_do_corpus()
    // curto, longo, extremos: Preparar + ComporAsync + CarregarAsync/AplicarPlanoAsync na "tela"; mediana de 3; grava tests/saida/tempos-comando.json; só mede
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Web.Testes --filter FullyQualifiedName~RecomposicaoTestes` → Expected: FAIL (compilação).
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** o filtro e depois `dotnet test tests/Pmvni.Web.Testes` inteiro → Expected: PASS (inclui todos os testes da Etapa 1).
- [ ] **Step 5:** registrar em `docs/notas-tecnicas.md` a tabela de `tempos-comando.json` (caso, folhas, mediana e rodadas) ao lado da linha de base da Etapa 1. Commit — `feat: recomposicao e exportacao a partir do pacote`

### Task 7 (Tarefa 7): Paginador — seleção, campos, edição no A4, marcas, âncora, atalhos e menu de contexto

**Files:**
- Create: `web/paginador/src/{edicao.ts,marcas.ts}`; Modify: `mensagens.ts`, `tela.ts`, `index.ts`; regenerate `src/Pmvni.Composicao.Web/web/paginador.js`
- Modify: `src/Pmvni.Composicao.Web/Ponte/Mensagens.cs`; Test: `tests/Pmvni.Web.Testes/EdicaoTelaTestes.cs`, `PonteTestes.cs`

**Interfaces:**
- Consumes: `aplicarPlano` (folhas com `data-ref`), `ativar` de `tela.ts`, `DocumentoWeb.ChamarAsync/Recebida`.
- Produces (mensagens; dados em camelCase; `ref` = data-ref; `campo` ∈ os 5 campos):
  - host→web: `estadoEditor {selecao: string|null, campos: bool, marcas: {visiveis: bool, alterados: string[], excluidos: {id, rotulo, ancora, lado: "antes"|"depois"}[]}, ancora?: {ref, deslocamentoPx}}` → `estadoAplicado {camposMarcados, marcasDesenhadas}`; `selecionar {ref: string|null, rolar: bool}` → `selecionado {ref, campo: null}`; `editarCampo {ref, campo, texto}` → `edicaoIniciada {ref, campo}` (erro `campo_inexistente`); `concluirEdicao {}` → `editar {ref, campo, texto}` ou `semEdicao {}`; `obterAncora {}` → `ancora {ref, deslocamentoPx}`.
  - web→host (eventos, id `evento-<n>`): `selecionado {ref, campo}` (clique; `ref` null fora de item), `pedirEdicao {ref, campo}` (duplo clique), `editar {ref, campo, texto}` (Enter), `edicaoCancelada {ref, campo}` (Esc), `atalho {tecla}`, `menuContexto {ref, campo, x, y}` (px CSS da janela = DIP), `excluidoClicado {id, x, y}`.
  - `Mensagens.TiposHostParaWeb`/`TiposWebParaHost` e `TIPOS_HOST`/`TIPOS_WEB` com os mesmos tipos novos.

Comportamento: `data-campo` marcado só no DOM da tela, dentro de cada pedaço `.card[data-ref]`: o texto do título depois
de `span.id` + `" — "` embrulhado num `span[data-campo=titulo]` (inline, sem mudar medida); `p` após o rótulo VERIFICAÇÃO →
`verificacao`; `.prov > p` → `providencia`; `.two > div:nth-child(1) p.m` → `base_legal`; `.two > div:nth-child(2) p.m` →
`referencia`. Seleção: atributo `data-pmvni-selecionado` em todos os pedaços + `outline` (CSS em `@media screen`; outline não
muda layout). Edição: `contenteditable="plaintext-only"` no **primeiro** pedaço do campo, que recebe o `texto` do host;
os demais pedaços do mesmo campo ficam `visibility:hidden`; Enter confirma, Esc restaura o texto e os pedaços; durante a
edição Ctrl+Z/Ctrl+Y são do campo e nada vai ao host além de Enter/Esc. Marcas: camada `#pmvni-camada` (filha do
`<body>`, `position:absolute`, fora de toda `section.folha`): traço azul de 3 px à esquerda de cada pedaço alterado e
botão "◦ excl. NN" na margem cinza, na altura do topo do item âncora (`lado "antes"`) ou da base do anterior (`"depois"`);
com marcas visíveis a margem esquerda mínima passa de 12 px para 72 px. Atalhos repassados (fora da edição, com
`preventDefault`): `Ctrl+O Ctrl+I Ctrl+S Ctrl+Shift+S Ctrl+E Ctrl+Z Ctrl+Y Ctrl+F Alt+ArrowUp Alt+ArrowDown Delete F2 F6
Ctrl+0 Ctrl+Plus Ctrl+Minus` (nomes canônicos exatamente assim). Âncora: o bloco `[data-ref]` que cruza o topo da janela
e o deslocamento em px sem zoom.

- [ ] **Step 1: Write the failing tests** (cliques e teclas por CDP `Input.dispatchMouseEvent` / `Input.dispatchKeyEvent` / `Input.insertText`; refs de sessão da Tarefa 6)

```csharp
[Fact] void tipos_da_ponte_iguais_nos_dois_lados()   // lê web/paginador/src/mensagens.ts e compara com Mensagens.cs
[Fact] async Task estado_editor_marca_campos_sem_mudar_texto_nem_medidas()
    // longo: camposMarcados >= 300; TextoPorFolhaAsync igual; getBoundingClientRect de todo '.area > *' igual (±0,01 px)
[Fact] async Task clique_no_card_envia_selecionado_com_o_id()   // ITEM 03 verificação → {ref: ids[2], campo: "verificacao"}; todos os pedaços com data-pmvni-selecionado
[Fact] async Task duplo_clique_pede_edicao_e_enter_envia_texto_puro()
    // pedirEdicao; editarCampo{texto:"Texto A"}; cursor no fim + insertText(" B") + Enter → editar{texto:"Texto A B"}; contenteditable removido
[Fact] async Task esc_cancela_e_restaura()
[Fact] async Task colar_html_vira_texto_puro()   // insertText("<b>x</b>\nlinha") → texto contém "<b>x</b>"; querySelector('[data-campo] b') == null
[Fact] async Task campo_dividido_edita_no_primeiro_pedaco()   // extremos (verificação de 9 000 caracteres): 1º pedaço com o texto inteiro, demais ocultos; Esc devolve
[Fact] async Task marcas_ficam_fora_das_folhas_e_fora_do_pdf()
    // marcasDesenhadas == 2; 'section.folha .pmvni-marca' == 0; texto "◦ excl. 07"; ImprimirPdfAsync com o mesmo nº de páginas e TextoPorFolha igual
[Fact] async Task ancora_restaura_a_rolagem_depois_de_recarregar()   // rola ao item 30; obterAncora; recarrega + aplicarPlano + estadoEditor{ancora} → mesma ref, |Δ| ≤ 2 px
[Fact] async Task atalhos_da_lista_vao_ao_host_e_os_outros_nao()   // Ctrl+S → atalho "Ctrl+S"; Ctrl+P → nada em 1 s; Ctrl+Z durante a edição → nada
[Fact] async Task botao_direito_envia_menu_de_contexto()   // {ref, campo, x, y} com x,y dentro da janela
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Web.Testes --filter "FullyQualifiedName~EdicaoTelaTestes|FullyQualifiedName~PonteTestes"` → Expected: FAIL.
- [ ] **Step 3: Implement** em TS; `cd web/paginador; npm run typecheck; npm run build` → Expected: sem erros; `paginador.js` regravado.
- [ ] **Step 4: Run** `dotnet test tests/Pmvni.Web.Testes` → Expected: PASS (novos + Etapa 1). Run `python -I tools/fidelidade.py v2 --caso longo` → Expected: aprovado.
- [ ] **Step 5: Commit** — `feat: selecao, edicao no A4 e marcas de revisao no paginador`

### Task 8 (Tarefa 8): Shell — barra de título, trilho de módulos, faixa de opções por dados, pacote no app

**Files:**
- Create: `src/Pmvni.App/Logica/{Modulos.cs,Faixas.cs,ComandosApp.cs,Ajustes.cs,TituloJanela.cs,EstadoGravacao.cs}`, `src/Pmvni.Revisao/Pastas/NomesArquivo.cs`, `src/Pmvni.App/Controles/{FaixaDeOpcoes,Trilho}.xaml(.cs)`, `src/Pmvni.App/Paginas/PaginaRelatorios.xaml(.cs)`, `src/Pmvni.App/Edicao/SessaoEdicao.cs`, `src/Pmvni.App/Perguntas.cs`, `tools/roteiro-e-esperar.ps1`, `docs/roteiro-etapa-2.md`
- Modify: `MainWindow.xaml(.cs)` (vira shell; a tela A4 e o painel vão para `PaginaRelatorios`, com os acessores `internal` mantidos no `MainWindow` para o roteiro), `Argumentos.cs`, `ItemNavegacao.cs`, `Estilos.xaml`, `Pmvni.App.csproj`, `Roteiro.cs`, `tools/roteiro.ps1` (resultado → `docs\roteiro-etapa-2.resultado.json`)
- Test: `tests/Pmvni.Revisao.Testes/{FaixasTestes.cs,AjustesTestes.cs,NomesArquivoTestes.cs}` (+ `<Compile Include="..\..\src\Pmvni.App\Logica\*.cs" LinkBase="Logica" />`)

**Interfaces:**
- Consumes: Tarefas 4–7 (`Pacote`, `Importador`, `Recompositor`, `PastaTrabalho`, `Exportador.ExportarFinalAsync(bytes…)`, mensagens `estadoEditor`).
- Produces:
  - `enum Modulo { Relatorios, Despachos, Processos, Legislacao, Ajustes }`.
  - `enum TipoComando { Botao, Alternar, Menu }`; `sealed record ComandoFaixa(string Id, string Rotulo, string Glifo, TipoComando Tipo = TipoComando.Botao, string? Atalho = null, IReadOnlyList<ComandoFaixa>? Itens = null)`; `sealed record AbaFaixa(string Nome, IReadOnlyList<ComandoFaixa> Comandos)`; `sealed record DefinicaoFaixa(Modulo Modulo, IReadOnlyList<AbaFaixa> Abas, int AbaPadrao)`; `static class Faixas { DefinicaoFaixa Para(Modulo m); IReadOnlyList<Modulo> Disponiveis }` — **uma definição por módulo**; cada tarefa seguinte acrescenta suas abas/comandos e põe o módulo em `Disponiveis`.
  - `static class ComandosApp` com as constantes de id (`"arquivo.importar"`, `"arquivo.abrir"`, `"arquivo.salvar"`, `"arquivo.salvarComo"`, `"arquivo.exportarPdf"`, `"arquivo.fechar"`, `"exibir.menos"`, `"exibir.mais"`, `"exibir.largura"`, `"exibir.pagina"`, `"exibir.painel"`, `"ajuda.atalhos"`, `"ajuda.sobre"`; tarefas seguintes acrescentam).
  - `sealed class Ajustes { Modulo Modulo; Dictionary<Modulo,string> AbaPorModulo; string Tema = "sistema"; int IntervaloAutosaveS = 30; bool MarcasRevisao = true; List<string> Recentes; string? PastaProcessos; static Ajustes Carregar(string raizDados) /* <raiz>\ajustes.json; corrompido ⇒ renomeia para ajustes.corrompido.json e usa o padrão */; void Salvar(string raizDados) /* tmp + Move */ }`.
  - `static string TituloJanela.De(string? processo, string? requerente, string nomeArquivo)` → `"Proc. 9001/2026 · FULANO DE TAL"`; sem os dois ⇒ nome do arquivo.
  - `enum EstadoGravacao { Salvo, Salvando, NaoSalvo, Conflito, SomenteLeitura }`; `static string EstadoGravacaoTexto.Texto(EstadoGravacao e, DateTime? salvoEmLocal)` → `"Salvo 14:32"`, `"Salvando…"`, `"Não salvo"`, `"Conflito"`, `"Somente leitura"`.
  - `static class NomesArquivo { string Importacao(int? numero, int? ano) /* "Relatorio_17098_2026.pmvrel" ou "Relatorio.pmvrel" */; (int? Numero, int? Ano) DoProcessoDaCapa(string? processo) /* "9001/2026" */; string Livre(string pasta, string nome) /* sem sobrescrever: nome, nome_2, nome_3… */; string CopiaDeConflito(string pacote, DateTime local) /* "<nome> (cópia 2026-11-14 14-32).pmvrel" */ }`.
  - `internal interface IPerguntas { Task<string?> AbrirArquivoAsync(string extensao); Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao); Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao); Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos); }` + `static IPerguntas Perguntas.Atual` (o roteiro troca).
  - `MainWindow`: `internal Task<bool> ImportarParaAsync(string json, string pmvrel)`, `internal Task<bool> AbrirPacoteAsync(string pmvrel)`, `internal Task ExecutarComandoAsync(string id)`, `internal IReadOnlyList<string> ComandosSemManipulador()`, `internal Modulo ModuloAtual`, `internal Task MudarModuloAsync(Modulo m)`, `internal SessaoEdicao? Sessao`.
  - `SessaoEdicao` (App): abre/importa, mantém `SessaoRevisao`, `Recompositor`, `PacoteAberto`, recompõe (`RecomporAsync()`: âncora → preparar → compor no host oculto → carregar/aplicar na tela → `estadoEditor`) e grava (`GravarAsync()` com `Pacote.Gravar(…, esperado: disco aberto)`).

Interface: `TitleBar` (Windows App SDK 1.8) com ícone, título (`TituloJanela`), estado (`EstadoGravacaoTexto`); trilho à
esquerda com ícone + rótulo (64 px, indicador 3 × 16 como `PmvniRailIndicatorStyle`), só com `Faixas.Disponiveis`;
`FaixaDeOpcoes` = `SelectorBar` (abas) + `CommandBar` (`DefaultLabelPosition="Right"`; `Collapsed` com a janela < 1000 DIP),
montada de `Faixas.Para(modulo)`; troca inteira com o módulo; aba padrão `AbaPadrao` (índice 1; Ajustes usa 0, decisão do
plano: sua 2ª aba seria Ajuda); módulo e aba lembrados em `Ajustes`. Páginas instanciadas uma vez e trocadas por
`Visibility` (o WebView2 nunca é recriado). Relatórios nesta tarefa: Arquivo (Importar dados.json, Abrir, Salvar,
Salvar como, Exportar PDF, Fechar), Exibir (−, +, Ajustar largura, Página inteira, Painel lateral), Ajuda (Atalhos de
teclado, Sobre: versões do app, modelo rev10, WebView2). Os botões Abrir/Exportar da barra da Etapa 1 saem (vão para a
faixa); o rodapé (zoom, "folha X de Y") fica. Importar: seletor `.json` → seletor de salvar com
`NomesArquivo.Importacao(DoProcessoDaCapa(capa.processo))`; `.json` na linha de comando faz o mesmo; `.pmvrel` abre.
Exportar: bloqueado com `ErrosFinais`; `AvisosExportacao` pedem `ConfirmarAsync`; recusa se `EstadoDisco.Ler` ≠ aberto
(`ExportacaoDivergenteException.ArquivoMudou`). Painel lateral (aba Seções; Excluídos e Histórico vêm na Tarefa 9).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] void toda_faixa_comeca_em_arquivo_e_termina_em_ajuda()       // para m em Faixas.Disponiveis (Ajustes: só Arquivo e Ajuda)
[Fact] void aba_padrao_e_a_segunda_exceto_ajustes()
[Fact] void ids_de_comando_unicos_e_conhecidos()   // todo Id está em ComandosApp; sem repetição dentro de um módulo
[Fact] void ajustes_lembram_modulo_e_aba()         // Salvar/Carregar em PastaDeTeste
[Fact] void ajustes_corrompidos_voltam_ao_padrao_e_guardam_o_arquivo()
[Fact] void titulo_da_janela()                     // ("9001/2026","FULANO","x") → "Proc. 9001/2026 · FULANO"; (null,null,"longo") → "longo"
[Fact] void estado_de_gravacao_em_texto()          // (Salvo, 14:32) → "Salvo 14:32"
[Fact] void nomes_de_arquivo()                     // Importacao(17098,2026) → "Relatorio_17098_2026.pmvrel"; Livre com 2 existentes → "…_3.pmvrel"
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter "FullyQualifiedName~FaixasTestes|FullyQualifiedName~AjustesTestes|FullyQualifiedName~NomesArquivoTestes"` → Expected: FAIL.
- [ ] **Step 3: Implement** a lógica e passar os testes; depois o XAML/shell. `dotnet build src/Pmvni.App -c Debug` → Expected: 0 erros.
- [ ] **Step 4: Roteiro.** `tools/roteiro-e-esperar.ps1`: apaga `tests\saida\roteiro-fim.txt`, abre `tools\roteiro.cmd` com `Start-Process explorer.exe`, espera até 10 min pelo `roteiro-fim.txt`, lê `docs\roteiro-etapa-2.resultado.json`, imprime `falhas` e sai 0 só com `ok: true`. `Roteiro.cs`: `Perguntas.Atual` = respostas fixas; tudo grava em `tests\saida\roteiro`. Passos: `1_importar_longo` (`ImportarParaAsync(longo.json, <pasta>\longo.pmvrel)`, mesmas conferências do antigo `1_abrir_longo` + título "Proc. …"), `2_rolagem_continua`, `3_zoom_nao_recompoe`, `4_navegacao`, `5_exportar_pdf` (recibo com `sha256_pacote`), `6_estouro_recusado` e `8_campos_em_aberto_bloqueiam_exportar` (importando cópias em `<pasta>`), `7_tema_escuro`, `9_pacote_mudou_recusa_exportar` (outro `Pacote.Gravar` com requerente trocado → recusa `ArquivoMudou`, nenhum PDF), `10_faixa_por_modulo` (`ComandosSemManipulador().Count == 0`; aba lembrada em `ajustes.json`), `11_reabrir_pacote` (`AbrirPacoteAsync` → mesmo `Plano.ParaJson()` da importação).
  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` → Expected: testes aprovados, autoteste aprovado, "Executável final: …".
  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1` → Expected: `ok: true`, código 0.
- [ ] **Step 5:** `docs/roteiro-etapa-2.md` (como rodar + tabela dos passos). Commit — `feat: shell com trilho de modulos, faixa de opcoes e pacote no app`

### Task 9 (Tarefa 9): Revisão no app — Página Inicial, Revisão, painéis, Propriedades, menu de contexto e atalhos

**Files:**
- Create: `src/Pmvni.App/Logica/Atalhos.cs`; Modify: `Logica/{Faixas.cs,ComandosApp.cs}`, `Paginas/PaginaRelatorios.xaml(.cs)`, `Edicao/SessaoEdicao.cs`, `MainWindow.xaml(.cs)`, `ItemNavegacao.cs`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`
- Test: `tests/Pmvni.Revisao.Testes/AtalhosTestes.cs`, `FaixasTestes.cs`

**Interfaces:**
- Consumes: Tarefas 3, 7, 8 (`SessaoRevisao`, mensagens da ponte, `FaixaDeOpcoes`, `SessaoEdicao.RecomporAsync`).
- Produces:
  - Relatórios: `Página Inicial` (Situação ▾ com as 4 situações de `Situacoes.SitItem` pelos textos de `Situacoes.Sit`, Excluir/Restaurar item, Editar texto, Original ⇄, Restaurar original, ◂ Item, Item ▸, Próximo pendente, Exportar PDF) e `Revisão` (Marcas de revisão, Somente alterados, Conferência); ids `inicio.situacao`, `inicio.excluirRestaurar`, `inicio.editarTexto`, `inicio.original`, `inicio.restaurarOriginal`, `inicio.itemAnterior`, `inicio.itemProximo`, `inicio.proximoPendente`, `revisao.marcas`, `revisao.somenteAlterados`, `revisao.conferencia`, `editar.desfazer`, `editar.refazer`, `janela.regioes`. Aba padrão do módulo passa a ser Página Inicial.
  - `static class Atalhos { IReadOnlyDictionary<string,string> Mapa /* tecla canônica → id */; string? Comando(string tecla) }` com exatamente as 16 teclas da Tarefa 7 (`Ctrl+O`→`arquivo.abrir`, `Ctrl+I`→`arquivo.importar`, `Ctrl+S`→`arquivo.salvar`, `Ctrl+Shift+S`→`arquivo.salvarComo`, `Ctrl+E`→`arquivo.exportarPdf`, `Ctrl+Z`→`editar.desfazer`, `Ctrl+Y`→`editar.refazer`, `Ctrl+F`→`pesquisa.focar` (constante criada aqui; o manipulador chega na Tarefa 11 e, até lá, a tecla não faz nada), `Alt+ArrowUp`→`inicio.itemAnterior`, `Alt+ArrowDown`→`inicio.itemProximo`, `Delete`→`inicio.excluirRestaurar`, `F2`→`inicio.editarTexto`, `F6`→`janela.regioes`, `Ctrl+0`→`exibir.largura`, `Ctrl+Plus`→`exibir.mais`, `Ctrl+Minus`→`exibir.menos`).
  - `MainWindow`: `internal string? Selecionado`, `internal Task SelecionarAsync(string? itemId)`, `internal Task AplicarAsync(Comando c)` (fluxo abaixo), `internal Task EditarTextoAsync(string itemId, string campo, string texto)` (mesma rota do `editar` da página), `internal IReadOnlyList<string> UltimosErros`, `internal long UltimaLatenciaMs`.

Fluxo de um comando (desenho §8.4): se houver campo em edição, `concluirEdicao` antes; `sessao.Testar(c)` →
`Recompositor.Preparar(candidato)` (recusa ⇒ InfoBar "A alteração não foi aplicada" com os erros do kit; nada muda) →
`sessao.Aplicar(c)` → `RecomporAsync()`; estado "Não salvo". Duplo clique/F2: o host responde `editarCampo` com o
`TextoEfetivo` (F2 usa o último campo clicado ou `verificacao`). Painel lateral com abas **Seções** (ícone de situação e
marcador "alterado"; "Somente alterados" filtra), **Excluídos (n)** (Restaurar em cada linha) e **Histórico** (mais
recente primeiro, `DescricaoHistorico.Texto`, "14:32 · usuário · …"). Propriedades (~320 px, recolhível pela faixa
Exibir › Propriedades): número atual e id, No PDF (alterna Excluir/Restaurar), Situação original e revisada,
Justificativa (aplica `DefinirJustificativa` ao perder o foco ou Enter), os 5 campos com indicador "alterado" e
Restaurar por campo, Original ⇄ lado a lado (nunca no A4), histórico do item. Menu de contexto na folha
(`menuContexto` → `MenuFlyout.ShowAt(WebView2, ponto)`): Situação ▸, Editar texto, Excluir/Restaurar, Original ⇄,
Propriedades; `excluidoClicado` → Flyout com o título e Restaurar. Desfazer/Refazer na barra de título. Conferência:
`ContentDialog` com `ErrosFinais` e avisos. Atalhos: `KeyboardAccelerator`s na janela com o mesmo `Atalhos.Mapa`
(Delete ignorado com foco em caixa de texto) + mensagens `atalho` da página.

- [ ] **Step 1: Write the failing tests** — `atalhos_sao_as_16_teclas_da_ponte` (o conjunto de chaves de `Atalhos.Mapa` == a lista de `edicao.ts`, lida do arquivo), `todo_atalho_aponta_para_comando_conhecido`, `relatorios_abre_em_pagina_inicial`. Run `dotnet test tests/Pmvni.Revisao.Testes --filter FullyQualifiedName~Atalhos` → Expected: FAIL.
- [ ] **Step 2: Implement**; `dotnet build src/Pmvni.App -c Debug` → 0 erros; testes acima → PASS.
- [ ] **Step 3: Roteiro** (novos passos, cada um conferindo o modelo **e** a tela por `Js`): `12_selecionar_e_editar_no_a4` (clique por CDP no ITEM 03 → Propriedades mostra 03; `EditarTextoAsync` muda a verificação; `TextoPorFolha` contém o texto novo; `data-campo` ausente no HTML do `Preparado`), `13_situacao_e_justificativa` (ATENDE sem justificativa → Exportar bloqueado com a mensagem exata da Tarefa 3; com justificativa → liberado), `14_excluir_e_marca` (Excluir ITEM 05 → 59 itens, "ITEM 05" é o antigo 06, aba Excluídos (1), marca "◦ excl. 05" fora das folhas), `15_desfazer_refazer` (desfazer 3× volta ao estado da importação: mesmo `Plano.ParaJson()`; refazer 3×), `16_salvar_reabrir_exportar` (Ctrl+S; reabrir; mesmas alterações, histórico com os registros `desfeito`/`refeito`; exportar PDF com `folhas` = tela), `17_menu_de_contexto_e_atalhos` (`menuContexto` abre o flyout com 5 entradas; `atalho` `Delete` exclui; `Alt+ArrowDown` seleciona o próximo), `18_latencia` (mediana de 5 comandos no longo em `latencia_ms`; só registra).
  Run: `tools\build-release.ps1` e depois `tools\roteiro-e-esperar.ps1` → Expected: `ok: true`.
- [ ] **Step 4: Commit** — `feat: revisao dos itens no app com paineis, propriedades e atalhos`

### Task 10 (Tarefa 10): Autosave, recuperação, trava e conflito no app

**Files:**
- Modify: `src/Pmvni.App/Edicao/SessaoEdicao.cs`, `MainWindow.xaml(.cs)`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`

**Interfaces:**
- Consumes: `Trava`, `Diario`, `Pacote.Gravar`/`ConflitoNoDiscoException`, `NomesArquivo.CopiaDeConflito`, `Ajustes.IntervaloAutosaveS`, `IPerguntas.RecuperarAsync`.
- Produces: `enum RespostaRecuperacao { Recuperar, Descartar }`; `SessaoEdicao.Estado` (`EstadoGravacao`); `MainWindow`: `internal Task<bool> GravarAgoraAsync()`, `internal void SimularQuedaParaRoteiro()` (solta pacote, trava e timers sem gravar; o diário fica), `internal InfoBar InfoConflito`.

Regras (desenho §5.1, §5.5, §8.6): diário iniciado ao abrir (`sha256Base` = `Disco.Sha256`), `Registrar` em cada
`SessaoRevisao.Registrado`; gravação após `IntervaloAutosaveS` sem comando, ao trocar de arquivo/módulo e no
`AppWindow.Closing` (cancela o fechamento, grava, fecha; só pergunta se a gravação falhar: "Salvar como…" / "Fechar sem
salvar (a recuperação fica guardada)" / "Cancelar"); gravação ok ⇒ `MarcarGravado`, diário reiniciado com o novo sha,
"Salvo HH:MM". Abertura: `Trava.Adquirir` (Ocupada ⇒ "Somente leitura" + InfoBar "Aberto por {usuario} em {maquina}
desde {HH:mm}: somente leitura."; comandos de edição desabilitados); batimento a cada 30 s; `Diario.Ler` com registros e
`DaMesmaVersao` ⇒ `RecuperarAsync` (Recuperar = `Reaplicar` + "Não salvo"; Descartar = `Descartar`); de outra versão ⇒
`Arquivar` + InfoBar informativo com o nome do arquivo arquivado. Conflito na gravação ⇒ "Conflito" + InfoBar com
"Salvar como cópia" (`NomesArquivo.CopiaDeConflito`, sem seletor) e "Recarregar" (confirma; descarta a sessão; o diário é
arquivado, nunca apagado). Exportar também recusa em Conflito.

- [ ] **Step 1: Implement**; `dotnet build src/Pmvni.App -c Debug` → 0 erros.
- [ ] **Step 2: Roteiro:** `19_autosave` (intervalo 15 s nos ajustes do roteiro; 1 comando; em ≤ 25 s o estado vira "Salvo", o pacote em disco tem o registro e o diário só o cabeçalho), `20_gravacao_falha_sem_perder` (arquivo preso com `FileShare.None` pelo roteiro → estado "Não salvo", InfoBar de erro, pacote em disco igual ao de antes, diário com o registro; solta → `GravarAgoraAsync` ok), `21_recuperacao_apos_queda` (2 comandos, `SimularQuedaParaRoteiro`, reabrir → pergunta com 2 comandos; Recuperar → mesmo `ProjetoJson`), `22_trava_e_somente_leitura` (trava viva de outro PID/máquina gravada pelo roteiro → "Somente leitura", Excluir desabilitado; trava com batimento de 3 min atrás → abre editável), `23_conflito_nao_sobrescreve` (outro `Pacote.Gravar` depois de abrir; comando + `GravarAgoraAsync` → "Conflito"; bytes do disco = os do outro; "Salvar como cópia" cria o arquivo de cópia com as alterações). Run `tools\build-release.ps1`, `tools\roteiro-e-esperar.ps1` → Expected: `ok: true`.
- [ ] **Step 3: Commit** — `feat: autosave, recuperacao, trava e conflito no app`

### Task 11 (Tarefa 11): Pesquisa, tema, Recentes, Comparar com o original e página Ajustes

**Files:**
- Create: `web/paginador/src/pesquisa.ts`, `src/Pmvni.Revisao/Comparacao.cs`, `src/Pmvni.App/Paginas/PaginaAjustes.xaml(.cs)`
- Modify: `mensagens.ts`, `index.ts`, `tela.ts`, `paginador.js`, `Ponte/Mensagens.cs`, `Logica/{Faixas.cs,ComandosApp.cs,Ajustes.cs}`, `MainWindow.xaml(.cs)`, `PaginaRelatorios.xaml(.cs)`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`
- Test: `tests/Pmvni.Web.Testes/PesquisaTelaTestes.cs`, `tests/Pmvni.Revisao.Testes/{ComparacaoTestes.cs,AjustesTestes.cs}`

**Interfaces:**
- Consumes: Tarefas 3, 7–10.
- Produces:
  - host→web `pesquisar {termo, passo: -1|0|1}` → `pesquisado {total, atual, ref|null}` (`termo` vazio limpa); realce pela CSS Custom Highlight API (`CSS.highlights`, `::highlight(pmvni-busca)` e `::highlight(pmvni-busca-atual)` só em `@media screen`; **nenhum elemento novo** no DOM); busca dentro de cada nó de texto de `.area`, sem diferenciar maiúsculas nem acentos (NFD, 1:1 por caractere); `passo` rola até a ocorrência.
  - `static IReadOnlyList<DiferencaItem> Comparacao.Diferencas(Projeto p, IReadOnlyList<RegistroHistorico> h)`; `sealed record DiferencaItem(string Item, int? NumeroAtual, int NumeroOriginal, bool Excluido, string? SituacaoOriginal, string? SituacaoRevisada, IReadOnlyList<DiferencaCampo> Campos, IReadOnlyList<string> CitacoesConvertidas)`; `sealed record DiferencaCampo(string Campo, string Original, string Revisado)` (textos impressos).
  - `Ajustes.AdicionarRecente(string caminho)` (no topo, sem repetir ignorando maiúsculas, no máximo 10); `IReadOnlyList<string> Ajustes.RecentesExistentes()` (some o que não existe mais: nada desabilitado).
  - Comandos: `arquivo.recentes` (Menu), `exibir.propriedades`, `exibir.tema` (Menu: Sistema/Claro/Escuro), `revisao.comparar`, `pesquisa.focar`; módulo `Ajustes` entra em `Faixas.Disponiveis` (trilho, embaixo).

Pesquisa: `AutoSuggestBox`/`TextBox` na barra de título ("Pesquisar no relatório"), Enter = próxima, Shift+Enter =
anterior, "3 de 12" ao lado; Ctrl+F foca. Tema: `Raiz.RequestedTheme` + `AplicarTemaWeb()` existentes, guardado em
`Ajustes.Tema`; folhas sempre brancas. Comparar: painel/`ContentDialog` por item (situação original → revisada, campos
original → revisado, excluídos, citações convertidas na importação). Página Ajustes (desenho §9): tema, intervalo do
autosave (15/30/60/120 s), pasta de recuperação (só o caminho, informativo), versões (app, modelo rev10, WebView2).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] async Task pesquisa_realca_sem_mudar_o_dom_e_navega()
    // longo: "verificacao" acha o mesmo total que um contador JS de "VERIFICAÇÃO" (sem acento/caixa); nº de elementos do body igual;
    // TextoPorFolha igual; passo 1 (total vezes) volta à 1ª; termo "" → CSS.highlights.size == 0
[Fact] async Task pesquisa_nao_entra_no_pdf()     // ImprimirPdfAsync com realce == sem realce (mesmas páginas e texto)
[Fact] void comparacao_lista_so_o_que_mudou()      // 1 situação + 1 campo + 1 excluído → 3 DiferencaItem; textos impressos (com números atuais)
[Fact] void recentes_no_maximo_10_sem_repetir()
[Fact] void recentes_somem_quando_o_arquivo_some()
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Web.Testes --filter FullyQualifiedName~PesquisaTelaTestes` e `dotnet test tests/Pmvni.Revisao.Testes --filter "FullyQualifiedName~ComparacaoTestes|FullyQualifiedName~AjustesTestes"` → Expected: FAIL.
- [ ] **Step 3: Implement** (TS: `npm run typecheck; npm run build`). Run os mesmos filtros → Expected: PASS.
- [ ] **Step 4: Roteiro:** `24_pesquisa` (termo do título do ITEM 10 → total ≥ 1, `rolarPara` coerente, "1 de N"; Enter avança), `25_tema_chrome` (Escuro/Claro pelos comandos; `ajustes.json` guarda; folha `rgb(255, 255, 255)` nos dois), `26_recentes` (abrir 2 pacotes → menu com os 2, mais recente primeiro; apagar um arquivo → some do menu), `27_comparar_com_o_original` (depois dos passos 12–16: itens alterados listados com os textos exatos). Run `tools\build-release.ps1`, `tools\roteiro-e-esperar.ps1` → Expected: `ok: true`.
- [ ] **Step 5: Commit** — `feat: pesquisa, tema, recentes, comparar e ajustes`

### Task 12 (Tarefa 12): Serviço de pastas — processos, despachos e abertura externa

> **Alterada pelo desenho §11 (15/11/2026):** saíram a leitura da pasta de legislação (`Pastas/Legislacao.cs`,
> `Legislacao.Listar`), a árvore `tests/corpus/legislacao/` de 4 arquivos e o teste `legislacao_lista_todos_por_nome`;
> o `atalho.lnk` do teste de abertura externa foi para `tests/corpus/processos/MODELOS/` (pasta que `Processos.Listar`
> já ignora). A legislação passa a ser embutida (Tarefas 17–18).

**Files:**
- Create: `src/Pmvni.Revisao/Pastas/{Processos.cs,Despachos.cs,AbrirExterno.cs}`
- Create (fictícios, arquivos de 0 byte salvo o `dados_0001.json`, cópia de `tests/corpus/curto.json`):

```
tests/corpus/processos/
  2098/0007 - 2098 - CICLANO DE EXEMPLO (FICTICIO)/DESPACHO 01 - 2098-03-02/DESPACHO_APROVACAO_0007_2098.pdf
  2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/.pmvni-ai/REVISAO_2099-01-10/dados_0001.json
  2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/DESPACHO 01 - 2099-01-15/DESPACHO_CORRECAO_0001_2099.docx  (+ .pdf)
  2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/DESPACHO 02 - 2099-02-20/DESPACHO_APROVACAO_0001_2099.pdf
  2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/PROJETO/ARQ 01-03.pdf   PROJETO/script.cmd   DOCUMENTOS/ART.pdf
  2099/0012-2099 - BELTRANO DE EXEMPLO (FICTICIO)/DESPACHO 01 - 2099-03-05/OFICIO_0012.docx
  2099/pasta fora do padrao/leia.txt
  MODELOS/x.txt   MODELOS/atalho.lnk
```
- Test: `tests/Pmvni.Revisao.Testes/PastasTestes.cs`

**Interfaces:**
- Consumes: `NomesArquivo` (Tarefa 8).
- Produces (tudo síncrono, para rodar em `Task.Run`, com `CancellationToken`; pasta ou arquivo ilegível vira aviso, nunca exceção):
  - `sealed record Leitura<T>(IReadOnlyList<T> Itens, IReadOnlyList<string> Avisos)`.
  - `sealed record Processo(string Pasta, string NomePasta, int? Numero, int? Ano, string? Requerente)`; `static Leitura<Processo> Processos.Listar(string raiz, CancellationToken ct)` — só subpastas de pastas de ano (`^\d{4}$`); nome `^(?<num>\d{1,6})\s*-\s*(?<ano>\d{4})\s+-\s+(?<req>.+)$`; fora do padrão: `Numero/Ano/Requerente` null, `NomePasta` como veio; ordem: ano da pasta desc, número desc, fora do padrão no fim do ano; `static bool Processos.Filtrar(Processo p, string termo)` (número, ou requerente/nome sem caixa nem acento).
  - `sealed record ArquivoEncontrado(string Caminho, string Relativo, long Tamanho, DateTime AlteradoUtc)`; `enum TipoRelatorio { Pacote, AnaliseIa }`; `sealed record RelatorioEncontrado(string Caminho, TipoRelatorio Tipo, DateTime AlteradoUtc)`; `enum TipoDespacho { Correcao, Aprovacao, NaoIdentificado }`; `sealed record Despacho(string Pasta, int Numero, DateOnly Data, TipoDespacho Tipo, IReadOnlyList<string> Arquivos, Processo? Processo)`; `sealed record DetalheProcesso(Processo Processo, IReadOnlyList<RelatorioEncontrado> Relatorios, IReadOnlyList<Despacho> Despachos, IReadOnlyList<ArquivoEncontrado> Projeto, IReadOnlyList<ArquivoEncontrado> Documentos, IReadOnlyList<string> Avisos)`; `static DetalheProcesso Processos.Detalhar(Processo p, CancellationToken ct)` — relatórios = `*.pmvrel` no 1º nível + `.pmvni-ai\*\dados*.json` (decisão do plano: o kit grava `dados_<nº>.json`), mais novo primeiro; despachos = subpastas `^DESPACHO\s+(\d{1,3})\s+-\s+(\d{4}-\d{2}-\d{2})$` (sem caixa) com `.docx/.doc/.pdf`; PROJETO/DOCUMENTOS até 3 níveis.
  - `static string Processos.NomeImportacao(Processo p, string? processoDaCapa)` → `NomesArquivo.Livre(p.Pasta, NomesArquivo.Importacao(nº/ano da pasta, senão da capa))`.
  - `static Leitura<Despacho> Despachos.Listar(IEnumerable<Processo> processos, CancellationToken ct)` — data desc; tipo pelo nome dos arquivos: contém `CORRECAO` → Correção, `APROVACAO` → Aprovação (sem caixa e sem acento), senão Não identificado.
  - `static class AbrirExterno { IReadOnlySet<string> Extensoes /* as 11 da restrição global */; bool Permitido(string caminho, out string? motivo) }` — exige arquivo existente, comum (não pasta nem ponto de reparse), sem `:` depois da unidade, extensão na lista; motivo em pt-BR: `"Este tipo de arquivo não é aberto pelo editor (.cmd). Use Mostrar no Explorador."`.

- [ ] **Step 1: Write the failing tests** (sobre `tests/corpus/...`, só leitura; a árvore grande é gerada em `PastaDeTeste`)

```csharp
[Fact] void processos_mais_recentes_primeiro()
    // NomePasta na ordem: "0012-2099 - BELTRANO…", "0001 - 2099 - FULANO…", "pasta fora do padrao", "0007 - 2098 - CICLANO…"; MODELOS ignorada
[Fact] void nome_fora_do_padrao_vem_como_veio()     // Numero null, NomePasta "pasta fora do padrao"
[Fact] void filtro_por_numero_e_nome_sem_acento()    // "12" → só 0012; "fulano" → só 0001
[Fact] void detalhe_acha_analise_da_ia_e_despachos()
    // 0001: 1 relatório AnaliseIa (dados_0001.json); despachos [02 2099-02-20 Aprovação, 01 2099-01-15 Correção (2 arquivos)];
    // Projeto ["ARQ 01-03.pdf","script.cmd"]; Documentos ["ART.pdf"]
[Fact] void pmvrel_na_pasta_do_processo_aparece_como_pacote()   // cópia da árvore em PastaDeTeste + Relatorio_0001_2099.pmvrel
[Fact] void nome_de_importacao_nao_sobrescreve()     // na cópia: "Relatorio_0001_2099.pmvrel", depois "Relatorio_0001_2099_2.pmvrel"
[Fact] void despachos_de_todos_por_data()            // 4 despachos: 2099-03-05 NaoIdentificado, 2099-02-20, 2099-01-15, 2098-03-02
[Theory] [InlineData("ARQ 01-03.pdf", true)] [InlineData("script.cmd", false)] [InlineData("atalho.lnk", false)]
void abrir_externo_so_documentos(string arquivo, bool permitido)   // atalho.lnk em MODELOS/; + pasta → false; "x.pdf:fluxo" → false
[Fact] void raiz_ausente_vira_aviso()                // Itens vazio, 1 aviso com "não foi encontrada"
[Fact] void arvore_grande_e_lida_por_inteiro()       // 2 000 pastas geradas em PastaDeTeste: 2 000 processos; tempo só registrado
[Fact] void cancelamento_interrompe_a_leitura()
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter FullyQualifiedName~PastasTestes` → Expected: FAIL.
- [ ] **Step 3: Implement.** - [ ] **Step 4: Run** o filtro → Expected: PASS.
- [ ] **Step 5: Commit** — `feat: leitura das pastas de processos e despachos e abertura externa`

### Task 13 (Tarefa 13): Módulos Processos e Despachos na interface; pasta de processos nos Ajustes

> **Alterada pelo desenho §11 (15/11/2026):** saíram desta tarefa a `PaginaLegislacao` (vai para a Tarefa 18), a faixa
> de Legislação (`legislacao.abrir`, `legislacao.mostrar`, `legislacao.atualizar`, `legislacao.copiarCaminho`,
> `legislacao.ordenar`), `Ajustes.PastaLegislacaoEfetiva` e o seu seletor de pasta, a pesquisa por nome de arquivo em
> Legislação, a cópia de `tests\corpus\legislacao` no roteiro e o passo `30_legislacao` (o número 30 é reaproveitado
> pela Tarefa 18). Legislação **não** entra em `Faixas.Disponiveis` aqui (nenhum módulo sem função no trilho); entra na
> Tarefa 18.

**Files:**
- Create: `src/Pmvni.App/Paginas/{PaginaProcessos,PaginaDespachos}.xaml(.cs)`, `src/Pmvni.App/AberturaExterna.cs`
- Modify: `Logica/{Faixas.cs,ComandosApp.cs,Ajustes.cs}`, `PaginaAjustes.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`
- Test: `tests/Pmvni.Revisao.Testes/FaixasTestes.cs` (casos novos)

**Interfaces:**
- Consumes: Tarefa 12; `MainWindow.ImportarParaAsync/AbrirPacoteAsync/MudarModuloAsync`; `FaixaDeOpcoes`.
- Produces:
  - Faixas (desenho §9.1): **Processos** — Página Inicial (`processos.abrirRelatorio`, `processos.importarAnalise`, `processos.abrirArquivo`, `processos.abrirPasta`, `processos.atualizar`, `processos.ano` Menu), Exibir (`processos.ordenar` Menu número/data/requerente, `processos.mostrarPastas` Alternar); **Despachos** — Página Inicial (`despachos.abrir`, `despachos.abrirPdf`, `despachos.mostrar`, `despachos.irAoProcesso`, `despachos.atualizar`, `despachos.filtro` Menu Todos/Correção/Aprovação), Exibir (`despachos.agrupar` Menu processo/data, `despachos.ordenar`). Os dois entram em `Faixas.Disponiveis` na ordem do trilho: Relatórios, Despachos, Processos; Ajustes embaixo (Legislação entra entre Processos e Ajustes na Tarefa 18).
  - `static class AberturaExterna { bool Simular; List<string> Registradas; bool Abrir(string caminho, out string? motivo) /* AbrirExterno.Permitido + Process.Start(UseShellExecute) */; void MostrarNoExplorador(string caminho) /* explorer.exe /select,"…" */ }`.
  - `Ajustes.PastaProcessosEfetiva` (padrão `%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\PROCESSOS`); Ajustes ganha o seletor dessa pasta (`FolderPicker`). O campo `Ajustes.PastaLegislacao` da Tarefa 8 sai (um `ajustes.json` antigo com essa chave é lido sem erro e a chave é ignorada).
  - Pesquisa da barra muda de alvo: Relatórios = folhas; Processos = `Processos.Filtrar`; Despachos = nome do arquivo (sem caixa/acento). O texto de dica muda ("Pesquisar processos", "Pesquisar despachos"); Legislação: Tarefa 18.

Processos: lista à esquerda (mais recentes primeiro, filtro por ano), detalhe à direita (relatórios, despachos, PROJETO/
DOCUMENTOS); Abrir relatório: `.pmvrel` → `AbrirPacoteAsync` + módulo Relatórios; `dados*.json` → `ImportarParaAsync(json,
Processos.NomeImportacao(p, capa.processo))` + módulo Relatórios (é a "Importar análise da IA"). Pasta ausente ⇒ mensagem
"A pasta de processos não foi encontrada: <caminho>. Escolha a pasta em Ajustes." com o botão "Abrir Ajustes". Leitura em
`Task.Run`, cancelada ao trocar de módulo ou atualizar. Despachos: do processo selecionado ou de todos (alternância).

- [ ] **Step 1: Write the failing tests** — `cada_modulo_tem_a_faixa_do_desenho` (nomes das abas por módulo exatamente: Relatórios `[Arquivo, Página Inicial, Revisão, Exibir, Ajuda]`; Despachos/Processos `[Arquivo, Página Inicial, Exibir, Ajuda]`; Ajustes `[Arquivo, Ajuda]`), `trilho_na_ordem_do_desenho` (`[Relatorios, Despachos, Processos, Ajustes]`; a Tarefa 18 acrescenta Legislação). Run o filtro `FaixasTestes` → Expected: FAIL.
- [ ] **Step 2: Implement**; build Debug → 0 erros; testes → PASS.
- [ ] **Step 3: Roteiro** (copia `tests\corpus\processos` para `<pasta>\` e aponta os Ajustes para a cópia **antes** de abrir os módulos; `AberturaExterna.Simular = true`): `28_processos` (4 processos na ordem da Tarefa 12; filtro "fulano" → 1; Importar análise da IA do 0001 → cria `Relatorio_0001_2099.pmvrel` na cópia, módulo Relatórios aberto com 3 itens; repetir → `_2`), `29_despachos` (todos: 4 na ordem por data; filtro Aprovação → 2; Abrir PDF registra o caminho sem abrir; Abrir `MODELOS\atalho.lnk` pela `AberturaExterna` → recusado com a mensagem exata, nada registrado), `31_ajustes_pastas` (pasta inexistente → mensagem + botão Abrir Ajustes), `32_faixa_e_pesquisa_por_modulo` (para cada módulo: abas iguais a `Faixas.Para`, `ComandosSemManipulador()` vazio, dica da pesquisa certa; módulo/aba lembrados após `MudarModuloAsync`), `33_pastas_intactas` (lista de arquivos e sha da cópia igual à de antes, exceto os `.pmvrel` criados). Run `tools\build-release.ps1`, `tools\roteiro-e-esperar.ps1` → Expected: `ok: true`.
- [ ] **Step 4: Commit** — `feat: modulos processos e despachos`

### Task 14 (Tarefa 14): Roteiro completo, build, fidelidade, documentação e Recriar PC

> Executada **por último**, depois das Tarefas 15 a 18 (que vêm escritas abaixo dela). O roteiro, a documentação, a
> fidelidade e o Recriar PC desta tarefa cobrem também o novo despacho das Tarefas 15–16 (desenho §10) e a legislação
> compilada embutida das Tarefas 17–18 (desenho §11). O passo final passa de `39_fluxo_completo` para
> `44_fluxo_completo` (a Tarefa 18 acrescenta os passos 30 e 40–43).

**Files:**
- Modify: `src/Pmvni.App/Roteiro.cs`, `tools/roteiro.ps1`, `docs/roteiro-etapa-2.md`, `docs/roteiro-etapa-2.resultado.json`, `docs/notas-tecnicas.md`, `docs/fidelidade.md`, `recriar.json`
- Create: `docs/entrega-etapa-2.md`
- Modify (fora do repositório, se a conferência pedir): `C:\Users\smendesjunior\OneDrive\Aplicativos IA\Recriar PC\sistema\MAPA-DO-AMBIENTE.md`

**Interfaces:**
- Consumes: tudo das Tarefas 1–13 e 15–18.
- Produces: o roteiro final `44_fluxo_completo` em sequência única num pacote novo: importar `curto` (com a capa do processo fictício `0001/2099`, `RelatorioFicticio`, na cópia da árvore de processos) → editar texto no A4 → mudar situação com justificativa → excluir item → desfazer → salvar → fechar → reabrir (mesmo estado e histórico) → exportar PDF (páginas = folhas; recibo com `sha256_pacote` = sha do arquivo) → pesquisar → trocar tema → Recentes com o pacote no topo → Comparar lista as 2 alterações restantes → **Novo despacho › Correção** pela faixa de Relatórios (processo e relatório pré-escolhidos, etapa nova) → exigências = itens ≠ ATENDE do estado atual → conferir as páginas → gravar (DOCX + PDF + recibo; `relatorio.sha256_pacote` do recibo = sha do pacote) → Despachos lista o novo como Correção → **Legislação**: pesquisar `art. 45 pdm` → o PDM abre no Art. 45 → Copiar citação
= `"art. 45 da Lei Municipal nº 1.382/2020 (PDM)"`.

- [ ] **Step 1:** acrescentar `44_fluxo_completo`; garantir que todos os passos 1–44 continuam independentes (falha num não impede os seguintes) e que nada é gravado fora de `tests\saida\roteiro` e `tests\saida\dados-app`.
- [ ] **Step 2:** Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` → Expected: todos os testes aprovados, autoteste "aprovado (2 rodadas, ok=true, sem elevação)", a linha `Legislação embutida: 50 textos, … MB; kit: igual` (ou o `AVISO:` da Tarefa 17, que não reprova; se aparecer, regenerar o pacote pela Tarefa 17, Step 5, e rodar de novo), "Executável final: …" com o tamanho registrado, espaço liberado informado; `Aplicativo\` só com o `.exe`. O build **não** pode exigir Python/PyMuPDF: conferir que nenhum passo do `build-release.ps1` chama `tools\legislacao`.
- [ ] **Step 3:** Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1` → Expected: `ok: true`, `falhas: []`. Registrar a latência (passo 18) e a tabela da Tarefa 6 em `docs/notas-tecnicas.md` ("Latência de um comando (Etapa 2)", com a meta ≤ 1,5 s e se foi atingida; se não, registrar que a recomposição incremental fica para depois — desenho §8.4).
- [ ] **Step 4:** Run `python -I tools/fidelidade.py tudo` → Expected: V1, V2, V3, V5 e V6 aprovados em todos os casos (corpus, `dados_modelo --instrucoes`, 17098 local; V6 = despachos de correção e de aprovação × validador do kit, Tarefa 15), código 0. Resultado resumido em `docs/fidelidade.md` (só contagens e hashes). Run `python -I tools/legislacao/testes_extrair.py` → Expected: `OK`; Run `python -I tools/legislacao/extrair.py conferir --kit "<kit>"` → Expected: código 0 (igual) — se 1, o pacote embutido está desatualizado: regenerar (Tarefa 17, Step 5), refazer o build e registrar na entrega.
- [ ] **Step 5:** `docs/entrega-etapa-2.md` no formato de `docs/entrega-etapa-1.md` (o que funciona, inclusive o novo despacho de correção e de aprovação e a legislação compilada embutida — quantos textos, tamanho do pacote e do executável, o que ficou fora e por quê, como regenerar; o que foi testado com os números do `dotnet test`, do roteiro, da `fidelidade.py tudo` e dos `testes_extrair.py`; o que fica pendente: cálculos, aprovação completa e outros tipos de despacho fora do registro do kit, gestão de versões dos modelos, schema público, segurança completa do pacote, merge no kit); `docs/roteiro-etapa-2.md` com a tabela final dos 44 passos (o 30 é o `30_legislacao_arvore` da Tarefa 18).
- [ ] **Step 6:** `recriar.json`: `usadoPor` passa a citar o pacote `.pmvrel`, as pastas `recuperacao`, `edicao`, `despachos` e `ajustes.json` em `%LOCALAPPDATA%\EditorRelatoriosPMVNI` (não guardados: dados de processos), a leitura da pasta PROCESSOS do PMVNI (o app só cria `.pmvrel` de importação e, ao gravar um despacho, a pasta `DESPACHO NN` com DOCX/PDF/recibo), a leitura dos modelos de despacho e das referências do plugin `despacho` no kit (só leitura), a **legislação compilada embutida no executável** (nada a instalar; a leitura de `<kit>\03 - LEGISLACAO` é opcional, só para a conferência e o PDF original) e o LibreOffice para o PDF do despacho; nenhum item novo em `conferir` nem em `guardado` para a legislação; em `conferir`, acrescentar como aviso (igual ao Python) `{ "tipo": "arquivo", "caminho": "%ProgramFiles%\\LibreOffice\\program\\soffice.com", "nome": "LibreOffice (PDF dos despachos; instalado pelo instalador do kit PMVNI)", "aviso": true }` — sem passo de instalação próprio (quem instala é o instalador do kit, receita `32-kit-pmvni`). Se o `MAPA-DO-AMBIENTE.md` não cita as ligações editor ↔ pastas do PMVNI, editor ↔ kit (modelos de despacho) ↔ LibreOffice e editor ↔ kit (`03 - LEGISLACAO`, só leitura e opcional; o texto vem embutido), acrescentar uma linha para cada. Abrir **via `explorer.exe`** `Recriar PC\sistema\motor\Capturar agora.cmd` e depois `Conferir.cmd`; ler `Recriar PC\sistema\CONFERENCIA.md` e corrigir o que ele apontar sobre este app (sem mexer no motor).
- [ ] **Step 7:** conferir que `tests\saida` e os builds sumiram (a limpeza do `build-release` já apaga; o roteiro, a `fidelidade.py` e os `testes_extrair.py` rodados depois deixam `tests\saida`: apagar só ela, conferindo o caminho sob a raiz) e que não há `tools\legislacao\__pycache__`; o `src/Pmvni.Legislacao/Dados/legislacao.pmvleg` versionado **fica** (é fonte do build, não saída). Commit — `docs: entrega da Etapa 2` (inclui `docs/roteiro-etapa-2.resultado.json`, sem capturas, PDFs ou dados).

### Task 15 (Tarefa 15): Serviço de despacho — modelos do kit, conteúdo do relatório, DOCX, validação, PDF e recibo

> Desenho §10 (vinculante). Executada depois da Tarefa 13 e antes da 16; a 14 vem por último.

**Files:**
- Create: `src/Pmvni.Revisao/Despachos/{RegistroModelos.cs,TextosFixos.cs,ConteudoDespacho.cs,Etapas.cs,NomesDespacho.cs,PreenchimentoDocx.cs,ValidacaoDespacho.cs,ConversorPdf.cs,EmissaoDespacho.cs,ReciboDespacho.cs}`
- Create: `src/Pmvni.Composicao/InfoPdf.cs` (o `InfoPdf` sai de `src/Pmvni.Composicao.Web/Exportador.cs` sem mudar o comportamento, namespace `Pmvni.Composicao`; quem o usa só ganha o `using`) com `static IReadOnlyList<string> InfoPdf.Fontes(byte[] pdf)`
- Create: `tests/Pmvni.Revisao.Testes/Ficticios/{DocxFicticio.cs,PdfFicticio.cs,RelatorioFicticio.cs,Conversores.cs}`, `tests/Pmvni.Revisao.Testes/Despachos/{RegistroModelosTestes.cs,TextosFixosTestes.cs,ConteudoDespachoTestes.cs,EtapasNomesTestes.cs,PreenchimentoDocxTestes.cs,ValidacaoDespachoTestes.cs,EmissaoDespachoTestes.cs}`
- Modify: `tests/Pmvni.Revisao.Testes/Ambiente.cs` (`RaizKit`: `PMVNI_KIT_RAIZ` ou o padrão do desenho §10.10; `ExigirKit()` faz `Assert.Fail("Kit PMVNI ausente: <raiz> (defina PMVNI_KIT_RAIZ)")`, como a Etapa 1)
- Modify: `src/Pmvni.Verificador/{Argumentos.cs,Program.cs}`, `tests/Pmvni.Web.Testes/VerificadorArgumentosTestes.cs`, `tools/fidelidade.py` (v6), `docs/fidelidade.md`

**Interfaces:**
- Consumes: `DadosEfetivos.Gerar`, `ConferenciaRevisao.Erros`, `Projeto`, `Pacote.Ler`/`PacoteAberto`, `TextoEditado.Normalizar`, `Situacoes.Sit`, `ContextoUsuario`, `Importador.Importar` (Verificador), `Processo`, `RelatorioEncontrado` e `TipoDespacho` (Tarefa 12; só `Correcao` e `Aprovacao` são emitidos, `NaoIdentificado` nunca).
- Produces (tudo síncrono e puro, para `Task.Run`; nada grava no kit; nada grava na pasta do processo fora de `EmissaoDespacho.Gravar`/`GravarPdf`):
  - **RegistroModelos:** `sealed record ModeloDespacho(TipoDespacho Tipo, string Papel, string CaminhoRelativo, string Caminho, string Sha256, int RevisaoRegistro, string Sha256Registro)`; `sealed record ModelosDisponiveis(IReadOnlyList<ModeloDespacho> Modelos, IReadOnlyDictionary<TipoDespacho, string> Indisponiveis)`; `static ModelosDisponiveis RegistroModelos.Ler(string raizKit)`; `const string RegistroModelos.CaminhoRegistro = "02 - MODELOS DE DOCUMENTOS/TEMPLATES_OFICIAIS.json"`; `static string Rotulo(TipoDespacho t)` → `"Correção"`/`"Aprovação"`; `static string NomeNoArquivo(TipoDespacho t)` → `"Correcao"`/`"Aprovacao"`. Papéis: `correction`→Correção, `approval_simple`→Aprovação; os outros são ignorados. `Ler` também chama `TextosFixos.Ler` e `PreenchimentoDocx.LerModelo` e põe em `Indisponiveis` o motivo de qualquer falha. Motivos exatos: `"Kit PMVNI não encontrado em <raiz>. Escolha a pasta do kit em Ajustes."` (os dois tipos); `"O registro de modelos do kit não tem o modelo <papel>."`; `"O registro de modelos do kit está na revisão <n>; o editor precisa da revisão 10 ou posterior."`; `"O registro aponta um modelo fora da pasta do kit: <path>."`; `"O modelo <path> não foi encontrado no kit."`; `"O modelo <path> não confere com o registro do kit (sha256 diferente)."`; `"O modelo <path> não está no formato esperado (âncora \"<ancora>\")."`.
  - **TextosFixos:** `sealed record ParagrafoFixo(string Id, string Texto)` (lacunas como `{intervencao}` e `{area}`); `sealed record TextosFixos(TipoDespacho Tipo, string Arquivo, string Sha256, IReadOnlyList<ParagrafoFixo> Paragrafos)`; `static TextosFixos TextosFixos.Ler(string raizKit, TipoDespacho t)`; `sealed class TextosForaDoPadraoException(string arquivo, string detalhe)` com a mensagem `"O texto de referência do kit (<arquivo>) não está no formato esperado: <detalhe>."`; `const string PastaReferencias = "01 - PLUGINS/plugins/despacho/skills/despacho/references"`. Aprovação = `modelo-despacho-aprovacao-gabinete.md`, ids `considerando_pedido`, `considerando_analise`, `considerando_area`, `encaminho`; Correção = `modelo-despacho-analise-correcao.md`, id `fechamento`. Leitura (desenho §10.3): UTF-8 com ou sem BOM; só o bloco entre a linha `## Conteúdo na ordem do documento` e o próximo `## `; parágrafos = textos entre linhas `<!-- estilo: … -->`, aparados, vazios fora. Aprovação: exatamente 3 que começam por `Considerando` (pedido = o que contém `solicitou`; área = o que contém `área total`; análise = o terceiro) e 1 por `Encaminho`; no pedido o literal `obra nova/alteração de obra/ampliação de obra` vira `{intervencao}`; na área o número `\d{1,3}(?:\.\d{3})*,\d{2}` seguido de `m²` vira `{area}`. Correção: exatamente 1 que começa por `Para prosseguimento`. Qualquer outra forma ⇒ exceção.
  - **ConteudoDespacho:** `enum Intervencao { ObraNova, AlteracaoDeObra, AmpliacaoDeObra }`; `static string ConteudoDespacho.Intervencoes(IEnumerable<Intervencao> i)` (na ordem do enum; 1 = "x", 2 = "x e y", 3 = "x, y e z"); `sealed class PendenciaDespacho { string ItemId; int Numero; string Situacao; string Titulo; string Grupo; string Texto; string Fundamento; bool Incluida; bool Editada; string TextoOriginal; string FundamentoOriginal }`; `sealed class RascunhoDespacho` com `TipoDespacho Tipo`, `string Protocolo`, `string Requerente`, `string Assunto`, `DateOnly Data`, `IReadOnlyList<PendenciaDespacho> Pendencias`, `IReadOnlySet<Intervencao> Intervencoes`, `string AreaTotal`, `IReadOnlySet<string> CamposEditados` e os únicos modificadores `DefinirCabecalho(string campo /* protocolo|requerente|assunto */, string texto)`, `DefinirTexto(string itemId, string texto)`, `DefinirFundamento(string itemId, string texto)`, `DefinirGrupo(string itemId, string texto)`, `Marcar(string itemId, bool incluida)`, `Mover(string itemId, int delta)`, `DefinirIntervencao(Intervencao i, bool marcada)`, `DefinirArea(string texto)` — todo texto passa por `TextoEditado.Normalizar`; `IReadOnlyList<string> Faltando()`; `DespachoFinal Congelar(TextosFixos tx)` (lança `DespachoIncompletoException(IReadOnlyList<string> Faltando)`); `sealed record ExigenciaFinal(string Texto, string Fundamento)`; `sealed record GrupoFinal(string Titulo, IReadOnlyList<ExigenciaFinal> Exigencias)`; `sealed record DespachoFinal(TipoDespacho Tipo, string Protocolo, string Requerente, string Assunto, string DataPorExtenso, IReadOnlyList<GrupoFinal> Grupos, IReadOnlyList<string> Corpo)` (aprovação: os 4 parágrafos resolvidos; correção: `[fechamento]`); `static RascunhoDespacho ConteudoDespacho.Montar(TipoDespacho t, Projeto p, InfoModelo m, EtapaDespacho etapa)`; `static IReadOnlyList<string> Bloqueios(TipoDespacho t, Projeto p, Processo processo, bool somenteLeitura)`; `static IReadOnlyList<string> Avisos(TipoDespacho t, Projeto p)`; `static ExigenciaFinal Exigencia(string providencia, string baseLegal)`; `static bool AreaValida(string area)` (`^(\d{1,3}(\.\d{3})+|\d+),\d{2}$`); `enum EstiloPrevia { Titulo, Rotulo, Destinatario, Grupo, Corpo, Data, Assinatura, Vazio }`; `sealed record ParagrafoPrevia(string Texto, EstiloPrevia Estilo, string? Faltando)`; `static IReadOnlyList<ParagrafoPrevia> Previa(InfoModelo m, RascunhoDespacho r, TextosFixos tx)`.
    Regras de `Montar`: lê `DadosEfetivos.Gerar(p).Json`; `Protocolo` = `capa.processo`, `Requerente` = `capa.requerente` (string; ausente/não string ⇒ ""); `Assunto` = `m.Assunto`; `Data` = `etapa.Data`; Correção: um `PendenciaDespacho` por item incluído (ordem dos dados efetivos) com `situacao ≠ "ATENDE"`, `Numero` = posição, `Titulo`/`Texto`/`Fundamento` = `titulo`/`providencia`/`base_legal` efetivos, `Grupo` = `m.TituloGrupo`, `Incluida` = true; `ItemId` = `DadosGerados.IdsDosItens`. `Congelar`: grupos na ordem da primeira aparição, exigências marcadas na ordem da lista, cada uma `Exigencia(Texto, Fundamento)`; lacunas da aprovação resolvidas.
    `Exigencia`: aparar; tirar um `.` final do fundamento; fundamento vazio ⇒ `(texto, "")`; senão, se o texto termina em `.`, `(texto sem o ponto, " (" + fundamento + ").")`, senão `(texto, " (" + fundamento + ")")`.
    Mensagens exatas — Faltando: `"Protocolo nº"`, `"Requerente"`, `"Assunto"`, `"Ao menos uma exigência marcada"`, `"ITEM 02: texto da exigência"`, `"ITEM 02: grupo"`, `"Tipo de obra (obra nova, alteração de obra ou ampliação de obra)"`, `"Área total a construir (m², ex.: 210,52)"`. Bloqueios: cada erro de `ConferenciaRevisao.Erros(p)` como vem; `"O processo da capa (<capa>) não é o da pasta (<nº com 4 dígitos>/<ano>). Confira se o relatório é deste processo."` (comparação de número e ano como inteiros, capa lida pela regra `numero_ano` do kit `^\s*0*(\d{1,7})\s*(?:[/\-]|\s)\s*((?:19|20)\d{2})\s*$`); capa fora desse formato ⇒ `"O processo da capa (<capa>) não está no formato número/ano; confira o relatório."`; pasta do processo fora do padrão (`Numero`/`Ano` null) ⇒ sem essa conferência, e o número e o ano do nome do arquivo vêm da capa; `"Este relatório foi gravado por uma versão mais nova do editor (somente leitura); o despacho não pode ser emitido a partir dele."`; Correção sem item ≠ ATENDE ⇒ `"Nenhum item incluído tem situação diferente de ATENDE: não há exigência para o despacho de correção."`. Aviso (Aprovação): `"O relatório tem <n> itens com situação diferente de ATENDE (ITEM 02, ITEM 03). Emitir a aprovação mesmo assim?"` (`n = 1` ⇒ `"1 item"`).
  - **Etapas:** `sealed record EtapaDespacho(string Pasta, string Nome, int Numero, DateOnly Data, bool Nova)`; `static EtapaDespacho Etapas.Nova(string pastaProcesso, DateOnly hoje)` (NN = maior `^DESPACHO\s+(\d+)\b`, sem caixa, entre as subpastas + 1; nome `DESPACHO {NN:00} - {aaaa-MM-dd}`; não cria nada); `static IReadOnlyList<EtapaDespacho> Etapas.Existentes(string pastaProcesso)` (só `^DESPACHO\s+(\d{1,3})\s+-\s+(\d{4}-\d{2}-\d{2})$` com data válida; nº desc); `static EtapaDespacho Etapas.Sugerida(string pastaProcesso, DateOnly hoje)` (a existente de maior nº sem `.docx`/`.pdf` iniciado por `Despacho_`/`DESPACHO_`, sem caixa; senão `Nova`); `static string Etapas.DataPorExtenso(DateOnly d)`.
  - **NomesDespacho:** `static string Base(TipoDespacho t, int numero, int ano, string requerente)` (`Despacho_<NomeNoArquivo>_<numero>_<ano>_<Nome>`; `Nome` = NFD sem marcas, tudo que não é `[A-Za-z0-9]` vira espaço, palavras com inicial maiúscula e o resto minúsculo, unidas por `_`, cortadas na última palavra que cabe em 60 caracteres); `sealed record NomesLivres(string Docx, string Pdf, string Recibo)`; `static NomesLivres Livres(string pasta, string nomeBase)` (o primeiro de `nomeBase`, `nomeBase (2)`, `nomeBase (3)`… em que **nenhum** dos três existe).
  - **PreenchimentoDocx:** `enum PapelParagrafo { Fixo, Protocolo, Requerente, Assunto, Destinatario, Grupo, Vaga, Data, LinhaAssinatura }`; `sealed record ParagrafoModelo(string Texto, bool Negrito, string Alinhamento, PapelParagrafo Papel)`; `sealed record InfoModelo(TipoDespacho Tipo, string Assunto, string TituloGrupo /* "" na aprovação */, int Vagas, IReadOnlyList<ParagrafoModelo> Paragrafos)`; `static InfoModelo LerModelo(byte[] docx, TipoDespacho t)`; `static byte[] Preencher(byte[] modelo, DespachoFinal d)`; `sealed class ModeloForaDoPadraoException(string ancora)`. Regras do desenho §10.5: âncoras `"Protocolo nº:"`, `"Requerente:"`, `"Assunto:"` (texto do parágrafo aparado começa por), destinatário = o parágrafo que começa por `"AO "` e termina em `":"`, grupo (só correção) = o parágrafo em negrito logo depois do destinatário, vagas = parágrafos sem texto com `w:jc="both"` consecutivos depois dele, data = o parágrafo que contém `"dia de mês de ano"`, linha de assinatura = o parágrafo cujo texto é só `_` (≥ 10); cada âncora exatamente uma vez. Zip: `ZipArchive` de leitura → `ZipArchive` novo em memória com as mesmas entradas, na mesma ordem, `LastWriteTime` igual, conteúdo copiado sem tocar, exceto `word/document.xml` (XDocument com `LoadOptions.PreserveWhitespace`, gravado com `SaveOptions.DisableFormatting`, UTF-8 sem BOM, `standalone="yes"` mantido).
  - **ValidacaoDespacho:** `static IReadOnlyList<string> Comparar(byte[] modelo, byte[] documento)` — porta de `validate()` do `validate_template_fidelity.py` com as mesmas mensagens: `"Componente original alterado/ausente: <parte>"`, `"Seções, margens, formato, orientação ou vínculos de cabeçalho/rodapé alterados."`, `"Fonte ou tamanho direto não existente no template: <rFonts|sz|szCs>"`, `"Estrutura, propriedades ou colunas das tabelas originais alteradas."`, `"Não foi possível comparar os DOCX: <erro>"`; `static IReadOnlyList<string> Conteudo(byte[] modelo, byte[] documento, DespachoFinal d)`: `"Texto do modelo não preenchido: dia de mês de ano"` (e o mesmo para `XX`, `[ ]`, `{{`, `{intervencao}`, `{area}`), `"O despacho não traz o valor de <Protocolo nº|Requerente|Assunto|data>."`, `"O despacho não traz a exigência <n> do grupo <título>."`, `"Falta a linha em branco antes da assinatura."`, `"Parte do modelo alterada: <parte>"` (toda entrada ≠ `word/document.xml`).
  - **ConversorPdf:** `interface IConversorPdf { bool Disponivel { get; } string? Versao { get; } ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct); }`; `sealed record ResultadoConversao(byte[]? Pdf, string? Motivo /* null, "SEM_LIBREOFFICE", "CONVERSAO_FALHOU", "TEMPO_ESGOTADO" */, string? Detalhe)`; `sealed class ConversorLibreOffice(string? soffice, TimeSpan? limite = null) : IConversorPdf` (limite padrão 5 min) com `static string? Localizar()` (`PMVNI_SOFFICE` se o arquivo existe → `%ProgramFiles%\LibreOffice*\program\soffice.com` → `soffice.exe` → `%ProgramFiles(x86)%\…` → `soffice.com`/`soffice.exe` no PATH) e `static ProcessStartInfo Preparar(string soffice, string copia, string pastaConv)`; `Versao` = `"LibreOffice " + FileVersionInfo(soffice.exe ao lado).ProductVersion`, sem abrir processo. `Converter`: `<trabalho>\conv\copia.docx` (cópia), `Preparar`, `Start`, `WaitForExit(limite)` (estourou ⇒ `Kill(entireProcessTree: true)` e `TEMPO_ESGOTADO`), `copia.pdf` ≥ 1000 bytes começando por `%PDF-` (senão `CONVERSAO_FALHOU` com `"código <n>"`).
  - **EmissaoDespacho:** `sealed record Preparacao(string PastaTrabalho, byte[] Docx, byte[]? Pdf, int? Paginas, IReadOnlyList<string> Fontes, string? SemPdfMotivo, string? MensagemSemPdf, IReadOnlyList<string> Erros)`; `static Preparacao Preparar(DespachoFinal d, ModeloDespacho m, string pastaTrabalho, IConversorPdf conv, CancellationToken ct)` (lê o modelo e confere o sha com `m.Sha256` → `Preencher` → `Comparar` + `Conteudo` → grava `<trabalho>\despacho.docx` → relê → converte → confere sha do DOCX, `InfoPdf.Ler`, `InfoPdf.Fontes` contém um nome que começa por `Verdana`); `sealed record OrigemDespacho(Processo Processo, string CaminhoPacote, string Sha256Pacote, string Sha256DadosEfetivos, int HistoricoN, string AppVersao, IReadOnlyList<string> AvisosConfirmados)`; `sealed record Entrega(string Pasta, string Docx, string? Pdf, string Recibo)`; `static Entrega Gravar(Preparacao p, RascunhoDespacho r, DespachoFinal d, ModeloDespacho m, TextosFixos tx, EtapaDespacho etapa, OrigemDespacho o, IReadOnlyList<int>? paginasConferidas, string? motorPdf, ContextoUsuario u)`; `static Preparacao PrepararPdf(string docxGravado, string pastaTrabalho, IConversorPdf conv, CancellationToken ct)` e `static Entrega GravarPdf(Preparacao p, string docxGravado, IReadOnlyList<int> paginasConferidas, string? motorPdf, ContextoUsuario u)` (Gerar PDF depois); `static void LimparOrfas(string raizTrabalho)` (subpastas com mais de 1 dia). `Gravar`: recusa com `Erros` ≠ []; com PDF exige `paginasConferidas` = 1..N (`ArgumentException`); cria a pasta da etapa se `Nova` (`Directory.CreateDirectory`; se já existir com o mesmo nome, usa-a); `NomesDespacho.Livres`; cada arquivo em `<nome>.<guid8>.tmp` oculto na mesma pasta e `File.Move(tmp, final, overwrite: false)` (se outro processo criar o nome no meio, recomeça com o próximo livre); recibo por último; apaga a pasta de trabalho; em qualquer exceção apaga os `.tmp` (nunca apaga um arquivo final que já existia).
    Mensagens exatas: `"O LibreOffice não está instalado neste computador. O despacho será gravado só em Word (DOCX). Para o PDF, rode o instalador do kit (INSTALAR_KIT_PMVNI.cmd) e use Despachos › Gerar PDF."`; `"O LibreOffice não conseguiu gerar o PDF (<detalhe>). O despacho será gravado só em Word (DOCX); use Despachos › Gerar PDF para tentar de novo."` (`TEMPO_ESGOTADO`: detalhe `"tempo esgotado"`); `"O PDF saiu sem a fonte Verdana (fontes: <lista>); será gravado só o DOCX."`; erros: `"O DOCX mudou durante a conversão; gere o despacho de novo."`, `"O modelo do kit mudou depois de aberto; abra o Novo despacho de novo."`; `"Confira as <n> páginas do PDF antes de gravar."` (`n = 1` ⇒ `"Confira a página do PDF antes de gravar."`); `GravarPdf` sem recibo do editor ⇒ `"Este despacho não foi gerado pelo editor (sem recibo); o PDF não é gerado por aqui."`.
  - **ReciboDespacho:** `static string CaminhoDe(string docx)` (`x.docx` → `x.recibo-editor.json`); `static JsonObject Ler(string caminho)`; o recibo (UTF-8 sem BOM, indentado, chaves nesta ordem):

```json
{ "recibo_versao": 1, "tipo": "correcao", "papel": "correction",
  "registro": { "revisao": 10, "sha256": "62da…" },
  "modelo": { "caminho": "02 - MODELOS DE DOCUMENTOS/DESPACHO/MODELO DESPACHO CORREÇÃO.docx", "sha256": "856d…" },
  "textos_fixos": { "arquivo": "modelo-despacho-analise-correcao.md", "sha256": "465d…" },
  "relatorio": { "arquivo": "Relatorio_0001_2099.pmvrel", "sha256_pacote": "…", "sha256_dados_efetivos": "…", "historico_n": 1 },
  "processo": { "numero": 1, "ano": 2099, "pasta": "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)" },
  "etapa": { "pasta": "DESPACHO 03 - 2099-03-10", "numero": 3, "data": "2099-03-10", "nova": true },
  "exigencias": [ { "item": "it_…", "numero": 2, "situacao": "NAO_COMPROVADO", "grupo": "GERAL E DOCUMENTAÇÃO", "texto_editado": false } ],
  "campos_editados": [], "avisos_confirmados": [],
  "docx": { "arquivo": "Despacho_Correcao_1_2099_Fulano_De_Exemplo_Ficticio.docx", "sha256": "…", "validacao": "OK" },
  "pdf": { "arquivo": "….pdf", "sha256": "…", "paginas": 2, "fontes": ["Verdana", "Verdana-Bold"], "motor": "LibreOffice 26.8.3.1" },
  "sem_pdf_motivo": null,
  "paginas_conferidas": { "paginas": [1, 2], "usuario": "…", "em": "2099-03-10T12:00:00Z" },
  "usuario": "…", "app_versao": "…", "gerado_em": "2099-03-10T12:00:00Z" }
```
  (`pdf` e `paginas_conferidas` são `null` sem PDF; `exigencias` é `[]` na aprovação; `GravarPdf` regrava o recibo por temporário + troca, preenchendo `pdf`/`paginas_conferidas` e zerando `sem_pdf_motivo`.)

Fictícios do teste (escritos no código, nada copiado do kit): `DocxFicticio.Criar(params string[] paragrafos)` monta um DOCX mínimo (`[Content_Types].xml`, `_rels/.rels`, `word/document.xml`, `word/_rels/document.xml.rels`, `word/styles.xml` com Verdana nos `docDefaults`, um `sectPr`); cada parágrafo é um run `sz 24`; `""` = vaga `jc both`; prefixo `**` = negrito; prefixo `>` = `jc right`. `DocxFicticio.KitFicticio(pasta, …)` grava uma árvore de kit fictícia (registro, DOCX e `.md` fictícios com textos inventados no teste). `PdfFicticio.Criar(int paginas, params string[] fontes)` monta um PDF 1.4 sem compressão (`/Type /Page` × n, `/Count n`, `/MediaBox [0 0 595.28 841.89]`, um `/BaseFont /<fonte>` por fonte). `RelatorioFicticio.Curto(processo = "0001/2099", requerente = "FULANO DE EXEMPLO (FICTICIO)")` = `tests/corpus/curto.json` com `capa.processo`/`capa.requerente` trocados (por `JsonFiel`); `RelatorioFicticio.Arvore(PastaDeTeste)` = cópia de `tests/corpus/processos` + `Relatorio_0001_2099.pmvrel` importado desse relatório na pasta do 0001. `Conversores.cs`: `ConversorFalso(Func<string, ResultadoConversao>)` e `ConversorAusente`. Testes que mexem em variável de ambiente ficam na coleção `[Collection("Ambiente")]` (sem paralelismo). Hoje fixo nos testes: `new DateOnly(2099, 3, 10)`.

- [ ] **Step 1: Write the failing tests** (modelos e referências do kit lidos no lugar com `Ambiente.ExigirKit()`; tudo o que é gravado vai para `Ambiente.PastaDeTeste`)

```csharp
// RegistroModelosTestes
[Fact] void registro_do_kit_oferece_correcao_e_aprovacao()
    // Ler(kit): Modelos.Select(m => (m.Tipo, m.Papel)) == [(Correcao,"correction"), (Aprovacao,"approval_simple")]; RevisaoRegistro == 10; Indisponiveis vazio;
    // Sha256 == "856dcf9ee49da68f5a7ea440bda43dc5a6f5ab06453c501b94b9a222f4039d61" e "f5598577e2954726e3a2ce99d06fc728e6447dc461101e6c2df2280d82e152b7"
    // (falha de propósito se o kit mudar: rever o desenho §10.1 antes de ajustar o teste)
[Fact] void so_papeis_de_despacho_conhecidos_viram_tipo()
    // kit fictício com report, correction, approval_complete e declaration → Modelos == [Correcao]; Indisponiveis[Aprovacao] == "O registro de modelos do kit não tem o modelo approval_simple."
[Fact] void sha_diferente_deixa_o_tipo_indisponivel()     // motivo exato "O modelo <path> não confere com o registro do kit (sha256 diferente)."
[Fact] void caminho_fora_da_raiz_e_recusado()             // "path": "../fora.docx" → "O registro aponta um modelo fora da pasta do kit: ../fora.docx."
[Fact] void revisao_antiga_e_recusada()                   // templates_revision 9 → os dois com "O registro de modelos do kit está na revisão 9; o editor precisa da revisão 10 ou posterior."
[Fact] void kit_ausente_deixa_os_dois_indisponiveis()     // raiz inexistente → "Kit PMVNI não encontrado em <raiz>. Escolha a pasta do kit em Ajustes."
[Fact] void ler_nao_grava_nada_no_kit()   // (caminho, tamanho, data) de "02 - MODELOS DE DOCUMENTOS" e da pasta references iguais antes e depois de Ler
// TextosFixosTestes
[Fact] void aprovacao_tem_4_paragrafos_com_as_lacunas()
    // Sha256 == "0a0da0207f9ce04e478b2e1abd5e3f0e1a86a8f54fc04035fddc6e539eb4dbe7"; Ids == [considerando_pedido, considerando_analise, considerando_area, encaminho];
    // pedido contém "{intervencao}" uma vez e não contém "obra nova/"; área contém "{area}m²" e nenhum dígito; nenhum contém "XX"
[Fact] void correcao_tem_o_fechamento()   // Sha256 == "465d6432580f867ef60b0537931fc77c44b54cc9e63c3e1fe92bf007ed441e21"; 1 parágrafo, Id "fechamento", começa por "Para prosseguimento"
[Theory] [InlineData("sem_secao")] [InlineData("dois_encaminho")] [InlineData("sem_lacuna_de_area")] [InlineData("sem_fechamento")]
void referencia_fora_do_formato_lanca(string caso)   // .md fictício → TextosForaDoPadraoException, mensagem começa por "O texto de referência do kit (<arquivo>)"
// ConteudoDespachoTestes (RelatorioFicticio.Curto importado; InfoModelo do kit)
[Fact] void correcao_traz_os_itens_que_nao_atendem_na_ordem()
    // Pendencias.Select(p => (p.Numero, p.Situacao)) == [(2,"NAO_COMPROVADO"), (3,"NAO_ATENDE")]; todas Incluida; Grupo == "GERAL E DOCUMENTAÇÃO";
    // Texto == providencia efetiva do item 2 do curto; Fundamento == "Código de Obras (LC nº 70/1990), art. 102."
[Fact] void situacao_e_texto_revisados_valem_e_excluido_nao_entra()
    // DefinirSituacao(item1,"NAO_ATENDE") + justificativa; Excluir(item3); DefinirCampo(item2,"providencia","Apresentar a cota do recuo.")
    // → [(1,"NAO_ATENDE"), (2,"NAO_COMPROVADO")]; Texto do 2º == "Apresentar a cota do recuo."
[Fact] void cabecalho_vem_da_capa_e_do_modelo()   // "0001/2099", "FULANO DE EXEMPLO (FICTICIO)", Assunto "SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", Data == etapa.Data
[Fact] void exigencia_com_fundamento()
    // ("Apresentar a cota.", "Código de Obras (LC nº 70/1990), art. 102.") → ("Apresentar a cota", " (Código de Obras (LC nº 70/1990), art. 102).");
    // ("Apresentar a cota", "") → ("Apresentar a cota", ""); ("Apresentar a cota", "LC 70/1990") → ("Apresentar a cota", " (LC 70/1990)")
[Fact] void faltando_lista_o_que_o_usuario_precisa_preencher()
    // DefinirTexto(2º, "") → ["ITEM 02: texto da exigência"]; desmarcar os 2 → ["Ao menos uma exigência marcada"]; DefinirCabecalho("requerente","") → contém "Requerente";
    // aprovação recém-montada → ["Tipo de obra (obra nova, alteração de obra ou ampliação de obra)", "Área total a construir (m², ex.: 210,52)"]
[Theory] [InlineData("210,52", true)] [InlineData("1.234,56", true)] [InlineData("1234,56", true)] [InlineData("210.52", false)]
[InlineData("210", false)] [InlineData("12.34,56", false)] [InlineData("", false)]
void area_total_no_formato_brasileiro(string area, bool valida)
[Fact] void intervencoes_por_extenso()
    // [ObraNova] → "obra nova"; [AmpliacaoDeObra, AlteracaoDeObra] → "alteração de obra e ampliação de obra"; as 3 → "obra nova, alteração de obra e ampliação de obra"
[Fact] void congelar_resolve_lacunas_e_grupos()
    // aprovação {ObraNova} + "210,52" → Corpo[0] termina com "para a execução de obra nova."; Corpo[2] contém "é de 210,52m²."; nenhum "{"
    // correção com grupos [G, "PRANCHA 01", G] → Grupos == [(G, 2 exigências), ("PRANCHA 01", 1)]; Corpo == [fechamento]
[Fact] void congelar_com_faltando_lanca()   // DespachoIncompletoException.Faltando == r.Faltando()
[Fact] void textos_do_usuario_sao_texto_puro()   // DefinirTexto(id, "  a\r\nb <i>c</i> & d ") → "a b <i>c</i> & d"; Editada; CamposEditados contém "exigencia:<id>:texto"
[Fact] void bloqueios_do_relatorio()
    // situação mudada sem justificativa → contém a mensagem exata da Tarefa 3; capa "9001/2026" na pasta 0001/2099 →
    // "O processo da capa (9001/2026) não é o da pasta (0001/2099). Confira se o relatório é deste processo."; capa "sem número" → formato; somenteLeitura → mensagem exata
[Fact] void correcao_sem_item_pendente_e_bloqueada()   // os 3 em ATENDE (com justificativa) → a mensagem exata
[Fact] void aprovacao_com_pendencias_avisa()   // Avisos(Aprovacao) == ["O relatório tem 2 itens com situação diferente de ATENDE (ITEM 02, ITEM 03). Emitir a aprovação mesmo assim?"]; Avisos(Correcao) vazio
[Fact] void previa_segue_o_modelo_e_marca_o_que_falta()
    // correção: textos na ordem contêm "INFORMAÇÕES GERAIS", "Protocolo nº: 0001/2099", "GERAL E DOCUMENTAÇÃO", as 2 exigências, o fechamento,
    // "Venda Nova do Imigrante/ES, 10 de março de 2099"; os últimos parágrafos == os do modelo a partir da linha de assinatura;
    // aprovação sem área → um ParagrafoPrevia com Faltando == "Área total a construir (m², ex.: 210,52)"
// EtapasNomesTestes (RelatorioFicticio.Arvore)
[Fact] void etapa_nova_e_a_seguinte_das_existentes()   // 0001 → Nome "DESPACHO 03 - 2099-03-10", Numero 3, Nova; nada criado
[Fact] void pasta_sem_data_tambem_conta_no_numero()   // + "DESPACHO 7 rascunho" → "DESPACHO 08 - 2099-03-10"; Existentes não a lista
[Fact] void processo_sem_despacho_comeca_em_01()      // pasta sem DESPACHO → "DESPACHO 01 - 2099-03-10"
[Fact] void sugerida_e_a_ultima_etapa_ainda_sem_despacho()
    // 0001 (a 02 tem DESPACHO_APROVACAO_0001_2099.pdf) → nova 03; com "DESPACHO 03 - 2099-03-01\relatorio.pdf" → a 03 existente, Data 2099-03-01, Nova false
[Fact] void existentes_mais_recente_primeiro()        // 0001 → [(2, 2099-02-20), (1, 2099-01-15)]
[Fact] void data_por_extenso()   // 2026-11-14 → "14 de novembro de 2026"; 2026-12-01 → "1º de dezembro de 2026"; 2099-03-05 → "5 de março de 2099"
[Fact] void nome_base_como_o_kit()
    // (Correcao, 1, 2099, "FULANO DE EXEMPLO (FICTICIO)") → "Despacho_Correcao_1_2099_Fulano_De_Exemplo_Ficticio";
    // (Aprovacao, 9001, 2026, "CONDOMÍNIO RESIDENCIAL EXEMPLO ALFA") → "Despacho_Aprovacao_9001_2026_Condominio_Residencial_Exemplo_Alfa"
[Fact] void nome_longo_corta_em_palavra()   // 30 × "PALAVRA" → parte do nome com ≤ 60 caracteres, terminando em "Palavra"
[Fact] void nomes_livres_nunca_sobrescrevem()
    // pasta vazia → "base.docx/.pdf/.recibo-editor.json"; só "base.pdf" existe → "base (2).*"; e "base (2).recibo-editor.json" também → "base (3).*"
// PreenchimentoDocxTestes (modelos do kit lidos no lugar)
[Theory] [InlineData("Correcao")] [InlineData("Aprovacao")]
void so_o_document_xml_muda(string tipo)   // mesmas entradas na mesma ordem, mesmo LastWriteTime; bytes descomprimidos iguais em toda entrada ≠ word/document.xml
[Fact] void ler_modelo_acha_as_ancoras()   // Correção: Assunto "SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", TituloGrupo "GERAL E DOCUMENTAÇÃO", Vagas 4; Aprovação: TituloGrupo "", Vagas 4
[Fact] void valores_do_cabecalho_em_negrito_depois_do_rotulo()
    // "Protocolo nº:" → runs ["Protocolo nº:", " 0001/2099"], o 2º com <w:b/> e <w:sz w:val="24"/>; "Requerente: " → ["Requerente: ", "FULANO DE EXEMPLO (FICTICIO)"]
[Fact] void data_por_extenso_nos_dois_modelos()   // texto do parágrafo da data == "Venda Nova do Imigrante/ES, 10 de março de 2099"; "dia de mês de ano" não aparece
[Fact] void exigencias_ocupam_as_vagas_e_as_que_sobram_saem()
    // 2 exigências + fechamento: entre o título de grupo e a data, 3 parágrafos com o pPr da vaga do modelo, runs [texto, fundamento] (fechamento: 1 run);
    // nenhuma vaga vazia sobra; o parágrafo vazio antes do "___" continua
[Fact] void mais_exigencias_que_vagas_clonam_a_vaga()   // 7 exigências + fechamento → 8 parágrafos com o pPr da vaga
[Fact] void grupos_clonam_o_titulo_de_grupo()   // 2º título com o mesmo pPr e rPr do "GERAL E DOCUMENTAÇÃO" e texto "PRANCHA 01"
[Fact] void aprovacao_ocupa_as_4_vagas_com_o_corpo()   // os 4 textos de Corpo, na ordem; destinatário "AO SETOR DE FISCALIZAÇÃO:" intacto
[Fact] void clones_sem_paraId_repetido()
[Fact] void caracteres_especiais_e_emoji_sao_texto()   // "a & <b> \"x\" 'y' 😀" → w:t com o mesmo texto; XDocument.Parse ok; nenhum w:b novo nesse run
[Fact] void ancora_ausente_ou_repetida_lanca()   // DocxFicticio sem "Requerente:" → Ancora == "Requerente:"; com dois "Protocolo nº:" → Ancora == "Protocolo nº:"
// ValidacaoDespachoTestes
[Theory] [InlineData("Correcao")] [InlineData("Aprovacao")]
void despacho_gerado_passa_na_comparacao_do_kit(string tipo)   // Comparar(modelo, Preencher(...)) vazio e Conteudo(...) vazio
[Fact] void parte_fixa_alterada_e_apontada()      // styles.xml com um byte trocado → "Componente original alterado/ausente: word/styles.xml"
[Fact] void fonte_direta_nova_e_apontada()        // run com <w:rFonts w:ascii="Arial" w:hAnsi="Arial"/> → "Fonte ou tamanho direto não existente no template: rFonts"
[Fact] void secao_alterada_e_apontada()           // pgMar top 1417 → 1418 → "Seções, margens, formato, orientação ou vínculos de cabeçalho/rodapé alterados."
[Fact] void resto_do_modelo_e_apontado()          // "dia de mês de ano" restante → "Texto do modelo não preenchido: dia de mês de ano"
[Fact] void falta_de_linha_antes_da_assinatura_e_apontada()   // parágrafo vazio antes do "___" removido → "Falta a linha em branco antes da assinatura."
[Fact] void valor_ausente_e_apontado()            // requerente apagado do XML → "O despacho não traz o valor de Requerente."
// EmissaoDespachoTestes (RelatorioFicticio.Arvore; pasta de trabalho em PastaDeTeste)
[Fact] void preparar_gera_docx_e_pdf_so_na_pasta_de_trabalho()
    // ConversorFalso(PdfFicticio(2, "BAAAAA+Verdana", "CAAAAA+Verdana-Bold")) → Erros vazio; Paginas 2; Fontes ["Verdana", "Verdana-Bold"]; SemPdfMotivo null;
    // lista e sha dos arquivos da árvore de processos iguais aos de antes
[Fact] void sem_libreoffice_fica_so_o_docx()       // ConversorAusente → Pdf null; SemPdfMotivo "SEM_LIBREOFFICE"; MensagemSemPdf == a mensagem exata
[Fact] void conversao_que_falha_fica_so_o_docx()   // Motivo "CONVERSAO_FALHOU", Detalhe "código 1" → "O LibreOffice não conseguiu gerar o PDF (código 1). O despacho será gravado só em Word (DOCX); use Despachos › Gerar PDF para tentar de novo."
[Fact] void pdf_sem_verdana_nao_e_entregue()       // PdfFicticio(1, "ArialMT") → Pdf null; "SEM_VERDANA"; "O PDF saiu sem a fonte Verdana (fontes: ArialMT); será gravado só o DOCX."
[Fact] void docx_alterado_durante_a_conversao_e_erro()   // conversor falso que regrava o docx → Erros == ["O DOCX mudou durante a conversão; gere o despacho de novo."]
[Fact] void modelo_alterado_depois_de_aberto_e_erro()    // ModeloDespacho com outro Sha256 → Erros == ["O modelo do kit mudou depois de aberto; abra o Novo despacho de novo."]
[Fact] void gravar_exige_todas_as_paginas()   // [1] de 2 → "Confira as 2 páginas do PDF antes de gravar."; PDF de 1 página e null → "Confira a página do PDF antes de gravar."
[Fact] void gravar_cria_a_etapa_nova_com_os_tres_arquivos()
    // 0001, etapa nova → "DESPACHO 03 - 2099-03-10\Despacho_Correcao_1_2099_Fulano_De_Exemplo_Ficticio.{docx,pdf,recibo-editor.json}";
    // nenhum .tmp; pasta de trabalho apagada; os outros arquivos da árvore iguais (lista e sha)
[Fact] void gravar_na_etapa_existente_nunca_sobrescreve()   // 2× na "DESPACHO 02 - 2099-02-20" → 2ª com " (2)"; sha dos da 1ª e do DESPACHO_APROVACAO_0001_2099.pdf iguais
[Fact] void recibo_com_origens_e_hashes()
    // tipo "correcao"; papel "correction"; registro.revisao 10; modelo.sha256 == m.Sha256; textos_fixos.sha256 == tx.Sha256; relatorio.sha256_pacote == sha do .pmvrel;
    // docx.sha256/pdf.sha256 == sha dos arquivos; pdf.paginas 2; exigencias.numero == [2,3]; paginas_conferidas.paginas == [1,2]; usuario == u.Usuario
[Fact] void gravar_so_docx_registra_o_motivo()   // SEM_LIBREOFFICE → 2 arquivos (docx, recibo); pdf null; sem_pdf_motivo "SEM_LIBREOFFICE"; paginas_conferidas null
[Fact] void gerar_pdf_depois_completa_o_despacho()   // PrepararPdf + GravarPdf([1,2]) → "<base>.pdf"; recibo com pdf preenchido e sem_pdf_motivo null; DOCX com o mesmo sha
[Fact] void gerar_pdf_recusa_docx_sem_recibo_do_editor()   // DESPACHO_CORRECAO_0001_2099.docx da árvore → a mensagem exata
[Fact] void argumentos_do_libreoffice()
    // Preparar(@"C:\LO\soffice.com", @"<t>\conv\copia.docx", @"<t>\conv") → ArgumentList == ["-env:UserInstallation=" + new Uri(@"<t>\conv\perfil").AbsoluteUri,
    // "--headless", "--norestore", "--convert-to", "pdf", "--outdir", @"<t>\conv", @"<t>\conv\copia.docx"]; Environment["TEMP"] == Environment["TMP"] == @"<t>\conv\tmp";
    // CreateNoWindow; !UseShellExecute
[Fact] void localizar_usa_PMVNI_SOFFICE_quando_o_arquivo_existe()   // [Collection("Ambiente")]: arquivo fictício → ele; caminho inexistente → nunca ele
[Fact] void orfas_com_mais_de_um_dia_sao_apagadas()   // 2 subpastas (uma com data de 2 dias atrás) → só a nova fica
[Fact] void info_pdf_lista_fontes_sem_prefixo()   // PdfFicticio(1, "BAAAAA+Verdana", "Verdana-Bold", "CAAAAA+Verdana") → ["Verdana", "Verdana-Bold"]
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/Pmvni.Revisao.Testes -c Debug --nologo --filter FullyQualifiedName~Despachos`
Expected: FAIL (compilação: `RegistroModelos`, `PreenchimentoDocx`… não existem).

- [ ] **Step 3: Implement** os tipos acima (sem pacote NuGet novo: só `System.IO.Compression`, `System.Xml.Linq`, `System.Text.Json`, `System.Diagnostics.Process`) e mover o `InfoPdf`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/Pmvni.Revisao.Testes -c Debug --nologo` → Expected: PASS (todos, inclusive os das Tarefas 1–13).
Run: `dotnet test tests/Pmvni.Web.Testes -c Debug --nologo` → Expected: PASS (o `InfoPdf` movido continua igual).

- [ ] **Step 5: V6 (despacho × validador do kit).** `Pmvni.Verificador despacho <correcao|aprovacao> <dados.json> <saida.docx> --data AAAA-MM-DD [--kit <raiz>] [--intervencao obra-nova|alteracao|ampliacao]… [--area 210,52]`: importa em memória (não grava pacote), monta o rascunho com todas as exigências marcadas, congela, preenche, valida (`Comparar` + `Conteudo`) e grava só o DOCX; imprime `DESPACHO_OK <sha256>`; faltando ⇒ código 4 com a lista; validação ⇒ código 5 com os erros (sem a conferência da pasta do processo, que não existe aqui). Testes em `VerificadorArgumentosTestes`: `despacho_exige_tipo_dados_saida_e_data`, `despacho_aceita_intervencoes_repetidas_e_area`, `despacho_recusa_tipo_desconhecido` (`aprovacao_completa` → `"Tipo de despacho desconhecido: aprovacao_completa (use correcao ou aprovacao)."`). `fidelidade.py v6`: para `curto` (correção) e `curto` (aprovação com `--intervencao obra-nova --area 210,52`) e, se houver, os casos de `tests/local.json` (correção), roda o Verificador com saída em `%USERPROFILE%\.pmvni-fid\<run>\`, importa o validador do kit (`<kit>/01 - PLUGINS/plugins/despacho/skills/despacho/scripts/validate_template_fidelity.py`) por `importlib.util.spec_from_file_location` com `sys.dont_write_bytecode = True` (nada gravado no kit), exige `check_registry(<kit>/02 - MODELOS DE DOCUMENTOS/TEMPLATES_OFICIAIS.json)[1] == []` e `validate(<modelo do papel>, <docx>) == []`; grava `tests/saida/v6.json` só com `{caso, tipo, erros_kit, sha256_docx}`; `tudo` = v1, v2, v3, v5, v6. Registrar a V6 em `docs/fidelidade.md`.
  Run: `dotnet test tests/Pmvni.Web.Testes --filter FullyQualifiedName~VerificadorArgumentosTestes` → Expected: PASS.
  Run: `python -I tools/fidelidade.py v6` → Expected: `V6 [curto-correcao] OK`, `V6 [curto-aprovacao] OK` (e os locais), código 0.

- [ ] **Step 6: Commit** — `git add src/Pmvni.Revisao/Despachos src/Pmvni.Composicao/InfoPdf.cs src/Pmvni.Composicao.Web/Exportador.cs src/Pmvni.App/Roteiro.cs src/Pmvni.Verificador tests/Pmvni.Revisao.Testes tests/Pmvni.Web.Testes tools/fidelidade.py docs/fidelidade.md` · `feat: servico de despacho com modelos do kit, docx, validacao e pdf`

### Task 16 (Tarefa 16): Novo despacho na interface — tipo, prévia editável, conferência das páginas, gravar e abrir

> Desenho §10.2, §10.4, §10.7–§10.9. Executada depois da Tarefa 15; a 14 vem por último.

**Files:**
- Create: `src/Pmvni.App/Logica/FluxoNovoDespacho.cs`, `src/Pmvni.App/Paginas/PaginaNovoDespacho.xaml(.cs)`, `src/Pmvni.App/Despachos/SessaoNovoDespacho.cs`
- Modify: `src/Pmvni.App/Logica/{Faixas.cs,ComandosApp.cs,Ajustes.cs}`, `Paginas/{PaginaDespachos,PaginaProcessos,PaginaRelatorios,PaginaAjustes}.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`
- Test: `tests/Pmvni.Revisao.Testes/{FaixasTestes.cs,AjustesTestes.cs,FluxoNovoDespachoTestes.cs}`

**Interfaces:**
- Consumes: Tarefa 15 inteira; `Processos.Listar/Detalhar`, `Despachos.Listar`, `AberturaExterna`, `MainWindow.GravarAgoraAsync`, `SessaoEdicao.Estado`, `IPerguntas.ConfirmarAsync`, `Pacote.Ler`, `PastaDados.Raiz`, `FaixaDeOpcoes`.
- Produces:
  - `ComandosApp`: `"despachos.novo"` (Menu, rótulo "Novo despacho") com os itens `"despachos.novo.correcao"` ("Correção") e `"despachos.novo.aprovacao"` ("Aprovação"), e `"despachos.gerarPdf"` ("Gerar PDF"). O Menu `despachos.novo` entra em Despachos › Página Inicial (primeiro comando), Processos › Página Inicial (depois de `processos.importarAnalise`) e Relatórios › Página Inicial (último comando); `despachos.gerarPdf` em Despachos › Página Inicial depois de `despachos.abrirPdf`. As abas de cada módulo não mudam.
  - `Ajustes.PastaKit` (string?) e `Ajustes.PastaKitEfetiva` (`PMVNI_KIT_RAIZ` se definida, senão `PastaKit`, senão `%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)`); página Ajustes ganha "Pasta do kit PMVNI" (`FolderPicker`) e a situação dos modelos ("Correção: disponível" ou o motivo de `ModelosDisponiveis.Indisponiveis`, idem Aprovação).
  - `FluxoNovoDespacho` (lógica pura, compilada nos testes): `enum PassoNovoDespacho { Origem, Conteudo, Conferencia, Gravado }`; `sealed record OrigemSugerida(Processo? Processo, string? Pacote, EtapaDespacho? Etapa)`; `static OrigemSugerida Sugerir(Modulo origem, Processo? selecionado, string? pacoteAberto, IReadOnlyList<Processo> processos, Func<Processo, IReadOnlyList<RelatorioEncontrado>> relatorios, DateOnly hoje)` (Relatórios: o processo cuja `Pasta` contém o pacote aberto, e esse pacote; Processos/Despachos: o selecionado e o `.pmvrel` mais novo dele; `Etapa` = `Etapas.Sugerida`); `static string? MotivoDesabilitado(TipoDespacho t, ModelosDisponiveis m, bool compondo)` (`compondo` ⇒ `"Termine ou cancele o despacho em edição."`); `static bool PodeGerar(RascunhoDespacho r)` (= `Faltando()` vazio); `static bool PodeGravar(Preparacao p, IReadOnlyCollection<int> conferidas)`.
  - `MainWindow`: `internal Task<bool> NovoDespachoAsync(TipoDespacho t)` (o mesmo caminho do menu, com a origem do módulo atual), `internal PaginaNovoDespacho NovoDespacho`, `internal IConversorPdf Conversor { get; set; }` (padrão `new ConversorLibreOffice(ConversorLibreOffice.Localizar())`; o roteiro troca), `internal Func<DateOnly> Hoje` (padrão data local; o roteiro fixa), `internal Task<Entrega?> GravarDespachoAsync()` (o clique em "Gravar na pasta do processo"), `internal Task<bool> GerarPdfDoDespachoAsync(string docx)`.

Comportamento (desenho §10.2–§10.4, §10.7–§10.9):
- **Menu:** ao montar a faixa de cada módulo e ao mudar a pasta do kit, `RegistroModelos.Ler(Ajustes.PastaKitEfetiva)` (em `Task.Run`, guardado até a pasta mudar) habilita cada item ou o desabilita com `MotivoDesabilitado` na dica. Nunca há item de tipo que o registro não tem.
- **Abrir:** `NovoDespachoAsync` passa ao módulo Despachos e mostra `PaginaNovoDespacho` no lugar da lista (a lista volta em Cancelar ou depois de gravar); `EmissaoDespacho.LimparOrfas(<dados>\despachos)`; `FluxoNovoDespacho.Sugerir`. Se o relatório escolhido é o aberto em Relatórios e o estado não é Salvo ⇒ `GravarAgoraAsync()` antes; falha ⇒ InfoBar `"O relatório não pôde ser salvo; o despacho usa sempre a versão gravada. Resolva a gravação e tente de novo."` e para. Processo sem `.pmvrel` ⇒ `"Este processo não tem relatório revisado (.pmvrel). Importe a análise da IA em Processos antes de emitir o despacho."`. `Pacote.Ler` do arquivo (somente leitura, sem trava; `Disco.Sha256` vai para a origem); `Bloqueios` ⇒ InfoBar de erro com a lista e só o passo 1 fica ativo; `Avisos` ⇒ `ConfirmarAsync("Emitir a aprovação?", <aviso>, "Emitir mesmo assim", "Voltar")` (Voltar ⇒ fica no passo 1; Emitir ⇒ o aviso vai para `AvisosConfirmados`).
- **Passo 2:** cartões e folha de prévia do desenho §10.4; cada alteração chama o modificador do `RascunhoDespacho` e redesenha a prévia por `ConteudoDespacho.Previa` (texto `[preencher: <rótulo>]` em vermelho só na prévia); a lista `Faltando` fica acima do botão "Gerar e conferir ▸", desabilitado enquanto não estiver vazia.
- **Passo 3:** `Congelar` + `EmissaoDespacho.Preparar` em `Task.Run` com `CancellationToken` (Cancelar interrompe e apaga a pasta de trabalho `<PastaDados.Raiz>\despachos\<guid>`); `Erros` ⇒ lista + "Voltar e editar"; com PDF ⇒ cada página desenhada por `Windows.Data.Pdf.PdfDocument.LoadFromFileAsync` (`StorageFile.GetFileFromPathAsync` do PDF da pasta de trabalho) com largura de 600 DIP, cada uma com a caixa "Conferi a página N" e um "Marcar todas"; linha "Fontes do PDF: Verdana, Verdana-Bold · LibreOffice 26.8.3.1"; "Gravar na pasta do processo" habilitado por `PodeGravar`; sem PDF ⇒ InfoBar de aviso com `MensagemSemPdf` e o botão "Gravar só o DOCX".
- **Gravar:** `EmissaoDespacho.Gravar(...)`; InfoBar de sucesso "Despacho gravado" com `"<n> arquivos em <pasta>. Recibo: <recibo>."` e botões "Abrir DOCX", "Abrir PDF" (se houver) e "Mostrar no Explorador" (por `AberturaExterna`); a lista de Despachos é relida e o novo fica selecionado.
- **Gerar PDF** (Despachos): habilitado só para despacho com `.recibo-editor.json` ao lado e `sem_pdf_motivo` não nulo; abre a página direto no passo 3 com `PrepararPdf` e grava por `GravarPdf`.
- Trocar de módulo, abrir outro Novo despacho ou fechar o app com a página no passo 2 ou 3 ⇒ `ConfirmarAsync("Descartar o despacho em edição?", "Nada foi gravado na pasta do processo.", "Descartar", "Continuar editando")`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] void novo_despacho_nos_tres_modulos_com_os_dois_tipos()
    // Despachos, Processos e Relatórios: aba "Página Inicial" tem "despachos.novo" (Tipo Menu, Rotulo "Novo despacho") com Itens ids
    // ["despachos.novo.correcao", "despachos.novo.aprovacao"] e rótulos ["Correção", "Aprovação"]; em Despachos é o 1º comando; em Processos vem logo depois de "processos.importarAnalise"
[Fact] void gerar_pdf_so_em_despachos()   // "despachos.gerarPdf" só em Despachos › Página Inicial, logo depois de "despachos.abrirPdf"
[Fact] void ids_de_comando_unicos_e_conhecidos()   // (caso existente) continua verde com os ids novos
[Fact] void cada_modulo_tem_a_faixa_do_desenho()   // (caso existente) abas iguais às da Tarefa 13
[Fact] void ajustes_guardam_a_pasta_do_kit()   // Salvar/Carregar com PastaKit; PastaKitEfetiva: PMVNI_KIT_RAIZ > PastaKit > padrão ([Collection("Ambiente")])
[Fact] void sugestao_pelo_relatorio_aberto()
    // RelatorioFicticio.Arvore; pacote "<arvore>\2099\0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)\Relatorio_0001_2099.pmvrel" aberto, origem Relatorios →
    // Processo.Numero 1, Pacote == esse, Etapa.Nome "DESPACHO 03 - 2099-03-10" (hoje 2099-03-10)
[Fact] void sugestao_por_processos_escolhe_o_pmvrel_mais_novo()   // 2 .pmvrel (AlteradoUtc diferentes) → o mais novo
[Fact] void sugestao_sem_contexto_nao_escolhe_nada()   // Despachos sem seleção e nada aberto → (null, null, null)
[Fact] void motivo_desabilitado()   // indisponível → o motivo do registro; compondo → "Termine ou cancele o despacho em edição."; disponível → null
[Fact] void pode_gravar_so_com_todas_as_paginas()   // Paginas 2 + {1} → false; {1,2} → true; sem PDF (SemPdfMotivo "SEM_LIBREOFFICE") + {} → true; Erros ≠ [] → false
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter "FullyQualifiedName~FaixasTestes|FullyQualifiedName~AjustesTestes|FullyQualifiedName~FluxoNovoDespachoTestes"` → Expected: FAIL.
- [ ] **Step 3: Implement** a lógica e passar os testes; depois a página, a sessão e as ligações no `MainWindow`. `dotnet build src/Pmvni.App -c Debug` → Expected: 0 erros; os testes do Step 2 → PASS.
- [ ] **Step 4: Roteiro** (cada passo confere o modelo, a tela e os arquivos; tudo em `tests\saida\roteiro`). Preparação comum: copiar `tests\corpus\processos` para `<pasta>\processos-despacho\` (cópia separada da Tarefa 13) e apontar `Ajustes.PastaProcessos` para ela; `Ajustes.PastaKit` = o kit real (o roteiro só lê); `Hoje` = 2099-03-10; gravar `<pasta>\curto-0001.json` (o `curto` com `capa.processo` `"0001/2099"` e `capa.requerente` `"FULANO DE EXEMPLO (FICTICIO)"`) e importá-lo para `<cópia>\2099\0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)\Relatorio_0001_2099.pmvrel`; `AberturaExterna.Simular = true`.
  - `34_novo_despacho_correcao` (pela faixa de Relatórios, com o pacote aberto): `NovoDespachoAsync(Correcao)` → módulo Despachos; processo 0001, relatório o aberto, etapa nova "DESPACHO 03 - 2099-03-10"; 2 exigências (ITEM 02, ITEM 03), prévia com "Protocolo nº: 0001/2099"; Gerar com o **LibreOffice real** → `Paginas ≥ 1`, `Fontes` contém "Verdana", `InfoPdf.Ler` sem exceção (se o leitor mínimo não ler o PDF do LibreOffice: **parar e relatar**, nunca afrouxar o leitor); marcar todas; Gravar → os 3 arquivos na pasta nova; `ValidacaoDespacho.Comparar(modelo, docx)` vazio; sha do docx e do pdf = os do recibo; Despachos lista o novo (Correção, nº 3, 2099-03-10) selecionado; "Abrir PDF" registra o caminho em `AberturaExterna.Registradas`.
  - `35_novo_despacho_aprovacao` (pela faixa de Processos, 0001 selecionado): aviso de 2 itens (resposta fixa "Emitir mesmo assim"); `Faltando` == as 2 mensagens exatas e "Gerar e conferir" desabilitado; marcar "obra nova" e área "210,52" → habilitado; etapa existente "DESPACHO 03 - 2099-03-10" → Gerar, marcar, Gravar → `Despacho_Aprovacao_1_2099_Fulano_De_Exemplo_Ficticio.{docx,pdf,recibo-editor.json}` na mesma pasta; sha dos arquivos da correção iguais; recibo com `avisos_confirmados` = o aviso exato.
  - `36_despacho_sem_libreoffice` (pela faixa de Despachos, 0001 selecionado): `Conversor = ConversorAusente` → passo 3 mostra a mensagem exata do desenho §10.7 e "Gravar só o DOCX" → `.docx` + recibo com `sem_pdf_motivo` `"SEM_LIBREOFFICE"`, sem `.pdf`; volta o conversor real → Despachos › Gerar PDF nesse despacho → páginas → marcar → Gravar → `.pdf` com o mesmo nome base; recibo com `pdf` preenchido e `sem_pdf_motivo` null.
  - `37_despacho_nao_sobrescreve`: nova correção na "DESPACHO 03 - 2099-03-10" → nomes com " (2)"; sha de todos os arquivos anteriores da pasta iguais; nenhum `.tmp` na árvore.
  - `38_despacho_bloqueios`: (a) `DefinirSituacao` sem justificativa no pacote aberto → Novo despacho mostra a mensagem exata da Tarefa 3 e não passa do passo 1; (b) importar o `dados_0001.json` da cópia (capa `9001/2026`) para `Relatorio_0001_2099_2.pmvrel`, escolhê-lo → `"O processo da capa (9001/2026) não é o da pasta (0001/2099). Confira se o relatório é deste processo."`; (c) `Ajustes.PastaKit` = `<pasta>\kit-vazio` → os dois itens do menu desabilitados com `"Kit PMVNI não encontrado em <pasta>\kit-vazio. Escolha a pasta do kit em Ajustes."`; volta o kit; (d) lista de arquivos da cópia = a de antes + só os arquivos gravados nos passos 34–37 e os `.pmvrel` importados; nada mudou no kit (lista, tamanho e data de `02 - MODELOS DE DOCUMENTOS` e da pasta `references`).
  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` → Expected: testes aprovados, autoteste aprovado, "Executável final: …".
  Run: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1` → Expected: `ok: true`, código 0.
  Run: `python -I tools/fidelidade.py v6` → Expected: os casos `OK`, código 0.
- [ ] **Step 5:** `docs/roteiro-etapa-2.md` (passos 34–38 na tabela; como o roteiro usa o kit no lugar e o LibreOffice real). Commit — `feat: novo despacho com escolha do tipo, previa, conferencia e gravacao`


### Task 17 (Tarefa 17): Legislação compilada — ferramenta de extração, pacote `legislacao.pmvleg` e embutimento

> Desenho §11 (vinculante). Executada depois da Tarefa 16 e antes da 18; a 14 vem por último. Pedido do usuário de
> 14/11/2026 ("A legislação tem que estar compilada… somente o texto. Se houver imagem por, mas comprimir").

**Files:**
- Create: `tools/legislacao/extrair.py` (CLI `gerar` e `conferir`), `tools/legislacao/catalogo.json` (os 61 arquivos do kit, §11.1), `tools/legislacao/ficticios.py` (monta os PDFs fictícios), `tools/legislacao/testes_extrair.py` (unittest)
- Create (fixtures fictícias, versionadas, poucos KB; regeneradas só por `python -I tools/legislacao/ficticios.py`):

```
tests/corpus/legislacao/
  catalogo.json                                   catálogo da fixture (2 incluídos, 2 excluídos)
  kit/03 - LEGISLACAO/MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf          4 páginas (casos da lei)
  kit/03 - LEGISLACAO/MUNICIPAL/ANOTACOES.docx                         0 byte (excluído: não é ato normativo)
  kit/03 - LEGISLACAO/ESTADUAL - CBMES (NORMAS TECNICAS)/NT 99-2099 - FICTICIA.pdf   1 página (itens)
  kit/03 - LEGISLACAO/NORMAS ABNT/NBR 0000 - FICTICIA.pdf              1 página (excluído: ABNT)
  ficticia.pmvleg                                 pacote gerado da fixture com --gerado-em 2099-01-01T00:00:00Z
```
- Create: `src/Pmvni.Legislacao/Pmvni.Legislacao.csproj` (net10.0, sem UI; `<EmbeddedResource Include="Dados\legislacao.pmvleg" LogicalName="legislacao.pmvleg" />`; `<InternalsVisibleTo Include="Pmvni.Legislacao.Testes" />`), `src/Pmvni.Legislacao/{Modelo.cs,PacoteLegislacao.cs,ConferenciaKit.cs}`, `src/Pmvni.Legislacao/Dados/legislacao.pmvleg` (**gerado pela ferramenta e versionado**), `docs/legislacao-embutida.md` (gerado)
- Create: `tests/Pmvni.Legislacao.Testes/` (xUnit 2.9.3, net10.0; `Ambiente.cs` com `PastaDeTeste` em `tests/saida/legislacao/<nome>-<guid>` e `RaizKit` como na Tarefa 15), `{PacoteLegislacaoTestes.cs,ConferenciaKitTestes.cs,PacoteEmbutidoTestes.cs}`
- Modify: `EditorRelatoriosPMVNI.sln` (os dois projetos novos), `src/Pmvni.App/Pmvni.App.csproj` (referência a `Pmvni.Legislacao`), `src/Pmvni.App/Autoteste.cs` (bloco `legislacao` no JSON), `tools/build-release.ps1` (linha de resultado e aviso da legislação, sem falhar), `.gitignore` (`tools/legislacao/__pycache__/`, por garantia)

**Interfaces:**
- Consumes: kit em `<kit>\03 - LEGISLACAO` (somente leitura); `Ajustes.PastaKitEfetiva` (Tarefa 16) só no autoteste.
- Produces (Python, `python -I`; a ferramenta põe `sys.dont_write_bytecode = True` e os testes também, antes de importar `extrair` pelo caminho do próprio arquivo — com `-I` a pasta do script não entra no `sys.path`):
  - `python -I tools/legislacao/extrair.py gerar --kit <raiz> [--catalogo tools/legislacao/catalogo.json] [--saida src/Pmvni.Legislacao/Dados/legislacao.pmvleg] [--relatorio docs/legislacao-embutida.md] [--gerado-em <ISO UTC>] [--aviso-mb 11] [--limite-mb 12]` — códigos: 0 gerado ou "Nada mudou"; 2 catálogo inválido; 3 PDF ilegível; 4 acima do limite. Saída em uma linha: `Legislação embutida: <n> textos, <e> excluídos, <i> imagens, <p> páginas sem texto, <x,xx> MB (conjunto <sha8>).` ou `Nada mudou (conjunto <sha8>).`; acima do aviso, mais `AVISO: o pacote da legislação passou de 11 MB.`
  - `python -I tools/legislacao/extrair.py conferir --kit <raiz> [--pacote …]` — 0 igual; 1 diferente, com linhas `DIFERENTE <rel>`, `NOVO <rel>`, `AUSENTE <rel>` (rel com `/`, relativo a `03 - LEGISLACAO`).
  - Funções: `classificar(raiz_leg, catalogo) -> list[Entrada]` (cada arquivo casa com exatamente uma entrada por `padrao` fnmatch; cada entrada `incluir` casa com exatamente um arquivo), `extrair_pdf(caminho, entrada) -> (lei: dict, imagens: dict[str, bytes])`, `estruturar(blocos, tipo) -> list[dict]`, `reencodar(bytes) -> (webp: bytes, w, h) | None`, `montar_pacote(manifesto, leis, imagens) -> bytes`.
  - Mensagens exatas (código 2): `Arquivo do kit sem entrada no catálogo: <rel>`, `Entrada do catálogo sem arquivo no kit: <padrao>`, `Arquivo casa com mais de uma entrada do catálogo: <rel>`, `Norma ABNT não pode ser embutida: <rel>` (qualquer `incluir: true` com `NBR` ou `ABNT` no caminho, sem caixa); código 4: `O pacote da legislação ficou com <x,xx> MB, acima do limite de <l> MB.`
- Catálogo (`catalogo.json`, à mão; formato):

```json
{ "versao": 1,
  "arquivos": [
    { "padrao": "MUNICIPAL/PDM - LEI 1.382-2020 - TEXTO COMPILADO*.pdf", "incluir": true, "id": "pdm", "esfera": "Municipal",
      "titulo": "Plano Diretor Municipal — Lei nº 1.382/2020 (texto compilado)", "citacao": "Lei Municipal nº 1.382/2020 (PDM)",
      "apelidos": ["pdm", "plano diretor", "1.382", "1382"], "estrutura": "artigos" },
    { "padrao": "FEDERAL/LEI 10.406-2002*.pdf", "incluir": true, "id": "codigo-civil", "esfera": "Federal",
      "titulo": "Código Civil — Lei nº 10.406/2002", "citacao": "Lei nº 10.406/2002 (Código Civil)",
      "apelidos": ["codigo civil", "cc", "10.406", "10406"], "estrutura": "artigos", "abrir_em": "Art. 1.277" },
    { "padrao": "MUNICIPAL/PDM - LEIS DE ALTERACAO (ORIGINAIS)/*.pdf", "incluir": false,
      "motivo": "Lei de alteração já incorporada ao texto compilado do PDM (original digitalizado)." },
    { "padrao": "NORMAS ABNT/*", "incluir": false,
      "motivo": "Norma ABNT protegida por direito autoral (cópia licenciada; não redistribuir)." } ] }
```
  Ordem do catálogo = ordem da árvore: Municipal (PDM, Código de Obras, perímetros 1.271, 1.276, 1.301), Estadual — CBMES (NT 01 parte 1 … NT 23), Federal (na ordem dos arquivos). Esferas exatas: `"Municipal"`, `"Estadual — CBMES"`, `"Federal"`. Ids: `[a-z0-9-]{1,64}` (`pdm`, `codigo-de-obras`, `nt-01-1`, `nt-10-1`, `estatuto-da-cidade`…). Os excluídos do kit real: 6 leis de alteração, `DÚVIDAS DO NOVO PDM.docx` (`"Não é ato normativo (perguntas de trabalho)."`), 3 PDFs ABNT e o `.md` ABNT (`"Não é ato normativo (lista de normas e onde obter)."`) = 11.
- Formato do pacote (desenho §11.2, decisões de detalhe do plano): `manifesto.json` =

```json
{ "formato": "pmvleg", "versao_formato": "1.0.0", "versao_extrator": "1.0.0", "gerado_em": "2026-11-15T12:00:00Z",
  "kit": { "pasta": "03 - LEGISLACAO", "sha256_conjunto": "…64 hex…" },
  "leis": [ { "id": "pdm", "esfera": "Municipal", "titulo": "…", "citacao": "…", "apelidos": ["…"], "estrutura": "artigos",
              "abrir_em": null, "arquivo": "MUNICIPAL/PDM - LEI 1.382-2020 - TEXTO COMPILADO (CAMARA, ATUALIZADO EM 16-10-2026).pdf",
              "sha256": "…", "tamanho": 5638944, "paginas": 81, "paginas_sem_texto": [79], "paginas_vazias": [],
              "unidades": 212, "aviso_fonte": "Este texto não substitui o original publicado e arquivado na Câmara Municipal…" } ],
  "excluidos": [ { "arquivo": "NORMAS ABNT/NBR 9050-2020 - Acessibilidade.pdf", "sha256": "…", "motivo": "…" } ] }
```
  `leis/<id>.json` = `{ "id", "estrutura": [No] }`; `No` = `{ "id": "u17", "tipo": "preambulo|titulo|capitulo|secao|subsecao|artigo|item|anexo", "rotulo": "Art. 45", "numero": "45" | null, "nome": "DAS DISPOSIÇÕES GERAIS" | null, "pagina": 12, "revogado": false, "blocos": [Bloco], "filhos": [No] }` (ids `u1…` na ordem do texto); `Bloco` = `{ "t": "p", "texto": [Trecho] }` · `{ "t": "dispositivo", "id": "d40", "tipo": "par|inciso|alinea", "rotulo": "§ 2º", "texto": [Trecho], "notas": [Trecho] }` · `{ "t": "nota", "texto": [Trecho] }` · `{ "t": "tabela", "pagina", "cabecalho": 1, "linhas": [[{ "texto": "…", "colspan": 1 }]] }` · `{ "t": "imagem", "src": "img/<16 hex>.webp", "w", "h", "pagina" }` · `{ "t": "recorte", "src", "w", "h", "pagina", "texto": "…" }` · `{ "t": "pagina_sem_texto", "pagina" }`; `Trecho` = `{ "t": "…", "r": true }` (`r` só quando riscado). Alínea guarda `"pai": "<id do inciso>"`. `revogado` = todo o texto da unidade riscado. `aviso_fonte` = a 1ª frase que começa por "Este texto não substitui" no PDF, ou null.
  ZIP: entradas em ordem (`manifesto.json`, `leis/*` por id, `img/*` por nome), data 1980-01-01 00:00, Deflate nível 9 para `.json`, Stored para `.webp`; JSON com `ensure_ascii=False`, `sort_keys=False`, separadores `(",", ":")`, UTF-8 sem BOM.
- Produces (C#, `Pmvni.Legislacao`, tudo síncrono, para `Task.Run`):
  - Registros `Manifesto(string Formato, string VersaoFormato, string VersaoExtrator, DateTimeOffset GeradoEm, string PastaKit, string Sha256Conjunto, IReadOnlyList<LeiResumo> Leis, IReadOnlyList<Excluido> Excluidos)`, `LeiResumo(string Id, string Esfera, string Titulo, string Citacao, IReadOnlyList<string> Apelidos, string Estrutura, string? AbrirEm, string Arquivo, string Sha256, long Tamanho, int Paginas, IReadOnlyList<int> PaginasSemTexto, int Unidades, string? AvisoFonte)`, `Excluido(string Arquivo, string Sha256, string Motivo)`, `Lei(string Id, IReadOnlyList<No> Estrutura)`, `No(string Id, string Tipo, string Rotulo, string? Numero, string? Nome, int Pagina, bool Revogado, IReadOnlyList<Bloco> Blocos, IReadOnlyList<No> Filhos)`, `Trecho(string Texto, bool Riscado)`, `abstract record Bloco` com `BlocoParagrafo`, `BlocoDispositivo(string Id, string Tipo, string Rotulo, string? Pai, …)`, `BlocoNota`, `BlocoTabela`, `BlocoImagem`, `BlocoRecorte`, `BlocoPaginaSemTexto` (desserializados com `System.Text.Json`, discriminador `t`).
  - `sealed class PacoteLegislacao : IDisposable { static PacoteLegislacao Abrir(Stream zip); static PacoteLegislacao Embutido(); static long TamanhoEmbutido(); Manifesto Manifesto; Lei Carregar(string id) /* cache */; byte[]? Imagem(string src) }` — leitura segura: só `manifesto.json`, `leis/<id>.json` (id do manifesto) e `img/<16 hex>.webp`; outra entrada, `..`, `\`, caminho absoluto, entrada > 20 MB descomprimida, total > 100 MB ou razão > 100 ⇒ `FormatoLegislacaoException`; imagem sem assinatura `RIFF????WEBP` ⇒ exceção; `versao_formato` com maior > 1 ⇒ `FormatoLegislacaoException("Pacote de legislação em formato mais novo (2.0.0) do que este editor conhece (1.x).")`.
  - `enum EstadoKit { Igual, Diferente, KitAusente }`; `sealed record ResultadoConferencia(EstadoKit Estado, IReadOnlyList<string> Diferentes, IReadOnlyList<string> Novos, IReadOnlyList<string> Ausentes)`; `static ResultadoConferencia ConferenciaKit.Conferir(Manifesto m, string raizKit, CancellationToken ct)` (sha256 de cada arquivo do manifesto, incluídos e excluídos, em `<raiz>\03 - LEGISLACAO`; novos = arquivos que não estão no manifesto; ilegível conta como diferente); `static string? ConferenciaKit.Aviso(ResultadoConferencia r, Manifesto m)` → null se Igual ou KitAusente, senão exatamente `"A legislação do kit mudou depois da versão embutida neste editor (diferentes: 1, novos: 0, ausentes: 0). O texto mostrado é o da versão embutida de 01/01/2099."` (data local de `GeradoEm`, `dd/MM/yyyy`); `static string ConferenciaKit.Resumo(Manifesto m)` → `"Legislação embutida: versão de 01/01/2099, 2 textos, extrator 1.0.0, conjunto do kit 1a2b3c4d"`.

PDFs fictícios (`ficticios.py`, PyMuPDF; `set_metadata` com datas `D:20990101000000Z`, `save(garbage=4, deflate=True, no_new_id=True)` ⇒ bytes fixos; o teste `test_fixtures_sao_estaveis` confere):
- **Lei** (A4, 4 páginas): cabeçalho "CÂMARA MUNICIPAL FICTÍCIA" no topo e rodapé "Página N de 4" nas págs. 1–2; pág. 1: "LEI Nº 9.999, DE 1º DE JANEIRO DE 2099 (FICTÍCIA)", "TÍTULO I" / "DAS DISPOSIÇÕES GERAIS", "CAPÍTULO I" / "DO OBJETO", "Art. 1º Esta Lei fictícia serve para testar a extra-" / "ção do editor.", "§ 1º Primeiro parágrafo, com aviso pré-" / "via." (hifenização sem a palavra "prévia" em outro lugar do texto), "§ 2º Segundo parágrafo. (Redação dada pela Lei nº 9.998/2099)", "Art. 2º São objetivos da extração de tabelas:", "I – primeiro inciso;", "II – segundo inciso:", "a) alínea a;", "b) alínea b.", "Parágrafo único. Parágrafo único fictício.", "Art. 2º-A Artigo incluído. (Incluído pela Lei nº 9.998/2099)", "Art. 3º Texto antigo." **tachado** (por `insert_htmlbox` com `<s>`; se o PyMuPDF não marcar o tachado, desenhar a linha com `draw_line` na meia altura, como o Skia do Planalto) e "Art. 3º Texto vigente. (Redação dada pela Lei nº 9.998/2099)"; pág. 2: "CAPÍTULO II" / "DAS TABELAS", "Art. 4º Os parâmetros constam da tabela:", tabela com bordas 3 × 3 (`Zona | Taxa de ocupação | Coeficiente`, `ZR1 | 60% | 1,2`, `ZC | 80% | 2,0`), imagem PNG 400 × 300 (pixmap gerado), figura vetorial 6 × 4 cm (retângulo e círculo) com o texto "Figura 1" dentro, "ANEXO I" / "QUADRO FICTÍCIO"; pág. 3: só uma imagem raster 2000 × 2800 cobrindo a página (página digitalizada, sem texto); pág. 4: vazia.
- **NT** (1 página): "NORMA TÉCNICA FICTÍCIA Nº 99/2099", "1 OBJETIVO", "1.1 Estabelecer critérios fictícios.", "2 APLICAÇÃO", "2.1 Aplica-se a edificações fictícias.", "2.1.1 Subitem fictício.", "ANEXO A", "Modelo fictício.".
- **NBR** (1 página, "NORMA FICTÍCIA"): só para o caso de exclusão.
- Catálogo da fixture: `lei-ficticia` (Municipal, citação `"Lei nº 9.999/2099 (fictícia)"`, apelidos `["ficticia", "9.999", "9999"]`, `artigos`), `nt-99` (Estadual — CBMES, citação `"NT 99/2099 (fictícia)"`, apelidos `["nt 99", "nt99"]`, `itens`); `MUNICIPAL/ANOTACOES.docx` e `NORMAS ABNT/*` excluídos.

- [ ] **Step 1: Write the failing tests (Python)** — `tools/legislacao/testes_extrair.py`, tudo gravado em `tests/saida/legislacao/<teste>-<uuid>` (apagada no `tearDown`; nunca `%TEMP%`):

```python
def test_fixtures_sao_estaveis(self)          # ficticios.py em pasta de teste → os 3 PDFs byte a byte iguais aos versionados
def test_estrutura_da_lei_ficticia(self)
    # árvore: [preambulo "LEI Nº 9.999…", titulo "TÍTULO I" (nome "DAS DISPOSIÇÕES GERAIS") > capitulo "CAPÍTULO I" >
    #   artigos ["Art. 1º", "Art. 2º", "Art. 2º-A", "Art. 3º" (revogado), "Art. 3º"]; capitulo "CAPÍTULO II" > ["Art. 4º"]; anexo "ANEXO I"]
    # números ["1", "2", "2-A", "3", "3", "4"]; Art. 1º: p "Esta Lei fictícia serve para testar a extração do editor.",
    # dispositivos par ["§ 1º", "§ 2º"], nota do § 2º "(Redação dada pela Lei nº 9.998/2099)"; Art. 2º: incisos ["I", "II"],
    # alíneas ["a)", "b)"] com pai = id do inciso II, par ["Parágrafo único"]
def test_hifenizacao_desfeita_so_com_palavra_conhecida(self)   # "extração" unida (existe no Art. 2º); § 1º == "Primeiro parágrafo, com aviso pré-via."
def test_cabecalho_e_rodape_repetidos_saem(self)   # "CÂMARA MUNICIPAL FICTÍCIA" e "Página 1 de 4" em nenhum trecho
def test_tachado_vira_trecho_riscado_e_unidade_revogada(self)   # 1º "Art. 3º": revogado true, todos os trechos r; 2º: revogado false
def test_tabela_vira_celulas_e_sai_do_texto(self)   # linhas == [["Zona","Taxa de ocupação","Coeficiente"],["ZR1","60%","1,2"],["ZC","80%","2,0"]], cabecalho 1; "ZR1" em nenhum bloco p
def test_imagem_reduzida_para_1600_em_webp(self)   # 400×300 fica 400×300; 2000×2800 vira 1143×1600; bytes começam com b"RIFF" e [8:12] == b"WEBP"
def test_figura_vetorial_vira_recorte_com_texto(self)   # 1 recorte na pág. 2 com texto "Figura 1"; "Figura 1" fora dos blocos p
def test_pagina_sem_texto_nao_tem_ocr(self)   # pág. 3: bloco pagina_sem_texto {pagina: 3} + 1 imagem; nenhum trecho com pagina 3; paginas_sem_texto == [3]
def test_pagina_vazia_omitida(self)   # paginas_vazias == [4]; nenhum bloco com pagina 4
def test_itens_da_nt_ficticia(self)   # item "1" > ["1.1"]; item "2" > ["2.1" > ["2.1.1"]]; anexo "ANEXO A"
def test_catalogo_incompleto_falha(self)   # sem a entrada do .docx → código 2, "Arquivo do kit sem entrada no catálogo: MUNICIPAL/ANOTACOES.docx"
def test_abnt_nunca_entra(self)   # NBR com incluir true → código 2, "Norma ABNT não pode ser embutida: NORMAS ABNT/NBR 0000 - FICTICIA.pdf"
def test_limite_de_tamanho(self)   # --limite-mb 0.001 → código 4, mensagem com "acima do limite de 0.001 MB"; nada gravado na saída
def test_pacote_deterministico_e_igual_a_fixture(self)   # 2 gerações com --gerado-em 2099-01-01T00:00:00Z → bytes iguais entre si e a tests/corpus/legislacao/ficticia.pmvleg
def test_nada_mudou_nao_regrava(self)   # 2ª geração sobre a saída existente: "Nada mudou (conjunto …)." e mesmos bytes/mtime
def test_conferir_aponta_diferencas(self)   # cópia do kit fictício com 1 byte trocado na lei → código 1 e "DIFERENTE MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"; + arquivo novo → "NOVO …"
```

- [ ] **Step 2: Run** `python -I tools/legislacao/testes_extrair.py` → Expected: FAIL (o módulo `extrair` não existe).
- [ ] **Step 3: Implement** `ficticios.py` (rodar uma vez: `python -I tools/legislacao/ficticios.py` grava os 3 PDFs em `tests/corpus/legislacao/kit/…`) e `extrair.py` (desenho §11.2; limiares: faixa de cabeçalho/rodapé 7% da altura e ≥ 50% das páginas; página "sem texto" = nenhum caractere visível fora de espaços; imagem < 16 × 16 px descartada; recorte vetorial: desenhos sem fios (< 2,5 pt) nem fundos brancos (canais > 0,97), grupo ≥ 4 cm², < 85% da página e < 0,02 caractere/pt², a 150 dpi; página sem texto sem imagem raster que cubra ≥ 50% dela ⇒ recorte da página inteira a 110 dpi em cinza; tabela não confiável ⇒ recorte). Gerar a fixture: `python -I tools/legislacao/extrair.py gerar --kit tests/corpus/legislacao/kit --catalogo tests/corpus/legislacao/catalogo.json --saida tests/corpus/legislacao/ficticia.pmvleg --relatorio tests/saida/legislacao/ficticia.md --gerado-em 2099-01-01T00:00:00Z` → Expected: `Legislação embutida: 2 textos, 2 excluídos, 3 imagens, 1 páginas sem texto, 0,0… MB (conjunto …).`, código 0 ("imagens" conta as entradas `img/*` do pacote: o PNG, a página digitalizada e o recorte vetorial).
- [ ] **Step 4: Run** `python -I tools/legislacao/testes_extrair.py` → Expected: `OK` (17 testes), nenhum `__pycache__` em `tools/legislacao`.
- [ ] **Step 5: Gerar o pacote real** (o kit só é lido): `python -I tools/legislacao/extrair.py gerar --kit "%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)"` → Expected: `Legislação embutida: 50 textos, 11 excluídos, …, … MB (conjunto …).`, código 0, tamanho ≤ 11 MB (estimativa medida no desenho §11.3: ≈ 9,8 MB — texto 1,06, imagens 7,25, páginas sem texto 0,22, 113 figuras vetoriais 1,22; acima de 11 MB, medir 1.400 px/WebP 55 e registrar a escolha em `docs/notas-tecnicas.md` antes de commitar; acima de 12 MB a ferramenta falha). Figuras vetoriais ≈ 113 (fora disso, conferir os filtros de fios e fundos brancos do desenho §11.2). Conferir em `docs/legislacao-embutida.md`: PDM com 199 unidades `artigo` e 13 `anexo` (ANEXO XIII não tem título em texto no compilado — a pág. 79 só tem mapas — e **não** é inventado: fica a nota de página sem texto) e 34 tabelas; Código de Obras com 150 `artigo`; Estatuto da Cidade com trechos riscados (> 0); NT 01 parte 6 com 190 páginas; nenhuma linha "NBR" na lista de textos; os 11 excluídos com motivo. Divergência dessas contagens ⇒ investigar a extração antes de seguir (não ajustar o número esperado sem entender). Registrar o tempo da geração.
- [ ] **Step 6: Write the failing tests (C#)** — `tests/Pmvni.Legislacao.Testes`:

```csharp
[Fact] void abre_o_pacote_ficticio()   // Formato "pmvleg", VersaoFormato "1.0.0", GeradoEm 2099-01-01T00:00:00Z, Leis ids ["lei-ficticia", "nt-99"], 2 Excluidos
[Fact] void arvore_da_lei_ficticia()   // as mesmas contagens e rótulos do teste Python test_estrutura_da_lei_ficticia (contrato Python × C#)
[Fact] void trechos_riscados_e_imagens()   // 1º "Art. 3º" Revogado; Imagem(src) de cada BlocoImagem/BlocoRecorte começa com RIFF…WEBP; tabela 3 × 3
[Fact] void formato_mais_novo_e_recusado()   // manifesto com versao_formato "2.0.0" (zip montado no teste) → mensagem exata
[Theory] [InlineData("../fora.json")] [InlineData("leis/../../x.json")] [InlineData("img/abc.exe")] [InlineData("outro.txt")]
void entrada_fora_do_padrao_e_recusada(string nome)
[Fact] void bomba_de_compressao_e_recusada()   // entrada de 21 MB de zeros (razão > 100) → FormatoLegislacaoException
[Fact] void imagem_com_assinatura_errada_e_recusada()   // img/0123456789abcdef.webp com bytes PNG
[Fact] void conferencia_igual_diferente_novo_ausente()
    // cópia de tests/corpus/legislacao/kit em PastaDeTeste: Igual; 1 byte trocado na lei → Diferente ["MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"];
    // + "MUNICIPAL/NOVA.pdf" → Novos ["MUNICIPAL/NOVA.pdf"]; NT apagada → Ausentes [...NT 99…]; Aviso == a frase exata com (diferentes: 1, novos: 1, ausentes: 1)
[Fact] void conferencia_sem_kit()   // raiz inexistente → KitAusente; Aviso null
[Fact] void resumo_da_versao()   // Resumo == "Legislação embutida: versão de 01/01/2099, 2 textos, extrator 1.0.0, conjunto do kit <8 hex do manifesto>"
[Fact] void pacote_embutido_dentro_do_orcamento()   // TamanhoEmbutido() ≤ 12_000_000 bytes (registra o tamanho na saída do teste)
[Fact] void pacote_embutido_tem_50_textos_e_nenhuma_abnt()
    // Embutido(): 50 Leis, 11 Excluidos; nenhum LeiResumo.Arquivo com "NBR" ou "ABNT" (sem caixa); esferas na ordem Municipal, Estadual — CBMES, Federal;
    // ids "pdm", "codigo-de-obras" e "codigo-civil" presentes; codigo-civil.AbrirEm "Art. 1.277"; Carregar de cada id sem exceção
```
  (Sem teste que compare o pacote embutido com o kit real: o kit se atualiza sozinho a cada 15 min, e isso não pode quebrar o build — essa conferência é só aviso, no autoteste.)
- [ ] **Step 7: Run** `dotnet test tests/Pmvni.Legislacao.Testes` → Expected: FAIL (projeto sem implementação). **Implement** `Modelo.cs`, `PacoteLegislacao.cs`, `ConferenciaKit.cs`. Run → Expected: PASS.
- [ ] **Step 8: Autoteste e build.** `Autoteste.cs` acrescenta ao JSON `"legislacao": { "textos": 50, "bytes": <TamanhoEmbutido>, "kit": "igual|diferente|ausente", "diferentes": n, "novos": n, "ausentes": n }` (conferência com `Ajustes.PastaKitEfetiva`, fora do pacote MSIX porque o autoteste já roda pelo `explorer.exe`); `ok` exige só que o pacote embutido abra e tenha ≥ 1 texto (diferença do kit **não** reprova). `build-release.ps1`, depois do autoteste, escreve `Legislação embutida: 50 textos, <x,x> MB; kit: igual` ou, se `diferente`, `AVISO: a legislação do kit mudou depois da versão embutida (diferentes: D, novos: N, ausentes: A). Rode: python -I tools\legislacao\extrair.py gerar --kit "<kit>"` — sem falhar e sem exigir Python. Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` → Expected: testes aprovados, autoteste aprovado, a linha da legislação, `Executável final: …` com tamanho ≈ 85 MB + o pacote (registrar o tamanho antes e depois em `docs/notas-tecnicas.md`, "Legislação embutida (Etapa 2)").
- [ ] **Step 9: Commit** — `feat: legislacao compilada embutida (extracao, pacote e leitura)` (inclui `src/Pmvni.Legislacao/Dados/legislacao.pmvleg`, `docs/legislacao-embutida.md` e as fixtures; nada de `tests/saida`).

### Task 18 (Tarefa 18): Módulo Legislação — árvore, texto, pesquisa, citação e PDF original

> Desenho §11.4–§11.6. Executada depois da Tarefa 17; a 14 vem por último. Substitui a parte de Legislação que as
> Tarefas 12 e 13 tinham (lista de arquivos de uma pasta).

**Files:**
- Create: `src/Pmvni.Legislacao/{Busca.cs,Citacao.cs,HtmlLei.cs,ServidorLegislacao.cs}`, `src/Pmvni.Legislacao/web/legislacao.js` (gerado, commitado; `<EmbeddedResource Include="web\legislacao.js" LogicalName="legislacao.js" />`), `web/paginador/src/legislacao/{index.ts,mensagens.ts}`
- Create: `src/Pmvni.App/Logica/ArvoreLegislacao.cs`, `src/Pmvni.App/Paginas/PaginaLegislacao.xaml(.cs)`, `src/Pmvni.App/Legislacao/{SessaoLegislacao.cs,SegurancaLegislacao.cs}`, `src/Pmvni.App/AreaTransferencia.cs`
- Modify: `web/paginador/build.mjs` (segunda entrada → `src/Pmvni.Legislacao/web/legislacao.js`, IIFE `Legislacao`), `src/Pmvni.App/Logica/{Faixas.cs,ComandosApp.cs,Atalhos.cs}`, `Paginas/PaginaAjustes.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`, `tests/Pmvni.Revisao.Testes/Pmvni.Revisao.Testes.csproj` (referência a `Pmvni.Legislacao`, para compilar `Logica/ArvoreLegislacao.cs`)
- Test: `tests/Pmvni.Legislacao.Testes/{BuscaTestes.cs,CitacaoTestes.cs,HtmlLeiTestes.cs,ServidorLegislacaoTestes.cs}`, `tests/Pmvni.Revisao.Testes/{FaixasTestes.cs,ArvoreLegislacaoTestes.cs}`

**Interfaces:**
- Consumes: Tarefa 17 (`PacoteLegislacao`, `ConferenciaKit`); `FabricaWebView2`, o padrão da `Seguranca` (Etapa 1); `AberturaExterna` (Tarefa 13); `Ajustes.PastaKitEfetiva` (Tarefa 16); a pesquisa da barra de título e o realce da Tarefa 11; `FaixaDeOpcoes`, `Faixas`, `ComandosApp`.
- Produces (`Pmvni.Legislacao`, puro):
  - `static string Busca.Normalizar(string s)` (NFD sem marcas combinantes, minúsculas invariantes, `º`/`°` → `o`, espaços e NBSP colapsados, nas pontas removidos); `sealed record ConsultaDispositivo(string? LeiId, string Tipo /* "artigo" | "item" | "anexo" */, string Numero)`; `static ConsultaDispositivo? Busca.InterpretarDispositivo(string consulta, IReadOnlyList<LeiResumo> leis)` — aceita `art. 45`, `art 45`, `artigo 45`, `art. 45-a`, `art. 1.277` = `art. 1277`, `item 5.1.2`, `anexo xii`, e `nt 10-1 5.1.2` (= item 5.1.2 da `nt-10-1`); apelido da lei antes ou depois (`pdm art 45`); número comparado sem pontos de milhar e com a letra em maiúscula; sem apelido ⇒ `LeiId` null.
  - `sealed record ResultadoBusca(string LeiId, string NoId, string? DispId, string Rotulo, string Trecho, bool Revogado)`; `sealed class IndiceBusca { static IndiceBusca Montar(PacoteLegislacao p, CancellationToken ct); IReadOnlyList<ResultadoBusca> Dispositivo(ConsultaDispositivo c) /* vigente antes do revogado; na ordem do catálogo */; IReadOnlyList<ResultadoBusca> Pesquisar(string termo, int max = 200) /* termo normalizado com ≥ 2 caracteres; trecho de até 160 caracteres em volta da 1ª ocorrência; ordem do catálogo e do texto */ }`.
  - `static string Citacao.De(LeiResumo lei, No unidade, BlocoDispositivo? disp, BlocoDispositivo? pai, bool revogado)` → `"art. 1º da Lei nº 9.999/2099 (fictícia)"`, `"art. 1º, § 2º, da Lei nº 9.999/2099 (fictícia)"`, `"art. 2º, parágrafo único, da …"`, `"art. 2º, inciso II, da …"`, `"art. 2º, inciso II, alínea \"a\", da …"`, `"art. 2º-A da …"`, `"item 2.1.1 da NT 99/2099 (fictícia)"`, `"Anexo I da Lei nº 9.999/2099 (fictícia)"`; revogado ⇒ + `" (revogado)"`. O rótulo vem do texto (`Art. 1º` → `art. 1º`; `ANEXO I` → `Anexo I`); `static string Citacao.TextoDoDispositivo(…)` = citação + `"\n\n"` + o texto puro (trechos unidos; riscados entre `[revogado: …]`).
  - `sealed record OpcoesHtml(int TamanhoPct = 100, bool MostrarRevogado = true)`; `static string HtmlLei.Gerar(LeiResumo r, Lei lei, OpcoesHtml o)` — documento completo com `<meta http-equiv="Content-Security-Policy" content="<HtmlLei.Csp>">`, um único `<script src="https://leg.pmvni.invalid/legislacao.js">`, cabeçalho com título, citação, "Fonte: <arquivo>" e `AvisoFonte`; cada nó `<section id="u-<id>" data-no="<id>">` com `<h2|h3|h4>` do rótulo e nome; dispositivo `<p data-disp="<id>">`; trecho riscado `<s class="revogado" title="Texto revogado">`; tabela `<table>` com `colspan`; imagem e recorte `<img src="https://leg.pmvni.invalid/img/<x>.webp" width="w" height="h" alt="Figura da página N do PDF original" loading="lazy">`; `pagina_sem_texto` ⇒ `<p class="aviso">Página N do PDF original sem texto: mostrada como imagem; o conteúdo não foi transcrito.</p>`; **todo** texto por `WebUtility.HtmlEncode`; sem `style` vindo de dados, sem `on*=`, sem `href`.
  - `HtmlLei.Csp` = `"default-src 'none'; img-src https://leg.pmvni.invalid; style-src 'unsafe-inline'; script-src https://leg.pmvni.invalid/legislacao.js"`; `HtmlLei.Host` = `"leg.pmvni.invalid"`.
  - `sealed record RespostaLeg(byte[] Corpo, string TipoConteudo)`; `sealed class ServidorLegislacao(PacoteLegislacao p, Func<OpcoesHtml> opcoes) { RespostaLeg? Responder(string uri) }` — só `https://leg.pmvni.invalid/lei/<id>.html` (`text/html; charset=utf-8`), `/legislacao.js` (`text/javascript; charset=utf-8`) e `/img/<16 hex>.webp` (`image/webp`); qualquer outra (outro host, `http:`, query, `..`, id desconhecido) ⇒ null (o app responde 403).
- Produces (web, `legislacao.ts`, envelope `{v:1,tipo,id,dados}` da Etapa 1): host→web `irPara {no, disp|null}` (rola até o elemento, foco visual por 2 s), `realcar {termo, passo: -1|0|1}` → `realcado {total, atual}` (CSS Custom Highlight API, mesma normalização 1:1 por caractere da Tarefa 11; **nenhum elemento novo** no DOM), `opcoes {tamanho, mostrarRevogado}` (classe no `body`, sem recarregar); web→host `pronto`, `posicao {no}` (unidade no topo da vista, debounce 200 ms), `selecao {no, disp|null, texto}` (a cada mudança de seleção dentro da área do texto).
- Produces (App):
  - `Logica/ArvoreLegislacao`: `sealed record NoArvore(string Rotulo, string? LeiId, string? NoId, bool Revogado, bool TemFilhos)`; `static IReadOnlyList<NoArvore> Raizes(Manifesto m)` (3 esferas, na ordem do catálogo, com as leis por `Titulo`); `static IReadOnlyList<NoArvore> Filhos(Lei lei, string? noId)` (rótulos: `"TÍTULO I — DAS DISPOSIÇÕES GERAIS"`, `"Art. 45"`, `"5.1.2"`; artigos e itens sem nome); carga preguiçosa (`TreeView` com `HasUnrealizedChildren`).
  - Faixa **Legislação** (desenho §11.4; abas `[Arquivo, Página Inicial, Exibir, Ajuda]`, padrão Página Inicial): Página Inicial — `legislacao.copiarCitacao` ("Copiar citação", Ctrl+Shift+C), `legislacao.copiarTexto` ("Copiar texto"), `legislacao.abrirPdf` ("Abrir o PDF original"), `legislacao.mostrar` ("Mostrar no Explorador"); Exibir — `legislacao.tamanho` (Menu: "100%", "125%", "150%"), `legislacao.revogados` (Alternar, "Mostrar texto revogado", ligado), `legislacao.recolher` ("Recolher a árvore"). `Faixas.Disponiveis` passa a `[Relatorios, Despachos, Processos, Legislacao, Ajustes]` (ordem do desenho §9).
  - `internal static class AreaTransferencia { bool Simular; List<string> Copiados; void Copiar(string texto) /* DataPackage + Clipboard.SetContent; no roteiro só registra */ }`.
  - `SegurancaLegislacao.AplicarAsync(CoreWebView2 core, ServidorLegislacao s, Action<string>? aoBloquear)` — as mesmas configurações da `Seguranca` da Etapa 1, **sem** `SetVirtualHostNameToFolderMapping` (nada no disco): filtro `*` responde `s.Responder(uri)` ou 403; navegação de nível superior só para `https://leg.pmvni.invalid/lei/<id>.html`; `NewWindowRequested`/`DownloadStarting` cancelados. Controller InPrivate pela `FabricaWebView2`.
  - `MainWindow`: `internal PaginaLegislacao Legislacao`, `internal Task<bool> IrParaLegislacaoAsync(string consulta)` (o Enter da pesquisa), `internal Task<IReadOnlyList<ResultadoBusca>> PesquisarLegislacaoAsync(string termo)`, `internal int RequisicoesBloqueadasLegislacao`.

Comportamento (desenho §11.4–§11.5): o módulo abre na página "Sobre a legislação embutida" (`ConferenciaKit.Resumo`, lista dos excluídos com motivo, resultado da conferência). Na primeira abertura da sessão, `ConferenciaKit.Conferir(Manifesto, Ajustes.PastaKitEfetiva)` em `Task.Run`; `Aviso` não nulo ⇒ InfoBar de aviso no topo do módulo (fechável; volta na sessão seguinte). Escolher uma lei sem nó ⇒ abre no `AbrirEm` (Código Civil: Art. 1.277) ou no início. Pesquisa da barra (dica "Pesquisar na legislação"): ao digitar, depois de 300 ms, `InterpretarDispositivo` → lista com as unidades; senão `Pesquisar` → lista "N resultados" agrupada por texto no painel da esquerda (no lugar da árvore; Esc ou limpar a caixa volta à árvore); Enter abre o 1º resultado e realça o termo no texto ("1 de N"; Enter/Shift+Enter navegam como no relatório); resultado revogado com o sufixo " (revogado)". Copiar citação/texto: a seleção (`selecao`) ou, sem seleção, a unidade da `posicao`; trecho revogado ⇒ InfoBar informativa "Este trecho está revogado no texto compilado." e copia mesmo assim. "Abrir o PDF original" e "Mostrar no Explorador": `<PastaKitEfetiva>\03 - LEGISLACAO\<Arquivo>` existe ⇒ `AberturaExterna.Abrir` / `MostrarNoExplorador`; não existe ⇒ desabilitados com a dica "Pasta do kit não encontrada.". Ajustes ganha a seção "Legislação embutida" com `Resumo` e o resultado da conferência (`"Igual ao kit."`, o `Aviso`, ou `"Pasta do kit não encontrada: a conferência da legislação não foi feita."`). Nada da legislação é gravado em disco pelo app.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Pmvni.Legislacao.Testes (sobre tests/corpus/legislacao/ficticia.pmvleg)
[Theory]
[InlineData("art. 1 ficticia", "lei-ficticia", "artigo", "1")] [InlineData("ARTIGO 2º-a 9999", "lei-ficticia", "artigo", "2-A")]
[InlineData("ficticia art 4", "lei-ficticia", "artigo", "4")] [InlineData("nt 99 2.1.1", "nt-99", "item", "2.1.1")]
[InlineData("anexo i ficticia", "lei-ficticia", "anexo", "I")] [InlineData("art. 1.277", null, "artigo", "1277")]
void interpreta_dispositivo(string consulta, string? lei, string tipo, string numero)
[Theory] [InlineData("extracao de tabelas")] [InlineData("zona")] [InlineData("a")] void consultas_que_nao_sao_dispositivo(string c)   // → null
[Fact] void dispositivo_prefere_o_vigente()   // "art. 3 ficticia" → 2 resultados, o 1º com Revogado false
[Fact] void pesquisa_sem_acento_nem_caixa()   // "EXTRACAO" → ≥ 2 resultados (Art. 1º e Art. 2º), trecho contém "extração"; "ZR1" → 1 (célula de tabela); "Figura 1" → 1 (recorte)
[Fact] void pesquisa_marca_revogado()   // "texto antigo" → 1 resultado com Revogado true
[Fact] void pesquisa_limita_a_200()   // índice com 300 ocorrências (lei montada no teste) → 200
[Theory]
[InlineData("Art. 1º", null, "art. 1º da Lei nº 9.999/2099 (fictícia)")]
[InlineData("Art. 1º", "§ 2º", "art. 1º, § 2º, da Lei nº 9.999/2099 (fictícia)")]
[InlineData("Art. 2º", "Parágrafo único", "art. 2º, parágrafo único, da Lei nº 9.999/2099 (fictícia)")]
[InlineData("Art. 2º", "II", "art. 2º, inciso II, da Lei nº 9.999/2099 (fictícia)")]
[InlineData("Art. 2º", "a)", "art. 2º, inciso II, alínea \"a\", da Lei nº 9.999/2099 (fictícia)")]
[InlineData("Art. 2º-A", null, "art. 2º-A da Lei nº 9.999/2099 (fictícia)")]
[InlineData("ANEXO I", null, "Anexo I da Lei nº 9.999/2099 (fictícia)")]
void citacao_exata(string unidade, string? disp, string esperado)
[Fact] void citacao_de_item_e_de_revogado()   // "item 2.1.1 da NT 99/2099 (fictícia)"; 1º Art. 3º → "art. 3º da Lei nº 9.999/2099 (fictícia) (revogado)"
[Fact] void texto_do_dispositivo()   // § 2º → "art. 1º, § 2º, da Lei nº 9.999/2099 (fictícia)\n\nSegundo parágrafo. (Redação dada pela Lei nº 9.998/2099)"
[Fact] void html_escapa_tudo_e_tem_csp()
    // lei montada no teste com texto "<script>alert(1)</script> & \"x\"" → aparece escapado; 1 único "<script" (o src permitido); meta CSP == HtmlLei.Csp;
    // nenhum "on" + letra + "=" em atributos, nenhum "href", nenhum "http://"; ids "u-<id>" de todas as unidades
[Fact] void html_riscado_tabela_imagem_e_pagina_sem_texto()   // <s class="revogado"; <table> 3 linhas; 3 <img> com o host; o aviso exato da pág. 3
[Theory]
[InlineData("https://leg.pmvni.invalid/lei/lei-ficticia.html", "text/html; charset=utf-8")]
[InlineData("https://leg.pmvni.invalid/legislacao.js", "text/javascript; charset=utf-8")]
void servidor_responde_o_permitido(string uri, string tipo)   // + a 1ª imagem do manifesto → "image/webp"
[Theory]
[InlineData("https://leg.pmvni.invalid/lei/nao-existe.html")] [InlineData("http://leg.pmvni.invalid/legislacao.js")]
[InlineData("https://doc.pmvni.invalid/legislacao.js")] [InlineData("https://leg.pmvni.invalid/lei/lei-ficticia.html?x=1")]
[InlineData("https://leg.pmvni.invalid/img/../manifesto.json")] [InlineData("https://exemplo.com/x.png")]
void servidor_recusa_o_resto(string uri)   // → null

// tests/Pmvni.Revisao.Testes
[Fact] void arvore_raizes_e_filhos()   // fixture: raízes ["Municipal", "Estadual — CBMES"] (sem Federal na fixture); filhos da lei: ["LEI Nº 9.999, DE 1º DE JANEIRO DE 2099 (FICTÍCIA)", "TÍTULO I — DAS DISPOSIÇÕES GERAIS", "ANEXO I — QUADRO FICTÍCIO"]
[Fact] void faixa_da_legislacao()   // abas [Arquivo, Página Inicial, Exibir, Ajuda]; ids e rótulos exatos acima; Ctrl+Shift+C em Atalhos sem conflito
[Fact] void trilho_na_ordem_do_desenho()   // (caso da Tarefa 13 atualizado) [Relatorios, Despachos, Processos, Legislacao, Ajustes]
[Fact] void cada_modulo_tem_a_faixa_do_desenho()   // (caso existente) agora com Legislação [Arquivo, Página Inicial, Exibir, Ajuda]
[Fact] void ids_de_comando_unicos_e_conhecidos()   // (caso existente) verde com os ids novos
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Legislacao.Testes` e `dotnet test tests/Pmvni.Revisao.Testes --filter "FullyQualifiedName~FaixasTestes|FullyQualifiedName~ArvoreLegislacaoTestes"` → Expected: FAIL.
- [ ] **Step 3: Implement** `Busca`, `Citacao`, `HtmlLei`, `ServidorLegislacao`, `ArvoreLegislacao`, faixa; TS: `cd web/paginador; npm run typecheck; npm run build` → Expected: 0 erros, `src/Pmvni.Legislacao/web/legislacao.js` e `src/Pmvni.Composicao.Web/web/paginador.js` gerados (o paginador sem diferença de bytes). Run os filtros do Step 2 → Expected: PASS.
- [ ] **Step 4: Implement** a página, a sessão, `SegurancaLegislacao`, `AreaTransferencia` e as ligações no `MainWindow` e nos Ajustes. `dotnet build src/Pmvni.App -c Debug` → Expected: 0 erros.
- [ ] **Step 5: Roteiro** (pacote **embutido real**; `AberturaExterna.Simular = true`, `AreaTransferencia.Simular = true`; `Ajustes.PastaKit` = o kit real, só lido):
  - `30_legislacao_arvore` (substitui o `30_legislacao` da Tarefa 13): `MudarModuloAsync(Legislacao)` → página "Sobre" com `ConferenciaKit.Resumo` (50 textos); raízes `["Municipal", "Estadual — CBMES", "Federal"]`; expandir o PDM → 1º filho de título começa por `"TÍTULO I"`; escolher "Art. 1º" → URL `https://leg.pmvni.invalid/lei/pdm.html`, `posicao.no` = o nó escolhido; abrir o Código Civil sem nó → `posicao` em "Art. 1.277"; `RequisicoesBloqueadasLegislacao == 0` e nenhuma requisição a outro host; nenhum arquivo novo em `<dados>` além de `ajustes.json`.
  - `40_legislacao_pesquisa`: `"art. 45 pdm"` → PDM aberto com `posicao` em "Art. 45"; `"art 1277 codigo civil"` → "Art. 1.277"; `"nt 10-1 5.1"` → NT 10 parte 1 no item "5.1" (se o item não existir no texto, o passo registra e falha: não inventar); texto livre `"afastamento frontal"` → ≥ 1 resultado, todos com `LeiId` do catálogo; Enter abre o 1º com "1 de N"; termo `""` → árvore de volta e `CSS.highlights.size == 0`.
  - `41_legislacao_citacao`: depois de `"art. 45 pdm"`, Copiar citação → `AreaTransferencia.Copiados[^1] == "art. 45 da Lei Municipal nº 1.382/2020 (PDM)"`; Copiar texto começa por essa citação + `"\n\n"`; no Estatuto da Cidade, a 1ª unidade com `Revogado` → citação termina com `" (revogado)"` e a InfoBar exata aparece; Exibir › Mostrar texto revogado desligado → nenhum `s.revogado` visível (`getComputedStyle(...).display == "none"`), religado → visível.
  - `42_legislacao_pdf_original`: com o kit real, Abrir o PDF original no PDM → `AberturaExterna.Registradas[^1]` = `<kit>\03 - LEGISLACAO\MUNICIPAL\PDM - LEI 1.382-2020 - TEXTO COMPILADO (CAMARA, ATUALIZADO EM 16-10-2026).pdf` (o nome do manifesto); `Ajustes.PastaKit` = `<pasta>\kit-vazio` → os dois comandos desabilitados com a dica "Pasta do kit não encontrada." e Ajustes com "Pasta do kit não encontrada: a conferência da legislação não foi feita."; volta o kit real.
  - `43_legislacao_kit_diferente`: com o kit real, registra só o estado (`igual`/`diferente`, sem falhar: o kit se atualiza sozinho); `Ajustes.PastaKit` = `<pasta>\kit-legislacao` (criado pelo roteiro com `03 - LEGISLACAO\MUNICIPAL\NOVA.pdf` de 1 byte) → InfoBar do módulo com o texto exato `"A legislação do kit mudou depois da versão embutida neste editor (diferentes: 0, novos: 1, ausentes: 61). O texto mostrado é o da versão embutida de <dd/MM/aaaa do manifesto>."`; volta o kit real.
  Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` → Expected: testes aprovados, autoteste aprovado, linha da legislação, "Executável final: …". Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1` → Expected: `ok: true`, código 0.
- [ ] **Step 6:** `docs/roteiro-etapa-2.md` (passos 30 e 40–43; como o roteiro usa o pacote embutido e o kit só para o PDF original e a conferência). Commit — `feat: modulo legislacao com texto compilado, pesquisa e citacao`

---

## Autoconferência do plano

- **Cobertura do desenho:** §5.1 pacote (T4), trava/autosave/diário (T5, T10), importação e assets (T1); §5.2 dados efetivos e numeração (T2), citações (T1, T3); §5.3 comandos/histórico (T3); §5.4 edição no A4 e marcas (T7, T9); §5.5 barra, faixa, trilho, propriedades, menu, atalhos, fechar com autosave (T8–T11); §6 prova de pronto (T4, T5, T3, T2, T14); §8.1–8.8 (T1–T3, T7, T9, T10); §9 módulos (T12, T13, Ajustes em T11/T13); §9.1 faixa por módulo e pesquisa por módulo (T8 dados, T9/T11/T13 abas, T13 pesquisa). §10 novo despacho: tipos do registro, textos fixos, conteúdo, etapa/nomes, DOCX, validação, PDF e recibo (T15); menu nos três módulos, prévia editável, conferência das páginas, Gerar PDF e pasta do kit nos Ajustes (T16); roteiro final, V6 em `tudo` e recriar.json/LibreOffice (T14). §11 legislação compilada embutida: inclusão/exclusão,
ferramenta de extração, pacote, orçamento e embutimento, conferência com o kit e aviso no build (T17); viewer em WebView2
com host próprio, árvore, pesquisa por dispositivo e por texto, citação, PDF original, Ajustes e roteiro (T18); §9/§9.1
Legislação por pasta retirada das T12–T13; roteiro final `44_fluxo_completo`, docs e recriar.json (T14).
- **Ordem do desenho §8.7:** pacote + importação (T1, T4) → modelo/comandos/efetivos (T2, T3) → ponte de seleção e edição (T6, T7) → controles (T8, T9) → autosave/recuperação/trava (T5, T10) → pesquisa/tema/recentes/comparar (T11) → módulos (T12, T13) → novo despacho (T15 serviço, T16 interface) → legislação embutida (T17 pacote, T18 interface) → roteiro, build e entrega (T14, sempre a última). T5 (lógica pura) vem cedo; a ligação no app fica na T10, na ordem do desenho.
- **Decisões tomadas no plano (o desenho deixava ao construtor):** projeto novo `Pmvni.Revisao`; formato interno do `projeto.json` (`chaves`, `outros`, `numero_original`); token `{{ref:<id>:<largura>}}` só nos dígitos; alvo excluído imprime o número original; edição reconverte citações; recusa por teste em clone + render do kit; Ajustes abre na aba Arquivo; `dados*.json` nas análises da IA; nome da cópia de conflito; seletor de salvar na importação genérica. Nas Tarefas 15–16: assinaturas e mensagens exatas do serviço de despacho; `Verificador despacho` e V6 para rodar o validador do kit; recibo `.recibo-editor.json` com as chaves listadas; LibreOffice real só no roteiro (passos 34–38) e conversor falso nos testes; roteiro final renumerado para `39_fluxo_completo`. Nas Tarefas 17–18: projeto `Pmvni.Legislacao` e testes próprios; formato `legislacao.pmvleg` (manifesto, nós, blocos, trechos riscados) e catálogo por `padrao`; limiares da extração; PDFs fictícios como contrato Python × C#; conferência com o kit no autoteste (aviso no build, nunca falha) em vez de teste que dependa do kit atualizado; passo 30 reaproveitado e passos 40–43; roteiro final renumerado para `44_fluxo_completo`; `Ajustes.PastaLegislacao` (T8) retirado.
