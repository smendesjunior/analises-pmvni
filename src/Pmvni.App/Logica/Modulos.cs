namespace Pmvni.App;

/// <summary>Módulos principais do app, na ordem do trilho (desenho §9). Ajustes fica embaixo.</summary>
public enum Modulo { Relatorios, Despachos, Processos, Legislacao, Ajustes }

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
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>O módulo de um id <c>modulo.*</c>; null para os outros ids.</summary>
    public static Modulo? DoComando(string id) => Enum.GetValues<Modulo>().Select(m => (Modulo?)m).FirstOrDefault(m => Comando(m!.Value) == id);

    /// <summary>A dica do item do trilho: "Relatórios (Ctrl+1)".</summary>
    public static string Dica(Modulo m) => $"{Rotulo(m)} (Ctrl+{Numero(m)})";

    /// <summary>Ajustes fica no pé do trilho, separado dos módulos de trabalho.</summary>
    public static bool Embaixo(Modulo m) => m == Modulo.Ajustes;
}
