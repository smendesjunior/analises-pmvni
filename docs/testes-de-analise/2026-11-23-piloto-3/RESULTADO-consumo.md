# Consumo da janela de 5 horas na análise

## Auditoria complementar da meta de 40 a 50 pontos

A última rodada completa rastreada, na noite de 22 de novembro, chegou a `gate` com a janela de 5 h em **67 p.p.** O histórico do balanceador confirma as leituras de 0% antes do início e 67% ao fim. Na manhã de 23 de novembro, o piloto chegou a **70 p.p. com a auditoria incompleta**. Montou as 28 seções e registrou 103 decisões de candidatos. A auditoria registrou contraprova em 9 das 28 seções e em 24 dos 64 achados. A rota de produção do relatório não chegou ao fechamento nesse piloto.

### Chamada, sessão e cota

A tabela cruza os IDs registrados em `engine_state.json` e `orcamento_lb.json` com `request_logs`, em leitura, por `session_id` ou `conversation_id`. O sufixo identifica cada sessão no controle local. A conta e a janela são as mesmas nas cinco sessões que receberam tokens. Os eventos do CLI foram conferidos por diferenças sucessivas dos acumulados de cada sessão, partindo de zero. Cache já integra a entrada. Os números da tabela vêm do balanceador.

| Etapa e tentativa | Sufixo de sessão | Esforço | Chamadas com tokens | Entrada | Cache | Entrada nova | Saída | Janela de 5 h |
|---|---|---|---:|---:|---:|---:|---:|---|
| Análise, 1ª tentativa, três frentes | `82fdef9d`, `9deafe70`, `3472eae3` | Médio | 0 | — | — | — | — | 39 recusas `no_accounts`, 13 por sessão, sem conta vinculada |
| Análise, retomada, seções 1–9 | `5f25118c` | Médio | 39 | 5.858.402 | 5.633.280 | 225.122 | 31.417 | 43 p.p. no intervalo conjunto das três frentes |
| Análise, retomada, seções 10–18 | `fa508294` | Médio | 41 | 5.931.656 | 5.680.128 | 251.528 | 34.016 | Mesmo intervalo |
| Análise, retomada, seções 19–28 | `bb5d3f83` | Médio | 87 | 10.637.832 | 10.437.760 | 200.072 | 31.766 | Mesmo intervalo |
| Auditoria, bloco 1–9 concluído | `cd39ce7c` | Alto | 94 | 16.471.562 | 16.161.152 | 310.410 | 40.944 | 18 p.p. observados no intervalo |
| Auditoria, bloco 10–18 interrompido | `cc78ea24` | Alto | 24 | 2.744.786 | 2.555.776 | 189.010 | 12.975 | 9 p.p. observados no intervalo |
| **Total do piloto parcial** | **5 sessões com medição** | | **285** | **41.644.238** | **40.468.096** | **1.176.142** | **151.118** | **70 p.p. de variação da conta** |

O modelo das cinco sessões foi `gpt-6.2-sol`. O preparo local foi reaproveitado na retomada. As três frentes fecharam na primeira tentativa que obteve conta. O validador registrou zero sessões de reparo. O segundo bloco da auditoria teve ainda uma chamada cancelada, sem contagem de tokens. O terceiro bloco não iniciou. O maior consumo observado foi o intervalo da análise, com 43 p.p. A repetição mais cara identificada foi abrir nova sessão para cada bloco de auditoria. O primeiro bloco isolado já ocupou 18 p.p., perto dos 20 p.p. de uma auditoria integral anterior. Os conjuntos tinham quantidades diferentes de achados.

**Atribuição da janela.** O recibo gravou 0 p.p. atribuídos exclusivamente e 70 sem atribuição individual. Na mesma conta e no intervalo do piloto, as outras sessões registraram **272.792 tokens de entrada**, sendo 48.664 novos, e 654 de saída. A última requisição externa registrada ocorreu às 09h17. Das 09h18min08s às 09h52min08s, a leitura passou de **23% a 70%** e o balanceador registrou apenas tráfego do piloto nessa conta. São **47 p.p. de variação em um período com uso exclusivo observado pelo balanceador**. O histórico não discrimina a cobrança de cada chamada nem o atraso da telemetria. O restante da janela inclui tráfego compartilhado. Os intervalos paralelos das frentes também impedem repartir seus 43 pontos com precisão.

### Defeitos conferidos no fluxo

- **Monitor.** O kit grava `sessao_codex.technical_audit.blocos`. O leitor do Editor consultava apenas `blocos_monitor` e o ID principal. Aplicado ao piloto, mostrava 2.744.784 tokens de entrada e 12.975 de saída para a auditoria. Com a correção em `ConsumoSessaoCodex.LerEtapa`, o mesmo leitor devolveu **19.216.344 de entrada, 18.716.928 de cache e 53.919 de saída**. As sessões são somadas uma vez mesmo quando o último ID também aparece como principal. Os quatro tokens adicionais do balanceador vieram de duas chamadas de pré-aquecimento. Na análise, a diferença equivalente foi de seis tokens, dois por sessão.
- **Referência no texto de ausência de figura.** O código interno observado correspondia ao mapa QGIS. O montador agora usa o rótulo público “mapa de localização” e a numeração publicada para figuras de projeto. Na remontagem local das evidências, apenas o quinto texto de ausência de figura mudou. Foram mantidos os 64 itens, as 58 figuras numeradas e o mapa. Os avisos de publicação passaram de um para zero. O validador de dados publicados passou antes e depois. Os hashes de perfil das três rotas foram atualizados para preservar a retomada de etapas certificadas.
- **Recusa do balanceador.** As 39 recusas ocorreram antes de o piloto receber uma conta. O código posterior ao piloto já reconhece esse diagnóstico e faz duas novas tentativas, com esperas de 5 e 15 segundos. A correção de mensagem e espera está coberta por simulação. A razão pela qual o balanceador recusou a conta naquela manhã continua sem confirmação.
- **Rigor técnico.** A conclusão da montagem e os testes do montador verificam integridade dos dados. O piloto deixou 19 seções e 40 achados sem contraprova registrada. Ele não certifica o mérito integral do projeto.

### Economia medida sem abrir sessão de IA

Foi usado o tokenizador `o200k_base` sobre JSON minificado, em UTF-8, dos arquivos reais do piloto. Para as 57 figuras com recibos de recorte e anotação, foram removidos somente os campos que o montador consegue reconstituir. Foram preservadas oito listas de anotações revisadas. A figura restante manteve a representação integral. Cada arquivo foi reexpandido com o código do kit e comparado campo a campo.

| Material do piloto | Formato anterior | Formato compacto | Redução no texto serializado |
|---|---:|---:|---:|
| 28 fragmentos de seção | 105.009 tokens | 74.799 | 30.210 |
| 103 decisões, incluindo 93 confirmações simples | 3.698 | 2.860 | 838 |
| **Total** | **108.707** | **77.659** | **31.048 tokens (28,6%)** |

A comparação mede a representação dos arquivos. Uma sessão pode produzi-los por meio de scripts e editar um mesmo campo várias vezes. A redução só poderá ser convertida em economia de cota a partir dos contadores de uma execução. O ajuste aos sete grupos, refeito com os tokens de `request_logs`, foi de 14,89 vezes a entrada nova em milhões mais 0,320 vezes a saída em milhares. Ao retirar um grupo por vez, o maior erro de previsão foi de 9,7 p.p.

**Critério de aceitação.** O histórico disponível de uma sessão integral de análise (35 p.p.) mais uma auditoria integral (20 p.p.) dá uma referência de **55 p.p.**, combinando duas execuções. A meta exige comprovar ao menos cinco pontos adicionais de economia frente a essa referência. No próximo ensaio autorizado, usar cópia do mesmo preparo, mesma janela de 5 h em uma conta sem tráfego concorrente, o mesmo modelo, esforço médio na análise, alto na auditoria e as mesmas peças e resolução. Conferir as 28 seções, todos os candidatos e achados e o relatório até o `gate`. Registrar a diferença da cota desde o início, inclusive retomadas, reparos e fechamento. Uma pausa aos 50 pontos ou uma entrega para edição manual caracteriza ensaio incompleto. Uma segunda execução em processo comparável permite avaliar a repetibilidade do padrão.

## Medição de 23 de novembro de 2026

A janela avançou 43 pontos percentuais durante as três frentes da análise, 18 durante o primeiro bloco da auditoria e 9 durante o segundo, interrompido. O preparo ficou preservado e a montagem das 28 seções passou na primeira tentativa, sem sessão de reparo. O principal gasto adicional deste piloto apareceu quando a auditoria abriu sessões independentes para reler as peças. Houve tarefas concorrentes na conta. O balanceador mede a variação da janela por conta e os tokens por sessão. Os pontos na tabela são as diferenças de janela observadas nos intervalos, com atribuição aproximada.

A tabela usa uma consulta somente leitura a `request_logs`, agrupada por sessão. Entrada nova é a entrada menos a parcela servida pelo cache. As duas auditorias do último piloto se sucederam e as três frentes da análise rodaram em paralelo.

| Execução e etapa | Chamadas com entrada medida | Entrada total | Entrada nova | Saída | Janela de 5 h |
|---|---:|---:|---:|---:|---:|
| Piloto, análise 1–9 | 39 | 5.858.402 | 225.122 | 31.417 | compartilhada entre frentes |
| Piloto, análise 10–18 | 41 | 5.931.656 | 251.528 | 34.016 | compartilhada entre frentes |
| Piloto, análise 19–28 | 87 | 10.637.832 | 200.072 | 31.766 | compartilhada entre frentes |
| **Piloto, soma das frentes** | **167** | **22.427.890** | **676.722** | **97.199** | **43 p.p.** |
| Piloto, auditoria 1–9 | 94 | 16.471.562 | 310.410 | 40.944 | 18 p.p. |
| Piloto, auditoria 10–18, parcial | 24 | 2.744.786 | 189.010 | 12.975 | 9 p.p. |
| Rodada anterior, três frentes | 150 | 19.919.033 | 680.633 | 104.280 | 39 p.p. |
| Rodada anterior, reparo | 21 | 1.043.796 | 101.716 | 7.859 | 4 p.p. |
| Rodada anterior, auditoria integral | 70 | 13.589.827 | 304.579 | 40.683 | 20 p.p. |
| Rodada anterior, análise integral em uma sessão | 64 | 14.607.825 | 360.913 | 86.449 | 35 p.p. |

A auditoria interrompida teve uma chamada adicional sem contagem de entrada. O limite de 70 p.p. encerrou a rodada antes da conclusão. A extrapolação de 97 p.p. pressupõe que os outros blocos custariam tanto quanto o primeiro. A única medição integral da auditoria na tabela é de 20 p.p. e veio de uma rodada com outra quantidade de achados.

## Intervenção no motor do kit

- A rota Codex abre uma sessão de auditoria técnica para todas as seções. Mantém o esforço alto configurado, a inspeção das figuras e das peças originais, a verificação de omissões, as contraprovas individuais, os registros em lote e a validação final. A retomada pode consultar os registros já gravados.
- O controlador gera um dossiê por seção com o observado, requisito, base legal declarada, correção e localização de cada achado, além das referências dos recortes e das lacunas de evidência. A sessão deve abrir as fontes e confrontar cada item. As citações fornecidas no dossiê ainda precisam de verificação na fonte normativa. Seções sem achados também entram para a conferência de cobertura.
- Em cópia temporária dos insumos do piloto, os três pedidos antigos juntos tinham **50.368 bytes UTF-8**. O pedido único teve **6.334 bytes**. Os 28 arquivos de dossiê continham **64 de 64 IDs** e **69 vínculos com figuras**. Somaram **108.460 bytes** frente a **145.852 bytes** dos objetos completos dos achados. Os arquivos são lidos conforme a auditoria avança. Bytes de texto medem o volume do material e não permitem converter esta diferença em cota.
- O reconhecimento da indisponibilidade de contas passou a considerar também o diagnóstico textual do CLI, inclusive quando seguido de avisos. Nesse caso há até duas novas tentativas com espera e uma orientação para ativar uma conta e retomar. Esse caminho foi exercitado com falha simulada.
- Permanecem as três frentes de análise, o mesmo modelo, o limite de 70 pontos e os parâmetros de qualidade da imagem. A mudança concentra o ganho na auditoria e permite medir seu efeito antes de mexer na elaboração dos achados.

## O que a amostra permite prever

Refiz o ajuste de mínimos quadrados, sem intercepto, nos sete grupos acima com pontos observados. Ele resultou em **14,89 × entrada nova em milhões + 0,320 × saída em milhares**. No conjunto usado para ajustar, o maior erro absoluto foi de 4,6 p.p. Ao retirar um grupo por vez e prever o grupo retirado, o erro chegou a 9,7 p.p. A saída e a entrada nova variam juntas e a amostra inclui dois blocos de auditoria, um deles interrompido. A fórmula muda bastante em relação à sugerida no escopo e serve somente para priorizar medições.

Como referência, **43 + 20 = 63 p.p.**, ainda **8 p.p. acima** do teto desejado. Isso combina etapas de rodadas diferentes e ainda não mede a versão com dossiê. Com uma sessão de análise e uma de auditoria, **35 + 20 = 55 p.p.** também combina rodadas diferentes. A meta de 40–55 p.p. continua dependente de um piloto controlado.

## Proposta de verificação e próximos cortes

1. Para isolar o dossiê, partir de duas cópias do mesmo manifesto e evidências, com os mesmos 64 achados e sem registros finais de auditoria. Comparar auditoria única com o pedido anterior e auditoria única com dossiê, em janelas renovadas, sem tráfego paralelo na conta. Preservar modelo e esforço. Medir os pontos da conta, entrada nova, cache, saída, duração, reparos, achados corrigidos e conferência das 28 seções. Separar os tokens gastos na leitura dos arquivos dos tokens usados para abrir imagens.
2. Para comparar **uma frente integral contra três paralelas**, usar cópias do mesmo preparo, mesma versão de normas e modelos, sem recibos de frentes concluídas. Manter iguais os contratos e a montagem final. Medir o bloco de análise inteiro por janela para evitar repartir pontos simultâneos entre sessões. Exigir validação de todas as seções, decisões de candidatos, confirmação visual da amostra de cotas e revisão das diferenças de mérito pelo analista. A alternativa de duas frentes só precisa ser ensaiada se uma sessão integral perder qualidade, atingir o limite de contexto ou se o tempo de resposta justificar o consumo extra. Uma sessão completa da rodada mais antiga levou cerca de 33 minutos contra 16 das três frentes neste piloto.
3. Antes de um novo piloto, medir offline o custo de saída dos fragmentos com e sem caminhos e hashes deriváveis. Reconstituir os campos a partir dos arquivos e exigir equivalência dos vínculos e dos hashes antes de adotar uma representação abreviada. Avaliar também o retorno apenas do dispositivo legal consultado e o agrupamento das falhas mecânicas do validador, com acesso ao diagnóstico integral.
4. Para imagens, registrar peça, página, caixa e escala por sessão e medir sobreposição de regiões, taxa de reutilização e entradas novas nas chamadas que abrem só uma imagem. O piloto ainda não permite atribuir custo a uma dimensão específica. A estratégia de corte recomendada é eliminar redescoberta do mesmo recorte, preservando resolução e acesso a todas as pranchas.
5. Validar a economia final em uma análise completa com janela exclusiva. Registrar percentual inicial, final e horário de renovação, comparar com o caso de referência e repetir em outro processo. Só depois definir novo limite com margem observada para um reparo. Em contas compartilhadas, mostrar a variação global junto aos tokens identificados por sessão e deixar a atribuição em pontos como indeterminada.

Os testes desta revisão usaram chamadas simuladas. Atingir a faixa de 40–55 p.p., ativar o dossiê nas estações e atribuir cota precisamente em janelas compartilhadas são verificações independentes.


## Verificação e distribuição

Passaram os 385 testes dos scripts do motor e os 83 testes da rota Codex, incluindo sequência completa com provedor fictício, interrupção por cota, retomada e reparo. O commit institucional `77d8b06` foi promovido ao kit mestre local sob a trava de execução do instalador. Os seis arquivos da mudança foram comparados antes da cópia e seus hashes SHA-256 foram conferidos após a promoção também no inventário. A reinstalação do plugin e a distribuição às demais estações dependem da atualização do kit.
