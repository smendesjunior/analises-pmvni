# Roteiro da tela (Etapa 1, Tarefa 10)

A tela é somente leitura: o centro da janela é o relatório inteiro como folhas A4 empilhadas (WebView2 visível), idêntico ao
PDF. Esquerda: seções e itens (recolhível). Barra de título própria: "Processo nº — Requerente", estado ("Composto: N folhas" /
"1 bloco excede a página"), **Abrir** e **Exportar PDF**. Rodapé: zoom (− 100 % +, Ajustar largura, Página inteira) e "folha X de Y".

## Como rodar

1. `tools\build-release.ps1` (publica o executável único em `Aplicativo\` e limpa os builds).
2. Abrir `tools\roteiro.cmd` com o `explorer.exe` (fora do pacote do Claude). O app abre `tests\corpus\longo.json`, exercita a
   tela pelos mesmos caminhos dos botões (`--roteiro`) e grava `docs\roteiro-etapa-1.resultado.json`; as capturas (PNG) e os PDFs
   ficam em `tests\saida\roteiro` (ignorado pelo git; apagado depois da conferência).
3. Uso manual: `Editor de Relatorios PMVNI.exe <dados.json>` (ou o botão Abrir).

## Passos e resultado automático (`roteiro-etapa-1.resultado.json`, `ok: true`)

| # | Passo | O que o roteiro conferiu |
|---|-------|--------------------------|
| 1 | Abrir `longo.json` | 21 folhas, escala de impressão 1,0715; InfoBar informativo "reduzido 7% para caber" com o texto exato; título "Processo nº 9001/2026 — …"; estado "Composto: 21 folhas"; 77 linhas na lista; Exportar habilitado; WebView2 visível + host oculto de composição no mesmo processo e na mesma pasta de dados (convivem); os dois InPrivate e com os atalhos do navegador desligados; folha 14 com conteúdo além da borda lateral (faixa vermelha + InfoBar informativo). |
| 2 | Rolagem contínua | Folhas contíguas com passo de 297 mm + 8 mm (±1 px) em 21 folhas; rolagem até as folhas 1, 4, 9, 10, 20 e 21 atualiza "folha X de Y" (IntersectionObserver, com espera de 120 ms). |
| 3 | Zoom não recompõe | `definirZoom` 0,5 / 2 / 3 / 1 e os botões + + − / Ajustar largura / Página inteira: mesmo texto por folha, **os mesmos elementos de folha no DOM** (nada refeito), largura da folha = 793,7 px × zoom; recompor no host oculto depois dá o mesmo plano (JSON idêntico). |
| 4 | Navegação | 9 itens (capa, seções, itens) levam à folha certa; o trilho marca o item atual (indicador 3 × 16). |
| 5 | Exportar PDF | 21 páginas = 21 folhas; recibo com `sha256_html` do HTML mostrado e `sha256_pdf` do arquivo; mesmo plano da tela; InfoBar de sucesso. |
| 6 | `estouro.json` | 1 estouro (folha 3, `secao-2`); estado "1 bloco excede a página"; InfoBar de erro; Exportar desabilitado com dica explicando; `ExportarAsync` recusa (`ExportacaoRecusadaException`) sem gravar PDF; a folha 3 recebe a faixa vermelha (5 px, na margem cinza, fora da folha). |
| 7 | Tema escuro | `prefers-color-scheme` claro → escuro: o fundo fora das folhas muda (228 → 31); as folhas seguem `rgb(255,255,255)`; texto por folha igual. |
| 8 | Campos `[ ]` em aberto (cópia do `curto` na pasta do roteiro) | Abre em rascunho; Exportar desabilitado e a dica começa com "A exportação está bloqueada". |
| 9 | Arquivo muda depois de aberto (cópia do `curto`) | A exportação recusa com "O arquivo mudou desde que foi aberto; reabra antes de exportar." e não grava PDF nem recibo. |

Testes automáticos que cobrem o mesmo comportamento sem a janela (`tests\Pmvni.Web.Testes\TelaTestes.cs`):
`zoom_nao_recompoe`, `aparencia_de_tela_nao_entra_no_pdf` (PDF impresso antes e depois de ligar a aparência de tela, mesmos pixels
em todas as páginas; mesmo `sha256_html` e mesmas páginas do PDF exportado), `rolar_e_ajustar_o_zoom_informam_a_folha_visivel`,
`folha_que_transborda_ganha_faixa_vermelha_fora_da_folha`.

## O que ainda precisa de olhar humano

O roteiro grava capturas do WebView2 e da casca XAML e elas foram vistas na construção, mas a conferência final é humana:

- Acabamento visual da casca: espaçamentos da barra de título, alinhamento dos botões Abrir/Exportar com os botões da janela,
  tamanho do trilho, aparência do Mica e do tema escuro em outros monitores (as capturas XAML não incluem o Mica).
- O seletor de arquivos (FileOpenPicker/FileSavePicker) abre e salva de fato: o roteiro exporta por código (mesmo
  `ExportarParaAsync` do botão) e não abre os seletores.
- Sensação de rolagem e zoom com mouse/teclado (o zoom do navegador por Ctrl + roda está desligado de propósito; o zoom é só o do rodapé).
- Janela estreita (< 794 px de largura útil): abre com "ajustar largura"; o mínimo de zoom é 50 %.
