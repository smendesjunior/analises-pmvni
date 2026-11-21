# Recursos do Editor de Relatórios PMVNI — frente 3.1

## Ponto de partida

O usuário mediu a versão 3.0.0 após 14 minutos aberta: 651 MB privados, 2.657 handles, 30 threads e 1,4 s de CPU em 15 s de parede (9,3% de um núcleo). Essa amostra é do executável do Editor. Processos de renderização e GPU do WebView2 têm contadores próprios. A amostra, sozinha, não permite atribuir a CPU a um timer específico.

## Atividade da interface

| Local no código | Achado e mudança | Efeito esperado |
| --- | --- | --- |
| `src/Pmvni.App/Controles/BrilhoArcoIris.cs:492` e `MainWindow.xaml.cs:232` | O brilho usa um relógio de 31 ms compartilhado. A animação já verificava ancestrais ocultos, mas perder o foco mantinha os callbacks. Agora a desativação para o relógio e a ativação retoma apenas quando há animações inscritas. | Até 32 callbacks/s a menos fora de foco **quando havia brilho ativo**. Com zero animações, o timer já ficava parado. |
| `src/Pmvni.App/Paginas/PaginaSites.cs:62` | Os timers de renovação (5 min) e cópia de cookies (1 min) começavam no construtor, inclusive sem site aberto. Passam a acompanhar foco, módulo e site atual. A gravação final dos cookies é preservada. O WebView2 recebe a meta de memória baixa fora de uso. | Menos despertares, consultas ao cofre e scripts de rede de sites ocultos. A liberação física depende do runtime. |
| `src/Pmvni.App/MainWindow.Agentes.cs:210` e `MainWindow.ModelosIa.cs:84` | A janela de login Gemini era escondida no fechamento, mantendo a consulta de 2 s. Agora a consulta ocorre a cada 10 s somente com a janela ativa. O catálogo de modelos usa o evento de entrada na categoria e o botão de atualização. | A janela oculta deixa de consultar a conta. Sai a verificação periódica do catálogo. |
| `src/Pmvni.App/MainWindow.Acompanhamento.cs:171` e `:216` | O observador de arquivo permanecia associado à planilha depois de sair da página. Ao ocultar ou desativar o Acompanhamento, são liberados o watcher e as tentativas periódicas de gravação. Callbacks já enfileirados conferem a identidade do watcher. O retorno relê o arquivo para recuperar mudanças ocorridas durante a pausa. | Sem releitura nem tentativas a cada 15 s no módulo oculto. Há o custo de uma leitura ao voltar. As alterações pendentes continuam guardadas em disco. |
| `src/Pmvni.App/MainWindow.Revisao.cs:1136` e `MainWindow.NovoRelatorio.cs:260` | O relógio de revisão depende de operação, foco e página. O relógio da análise preserva a pílula global em primeiro plano, e só consulta achados quando o monitor está visível. | Reduz atualizações de controles e leituras de disco sem interromper a análise. |
| `src/Pmvni.App/Atualizador.cs:59` e `:86` | A consulta automática ao GitHub usava um laço permanente. O novo timer de disparo único para ao perder o foco e conserva o vencimento da próxima consulta. O evento estático da atualização é removido ao fechar. | Sem consultas automáticas iniciadas em segundo plano. Uma transferência já iniciada pode terminar. |
| `src/Pmvni.App/MainWindow.xaml.cs:1176` e `Paginas/PaginaRelatorios.xaml.cs:19` | O fechamento esperava a operação com sondagens a cada 100 ms. Agora aguarda o sinal de liberação. O rodapé ignora layout da página oculta e a barra de título evita recalcular suas regiões sem foco. | Elimina sondagens de fechamento e parte do processamento de layout. |

## Memória, inicialização e duração das operações

- `src/Pmvni.App/MainWindow.NovoRelatorio.cs:798`: o dicionário da janela mantinha por toda a sessão as execuções concluídas, seu progresso e o resultado da importação. Cada execução agora guarda a própria tarefa de conclusão. Quando sai da sessão e da tela, o coletor pode liberar o conjunto. A conclusão também descarta a fonte de cancelamento.
- `src/Pmvni.App/Controles/IconeArquivo.cs:19`: o cache estático de ícones do Windows crescia com cada extensão, inclusive as sem ícone. O limite é de 128 extensões por ordem de inserção. Imagens exibidas continuam nas respectivas linhas. As miniaturas de arquivo já tinham limite de 200 (`MiniaturaArquivo.cs:17`).
- `src/Pmvni.Legislacao/ServidorLegislacao.cs:153`: as respostas PDF abrem o arquivo sob demanda e servem o trecho solicitado por stream. O registro do servidor guarda token e caminho. Os 92 MB do acervo no disco não correspondem a uma alocação demonstrada de 92 MB no Editor. Os registros do leitor e do extrator também já tinham limites (`LeitorPdfJs.cs:112`, `ExtratorPdfJs.cs:102`, em `src/Pmvni.Composicao.Web/Legislacao`).
- `src/Pmvni.App/MainWindow.xaml.cs:265`, `MainWindow.Bci.cs:272` e `MainWindow.Imoveis.cs:413`: fechamento explícito de navegadores, inclusive o boletim em janela auxiliar. O leitor de imóveis sai da árvore visual quando se volta à pesquisa ou quando a inicialização dele falha. Os sites mantêm o perfil para a próxima visita.
- `src/Pmvni.App/MainWindow.xaml.cs:281`, `MainWindow.Ajustes.cs:739`, `MainWindow.Sobre.cs:42`: a primeira carga do navegador entra na fila da interface com prioridade baixa após o carregamento da raiz. A listagem das leis dos Ajustes e o diagnóstico de versões são solicitados pelas respectivas categorias. O aplicativo ainda cria a interface das páginas no construtor.
- `src/Pmvni.Agentes/DiagnosticoGemini.cs:49`: reutilização do cliente HTTP nas conferências reais. A chave continua restrita à mensagem daquela chamada. O caminho dos testes preserva o manipulador injetado.
- Operações com obrigação de persistência mantêm seu próprio ritmo. O batimento do pacote aberto (`MainWindow.Gravacao.cs:35`) conserva a trava e a pasta de trabalho. O salvamento automático usa disparo único. A gravação periódica do tempo (`Agentes/SessaoNovoRelatorio.cs:61`) e a leitura de situação do kit (`src/Pmvni.Agentes/OrquestradorAnalise.cs:824`) só existem durante análise. As esperas de debounce da busca, mouse e redimensionamento são disparadas por interação. Não foi encontrado uso direto de `CompositionTarget.Rendering` no código C# do produto.

As leituras de pastas e pesquisas de imóveis descartam a fonte de cancelamento ao terminar, inclusive após substituição por outra leitura. A volta ao passo inicial do despacho também solta as imagens e o layout usados na prévia (`MainWindow.Pastas.cs:317`, `MainWindow.Imoveis.cs:118` e `Paginas/PaginaNovoDespacho.Folhas.cs:82`, em `src/Pmvni.App`).

## Processos internos

Em `src/Pmvni.Revisao/ProcessoInterno.cs:75`, o kernel recebe a lista de jobs e os únicos handles a herdar na própria criação do processo. A thread principal nasce suspensa. O Editor prepara os streams e o observador de término antes de liberá-la na linha 106. Fechar o último handle de um grupo com `KILL_ON_JOB_CLOSE` encerra seus membros, inclusive se o Editor cair. O fechamento da janela principal também fecha o job da sessão, mesmo se houver outra janela aberta. Uma falha na preparação impede a execução do programa.

A regra cobre execuções da IA, diagnósticos, catálogo de modelos, Python, comandos internos do instalador, extração de arquivos, console de login e conversão por LibreOffice. Em `src/Pmvni.Agentes/OrquestradorAnalise.cs:713`, o grupo de Claude/Gemini dura até o fim da análise: o comando curto de partida pode sair antes do trabalhador. Em `src/Pmvni.App/MainWindow.NovoRelatorio.cs:1365`, fechar pede a pausa conforme o ajuste do usuário e encerra também essas rotas.

Aberturas solicitadas ao shell, como um documento no aplicativo associado, uma pasta no Explorador, a atualização independente pelo Agendador e a reabertura da versão nova, são entregues ao Windows. Esses programas têm ciclo de vida próprio. Os scripts do motor de PDF e os mecanismos de limpeza de disco ficaram fora da alteração.

## Medição no aplicativo

O valor final de CPU, memória privada, handles, threads e tempo até a primeira tela requer medição interativa. Para comparar com a amostra original, usar o mesmo documento, módulo, escala e tempo de aquecimento. Medir CPU por diferença de tempo do processo em várias janelas de 15 s, com o Editor em foco, sem foco e minimizado. Para investigar retenção, repetir ciclos de abertura e fechamento de boletim, lei e relatório de imóvel e observar se os contadores estabilizam. Registrar separadamente o Editor e os processos WebView2. Não foi calculada economia percentual a partir da inspeção do código.

## Conferência

A solução x64 compilou com zero erros e zero avisos na última compilação. A restauração usou o cache local do NuGet. Os testes de Composição (78), Legislação (152), Imóveis (64) e Acompanhamento (20) passaram. A execução final de Agentes passou nos 283 testes, inclusive cancelamento de Claude e Gemini, retomada de registro anterior e árvore do Codex. Os 12 testes de trava de programas e de perfil do LibreOffice passaram com caminho curto para os perfis fictícios.

Na execução completa de Revisão, 1.057 passaram, dois foram ignorados e dois falharam. Os dois que falharam passaram em repetição pelo caminho físico do worktree, usando o Python do QGIS e os modelos do kit instalado. O teste novo do cache e o da árvore de processos também passaram individualmente.

A suíte Web teve 52 aprovações e 137 falhas. Entre os erros estão queda do renderizador WebView2 e identificador de janela inválido. O teste de aparência de tela também falhou numa cópia limpa do commit inicial 04de4f4. Essa contraprova cobre um dos casos e não estabelece a causa de todas as falhas. A validação interativa de CPU e memória e a aprovação integral da suíte Web seguem pendentes.
