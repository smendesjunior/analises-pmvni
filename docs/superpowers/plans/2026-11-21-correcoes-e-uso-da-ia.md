# Correções da execução e redução do uso de IA — Plano de implementação (fase única)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir os 17 erros do diagnóstico do processo 19486/2026 e passar para código tudo o que hoje a IA faz sem precisar de julgamento, no Editor e nos plugins analise, analise-pro e analise-google.

**Architecture:** O Editor deixa de abrir uma IA "controladora" e passa a rodar os controladores do kit diretamente, nas três rotas, lendo situação, cota e consumo de arquivos. No kit, prepare e produce viram etapas de programa (sem sessão de IA); analyze, technical_audit, audit e final_review continuam com IA, mas recebem contexto enxuto, dados pré-extraídos como candidatos e cálculos feitos por script. Regras determinísticas ficam em JSON único, com casos de teste comuns ao kit (Python) e ao Editor (C#).

**Tech Stack:** C# / .NET (WinUI, `src/Pmvni.*`, testes em `tests/Pmvni.*.Testes`, executor falso em `tests/AgenteFalso`); Python 3 só com biblioteca padrão nos plugins (`<KIT>\01 - PLUGINS\plugins\<plugin>\skills\<plugin>\scripts`), PyMuPDF/Docling só pelo Motor PDF já instalado.

**Spec:** `docs/auditoria/2026-11-20-consumo-codex-19486.md` (erros 1–17) e o parecer "o que tirar da IA" (resumo nas Decisões abaixo; origem: `Prompts-Claude-Editor-PMVNI.pdf` do ChatGPT, revisado).

`<KIT>` = `C:\Users\smendesjunior\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)`; `<P>` = `<KIT>\01 - PLUGINS\plugins`; `<S:x>` = `<P>\x\skills\x`.

## Decisões já tomadas (não reabrir)

- Sessões de IA por análise: de 7 para 4 (analyze, technical_audit, audit, final_review).
- O preparo da IA é descartado pela analyze ("COMPENSAÇÃO DO PREPARO"), então prepare vira só script. A escolha das peças atuais é por regra; empate vai para o analista no Editor.
- Critério de vãos **não muda**: janela + porta (`opening_rule_version: 2`, definido pelo usuário em 13/11/2026). A separação nominal/útil/móvel proposta pelo ChatGPT fica fora.
- A auditoria independente sobre os originais fica. Sai dos pedidos de auditoria só o que os validadores já provam (aritmética, IDs, cobertura, fidelidade do modelo).
- Tudo o que o código extrai de prancha (quadros, rótulos, cotas) entra como **candidato**, com `"verificacao": "pendente"`, e só vira dado depois da confirmação da IA no original.
- O progresso e a previsão do Monitor passam a ser calculados pelo Editor; a IA não grava mais `progresso\*.json` nem `estimativa.json`.

## Global Constraints

- **Onde trabalhar.** O kit mestre (`<KIT>`, confirmado em `%USERPROFILE%\.codex\pmvni-kit.json`) é espelhado no servidor e distribuído a todos os computadores a cada 15 minutos: **nunca editar o kit mestre durante a implementação.** Todo trabalho no kit é feito na cópia com git `C:\Users\smendesjunior\OneDrive\PMVNI\SIDNEY JUNIOR\KIT-PMVNI-DESENVOLVIMENTO` (mesma estrutura: `01 - PLUGINS\plugins\…` e `INSTALAR_KIT_PMVNI.cmd`; `core.autocrlf=false`, porque o kit confere hashes de arquivos). Nesta cópia, `<P>` = `KIT-PMVNI-DESENVOLVIMENTO\01 - PLUGINS\plugins`. Recursos só lidos (`00 - INDICE DO KIT`, `03 - LEGISLACAO`, modelos) continuam vindo do kit mestre. A publicação no kit mestre é só na Tarefa 17, com autorização do usuário.
- **Onde trabalhar no Editor.** Worktree `.worktrees\uso-ia`, branch `melhoria/uso-da-ia`.
- **Linha de base dos testes do kit (21/11/2026, antes de qualquer mudança):** nos três plugins passam todos os `test_*.py`, `tests\test_*.py`, `self_test_kit_version.py`, `self_test_pasta_processo.py` e `self_test_programas.py`. Já falhavam antes: `self_test.py` (assert "scope é obrigatório", linha 248), `self_test_report_evidence.py` e `self_test_visual_evidence.py` (falta `pypdfium2` no Python do sistema; rodar com o Python do Motor PDF em `%LOCALAPPDATA%\PMVNI\MotorPdf\env-*\Scripts\python.exe`). Uma tarefa não pode piorar essa linha de base.
- **As três rotas valem igualmente.** Analisar com Claude (analise-pro) e com Gemini (analise-google) são opções do Editor: toda correção que se aplica à rota ChatGPT é feita também nessas rotas, quando o mecanismo existir nelas.
- Os três plugins têm scripts copiados: toda mudança em script comum é aplicada nos três (`analise`, `analise-pro`, `analise-google`) e coberta por teste em cada um.
- Versões desta entrega: analise **3.1.0**, analise-pro **2.1.0**, analise-google **2.1.0**, Editor **2.3.0** (`Directory.Build.props` + seção no `CHANGELOG.md`).
- Compatibilidade: o Editor 2.3.0 exige esses mínimos e, com plugin mais antigo, informa qual componente atualizar em vez de iniciar.
- Scripts do kit: só biblioteca padrão do Python; nada de dependência nova.
- Nenhuma senha, chave ou token em arquivo de registro, receita ou teste. Casos de teste usam dados fictícios.
- Escrita em AppData/HKCU, instalação e abertura de programas: por `.cmd` externo aberto com `explorer.exe` (regra do CLAUDE.md do usuário).
- Fim da entrega: `recriar.json` atualizado, `Capturar agora.cmd` + `Conferir.cmd` do Recriar PC, `tools\build-release.ps1` com GitHub e push; o roteiro de conferência da interface só no fim.
- Análise interrompida iniciada na versão anterior continua retomável: o controlador revalida só o que mudou e mostra histórico legível.

## Review Focus

1. Cota acaba no meio da analyze (20 min de astra alto): a etapa é **retomada** depois da espera informada, não refeita; o Editor mostra "aguardando cota até HH:MM".
2. Análise iniciada no kit 3.0.1 e retomada no 3.1.0: segue sem perguntar nada (ninguém responde num `exec`) e sem refazer etapas técnicas já aprovadas.
3. Duas pranchas com o mesmo número e revisões que a regra não desempata: o Editor pergunta ao analista **antes** de gastar qualquer sessão de IA.
4. PDF escaneado, sem texto: os extratores devolvem lista vazia de candidatos e registram a página como ilegível; a analyze roda normalmente.
5. Objeção de apresentação que na verdade altera número, ID, norma, localização ou figura: é tratada como técnica (volta à analyze), nunca corrigida só no produce.

---

## Mapa de arquivos

**Kit (novos, nos três plugins salvo indicação):**
- `scripts/falhas.py` — classificação de falhas e espera de cota.
- `scripts/pecas_atuais.py` — escolha das peças vigentes por regra.
- `scripts/etapas_programa.py` — executa prepare e produce sem IA.
- `scripts/contexto_etapa.py` — monta o pacote de instruções de cada etapa.
- `scripts/calcular_indices.py` + `references/anexo-vi-zonas.json` — índices urbanísticos.
- `scripts/checklist_documental.py` + `references/formatos-documentais.json`.
- `scripts/extrair_quadros.py` — quadros e rótulos a partir de `geometria-v1`.
- `scripts/cadeias_cotas.py` — cadeias de cotas candidatas a partir dos vetores.
- `scripts/diferenca_revisao.py` — reanálise por diferença.
- `references/achados-padrao.json` — biblioteca de achados.
- `references/casos-ouro.json` — casos de teste comuns a kit e Editor.

**Kit (alterados):** `analise_runtime.py`, `executar_analise_62.py`, `pro_runtime.py`, `pro_auto.py`, `google_runtime.py`, `google_auto.py`, `calcular_vaos.py`, `report_evidence.py`, `validate_analysis.py`, `pro-stage-prompts.json`, `etapas-analise.json`, `niveis-raciocinio.json`, os três `SKILL.md`, `INSTALAR_KIT_PMVNI.cmd` (ou o script que ele chama).

**Editor (alterados):** `src/Pmvni.Agentes/{OrquestradorAnalise,LinhasDeComando,PedidoAgente,RegistroExecucao,EventosCodex,ResultadoAnalise,PoliticaKit}.cs`, `src/Pmvni.App/Logica/AchadosExecucao.cs`, `src/Pmvni.App/MainWindow.{NovoRelatorio,NovoDespacho,Revisao,Ajustes}.cs`, `src/Pmvni.App/Despachos/IaRecalculoIndices.cs`, `src/Pmvni.Revisao/{RecalculoIndices,Esquadrias,ConferenciaRevisao}.cs`.
**Editor (novos):** `src/Pmvni.Agentes/{SituacaoAnalise62,UsoDasIas,ProgressoLocal,PrevisaoTermino}.cs`, `src/Pmvni.Revisao/Despachos/ResumoExigencias.cs`, `src/Pmvni.App/Dialogos/EscolhaDePecas.xaml(.cs)`.

---

## Parte A — Kit: correções da execução

### Task 1: Só objeção técnica sobe o nível; cota baixa desce (erros 8 e 13)

**Files:** Create `<S:analise>/scripts/falhas.py` (+ cópias). Modify `analise_runtime.py:47-78` e a chamada de `next_stage`. Test `test_analise_runtime_niveis.py`.

**Interfaces — Produces:**
- `falhas.classificar(returncode: int, stderr: str, nota: str) -> Literal['cota','ambiente','ferramenta','tecnica']`
- `falhas.espera_cota(stderr: str) -> int | None` (segundos de "Try again in 300s")
- `analise_runtime.record_failure(case, stage, tipo)`: só `tipo == 'tecnica'` conta para `required_level`.

- [ ] Teste `test_cota_nao_sobe_nivel`: três falhas `cota` + uma `ambiente` em prepare → `required_level('prepare') == 'baixo'`.
- [ ] Teste `test_objecao_tecnica_sobe_um_nivel`: uma falha `tecnica` em analyze → sobe exatamente um nível.
- [ ] Teste `test_cota_baixa_desce`: `next_stage(case, require_models=True, quota_left=0.14)` desce um nível; `0.16` não desce.
- [ ] Teste `test_espera_cota`: `espera_cota('Rate limit exceeded. Try again in 300s') == 300`; texto sem tempo → `None`.
- [ ] Rodar e ver falhar; implementar; rodar e ver passar. `python -B -m unittest test_analise_runtime_niveis -v`
- [ ] Passar `quota_left` (de `cota_codex.py`/`cota_claude.py`) em todas as chamadas de `next_stage` nos três controladores.
- [ ] Falhas antigas já gravadas sem `tipo` são lidas como `ambiente` (não sobem nível). Teste `test_registro_antigo_sem_tipo`.
- [ ] Commit.

### Task 2: Sessões técnicas retomáveis, espera de cota e mesmo contexto nas duas rotas (erros 9, 10, 14, 16)

**Files:** Modify `executar_analise_62.py:60-95`. Test novo `test_executar_analise_62.py` com `codex` falso (script que imprime banner e devolve código configurável).

**Interfaces — Produces (contrato lido pelo Editor na Tarefa 12):**
- Códigos de saída: `0` entregue ou etapa concluída; `20` aguardando cota; `21` precisa de decisão do analista; `1` falha.
- Última linha de stdout sempre JSON: `{"situacao": "ENTREGUE|ETAPA_OK|AGUARDANDO_COTA|DECISAO|FALHA", "etapa": str, "retomar_em_utc": str|null, "motivo": str, "decisao": obj|null}`.
- `engine_state.json` ganha `sessao_codex` por etapa.

- [ ] Teste `test_sem_ephemeral_e_guarda_sessao`: a linha de comando não tem `--ephemeral` e o id da sessão fica em `engine_state.json`.
- [ ] Teste `test_cota_espera_e_retoma`: primeira execução falha com "Try again in 300s" → o controlador não repete na hora, sai com código 20 e `retomar_em_utc` = agora + 300 s, sem registrar falha; segunda chamada usa `codex exec resume <sessao>`.
- [ ] Teste `test_codex_lb_mantem_ignore_user_config`: na rota `codex-lb` a linha mantém `--ignore-user-config` e recebe `-c model_provider="codex-lb"` mais `-c model_providers.codex-lb.base_url=…`, `wire_api` e `requires_openai_auth`, lidos de `~/.codex/config.toml` pelo controlador.
- [ ] Teste `test_nunca_pergunta`: situação que antes gerava pergunta ("autoriza revalidar o preparo?") sai com código 21 e `decisao` preenchida, sem prompt interativo.
- [ ] Implementar. Confirmar no log se as requisições `gpt-6-luna` são a tarefa de memórias; se forem, desligar por `-c` nas sessões de etapa e cobrir com `test_memorias_desligadas` (erro 16). Se não forem, registrar o achado em `docs/auditoria` e não mexer.
- [ ] Repetir o mesmo contrato de saída e a espera de cota em `pro_runtime.py`/`pro_auto.py` e `google_runtime.py`/`google_auto.py`; testes em `test_pro_runtime.py` e `test_google.py`.
- [ ] **Rota Claude (analise-pro), mesmos problemas no `claude --print` (`pro_runtime.claude_command`, linha 279; `run_selected`, linha 398):**
  - Teste `test_claude_guarda_sessao_e_retoma`: o `session_id` do JSON de saída fica em `engine_state.json`; depois de recusa por cota, a etapa é retomada com `--resume <session_id>` em vez de começar sessão nova com "reusar arquivos".
  - Teste `test_claude_contexto_minimo_conta`: na rota Conta (OAuth), a linha tem `--strict-mcp-config`, `--setting-sources project,local` e `--disable-slash-commands` (sem MCP, plugins, hooks e skills do usuário; `--bare` não serve porque desliga o OAuth).
  - Teste `test_claude_contexto_minimo_api`: na rota API, a linha usa `--bare` com a chave por `ANTHROPIC_API_KEY` do ambiente filho (já montado por `cota_codex.ambiente`).
  - Antes de fixar os flags, medir com uma chamada de teste curta os tokens de entrada da primeira requisição com e sem eles e registrar no relatório da tarefa. Se algum flag impedir a leitura do `CLAUDE.md` da pasta de processos que o kit precisa, manter só os que não impedem e registrar.
  - Teste `test_claude_cota_nao_reinicia`: recusa por cota não apaga o `result` parcial nem abre nova sessão na mesma hora; `pro_auto` aguarda a renovação (já faz) e retoma a mesma sessão.
- [ ] **Rota Gemini (analise-google):** aplicar o contrato de saída e a espera de cota; retomada de sessão só onde o executor oferecer (registrar no relatório se `agy`/Gemini Web não oferecem).
- [ ] Commit.

### Task 3: Objeção de apresentação volta só ao produce (erro 11)

**Files:** Modify os três runtimes, `etapas-analise.json`, `pro-stage-prompts.json` (audit e final_review). Test `test_execution_control.py`, `test_pro_runtime.py`, `test_google.py`.

**Interfaces — Produces:** cada objeção da audit tem `tipo: "apresentacao" | "tecnica"` e `campos_alterados: [str]`. `runtime.destino_da_objecao(objecoes) -> 'produce' | 'analyze'`.

- [ ] Teste `test_apresentacao_volta_ao_produce`: três objeções `apresentacao` (rótulo sobreposto, legenda, digitação) → próxima etapa `produce`; analyze e technical_audit continuam válidas.
- [ ] Teste `test_apresentacao_que_muda_numero_e_tecnica`: objeção marcada `apresentacao` com `campos_alterados` contendo `valor`, `id`, `norma`, `localizacao` ou `figura` → destino `analyze` (Review Focus 5).
- [ ] Teste `test_hash_do_manifesto_decide`: depois da correção no produce, se o hash de `findings`/`opening_calculations` do manifesto mudou, a correção é rejeitada e vira objeção técnica.
- [ ] Implementar, rodar, commit.

### Task 4: Prepare e produce como etapas de programa (erro 12 e parecer)

**Files:** Create `pecas_atuais.py`, `etapas_programa.py`. Modify os três runtimes (`ESTAGIOS_PROGRAMA = ('prepare', 'produce')`), `uso_ias.py` (executor `programa`), `validate_plugin_workflow.py`. Test `test_pecas_atuais.py`, `test_etapas_programa.py`.

**Interfaces — Produces:**
- `pecas_atuais.escolher(inventario: list[dict]) -> {"atuais": [...], "historico": [...], "ambiguas": [{"prancha": str, "candidatas": [...], "motivo": str}]}`. Regra, nesta ordem: pasta `REVISÃO NN` mais alta; revisão no carimbo; data do arquivo. Seleção explícita do analista (anexos escolhidos no Editor) sempre prevalece.
- `etapas_programa.prepare(case) -> dict` roda `check_kit_version.py`, `preparar_caso.py`, `pecas_atuais`, `extrair_quadros` (Tarefa 8), `checklist_documental` (Tarefa 7) e grava os sete grupos de enquadramento como `desconhecido`.
- `etapas_programa.produce(case) -> dict` roda `report_evidence.py montar`, `gerar_relatorio.py` (Edge **fora** da sandbox do Codex, direto pelo controlador), os quatro validadores e grava `delivery.json`.
- Registro da etapa: `{"executor": "programa", "regras_versao": str, "entradas_sha256": {...}, "falhas": [...]}`.

- [ ] Teste `test_revisao_mais_alta_vence`: `A01` em `INICIAL` e em `REVISÃO 02` → atual é a da `REVISÃO 02`.
- [ ] Teste `test_empate_vira_ambigua`: duas `A01` na mesma pasta, mesma revisão no carimbo → entra em `ambiguas`; o controlador sai com código 21 e `decisao.tipo == "pecas"` (Review Focus 3).
- [ ] Teste `test_copias_identicas_reaproveitam`: dois arquivos com o mesmo sha256 → um só preparo em `cache-pdf`.
- [ ] Teste `test_prepare_sem_sessao`: com `codex`/`claude` falso que falha se for chamado, `prepare` conclui.
- [ ] Teste `test_produce_sem_sessao_e_delivery`: `relatorio_conteudo.json` de exemplo → PDF existe, recibo confere, `delivery.json` aponta `document` e `pdf` absolutos.
- [ ] Teste `test_produce_figura_ausente_bloqueia`: figura citada sem arquivo → falha `ambiente` com o ID do item, sem PDF.
- [ ] Teste `test_troca_de_peca_invalida`: trocar o conteúdo de uma peça depois do prepare invalida prepare e as evidências dependentes.
- [ ] Teste `test_retomada_de_3_0_1`: `engine_state.json` do 3.0.1 com analyze aprovada → retoma no produce sem pergunta (Review Focus 2).
- [ ] Substituir a escolha das peças por Sonnet/Gemini (`pro_auto.escolher_pecas`, linha 105, e o equivalente em `google_auto.py`) por `pecas_atuais.escolher`. Teste `test_auto_nao_chama_ia_para_pecas` em `tests/test_auto.py` e `tests/test_google.py`.
- [ ] Remover dos `SKILL.md` e de `pro-stage-prompts.json` os textos de prepare e produce; mover a checagem editorial P10 do produce para o pedido da final_review. Tirar o "COMPENSAÇÃO DO PREPARO" da analyze e da technical_audit e substituir por: dados do preparo são candidatos de programa, a confirmar no original.
- [ ] Commit.

### Task 5: Atualização do kit não roda durante análise (erro 15)

**Files:** Modify o script chamado por `INSTALAR_KIT_PMVNI.cmd atualizar`. Test: script de teste do instalador (seguir o padrão existente no kit).

- [ ] Teste: com `execucao-62.lock` (ou o lock equivalente dos runtimes pro/google) aberto em qualquer processo de `PROCESSOS`, `atualizar` sai sem alterar nada, com a mensagem "Análise em andamento em <processo>; atualização adiada."
- [ ] Teste: lock órfão (pid inexistente) não bloqueia.
- [ ] Implementar; commit.

## Parte B — Kit: menos trabalho para a IA

### Task 6: Contexto enxuto por etapa e auditorias focadas

**Files:** Create `contexto_etapa.py`. Modify `executar_analise_62.py` (prompt), `pro_runtime.py:353` (`build_request`), `google_runtime.py`, `pro-stage-prompts.json`, `etapas-analise.json`. Test `test_contexto_etapa.py`.

**Interfaces — Produces:** `contexto_etapa.montar(etapa: str, case: Path) -> str`: núcleo fixo comum (primeiro, para aproveitar cache) + só as referências da etapa + diretrizes do analista por último. Tabela `REFERENCIAS_POR_ETAPA` no próprio módulo.

- [ ] Teste `test_tamanho`: o contexto de cada etapa tem no máximo 60 KB (hoje ~190 KB).
- [ ] Teste `test_prefixo_estavel`: os primeiros N bytes são idênticos entre as quatro etapas e entre duas execuções.
- [ ] Teste `test_diretrizes_preservadas`: o texto de `diretrizes.md` aparece inteiro e por último.
- [ ] Teste `test_auditoria_sem_tarefas_de_validador`: os pedidos de technical_audit, audit e final_review não mandam recalcular aritmética, reconciliar IDs nem conferir cobertura; mandam ler a saída real dos validadores e conferir se cada prancha demonstra o achado e se há omissões.
- [ ] Implementar; rodar os `self_test*.py` existentes; commit.

### Task 7: Regras como dados — índices, fração de iluminação, documentos

**Files:** Create `references/anexo-vi-zonas.json`, `calcular_indices.py`, `references/formatos-documentais.json`, `checklist_documental.py`. Modify `calcular_vaos.py`, `validate_analysis.py`. Test `test_calcular_indices.py`, `test_calcular_vaos.py`, `test_checklist_documental.py`.

**Interfaces — Produces:**
- `anexo-vi-zonas.json`: por zona, `to_max`, `ca_max`, `tp_min`, `afastamentos {frontal, lateral, fundos}`, `gabarito`, `altura_max`, `lote_min`, cada valor com `dispositivo` e `pagina` do PDF compilado do PDM. Transcrito do PDF oficial e conferido linha a linha por uma segunda leitura antes do commit.
- `calcular_indices.calcular(zona: str, operandos: dict) -> {"linhas": [{"nome","calculo","projeto","limite","situacao","regra_versao","origem"}]}` — formato de `indices.linhas` do `relatorio_dados.json`. Sem arredondar a favor: compara o valor exato.
- `calcular_vaos`: `fracao` vem de `tabela_art127` pelo tipo do ambiente; `fracao_fonte` manual só com justificativa.
- `checklist_documental.conferir(inventario) -> {"presentes": [...], "ausentes": [...], "formato_invalido": [...]}`.

- [ ] Teste `test_limite_exato`: TO igual ao limite → `ATENDE`; 0,01 ponto acima → `NAO_ATENDE`; valor que só atenderia arredondado → `NAO_ATENDE`.
- [ ] Teste `test_zona_desconhecida`: zona fora da tabela → erro claro, nenhuma linha `ATENDE`.
- [ ] Teste `test_operando_ausente`: sem área do terreno → linha `NAO_COMPROVADO` com o dado faltante nomeado.
- [ ] Teste `test_fracao_automatica`: dormitório sem `fracao` informada usa a fração da `tabela_art127`; fração manual diferente sem justificativa é reprovada por `validate_analysis.py`.
- [ ] Teste `test_formatos`: inscrição com 14 dígitos válida/inválida; número de ART, RRT, CAU e CREA nos formatos de `formatos-documentais.json`; texto original preservado ao lado do normalizado.
- [ ] Teste `test_checklist`: inventário sem ART/RRT → `ausentes` contém o documento e onde foi procurado.
- [ ] A analyze passa a chamar `calcular_indices.py` (texto do pedido atualizado); `validate_analysis.py` reprova `indices.linhas` que não venham do script.
- [ ] Commit.

### Task 8: Quadros e rótulos lidos por código

**Files:** Create `extrair_quadros.py`. Test `test_extrair_quadros.py` com JSONs `geometria-v1` fictícios.

**Interfaces — Consumes:** `.pmvni-ai\cache-pdf\…\geometria-v1-*\N.json` (palavras com bbox). **Produces:** `modelo_projeto.candidatos.json` no formato de `modelo_projeto.json`, cada entrada com `{"peca","pagina","bbox","texto_original","verificacao":"pendente"}`; `extrair_quadros.extrair(case) -> dict`.

- [ ] Teste `test_rotulo_de_ambiente`: palavras "SALA" + "12,50" + "m²" próximas → ambiente `SALA`, área `12.50`.
- [ ] Teste `test_quadro_de_esquadrias`: tabela com código, largura, altura, quantidade → portas e janelas com dimensões.
- [ ] Teste `test_quadro_de_areas`: linhas "ÁREA DO TERRENO", "ÁREA CONSTRUÍDA", "ÁREA PERMEÁVEL" → operandos de `calcular_indices`.
- [ ] Teste `test_pagina_sem_texto`: página escaneada → lista vazia e registro em `paginas_ilegiveis` (Review Focus 4).
- [ ] Teste `test_ambiguo_fica_fora`: número sem rótulo próximo não vira ambiente.
- [ ] Pedido da analyze: partir de `modelo_projeto.candidatos.json`, confirmar cada candidato no original e completar o que faltar; `conferir_projeto.py` roda automaticamente no fim.
- [ ] Commit.

### Task 9: Cadeias de cotas candidatas a partir dos vetores

**Files:** Create `cadeias_cotas.py`. Test `test_cadeias_cotas.py`.

**Interfaces — Produces:** `cadeias_cotas.extrair(case) -> [{"pagina","parciais":[...],"total","diferenca","extremos","verificacao":"pendente"}]`, no formato de `dimension_chains`.

- [ ] Teste `test_cadeia_fecha`: parciais 3,00 + 2,50 alinhadas com total 5,50 no mesmo eixo → `diferenca == 0`.
- [ ] Teste `test_cadeia_nao_fecha`: total 5,60 → candidata com `diferenca == 0.10`.
- [ ] Teste `test_sem_vetores`: página sem desenhos → lista vazia.
- [ ] Limite de escopo: só cotas horizontais e verticais com texto numérico sobre a linha; o resto fica para a IA. Commit.

### Task 10: Reanálise por diferença

**Files:** Create `diferenca_revisao.py`. Modify os três runtimes. Test `test_diferenca_revisao.py`.

**Interfaces — Produces:** `diferenca_revisao.comparar(manifesto_anterior, inventario_atual) -> {"pecas_trocadas":[...], "achados_afetados":[ids], "secoes_reaproveitaveis":[n], "pendencias_anteriores":[ids]}`.

- [ ] Teste `test_peca_igual_reaproveita`: mesma peça (sha256) → seções que só dependem dela entram em `secoes_reaproveitaveis`.
- [ ] Teste `test_prancha_trocada_afeta`: prancha com hash novo → todos os achados com `evidence` nela entram em `achados_afetados`.
- [ ] Teste `test_historico_preservado`: o manifesto anterior é arquivado, não sobrescrito.
- [ ] Pedido da analyze na reanálise: verificar as pendências anteriores e as seções afetadas; seções reaproveitadas entram com referência à análise anterior e passam pela technical_audit normalmente. Commit.

### Task 11: Biblioteca de achados padrão e casos comuns

**Files:** Create `references/achados-padrao.json`, `references/casos-ouro.json`. Modify `report_evidence.py` (montar), `validate_analysis.py`. Test `self_test_report_evidence.py`.

**Interfaces — Produces:** `achados-padrao.json`: `{"codigo","secao","observado","exigencia","correcao","base_legal","parametros":[...]}`. No `relatorio_conteudo.json`, um item pode ter `{"padrao": "<codigo>", "valores": {...}}`. `casos-ouro.json`: `[{"id","entrada","esperado"}]` para índices e vãos.

- [ ] Teste `test_item_padrao_expande`: item com `padrao` + `valores` vira o texto completo, com os valores no lugar.
- [ ] Teste `test_parametro_faltando`: valor ausente → erro com o código e o parâmetro.
- [ ] Teste `test_casos_ouro_python`: `calcular_indices` e `calcular_vaos` dão o `esperado` de cada caso.
- [ ] Carga inicial: os achados mais repetidos dos relatórios já emitidos em `PROCESSOS` (levantar por frequência de `base_legal`); o usuário revisa os textos antes da distribuição. Commit.

## Parte C — Editor

### Task 12: Rota ChatGPT sem IA controladora (erros 1–7)

**Files:** Modify `OrquestradorAnalise.cs:286-370` e `:112-118`, `LinhasDeComando.cs:9-15`, `PedidoAgente.cs`, `EventosCodex.cs`. Create `SituacaoAnalise62.cs`. Test `tests/Pmvni.Agentes.Testes` + `tests/AgenteFalso`.

**Interfaces — Consumes:** contrato da Tarefa 2 (códigos 0/20/21/1 e JSON da última linha). **Produces:**
- `LinhasDeComando.Analise62(string pastaKit, string processo, IReadOnlyList<string> anexos) -> ProcessStartInfo` (`python -B -X utf8 executar_analise_62.py --case … --inputs …`).
- `SituacaoAnalise62.Ler(string ultimaLinha) -> SituacaoAnalise62` com `Situacao`, `Etapa`, `RetomarEmUtc`, `Motivo`, `Decisao`.
- Diretrizes e `TratamentoDoProcesso` vão em `<processo>\.pmvni-ai\analise\diretrizes.md`, como já acontece no analise-pro.

- [ ] Teste `ChatGpt_NaoAbreCodexExec`: a linha de comando iniciada é o Python do controlador; nenhum `codex exec "Use $analise"`.
- [ ] Teste `ChatGpt_CotaNaEtapaTecnica_AgendaRetomada`: saída 20 com `retomar_em_utc` → situação "Aguardando cota até HH:MM" e nova chamada automática no horário, sem clique.
- [ ] Teste `ChatGpt_Decisao_AbrePergunta`: saída 21 → o Editor mostra a decisão ao analista e chama de novo com a resposta.
- [ ] Teste `Rota_ResolvidaAntesDeIniciar`: com `herdar`, o Editor lê o provedor do `config.toml`, mostra qual rota será usada e recusa provedor incompatível **antes** de iniciar qualquer processo (erro 7); `openai` e `codex-lb` chegam ao controlador pela mesma variável nas duas camadas (erro 6).
- [ ] Teste `Cancelar_MataSoOControlador`: mesmo critério de pid e hora de partida usado no Claude.
- [ ] Teste `PluginAntigo_InformaComponente`: analise 3.0.1 instalado → mensagem "Atualize o kit PMVNI (analise 3.1.0 ou mais novo)" e nada é iniciado.
- [ ] Remover o caminho `codex exec resume` da controladora e `PedidoAgente.Continuar`; a retomada é chamar o controlador sem `--inputs`.
- [ ] **Rotas Claude e Gemini no Editor** (`OrquestradorAnalise.cs:398-450`, `SituacaoAnalisePro.cs`): passam a ler o mesmo contrato (situação `AGUARDANDO_COTA` com horário, `DECISAO`, `FALHA` com motivo) e a mesma checagem de versão mínima (analise-pro 2.1.0, analise-google 2.1.0). Testes `Claude_AguardandoCota_MostraHorario`, `Claude_Decisao_AbrePergunta`, `Gemini_PluginAntigo_InformaComponente`.
- [ ] Rodar `dotnet test tests/Pmvni.Agentes.Testes`; commit.

### Task 13: Progresso, previsão e consumo calculados pelo Editor

**Files:** Create `ProgressoLocal.cs`, `PrevisaoTermino.cs`, `UsoDasIas.cs`. Modify `RegistroExecucao.cs:214-232` (remover `InstrucoesProgresso`), `OrquestradorAnalise.cs:306,414`, `AchadosExecucao.cs`, `MainWindow.NovoRelatorio.cs:255-262,902-928`, `Roteiro.cs:3134,3330`. Test `Pmvni.Agentes.Testes`.

**Interfaces — Produces:**
- `ProgressoLocal.Ler(string pastaControle) -> Progresso { Etapa, EtapasConcluidas, SecoesComResultado, TotalSecoes = 28, Achados: IReadOnlyList<AchadoResumo> }`, a partir de `manifesto.json` (`section_coverage`, `findings`) e de `engine_state.json`.
- `PrevisaoTermino.Estimar(Progresso atual, IReadOnlyList<DuracaoEtapa> historico) -> DateTimeOffset?` — mediana por etapa e provedor das execuções anteriores (`uso-das-IAs.json` de todos os processos); `null` com menos de 3 execuções.
- `UsoDasIas.Ler(string pastaControle) -> IReadOnlyList<UsoEtapa { Etapa, Executor ("programa"|"GPT"|"Claude"|"Gemini"), Modelo, Entrada, Saida, CacheLido, Duracao, Cota5h, CotaSemana }>`.

- [ ] Teste `Pedido_NaoTemInstrucoesDeProgresso`: `diretrizes.md` contém só o texto do analista.
- [ ] Teste `Progresso_DoManifesto`: manifesto com 10 seções `ANALISADO` e 3 achados → 10/28 e os 3 achados com situação.
- [ ] Teste `Progresso_ManifestoIncompleto`: JSON em gravação (truncado) → mantém o último progresso válido, sem exceção.
- [ ] Teste `Previsao_SemHistorico_Nula` e `Previsao_PelaMediana`.
- [ ] Teste `Uso_MostraExecutorPrograma`: prepare e produce aparecem como "programa", sem tokens.
- [ ] Monitor: cartão "Consumo" por etapa (tokens, cache, variação da cota de 5 h e semanal) e selo de quem executou cada etapa. Commit.

### Task 14: Escolha de peças ambíguas no Editor

**Files:** Create `src/Pmvni.App/Dialogos/EscolhaDePecas.xaml(.cs)`. Modify `MainWindow.NovoRelatorio.cs`. Test `Pmvni.Agentes.Testes` (lógica) e roteiro (interface).

**Interfaces — Consumes:** `decisao.tipo == "pecas"` da Tarefa 4 (`ambiguas`). **Produces:** arquivo `<controle>\pecas_escolhidas.json` `{"prancha": "<caminho escolhido>"}` lido por `pecas_atuais.escolher`.

- [ ] Teste `Decisao_GravaEscolhaERetoma`: resposta do analista é gravada e o controlador é chamado de novo.
- [ ] Teste `Decisao_Cancelada_NaoGastaIA`: cancelar deixa a análise parada antes de qualquer sessão.
- [ ] Diálogo: miniaturas lado a lado (do `cache-pdf`), motivo da dúvida, botão para abrir cada PDF. Commit.

### Task 15: Despacho e revisão de tabelas sem IA por padrão

**Files:** Create `src/Pmvni.Revisao/Despachos/ResumoExigencias.cs`. Modify `MainWindow.NovoDespacho.cs:421`, `MainWindow.Revisao.cs:1281-1345`, `IaRecalculoIndices.cs`, `RecalculoIndices.cs`, `Esquadrias.cs`. Test `tests/Pmvni.Revisao.Testes`.

**Interfaces — Produces:** `ResumoExigencias.Montar(IReadOnlyList<Item> pendentes) -> string` (modelo fixo; mesma regra de "situação efetiva diferente de ATENDE" da correção automática).

- [ ] Teste `Resumo_SoPendentes`: itens ATENDE e excluídos ficam fora; ordem dos itens preservada.
- [ ] Teste `Correcao_NaoChamaIA`: montar despacho de correção não chama `IaDespacho`; "Ajustar com IA" continua disponível sob comando.
- [ ] Teste `ReverTabela_RecalculaLocal`: "Rever" numa tabela de índices ou esquadrias usa `RecalculoIndices`/`Esquadrias` e mostra valores anteriores e novos, com desfazer; a IA só entra quando há instrução livre digitada.
- [ ] Teste `CasosOuro_CSharp`: `RecalculoIndices` e `Esquadrias` dão o `esperado` de cada caso de `casos-ouro.json` (copiado do kit para `tests/referencias`), garantindo o mesmo resultado do Python.
- [ ] Commit.

### Task 16: Conferência depois da edição, primeira abertura e atualização do kit

**Files:** Modify `ConferenciaRevisao.cs`, `MainWindow.Ajustes.cs:386-399`, ponto de inicialização do app (onde `InstalacaoMotorPdf` é usado). Test `Pmvni.Revisao.Testes`, `Pmvni.Agentes.Testes`.

- [ ] Teste `Conferencia_ReferenciaQuebrada`: item que cita figura ou item removido → bloqueia a exportação e aponta seção e item.
- [ ] Teste `Conferencia_SituacaoContraditoria`: linha de tabela `NAO_ATENDE` com item `ATENDE` para o mesmo ID → aponta os dois.
- [ ] Teste `PrimeiraAbertura_Chamada`: instalação incompleta → `primeira-abertura.ps1` é iniciado fora do pacote (via `.cmd` + `explorer.exe`); instalação completa → não inicia.
- [ ] Teste `AtualizarKit_ComAnaliseAtiva`: botão "Atualizar kit PMVNI" com análise em andamento → aviso e nada iniciado.
- [ ] Commit.

## Parte D — Fechamento

### Task 17: Medição antes e depois, versões e entrega

- [ ] Antes de distribuir: rodar um caso fictício completo nas três rotas com o kit 3.0.1/2.0.1 atual e guardar `uso-das-IAs.json` como base (se ainda não houver base equivalente).
- [ ] Rodar o mesmo caso com as versões novas. Tabela antes/depois por etapa e provedor em `docs/auditoria/2026-11-21-antes-depois.md`: sessões, tokens, cache, duração, variação de cota, tentativas falhas. Conferir também: 28 seções cobertas, achados iguais nos dois, PDF legível.
- [ ] Simular: cota esgotada na analyze, troca de arquivo depois do preparo, interrupção entre etapas, objeção de apresentação. Registrar como cada uma aparece para o analista.
- [ ] Subir versões (kit: três `plugin.json` de cada plugin; Editor: `Directory.Build.props` 2.3.0 + `CHANGELOG.md`); atualizar os `SKILL.md`, `docs/motor-pdf.md` e `README.md`.
- [ ] Distribuir o kit pelo procedimento oficial; `recriar.json` do Editor e, se a ligação entre apps mudou (o Editor não depende mais do encaminhador na rota ChatGPT), `Recriar PC\sistema\MAPA-DO-AMBIENTE.md`; `Capturar agora.cmd` e `Conferir.cmd` fora do pacote.
- [ ] `tools\build-release.ps1` com GitHub, limpeza de builds anteriores conferida, push.
- [ ] Como voltar: os backups de `<KIT>\01 - PLUGINS\backups\<plugin>\antes-<versão>-<data>` e o instalador da 2.2.0; documentar o passo a passo no mesmo arquivo de antes/depois.
- [ ] Roteiro de conferência da interface para o usuário.

## Correspondência com as duas requisições

| Origem | Onde é resolvido |
|---|---|
| Erros 1–5 (controladora) | Tarefa 12 |
| Erros 6–7 (rota) | Tarefas 2 e 12 |
| Erros 8 e 13 (nível) | Tarefa 1 |
| Erros 9, 10, 14 (sessões, cota, contexto) | Tarefa 2 |
| Erro 11 (apresentação) | Tarefa 3 |
| Erro 12 (PDF na sandbox) | Tarefa 4 |
| Erro 15 (atualização no meio) | Tarefas 5 e 16 |
| Erro 16 (luna) | Tarefa 2 (confirmar antes) |
| Erro 17 (troca de conta) | Sem ação própria: efeito pequeno, some em boa parte com a Tarefa 12 |
| Relatório do Codex-LB sem as etapas técnicas | Tarefa 2 (sessões deixam de ser efêmeras) e Tarefa 13 (consumo no Editor) |
| ChatGPT 02, 03, 04 | Tarefas 4, 12, 13, 14 |
| ChatGPT 05 | Tarefas 7 e 15 (sem mudar o critério de vãos) |
| ChatGPT 06 | Tarefas 7 e 10 |
| ChatGPT 07 | Tarefas 6 e 16 |
| ChatGPT 08 | Tarefas 13 e 17 |
| Ideias extras do parecer | Tarefas 6, 8, 9, 10, 11, 13, 15 |
