namespace Pmvni.App;

/// <summary>Módulos principais do app, na ordem do trilho (desenho §9). Ajustes fica embaixo.</summary>
/// Os sistemas da Prefeitura (2026-11-16) vêm depois de Ajustes no enum, para Ctrl+1 … Ctrl+5 e o número de Ajustes não mudarem.
public enum Modulo { Relatorios, Despachos, Processos, Legislacao, Ajustes, Essencial, Gpi, Alfresco }

/// <summary>Rótulo, ícone e atalho de cada módulo no trilho. Sem tipos WinUI: compilado também nos testes.</summary>
public static class Modulos
{
    public static string Rotulo(Modulo m) => m switch
    {
        Modulo.Relatorios => "Relatórios",
        Modulo.Despachos => "Despachos",
        Modulo.Processos => "Processos",
        Modulo.Legislacao => "Legislação",
        Modulo.Ajustes => "Ajustes",
        Modulo.Essencial => "Essencial",
        Modulo.Gpi => "GPI",
        Modulo.Alfresco => "Alfresco",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>O glifo Segoe Fluent do módulo: reserva quando o ícone colorido (<see cref="ChaveIcone"/>) não carrega.</summary>
    public static string Glifo(Modulo m) => m switch
    {
        Modulo.Relatorios => Glifos.Relatorio,
        Modulo.Despachos => Glifos.Despacho,
        Modulo.Processos => Glifos.Pasta,
        Modulo.Legislacao => Glifos.Livro,
        Modulo.Ajustes => Glifos.Engrenagem,
        Modulo.Essencial or Modulo.Gpi or Modulo.Alfresco => Glifos.Globo,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>A chave do ícone colorido do módulo (DataTemplate em <c>Recursos\IconesModulos.xaml</c>): "PmvniIconeRelatorios" etc.</summary>
    public static string ChaveIcone(Modulo m) => Enum.IsDefined(m) ? "PmvniIcone" + m : throw new ArgumentOutOfRangeException(nameof(m));

    /// <summary>A posição do módulo nos atalhos (Ctrl+1 … Ctrl+5) e nas teclas de acesso (Alt, 1 … 5), na ordem do trilho.</summary>
    public static int Numero(Modulo m) => Enum.IsDefined(m) ? (int)m + 1 : throw new ArgumentOutOfRangeException(nameof(m));

    /// <summary>O id do comando que leva ao módulo (<c>modulo.relatorios</c> …).</summary>
    public static string Comando(Modulo m) => m switch
    {
        Modulo.Relatorios => ComandosApp.ModuloRelatorios,
        Modulo.Despachos => ComandosApp.ModuloDespachos,
        Modulo.Processos => ComandosApp.ModuloProcessos,
        Modulo.Legislacao => ComandosApp.ModuloLegislacao,
        Modulo.Ajustes => ComandosApp.ModuloAjustes,
        Modulo.Essencial => ComandosApp.ModuloEssencial,
        Modulo.Gpi => ComandosApp.ModuloGpi,
        Modulo.Alfresco => ComandosApp.ModuloAlfresco,
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>O módulo de um id <c>modulo.*</c>; null para os outros ids.</summary>
    public static Modulo? DoComando(string id) => Enum.GetValues<Modulo>().Select(m => (Modulo?)m).FirstOrDefault(m => Comando(m!.Value) == id);

    /// <summary>A dica do item do trilho: "Relatórios (Ctrl+1)".</summary>
    public static string Dica(Modulo m) => EhSite(m) ? $"{Rotulo(m)}: {Descricao(m)}" : $"{Rotulo(m)} (Ctrl+{Numero(m)})";

    /// <summary>Os sistemas da Prefeitura que abrem num navegador dentro do app.</summary>
    public static bool EhSite(Modulo m) => m is Modulo.Essencial or Modulo.Gpi or Modulo.Alfresco;

    /// <summary>O endereço inicial de cada sistema.</summary>
    public static string Endereco(Modulo m) => m switch
    {
        Modulo.Essencial => "https://vendanova.essencialbpms.com.br/login.html#!/minhamesa",
        Modulo.Gpi => "https://gpi12.cloud.el.com.br/ServerExec/acessoBase/",
        Modulo.Alfresco => "http://alfresco.vendanova.es.gov.br:8080/share/page/repository#filter=path%7C%2FSites%2Fpmvni%2FdocumentLibrary%2FObras&page=1",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    public static string Descricao(Modulo m) => m switch
    {
        Modulo.Essencial => "processos e protocolos (Essencial BPMS)",
        Modulo.Gpi => "gestão pública integrada (GPI)",
        Modulo.Alfresco => "documentos de Obras no Alfresco",
        _ => "",
    };

    /// <summary>Ajustes fica no pé do trilho, separado dos módulos de trabalho.</summary>
    public static bool Embaixo(Modulo m) => m == Modulo.Ajustes;
}
