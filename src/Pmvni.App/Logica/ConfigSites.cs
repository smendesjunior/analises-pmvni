namespace Pmvni.App;

/// <summary>Regras do preenchimento de login nos sites da Prefeitura (2026-11-17, correção: o WebView2 trazia um login antigo,
/// "izabelat", por cima do login guardado em Ajustes). O app só usa o login de Ajustes; sem ele, o campo fica vazio.</summary>
public static class ConfigSites
{
    /// <summary>O navegador não guarda nem preenche senhas e formulários por conta própria.</summary>
    public const bool PreenchimentoDoNavegador = false;

    /// <summary>Ao abrir, apaga do perfil dos sites as senhas e os formulários que o navegador guardou. Os cookies (login lembrado) ficam.</summary>
    public static bool ApagarPreenchimentoGuardado => true;
}
