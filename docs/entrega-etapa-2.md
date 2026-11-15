# Entrega da Etapa 2: revisão básica, despachos, legislação e Novo relatório

Editor de Relatórios PMVNI, versão da Etapa 2 (15/11/2026). Este texto diz o que já funciona, o que foi testado e o que ainda falta.
O nome do arquivo e do `.exe` continua sem acentos (`Editor de Relatorios PMVNI`).

## O que funciona

- **O programa**: continua um único arquivo, `Aplicativo\Editor de Relatorios PMVNI.exe` (89,2 MB), sem instalar e sem ser
  administrador. O ícone "PMVNI Projetos" está no executável (atalho), na janela e na barra de tarefas; a barra de título mostra o
  brasão da Prefeitura, com a versão clara no tema escuro. À esquerda, o **trilho** com os módulos Relatórios, Despachos, Processos,
  Legislação e Ajustes; em cima, a **faixa de opções** do módulo (abas e botões, como no Office).
- **Relatórios (revisão básica)**: a análise da IA é importada para um **pacote `.pmvrel`** (nome `Relatorio_NNNN_AAAA.pmvrel`, nunca
  sobrescreve). No próprio A4: selecionar um item, editar o texto (duplo clique ou F2; Enter confirma, Esc cancela), mudar a
  situação (com justificativa obrigatória quando muda), excluir e restaurar itens (o item excluído vira uma marca fora da folha),
  desfazer e refazer, comparar com o original recebido, pesquisar (Ctrl+F) e conferir. O original recebido nunca muda; o
  histórico de cada alteração fica no pacote. **Salvamento automático** (30 s por padrão), diário de recuperação depois de uma
  queda, trava contra a mesma abertura em duas janelas e detecção de conflito (nunca sobrescreve uma versão mais nova).
  Exportar PDF continua igual à Etapa 1, agora a partir do pacote (o recibo leva o hash do `.pmvrel`).
- **Processos e Despachos**: listas da pasta PROCESSOS (somente leitura), com filtros e detalhe do processo.
- **Novo despacho de correção e de aprovação**: pela faixa de Relatórios, Processos ou Despachos. O texto vem dos modelos oficiais do
  kit (lidos no lugar, nunca copiados); as exigências da correção são os itens que não ATENDEM no estado atual do relatório; a prévia
  é editável; o PDF sai pelo LibreOffice sobre uma cópia; você confere cada página antes de gravar; grava DOCX, PDF e recibo numa
  pasta `DESPACHO NN - AAAA-MM-DD` nova ou existente, sem nunca sobrescrever.
- **Legislação por PDFs anexados** (Ajustes › Legislação e o módulo Legislação):
  - *Anexar PDFs…* (um ou vários), **uma linha por anexo**, com título e esfera editáveis, nome do arquivo, tamanho, páginas, data
    e selo ("Sem texto pesquisável"); *Substituir arquivo…* (nova versão da mesma lei) e *Excluir* (com confirmação). O editor guarda
    uma cópia de cada PDF em `%LOCALAPPDATA%\EditorRelatoriosPMVNI\legislacao` e **nunca altera o original**.
  - Limites e mensagens: até **200 MB** por PDF; vazio, não-PDF, repetido, com senha de abertura ou danificado são recusados com a
    mensagem exata e o resto segue; PDF digitalizado (sem texto) entra com o aviso "Sem texto pesquisável" (**sem OCR: nada é
    inventado**).
  - **Leitor**: páginas em rolagem contínua, "Página N de M", escala (aumentar, diminuir, página inteira, ajustar largura),
    marcadores do PDF, seleção de texto, **Copiar** e **Copiar citação** (“trecho” (título, página N)), pesquisa na lei aberta
    ("k de n") e em todas as leis ("N resultados em K leis", clicar abre na página).
  - O leitor é o **PDF.js 6.4.299** (Apache-2.0), embutido no executável: **283 arquivos, 4,75 MB** (sha conferido no build). Os PDFs
    abrem numa área isolada (InPrivate, sem rede, scripts do PDF desligados, links externos não abrem).
- **Novo relatório** (Relatórios › Novo relatório; desenho §12): o editor é **só o intermediário**. Ele arquiva os anexos na pasta do
  processo pela regra do kit (Documento, Projeto inicial ou reapresentação numa `REVISÃO NN` nova; cópia, nunca move), guarda o
  registro em `<processo>\.pmvni-ai\editor\<data>-<provedor>\`, inicia o agente **instalado neste computador** e, no fim, importa e
  abre o relatório. Quem analisa é o agente, com a sua configuração, os plugins do kit, os programas (QGIS…) e as pastas de rede.
  - **Analisar com o ChatGPT**: `"<codex.exe>" exec --skip-git-repo-check --cd "<PASTA DO PROCESSO>" --json --output-last-message
    "<PASTA DO PROCESSO>\.pmvni-ai\editor\<id>\ultima-mensagem.md" -` (pedido pela entrada padrão; o `codex.exe` mais novo de
    `%LOCALAPPDATA%\OpenAI\Codex\bin\*\`, o CLI que o app do ChatGPT/Codex instala, com o plugin `analise` do kit).
  - **Analisar com o Claude**: `"<python.exe>" -B -X utf8 "<pro_runtime.py>" --case "<PASTA DO PROCESSO>" iniciar` e, a cada 10 s,
    `… situacao`. O `pro_runtime.py` é o do `analise-pro` instalado no Codex
    (`%USERPROFILE%\.codex\plugins\cache\pmvni-kit\analise-pro\<versão>\skills\analise-pro\scripts\`); ele chama o Claude Code com a
    sua conta e segue numa janela minimizada do kit mesmo se o editor fechar. O pedido escrito fica só no registro (o modo
    automático do análise pro não recebe instruções).
  - Antes da primeira análise de cada provedor, um **aviso** pede o seu consentimento (uma vez por versão do texto).
  - **Conta ou chave de API** (Ajustes › Inteligência artificial): por padrão vale o login do programa. No ChatGPT você pode guardar
    uma chave de API: ela fica em `%LOCALAPPDATA%\EditorRelatoriosPMVNI\segredos`, protegida pelo Windows (DPAPI) só para o seu
    usuário, e vai só para o processo do Codex (variável `CODEX_API_KEY`); nunca vai para `ajustes.json`, registro, log ou Recriar
    PC. No Claude, a chave fica desabilitada enquanto o análise pro do kit proibir API paga.
  - **Testar** em cada cartão mostra versões, login e plugin (só comandos locais). **Cancelar análise** encerra o processo; uma
    análise por processo; trocar de módulo não interrompe.
- **Ajustes**: tema (claro, escuro, sistema), intervalo do salvamento automático, pasta de processos, pasta do kit, legislação e
  inteligência artificial; versões do app, do modelo e do WebView2.

## Como atualizar o PDF.js

1. Trocar a versão exata de `pdfjs-dist` em `web/paginador/package.json` (sem `^`) com `npm install --save-exact pdfjs-dist@<versão>`.
2. `npm ci` e `npm run build` em `web/paginador` (copia os arquivos usados para `src/Pmvni.Legislacao/web/pdfjs/` e regrava o
   `VERSAO.json`).
3. Revisar a diferença do `VERSAO.json` e dos arquivos vendorizados (licenças inclusive).
4. Atualizar a contagem de arquivos e os bytes esperados (`VERSAO_PDFJS`, `ARQUIVOS_ESPERADOS` e `BYTES_ESPERADOS` em
   `web/paginador/build.mjs`, que recusa números diferentes), nos testes de `Pmvni.Legislacao.Testes` e no desenho §11.4 (a linha "PDF.js … embutido" do
   `tools\build-release.ps1` vem do autoteste e se ajusta sozinha).
5. Rodar `dotnet test` (pelo `tools\build-release.ps1`) e o roteiro (`tools\roteiro-e-esperar.ps1`).

## O que foi testado

Tudo abaixo foi rodado nesta entrega (15/11/2026).

- **Testes automáticos** (`tools\build-release.ps1`, `dotnet test` da solução em Release): **1.031 testes, todos aprovados** na rodada final (integração da onda final de correções com a rodada de interface, duas execuções inteiras seguidas do `build-release.ps1` verdes: 60 da montagem do relatório, 524 da revisão, dos despachos e da lógica do app, 133 da legislação, 149 dos agentes e 165 da tela, do paginador, da exportação, do leitor e da segurança). Algumas rodadas anteriores do mesmo dia tiveram uma falha isolada em testes que dependem do relógio ou do OneDrive (registradas no relatório da tarefa); cada um passou sozinho e a rodada inteira seguinte saiu verde.
- **Autoteste do executável final**: aprovado (2 rodadas, ok, sem administrador); o PDF.js embutido confere com o `VERSAO.json` (283 arquivos, 4,75 MB). Executável: 89,2 MB. A limpeza do build liberou 1.001 MB no projeto e 411 MB em extrações antigas.
- **Roteiro da tela** (`docs\roteiro-etapa-2.md`, resultado em `docs\roteiro-etapa-2.resultado.json`): **69 passos, todos
  aprovados** (`ok: true`, nenhuma falha), com o executável final, aberto fora do pacote do Claude. Os passos 65 a 69 conferem a interface (ícones e seleção do trilho, avisos de layout no indicador, Ajustes por categorias, nomes acessíveis, botão primário) e gravam as capturas da janela em `tests\saida\capturas`. O `64_fluxo_completo` faz o
  caminho inteiro em sequência: Novo relatório pelo agente falso → editar, situação, excluir e desfazer → salvar, fechar e reabrir →
  exportar PDF → pesquisar → tema → Recentes → Comparar → Novo despacho de correção com o LibreOffice real → anexar, pesquisar, citar e
  excluir uma lei fictícia; no fim confere que nenhuma chave falsa ficou em arquivo, que nada foi gravado fora de `tests\saida\roteiro`,
  `tests\saida\dados-app` e `tests\saida\agente-falso` e que só partiram os programas da trava do roteiro: o agente falso e o
  LibreOffice (o `soffice` exato que o editor acha), que converte os despachos dos passos 44 e 64 e aparece em `programas_iniciados`
  (`fora_do_agente_falso` conta as partidas dele). Latência de um comando no relatório longo: 623 ms (meta 1,5 s).
- **Comparação com o kit** (`python -I tools\fidelidade.py tudo`, código 0; detalhes em `docs\fidelidade.md`): 7 relatórios (5 de
  teste, o `dados_modelo` e 1 processo real, não citado nem copiado). V1 (HTML igual ao do kit) 7/7; V2 (tela × PDF do editor) 7/7, maior
  diferença 1,42 %; V3 (PDF do editor × PDF do kit) 7/7, maior diferença 1,66 %; **V5** (o relatório refeito a partir do pacote `.pmvrel`
  dá o mesmo HTML do kit, letra por letra) 7/7; **V6** (o despacho gerado pelo editor passa no validador oficial do kit) 3/3 (correção
  e aprovação de teste e a correção do processo real), 0 erros. Privacidade do navegador interno: a marca de teste em 0 de 142 arquivos.
- **Novo relatório**: no `dotnet test` (`Pmvni.Agentes.Testes`) e no roteiro (passos 49 a 56 e 64) só com o **agente falso**
  (`tests\AgenteFalso`), que faz o papel do `codex.exe`, do `claude.exe` e do `python.exe` sem rede e sem IA. A trava de programas
  (`TravaDeProgramas`) vale para tudo o que o editor inicia, o agente e o LibreOffice: nos testes, só a pasta do agente falso; no roteiro,
  a pasta do agente falso e o `soffice` do LibreOffice (os despachos são convertidos com o LibreOffice real). A execução com o ChatGPT e o
  Claude reais é a conferência humana abaixo.

## Conferência humana pendente

Situação: **pendente** (não foi feita nesta entrega; o construtor não roda IA real). Ao fazer, registre aqui só situação, duração,
contagens e hashes (nunca nomes, conteúdo ou caminhos de processo real).

Abra o editor pelo atalho (ou `Aplicativo\Editor de Relatorios PMVNI.exe` pelo Explorador) e, num processo que você escolher:

- (a) **Ajustes › Inteligência artificial › Testar** nos dois cartões: aparecem as versões reais, "conectado com a conta…", "plugin
  analise <versão> habilitado" e "análise pro <versão>"; no Claude, "Usar chave de API" desabilitado com o motivo. — pendente
- (b) **Novo relatório** com 1 anexo e um pedido curto → **Analisar com o ChatGPT** → o aviso aparece uma vez → o andamento mostra as
  mensagens e os comandos do Codex ao vivo; o Codex usa os scripts do kit (e o QGIS, se a análise pedir) → no fim o `.pmvrel` abre no
  editor e o PDF do kit está na `DESPACHO NN`. — pendente
- (c) **Analisar com o Claude** (no mesmo ou noutro processo) → a janela minimizada do kit abre, as etapas avançam na vista da
  execução; fechar e reabrir o editor durante a análise → aparece a faixa "terminou" com "Importar relatório". — pendente
- (d) **Cancelar** uma execução de cada provedor → o processo é encerrado e o registro fica com `cancelada`. — pendente
- (e) Se você usa chave da OpenAI: modo chave no ChatGPT → a análise parte e o `execucao.json` mostra `modo: "chave"`, sem a chave.
  Confere também o **nome da variável `CODEX_API_KEY`**: se o Codex não aceitar a chave por ela, a análise falha com mensagem clara
  (vira correção antes de usar o modo chave). — pendente
- (f) No **Gerenciador de Tarefas**, os programas filhos (codex, python, claude) rodam como você, fora de pacote (coluna "Nome do
  pacote" vazia). — pendente
- **Aceleradores de teclado reais** da janela (Ctrl+S, Ctrl+Z, Ctrl+F, F2, Delete, Alt+↑/↓ e os do leitor) com o teclado de verdade
  (o roteiro manda as teclas direto à página). — pendente
- **Placeholders do OneDrive** na árvore PROCESSOS real (arquivos "só na nuvem"): as listas de Processos e Despachos abrem sem baixar
  tudo e sem travar. — pendente
- **Visual e toque do mouse nos dois temas** (claro e escuro): barra de título, faixa, trilho, páginas, leitor e diálogos. — pendente
- **Brasão nítido nos dois temas** na barra de título. — pendente
- **Ícone no atalho, na janela e na barra de tarefas** ("PMVNI Projetos"). — pendente
- **Seleção com o mouse e área de transferência reais** no leitor de legislação (arrastar para selecionar, Copiar e Copiar citação,
  colar no Word). — pendente

## O que fica pendente (etapas seguintes)

- Cálculos e diagramas vinculados; aprovação completa e outros tipos de despacho fora do registro do kit; gestão de versões dos
  modelos; esquema público do `.pmvrel`; segurança completa do pacote (assinatura).
- OCR de PDFs digitalizados: fora de propósito (nada é inventado).
- Integração (merge) ao kit: só com a sua autorização.
- Chave de API do Claude: enquanto o `analise-pro` do kit a proibir; pedido escrito no modo automático do `analise-pro` (o kit não o
  recebe hoje).

## Como gerar uma nova versão

`powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1`: roda os testes, publica, confere o `.exe`, roda o
autoteste, limpa o que sobrou e informa o espaço liberado. Depois, `tools\roteiro-e-esperar.ps1` (roteiro da tela) e
`python -I tools\fidelidade.py tudo`.
