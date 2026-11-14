# Entrega da Etapa 1: fidelidade documental

Editor de Relatórios PMVNI, versão da Etapa 1 (14/11/2026).
Este texto diz o que já funciona, o que foi testado e o que ainda falta.

## O que funciona

- **O programa**: um único arquivo, `Aplicativo\Editor de Relatorios PMVNI.exe`. Não precisa instalar, nem ser
  administrador. Usa o WebView2 que já vem com o Windows 11. A primeira abertura é mais lenta, porque o programa se desempacota; as seguintes são mais rápidas.
- **Abrir um relatório**: o botão **Abrir** carrega os dados que a análise da IA
  gerou. O programa monta o relatório com o mesmo modelo oficial (revisão 10) usado pelo kit.
- **Ver o relatório**: o centro da janela mostra o relatório inteiro, folha A4 sobre folha A4, em rolagem contínua, igual ao que sai no PDF.
  Nesta etapa a tela é **somente leitura**: não há edição.
- **Navegar**: a coluna da esquerda lista capa, seções e itens; clicar leva à folha certa. O rodapé mostra "folha X de Y".
- **Zoom**: de 50 % a 300 % (limites do roteiro, `docsoteiro-etapa-1.md`), com os botões + e −, **Ajustar largura** e **Página inteira**. O zoom não refaz o relatório e não muda o PDF.
- **Exportar PDF**: o botão **Exportar PDF** grava o PDF com o mesmo número de folhas da tela e um recibo. O recibo guarda o hash do HTML e
  do PDF: uma "impressão digital" (sequência curta calculada a partir do conteúdo) que muda se qualquer letra mudar, e prova qual versão foi exportada.
- **Proteção contra erro**: se um bloco do relatório não cabe numa página, o programa avisa, marca a folha com uma faixa vermelha e
  **recusa exportar** em vez de gerar um PDF errado. Exportação final com campos `[ ]` em aberto também é recusada.
- **Tema escuro**: a janela acompanha o tema do Windows; as folhas continuam brancas.

## O que foi testado

Tudo abaixo foi rodado nesta entrega, no `tools\build-release.ps1` e em `tools\fidelidade.py tudo`.

- **Testes automáticos do programa**: 139 testes, todos aprovados (49 da montagem do relatório e 90 da tela, do paginador, da
  exportação e da segurança; contagem do `dotnet test` no build de 14/11/2026). Eles rodam antes de qualquer publicação; se um falhar, nenhum executável novo é gerado.
- **Autoteste do executável final**: o autoteste é o próprio programa se abrindo sozinho, carregando a página no WebView2 e fechando,
  só para provar que o `.exe` pronto abre. Foi feito duas vezes (primeira abertura e segunda) e só vale se o programa avisar "ok" e não
  estiver rodando como administrador. Só depois de aprovado o `.exe` vai para `Aplicativo\`.
- **Comparação com o gerador oficial do kit**, rodada em 14/11/2026 com `python -I toolsidelidade.py tudo`. As provas se chamam V1 a V4
  (critérios e limites em `docsidelidade.md`); os resultados saem em `tests\saida1.json` a `v4.json`, que a limpeza do build apaga.
  Foram 7 relatórios: 5 de teste (`tests\corpus`), o `dados_modelo` e 1 processo real (não citado nem copiado aqui).
  - **V1 (HTML igual ao do kit)**: o texto-base do relatório que o programa monta é comparado ao do gerador do kit, "byte a byte", isto é,
    letra por letra e sinal por sinal, sem nenhuma diferença. Resultado: igual nos 7 casos.
  - **V2 (tela × PDF do programa)**: cada folha vista na tela é comparada com a página correspondente do PDF. O texto tem de ser idêntico;
    a imagem pode diferir em até 2,0 % dos pontos. Resultado: 7 de 7 aprovados; a maior diferença medida foi 1,42 %.
  - **V3 (PDF do programa × PDF do kit)**: mesmo número de páginas, mesmo texto em cada página e diferença de imagem de até 2 % por página.
    Resultado: 7 de 7 aprovados; a maior diferença medida foi 1,66 %.
  - **V4 (todos os casos juntos)**: roda V1, V2 e V3 sobre os 7 relatórios. Aprovado. Um relatório propositalmente grande demais
    (`estouro`) é recusado na exportação, como deve.
- **Diferenças conhecidas e fixadas** (documentadas em `docs\fidelidade.md`): em alguns relatórios o kit corta de 1 a 12 letras além da
  borda direita da área útil, ou deixa 1 caractere invadir a margem de baixo; o programa não. O teste reprova se surgir qualquer outra diferença.
- **Roteiro da tela** (`docs\roteiro-etapa-1.md`, resultado em `docs\roteiro-etapa-1.resultado.json`): rolagem, zoom, navegação,
  exportação, estouro e tema escuro exercitados pelos mesmos caminhos dos botões.
- **Limpeza**: depois do build, `bin`, `obj`, `artifacts`, `tests\saida`, caches do Node e extrações antigas do executável em
  `%TEMP%\.net` são apagados (o build informa o espaço liberado; na última vez foram 766 MB no projeto e 396 MB em extrações antigas).

## O que fica pendente

1. **Conferência humana da janela** (não dá para o teste automático ver): acabamento visual (espaçamentos, alinhamento dos botões
   com os da janela, aparência em outros monitores), abrir e salvar pelos seletores de arquivo do Windows, sensação da rolagem e do
   zoom com mouse e teclado, e janela bem estreita. A lista completa está em `docs\roteiro-etapa-1.md`.
2. **Etapas seguintes** (plano em `docs\especificacao`):
   - Etapa 2: revisão básica (arquivo `.pmvrel`, editar itens e classificação, original × revisado, salvamento automático, desfazer).
   - Etapa 3: cálculos e diagramas vinculados.
   - Etapa 4: pranchas e marcações.
   - Etapa 5: despachos e gestão de modelos.
   - Etapa 6: instalador, associação do arquivo `.pmvrel`, robustez.
   - Etapa 7: integração ao kit (só com a sua autorização, com backup).
3. O programa só **lê** o kit PMVNI (modelos); nada no kit foi alterado. O kit não faz parte da receita de recriação do PC.

## Como gerar uma nova versão

`powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1`: roda os testes, publica, confere o `.exe`, roda o
autoteste, limpa o que sobrou e informa o espaço liberado.
