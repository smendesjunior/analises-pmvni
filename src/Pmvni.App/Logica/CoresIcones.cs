namespace Pmvni.App;

/// <summary>Cor de cada ícone da faixa de opções pelo que o comando faz, como no Outlook (pedido do usuário, 2026-11-17): excluir em
/// vermelho, criar e conferir em verde, salvar/abrir/editar em azul, pastas em âmbar, PDF em vermelho, atualizar/filtrar em azul-petróleo,
/// figuras e marcas em roxo. Glifos sem cor ficam com a cor do texto. Sem tipos WinUI (testável): devolve ARGB do tema claro e escuro.</summary>
public static class CoresIcones
{
    public readonly record struct Par(uint Claro, uint Escuro);

    static readonly Par Vermelho = new(0xFFC42B1C, 0xFFFF99A4);
    static readonly Par Verde = new(0xFF107C10, 0xFF6CCB5F);
    static readonly Par Azul = new(0xFF0F6CBD, 0xFF62ABF5);
    static readonly Par Ambar = new(0xFFC27C0E, 0xFFF8C35B);
    static readonly Par Pdf = new(0xFFD13438, 0xFFFF8C8C);
    static readonly Par Petroleo = new(0xFF038387, 0xFF4CC2C4);
    static readonly Par Roxo = new(0xFF8764B8, 0xFFC3A6F2);

    static readonly Dictionary<string, Par> PorGlifo = new(StringComparer.Ordinal)
    {
        [Glifos.Excluir] = Vermelho, [Glifos.Pendente] = Vermelho,
        [Glifos.Adicionar] = Verde, [Glifos.NovoItem] = Verde, [Glifos.Conferir] = Verde, [Glifos.Situacao] = Verde,
        [Glifos.Salvar] = Azul, [Glifos.SalvarComo] = Azul, [Glifos.Editar] = Azul, [Glifos.Abrir] = Azul, [Glifos.AbrirArquivo] = Azul,
        [Glifos.Copiar] = Azul, [Glifos.Citacao] = Azul, [Glifos.Propriedades] = Azul, [Glifos.Importar] = Azul,
        [Glifos.ImportarAnalise] = Azul, [Glifos.Anexar] = Azul, [Glifos.IrPara] = Azul, [Glifos.Inicio] = Azul, [Glifos.Despacho] = Azul,
        [Glifos.Explorador] = Ambar, [Glifos.Pasta] = Ambar, [Glifos.Recentes] = Ambar, [Glifos.Ano] = Ambar,
        [Glifos.Exportar] = Pdf,
        [Glifos.Atualizar] = Petroleo, [Glifos.Filtro] = Petroleo, [Glifos.Ordenar] = Petroleo, [Glifos.Pesquisar] = Petroleo,
        [Glifos.Ocorrencia] = Petroleo, [Glifos.OcorrenciaProxima] = Petroleo,
        [Glifos.Figuras] = Roxo, [""] = Roxo, [Glifos.MarcasRevisao] = Roxo, [Glifos.Marcadores] = Roxo, [Glifos.Ia] = Roxo,
        [Glifos.Original] = Roxo, [Glifos.Tema] = Roxo,
    };

    /// <summary>A cor do glifo; null = sem cor própria (fica a do texto).</summary>
    public static Par? De(string? glifo) => glifo is not null && PorGlifo.TryGetValue(glifo, out var p) ? p : null;
}
