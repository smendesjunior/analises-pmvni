# Paginador (TypeScript)

Ponte tipada host <-> web e paginador A4. O resultado e um unico `paginador.js` (IIFE, `chrome120`, com `folha.css`
embutido como texto) embutido em `Pmvni.Composicao.Web`.

## Modulos

- `src/plano.ts`: tipos do plano de quebras (espelho de `src/Pmvni.Composicao.Web/PlanoQuebras.cs`).
- `src/medida.ts`: px a 96 dpi, tolerancia `EPS_PX`, espera de estabilidade, linhas de texto, escala de impressao.
- `src/fragmentar.ts`: pontos de quebra (lidos do estilo computado) e recorte do fluxo entre duas posicoes.
- `src/folhas.ts`: prepara o documento, monta as folhas e escolhe as quebras (`compor`); aplica um plano pronto sem
  medir para decidir (`aplicarPlano`).
- `src/texto.ts`: texto visivel da area util de cada folha (`textoPorFolha`).
- `src/tela.ts`: aparencia de tela (fundo cinza, zoom, rolagem, indicador de folha), so em `@media screen`.
- `src/edicao.ts`: selecao de itens, campos editaveis (`data-campo`, so no DOM da tela), edicao no A4 em texto puro, ancora de
  rolagem, atalhos e menu de contexto (Tarefa 7 da Etapa 2).
- `src/marcas.ts`: marcas de revisao (traco azul nos itens alterados, botao "excl. NN") na camada `#pmvni-camada`, fora das folhas.
- `folha.css`: geometria da folha A4, da area util, do cabecalho e do rodape (as constantes ficam so aqui).

## Gerar o JS

```
cd web/paginador
npm install        # so na primeira vez (esbuild e typescript, em node_modules, ignorado pelo git)
npm run build      # grava ..\..\src\Pmvni.Composicao.Web\web\paginador.js
npm run typecheck  # tsc --noEmit
```

O `paginador.js` gerado **fica commitado**: `dotnet build` nao precisa de Node. Depois de mexer em `src/`, rode
`npm run build` e commite o JS junto.

## Ponte

Envelope `{ "v": 1, "tipo": string, "id": string, "dados": object }`. Host -> web: `compor`, `aplicarPlano`,
`textoPorFolha`, `rolarPara`, `definirZoom`, `estadoEditor`, `selecionar`, `editarCampo`, `concluirEdicao`, `obterAncora`.
Web -> host: `pronto`, `composto`, `planoAplicado`, `textoPorFolha`, `folhaVisivel`, `erro`, `estadoAplicado`, `selecionado`,
`edicaoIniciada`, `editar`, `semEdicao`, `ancora` e os eventos de interacao (id `evento-N`: `selecionado`, `pedirEdicao`, `editar`,
`edicaoCancelada`, `atalho`, `menuContexto`, `excluidoClicado`). Mensagem invalida e descartada e registrada nos dois lados (aqui: `console.warn` e uma
mensagem `erro` ao host); o espelho em C# esta em `src/Pmvni.Composicao.Web/Ponte/Mensagens.cs`.
