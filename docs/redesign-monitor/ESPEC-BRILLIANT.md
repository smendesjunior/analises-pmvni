# Monitor de análise — especificação do design final (Brilliant, 8ª atualização)

Fonte: https://brilliant.design/playground?seed=cg-jvlkgze4mipmr35nbbz3 — quadro "Janela Monitor de análise", 1680 × 960, 206 elementos.
Medidas em px a 100 %. **Cores do design são só referência**: na implementação use os recursos de tema do Editor/Fluent
(`Estilos.xaml`, `App.xaml`, `ThemeResource`), com o verde institucional #1E7A46 como destaque. Botões, títulos e textos seguem os
estilos já existentes do Editor (ver `PadraoInterfaceTestes`): botões de 32 px, raio nativo, sem negrito, hover nativo.

## Estrutura (de cima para baixo)

Fundo da página; margens laterais 30, topo 19. Espaço vertical entre blocos ≈ 10.

### 1. Cabeçalho (altura 68)
- Esquerda: ladrilho 44×44 (raio 10, fundo verde tonal) com o ícone da IA; título "Monitor de análise" (24, semibold);
  subtítulo "Acompanhamento de execução, evidências e auditoria técnica em tempo real" (12,5, secundário).
- Direita, na mesma linha: bloco **PROCESSO** (rótulo 10,5 semibold maiúsculo, alinhado à direita; valor 14 semibold, ex. "19486/2026");
  bloco **REQUERENTE** (largura ~220, rótulo igual; valor 14 semibold numa linha só, reticências + dica com o nome completo; sem requerente: "Não informado");
  separador vertical 1×28; botões **Abrir registro** (ícone pasta), **Nova análise** (ícone +, botão de destaque/accent),
  **Pausar** (ícone pausa; vira **Retomar** quando parada e retomável), **Cancelar** (ícone X, texto/ícone em tom crítico).

### 2. Cartão de status (altura 78, raio 10, borda sutil)
- Esquerda: ladrilho 42×42 (raio 10, verde tonal) com ProgressRing quando rodando (ícone de situação quando concluída/erro);
  "Análise em andamento" (16 semibold) + pílula "● AO VIVO" (10 semibold, fundo verde claro, raio 20);
  abaixo "Etapa atual: Preparo · 2 atividades registradas · ChatGPT" (12,5 secundário).
- Direita, três métricas (rótulo 10,5 semibold maiúsculo; valor 15 semibold), espaço ~30 entre elas:
  **TEMPO DECORRIDO** (ícone relógio 16 + "03 min 47 s"), **ETAPAS** ("0 / 6 concluídas"), **PREVISÃO DE TÉRMINO**
  (valor 14 medium secundário, ex. "Histórico insuficiente" ou "≈ 23:58 · 12 min"; dica com o texto completo).

### 3. Cartão de progresso (altura 127, raio 10)
- Linha 1: "ETAPAS DA ANÁLISE" (12 semibold, secundário) à esquerda; "0 de 6 etapas concluídas" (12 medium) à direita.
- Linha 2: "Progresso global da análise" (12,5 medium) à esquerda; percentual "0%" (18 semibold, verde) à direita.
- Barra horizontal: altura 10, raio 5, trilho cinza claro, preenchimento verde (ProgressBar nativo estilizado ou equivalente).
- Linha 3 (sob a barra): as seis etapas distribuídas igualmente — "● 01 Preparo", "● 02 Análise", "● 03 Auditoria técnica",
  "● 04 Auditoria cruzada", "● 05 Relatório", "● 06 Revisão final". Ponto 7–8 px: verde = concluída/atual; cinza = pendente;
  vermelho = falha. Etapa atual em verde e semibold; concluídas com ✓ ou verde; pendentes cinza.

### 4. Três colunas (altura restante ~570; larguras 539 / 607 / 450 → proporção ≈ 12 : 13,5 : 10; espaço 12)

**Atividades da análise** (cartão raio 10, padding 14)
- Cabeçalho: ícone de lista + "Atividades da análise" (17 semibold) + pílula "● Ao vivo" (11 semibold) à direita.
- "Acompanhe cada etapa e os registros de execução." (12 secundário).
- Faixa-resumo (altura 43, fundo sutil, raio 7): "Etapas concluídas" à esquerda; "0 / 6 · 0%" (13 semibold) à direita.
- Lista das seis etapas (rolagem interna): etapa atual em bloco de fundo verde tonal (raio 7, altura ~64): ponto verde 10,
  nome 14 semibold, descrição 11,5, selo "EM ANDAMENTO" (10 semibold, verde) à direita. Pendentes: círculo vazado 9, nome 13,5 semibold,
  descrição 11,5 secundária. Concluída: ✓ verde + "CONCLUÍDA"; falha: ! vermelho + "COM ERRO".
  Descrições curtas: Preparo "Identificar pranchas e organizar os arquivos do processo"; Análise "Conferir itens, registrar conclusões e evidências";
  Auditoria técnica "Revisar cálculos, referências e pendências"; Auditoria cruzada "Confrontar as conclusões e localizar divergências";
  Produção do relatório "Organizar constatações e redigir exigências"; Revisão final "Conferir a versão consolidada e o arquivo final".

**Principais descobertas** (cartão raio 10, padding 18)
- Cabeçalho: "Principais descobertas" (17 semibold) + botão "Filtrar" (ícone funil, 109×30, fundo sutil) à direita.
- "Constatações preliminares até a auditoria cruzada." (12 secundário).
- Quatro ladrilhos (altura 100, raio 8, espaço 8): **ATENDE**/Conformidades (verde), **NÃO ATENDE**/Não conformidades (vermelho),
  **FALTA COMPROVAR**/Sem evidência (âmbar), **DIVERGÊNCIAS**/Inconsistências (azul). Rótulo 11 semibold, número 32 semibold, legenda 11.
- "EVIDÊNCIAS IDENTIFICADAS" (11 semibold) à esquerda; "0 registros" à direita.
- Área da lista (fundo sutil, raio 9, rolagem interna). Vazia: círculo 64 com ícone de prancheta, "Aguardando primeiras descobertas"
  (15 semibold, centro) e "As constatações aparecerão aqui, com classificação, referência ao PDF e página e trechos da evidência." (12, centro).
- Rodapé: "Exibição: todas as classificações" (11,5) à esquerda; link "Ver evidências por PDF e página ›" (12 semibold verde) à direita.

**Coluna direita** (dois cartões empilhados, espaço 12)
- **Consumo e desempenho** (altura ~208): título 16 semibold; "Uso registrado por etapa e modelo" (11,5);
  faixa (altura 45, fundo sutil): "Etapa" à esquerda, nome da etapa (13 semibold) à direita;
  três ladrilhos (altura 72, raio 7): **ENTRADA / SAÍDA** ("— / —"), **CACHE** ("—"), **CUSTO** ("—"); rótulo 10,5 semibold, valor 17 semibold.
- **Log de execução** (altura restante): "Log de execução" (16 semibold) + "● Ao vivo" (11) à direita;
  "2 atividades registradas · Última atualização 23:36" (11,5);
  itens (raio 8, altura ~75, espaço 9; rolagem interna): hora "23:36" (11 semibold) e ponto 10 (verde = mais recente/em curso, cinza = anteriores)
  numa coluna de 50; à direita título (12,5 semibold) e "Etapa · detalhe" (11,5);
  botão largo "Abrir evidências por PDF e página" (altura 42, fundo verde tonal, ícone documento à esquerda, ícone abrir-externo à direita).
