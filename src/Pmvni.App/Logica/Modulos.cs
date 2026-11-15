namespace Pmvni.App;

/// <summary>Módulos principais do app, na ordem do trilho (desenho §9). Ajustes fica embaixo.</summary>
public enum Modulo { Relatorios, Despachos, Processos, Legislacao, Ajustes }

/// <summary>Rótulo e ícone (Segoe Fluent Icons) de cada módulo no trilho. Sem tipos WinUI: compilado também nos testes.</summary>
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

    public static string Glifo(Modulo m) => m switch
    {
        Modulo.Relatorios => "",
        Modulo.Despachos => "",
        Modulo.Processos => "",
        Modulo.Legislacao => "",
        Modulo.Ajustes => "",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    /// <summary>Ajustes fica no pé do trilho, separado dos módulos de trabalho.</summary>
    public static bool Embaixo(Modulo m) => m == Modulo.Ajustes;
}
