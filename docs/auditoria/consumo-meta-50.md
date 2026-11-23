# Consumo da análise e orçamento de 50 pontos

## Diagnóstico do piloto de 23 de novembro de 2026

A conta variou **70 pontos percentuais (p.p.)** da janela de cinco horas antes de terminar a auditoria. As três frentes ocuparam o mesmo intervalo de 43 p.p. A auditoria seguinte consumiu intervalos de 18 e 9 p.p., com o segundo bloco interrompido. A montagem das 28 seções passou na primeira tentativa. O gasto observado concentrou-se na análise e nas novas sessões de auditoria. A conta também atendeu tráfego concorrente. Os pontos indicam a variação global durante cada intervalo e os tokens permitem identificar o trabalho das sessões.

A consulta reproduzida em modo de leitura no banco do balanceador confirmou os totais do relatório anterior. Entre parênteses está o número de chamadas ao modelo com entrada medida. A última sessão teve ainda uma chamada sem medição de entrada.

| Etapa | Entrada total | Entrada nova¹ | Saída | Imagens recebidas | Megapixels² |
|---|---:|---:|---:|---:|---:|
| Análise 1–9 (39) | 5.858.402 | 225.122 | 31.417 | 69 | 81,0 |
| Análise 10–18 (41) | 5.931.656 | 251.528 | 34.016 | 63 | 95,1 |
| Análise 19–28 (87) | 10.637.832 | 200.072 | 31.766 | 39 | 61,3 |
| Auditoria 1–9 (94) | 16.471.562 | 310.410 | 40.944 | 50 | 67,9 |
| Auditoria 10–18 parcial (24) | 2.744.786 | 189.010 | 12.975 | 42 | 66,4 |
| **Total parcial (285)** | **41.644.238** | **1.176.142** | **151.118** | **263** | **371,7** |

¹ Entrada total menos entrada servida pelo cache. ² Soma das dimensões das imagens incorporadas nos retornos das ferramentas. As dimensões medem o material visual apresentado ao modelo.

## O que as ferramentas repetiram

**Imagens.** Dos 263 envios, 189 tinham conteúdo codificado distinto, pelo SHA-256 do corpo base64. Das 50 imagens da primeira auditoria, **34 já haviam aparecido nas três frentes**. Isso corrige a hipótese de que todas eram inéditas. Ainda assim a auditoria precisa inspecionar a fonte em sua própria sessão. O primeiro corte a testar é reutilizar a localização do recorte para poupar buscas por quadrantes e aberturas repetidas dentro da mesma sessão. Comparar peça, página, caixa e escala detectaria também recortes visualmente próximos com bytes diferentes. Em três ciclos com um único retorno de uma imagem e menos de 240 caracteres de texto, a entrada nova registrada pelo modelo foi de 420, 485 e 3.837 tokens. Há variação no contexto e no cache em cada chamada, o que impede usar esses valores como tarifa por imagem.

**Normas.** As três frentes devolveram aproximadamente 183 mil, 181 mil e 12 mil caracteres respectivamente em *lotes de ferramentas que mencionavam* a busca de normas. Nas auditorias foram cerca de 6 mil e 104 mil. Esses lotes também continham contratos, comandos de orientação e leitura de peças. Para obter uma medida menor e mais específica, foram isolados somente objetos JSON completos no formato de resposta do buscador, inclusive quando aninhados no retorno do executor. Nas cinco sessões foram recuperados 101 objetos, incluindo 88 respostas por artigo, dez por termo e três por anexo. A serialização uniforme somou 124.938 caracteres. Havia 90 respostas distintas e 14.433 caracteres de respostas idênticas reapresentadas. O levantamento exclui JSON truncado e texto já filtrado pela sessão. O buscador já delimita um artigo pelo cabeçalho seguinte de número diferente. A busca por termo pode devolver até 12 trechos e a busca por anexo chega a 15 mil caracteres. Priorizar consultas pelo artigo conhecido e, quando a localização depender de busca por termo, conferir o dispositivo completo e a página oficial. A economia em p.p. exige medição própria.


**Validação.** Entre os retornos que citavam o validador estavam contratos de saída e leituras combinadas. Duas respostas efetivas de reprovação mostraram 14 e oito erros. Reaplicar a apresentação agrupada às linhas individuais reduziu 22 linhas para três e 1.804 caracteres para 560, preservando os identificadores afetados e cada diagnóstico. A opção `--integral` recupera as linhas originais. O agrupamento muda a apresentação. A validação continua exigindo a correção de todos os itens e retorna falha enquanto houver erros.

**Saída do modelo.** As três frentes geraram 97.199 tokens de saída. As respostas das ferramentas são entrada nas chamadas seguintes. Uma economia de caracteres nas respostas tem efeito diferente de poupar a redigitação dos achados. No contrato das figuras, o recorte e a anotação já guardam caminho, hash, origem e caixa. O montador consegue expandir esses vínculos a partir do metadado verificado. As decisões de candidatos com conclusão e localização iguais também admitem expansão por identificador. O ganho do formato curto deve ser medido nos fragmentos aceitos e comparado campo a campo, preservando divergências, juízo técnico, proveniência e qualidade do recorte.

## Cenários e critério de aceitação

O orçamento a testar é **30 p.p. para a análise, 15 para a auditoria e cinco para finalização e contingência**, somando 50 p.p. Os números são limites de planejamento. Os 15 p.p. da auditoria representam uma redução de cinco pontos sobre os 20 observados em outra auditoria integral. A aplicação dessa referência a todas as linhas mantém visível o efeito da quantidade de frentes.

| Frentes de análise | Referência disponível | Distância até o orçamento | Ensaio necessário |
|---|---|---|---|
| Uma, seções 1–28 | Uma rodada anterior usou 35 p.p. em cerca de 33 min. Com a referência de auditoria, o subtotal seria 55. | Cortar 5 p.p. na análise e 5 na auditoria para reservar 5 para o fechamento. | Medir o efeito de uma sessão longa, reaproveitamento de contexto e completude de todas as seções. |
| Duas | Sem medição comparável. Separar plantas, cortes e vãos da frente de documentação e implantação é uma hipótese de menor sobreposição. | A soma das duas frentes precisa caber nos mesmos 30 p.p. Uma média aritmética entre uma e três frentes encobriria a duplicação de pranchas. | Fixar a responsabilidade de cada seção e candidato. Comparar busca de normas, imagens comuns e reparos com a frente integral. |
| Três, em paralelo | A análise variou de 39 a 43 p.p. em duas rodadas. Com a referência de auditoria, os subtotais seriam 59 a 63. Neste piloto a análise durou cerca de 16 min. | Cortar 9 a 13 p.p. na análise e 5 na auditoria para preservar a reserva. | Medir o intervalo global das frentes e a entrada nova de cada sessão. Uma janela compartilhada impede somar deltas individuais de cota. |

Usar o mesmo preparo e as mesmas versões de normas, modelo, esforço e imagens. Começar por uma frente e uma única auditoria. Só vale testar o cenário seguinte se houver motivo de prazo, qualidade ou contexto. Em cada ensaio futuro, registrar saldo inicial e final, renovação da janela, chamadas sem contagem, novas tentativas, etapas concluídas e os custos até o encerramento. Separar o contexto servido pelo cache e registrar peça, página, caixa e escala de cada abertura. Antes de aceitar o resultado, comparar a cobertura das 28 seções, as decisões de candidatos e a adequação das contraprovas no mesmo conjunto de evidências.

O piloto demonstrou pressão de cota e releitura na auditoria. A auditoria offline não demonstrou uma análise completa por 50 p.p. A confirmação exige rodada exclusiva até o fim e repetição em outro processo. O erro de validação cruzada de até 9,7 p.p. no ajuste histórico é maior que a reserva de cinco pontos, tornando esse ajuste insuficiente para certificar o orçamento.

[Dados agregados do piloto e metodologia histórica](../testes-de-analise/2026-11-23-piloto-3/RESULTADO-consumo.md)
