# Paginador (TypeScript)

Ponte tipada host <-> web e, nas proximas tarefas, o paginador A4. O resultado e um unico `paginador.js`
(IIFE, `chrome120`) embutido em `Pmvni.Composicao.Web`.

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
`textoPorFolha`, `rolarPara`, `definirZoom`. Web -> host: `pronto`, `composto`, `planoAplicado`, `textoPorFolha`,
`folhaVisivel`, `erro`. Mensagem invalida e descartada e registrada nos dois lados (aqui: `console.warn` e uma
mensagem `erro` ao host); o espelho em C# esta em `src/Pmvni.Composicao.Web/Ponte/Mensagens.cs`.
