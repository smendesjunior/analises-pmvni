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
xUnit 2.9.3, TypeScript + esbuild (`web/paginador`), Python 3.14 + PyMuPDF (só `tools/fidelidade.py`).

**Especificação (Spec):** `docs/superpowers/specs/2026-11-14-etapa-2-revisao-basica-design.md` (vinculante, inclusive §8,
§9 e §9.1); contexto em `docs/especificacao/2026-11-14-editor-relatorios-concepcao.md` §3, §5.4, §6, §11. Formato e
convenções: `docs/superpowers/plans/2026-11-14-etapa-1-fidelidade-documental.md`.

## Restrições globais (Global Constraints)

- **Executável único** self-contained (`Aplicativo\Editor de Relatorios PMVNI.exe`), manifesto `asInvoker`; sem admin, serviço, tarefa agendada, `HKLM`, `Program Files`, associação de arquivo ou permissão nova.
- WebView2 **InPrivate**, host virtual `doc.pmvni.invalid` com `CoreWebView2HostResourceAccessKind.Deny`, CSP e filtro 403 da Etapa 1 intactos; atalhos do navegador desligados. **Todo HTML do relatório é conteúdo não confiável**: texto editado é texto puro, nunca HTML.
- Nada sai da máquina (sem rede, telemetria ou atualização).
- Dados reais (17098, caminho em `tests/local.json`, ignorado pelo git) só locais: nunca em `tests/corpus`, `tests/referencias`, docs ou commits; saídas guardadas só com contagens, hashes e páginas. Pastas reais de PROCESSOS/LEGISLAÇÃO nunca entram em teste versionado: os testes usam `tests/corpus/processos/` e `tests/corpus/legislacao/` (fictícios).
- Testes e ferramentas rodados pelo shell do Claude **não gravam** em `%APPDATA%`, `%LOCALAPPDATA%` nem `%TEMP%` (virtualização MSIX): pasta de dados por `PMVNI_DADOS_DIR` (`PastaDados.Raiz`); temporários de teste em `tests/saida/...` (ignorada pelo git). Testes novos não usam `Path.GetTempPath()`.
- O app e o roteiro só são abertos por `explorer.exe` com `.cmd` (`tools/roteiro.cmd`, `tools/autoteste.cmd`); verificações leem o resultado por arquivo em `tests\saida` / `docs\`.
- Fidelidade: `python -I tools/fidelidade.py tudo` continua aprovando V1–V4 (e a V5 nova, Tarefa 4). O HTML exportado nunca leva `data-campo`, seleção, marcas, chips ou realce: tudo isso existe só no DOM da tela.
- Pacote: `versao_esquema` **"1.0.0"**; limites **50 MB por entrada, 500 MB no total, razão de compressão ≤ 100**; imagens só PNG/JPEG/WebP/GIF conferidas por assinatura; esquema maior que o conhecido ⇒ **somente leitura**.
- Trava: batimento a cada **30 s**; abandonada com batimento **> 2 min**. Autosave: **30 s** sem edição (opções 15/30/60/120 s), ao trocar de arquivo e ao fechar. Recentes: **10**. Meta de latência por comando: **≤ 1,5 s** no `longo` (medida e registrada; não é assert).
- O original recebido (`original/recebido.json`) nunca muda. O editor não preenche nada sozinho. Pastas de processos/legislação: leitura apenas; o único arquivo que o app cria nelas é o `.pmvrel` de uma importação pedida pelo usuário (e `.bak`/`.tmp`/`.lock` ao lado de um pacote que ele abriu).
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
  Pastas/Processos.cs  Pastas/Despachos.cs  Pastas/Legislacao.cs  Pastas/AbrirExterno.cs
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
src/Pmvni.App/
  Logica/*.cs                             NOVO, sem tipos WinUI (compilados também nos testes): Modulos, Faixas,
                                          ComandosApp, Ajustes, Atalhos, TituloJanela, EstadoGravacao
  Controles/FaixaDeOpcoes.xaml(.cs)       NOVO: SelectorBar + CommandBar montados de uma DefinicaoFaixa
  Controles/Trilho.xaml(.cs)              NOVO: trilho de módulos (ícone + rótulo, estilo Outlook/Store)
  Paginas/PaginaRelatorios.xaml(.cs)      NOVO: painel (Seções | Excluídos | Histórico), WebView2, Propriedades
  Paginas/PaginaProcessos|PaginaDespachos|PaginaLegislacao|PaginaAjustes.xaml(.cs)   NOVOS (Tarefas 11 e 13)
  Edicao/SessaoEdicao.cs                  NOVO: liga SessaoRevisao, Recompositor, Pacote, Trava, Diário e a tela
  Perguntas.cs                            NOVO: IPerguntas (diálogos e seletores; o roteiro troca por respostas fixas)
  MainWindow.xaml(.cs)                    vira o "shell": barra de título, trilho, faixa, páginas
  Roteiro.cs  Argumentos.cs  ItemNavegacao.cs  Estilos.xaml  (alterados)
src/Pmvni.Verificador/                    html --via-pacote (Tarefa 4)
tests/Pmvni.Revisao.Testes/               NOVO, xUnit net10.0; liga (Compile Link) src/Pmvni.App/Logica/*.cs
  Ambiente.cs                             PastaDeTeste(nome) em tests/saida/revisao/<nome>-<guid>, apagada no Dispose
tests/Pmvni.Web.Testes/                   RecomposicaoTestes.cs, EdicaoTelaTestes.cs, PesquisaTelaTestes.cs (novos)
tests/corpus/processos/  tests/corpus/legislacao/   árvores fictícias (Tarefa 12)
tools/fidelidade.py                       subcomando v5; tudo = v1..v5
tools/roteiro.ps1  tools/roteiro-e-esperar.ps1 (novo)
docs/roteiro-etapa-2.md  docs/roteiro-etapa-2.resultado.json  docs/entrega-etapa-2.md  docs/notas-tecnicas.md
```

`EditorRelatoriosPMVNI.sln` ganha `Pmvni.Revisao` e `Pmvni.Revisao.Testes`; `Pmvni.Composicao.Web`, `Pmvni.App` e
`Pmvni.Verificador` referenciam `Pmvni.Revisao`. O `build-release.ps1` já roda `dotnet test` na solução e limpa
`bin`/`obj` de todo projeto sob `src` e `tests` (nada a mudar nele).

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
  - `sealed class Ajustes { Modulo Modulo; Dictionary<Modulo,string> AbaPorModulo; string Tema = "sistema"; int IntervaloAutosaveS = 30; bool MarcasRevisao = true; List<string> Recentes; string? PastaProcessos; string? PastaLegislacao; static Ajustes Carregar(string raizDados) /* <raiz>\ajustes.json; corrompido ⇒ renomeia para ajustes.corrompido.json e usa o padrão */; void Salvar(string raizDados) /* tmp + Move */ }`.
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

### Task 12 (Tarefa 12): Serviço de pastas — processos, despachos, legislação e abertura externa

**Files:**
- Create: `src/Pmvni.Revisao/Pastas/{Processos.cs,Despachos.cs,Legislacao.cs,AbrirExterno.cs}`
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
  MODELOS/x.txt
tests/corpus/legislacao/
  LEI 1382-2020 PDM.pdf   NBR 9050.pdf   CODIGO DE OBRAS/LC 70-1990.pdf   atalho.lnk
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
  - `static Leitura<ArquivoEncontrado> Legislacao.Listar(string raiz, CancellationToken ct)` — todos os arquivos até 4 níveis, por nome.
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
[Fact] void legislacao_lista_todos_por_nome()        // 4 arquivos, "CODIGO DE OBRAS\LC 70-1990.pdf" com Relativo
[Theory] [InlineData("ARQ 01-03.pdf", true)] [InlineData("script.cmd", false)] [InlineData("atalho.lnk", false)]
void abrir_externo_so_documentos(string arquivo, bool permitido)   // + pasta → false; "x.pdf:fluxo" → false
[Fact] void raiz_ausente_vira_aviso()                // Itens vazio, 1 aviso com "não foi encontrada"
[Fact] void arvore_grande_e_lida_por_inteiro()       // 2 000 pastas geradas em PastaDeTeste: 2 000 processos; tempo só registrado
[Fact] void cancelamento_interrompe_a_leitura()
```

- [ ] **Step 2: Run** `dotnet test tests/Pmvni.Revisao.Testes --filter FullyQualifiedName~PastasTestes` → Expected: FAIL.
- [ ] **Step 3: Implement.** - [ ] **Step 4: Run** o filtro → Expected: PASS.
- [ ] **Step 5: Commit** — `feat: leitura das pastas de processos, despachos e legislacao`

### Task 13 (Tarefa 13): Módulos Processos, Despachos e Legislação na interface; pastas nos Ajustes

**Files:**
- Create: `src/Pmvni.App/Paginas/{PaginaProcessos,PaginaDespachos,PaginaLegislacao}.xaml(.cs)`, `src/Pmvni.App/AberturaExterna.cs`
- Modify: `Logica/{Faixas.cs,ComandosApp.cs,Ajustes.cs}`, `PaginaAjustes.xaml(.cs)`, `MainWindow.xaml(.cs)`, `Roteiro.cs`, `docs/roteiro-etapa-2.md`
- Test: `tests/Pmvni.Revisao.Testes/FaixasTestes.cs` (casos novos)

**Interfaces:**
- Consumes: Tarefa 12; `MainWindow.ImportarParaAsync/AbrirPacoteAsync/MudarModuloAsync`; `FaixaDeOpcoes`.
- Produces:
  - Faixas (desenho §9.1): **Processos** — Página Inicial (`processos.abrirRelatorio`, `processos.importarAnalise`, `processos.abrirArquivo`, `processos.abrirPasta`, `processos.atualizar`, `processos.ano` Menu), Exibir (`processos.ordenar` Menu número/data/requerente, `processos.mostrarPastas` Alternar); **Despachos** — Página Inicial (`despachos.abrir`, `despachos.abrirPdf`, `despachos.mostrar`, `despachos.irAoProcesso`, `despachos.atualizar`, `despachos.filtro` Menu Todos/Correção/Aprovação), Exibir (`despachos.agrupar` Menu processo/data, `despachos.ordenar`); **Legislação** — Página Inicial (`legislacao.abrir`, `legislacao.mostrar`, `legislacao.atualizar`, `legislacao.copiarCaminho`), Exibir (`legislacao.ordenar` Menu nome/data). Os três entram em `Faixas.Disponiveis` na ordem do trilho: Relatórios, Despachos, Processos, Legislação; Ajustes embaixo.
  - `static class AberturaExterna { bool Simular; List<string> Registradas; bool Abrir(string caminho, out string? motivo) /* AbrirExterno.Permitido + Process.Start(UseShellExecute) */; void MostrarNoExplorador(string caminho) /* explorer.exe /select,"…" */ }`.
  - `Ajustes.PastaProcessosEfetiva` (padrão `%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\PROCESSOS`) e `PastaLegislacaoEfetiva` (padrão `…\SIDNEY JUNIOR\LEGISLAÇÃO`); Ajustes ganha os dois seletores de pasta (`FolderPicker`).
  - Pesquisa da barra muda de alvo: Relatórios = folhas; Processos = `Processos.Filtrar`; Despachos e Legislação = nome do arquivo (sem caixa/acento). O texto de dica muda ("Pesquisar processos", "Pesquisar despachos", "Pesquisar legislação").

Processos: lista à esquerda (mais recentes primeiro, filtro por ano), detalhe à direita (relatórios, despachos, PROJETO/
DOCUMENTOS); Abrir relatório: `.pmvrel` → `AbrirPacoteAsync` + módulo Relatórios; `dados*.json` → `ImportarParaAsync(json,
Processos.NomeImportacao(p, capa.processo))` + módulo Relatórios (é a "Importar análise da IA"). Pasta ausente ⇒ mensagem
"A pasta de processos não foi encontrada: <caminho>. Escolha a pasta em Ajustes." com o botão "Abrir Ajustes". Leitura em
`Task.Run`, cancelada ao trocar de módulo ou atualizar. Despachos: do processo selecionado ou de todos (alternância).

- [ ] **Step 1: Write the failing tests** — `cada_modulo_tem_a_faixa_do_desenho` (nomes das abas por módulo exatamente: Relatórios `[Arquivo, Página Inicial, Revisão, Exibir, Ajuda]`; Despachos/Processos/Legislação `[Arquivo, Página Inicial, Exibir, Ajuda]`; Ajustes `[Arquivo, Ajuda]`), `trilho_na_ordem_do_desenho`. Run o filtro `FaixasTestes` → Expected: FAIL.
- [ ] **Step 2: Implement**; build Debug → 0 erros; testes → PASS.
- [ ] **Step 3: Roteiro** (copia `tests\corpus\processos` e `tests\corpus\legislacao` para `<pasta>\` e aponta os Ajustes para as cópias **antes** de abrir os módulos; `AberturaExterna.Simular = true`): `28_processos` (4 processos na ordem da Tarefa 12; filtro "fulano" → 1; Importar análise da IA do 0001 → cria `Relatorio_0001_2099.pmvrel` na cópia, módulo Relatórios aberto com 3 itens; repetir → `_2`), `29_despachos` (todos: 4 na ordem por data; filtro Aprovação → 2; Abrir PDF registra o caminho sem abrir), `30_legislacao` (4 arquivos; Abrir `atalho.lnk` → recusado com a mensagem exata, nada registrado), `31_ajustes_pastas` (pasta inexistente → mensagem + botão Abrir Ajustes), `32_faixa_e_pesquisa_por_modulo` (para cada módulo: abas iguais a `Faixas.Para`, `ComandosSemManipulador()` vazio, dica da pesquisa certa; módulo/aba lembrados após `MudarModuloAsync`), `33_pastas_intactas` (lista de arquivos e sha das cópias iguais às de antes, exceto os `.pmvrel` criados). Run `tools\build-release.ps1`, `tools\roteiro-e-esperar.ps1` → Expected: `ok: true`.
- [ ] **Step 4: Commit** — `feat: modulos processos, despachos e legislacao`

### Task 14 (Tarefa 14): Roteiro completo, build, fidelidade, documentação e Recriar PC

**Files:**
- Modify: `src/Pmvni.App/Roteiro.cs`, `tools/roteiro.ps1`, `docs/roteiro-etapa-2.md`, `docs/roteiro-etapa-2.resultado.json`, `docs/notas-tecnicas.md`, `docs/fidelidade.md`, `recriar.json`
- Create: `docs/entrega-etapa-2.md`
- Modify (fora do repositório, se a conferência pedir): `C:\Users\smendesjunior\OneDrive\Aplicativos IA\Recriar PC\sistema\MAPA-DO-AMBIENTE.md`

**Interfaces:**
- Consumes: tudo das Tarefas 1–13.
- Produces: o roteiro final `34_fluxo_completo` em sequência única num pacote novo: importar `curto` → editar texto no A4 → mudar situação com justificativa → excluir item → desfazer → salvar → fechar → reabrir (mesmo estado e histórico) → exportar PDF (páginas = folhas; recibo com `sha256_pacote` = sha do arquivo) → pesquisar → trocar tema → Recentes com o pacote no topo → Comparar lista as 2 alterações restantes.

- [ ] **Step 1:** acrescentar `34_fluxo_completo`; garantir que todos os passos 1–34 continuam independentes (falha num não impede os seguintes) e que nada é gravado fora de `tests\saida\roteiro` e `tests\saida\dados-app`.
- [ ] **Step 2:** Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1` → Expected: todos os testes aprovados, autoteste "aprovado (2 rodadas, ok=true, sem elevação)", "Executável final: …", espaço liberado informado; `Aplicativo\` só com o `.exe`.
- [ ] **Step 3:** Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools\roteiro-e-esperar.ps1` → Expected: `ok: true`, `falhas: []`. Registrar a latência (passo 18) e a tabela da Tarefa 6 em `docs/notas-tecnicas.md` ("Latência de um comando (Etapa 2)", com a meta ≤ 1,5 s e se foi atingida; se não, registrar que a recomposição incremental fica para depois — desenho §8.4).
- [ ] **Step 4:** Run `python -I tools/fidelidade.py tudo` → Expected: V1–V5 aprovados em todos os casos (corpus, `dados_modelo --instrucoes`, 17098 local), código 0. Resultado resumido em `docs/fidelidade.md` (só contagens e hashes).
- [ ] **Step 5:** `docs/entrega-etapa-2.md` no formato de `docs/entrega-etapa-1.md` (o que funciona, o que foi testado com os números do `dotnet test`, do roteiro e da `fidelidade.py tudo`, o que fica pendente: cálculos, despachos emitidos, schema público, segurança completa do pacote, merge no kit); `docs/roteiro-etapa-2.md` com a tabela final dos 34 passos.
- [ ] **Step 6:** `recriar.json`: `usadoPor` passa a citar o pacote `.pmvrel`, as pastas `recuperacao`, `edicao` e `ajustes.json` em `%LOCALAPPDATA%\EditorRelatoriosPMVNI` (não guardados: dados de processos) e a leitura das pastas PROCESSOS e LEGISLAÇÃO do PMVNI (só leitura; o app só cria `.pmvrel` de importação); dependências inalteradas. Se o `MAPA-DO-AMBIENTE.md` não cita essa ligação editor ↔ pastas do PMVNI, acrescentar uma linha. Abrir **via `explorer.exe`** `Recriar PC\sistema\motor\Capturar agora.cmd` e depois `Conferir.cmd`; ler `Recriar PC\sistema\CONFERENCIA.md` e corrigir o que ele apontar sobre este app (sem mexer no motor).
- [ ] **Step 7:** conferir que `tests\saida` e os builds sumiram (a limpeza do `build-release` já apaga; o roteiro e a `fidelidade.py` rodados depois deixam `tests\saida`: apagar só ela, conferindo o caminho sob a raiz). Commit — `docs: entrega da Etapa 2` (inclui `docs/roteiro-etapa-2.resultado.json`, sem capturas, PDFs ou dados).

---

## Autoconferência do plano

- **Cobertura do desenho:** §5.1 pacote (T4), trava/autosave/diário (T5, T10), importação e assets (T1); §5.2 dados efetivos e numeração (T2), citações (T1, T3); §5.3 comandos/histórico (T3); §5.4 edição no A4 e marcas (T7, T9); §5.5 barra, faixa, trilho, propriedades, menu, atalhos, fechar com autosave (T8–T11); §6 prova de pronto (T4, T5, T3, T2, T14); §8.1–8.8 (T1–T3, T7, T9, T10); §9 módulos (T12, T13, Ajustes em T11/T13); §9.1 faixa por módulo e pesquisa por módulo (T8 dados, T9/T11/T13 abas, T13 pesquisa).
- **Ordem do desenho §8.7:** pacote + importação (T1, T4) → modelo/comandos/efetivos (T2, T3) → ponte de seleção e edição (T6, T7) → controles (T8, T9) → autosave/recuperação/trava (T5, T10) → pesquisa/tema/recentes/comparar (T11) → módulos (T12, T13) → roteiro, build e entrega (T14). T5 (lógica pura) vem cedo; a ligação no app fica na T10, na ordem do desenho.
- **Decisões tomadas no plano (o desenho deixava ao construtor):** projeto novo `Pmvni.Revisao`; formato interno do `projeto.json` (`chaves`, `outros`, `numero_original`); token `{{ref:<id>:<largura>}}` só nos dígitos; alvo excluído imprime o número original; edição reconverte citações; recusa por teste em clone + render do kit; Ajustes abre na aba Arquivo; `dados*.json` nas análises da IA; nome da cópia de conflito; seletor de salvar na importação genérica.
