using Xunit;

namespace Pmvni.Composicao.Testes;

/// <summary>Caminhos do ambiente usados pelos testes de paridade com o Python do kit.</summary>
public static class Ambiente
{
    /// <summary>Pasta do modelo HTML do kit; sobrescreva com a variável PMVNI_KIT_DIR.</summary>
    public static string PastaModeloKit
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("PMVNI_KIT_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OneDrive", "PMVNI", "SIDNEY JUNIOR", "KIT DE TREINAMENTO DE IA (PMVNI)", "02 - MODELOS DE DOCUMENTOS",
                "RELATORIO", "MODELO HTML (USADO PELOS PLUGINS)");
        }
    }

}
