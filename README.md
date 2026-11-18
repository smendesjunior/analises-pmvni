# Editor de Relatórios PMVNI

Aplicativo do Windows para a análise de projetos da Secretaria Municipal de Obras de Venda Nova do Imigrante/ES. Nele o analista:

- abre e revisa os relatórios técnicos de análise;
- pede análises à IA (ChatGPT, Claude ou Gemini) com os anexos do processo;
- emite despachos de correção e de aprovação;
- consulta a legislação e os imóveis da base QGIS;
- acompanha a tramitação dos processos na planilha padrão.

É um executável único, `Aplicativo\Editor de Relatorios PMVNI.exe`. Ele se atualiza sozinho pelas Releases do GitHub. As novidades de cada versão estão no [CHANGELOG.md](CHANGELOG.md).

## Módulos (trilho à esquerda)

| Módulo | Para quê |
|---|---|
| Relatórios | Abrir, revisar e exportar em PDF o relatório técnico (`.rpmvni`); Novo relatório pela IA |
| Despachos | Emitir despachos de correção e de aprovação no modelo do kit (Word e PDF, com assinatura digital opcional) |
| Processos | As pastas dos processos: relatórios, despachos, projeto e documentos |
| Legislação | Leis anexadas, leitura padronizada e citação |
| Acompanhamento de processos | A planilha de tramitação: resumo por status, tabela, edição e gravação na própria planilha |
| Imóveis | Consulta à base QGIS do município |
| Essencial, GPI e Alfresco | Sites do dia a dia, dentro do app |
| Ajustes | Pastas, aparência, fontes de dados, IA, Sobre e suporte |

## Guia rápido: Acompanhamento de processos

1. **Escolher a planilha.** Use "Escolher planilha…" na faixa, ou Ajustes › Fontes de dados.
   - A planilha precisa seguir o modelo padrão, com a linha de cabeçalho que tem a coluna "Processo".
   - Com mais de uma aba, escolha a aba ao lado do título. O app lembra a aba pelo nome, então mudar a ordem das abas no Excel não troca a aba.
2. **Ver o andamento.** O resumo mostra quantos processos há em cada status (clicar num status filtra), a pizza e a aprovação.
   - O botão "Resumo" mostra ou esconde essa área.
   - Em telas baixas o resumo fica compacto sozinho, para a tabela não sumir.
3. **Encontrar um processo.** A caixa de pesquisa da barra de título filtra a tabela; "Filtrar" filtra por status, prioridade, aprovação e profissional.
   - As colunas Nº e Processo ficam fixas quando a tabela rola para o lado.
4. **Mudar um processo.** Escolha a linha e use "Editar processo" (ou Enter) para todas as colunas. O menu "…" (ou o botão direito) muda status, aprovação e prioridade.
   - "Excluir processo" (ou Delete) tira o processo da planilha.
   - A data de Atualização vira hoje quando o status ou a aprovação mudam.
5. **Gravação.** Tudo é gravado direto na planilha, mexendo só nas células alteradas; formatação, listas, fórmulas e tabela ficam como estão.
   - Antes de cada gravação, uma cópia da planilha vai para a pasta de dados do app (as 15 últimas).
   - Se a planilha estiver aberta no Excel, a alteração fica guardada. O aviso "aguardando gravação" mostra o que falta, e o app grava sozinho quando a planilha for liberada (ou em "Gravar agora"), mesmo que o app seja fechado e aberto de novo.
6. **Preenchimento automático.** "Detectar local e autor" busca o local e o autor do projeto nos relatórios, na consulta QGIS e no carimbo das pranchas.
   - Só preenche célula vazia: nunca troca o que foi digitado.
   - O painel do processo mostra de onde veio cada valor.

## Guia rápido: anexos compactados no Novo relatório

1. Em Relatórios › Novo relatório, escolha o processo (passo 1) e arraste os arquivos ou use "Anexar arquivos…" (passo 2).
2. Arquivos `.zip`, `.rar` e `.7z` são abertos pelo app. Cada arquivo de dentro vira um anexo, agrupado sob o nome do compactado.
   - O destino de cada um segue a regra do kit: matrícula, ART/RRT, requerimento, certidões e outros documentos vão para DOCUMENTOS; pranchas, DWG e o resto vão para o projeto.
   - Confira o destino na linha e troque se precisar.
3. Compactado grande pode demorar: "Cancelar abertura" para a abertura.
4. O app recusa e avisa quando o compactado:
   - tem senha;
   - está corrompido;
   - passa de 4 GB ou de 3.000 arquivos;
   - tem caminhos que saem da pasta.

   Arquivos com o mesmo nome dentro do compactado entram como "nome (2)".
5. Nada é copiado antes de "Analisar". A cópia vai para a pasta do processo e o arquivo original não muda.

## Para quem desenvolve

- **Código:** `src` (WinUI 3, .NET 10). **Testes:** `tests`, com `dotnet test EditorRelatoriosPMVNI.sln -c Release -p:Platform=x64`. Os projetos portáveis usam `net10.0` sem RID; os testes Web herdam `win-x64`.
- **Build e publicação:** `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build-release.ps1`. Executa os sete projetos sequencialmente, exige TRX válido sem falhas e testa o executável duas vezes antes de publicar no GitHub. Não repete automaticamente testes reprovados. `-SemGitHub` gera somente a build local; `-SemLimpeza` preserva os intermediários para auditoria. Para incluir os testes opcionais do LibreOffice instalado, defina `$env:PMVNI_TESTES_LIBREOFFICE='1'` antes de executar.
- **Reenvio de build validada:** `$v = Get-Content '.\Aplicativo\build-validada.json' -Raw | ConvertFrom-Json; & .\tools\publicar-github.ps1 -Exe '.\Aplicativo\Editor de Relatorios PMVNI.exe' -Compilacao $v.Compilacao`. O script exige o comprovante da build e confere o SHA-256 antes de criar a etiqueta e a Release. Uma compilação já publicada não é reenviada.
- **Notas técnicas:** [docs/notas-tecnicas.md](docs/notas-tecnicas.md). **Auditoria e pendências:** [docs/auditoria/plano.md](docs/auditoria/plano.md).
- **Telas para conferência:** o modo de captura (`tools\capturar-janela.cmd`, aberto pelo Explorador) grava um PNG de cada tela em `tests\saida\captura`.
