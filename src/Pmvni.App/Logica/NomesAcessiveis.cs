using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.App;

/// <summary>Nomes acessíveis das linhas das listas (auditoria A3/M10): o que o Narrador lê em cada linha, com a situação que na tela é só
/// a cor do ponto. Sem tipos WinUI: compilado também nos testes.</summary>
public static partial class NomesAcessiveis
{
    [GeneratedRegex(@"^(\d+)\s+(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Item();

    [GeneratedRegex(@"^(Figura \d+)\s+(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Figura();

    /// <summary>"Item 07, Recuos, Não atende, alterado, folha 4"; "Seção LOCALIZAÇÃO…, folha 1"; "Figura 3, Planta, folha 2"; "Capa, folha 1".</summary>
    public static string ItemNavegacao(string rotulo, bool secao, string? situacao, bool alterado, int folha)
    {
        var partes = new List<string>();
        if (secao) partes.Add(rotulo == "Capa" ? "Capa" : "Seção " + rotulo);
        else if (Item().Match(rotulo) is { Success: true } i) { partes.Add("Item " + i.Groups[1].Value); partes.Add(i.Groups[2].Value.Trim()); }
        else if (Figura().Match(rotulo) is { Success: true } f) { partes.Add(f.Groups[1].Value); partes.Add(f.Groups[2].Value.Trim()); }
        else partes.Add(rotulo);
        if (!string.IsNullOrWhiteSpace(situacao)) partes.Add(situacao);
        if (alterado) partes.Add("alterado");
        partes.Add("folha " + folha.ToString(CultureInfo.InvariantCulture));
        return string.Join(", ", partes.Where(p => p.Length > 0));
    }
}

/// <summary>Datas da interface num formato só, em português do Brasil (auditoria B10; não é opção).</summary>
public static class Formatos
{
    public static CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"02/01/2099".</summary>
    public static string Data(DateTime d) => d.ToString("dd/MM/yyyy", Cultura);

    public static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", Cultura);

    /// <summary>"02/01/2099 13:05".</summary>
    public static string DataHora(DateTime d) => d.ToString("dd/MM/yyyy HH:mm", Cultura);
}
