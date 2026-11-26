using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao;

internal static class GruposDoModelo
{
    // O molde oficial usa divs balanceadas. Os intervalos conservam o HTML original,
    // inclusive as imagens, a geometria dos cartões e as chaves das edições antigas.
    sealed class Bloco(int inicio, string classe, Bloco? pai)
    {
        public int Inicio = inicio, Fim;
        public string Classe = classe;
        public Bloco? Pai = pai;
    }

    static readonly Regex Tags = new("""<div\b[^>]*>|</div>|<pmvni-texto data-texto-modelo="([^"]+)" """.TrimEnd(), RegexOptions.CultureInvariant);
    static readonly Regex Classe = new("""class="([^ "]+)""");
    static readonly Dictionary<string, string> Nomes = new()
    {
        ["cover"] = "Capa", ["ttl-row"] = "Título e datas", ["band"] = "Identificação do processo",
        ["ic"] = "Quadro da capa", ["confirm"] = "Pontos a confirmar", ["locblk"] = "Localização na base municipal",
        ["keep"] = "Índices urbanísticos", ["obs"] = "Observações", ["att"] = "Atenção", ["note"] = "Aviso",
        ["panel"] = "Vista do diagrama", ["rf"] = "Referência", ["sign"] = "Assinatura",
    };

    public static string Aplicar(string html, Dictionary<string, TextoDoModelo> campos, IReadOnlyDictionary<string, object?> revisoes)
    {
        var blocos = new List<Bloco>();
        var folhas = new List<(int Pos, string Chave)>();
        Bloco? aberto = null;
        foreach (Match tag in Tags.Matches(html, html.IndexOf("<body>", StringComparison.Ordinal)))
        {
            if (tag.Groups[1].Success) folhas.Add((tag.Index, WebUtility.HtmlDecode(tag.Groups[1].Value)));
            else if (tag.Value == "</div>")
            {
                if (aberto is null) continue;
                aberto.Fim = tag.Index + tag.Length;
                aberto = aberto.Pai;
            }
            else
            {
                aberto = new Bloco(tag.Index, Classe.Match(tag.Value).Groups[1].Value, aberto);
                blocos.Add(aberto);
            }
        }
        var ocultos = new List<(int Inicio, int Fim)>();
        foreach (var b in blocos.Where(b => b.Classe == "sec" || Nomes.ContainsKey(b.Classe)))
        {
            var fim = b.Fim;
            if (b.Classe == "sec")
            {
                // O cabeçalho, a tabela, os avisos e o texto final pertencem à mesma seção.
                var proximo = blocos.FirstOrDefault(n => n.Inicio >= fim && n.Pai == b.Pai &&
                    (n.Classe is "sec" or "sign" or "keep" or "locblk" or "endblk" || n.Classe == "cover"));
                fim = proximo?.Inicio ?? b.Pai?.Fim - "</div>".Length ?? html.LastIndexOf("</body>", StringComparison.Ordinal);
            }
            var membros = folhas.Where(f => f.Pos >= b.Inicio && f.Pos < fim && campos.ContainsKey(f.Chave)).ToList();
            if (membros.Count == 0 || b.Classe == "sec" && b.Pai?.Classe is "locblk" or "keep") continue;
            var primeiro = membros[0].Chave;
            var id = "grupo-" + b.Classe.Replace("-", "") + "-" + primeiro;
            var rotulo = b.Classe is "sec" or "ic" or "rf" ? campos[primeiro].Original : Nomes[b.Classe];
            var visivel = Dados.Obter(revisoes, id) is not IReadOnlyDictionary<string, object?> rev || !Equals(Dados.Obter(rev, "visivel"), false);
            campos[id] = new("Grupo · " + rotulo, "Grupo · " + rotulo, visivel, campos[primeiro].Grupo);
            foreach (var (_, chave) in membros) campos[chave] = campos[chave] with { Grupo = id };
            if (!visivel) ocultos.Add((b.Inicio, fim));
        }
        if (ocultos.Count == 0) return html;
        var saida = new StringBuilder(html.Length);
        var pos = 0;
        foreach (var (inicio, fim) in ocultos.OrderBy(i => i.Inicio))
        {
            if (inicio > pos) saida.Append(html, pos, inicio - pos);
            pos = Math.Max(pos, fim);
        }
        return saida.Append(html, pos, html.Length - pos).ToString();
    }
}
