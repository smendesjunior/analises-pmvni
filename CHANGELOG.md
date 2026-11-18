# Novidades do Editor de Relatórios PMVNI

Cada versão tem a sua seção; o texto da versão publicada aparece no aviso de atualização e na página da Release no GitHub.

## 1.2.4

- Pranchas aceitam observações por folha e mantêm o campo de data do carimbo livre. A assinatura institucional em duas linhas acompanha a logo em todas as páginas.
- O acompanhamento preserva a posição de leitura e respeita as preferências de animação.

## 1.2.3

- A emissão preserva a área visível de pranchas que chegam sem recorte ou com recorte zerado. A leitura do nome do arquivo reconhece também “Peticao” sem acentos.

## 1.2.2

- A prévia e a emissão de pranchas usam a mesma área da folha para posicionar as observações. A certificação aceita páginas com caixa de recorte zerada.
- O acompanhamento da análise mostra achados por item, contadores e progresso por etapa. A retomada recupera o tempo ativo e permite escolher a IA.
- Os ajustes do ChatGPT incluem a rota Codex-LB para o modo conta.

## 1.2.1

- A busca do Editor exibe resultados abaixo do campo desde a primeira letra, inclusive quando nenhum relatório está aberto.
- O indicador “Compondo as folhas…” mantém o anel e a mensagem dentro da borda colorida.
- Emitir Processo Aprovado abre o seletor de PDFs diretamente. A prévia mostra o visto do analista e permite adicionar outros carimbos antes da assinatura.
- A criação de processos aceita identificadores com letras e zeros iniciais. Os navegadores internos do portal passam a aceitar arraste de arquivos.

## 1.2.0

- Em Relatórios, “Emitir Processo Aprovado” abre a seleção de pranchas, a colocação do carimbo e a escolha das observações. A emissão preenche petição e data e usa a assinatura configurada do analista.
- A certificação de emissão local fica no limite inferior direito da última página. O PDF inclui a via assinada pelo analista. O certificado público acompanha o aplicativo. Nos demais PCs, a primeira emissão instala a chave pelo pacote protegido da Engenharia e pede a senha de inscrição.
- A impressão dos despachos antigos com o campo de assinatura do Editor prepara uma via temporária imprimível.

## 1.1.2

- A assinatura visível dos novos despachos integra o conteúdo imprimível do PDF, inclusive quando a impressão omite anotações.

- Os controles do cabeçalho recebem cliques em uma região interativa que acompanha a escala da janela.
- O número do processo pode ser digitado sem limite no campo. A lista reconhece pastas com prefixos PROC. e PROCC.
- Adicionar exigência abre uma caixa de texto com confirmação e cancelamento.
- O acompanhamento mostra segundos no primeiro minuto e volta a usar o contorno arco-íris durante a análise.

## 1.1.1

- O cartão Gemini Web reconhece a sessão da Conta Google e mostra o e-mail quando o site o informa.
- Fechar a janela do Gemini mantém a sessão disponível enquanto o editor estiver aberto.
- A análise pelo Gemini Web depende dos modelos e níveis exigidos pelo perfil do kit. O editor informa quando o modelo necessário está indisponível.

## 1.1.0

Terceira IA no Novo relatório: **Analisar com o Gemini** (o app mostra "Versão 1.1"). O ChatGPT e o Claude continuam como estavam.

- **Analisar com o Gemini:** terceiro botão em Novo relatório › Analisar, ao lado do ChatGPT e do Claude. Usa o mesmo processo, os mesmos anexos e o mesmo pedido, mostra as seis etapas e o andamento, pode ser cancelado, continua de onde parou e abre o relatório no fim, como os outros dois.
- **Plugin análise Google (kit PMVNI):** a análise é feita pelo plugin novo do kit, `analise-google` 1.0.0, com as mesmas seis etapas, as 28 seções, as evidências, os quatro validadores e o PDF oficial do análise pro. Ele roda pelo Antigravity CLI (`agy`) do Google, que lê os arquivos e roda as ferramentas neste computador. Instale ou atualize o kit pelo `INSTALAR_KIT_PMVNI.cmd`.
- **Cartão Gemini em Ajustes › Inteligência artificial:** escolha **Conta Google** (login do Antigravity, cota do plano) ou **Chave de API do Google AI Studio**. A chave fica guardada protegida pelo Windows, só para o seu usuário, e nunca aparece na tela, em registros ou no `ajustes.json`. "Testar" confere o Antigravity, o login, o plugin, o Python e, no modo chave, a própria chave na API do Google (sem gerar texto nem gastar cota).
- **Perfil da chave escolhido por você:** o Google não informa pela chave se o projeto tem créditos. Com a chave, você escolhe **API com créditos** ou **API gratuita**. A gratuita só serve para processos fictícios (o Google pode usar o conteúdo enviado para melhorar os produtos dele) e pede essa confirmação a cada análise. O plano Google AI Ultra não inclui sozinho créditos pagos da API.
- **Modelos e níveis por etapa:** o plugin escolhe sozinho, e o cartão mostra a lista do perfil em uso.

  | Etapa | Conta Google e API com créditos | API gratuita (só testes fictícios) |
  |---|---|---|
  | Preparo | Gemini 4 Hydrogen, high | Hydrogen, high |
  | Análise | Gemini 4 Carbon, high | Hydrogen, xhigh |
  | Auditoria técnica | Gemini 4 Carbon, high | Hydrogen, xhigh |
  | Produção do relatório | Gemini 4 Hydrogen, medium | Hydrogen, medium |
  | Auditoria cruzada | Gemini 4 Argon, high | Hydrogen, xhigh |
  | Revisão final | Gemini 4 Carbon, high | Hydrogen, xhigh |

- **Conferência antes de começar:** o app confere o Antigravity, a autenticação e o modelo e o nível de cada etapa. Se faltar algum, a análise não começa e a mensagem diz o perfil e a etapa.
- **Sem troca automática:** modo, perfil, modelo e nível ficam fixos do início ao fim da análise, inclusive na retomada. Se a cota acabar, a chave for recusada ou um modelo ficar indisponível, a análise pausa ou para com o motivo e a etapa, guarda o que já foi feito e continua depois com "Continuar a análise", sem repetir as etapas concluídas. Nenhuma etapa é rebaixada para um nível menor.
- **O que ainda depende de confirmação com a sua conta ou chave:** a partida, a leitura de arquivos, a gravação e a execução dos scripts do kit pela Conta Google foram conferidas neste computador com o Antigravity 1.5.10. Uma análise completa de seis etapas pelo Gemini e o modo chave de API ainda não foram executados com credenciais reais; foram testados com respostas simuladas. O limite de saída de 131.072 tokens das etapas de análise e auditoria técnica não é aplicado: o Antigravity não oferece esse ajuste (o limite de tempo de 2.400 s dessas etapas, pela conta, é aplicado).

## 1.0.0

Primeira versão 1.0 do Editor de Relatórios PMVNI (o app mostra "Versão 1.0").

- **Estabilidade e publicação:** encerramento mais seguro do visualizador em segundo plano e validação de cada projeto de testes antes de distribuir uma atualização. A publicação confere que o executável corresponde à build aprovada.
- **Visualizador de documentos dos Sites da Prefeitura:** no Essencial (Minha mesa › protocolo › visualizar documento), o documento abre numa janela própria e carrega de verdade: páginas, zoom, imprimir e baixar funcionam, e a página do protocolo continua aberta na janela principal. Antes, o visualizador abria cinza, com campos como {{DATA.DOCUMENTO.NOME}} sem preencher, porque a janela nova do site era aberta no lugar da página do protocolo e perdia a ligação com ela.
- **Brilho arco-íris só quando há trabalho:** o halo em pílula não fica mais aceso com o app parado (Imóveis, Sites da Prefeitura, temas claro e escuro). Ele apaga ao terminar, cancelar ou dar erro, ao trocar de módulo e quando o quadro sai de vista; a animação e o relógio dela também param, e os cantos dos quadros voltam ao original.
- **Cabeçalho em pílulas:** Desfazer e Refazer ficam numa pílula só, com ícones e divisória (dicas com Ctrl+Z e Ctrl+Y); Anterior e Próxima ficam em outra pílula, com cantos redondos como a pesquisa. Sem brilho decorativo; segue o destaque do Windows e o tema.
- **Pesquisa geral em tempo real:** enquanto você digita, a lista abaixo da caixa mostra o que o app tem com o termo, organizada em Arquivos, Processos, Legislação, Configurações, Funções e Módulos, com ícone, categoria, destino e o trecho encontrado em negrito. Agora acha também os documentos das pastas dos processos (DOCUMENTOS, PROJETO e a raiz do processo). Setas escolhem, Enter abre, Esc fecha, clicar fora fecha; sem resultado, um aviso claro.
- **Filtro de processos do Novo relatório:** "Filtrar por número ou requerente" passou a funcionar com número completo ou parcial ("15004", "1500", "0007"), número com ano em qualquer formato ("15004/2026", "15004-2026", "15.004/2026"), ano sozinho e nome parcial, sem diferença de acentos ou maiúsculas. Mostra quantos processos achou (ou que não achou nenhum), mantém o processo já escolhido e devolve a lista inteira ao limpar. Enter ou ↓ abre a lista filtrada. O mesmo filtro vale na lista de Processos.
- **Despacho: site da Prefeitura inteiro no rodapé da prévia:** a linha "CEP … CNPJ … - www.vendanova.es.gov.br" saía cortada à direita na prévia. Agora ela quebra em duas linhas como no Word e no PDF, e o rodapé sobe o necessário, sem mudar o texto oficial, a folha A4 ou o corpo do despacho.
- **Analisar com o Claude pela Anthropic API:** com "Usar chave de API" marcado no cartão do Claude, o análise pro (1.6.4 ou mais novo) usa de fato a chave guardada, com os créditos e limites da organização no Anthropic Console, sem gastar a cota do Claude Max. Com "Usar minha conta", segue pela assinatura do Claude, como antes.
- **Sem troca automática entre conta e API:** o app não converte mais o modo chave em conta. Se o análise pro instalado não aceitar a API, a análise não começa e o app explica o motivo.
- **Falhas da API com nome próprio:** saldo insuficiente pausa a análise, chave recusada a interrompe e limite de uso faz a análise esperar e tentar de novo. Em nenhum desses casos ela passa para a conta.
- A chave vai só para a partida da análise; o acompanhamento roda sem ela. O registro da execução informa a autenticação usada (conta ou API), nunca a chave.

## 0.6.8

- **Acompanhamento da análise redesenhado:** resumo no topo (situação, etapa atual e quantas etapas terminaram), as seis etapas com ícones e estados (concluída, em andamento, pendente e erro), atividades em frases naturais com ícone, situação e horário ("Documentos do processo identificados", "Legislação municipal consultada"…), principais descobertas marcadas como preliminares ou verificadas, próxima etapa e uma seção recolhível "Detalhes da execução" com os dados técnicos. Em janela estreita, as colunas se empilham.
- **Retomar ao reabrir:** ao fechar o app no meio de uma análise do ChatGPT, ela é pausada (não mais cancelada). Ao abrir de novo, o app pergunta se você quer retomar de onde parou e com qual IA: Claude ou ChatGPT. O mesmo vale para uma análise do Claude que parou com o app fechado.

## 0.6.7

- **Brilho arco-íris:** todo contorno de "trabalhando" agora é uma pílula de cantos redondos, no mesmo padrão da pílula da análise.
- **Indicadores de carregamento:** as barras que corriam de um lado a outro viraram uma pílula com o anel giratório e o texto centralizado (Imóveis, Acompanhamento, pasta QGIS em Ajustes, Sites e janela da prancha).

## 0.6.6

- **Análise em andamento:** o indicador da barra de título virou uma pílula maior, em duas linhas, com cantos redondos e o brilho arco-íris; a pesquisa e os botões ao lado não cortam mais. O brilho não aparece mais em volta de fileiras de botões.
- **Andamento da análise:** o registro mostra o que o ChatGPT está fazendo em frases ("Consultando a legislação", "Lendo o texto extraído dos documentos"…), com ícone para cada ação; as mensagens do ChatGPT aparecem em destaque.
- **Continuar a análise:** se o app fechar de repente ou a cota do ChatGPT acabar, a análise pode continuar de onde parou (botão "Continuar a análise", também oferecido ao reabrir o app).

## 0.6.5

- **Sites da Prefeitura (Essencial, GPI, Alfresco):** o login usado é só o guardado em Ajustes. O navegador do app não preenche mais um usuário antigo que tenha guardado sozinho; sem login guardado, o campo fica vazio.
- **Despachos e relatórios:** campo do profissional vazio em Ajustes (profissão, registro etc.) sai em branco, sem o texto do modelo.

## 0.6.4

- **Despachos:** os botões "Despacho de correção" e "Despacho de aprovação" da tela inicial não ficam mais cortados.
- **Sem processo:** o botão libera o modelo na hora para preencher na folha, sem caixa de diálogo; o despacho é gravado em Documentos › Despachos sem processo.

## 0.6.3

- **Boletim cadastral, "Onde salvar o boletim?":** basta clicar na opção (o processo achado, outro processo, criar novo processo ou só abrir); embaixo fica só "Cancelar".
- **Sócios achados de verdade:** os nomes vêm dos vínculos do cadastro (ex.: "FULANO DE TAL, BELTRANA DE TAL"), sem o CPF/CNPJ; antes a busca por sócios não encontrava nenhum.
- **Profissional responsável:** os exemplos dos campos agora são fictícios, e há o botão **Salvar dados** (que confirma a gravação).
- **Novo despacho:** embaixo de "Processo", os botões **Novo processo…** (cria a pasta e já a escolhe) e **Sem processo…** (o despacho não fica ligado a um processo: você escolhe a pasta onde ele será gravado e preenche protocolo e requerente na folha).
- **Tipo de obra:** além de obra nova, alteração e ampliação, o campo **Outro (digitar)** aceita qualquer tipo (ex.: regularização de obra).
- **Pedir à IA (despacho):** o botão de enviar volta a ser um círculo perfeito.
- **Acompanhamento:** as etiquetas de "Status da aprovação" ficam com a mesma largura, alinhadas com os números.
- **Atualizações não perdem ajustes:** opções gravadas por outra versão do editor continuam no arquivo, e a cada versão nova fica uma cópia do arquivo anterior (ajustes.anterior.json).

## 0.6.2

- **Ajustes › Despachos e PDF › Profissional responsável:** nome, profissão, registro (CAU/CREA) e matrícula de quem assina. Preenchendo, o bloco abaixo da linha de assinatura dos despachos e a assinatura no fim dos relatórios gerados passam a usar esses dados (cada profissional no seu computador); campo vazio mantém o texto do modelo.
- **Novo processo** também na faixa de **Imóveis** e de **Processos**: cria a pasta do processo (número, ano e requerente) com DOCUMENTOS e PROJETO.
- **Boletim cadastral: você confirma onde salvar.** Antes de gerar, o app mostra o processo achado (já marcado) e os outros prováveis, com o motivo de cada um; dá para escolher **Outro processo…** (qualquer pasta) ou **Criar novo processo…** (com o contribuinte já preenchido como requerente).
  - **Sócios também contam:** quando o imóvel é "FULANO E SÓCIO (A)(S)", os nomes dos sócios do cadastro entram na busca do processo (requerente da pasta e nomes nos documentos).
  - **Só abrir, sem salvar:** a mesma janela tem a opção de só abrir o boletim, sem ligar a nenhum processo (fica numa cópia temporária).

## 0.6.1

- **Correção do brilho "Trabalhando":** o arco-íris fica só no contorno; antes cobria o botão, o balão ou a barra inteira.
- **Sites da Prefeitura: o login agora fica guardado de verdade.** O app guarda a sessão também no Essencial e no GPI (que mudam de tela sem recarregar), a cada minuto e de todos os endereços por onde o site passa. Com login guardado, entra sozinho mesmo quando a tela de login aparece um pouco depois de a página abrir; o usuário é mesmo opcional (basta a senha, se o site já traz o usuário), e "Guardar login" já entra no site aberto.
- **Legislação: leis repetidas são ignoradas.** Ao anexar, um PDF com o mesmo nome (ou título) de uma lei já anexada, ou com o mesmo conteúdo, é pulado sem erro; o aviso diz quantas foram ignoradas.

## 0.6.0

- **Sites da Prefeitura (Essencial, GPI, Alfresco): login que não expira.** Entre uma vez pelo site e o app lembra o acesso até você clicar em **Sair** (Ajustes › Fontes de dados › Sites da Prefeitura). O app mantém a sessão ativa enquanto está aberto e, se você guardar usuário e senha (protegidos pelo Windows), entra sozinho quando o site pedir login de novo.
- **Imóveis: Gerar boletim cadastral.** Ao lado de "Abrir relatório", cada linha tem o botão que gera o Boletim de Cadastro Imobiliário oficial do GPI direto em PDF, sem abrir o site: o app usa o GPI escondido, com o seu login, pesquisa a inscrição municipal (Igual), confere a ficha em Consulta e recebe o PDF do próprio GPI. O arquivo vai para a pasta **DOCUMENTOS do processo do imóvel** como "BCI - CONTRIBUINTE - inscrição.pdf" e abre numa janela com salvar e imprimir; o aviso traz **Abrir PDF** e **Abrir pasta**.
  - **Processo do imóvel achado sozinho:** pela inscrição da consulta QGIS da análise (ou o mesmo lote), pelo requerente da pasta e pela inscrição ou pelo nome nos PDFs de DOCUMENTOS e PROJETO (escritura, matrícula, projeto). Empate: você escolhe. Sem processo: o app avisa para criar a pasta do processo e não gera.
- **"Trabalhando":** o mesmo brilho arco-íris do WinStatusBar aparece no contorno de tudo o que está processando (IA, leituras, pesquisas, o boletim), até o resultado.
  - **Resiste às atualizações do GPI:** usa só o que aparece na tela do GPI, nunca as chamadas internas do site. Se algo mudar, o aviso diz em que passo parou e oferece **Abrir no GPI**; em Ajustes › Fontes de dados, **Testar o boletim** confere o fluxo inteiro com uma inscrição.
- **Chave de API do Claude liberada.** "Usar chave de API" vale para a Claude API na revisão, nos ajustes e nos resumos (Modelos de IA). O análise pro do kit continua pela conta do Claude.
- **Correção:** trocar a "IA da revisão e dos ajustes" fechava o app.
- **Relatório:** o zoom inicial e o "Ajustar largura" não cortam mais a folha (descontam a margem das etiquetas e se refazem ao redimensionar a janela); a lista de seções começa mais estreita.
- **Lista de seções:** cada item mostra embaixo do título a etiqueta da situação no mesmo visual do relatório ("● NÃO ATENDE"), "RESULTADO ORIGINAL ALTERADO" quando o item foi mudado e a folha como "FOLHA 01". Só a etiqueta usa as cores da situação; o resto segue as cores do tema.

## 0.5.1

- **Imóveis:**
  - **Mapa maior:** formato 4:3 e largura total, com a legenda numa linha logo abaixo.
  - **Cotas sempre visíveis no lote escolhido:** lados do terreno, rio e APP ("Rio → terreno · APP"), DNIT e largura da rua, sem nada sobreposto. Conferido em 19 lotes reais.
  - **Nomes de rua com acento certo:** "Rua João Minete", "Altoé"; antes saía "JOÃƒO".
- **Botões:** todos os coloridos têm o mesmo formato, com fundo verde suave, borda fina e brilho.
- **Barra de título:** "Anterior / Próxima" não corta mais em telas menores.
- **Legislação:** o selo do tipo de lei ("Decreto", "Lei"…) fica em verde.
- **IA:** login vencido do Claude Code ou do Codex agora aparece com uma mensagem clara, dizendo o que fazer.
- **Kit PMVNI:** o PDF dos despachos feito pelo script do kit também não espera mais a impressora (de cerca de 55 s para 5 s).
## 0.5.0

### Despachos
- **PDF em 2 segundos e sem a janela "Aguardando conexão de impressora":** o LibreOffice não consulta mais a impressora do Windows ao converter o Word em PDF (antes levava cerca de 50 s). O PDF sai igual.
- **Pasta de hoje:** o despacho vai sempre para a pasta DESPACHO NN com a data do dia. A pasta da análise só é reaproveitada se for de hoje.
- **Gerar despacho** abre a pasta onde ele foi gravado, com o arquivo selecionado.
- **Ajustar com IA:** botão novo na caixa "Texto da exigência" para melhorar o texto antes de gravar.
- **Correção automática:** não continua mais com um rascunho antigo quando você escolhe "Continuar editando".

### Relatórios
- **Exportar PDF** grava o PDF ao lado do relatório (.rpmvni), na pasta do processo, e abre a pasta com ele selecionado.
- **Índices urbanísticos:** ao mudar um valor à mão, as contas que dependem dele (taxa, coeficiente, situação, área do lote no diagrama) se refazem na hora; um Desfazer volta tudo.
- **Diagrama dos índices:** um item pedido para a volumetria vai para a volumetria (lista e desenho), não mais para a planta.
- **Figuras:** "Adicionar figura…" no menu Figuras abre o PDF do projeto. No editor de figuras, clicar sem arrastar cria a região, os avisos ficam sempre à vista e o app explica quando um número desenhado na imagem não pode ser removido.
- **Tudo que se clica no relatório** mostra o contorno azul ao passar o mouse, como os itens.
- **Filtros das seções:** mais compactos, com a bolinha da cor de cada situação.

### Acompanhamento de processos
- **Colunas:** escolha quais aparecem (botão "Colunas") e mude a largura arrastando a borda do título; o app lembra.
- **Status e etiquetas** centralizados nas colunas; Status como a etiqueta ATENDE.
- **Colunas fixas** com a mesma cor da tabela e um traço de separação.

### Imóveis
- **Mapa sem sobreposição:** rótulos, cartão do lote e cotas procuram lugar livre por prioridade; o que não cabe some.
- **Cotas:** lados do terreno, faixa de domínio do DNIT (40 m do eixo, a confirmar) e distância do eixo ao terreno, e largura da rua medida na base. Botão "Cotas" para ligar e desligar.
- **Detectar automaticamente** (padrão) na pesquisa: reconhece inscrição, número, rua, bairro, loteamento ou nome.
- **"Sem lote no mapa":** inscrições com outro formato ou renumeradas agora acham o lote; inscrições baixadas aparecem em cinza, com o link para a inscrição atual.
- "Proprietário não identificado" aparece certo (antes saía "Proprietario Identificad").

### Todo o app
- **Imprimir (Ctrl+P)** na faixa de Relatórios, Legislação, Processos, Despachos, Imóveis e dos sites: abre a impressão com a lista de impressoras e a opção "Salvar como PDF". No relatório, sai igual ao PDF exportado.
- **Pesquisa na barra de título:** acha ajustes, funções, processos, arquivos, leis e linhas do Acompanhamento.
- **Modelos de IA** (Ajustes › Inteligência artificial): escolha a IA (Codex, Claude Code ou as APIs), o modelo e o nível de raciocínio usados na revisão e nos ajustes, com "Atualizar lista".
- **Ícones coloridos** na faixa de opções, pelo que cada comando faz.
- **Botões coloridos com brilho**, como no WinStatusBar, e botões das telas vazias no mesmo padrão.
- **Trilho:** a barrinha do módulo ativo não fica mais por cima do texto.
- **Recentes** em lista com ícone, nome e pasta.

## 0.4.0

### Gravação mais segura na planilha de acompanhamento
- **Planilha aberta no Excel:** a alteração não se perde mais. Ela fica guardada e aparece no aviso "aguardando gravação". O app grava sozinho quando a planilha for liberada, ou na hora em "Gravar agora", e isso vale mesmo que o editor seja fechado e aberto de novo.
- **Gravação segura:** a planilha é gravada num arquivo temporário, conferida e só então trocada. Uma falha no meio nunca deixa o arquivo pela metade.
- **Excluir processo:** não estraga mais as fórmulas da planilha (como "Dias sem atualização").
- **Preenchimento automático:** nunca grava por cima do que alguém digitou enquanto ele buscava.
- **Editar processo:** não regrava em maiúsculas os campos que você não mexeu e não muda a data à toa.
- **Aba da planilha:** é escolhida pelo nome e lembrada. Mudar a ordem das abas no Excel não troca mais a aba.
- **Cópias de segurança:** ficam numa pasta por planilha e nunca se sobrescrevem.

### Acompanhamento de processos
- **No notebook (1366 × 768):** o resumo fica compacto e se recolhe sozinho para a tabela aparecer, e o painel do processo abre por cima com a altura toda. O botão "Resumo" lembra a sua escolha.
- **Tabela:** Status logo depois do Processo, Nº e Processo fixos ao rolar para o lado, e colunas sem nenhum dado fora da tabela (avisadas no rodapé).
- **Resumo:** a pizza tem cores mais distintas, cada status tem a sua bolinha de cor e status sem processos saem do resumo.
- **Nomes e pesquisa:** os nomes aparecem iguais na tabela e no painel ("Enviado ao Requerente"). A pesquisa da barra de título filtra a planilha.
- **Teclado:** Enter edita, Delete exclui e a tecla Menu abre o menu do processo.
- **Comandos:** "Novo processo" é o botão principal da faixa, e "Escolher planilha…" tem um nome só.

### Interface
- **Trilho:** mais largo, com "Acompanhamento de processos" inteiro em duas linhas. Os módulos rolam em janelas baixas, e Ajustes fica sempre à vista.
- **Cor de destaque:** o verde institucional em todo o app, nos dois temas, no lugar da cor do Windows.
- **Barra de título:**
  - mostra só o que vale em cada módulo;
  - o processo aberto e o "Salvo" só aparecem em Relatórios;
  - em telas estreitas, os botões ficam só com o ícone.
- **Editor:**
  - em janelas estreitas, o painel Propriedades recolhe quando nada está selecionado;
  - "Ajustar largura" não deixa mais rolagem lateral;
  - a lista de seções mostra uma linha por item; Ajustes › Relatórios e revisão › "Lista de seções" escolhe entre Compacta e Confortável.
- **Novo relatório:**
  - os anexos de um compactado aparecem agrupados;
  - o destino aparece inteiro;
  - "Cancelar abertura" para a abertura de um compactado grande;
  - o compactado com nomes repetidos é aceito.
- **Tema:** as cores seguem o tema escolhido no app, mesmo quando é diferente do tema do Windows.
- **Textos mais claros:**
  - "Importar análise da IA…";
  - "21 folhas";
  - avisos da IA que dizem o que aconteceu;
  - "Substituir" ao trocar o despacho pelo relatório;
  - "Gravar só o Word (.docx)";
  - "Atualizar/Copiar diagnóstico".
- **Leitor de tela:** a recuperação de alterações não descarta mais com Esc, e o leitor de tela anuncia o estado de gravação e as mensagens que mudam.

### Fica para depois
- Trocar os cartões e as etiquetas antigos pelos estilos novos em todas as telas.
- Foco visível na caixa de instrução da IA do Novo despacho.
- Revisar os tamanhos fixos com o "Tamanho do texto" do Windows acima de 100%.

## 0.3.0

### Acompanhamento de processos (novo módulo no trilho)
- Página nova que mostra a planilha padrão de acompanhamento (ACOMPANHAMENTO DE PROCESSOS) com todas as colunas: Nº, datas, prioridade, processo, requerente, local, atualização, status, aprovação, profissional, dias sem atualização, observação e as demais.
- As listas (status, prioridade, aprovação) e as cores da formatação condicional vêm da própria planilha; os dias sem atualização são calculados como no Excel, com as mesmas faixas de cor.
- Cabeçalho de resumo com a contagem de cada status (clicar filtra) e gráfico de pizza do andamento, mais a contagem de aprovados e não aprovados.
- Novo processo, editar (todas as colunas), mudar status/aprovação/prioridade pelo menu "…", excluir, filtrar, buscar, ordenar por qualquer coluna e painel lateral com os dados do processo.
- Tudo o que muda no app é gravado na planilha, mexendo só nas células alteradas (formatação, listas, tabela, fórmulas e brasão continuam iguais). Antes de gravar, uma cópia de segurança vai para a pasta de dados do editor. Com a planilha aberta no Excel, o app avisa e oferece "Tentar de novo".
- Mudou a planilha no Excel: a página relê sozinha.
- Local e autor do projeto (Profissional) preenchidos sozinhos quando estão vazios: do relatório analisado (capa: Localização e Autoria), da consulta QGIS do processo (endereço no cadastro) e do carimbo das pranchas. Nunca troca o que foi digitado; o painel mostra de onde veio.
- Importar outra planilha no mesmo modelo, abrir no Excel e exportar a lista filtrada em CSV.
- Ajustes › Fontes de dados: escolher, verificar e abrir a planilha, e ligar/desligar o preenchimento automático.

### Ajustes › Sobre e suporte
- Página no padrão do WinStatusBar: Atualizações, Suporte (contato, copiar informações, abrir pasta de dados), Diagnóstico (relatório com versões, pastas e memória, com Atualizar e Copiar) e Sobre (versão e compilação, desenvolvedor, sistema, runtime e créditos). Ajuda › Sobre abre esta página.

### Correções
- Mapa de localização: a etiqueta vermelha "LOTE nº" sai de cima do lote e fica encostada nele, fora da projeção.
- Leitura padronizada da legislação: o menu do botão direito no texto segue o padrão dos menus do app (Copiar, Copiar parágrafo, Selecionar tudo).

## 0.2.1

- Novo relatório aceita arquivos compactados (.zip, .rar e .7z): o editor abre o arquivo e cada arquivo de dentro entra como anexo, com o destino pela regra do kit (matrícula, RRT/ART, requerimento, certidões e outros documentos em DOCUMENTOS; pranchas, DWG e o resto no projeto). Cada linha mostra de qual compactado e pasta o arquivo veio, e o destino continua podendo ser trocado. Compactado com senha, corrompido ou grande demais (mais de 4 GB ou 3.000 arquivos) é avisado e fica de fora.

## 0.2.0

### Janela e cabeçalho
- O editor não fecha mais ao passar a janela de um monitor para outro com escala diferente.
- Um erro inesperado na tela não fecha mais o editor: aparece um aviso e o trabalho aberto continua.
- Sumiu a dica "Ctrl+O" que ficava solta sobre a janela.
- Cabeçalho revisto: marca da Prefeitura centralizada, com mais respiro; pesquisa em pílula com lupa; Desfazer/Refazer e Anterior/Próximo agrupados no mesmo formato, com espaços iguais dos dois lados.

### Relatório
- Na ficha da Localização, rótulos longos ("INSCRIÇÃO IMOBILIÁRIA", "ÁREA DO LOTE (MATRÍCULA)") quebram linha em vez de invadir o valor. Valia para qualquer relatório com rótulo longo.
- A etiqueta longa do título de seção (zona, tipo de edificação) não sai mais da folha.
- Item excluído aparece como "ITEM EXCLUÍDO NN", com uma linha vermelha atravessando a folha onde ele estava (só na tela; o PDF não muda).

### Revisão do relatório
- "Rever com a IA" em todo item e em toda linha de tabela (índices, ambientes, chamadas): a justificativa é a instrução.
- A IA da revisão usa o ChatGPT Terra 6.1 com esforço baixo (ajustável em Ajustes › Inteligência artificial), responde em segundos, mostra o tempo e pode ser cancelada.
- O texto ajustado pela IA mantém o tamanho do original e fica coerente com a situação escolhida; mudar a justificativa avisa para reajustar o item inteiro.
- Editar à mão o valor de um índice (ex.: recuo de 3 m contra o mínimo de 3,00 m) refaz a situação e o texto da diferença na hora, sem IA.
- Novo item: cria um item de análise com os mesmos campos, logo depois do selecionado.
- Painel Propriedades em cartões (Estado, Esquadrias, Conteúdo, Histórico), com os campos editáveis no próprio painel.
- Esquadrias por ambiente, separadas em Janelas e Portas, com largura e altura editáveis e a conta (área, vão total, mínimo e resultado) refeita a cada tecla.
- Adicionar janela, Adicionar porta e Adicionar ambiente (nome, área do piso, divisor e pavimento); tudo se desfaz.
- Pedir à IA para acrescentar algo na volumetria cria a chamada numerada nova (ex.: 7), com a marca no desenho.
- Botão Figuras na Página Inicial, para abrir direto o editor da figura.
- Os botões principais de cada painel aparecem na cor de destaque.

### Despachos
- Correção automática: um clique monta o despacho com todos os itens que não atendem do relatório (situação já revisada) e vai direto para a conferência.
- Assinatura digital dos despachos (certificado .pfx/.p12 ou do Windows), com a assinatura visível acima do nome; senha pedida a cada vez ou guardada cifrada neste computador (Ajustes › Despachos e PDF).
- O texto não invade mais o cabeçalho da 2ª folha em diante, e o fecho (data e assinatura) fica inteiro na mesma folha.
- A caixa de edição da prévia cabe no cartão e na janela, com altura conforme o texto.
- Painel do Novo despacho em cartões, no mesmo padrão do painel Propriedades.

### Processos
- A página do processo ganhou Classificar (nome, data, tipo), Agrupar (etapa, tipo de arquivo, data) e Visualizar (Lista ou Miniaturas). A escolha fica lembrada.
- Em Miniaturas, os arquivos aparecem numa grade como a do Explorador, com a prévia de cada arquivo e grupos que recolhem.
- Botão direito, Enter e duplo clique nos arquivos: Abrir, Mostrar no Explorador, Copiar caminho.

### Imóveis (consulta à base QGIS)
- O mapa de localização segue o do kit: balão com os dados, lote em vermelho, APP, faixas da BR-262, zonas, ruas, rio e confrontantes, com escala e norte.
- O relatório do imóvel tem o aspecto do relatório de análise: faixa do processo, quadros, mapa com legenda ao lado e a ficha em quatro grupos com selos.
- Corrigido: o perímetro da AUC não era lido e o relatório dizia sempre "fora das AUC".
