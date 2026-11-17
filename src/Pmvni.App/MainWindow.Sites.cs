using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;
using Pmvni.Imoveis;

namespace Pmvni.App;

/// <summary>Sistemas da Prefeitura (2026-11-17): o grupo "Sites da Prefeitura" em Ajustes › Fontes de dados (situação, login guardado, Sair) e o
/// "Gerar boletim cadastral" do módulo Imóveis, que roda no navegador do GPI, à vista do usuário: pesquisa a inscrição municipal da linha em
/// Contribuintes Manutenção (Inscrição Municipal + Igual), abre a única linha exata em Consulta, confere a ficha e clica em "Boletim Cadastral".
/// O PDF oficial abre numa janela própria (mesmo perfil do GPI), com salvar e imprimir do próprio visualizador.</summary>
public sealed partial class MainWindow
{
    CoordenadorBci? coordenadorBci;
    CancellationTokenSource? cancelarBci;

    static readonly Modulo[] SitesDaPrefeitura = [Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco];

    void IniciarSites()
    {
        MontarSitesNosAjustes();
    }

    // ---------- Ajustes › Fontes de dados › Sites da Prefeitura ----------

    void MontarSitesNosAjustes()
    {
        var painel = PaginaDeAjustes.PainelSites;
        painel.Children.Clear();
        foreach (var m in SitesDaPrefeitura)
        {
            var linha = new StackPanel { Padding = new Thickness(16, 12, 16, 12), Spacing = 6 };
            linha.Children.Add(new TextBlock { Text = Modulos.Rotulo(m), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
            var situacao = new TextBlock { Text = PaginaSites.Situacao(m), Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] };
            linha.Children.Add(situacao);
            var usuario = new TextBox { PlaceholderText = "Usuário (opcional)", Width = 220 };
            var senha = new PasswordBox { PlaceholderText = "Senha", Width = 200 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(usuario, "Usuário do " + Modulos.Rotulo(m));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(senha, "Senha do " + Modulos.Rotulo(m));
            var guardar = new Button { Content = "Guardar login" };
            ToolTipService.SetToolTip(guardar, "Guarda usuário e senha protegidos pelo Windows, só para este usuário. Se o site pedir login de novo, o app entra sozinho.");
            var abrir = new Button { Content = "Abrir para entrar" };
            var sair = new Button { Content = "Sair" };
            ToolTipService.SetToolTip(sair, "Apaga o login lembrado e o guardado deste site neste computador. Só volta a lembrar quando você entrar de novo.");
            void Atualizar() => situacao.Text = PaginaSites.Situacao(m);
            guardar.Click += async (_, _) =>
            {
                try
                {
                    CofreSites.GuardarLogin(PastaDados.Raiz, m, new CredencialSite(usuario.Text, senha.Password));
                    CofreSites.Voltou(PastaDados.Raiz, m);
                    senha.Password = "";
                }
                catch (Exception e) { situacao.Text = e.Message; return; }
                Atualizar();
                situacao.Text += " · login guardado";
                // Site já aberto na tela de login: entra agora.
                await PaginaDeSites.EntrarAgoraAsync(m);
            };
            abrir.Click += async (_, _) => { CofreSites.Voltou(PastaDados.Raiz, m); await MudarModuloAsync(m); };
            sair.Click += async (_, _) =>
            {
                if (!await Perguntas.Atual.ConfirmarAsync("Sair do " + Modulos.Rotulo(m) + "?", "O login lembrado e o guardado deste site serão apagados deste computador.", "Sair", "Cancelar"))
                    return;
                if (m == Modulo.Gpi) cancelarBci?.Cancel();
                await PaginaDeSites.SairAsync(m);
                Atualizar();
            };
            linha.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { usuario, senha, guardar } });
            linha.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { abrir, sair } });
            if (m == Modulo.Gpi)
            {
                // Conferir o boletim antes de precisar (se o GPI mudar, diz em que passo parou). Nada é gravado.
                var insc = new TextBox { PlaceholderText = "Inscrição municipal (14 algarismos)", Width = 260 };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(insc, "Inscrição para testar o boletim");
                var testar = new Button { Content = "Testar o boletim" };
                ToolTipService.SetToolTip(testar, "Gera o boletim desta inscrição no GPI, sem gravar, e mostra se cada passo funcionou.");
                var saida = new TextBlock { Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"], TextWrapping = TextWrapping.Wrap };
                testar.Click += async (_, _) =>
                {
                    testar.IsEnabled = false;
                    try { await TestarBoletimAsync(insc.Text, saida); }
                    catch (Exception e) { RegistroErros.Gravar("sites.bci.teste", e); saida.Text = e.Message; }
                    finally { testar.IsEnabled = true; Atualizar(); }
                };
                linha.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { insc, testar } });
                linha.Children.Add(saida);
            }
            if (painel.Children.Count > 0) painel.Children.Add(new Border { Style = (Style)Application.Current.Resources["PmvniAjusteDivisoria"] });
            painel.Children.Add(linha);
        }
    }
}
