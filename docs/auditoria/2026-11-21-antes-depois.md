# Uso da IA na análise: antes e depois (Editor 3.0.0 e kit 4.0.0, 21/11/2026)

Este documento tem três partes: o que mudou no uso da IA, por etapa (a partir dos relatórios das tarefas 1 a 22); a tabela de
medição, ainda vazia; e o passo a passo para voltar à versão anterior (kit 3.x e Editor 2.2.0).

Fontes: `docs/auditoria/2026-11-20-consumo-codex-19486.md` (o diagnóstico do processo 19486), `docs/auditoria/2026-11-21-revisao-das-etapas.md`
e os relatórios das tarefas guardados em `.superpowers\sdd\2026-11-21-correcoes-e-uso-da-ia`.

## 1. O que mudou no uso da IA, por etapa

### 1.1. Resumo

| | Antes (Editor 2.2.0, kit 3.0.1 / analise-pro 2.0.1 / analise-google 2.0.1) | Depois (Editor 3.0.0, kit 4.0.0) |
|---|---|---|
| Sessões de IA por análise (caminho sem correções) | 6 etapas com IA, mais uma sessão "controladora" do Codex só para esperar o script na rota ChatGPT | 4 etapas com IA (analyze, technical_audit, audit, final_review); preparo e produção são do programa; sem controladora |
| Quem roda o controlador | Na rota ChatGPT, uma IA (sessão Codex) que ficava "olhando" o script | O próprio Editor, como processo filho (`python executar_analise_62.py`), nas três rotas |
| Plugins | analise, analise-pro, analise-google e despacho | Só o analise, com as rotas ChatGPT, Claude e Gemini; o despacho é do Editor |
| Contexto de cada etapa | Pedido grande, com material que a etapa não usava | Núcleo fixo no começo (cache) mais as referências da etapa |
| Correção após reprovação do validador | Nova sessão, do zero | Mesma sessão, com a saída real do validador |
| Objeção só de apresentação | Refazia analyze e technical_audit | Remonta o relatório (programa) e refaz só a revisão visual |
| Reanálise de uma nova revisão do projeto | Análise integral | A IA revê o que mudou; seções iguais são reaproveitadas, com conferência |
| Progresso, previsão e consumo | Estimados pela IA | Calculados pelo Editor |

No processo 19486 (19/11/2026), a sessão controladora gastou 23,4 milhões de tokens de entrada, 64 % só esperando o script, e
as sessões de etapa somaram cerca de 115 minutos, dos quais só uns 58 minutos foram trabalho que ficou. As mudanças abaixo
atacam justamente isso. Os números do "depois" vêm da medição (parte 2), ainda não feita.

### 1.2. Por etapa

| Etapa | Antes: sessões e quem executa | Depois: sessões e quem executa | Mudanças que valem para a etapa |
|---|---|---|---|
| Controle da execução (rota ChatGPT) | 1 sessão de IA (Codex controladora, com todos os plugins e conectores do usuário), 64 % do consumo registrado só em espera | Nenhuma. O Editor roda o controlador | Tarefa 12: as entradas, a retomada, a rota e a espera de cota são tratadas pelo Editor; códigos de saída 0, 20, 21 e 1 e uma linha JSON final |
| prepare (preparo) | 1 sessão de IA (Sol baixo ou médio, Sonnet médio, Flash). Em 16488, cinco preparos seguidos; tudo era descartado pela analyze | 0 sessões. Executor: **programa** | Tarefas 4, 7, 8, 9: confere a versão do kit, escolhe as peças atuais, gera inventário, texto, renders, quadros, cadeias de cotas candidatas e o checklist documental. A IA só confirma ou rejeita os candidatos |
| Escolha das peças | A IA decidia | **Programa**, com pergunta ao analista quando há versões diferentes | Tarefas 4 e 14: revisão da pasta, depois carimbo, sem desempate por data; cópia idêntica (SHA-256) é deduplicada; dúvida vira pergunta no Editor |
| analyze (análise) | 1 sessão por tentativa, sempre nova; 9 falhas de 11 por ambiente, ferramenta negada ou cota; uma falha sozinha custou 695 mil tokens | 1 sessão (IA), retomável depois de uma volta; correção na mesma sessão | Tarefas 1, 2, 6, 10: só objeção técnica sobe o nível; cota baixa desce; espera de cota em vez de repetir na hora; contexto menor e com cache; reanálise pela diferença |
| technical_audit (auditoria técnica) | 1 sessão (IA) auditava o manifesto, não o texto que seria publicado | 1 sessão independente (IA), que também confere o texto a publicar, antes da montagem | Tarefa 20: quem audita não corrige; trava por hash do mérito já auditado |
| audit (auditoria cruzada) | 1 sessão (IA), depois da produção | 1 sessão (IA), antes da produção, ligada nas três rotas | Tarefa 20: etapa opcional por rota; objeções técnicas voltam à analyze |
| produce (produção do PDF) | 1 sessão de IA; o Edge não gerava o PDF dentro da sandbox | 0 sessões. Executor: **programa** | Tarefas 4 e 20: valida as figuras, monta os dados, chama o gerador oficial fora da sessão da IA, confere os validadores, o recibo e os caminhos |
| final_review (revisão final) | 1 sessão (IA); objeção de apresentação refazia a análise e a auditoria técnica | 1 sessão (IA); só apresentação; percorre o PDF do `delivery.json` página por página | Tarefa 3: objeção só de apresentação volta à produção (programa); objeção técnica ou duvidosa volta à analyze com reauditoria. Limite de dois ciclos |
| final (gate) | Programa | Programa | Os quatro validadores e o arquivamento; reprovação do gate dispara o ciclo correspondente |
| Despacho | Plugin despacho (IA) | **Programa** (Editor), sem IA por padrão | Tarefas 15 e 22 |
| Rever das tabelas | IA | **Programa** por padrão; IA só em Rever com a IA | Tarefa 15 |
| Progresso, previsão e consumo | Estimados pela IA | **Programa** (Editor) | Tarefa 13 |

### 1.3. Modelos e níveis (tabela única por rota, Tarefa 19)

Padrão do kit (o analista pode ajustar em Ajustes > Modelos da análise; o ajuste local fica neste PC e pode ser publicado como padrão do kit):

| Rota | analyze | technical_audit | audit | final_review |
|---|---|---|---|---|
| ChatGPT | gpt-6.2-astra, alto | gpt-6.2-astra, alto | gpt-6.2-sol, médio | gpt-6.2-sol, baixo |
| Claude | claude-opus-5-5, alto | claude-opus-5-5, alto | claude-sonnet-5-5, alto | claude-opus-5-5, alto |
| Gemini (conta Ultra) | gemini-3.8-flash, médio | gemini-3.8-flash, médio | gemini-3.8-flash, médio | gemini-3.8-flash, médio |

Regras: na rota ChatGPT o nível sobe só por objeção técnica e desce um nível com 15 % ou menos de cota; na rota Claude não sobe por objeção e,
com 15 % ou menos de cota na conta, vai ao nível baixo; na rota Gemini modelo e nível são fixos. Os perfis do Gemini (conta Gemini, API com
créditos, API gratuita) têm linhas próprias no arquivo do kit.

## 2. Medição

A preencher com a rodada de medição: um caso fictício completo, com o mesmo processo, em cada rota, nas versões antiga e nova. Também
conferir que as 28 seções são cobertas, que os achados são iguais nos dois casos e que o PDF é legível. Fonte dos dados: `uso-das-IAs.json`,
`engine_state.json` e o relatório do Codex-LB.

Legenda: sessões = sessões de IA abertas na etapa; tokens = entrada e saída; cache = percentual de entrada vindo do cache; duração em minutos;
cota = variação da cota da conta (pontos percentuais); tentativas = tentativas, com as falhas entre parênteses.

| Etapa | Rota | Versão | Sessões | Tokens | Cache | Duração | Cota | Tentativas |
|---|---|---|---|---|---|---|---|---|
| prepare | ChatGPT | antes | | | | | | |
| prepare | ChatGPT | depois | | | | | | |
| prepare | Claude | antes | | | | | | |
| prepare | Claude | depois | | | | | | |
| prepare | Gemini | antes | | | | | | |
| prepare | Gemini | depois | | | | | | |
| analyze | ChatGPT | antes | | | | | | |
| analyze | ChatGPT | depois | | | | | | |
| analyze | Claude | antes | | | | | | |
| analyze | Claude | depois | | | | | | |
| analyze | Gemini | antes | | | | | | |
| analyze | Gemini | depois | | | | | | |
| technical_audit | ChatGPT | antes | | | | | | |
| technical_audit | ChatGPT | depois | | | | | | |
| technical_audit | Claude | antes | | | | | | |
| technical_audit | Claude | depois | | | | | | |
| technical_audit | Gemini | antes | | | | | | |
| technical_audit | Gemini | depois | | | | | | |
| audit | ChatGPT | antes | | | | | | |
| audit | ChatGPT | depois | | | | | | |
| audit | Claude | antes | | | | | | |
| audit | Claude | depois | | | | | | |
| audit | Gemini | antes | | | | | | |
| audit | Gemini | depois | | | | | | |
| produce | ChatGPT | antes | | | | | | |
| produce | ChatGPT | depois (programa) | | | | | | |
| produce | Claude | antes | | | | | | |
| produce | Claude | depois (programa) | | | | | | |
| produce | Gemini | antes | | | | | | |
| produce | Gemini | depois (programa) | | | | | | |
| final_review | ChatGPT | antes | | | | | | |
| final_review | ChatGPT | depois | | | | | | |
| final_review | Claude | antes | | | | | | |
| final_review | Claude | depois | | | | | | |
| final_review | Gemini | antes | | | | | | |
| final_review | Gemini | depois | | | | | | |
| Controle (controladora) | ChatGPT | antes | | | | | | |
| Total da análise | ChatGPT | antes | | | | | | |
| Total da análise | ChatGPT | depois | | | | | | |
| Total da análise | Claude | antes | | | | | | |
| Total da análise | Claude | depois | | | | | | |
| Total da análise | Gemini | antes | | | | | | |
| Total da análise | Gemini | depois | | | | | | |

Nota: a preencher com a rodada de medição. Linha de base já existente: o log do Codex-LB do processo 19486 (375 requisições, 8 sessões) e
os `uso-das-IAs` recentes, resumidos em `2026-11-20-consumo-codex-19486.md` e `2026-11-21-revisao-das-etapas.md`. Registrar também, como cada
uma aparece para o analista: cota esgotada na analyze, troca de arquivo depois do preparo, interrupção entre etapas e objeção de apresentação.

## 3. Como voltar

Voltar tem duas metades que andam juntas, porque o Editor 3.0.0 só funciona com o kit 4.0.0 e o Editor 2.2.0 só funciona com o kit 3.x
(analise 3.x, analise-pro e analise-google). Se só uma metade voltar, os botões de análise do Editor ficam desabilitados.
Faça os passos fora do pacote do app Claude (arquivo `.cmd` aberto pelo `explorer.exe`).

### 3.1. Antes de publicar (para poder voltar)

O instalador do kit (`INSTALAR_KIT_PMVNI.cmd`) **não** cria cópia dos plugins antigos: ele apaga do cache do Codex e do Claude Code os plugins
aposentados (analise-pro, analise-google, despacho) e as versões antigas, e só guarda em `%LOCALAPPDATA%\PMVNI\removidos\<data>` (as três últimas datas)
o que pode ter valor para o usuário (catálogos e configurações antigos, pastas PMVNI antigas). O kit mestre já usa, à mão, a convenção
`<KIT>\01 - PLUGINS\backups\<plugin>\antes-<versão>-<data>` (hoje existem `analise-google\antes-1.0.6-20261118-031008` e `antes-1.0.7-20261118-105046`).
Antes de publicar o kit 4.0.0 no kit mestre, siga a mesma convenção:

1. No kit mestre (`<KIT>` = `C:\Users\smendesjunior\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)`), copie cada plugin como está, para:
   - `01 - PLUGINS\backups\analise\antes-3.0.1-<aaaaMMdd-HHmmss>`;
   - `01 - PLUGINS\backups\analise-pro\antes-2.0.1-<aaaaMMdd-HHmmss>`;
   - `01 - PLUGINS\backups\analise-google\antes-2.0.1-<aaaaMMdd-HHmmss>`;
   - `01 - PLUGINS\backups\despacho\antes-2.3.0-<aaaaMMdd-HHmmss>`.
2. Copie também, na mesma data, os arquivos que mudam junto com os plugins e que o procedimento acima não cobre: os dois catálogos
   (`01 - PLUGINS\.agents\plugins\marketplace.json` e `01 - PLUGINS\.claude-plugin\marketplace.json`), `01 - PLUGINS\INSTRUCOES DAS IAS`
   e o `INSTALAR_KIT_PMVNI.cmd` da raiz do kit (o instalador novo remove os plugins aposentados; o antigo os instala), por exemplo para
   `01 - PLUGINS\backups\_catalogos-e-instalador\antes-<data>`.
3. Guarde também uma cópia do executável do Editor 2.2.0 (ver 3.3) antes de o `tools\build-release.ps1` trocar o `Aplicativo\...exe`.

### 3.2. Voltar o kit (3.x)

1. Feche o Editor e confira que não há análise em andamento (o instalador também recusa atualizar com análise viva).
2. No kit mestre, devolva ao lugar `01 - PLUGINS\plugins\analise`, `analise-pro`, `analise-google` e `despacho` a partir das pastas
   `backups\<plugin>\antes-<versão>-<data>` mais recentes (apague antes as pastas atuais `plugins\analise` 4.0.0). Devolva os dois catálogos,
   `INSTRUCOES DAS IAS` e o `INSTALAR_KIT_PMVNI.cmd` guardados no passo 2 de 3.1. Se a cópia tiver sido feita sem a pasta `plugins` (só o
   conteúdo), recrie a pasta com o nome do plugin.
3. Atualize o `01 - PLUGINS.zip` do kit, se ele for usado na distribuição, e deixe o servidor receber a cópia.
4. Em cada PC, rode `INSTALAR_KIT_PMVNI.cmd atualizar` (ou espere a tarefa "PMVNI\Atualizar kit"). Esse comando reinstala o que o kit mestre
   tiver. Num PC que já tenha o 4.0.0, confira em Ajustes > Plugins e ferramentas, no Editor, as versões instaladas.
5. Análises iniciadas pelo kit 4.0.0 criaram um controle com a marca do plugin único. Não as retome no kit 3.x: recomece a análise do processo
   (o controle antigo fica em `.pmvni-ai\<pasta da rota>\historico`). Não foi testado como o kit 3.x trata esse controle.

### 3.3. Voltar o Editor (2.2.0)

O Editor 2.2.0 tem duas versões publicadas com o mesmo número: as compilações `20261119160729` (commit `00f9897`) e `20261119164841`
(commit `39edc90`, a mais recente da 2.2.0). Use a `20261119164841`, a menos que a outra tenha sido a instalada no PC.

Onde está o instalador da 2.2.0:

- Release do GitHub do projeto: repositório `smendesjunior/analises-pmvni`, etiqueta `build-20261119164841`, arquivo `EditorRelatoriosPMVNI.exe`
  (página `https://github.com/smendesjunior/analises-pmvni/releases/tag/build-20261119164841`; confirme que a Release ainda está lá antes de
  precisar dela, porque não foi consultada pela rede ao escrever este documento). O manifesto é o `EditorRelatoriosPMVNI-update.json` da mesma Release.
- Pelo código: a etiqueta `build-20261119164841` do repositório local aponta o commit `39edc90`; a `build-20261119160729` aponta `00f9897`.
- Cópia local: o `Aplicativo\Editor de Relatorios PMVNI.exe` da pasta do projeto (antes do próximo `tools\build-release.ps1`) é a compilação
  `20261119160729` (conferir em `Aplicativo\build-validada.json`).

Passo a passo:

1. Se for usar a cópia local, guarde antes uma cópia do `Aplicativo\Editor de Relatorios PMVNI.exe` em outra pasta (o `build-release` da 3.0.0 a substitui).
2. Gere o executável da 2.2.0 pelo código: crie uma pasta de trabalho na etiqueta (`git worktree add <pasta> build-20261119164841`), e nela rode
   `tools\build-release.ps1 -SemGitHub` (o script roda os testes, publica, confere o executável e limpa os builds). O resultado fica em `Aplicativo\` dessa pasta.
   Alternativa sem compilar: baixe o `EditorRelatoriosPMVNI.exe` da Release `build-20261119164841` (o instalador `tools\instalar.cmd` só baixa a Release mais recente).
3. Feche o Editor. Troque o executável da cópia de uso, `%LOCALAPPDATA%\Programs\Analise de Projetos PMVNI\Editor de Relatorios PMVNI.exe`, pelo da 2.2.0.
   Os atalhos e a associação de `.rpmvni` continuam valendo.
4. **Atualização automática.** A cada 15 minutos o Editor instala a compilação mais nova publicada no GitHub. Enquanto a Release 3.0.0 for a mais
   recente, o Editor volta sozinho para a 3.0.0. Há duas saídas: (a) republicar a 2.2.0 como compilação nova, a partir da etiqueta, com
   `tools\build-release.ps1` na pasta de trabalho do passo 2 (o carimbo da compilação é a data da build, então ela passa a ser a mais recente);
   (b) apagar ou despublicar a Release 3.0.0 no GitHub, o que a retira da lista, e o `releases/latest` volta para a 2.2.0. Qualquer das duas
   publica algo no GitHub e depende de decisão do dono do projeto.
5. Dados: o `ajustes.json` e as pastas de dados são compatíveis nos dois sentidos. O ajuste local de Modelos da análise
   (`%LOCALAPPDATA%\EditorRelatoriosPMVNI\modelos-por-rota.local.json`) é ignorado pela 2.2.0 e pode ficar. Análises em andamento da 2.3.0 devem ser recomeçadas na 2.2.0.
6. Com o kit 3.x e o Editor 2.2.0 de volta, confira que o Novo relatório mostra os três botões de análise habilitados. O cartão Plugins e
   ferramentas só existe a partir da 3.0.0; na 2.2.0, confira as versões no cache do Codex (`%USERPROFILE%\.codex\plugins\cache\pmvni-kit`).

### 3.4. Depois de voltar

Se a volta for definitiva, atualize o `recriar.json` do Editor (versão e texto das rotas) e rode, fora do pacote, o `Capturar agora.cmd` e o
`Conferir.cmd` do Recriar PC, como em toda mudança de ambiente.
