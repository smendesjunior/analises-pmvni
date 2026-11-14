# Entrega da Etapa 1: fidelidade documental

Editor de Relatórios Técnicos da PMVNI, versão da Etapa 1 (14/11/2026).
Este texto diz o que já funciona, o que foi testado e o que ainda falta.

## O que funciona

- **O programa**: um único arquivo, `Aplicativo\Editor de Relatorios PMVNI.exe`. Não precisa instalar, nem ser
  administrador. Usa o WebView2 que já vem com o Windows 11. A primeira abertura demora uns 15 segundos; as seguintes são rápidas.
- **Abrir um relatório**: o botão **Abrir** carrega os dados que a análise da IA
  gerou. O programa monta o relatório com o mesmo modelo oficial (revisão 10) usado pelo kit.
- **Ver o relatório**: o centro da janela mostra o relatório inteiro, folha A4 sobre folha A4, em rolagem contínua, igual ao que sai no PDF.
  Nesta etapa a tela é **somente leitura**: não há edição.
- **Navegar**: a coluna da esquerda lista capa, seções e itens; clicar leva à folha certa. O rodapé mostra "folha X de Y".
- **Zoom**: de 50 % a 300 %, com os botões + e −, **Ajustar largura** e **Página inteira**. O zoom não refaz o relatório e não muda o PDF.
- **Exportar PDF**: o botão **Exportar PDF** grava o PDF com o mesmo número de folhas da tela e um recibo (impressão digital do HTML e do PDF).
- **Proteção contra erro**: se um bloco do relatório não cabe numa página, o programa avisa, marca a folha com uma faixa vermelha e
  **recusa exportar** em vez de gerar um PDF errado. Exportação final com campos `[ ]` em aberto também é recusada.
- **Tema escuro**: a janela acompanha o tema do Windows; as folhas continuam brancas.

## O que foi testado

Tudo abaixo foi rodado nesta entrega, no `tools\build-release.ps1` e em `tools\fidelidade.py tudo`.

- **Testes automáticos do programa**: 139 testes, todos aprovados (49 da montagem do relatório, 90 da tela, do paginador, da
  exportação e da segurança). Eles rodam antes de qualquer publicação; se um falhar, nenhum executável novo é gerado.
- **Autoteste do executável final**: o `.exe` publicado foi aberto duas vezes (primeira abertura e segunda) com `--autoteste`, fora do
  ambiente de desenvolvimento, e aprovado nas duas.
- **Comparação com o gerador oficial do kit** (7 relatórios: 5 de teste do corpus, o `dados_modelo` e 1 processo real, que não
  é citado nem copiado aqui):
  - **V1 (HTML idêntico)**: 7 de 7 aprovados. O HTML do programa é igual, byte a byte, ao do kit.
  - **V2 (tela × PDF do programa)**: 7 de 7 aprovados. O texto de cada folha é o mesmo do PDF; a maior diferença visual
    foi 1,42 % dos pontos (limite 2,0 %).
  - **V3 (PDF do programa × PDF do kit)**: 7 de 7 aprovados. Mesmo número de páginas e mesmo texto em cada página; a maior
    diferença visual foi 1,66 % (limite 2,0 %).
  - **V4 (todos os casos)**: aprovado. Um relatório propositalmente grande demais (`estouro`) é recusado na exportação, como deve.
- **Diferenças conhecidas e fixadas** (documentadas em `docs\fidelidade.md`): em alguns relatórios o kit corta de 1 a 12 letras além da
  borda direita da área útil, ou deixa 1 caractere invadir a margem de baixo; o programa não. O teste reprova se surgir qualquer outra diferença.
- **Roteiro da tela** (`docs\roteiro-etapa-1.md`, resultado em `docs\roteiro-etapa-1.resultado.json`): rolagem, zoom, navegação,
  exportação, estouro e tema escuro exercitados pelos mesmos caminhos dos botões.
- **Limpeza**: depois do build, `bin`, `obj`, `artifacts`, `tests\saida`, caches do Node e extrações antigas do executável em
  `%TEMP%\.net` são apagados (766 MB liberados no último build).

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
