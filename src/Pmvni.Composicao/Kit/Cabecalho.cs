using System.Text;

namespace Pmvni.Composicao.Kit;

/// <summary>Cabeçalho do relatório: SVG com o brasão embutido, entregue como data URI (<c>cabecalho</c> do kit).</summary>
public static class Cabecalho
{
    public const string OrgaoPadrao = "SECRETARIA MUNICIPAL DE OBRAS";

    /// <summary>Retorna <c>data:image/svg+xml;base64,...</c>; <paramref name="brasao"/> são os bytes do brasao.png.</summary>
    public static string CabecalhoUri(string orgao, byte[] brasao)
    {
        var logo = Convert.ToBase64String(brasao);
        const int wd = 688, hd = 64;
        var f48 = PythonTexto.Str(wd * .48);
        var f28 = PythonTexto.Str(wd * .28);
        var f76 = PythonTexto.Str(wd * .76);
        var f24 = PythonTexto.Str(wd * .24);
        var linhas = new[]
        {
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{wd}" height="{hd}" viewBox="0 0 {wd} {hd}">""",
            $"""<image href="data:image/png;base64,{logo}" x="0" y="2" width="160" height="52.2"/><line x1="172" y1="8" x2="172" y2="48" stroke="#DDE4EB" stroke-width="1"/>""",
            $"""<text x="{wd}" y="26" text-anchor="end" font-family="Verdana" font-weight="bold" font-size="14" fill="#1D3D5E">{Dados.E(orgao)}</text>""",
            $"""<text x="{wd}" y="41" text-anchor="end" font-family="Verdana" font-size="9.8" fill="#637083">RELATÓRIO TÉCNICO DE ANÁLISE</text>""",
            $"""<rect x="0" y="{hd - 4}" width="{f48}" height="3.5" fill="#2470B3"/><rect x="{f48}" y="{hd - 4}" width="{f28}" height="3.5" fill="#2F9E62"/><rect x="{f76}" y="{hd - 4}" width="{f24}" height="3.5" fill="#F2C43D"/></svg>""",
        };
        var svg = string.Join("\n", linhas);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(new UTF8Encoding(false).GetBytes(svg));
    }
}
