# Segunda análise fictícia de ponta a ponta (22/11/2026)

Esta rodada cobriu o que a primeira não testou: a rota Gemini, o Editor disparando a análise, a interrupção e a retomada, o cancelamento, a espera de cota, a objeção que bloqueia a entrega, a conferência do kit depois da publicação, peças problemáticas, a carga no PC, a conferência dos cálculos à mão e a estimativa de tokens.

Nenhuma IA real foi chamada. O codex.exe e o agy foram trocados por simuladores, e o banco do codex-lb e a telemetria de 5 h também foram simulados. O caso foi uma cópia do 19486, com as 14 peças enviadas em `--inputs`, como o Editor faz. O kit veio do repositório de desenvolvimento por meio de raízes temporárias. A pasta PROCESSOS e a instalação do kit não foram tocadas.

## Previsto × medido

Referência: `resultado-kit-fluxo-linear.md` previa **24–45 min e 24–40 pontos** da janela de 5 h. A meta é de no máximo 50% da janela de 5 h e no máximo 1 h.

| Etapa | Previsto (min) | Medido ou estimado (min) | Previsto (pts 5 h) | Estimado (pts 5 h) | Base |
|---|---:|---:|---:|---:|---|
| Preparo + processamento local | 1–4,5 | **0,55–0,8 medido** (33 s Codex, 46 s Gemini, local 2,5 s) | 0 | 0 | 7 peças; 1ª rodada: 66 s com 14 peças |
| Analyze, 3 frentes em paralelo | 10–15 | 6–15, central **9** | 15–24 | 6–27, central **11,6** (×1,2) | 21, 19 e 14 chamadas de cerca de 25 s |
| Montagem e ensaio | 0,5–2 | **0,3 medido** | 0 | 0 | ensaio, produce, final_review e gate juntos em cerca de 17 s |
| Auditoria técnica | 9–15 | 8–20, central **12,5** | 7–12 | 4–32, central **9,5** (×1,2) | 30 chamadas |
| Reparo pontual | 2–5 | **0** esperado (0–3) | 2–4 | 0 (até 1,3) | a formalidade do plugin_workflow não provoca mais reparo |
| **Total** | **24–45** | **16–40, central cerca de 23** | **24–40** | **central cerca de 21; provável 10–35; pior caso cerca de 58** | |

A previsão central fica abaixo da faixa prevista e dentro da meta: cerca de 21% da janela e cerca de 23 min. O pior caso junta todos os limites altos ao mesmo tempo (45 chamadas na auditoria e crescimento de contexto igual ao da 2ª rodada) e passaria de 50. Nesse caso, a guarda do codex-lb para a análise com 45 pontos (`orcamento_lb.MARGEM_PARADA`), e o prazo de 60 min do código (`fluxo_linear.segundos_disponiveis`) e o de 20 min por sessão também limitam. A margem de erro está explicada na seção de tokens.

## Resultado por item

| # | Item | Resultado | Evidência |
|---|---|---|---|
| 1 | Rota Gemini de ponta a ponta | **OK** | `google_runtime.py auto`, perfil conta_ultra, do preparo ao gate. 6/6 etapas EXECUTED, PDF gerado, sem reparo, 107,6 s. Frentes em gemini-3.8-flash-medium e technical_audit em gemini-3.8-flash-high. |
| 2 | O Editor dispara a análise (`OrquestradorAnalise`) e passa a tabela de modelos | **OK** | xUnit temporário com codex.exe falso. O simulador recebeu gpt-6.2-sol/medium nas 3 frentes, sol/high na technical_audit e sol/medium no reparo. O engine_state gravou `tabela_de_modelos: "ajuste local"`, o que prova que o kit leu `PMVNI_MODELOS_POR_ROTA`. O Monitor mostrou "esforço médio" e "esforço alto" e 6/6 etapas. |
| 3 | Interrupção de uma frente e retomada | **OK** | A frente 10 foi morta com `taskkill /T /F` e a análise ficou FAILED, com o estado legível. Na retomada, só a frente 10 e a auditoria chamaram o provedor. O `pedido.txt` das frentes 01 e 19 manteve a data, porque elas foram reaproveitadas pelo `concluida.json`. Terminou ENTREGUE. |
| 4a | Cancelamento pelo Editor | **OK** | O cancelamento encerrou a árvore inteira de processos, inclusive o simulador que dormia 600 s. A trava ficou livre, a análise ficou RUNNING e nunca EXECUTED, e `PodeRetomarChatGpt` deu verdadeiro. "Continuar a análise" reaproveitou as frentes concluídas. |
| 4b | Espera de cota | **OK** | Com "You've hit your usage limit. Try again in 61s", o Monitor mostrou "Aguardando cota até 13:01" e retomou sozinho depois de 62 s. Só a frente afetada foi refeita, e o estado ficou íntegro. |
| 5 | Objeção que a auditoria não corrige | **OK, com ressalva** | Com `objecoes_technical_audit.json` gravado, a etapa ficou OBJECTIONS e a análise continuou EXECUTED, sem `ciclos_correcao` e sem `delivery.json`. A objeção apareceu na linha da Auditoria técnica (`DetalhesDasEtapas.Objecoes`), e nenhuma ação manda de volta ao analyze. Ressalva: "Retomar análise" com a objeção pendente abre uma nova technical_audit, que gasta cota, e o Editor não avisa desse custo. |
| 6a | Conferência depois de publicar | **OK** | Numa raiz temporária com 01 vindo de `git archive HEAD` e 00 e 02 do mestre, com a skill instalada igual ao HEAD, `check_kit_version` deu **CURRENT**, sem erros. |
| 6b | Janela entre publicar e instalar | **Risco** | Com a instalação ainda em e2c4c4d e o mestre já no HEAD, o resultado é **CHECK_FAILED** ("Mesma versão do plugin com conteúdo diferente"), porque as duas cópias se dizem 4.0.0 e diferem em 48 arquivos. O preparo fica recusado até a tarefa "Atualizar kit" rodar, a cada 15 min, ou até o `INSTALAR_KIT_PMVNI.cmd atualizar`. |
| 6c | Editor 3.4.0 com o kit antigo instalado (e2c4c4d) | **OK** | `FluxoLinear.DoKit` deu falso e a tabela de modelos foi montada sem erro. O Monitor mostrou 0 frentes, 2/6 etapas, 28 seções, 27 achados e 1 objeção, sem exceção. |
| 7 | Peças problemáticas | **OK depois de 2 correções** | Ver a tabela abaixo. |
| 8 | Carga no PC | **OK** | No máximo **2** processos pesados ao mesmo tempo (geometria_pdf/docling), mais o `preparar_caso`, que fica quase parado. O OCR rodou um por vez. Detalhes na seção "Uso do PC". |
| 9 | Estimativa de tokens | feita | Ver a seção de tokens. |
| + | plugin_workflow sem reparo por formalidade (pedido do coordenador) | **Corrigido** | Com o simulador regravando os recursos como a IA real faz (adobe_acrobat e superpowers_plan como NOT_APPLICABLE, pdf como USED), houve 0 reparo no Codex e no Gemini. Os registros do programa prevaleceram no arquivo final. |
| f | Cálculos conferidos à mão | **OK depois de 2 correções** | Ver a seção de cálculos. |

### Peças problemáticas (item 7)

Caso montado no scratchpad com peças geradas e uma foto HEIC real.

| Peça | Resultado |
|---|---|
| Escritura escaneada (2 páginas, sem camada de texto) | O OCR do Windows leu 151 caracteres por página, com o texto correto. |
| BCI escaneado | O texto foi lido, mas a inscrição saiu "OI .02.051.0255.001" e a inscrição não era achada. **Bug corrigido** em `local/cadastro.py`: troca O/I/l por 0/1 só no início do número depois do rótulo e confere pelo formato oficial. Teste: `test_inscricao_do_bci_escaneado_com_o_e_i_no_lugar_de_0_e_1`. |
| PDF com página em branco | O OCR rodou e ficou marcado `indisponivel`, porque o OCR não distingue página vazia de OCR ausente. Agora o pedido da frente 1 avisa a IA para ler a página pela `imagem_ver` e registrar o `adobe_acrobat`. |
| PDF de 121 páginas (120 A4 e 1 A0) | Preparo de todo o caso em 26 s. A página A0 foi classificada como prancha. A lista de peças e as folhas de contato crescem junto (otimização d3). |
| Nome com acento, travessão e espaços (caminho de 320 caracteres) | OK. O cache usa o sha256, e o texto e a imagem foram gerados. |
| Peça duplicada (mesmo conteúdo, outro nome) | OK. Fica `identica_a` e é processada uma vez só. |
| JPG de 4284×5712 | OK. `imagem_ver` de 1600 px. |
| HEIC do iPhone | **Bug corrigido.** A peça entrava sem imagem nenhuma para a IA, e a folha de contato quebrava com "cannot identify image file". Agora o `preparar_caso.converter_heic` usa o heic-reader, que ganhou o backend **Windows WIC** (decodificador HEIF do Windows, sem instalar nada). Sem conversor, a peça fica com `imagem_indisponivel`. O `pecas_atuais` passa a inventariar .heic e .heif. Teste: `test_heic_convertido_tem_imagem_para_ver_e_sem_conversor_fica_registrado`. |

## Bugs reais corrigidos

**Kit** (commit `87c2321` no repositório de desenvolvimento, sem push; os digests de `perfis-compativeis.json` foram recalculados no fim):

1. **Gatilhos do plugin_workflow provocavam reparo por formalidade.** Em `etapas_programa.prepare`, o `needs_ocr` ficava verdadeiro sempre que o OCR rodava, até com o resultado "indisponivel", e o `complex_process` ficava verdadeiro com mais de uma peça. Com isso, o `validate_plugin_workflow.py` exigia o `adobe_acrobat` e o `superpowers_plan`. Se a IA registrasse NOT_APPLICABLE, a análise ia para uma sessão de reparo.
   - **Correção.** A nova `workflow_do_preparo` marca o OCR só quando uma página de documento sem texto precisou dele. O programa grava como LOCAL_ALTERNATIVE (executor programa) o que ele mesmo resolveu: superpowers_using, superpowers_plan (o plano fixo do executor), pdf, pdf_to_markdown (docling) e adobe_acrobat, quando o OCR local leu a página. Os recursos sem gatilho ficam NOT_APPLICABLE.
   - No `produce`, a `reconciliar_workflow` faz os registros do programa prevalecerem sobre os da IA e descarta as duplicatas.
   - O validador ganhou `exigidos()` e `PROGRAMA`: o programa só pode registrar o que resolveu localmente ou a dispensa de recurso sem gatilho.
   - O pedido da frente 1 diz "não o altere" ou lista só o que falta (`fluxo_linear.recursos_pendentes`).
   - Testes: `PluginWorkflowDoProgramaTests` (4 testes).
2. **A regra de revisão não valia quando o Editor mandava todas as peças.** O `pecas_atuais.selecionar` tratava qualquer `--inputs` como escolha do analista. O Editor manda PROJETO e DOCUMENTOS inteiros, por isso entrava a PRANCHA 2 da REVISÃO 02, já superada pela REVISÃO 04: 8 peças em vez de 7, também na rodada real. Agora, entradas que cobrem o inventário inteiro seguem a regra de revisão, e uma seleção parcial continua explícita. Teste: `test_entradas_com_o_processo_inteiro_seguem_a_regra_de_revisao`.
3. **HEIC sem imagem** (item 7).
4. **Inscrição do BCI escaneado** (item 7).
5. **Texto dos vãos no limite exato** (`calcular_vaos.casas_fieis`). O relatório imprimia "1,56 m² < mínimo 1,56 m²; faltam 0,00 m²". Agora usa só as casas decimais necessárias para os números não saírem iguais. Teste: `test_limite_exato_atende_e_diferenca_pequena_aparece_no_texto`.
6. **R05 sem o art. 128, § 2º** (`conferir_projeto.py` e `regras-de-projeto.json`). Em habitação com mais de 2 dormitórios, um deles pode ter 6,00 m². Antes, era um falso positivo. Teste: `test_terceiro_dormitorio_pode_ter_seis_metros`.

**Editor:** nenhum bug encontrado. Nenhum arquivo de código foi alterado.

## Cálculos conferidos à mão (item f)

A linha ZCC-2 do `anexo-vi-zonas.json` confere com o PDM, Anexo VI, p. 72: TO 75%, TP 10%, CA 4,00, frente 3,00 m, fundos 1,50 m, lateral 1,50 m com abertura, 7 pavimentos (nota \*3), 23,50 m e lote de 300 m² com testada de 12 m. O art. 127 da LC 70/1990 confere com o `regras-de-projeto.json`.

| Grandeza (19486) | Prancha | Conta manual | Kit | Situação |
|---|---|---|---|---|
| Área construída | 899,82 | 2 × 449,91 = 899,82 | 899,82 | OK |
| TO | 68,36% | 449,91 ÷ 658,13 = 68,362% ≤ 75% | 68,36%, confere | OK |
| CA | 1,37 | 899,82 ÷ 658,13 = 1,3672 ≤ 4 | 1,37, confere | OK (premissa: computável = construída) |
| TP | 11,05% e 11,01% | 72,72 e 72,49 ÷ 658,13 ≥ 10% | PENDENTE por divergência entre revisões | OK |
| Terreno: quadro × base × BCI | 658,13 / 663,00 / 658,00 | −0,73% e +0,02% | igual | OK |
| Gabarito e altura | 2 pisos, 9,39 m | ≤ 7 e ≤ 23,50 | PENDENTE (fica para a IA) | OK |
| Afastamento frontal | 5,00 m | ≥ 3,00 | PENDENTE (fica para a IA) | OK |
| Cadeias de cotas | — | CC003: 5,92 + 1,20 = 7,12 × 7,16 | CC003 e CC059 não fecham por −0,04 | OK |
| Vãos dos 12 ambientes | quadro de esquadrias | frações exatas por ambiente | os 12 batem | OK |

Casos sintéticos: 52 de 52 depois das correções. Entre eles:
- limite exato atende: TO 75%, CA 4,00, TP 10%, altura 23,50 m, vão 1/5 e 1/8 exatos;
- 0,01 acima do limite não atende: TO 75,01%, altura 23,51 m, frente 2,99 m, vão 1,5624;
- terreno zero é recusado como operando inválido, e área permeável zero dá TP 0%, que não atende;
- dado faltante fica PENDENTE com o nome do dado.

## Tokens e cota (item 9)

**Contagem.** Não há tiktoken neste PC e ele não foi instalado. Usei cerca de **3,7 caracteres por token** para português com JSON e caminhos, com erro de ±10%. A calibração usou o `log-completo.txt` da 2ª rodada real, com 5 sessões e 197 requisições de trabalho. Ajustando a cota contra a entrada: cerca de **19,2 pontos por M de entrada sem cache** e **2,05 pontos por M com cache**, com erro de cerca de ±3 pontos por sessão. Pela média simples, 97 ÷ 35,4 M dá 2,74 pontos por M.

**Medido na 2ª rodada real:**

| Sessão | Chamadas | Entrada | Cache | Saída | Contexto na 1ª chamada | Contexto máximo | Crescimento por chamada | Cota |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| analyze (única) | 63 | 14,61 M | 98% | 86 mil | 28,9 mil | 344 mil | 5,1 mil | 36 |
| technical_audit | 34 | 6,94 M | 96% | 34 mil | 18,6 mil | 303 mil | 8,6 mil | 20 |
| correção | 55 | 8,45 M | 97% | 39 mil | 29,7 mil | 216 mil | 3,4 mil | 19 |
| 2ª e 3ª technical_audit | 23 + 22 | 5,41 M | 92% | 16 mil | 19,3 mil | 227 mil | 8,4–9,4 mil | 19 |

**Pedidos medidos no fluxo novo** (texto exato que o provedor recebeu):

| Pedido | Codex | Gemini | Tokens (Codex) | Peso do checklist |
|---|---:|---:|---:|---:|
| Frente 1–9 | 15.877 caracteres | 18,0 mil | cerca de 4,3 mil | 9,2 mil caracteres |
| Frente 10–18 | 14.010 | — | cerca de 3,8 mil | 10,7 mil |
| Frente 19–28 | 13.081 | — | cerca de 3,5 mil | 10,1 mil |
| technical_audit | 13.594 | 15,2 mil | cerca de 3,7 mil | lista dos 27 achados: 11,5 mil |
| Reparo | 2,0–3,3 mil | — | cerca de 0,9 mil | — |

O contexto fixo da sessão Codex (sistema e ferramentas) é de cerca de 17,5 mil tokens, deduzido da 1ª chamada das auditorias (18,6–19,3 mil). Os dados prontos acrescentam cerca de 1 mil tokens ao todo.

**Imagens** (por `imagem_ver` de 1600 px em detail high):
- GPT: cerca de 1.105 tokens por página, com 6 blocos de 512 px; um recorte JPG fica entre 500 e 800.
- Claude: cerca de 1.500–1.600 tokens.
- Gemini: cerca de 1.100–1.550.

Abertura provável de imagens: frente 1–9 com cerca de 8 imagens (pranchas, BCI, escrituras, mapa e recortes), frente 10–18 com cerca de 8, frente 19–28 com cerca de 5 e auditoria com cerca de 15, ou seja, 6–15 mil tokens por sessão. Cada imagem volta em todas as chamadas seguintes e por isso entra no crescimento por chamada.

**Modelo.** A entrada de uma sessão é calculada como n × (17,5 mil + pedido) + g × n(n−1)/2, em que n é o número de chamadas e g o crescimento por chamada. A parte sem cache é a 1ª chamada mais os acréscimos. Aplicado à sessão analyze da 2ª rodada, o modelo dá 11,8 M e 30 pontos, contra 14,6 M e 36 medidos, ou seja, subestima cerca de 20%. Por isso o central abaixo já vem **multiplicado por 1,2**.

| Etapa / rota Codex | Chamadas | Entrada (com cache) | Sem cache | Saída | Contexto máximo | Pontos 5 h (central) | Faixa |
|---|---:|---:|---:|---:|---:|---:|---:|
| Frente 1–9 | 21 (15–30) | 1,43 M | 110 mil | 19 mil | 92 mil | 4,8 | 2,6–10,6 |
| Frente 10–18 | 19 (13–28) | 1,20 M | 100 mil | 17 mil | 84 mil | 4,2 | 2,2–9,5 |
| Frente 19–28 | 14 (9–22) | 0,68 M | 72 mil | 11 mil | 60 mil | 2,6 | 1,4–6,7 |
| technical_audit | 30 (20–45) | 3,11 M | 182 mil | 21 mil | 152 mil | 9,5 | 4,1–31,6 |
| Reparo (agora improvável) | 8 | 0,26 M | 43 mil | 5 mil | 36 mil | 1,3 | 0,7–2,8 |
| **Total sem reparo** | **84** | **6,4 M** | **0,46 M (93% em cache)** | **cerca de 70 mil** | | **cerca de 21** | **10–58** |

Contra a 2ª rodada (35,4 M, 213 requisições e 97%), o fluxo novo tem cerca de 18% da entrada e 40% das chamadas. O ganho vem de 4 sessões curtas (contexto máximo de 60–150 mil, contra 344 mil), do fim do ciclo de correção e de nenhum reparo por formalidade.

**Por rota.** No **Claude** (Opus 5.5, médio e alto), a entrada em tokens fica na mesma ordem, cerca de 6–7 M, quase toda em cache. A janela de 5 h do Claude não tem calibração própria, e a guarda usa amostras antes e depois de cada chamada. No **Gemini** (flash medium e high), o pedido é cerca de 13% maior e cada imagem custa cerca de 1,1–1,5 mil tokens. A cota é por requisição e por dia, não por janela de 5 h, por isso não há percentual comparável.

**Margem de erro.** O central (cerca de 21 pontos) tem incerteza de cerca de ±50%. O que mais pesa:
- o número de chamadas da auditoria, porque o pedido manda abrir `manifesto.py ver` achado por achado;
- o crescimento real por chamada, que foi de 3,4 a 9,4 mil na 2ª rodada;
- a contagem sem tiktoken (±10%);
- o ajuste da cota com só 5 sessões (±3 pontos por sessão).

Mesmo no lado alto do provável (cerca de 35), o resultado cabe na meta de 50%. Só a soma de todos os piores casos passa de 50, e aí a guarda para com 45.

## Uso do PC (item e)

| Medida | Preparo + processamento local (Codex, 7 peças) | Gemini |
|---|---|---|
| Duração | 33,1 s de preparo e 2,5 s de processamento local | 46,1 s de preparo |
| CPU (20 núcleos lógicos) | pico 58%, média 24% | pico 82%, média 30% |
| RAM somada dos processos | pico de 2,3 GB | 2,3 GB |
| RAM livre mínima | 30–31 GB de 64 GB | 32 GB |
| Processos pesados simultâneos | **2** (geometria_pdf e docling), mais o orquestrador parado | 2 |
| OCR | um por vez, 3–13 s cada | idem |
| Edge × docling | nunca ao mesmo tempo | idem |

Passos do preparo: docling 27,7 s somados nas 2 tarefas, parede da fase pesada 17,0 s, páginas PNG 9,0 s, `imagem_ver` 3,6 s e geometria 1,9 s. No disco, o cache do 19486 ocupa cerca de 40–60 MB de PNG e JPG por análise, dentro de `.pmvni-ai/cache-pdf`, e é reaproveitado entre as rotas.

## Bugs invisíveis (só com IA real, dados reais ou outro PC)

1. **Mesma versão 4.0.0 entre a instalação e o mestre** (item 6b). Depois de publicar, até a tarefa de atualização rodar, o preparo dá CHECK_FAILED. Se houver uma análise em andamento, o `atualizar` sai sem instalar. Sugestão: subir a versão do plugin a cada publicação, ou fazer o Editor mostrar "kit publicado, aguardando instalação" em vez da falha.
2. **Arquivos do OneDrive só na nuvem.** Um HEIC do Camera Roll deu "O provedor de arquivos de nuvem fechou inesperadamente" ao ser lido do contexto do pacote. Uma peça de PROCESSOS que esteja só na nuvem pode falhar no hash ou no preparo se o executor rodar dentro do pacote. O Editor roda fora, mas convém marcar a pasta do processo como "Manter sempre neste dispositivo" ou hidratar antes do preparo.
3. **OCR real.** As trocas O/I por 0/1 foram corrigidas só na inscrição. Outros números lidos por OCR, como área e matrícula, seguem sem essa tolerância, e a IA precisa conferir.
4. **Página em branco marcada `indisponivel`.** Isso não bloqueia: o pedido manda a IA registrar o `adobe_acrobat`. Mas se a IA real ignorar a instrução, ainda pode haver reparo.
5. **Sem conversor de HEIC em outro PC.** O WIC precisa da "Extensões de Imagem HEIF" do Windows. Sem ela, a peça fica com `imagem_indisponivel`, registrada, mas sem imagem.
6. **A guarda do codex-lb exige telemetria de no máximo 180 s.** O codex-lb grava a cada 12 s, conferido no `store.db` real só para leitura. Se ele parar, a análise é interrompida, o que é o comportamento desejado.
7. **Prazo de 20 min por sessão Codex** (`rodar_codex`). A auditoria no lado alto da faixa (45 chamadas de 25–30 s) chega perto desse limite.
8. **A rota Gemini abre o PDF no Acrobat ao concluir** (`google_auto.py:542`). Os testes e harnesses devem definir `PMVNI_SEM_AVISO=1`. Este teste abriu um PDF fictício, e a pasta `gm2` ficou presa até o Acrobat fechar.
9. **Editar o kit com uma análise em curso** dispara "kit atualizado, revalidar?". Num caso, o processamento local leu um arquivo no meio da edição e seguiu sem os dados prontos, como previsto.
10. **Edge.** A montagem depende do Edge headless, que falhou numa sessão isolada na revisão anterior.
11. **AGENTS.md global** (`~/.codex/AGENTS.md`, 2,7 mil caracteres). Talvez entre no contexto mesmo com `--ignore-user-config`. Não foi verificado, porque o simulador não mostra isso.

## Otimizações ainda possíveis (item d)

| # | Otimização | Ganho estimado |
|---|---|---|
| d1 | Auditoria técnica: em vez de mandar abrir `manifesto.py ver` em cada um dos 27 achados, o pedido trazer o resumo de cada achado (cerca de 300 caracteres por achado, cerca de 2,5 mil tokens) e só os contestados serem abertos | menos 10–15 chamadas, cerca de 3–5 pontos e 4–6 min |
| d2 | Checklist das frentes (9–11 mil caracteres, 60–75% do pedido): mandar só os critérios aplicáveis ao enquadramento do caso | cerca de 1,5 mil tokens por chamada, cerca de 1–2 pontos no total |
| d3 | PDFs muito longos (memorial de 121 páginas): no pedido e nas folhas de contato, listar só as páginas com quadro, planta ou carimbo, e o resto ficar por busca de texto | evita que a IA abra páginas inúteis; varia por caso |
| d4 | Ler altura ("ALTURA DA EDIFICAÇÃO 9,39 m") e gabarito ("2 PISOS") do quadro pelo programa | tira 2 lacunas da frente 1, cerca de 2 chamadas |
| d5 | TP com leituras divergentes: dizer "leituras divergentes entre revisões" em vez de "falta área permeável" | evita releitura, cerca de 1 chamada |
| d6 | Editor: avisar que "Retomar análise" com objeção pendente refaz a technical_audit e gasta cota | evita cerca de 8–10 pontos por clique sem efeito |
| d7 | Subir a versão do plugin a cada publicação (bug invisível 1) | elimina a janela de CHECK_FAILED |

## Testes

- Kit: `python tests/rodar_testes.py` terminou com código 0 e `TESTES_OK`. Os 2 self-tests que dependem de pypdfium2 foram aceitos.
- Editor: não houve mudança de código. O build Debug x64 terminou com 0 erros e 0 avisos. Pmvni.Agentes.Testes passou com 307/307 e Pmvni.Revisao.Testes com 1101/1103 (2 ignorados). Os dois rodaram com o teste temporário, que depois foi removido.

## Não verificado

- O tempo e a qualidade com a IA real: as durações das etapas com IA e todos os pontos são estimativas calibradas.
- A janela de 5 h do Claude e do Gemini, porque não há telemetria equivalente.
- O PDF real em outra estação. Aqui o Edge funcionou.
