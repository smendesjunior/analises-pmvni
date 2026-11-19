# Motor local de PDFs do Editor

Na primeira abertura do Editor ou no comando instalar/atualizar do kit, o bootstrap prepara um ambiente Windows x64 por usuário em `%LOCALAPPDATA%\PMVNI\MotorPdf`. A instalação inicial requer acesso ao índice público de pacotes e à distribuição do Python gerenciado. O bootstrap fixa a versão do uv, valida o executável e instala wheels com hashes do requirements.lock. As três entradas de preparo consultam runtime.txt. Um ambiente novo só assume esse apontamento após a sincronização e a verificação de importação. O instalador guarda o apontamento anterior em runtime.txt.anterior. Uma nova abertura tenta recuperar instalações incompletas. O modo de análise continua a exigir o kit e as peças originais do processo.

Docling MCP 3.5.0 (MIT) roda pelo conversor local do pacote, isolado em subprocesso. O primeiro processamento pode baixar modelos; ambiente instalado e modelos disponíveis offline são estados diferentes. PyMuPDF 2.1 (AGPL-3.0 ou licença comercial) é instalado da distribuição do fornecedor. A Prefeitura deve avaliar os termos aplicáveis ao uso e à distribuição. O código não incorpora os binários nem os pesos ao executável do Editor.

O índice de cada preparo aponta para evidências em .pmvni-ai/cache-pdf no processo. As saídas de geometria usam pontos PDF e mantêm a escala real pendente de calibração. O Docling recebe apenas recortes de páginas selecionadas para OCR, memoriais e quadros. Os JSON completos ficam no disco. O monitor abre a pasta no painel de detalhes.

## Executores experimentais

Blueprint Pipeline e FYP-Floorplan ficam pendentes de autorização para distribuição do código e dos pesos. As referências examinadas foram ASorceror/blueprint_pipeline no commit 98268f108a2cb813c8279c7aaf99f3631df7874f e REIkd/FYP-Floorplan no commit 628e1eae5a63974bbebfe6ebef8efa52fc7727ce. Um integrador autorizado pode registrar um executável Windows de cada ferramenta em adaptadores.json, na pasta do motor, com as chaves blueprint e fyp-floorplan. Cada entrada contém executavel (caminho absoluto) e sha256 do executável. O adaptador chama o programa com --pdf, --pagina (base 1) e --saida. O programa precisa devolver um objeto JSON com sha256 do PDF, pagina e evidências localizáveis. O integrador responde pela validação das dependências e dos pesos que seu executável carregar. A execução tem limite de 120 segundos. As hipóteses permanecem pendentes de conferência.

## Distribuição

Entregar a versão do Editor com os recursos MotorPdf incorporados e atualizar a fonte dos plugins e o instalador do kit. Conferir nos PCs de destino o runtime.txt, um preparo de PDF e o acesso às evidências. A execução em um PC sem internet desde a primeira instalação depende de pré-provisionamento dos pacotes e modelos.
