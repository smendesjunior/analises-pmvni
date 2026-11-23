namespace Pmvni;

/// <summary>Padroniza o nome físico de saída sem modificar o caminho da pasta.</summary>
public static class ArquivoGerado
{
    public static string Caminho(string caminho) =>
        System.IO.Path.Join(System.IO.Path.GetDirectoryName(caminho), System.IO.Path.GetFileName(caminho).ToUpperInvariant());
}
