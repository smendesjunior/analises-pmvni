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
            var descricao = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            descricao.Children.Add(new TextBlock { TextAlignment = TextAlignment.Left, Text = Modulos.Rotulo(m), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
            var situacao = new TextBlock { TextAlignment = TextAlignment.Left, Text = PaginaSites.Situacao(m), Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] };
            descricao.Children.Add(situacao);
            var usuario = new TextBox { PlaceholderText = "Usuário (opcional)", HorizontalAlignment = HorizontalAlignment.Stretch };
            var senha = new PasswordBox { PlaceholderText = "Senha", HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(usuario, "Usuário do " + Modulos.Rotulo(m));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(senha, "Senha do " + Modulos.Rotulo(m));
            var guardar = new Button { Content = "Guardar login" };
            IconeDeAcao.SetGlifo(guardar, Glifos.Salvar);
            ToolTipService.SetToolTip(guardar, "Guarda usuário e senha protegidos pelo Windows, só para este usuário. Se o site pedir login de novo, o app entra sozinho.");
            var abrir = new Button { Content = "Abrir para entrar" };
            IconeDeAcao.SetGlifo(abrir, Glifos.Abrir);
            var sair = new Button { Content = "Sair" };
            IconeDeAcao.SetGlifo(sair, Glifos.Fechar);
            ToolTipService.SetToolTip(sair, "Apaga o login lembrado e o guardado deste site neste computador. Só volta a lembrar quando você entrar de novo.");
            void Atualizar()
            {
                situacao.Text = PaginaSites.Situacao(m);
                senha.PlaceholderText = CofreSites.TemLogin(PastaDados.Raiz, m) ? "••••••••" : "Senha";
            }
            Atualizar();
            guardar.Click += async (_, _) =>
            {
                try
                {
                    if (senha.Password.Length == 0 && CofreSites.TemLogin(PastaDados.Raiz, m)) return;
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
            var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { abrir, sair } };
            linha.Children.Add(new LinhaComAcoes { Children = { descricao, acoes } });

            var credenciais = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { usuario, senha, guardar } };
            usuario.MaxWidth = senha.MaxWidth = 280;
            usuario.HorizontalAlignment = senha.HorizontalAlignment = HorizontalAlignment.Left;
            credenciais.SizeChanged += (_, e) =>
            {
                var larguraCampo = (e.NewSize.Width - guardar.ActualWidth - 16) / 2;
                var empilhar = larguraCampo < 150;
                credenciais.Orientation = empilhar ? Orientation.Vertical : Orientation.Horizontal;
                usuario.Width = senha.Width = Math.Clamp(empilhar ? e.NewSize.Width : larguraCampo, 0, 280);
            };
            linha.Children.Add(credenciais);
            if (m == Modulo.Gpi)
            {
                // Conferir o boletim antes de precisar (se o GPI mudar, diz em que passo parou). Nada é gravado.
                var insc = new TextBox { PlaceholderText = "Inscrição municipal", Width = 220 };
                ToolTipService.SetToolTip(insc, "Informe os 14 algarismos da inscrição municipal.");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(insc, "Inscrição para testar o boletim");
                var testar = new Button { Content = "Testar" };
                IconeDeAcao.SetGlifo(testar, Glifos.Conferir);
                ToolTipService.SetToolTip(testar, "Gera o boletim desta inscrição no GPI, sem gravar, e mostra se cada passo funcionou.");
                var saida = new TextBlock { TextAlignment = TextAlignment.Left, Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"], TextWrapping = TextWrapping.Wrap };
                testar.Click += async (_, _) =>
                {
                    testar.IsEnabled = false;
                    try { await TestarBoletimAsync(insc.Text, saida); }
                    catch (Exception e) { RegistroErros.Gravar("sites.bci.teste", e); saida.Text = e.Message; }
                    finally { testar.IsEnabled = true; Atualizar(); }
                };
                var descricaoTeste = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                descricaoTeste.Children.Add(new TextBlock { Text = "Testar o boletim do GPI", Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
                descricaoTeste.Children.Add(new TextBlock { Text = "Confere se o app consegue abrir o boletim de um imóvel.", Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] });
                var acoesTeste = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { insc, testar } };
                linha.Children.Add(new LinhaComAcoes { Children = { descricaoTeste, acoesTeste } });
                linha.Children.Add(saida);
            }
            if (painel.Children.Count > 0) painel.Children.Add(new Border { Style = (Style)Application.Current.Resources["PmvniAjusteDivisoria"] });
            painel.Children.Add(linha);
        }
    }
}
