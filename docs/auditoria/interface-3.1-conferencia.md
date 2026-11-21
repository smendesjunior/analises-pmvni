# Conferência geral da interface (Editor 3.1.1)

Data: 21/11/2026 · método: leitura estática de todo o XAML (`src/Pmvni.App`, 21 arquivos) e da interface montada em C#, comparada com o padrão dos Ajustes (título da página 24 + ajuda; título de seção `PmvniTituloSecao`; cartões `PmvniAjusteGrupo`; um botão primário `PmvniBotaoPrimarioStyle` por grupo e o resto no estilo padrão; dicas `Dicas.Titulo`/`Dicas.Descricao`).

**Nada foi visto na tela.** O app é WinUI 3 e esta conferência rodou em Linux; não houve compilação nem captura. Tudo abaixo é «código». O que depende de ver (tema escuro, alto contraste, 1366 × 768) está marcado como *conferir no Windows*.

## Resultado

| Verificação | Resultado |
|---|---|
| Botões no XAML sem dica (`Dicas` ou `ToolTip`) | 4 — `MainWindow.xaml:103` e `:111` (pesquisa anterior/próxima: a dica é posta em `MainWindow.Pesquisa.cs`) e `Controles/EstadoVazio.xaml:17-18` (o texto vem de quem usa o controle). Nenhum falta de verdade. |
| Botões com `ToolTipService.ToolTip` cru em vez de `Dicas` | 11 — barra superior, zoom da folha e os três agentes do Novo relatório. Funcionam; só o formato da dica difere do resto (sem título em negrito). |
| Títulos de página | Todos em `PmvniTituloPaginaStyle` (24). Corrigido nesta versão: "Monitor de análise" usava o estilo de página com fonte 20; passou a `PmvniTituloSecao`. |
| Títulos de seção | Todos em `PmvniTituloSecao` (20). Corrigido: o título do painel do Acompanhamento repetia `FontSize="20"`. |
| Raio de botões fora do padrão (8) | 2 intencionais: o botão redondo "Pedir à IA" do Novo despacho (16, ícone de envio) e o botão da análise na barra superior (21, pílula). |
| Cores fixas fora de `Recursos/` | `Controles/Trilho.xaml` (fundos pastel e ícones dos módulos) e logotipos dos agentes (`CartaoAgente.xaml`, fundo branco dos logos no Novo relatório). São cores de marca; *conferir no Windows* o trilho no tema escuro e no alto contraste, porque esses fundos não trocam com o tema. |
| Tamanhos de ícone | Padrão consistente: 14 em botões, 16 em títulos de seção e grupos, 18–20 nos cartões grandes do Novo relatório. Fora disso: 10, 11, 13 e 21 em pontos isolados (selo, seta de grupo, ícone do Acompanhamento) — aceitável. |
| Fontes avulsas | Concentradas em textos de etiqueta (10–13) e no cartão do agente (15/20). Sem efeito na hierarquia. |
| Diálogos com botão padrão | 8 de 9 tinham `DefaultButton`. Corrigido: "Adicionar observações" (`Despachos/JanelaAprovacao.cs`) agora tem `Aplicar` como padrão (Enter). |
| Botões montados em C# | 37 criados sem estilo, no padrão Fluent (como os secundários dos Ajustes); os principais recebem estilo onde são criados. Sem divergência encontrada. |

## Aplicado depois da conferência

1. Dicas no formato padrão (`Dicas`) em vez do `ToolTip` simples: botão da análise, Desfazer (Ctrl+Z), Refazer (Ctrl+Y), os quatro botões de zoom da folha e os três agentes do Novo relatório. Desfazer, Refazer e zoom já recebiam a dica completa pelo C#; o XAML agora começa no mesmo formato.
2. Ícone tonal ao lado dos títulos de seção que não tinham: "Seções do relatório" e "Processos" (as outras páginas já tinham ícone; o Novo relatório mantém os passos numerados).
3. Botão de fechar do Novo despacho: ícone na cor do texto, como no Acompanhamento.

## Ainda por conferir no Windows

- Trilho de módulos no tema escuro e no alto contraste (fundos pastel com cor fixa). Não alterado sem ver.
- Todas as mudanças acima: compilação e aparência.
