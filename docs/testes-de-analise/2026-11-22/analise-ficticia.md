# Análise fictícia de ponta a ponta (22/11/2026)

O executor real das rotas Codex e Claude foi conduzido de `prepare` até o `gate`. O teste usou uma cópia temporária do 19486, com as 14 peças PDF reais. Só o binário da IA foi trocado por um simulador, por isso não houve consumo de cota. O simulador gravou os fragmentos das três frentes a partir da análise salva e as decisões sobre os candidatos do preparo atual. Ele também gravou a auditoria técnica com uma objeção corrigida no próprio passo e simulou o uso do codex-lb (banco `store.db` fictício) e a janela de 5 h do Claude. O kit veio do repositório de desenvolvimento por meio de uma raiz de kit temporária com junções. A pasta PROCESSOS e a instalação do kit não foram tocadas.

## Resultado por etapa

| Etapa | Codex (s) | Claude (s) | Executor, modelo e esforço |
|---|---:|---:|---|
| prepare | 66,6 | 67,9 | programa |
| analyze | 16,6 | 17,0 | 3 frentes em paralelo. Codex: gpt-6.2-sol/medium via codex-lb. Claude: claude-opus-5-5/medium. Montagem e ensaio incluídos. |
| technical_audit | 5,0 | 3,0 | sol/high e opus/high. No Codex, o reparo pontual do validador foi forçado e rodou em sol/medium. |
| audit | 0,5 | 0,1 | programa |
| produce | 11,6 | 11,5 | programa, PDF de 21 páginas (Edge funcionou) |
| final_review | 2,8 | 2,5 | programa |
| gate | 2,2 | ok | ALL_PASSED, ENTREGUE |

O preparo levou cerca de 66 s com tarefas em paralelo. Os maiores tempos foram docling (39,6 s), fyp (29,9 s) e páginas PNG (10,8 s). O OCR do Windows leu 5 páginas de escritura. O consumo simulado observado foi de 24 pontos no Codex (3 frentes × 4 + auditoria 6 + reparo 6) e 16 no Claude.

## Conferências

- **Estado.** Todas as etapas terminaram em EXECUTED. Não houve `ciclos_correcao`, `objecoes_pendentes` nem volta da auditoria para analyze. Houve só 3 chamadas de analyze em toda a execução.
- **Frentes.** As frentes gravaram em `runs/secoes-<chave>/frente-01|10|19`, cada uma com `pedido.txt`, `resultado.json` e `concluida.json`. A pasta guarda as 28 seções, a capa, a parte comum, o territorial e os candidatos. Os pedidos tinham 15,8, 14,0 e 13,1 mil caracteres. O da auditoria tinha 11,2 mil.
- **Ensaio de montagem.** Rodou em `validate_analysis --fase analise` e removeu a pasta temporária.
- **Correção na auditoria.** A providência corrigida chegou ao texto publicado e ao PDF.
- **Assinatura e T-P01.** A assinatura foi preenchida. Os códigos T-P01 a T-P08 foram preservados no PDF, que não ficou com marcas `[[`.
- **Editor.** Um teste xUnit temporário de `DetalhesDasEtapas` e `ProgressoLocal`, apontado para as duas pastas, passou. A análise mostrou "esforço médio" e a auditoria técnica mostrou "esforço alto". As etapas locais apareceram como "Programa · sem consumo de IA". As 3 frentes ficaram concluídas (9/9, 9/9, 10/10), sem objeções, com 6/6 etapas, 28/28 seções e 27 achados. O teste foi removido depois de passar.

## Bugs reais corrigidos (kit)

1. `scripts/preparar_caso.py`, linhas 194–197. O OCR de uma página sobrescrevia a variável `motor` do leitor de PDF. Na página seguinte de uma escritura digitalizada, o código usava o ramo do PyMuPDF com uma página do pdfium e o preparo falhava (`'PdfPage' object has no attribute 'rect'`). Teste: `test_preparar_caso.test_ocr_de_uma_pagina_nao_troca_o_leitor_das_seguintes`.
2. `scripts/etapas_programa.py`, linhas 321–331, no `prepare` (plugin_workflow). O preparo gravava só 3 dos 10 gatilhos, e o ensaio reprovava em `PLUGIN_WORKFLOW_FAILED` porque o pedido da frente 1 não pede os `inputs`. Agora o preparo grava todos, com OCR e docling vindos das páginas. Teste: `test_controladores_programa.test_prepare_sem_sessao`.
3. `scripts/montar_manifesto.py`, linhas 31–34, e `scripts/report_evidence.py`, linhas 490–493. O código do achado padrão DOC-01 coincidia com o ID da peça DOC-01. O PDF trazia "item 01 a DOC-03" no lugar de "DOC-01 a DOC-03", e o validador recusava a peça citada como "código interno". Agora os IDs de peça ficam como peça nos dois lugares. Teste: `test_fluxo_linear.test_codigo_de_achado_igual_a_peca_fica_como_peca_no_texto`.

## Mudança de modelos (pedido do usuário)

`references/modelos-por-rota.json` passou a usar gpt-6.2-sol na rota Codex e claude-opus-5-5 na rota Claude. A análise roda em médio e a auditoria técnica em alto. O novo `reparo.nivel` = médio vale nas rotas Codex e Claude (`modelos_por_rota.reparo()`). No Gemini, a auditoria sobe para alto em flash e hydrogen. O argon só aceita médio e continua em médio, e o reparo do Gemini segue no nível da etapa. Também foram atualizados SKILL.md, niveis-raciocinio.md, README, AGENTS.md e os comentários do Editor.

## Não verificado

- O Gemini não foi rodado de ponta a ponta. Ele foi coberto só pelos testes do kit.
- O teste não mediu consumo, tempo de sessão ou qualidade de IA real.
- O conteúdo técnico das frentes veio da análise salva, sem leitura nova. O orçamento e a telemetria do codex-lb foram simulados.
- O `self_test.py` só falha quando o ambiente força `PYTHONIOENCODING=utf-8`, porque decodifica o stderr do validador em cp1252. No ambiente padrão ele passa.

## Testes

- Kit: `python tests/rodar_testes.py` terminou com `TESTES_OK`. Os dois self-tests que dependem de pypdfium2 foram aceitos.
- Editor: o build Debug x64 terminou sem erros. Pmvni.Agentes.Testes passou com 307 de 307 e Pmvni.Revisao.Testes com 1101 de 1103 (2 ignorados).
