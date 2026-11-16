# Legislação (lista nova) e módulo Imóveis (QGIS) — desenho aprovado em 2026-11-16

## 1. Barra lateral da Legislação

**Por quê:** achar a lei mais rápido (esfera e tipo à vista) e navegar pelos artigos resumidos sem sair da lista.
Referência visual enviada pelo usuário (cartões coloridos por esfera, botões de tipo, cartão por lei); respeitar a interface atual.

- Abas Leis · Marcadores · Resultados e o filtro "Filtrar por título, número ou arquivo", como hoje.
- Cartões **Federal / Estadual / Municipal** com contagem, cores azul / verde / âmbar (tons para claro e escuro). Clicar filtra pela
  esfera; clicar de novo volta a todas. Esfera "Outras" conta só em "Todas".
- Botões de tipo com contagem: **Todas, Leis, Decretos, LC, NT**. Tipo derivado do título (Lei Complementar → LC, Norma Técnica → NT).
- Cartão de cada lei: ícone, título, ementa em uma linha (lida em segundo plano do texto já indexado, sem mudar o catálogo), selo do
  tipo, páginas, ⚠ quando falta a cópia, estrela de favorito (salva nos ajustes; favoritas no topo).
- Clicar abre a lei e expande, no próprio cartão, os tópicos (Título/Capítulo/Seção) com "Art. Nº + resumo"; clicar leva ao artigo.
  Uma lei expandida por vez. A coluna "Neste documento" sai.

**Abordagens consideradas:** gravar ementa/tipo no catálogo ao anexar (exige migração das leis já anexadas) × derivar na hora a partir
do texto indexado (escolhida: nada muda no formato). Tópicos na coluna ao lado (como hoje) × na lista (escolhido pelo usuário).

## 2. Módulo Imóveis (base QGIS)

Pacote de design: `Documents\Codex\2026-11-16\product-design-plugin-product-design-openai-7\outputs\pacote-imoveis` (referência; nada
dele fica como dependência). O usuário pediu tudo de uma vez.

- Botão **Imóveis** na barra lateral (após Legislação, sem mudar Ctrl+1…5).
- **Ajustes › Fontes de dados › Pasta dos arquivos QGIS**: seletor nativo, caminho salvo em `pasta_qgis`, "Verificar acesso e
  arquivos" (inventário). Sem caminho padrão.
- Pesquisa numa linha: Parâmetro ▼ · Condição ▼ (Contém, Não contém, Igual, Diferente, Começa com, Termina com) · valor digitável ·
  lupa/Enter. Ignora caixa e acentos; inscrição é texto (zeros preservados); nulos não satisfazem condições negativas; valor vazio não
  consulta. Parâmetros vêm das colunas que existem de fato na base (`vw_cadastro_vigente`).
- Tabela paginada; cada linha com **Abrir relatório**. A pesquisa fica preservada ao voltar.
- Relatório no visual rev.10: cabeçalho, identificação, mapa (lote em vermelho, edificações, zonas, logradouros, hidrografia) com
  legenda/fontes ao lado, ficha cadastral em duas colunas, confrontantes (lotes que tocam o lote), zona(s) por interseção, proximidade
  da hidrografia/APP com ressalvas. Lote não identificado com segurança bloqueia conclusões de zona e APP. Exportar PDF.
- Leitura somente leitura: SQLite (via `Microsoft.Data.Sqlite`, modo `ReadOnly`), shapefiles por leitor próprio (.shp/.dbf), geometria
  com NetTopologySuite. Sem ortofotos nesta versão (a referência não as usa como camada principal).
- Dados pessoais: documento do contribuinte não aparece na ficha.

**Abordagens consideradas:** reaproveitar `mapa_localizacao.py` do kit via Python externo (dependência de Python na máquina, processo
extra) × leitura nativa em .NET (escolhida: o app já é autocontido; shapefile é formato simples).
