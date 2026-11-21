# Revisão das etapas no motor único — Tarefa 20, Entrega 1 (21/11/2026)

Situação: proposta para o controlador decidir. Nada foi implementado. A Entrega 2 implementa a sequência aprovada.

Fontes: kit de desenvolvimento no HEAD `6785c02` (`etapas-analise.json`, `pro-stage-prompts.json`, `niveis-raciocinio.json`,
`matriz-gemini.json`, `fluxo-revisado.md`, `protocolo-de-execucao.md`, `auditoria-final.md`, os três `SKILL.md`,
`analise_runtime.py`, `executar_analise_62.py`, `pro_runtime.py`, os quatro validadores e os scripts novos das Tarefas 7, 8, 9 e 11);
plano `2026-11-21-correcoes-e-uso-da-ia.md`; diagnóstico `2026-11-20-consumo-codex-19486.md`; `engine_state.json`,
`uso-das-IAs.json` e controles de três processos reais (16488, 19486 e 19774/2026).

## 1. Resumo

Hoje o fluxo tem seis sessões de IA. A IA confere o mesmo resultado várias vezes. Os quatro objetos de auditoria (A, B, C e D) aparecem
em duas ou três etapas, e o PDF é olhado página por página até três vezes (produce, audit e final_review). Com o preparo e a
montagem passando a programa (Tarefa 4) e com os validadores e os scripts novos, sobra trabalho de julgamento para **três sessões**:

**preparo (programa) → análise (IA) → auditoria técnica independente (IA) → montagem (programa) → revisão do PDF (IA) → entrega (programa)**

A auditoria cruzada (`audit`) fica como etapa opcional por rota, ligada por padrão só na rota Claude, enquanto o piloto da Tarefa 17
não mostrar o que ela acrescenta. A mudança principal não é só cortar sessões. A conferência do **texto que será publicado**
(`relatorio_conteudo.json`) passa para a auditoria técnica, antes da montagem. Na única execução real que chegou à audit (19486),
as duas objeções técnicas foram justamente divergências entre esse texto e o manifesto auditado.

## 2. Custo observado

Os dados reais são poucos. Nenhuma execução registrada chegou à final_review, e as sessões técnicas do Codex não aparecem no
relatório do Codex-LB (erro 9). Os números abaixo somam os três processos. As durações do 19486 são aproximadas e vêm dos horários
das pastas `etapas\` e da linha do tempo do diagnóstico.

| Etapa | Execuções | Concluídas | Duração | Consumo medido | Observação |
|---|---|---|---|---|---|
| prepare | 9 | 9 | 3,5 a 10,1 min (mediana 7); ~59 min no total | 89 mil e 187 mil tokens (19774); 7 e 14 p.p. da cota de 5 h | Tudo descartado pela "COMPENSAÇÃO DO PREPARO" da analyze. Em 16488, cinco preparos seguidos. |
| analyze | 11 | 2 | concluídas: ~16 e ~10 min; falhas: ~121 min somadas | uma falha sozinha: 29 min, 695 mil tokens, 54 M em cache, US$ 18 equivalentes, 28 p.p. da cota de 5 h (Opus xhigh, 19774) | 9 falhas por ambiente, ferramenta negada ou cota, nenhuma por mérito. Em 19774, seis trocas de modelo e nível em 1 h. |
| technical_audit | 3 | 1 | ~22 min | não medido | Uma falha por cota com nova tentativa imediata; outra interrompida por cota no ciclo de correção. |
| produce | 2 | 1 | ~4 min (falha) + ~7 min | não medido | O Edge não gerou o PDF dentro da sandbox. |
| audit | 1 | 1 | ~6 min | não medido | 3 objeções: 2 técnicas e 1 de apresentação. |
| final_review | 0 | 0 | — | — | Nunca chegou a rodar. |

No 19486, as sessões de etapa somaram cerca de 115 min. Só ~58 min foram de trabalho que ficou (um preparo, a analyze final, a
technical_audit, uma produce e a audit). O resto foi repetição causada por falha de ambiente, cota ou ciclo de correção. A sessão
controladora gastou, sozinha, 23,4 M tokens de entrada, 64 % deles só esperando o script (erro 1, Tarefa 12).

**Por que a audit do 19486 achou o que a technical_audit não achou.** AUD-01: a tabela publicada dizia "MEDIDA NÃO INFORMADA" para
uma janela cuja medida estava no manifesto. AUD-02: o item publicado afirmava atendimento para todas as vagas e omitia a ressalva da
vaga 14, que estava no manifesto. As duas são do objeto D (publicação × resultado consolidado). O texto tinha sido escrito na analyze, e
a technical_audit auditou o manifesto, não o texto. Pela regra P09, as duas são técnicas (mudam alcance ou valor publicado), então a
Tarefa 3 (apresentação volta só ao produce) não teria evitado o ciclo. O que evitaria é conferir o texto antes da montagem. AUD-03, o
rótulo sobreposto na ficha, era só de apresentação.

## 3. Etapa por etapa

### prepare
- **Faz hoje:** sessão de IA (Sol baixo/médio, Sonnet médio, Flash/Hydrogen). Roda `check_kit_version.py` e `preparar_caso.py`,
  separa as peças atuais do histórico, busca normas no índice e registra os sete grupos do P01 como declarado, confirmado ou desconhecido.
- **Passa a programa:** tudo. Peças atuais por regra (`pecas_atuais`), com empate indo para o analista (código 21). Inventário,
  texto, renders e folhas de contato. `extrair_quadros` gera `modelo_projeto.candidatos.json`, `cadeias_cotas` gera as cadeias
  candidatas e `checklist_documental` confere os documentos. A lista fixa de bases (`arquivos-base.md`) e as normas usuais saem pelo
  `kit_pmvni.py buscar`. Na reanálise entra `diferenca_revisao`. O mapa é gerado como candidato se a inscrição for lida no texto.
- **Continua com IA:** nada. O enquadramento P01 passa para a análise, porque decidir "confirmado" exige ler o original.
- **Sobreposição:** hoje a analyze e a technical_audit refazem o preparo inteiro ("COMPENSAÇÃO DO PREPARO"). Isso some.

### analyze
- **Faz hoje:** é a única etapa que decide mérito. Percorre as 28 seções, confirma dados no original, escolhe operandos, roda
  `calcular_vaos`, monta `modelo_projeto.json` e roda `conferir_projeto`. Classifica os achados com contraprova (P01 a P07, P10),
  cuida dos oito controles da S28 e das duplicatas, e escreve o manifesto v3 e o `relatorio_conteudo.json`. Também refaz o preparo.
- **Passa a programa:** índices (`calcular_indices`; o validador já reprova linha que não saiu do script), fração do art. 127
  (`calcular_vaos`), formatos documentais e textos de achados padrão (`achados-padrao.json`, expandidos pelo `report_evidence montar`).
  O controlador roda `conferir_projeto` e `review_result_candidates` no fim da etapa, e o pedido deixa de mandar a IA rodá-los e conferi-los.
- **Continua com IA:** confirmar cada candidato no original e completar o que o código deixou de fora. Ler as páginas ilegíveis.
  Enquadramento P01, operandos e extremos das cotas (P04), ocorrência × código (P05), contraprova (P06), norma e vigência
  (`buscar_norma`), classificação e alcance, S28, decisão de duplicidade (P10) e a redação.
- **Sobreposição:** nenhuma dentro dela. O problema é o custo dos ciclos (seção 5.4).

### technical_audit
- **Faz hoje:** leitura nova e integral. A (registro → original), B (original → registros), C (consistência e S28), recálculo,
  reconciliação de vãos e duplicatas. **Corrige em lote** e repete até `ANALYSIS_AUDIT_OK`. Também refaz o preparo.
- **Passa a programa:** aritmética das memórias, IDs, cobertura das 28 seções, `door_review` × `opening_calculations` e a regra
  janela + porta (todos já em `validate_analysis.py`). Também `conferir_projeto` de novo e os candidatos a duplicata. A Tarefa 6 já
  tira isso do pedido.
- **Continua com IA:** A, B e C sobre os originais, por sessão diferente da analyze. Entra também o texto publicado: cada item,
  tabela e legenda de `relatorio_conteudo.json` contra o manifesto (a parte de conteúdo do objeto D).
- **Muda:** a auditora deixa de corrigir mérito. Ela registra objeções com `tipo` e `campos_alterados`, e a correção volta à análise
  (restrição 6). Hoje a mesma sessão audita, corrige e reaudita a si mesma.

### produce
- **Faz hoje:** sessão de IA. Roda `report_evidence montar` e `gerar_relatorio.py`, escreve `delivery.json`, aplica a checagem
  editorial P10 e reconcilia os IDs.
- **Passa a programa:** tudo, com o Edge fora da sandbox (Tarefa 4). Somam-se a renderização de todas as páginas do PDF em PNG,
  folhas de contato e uma conferência automática de layout: página vazia, assinatura isolada ou sem linha em branco antes, sequência
  de ITEM e Figura, marca `[[…]]` ou código interno que sobrou, texto fora da margem.
- **Continua com IA:** nada. A checagem editorial P10 vai para a revisão do PDF.

### audit
- **Faz hoje:** auditoria cruzada integral com fontes e PDF. A/B/C de novo, D, todas as páginas, os quatro validadores e
  `auditoria_visual.json`.
- **Passa a programa:** os quatro validadores (o gate já roda os quatro), a fidelidade ao modelo e a reconciliação de IDs e figuras.
- **Continua com IA:** uma segunda leitura A/B/C, por outro modelo. A parte de PDF e páginas sai daqui e vai para a revisão final.
- **Proposta:** vira etapa opcional por rota (seção 5.1), antes da montagem e sem PDF.

### final_review
- **Faz hoje:** lê o PDF página por página como leigo e como técnico, confere com o manifesto e, na dúvida, com o original. Corrige a
  apresentação, monta de novo e refaz a auditoria visual.
- **Passa a programa:** os quatro validadores (ficam no gate), a remontagem e a nova geração depois de correção de apresentação, e a
  trava de hash da Tarefa 3.
- **Continua com IA:** é a única inspeção visual de todas as páginas do PDF que será entregue. Absorve a parte visual e D da audit.

## 4. Sobreposições (quem faz cada conferência)

| Conferência | Hoje | Proposta |
|---|---|---|
| Refazer os dados do preparo no original | analyze, technical_audit | analyze confirma candidatos; technical_audit audita (A/B) |
| A: cada ID → original, inclusive positivos | technical_audit, audit, final_review (na dúvida) | technical_audit (+ audit cruzada onde ligada) |
| B: original → registros (omissões) | technical_audit, audit | technical_audit (+ audit cruzada) |
| C: consistência e oito controles S28 | analyze, technical_audit, audit | analyze faz; technical_audit audita |
| Aritmética, IDs, cobertura, janela + porta | technical_audit, audit, final_review + validadores | só programa (`validate_analysis`) |
| Duplicatas, sobreposições, contradições (P10) | analyze, technical_audit, audit, final_review | programa aponta candidatos; analyze decide; technical_audit audita |
| Norma e vigência | analyze, audit | analyze; technical_audit no A |
| Texto publicado × manifesto (D, conteúdo) | produce, audit, final_review (depois do PDF) | technical_audit, **antes** da montagem |
| PDF × dados montados, fidelidade ao modelo | produce, audit | programa (`report_evidence validate`, `validate_template_fidelity`, recibo) |
| Todas as páginas do PDF olhadas | produce, audit, final_review (até 3 vezes) | final_review, uma vez por versão do PDF, com renders e alertas do programa |
| Os quatro validadores | technical_audit, audit, final_review, gate | programa, entre etapas e no gate |

## 5. Sequência proposta

### 5.1 Etapas

| # | Etapa (id) | Executor | Recebe | Decide ou produz |
|---|---|---|---|---|
| 1 | `prepare` | programa | peças, escolha de peças do analista, kit | `preparo.json`, candidatos (quadros, cotas, checklist, mapa), `paginas_ilegiveis`, diferença da revisão |
| 2 | `analyze` | IA, sessão 1 | contexto da etapa (Tarefa 6), candidatos, saída do checklist, diretrizes; na reanálise, as seções afetadas e as pendências anteriores; no ciclo, o arquivo de objeções | manifesto v3, `modelo_projeto.json`, entradas de vãos e índices, `relatorio_conteudo.json`; confirma ou rejeita cada candidato |
| 3 | `technical_audit` | IA, sessão 2 (nova, nunca a da analyze) | manifesto, `relatorio_conteudo.json`, saída real dos validadores, lista de páginas e vistas | `section_audit` e `final_audit` por ID, `auditoria_tecnica.json` com objeções tipadas; não altera mérito |
| 3b | `audit` (opcional) | IA, sessão 3 (outro modelo) | o mesmo da 3 | A/B/C de novo, objeções tipadas; padrão ligado só na rota Claude |
| 4 | `produce` | programa | conteúdo e manifesto auditados | `relatorio_dados.json`, PDF, recibo, renders das páginas, `alertas_layout.json`, `delivery.json` |
| 5 | `final_review` | IA, sessão nova | PDF, renders, alertas de layout, manifesto e conteúdo auditados, histórico de objeções | `auditoria_visual.json` (todas as páginas, hash do PDF), objeções tipadas; pode corrigir só apresentação |
| 6 | `gate` | programa | tudo | os quatro validadores, `opening_rule_version 2`, schema 3, hash do PDF = hash revisado; arquivamento |

### 5.2 Validadores por programa entre as etapas

- **Depois do prepare:** hashes das peças, `check_kit_version`. Peças ambíguas param com código 21 antes de qualquer sessão.
- **Depois da analyze:** `calcular_vaos` e `calcular_indices` rodados de novo com as entradas da IA (resultado igual ao gravado),
  `conferir_projeto`, `review_result_candidates`, `report_evidence montar --simular` (toda marca `[[ID]]` resolve) e
  `validate_analysis --fase analise`. Esse modo é novo: confere a estrutura sem exigir `section_audit` e `final_audit`, que hoje
  impedem `ANALYSIS_AUDIT_OK` antes da auditoria. Também confere que cada candidato tem `verificacao` confirmada ou rejeitada, com
  local. Se reprovar, a mesma sessão é retomada com a saída do validador (até 2 vezes). Depois disso, conta falha técnica e sobe o nível.
- **Depois da technical_audit (e da audit):** `validate_analysis` completo (`ANALYSIS_AUDIT_OK`). Trava de independência: o hash
  de `findings`, `opening_calculations`, `dimension_chains`, `section_coverage` e `relatorio_conteudo.json` tem de ser igual ao do fim
  da analyze. A auditora só escreve os campos de auditoria e as objeções.
- **Depois do produce:** `report_evidence validate`, `validate_plugin_workflow` (as entradas das etapas de programa são escritas pelo
  controlador) e a conferência do gerador.
- **Depois da final_review:** `validate_template_fidelity` com a auditoria visual. Se houve correção de apresentação, a trava
  da Tarefa 3 roda de novo.

### 5.3 O que faz voltar à análise

| Gatilho | Volta para | Invalida | Sessão |
|---|---|---|---|
| Objeção `tecnica` da technical_audit, da audit ou da final_review | `analyze` | technical_audit, audit, produce, final_review | retoma a sessão 1 com o arquivo de objeções; a reauditoria é sempre sessão nova e integral (P09) |
| Objeção `apresentacao` cujos `campos_alterados` tocam valor, ID, norma, localização, figura, classificação, alcance ou sentido | `analyze` | idem | idem (Review Focus 5) |
| Correção de apresentação que muda o hash de `findings` ou `opening_calculations`, ou muda número, ID, `situacao`, `base_legal` ou marca no texto | `analyze` | idem | idem |
| `ANALYSIS_AUDIT_OK` ou `REPORT_EVIDENCE_OK` reprovado no gate | `analyze` | idem | idem |
| Peça trocada depois do prepare | `prepare` | tudo, com `diferenca_revisao` | nova |
| Apresentação pura (tipografia, espaçamento, rótulo sobreposto, legenda sem mudança de sentido) | `produce` → `final_review` | só produce e final_review | a final_review edita só os campos de apresentação; o programa remonta; a mesma sessão revê **todas** as páginas do PDF novo |
| `TEMPLATE_FIDELITY_OK` reprovado | `produce` | produce, final_review | — |

Na dúvida, é técnica (P09). Objeções são reunidas numa passada só e corrigidas em lote.

### 5.4 Restrições, uma a uma

| Restrição | Como fica |
|---|---|
| 28 seções | Sem mudança. `validate_analysis` em duas fases; a auditoria cobre as 28 seções e todos os IDs. |
| Auditoria técnica independente sobre os originais | technical_audit em sessão nova, nunca retomada da analyze, com a trava de hash que impede a auditora de mudar mérito. Ela agora audita também o texto publicado. |
| Nenhuma conclusão sem evidência | Candidatos de programa entram como `pendente` e só valem confirmados no original. O validador da fase de análise reprova candidato sem decisão e local. |
| Vãos janela + porta | `calcular_vaos` e as regras do `validate_analysis` não mudam (`opening_rule_version 2`). |
| Revisão do PDF final antes da entrega | final_review olha todas as páginas da versão exata entregue. O gate confere o hash. |
| Correção de mérito volta à análise com reauditoria | A auditora deixa de corrigir mérito. Toda objeção técnica volta à analyze e passa por reauditoria integral em sessão nova. |

## 6. Ganhos e riscos

**Ganhos esperados** (estimativas; a Tarefa 17 mede):
- Sessões por análise: de 6 para 3 (4 na rota Claude). Somem os ~7 min e ~100 a 190 mil tokens de cada preparo e as falhas de Edge na produce.
- O PDF é olhado uma vez por versão, não três.
- Objeção de publicação é pega antes da montagem: um PDF a menos por ciclo, e a final_review não vira auditoria técnica.
- Um erro de validador na analyze deixa de custar uma etapa nova: a mesma sessão é retomada só com a mensagem do validador.
- O ciclo de correção retoma a sessão da analyze, que já tem o contexto. Não refaz a leitura das 28 seções. No 19486, esse ciclo
  custou uma analyze nova (~10 min, astra alto) antes da reauditoria.
- Pedidos menores: a "COMPENSAÇÃO DO PREPARO" sai, as tarefas de validador saem e as referências vão por etapa (Tarefa 6).

**Riscos de qualidade:**
1. **Sai uma segunda leitura técnica independente** onde a audit cruzada fica desligada (Codex e Gemini). Hoje a audit refaz A/B/C;
   no 19486 ela achou dois problemas que a technical_audit deixou passar. *Mitigação:* os dois eram do objeto D, que agora a
   technical_audit cobre. A audit continua disponível por rota, e a Tarefa 17 compara os achados com e sem ela antes de desligá-la de vez.
2. **Ancoragem nos candidatos do programa.** A IA pode confirmar o que o código extraiu e deixar de ver o que ele deixou de fora,
   porque os extratores preferem precisão a cobertura. *Mitigação:* o objeto B (original → registros) continua integral na auditoria.
   As páginas ilegíveis são listadas. O validador registra quantos candidatos foram confirmados, rejeitados e acrescentados; 100 %
   confirmados sem nenhum acréscimo vira alerta para a auditora.
3. **Correção de "apresentação" que muda sentido** na final_review. A trava de hash pega número e ID, mas não alcance redigido.
   *Mitigação:* a final_review só pode editar campos de apresentação (legenda, tipografia). Qualquer diferença em número, ID,
   `situacao`, `base_legal` ou marca vira técnica, e na dúvida é técnica.
4. Menor: a auditora que só registra objeções pode gerar mais ciclos do que a que corrigia sozinha. Fica compensado pela retomada da
   sessão 1 e pela correção em lote.

## 7. Alternativas consideradas

- **Manter as quatro sessões da Tarefa 4** (analyze, technical_audit, audit, final_review). É a decisão do plano. Fica a segunda
  leitura A/B/C, mas o PDF continua sendo olhado duas vezes e o objeto D continua depois da montagem. Recusada como padrão: custa uma
  sessão média e alta inteira por análise, e o problema do 19486 está no momento da conferência, não no número de leituras.
  **Atenção:** a proposta reabre a decisão "de 7 para 4 sessões". O controlador precisa decidir no livro de controle.
- **Juntar a technical_audit e a final_review numa sessão só, depois da montagem.** Uma sessão a menos. Recusada: toda objeção técnica
  achada nela custa um PDF já gerado, e mistura auditoria de mérito com revisão de leitura.
- **Retomar a sessão da auditora na reauditoria.** Mais barato, mas fere a "leitura nova" do P09 e o "não copiar confirmação
  anterior". Recusada.
- **Auditoria seletiva só das seções afetadas depois de correção técnica.** Recusada pelo P09 e pela restrição 6. A `diferenca_revisao`
  só orienta a análise; a reauditoria continua integral.
- **A IA faz só A e o programa faz B.** Recusada: não existe extrator que prove ausência de omissão. B continua com a IA.

## 8. Forma proposta de `references/etapas.json`

Lido pelos três controladores (rota = parâmetro), pela tabela de modelos da Tarefa 19 (uma linha por etapa; `programa` aparece
sem opção de modelo) e pelo Editor (`ProgressoLocal`, `UsoDasIas`). Os ids atuais ficam iguais, por causa da retomada, de
`engine_state.json` e da grade de modelos.

```json
{
  "versao": 1,
  "ordem": ["prepare", "analyze", "technical_audit", "audit", "produce", "final_review", "gate"],
  "etapas": {
    "prepare": {
      "executor": "programa",
      "entradas": ["pecas", "pecas_escolhidas.json"],
      "saidas": ["preparo.json", "modelo_projeto.candidatos.json", "cadeias_cotas.candidatos.json",
                 "checklist_documental.json", "diferenca_revisao.json"],
      "validadores": [{"script": "check_kit_version.py", "espera": "código 0"}],
      "decisao_analista": ["pecas"]
    },
    "analyze": {
      "executor": "ia", "sessao": "retomavel",
      "referencias": ["checklist-mestre.md", "fluxo-revisado.md#P01-P07,P10", "leitura-arquitetonica.md", "redacao-institucional.md"],
      "entradas": ["preparo.json", "*.candidatos.json", "checklist_documental.json", "diretrizes.md", "objecoes_pendentes.json?"],
      "saidas": ["manifesto.json", "modelo_projeto.json", "vaos_entrada.json", "indices_entrada.json", "relatorio_conteudo.json"],
      "validadores": [
        {"script": "calcular_vaos.py", "modo": "reexecutar_e_comparar"},
        {"script": "calcular_indices.py", "modo": "reexecutar_e_comparar"},
        {"script": "conferir_projeto.py"}, {"script": "review_result_candidates.py"},
        {"script": "report_evidence.py", "args": ["montar", "--simular"]},
        {"script": "validate_analysis.py", "args": ["--fase", "analise"], "espera": "ANALYSIS_STRUCTURE_OK"}],
      "em_falha_validador": {"acao": "retomar_mesma_sessao", "max": 2, "depois": "falha_tecnica"}
    },
    "technical_audit": {
      "executor": "ia", "sessao": "nova", "independente_de": ["analyze"],
      "referencias": ["fluxo-revisado.md#P08-P10", "auditoria-final.md"],
      "entradas": ["manifesto.json", "relatorio_conteudo.json", "saida_validadores.json", "originais"],
      "saidas": ["manifesto.json#section_audit,final_audit", "auditoria_tecnica.json"],
      "imutaveis": ["manifesto.json#findings,opening_calculations,dimension_chains,section_coverage", "relatorio_conteudo.json"],
      "validadores": [{"script": "validate_analysis.py", "espera": "ANALYSIS_AUDIT_OK"}],
      "voltas": [{"quando": "objecao.tipo == 'tecnica' or objecao.campos_alterados & CAMPOS_TECNICOS", "para": "analyze"}]
    },
    "audit": {
      "executor": "ia", "sessao": "nova", "independente_de": ["analyze", "technical_audit"],
      "opcional": true, "ligada_por_rota": {"codex": false, "claude": true, "gemini": false},
      "herda": "technical_audit"
    },
    "produce": {
      "executor": "programa",
      "entradas": ["relatorio_conteudo.json", "manifesto.json", "vaos_calculo.json", "report_evidence.json"],
      "saidas": ["relatorio_dados.json", "<relatorio>.pdf", "recibo", "paginas_pdf/*.png", "alertas_layout.json", "delivery.json"],
      "validadores": [{"script": "report_evidence.py", "args": ["validate"], "espera": "REPORT_EVIDENCE_OK"},
                      {"script": "validate_plugin_workflow.py", "espera": "PLUGIN_WORKFLOW_OK"}]
    },
    "final_review": {
      "executor": "ia", "sessao": "nova",
      "referencias": ["redacao-institucional.md", "fluxo-revisado.md#P10", "auditoria-final.md#inspecao-visual"],
      "entradas": ["<relatorio>.pdf", "paginas_pdf/*.png", "alertas_layout.json", "manifesto.json", "relatorio_conteudo.json",
                   "auditoria_tecnica.json"],
      "saidas": ["auditoria_visual.json", "objecoes_revisao.json"],
      "pode_editar": ["relatorio_conteudo.json#campos_apresentacao"],
      "validadores": [{"script": "validate_template_fidelity.py", "espera": "TEMPLATE_FIDELITY_OK"},
                      {"trava": "hash_manifesto_e_campos_tecnicos"}],
      "voltas": [{"quando": "objecao.tipo == 'apresentacao' and not campos_tecnicos", "para": "produce", "depois": "final_review"},
                 {"quando": "objecao.tipo == 'tecnica' or trava_falhou", "para": "analyze"}]
    },
    "gate": {"executor": "programa", "validadores": ["os quatro", "opening_rule_version == 2", "schema_version == 3",
                                                       "sha256(pdf) == auditoria_visual.pdf_sha256"]}
  },
  "campos_tecnicos": ["valor", "id", "norma", "localizacao", "figura", "classificacao", "alcance", "sentido"],
  "regra_volta": "Voltar a uma etapa invalida ela e todas as seguintes na ordem; reauditoria sempre em sessão nova e integral."
}
```

## 9. Pontos para o ruling do controlador (antes da Entrega 2)

1. Aprovar 3 sessões (4 na rota Claude) no lugar das 4 decididas no plano, ou manter as 4 com a conferência D movida para a
   technical_audit.
2. Valor padrão de `audit` por rota. O `fluxo-revisado.md` hoje diz que a auditoria cruzada do pro "continua integral e
   sequencial"; desligá-la em alguma rota exige mudar esse texto.
3. Criar `validate_analysis --fase analise` (mudança em validador comum, com teste nos casos de ouro).
4. Retomada de estados antigos: no 3.x, `audit` vinha depois do `produce`. Um estado com `audit` concluída e `final_review`
   pendente retoma na final_review nova; um estado com `audit` pendente retoma na final_review, sem rodar a audit cruzada antiga.
5. A Tarefa 3 continua valendo (destino da objeção e trava de hash), agora lida das `voltas` deste arquivo, sem lista fixa no código.

## 10. Decisões do controlador (21/11/2026)

1. **Sequência adotada:** prepare (programa) → analyze (IA, sessão 1, retomável) → technical_audit (IA, sessão nova; também confere o texto publicado `relatorio_conteudo.json`, com trava de hash) → audit cruzada → produce (programa) → final_review (IA, só apresentação) → gate (programa). Os ids das etapas ficam iguais. Isso substitui a meta "de 7 para 4 sessões" do plano: são 3 sessões de IA, 4 com a audit cruzada.
2. **Audit cruzada:** ligada por padrão nas três rotas, porque as três valem igualmente e a qualidade vem antes. Ela pode ser desligada por rota em `modelos-por-rota.json` / aba "Modelos da análise" (Tarefa 19). O `fluxo-revisado.md` passa a dizer "integral e sequencial quando ligada; ligada por padrão". A Tarefa 17 mede o resultado com ela ligada e desligada antes de mudar o padrão.
3. **`validate_analysis --fase analise`:** aceito. A conferência da fase de análise fica separada da conferência final.
4. **Estados 3.x:** sem mapeamento. Uma análise iniciada com plugin antigo recomeça, como já foi decidido na Tarefa 18.
