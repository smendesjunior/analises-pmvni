using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Agentes;
using Pmvni.App;
using System.Diagnostics;
using System.Net.Http;
using Windows.Storage.Pickers;

namespace Pmvni.Instalador;

public sealed partial class App : Application
{
    Window? janela;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        janela = new Assistente();
        janela.Activate();
    }
}

sealed class Assistente : Window
{
    readonly StackPanel painel = new() { Margin = new Thickness(28), Spacing = 12 };
    readonly TextBox processos = new() { Header = "Pasta de processos", IsReadOnly = true };
    readonly TextBox destino = new() { Header = "Destino do Editor", IsReadOnly = true, Text = Pasta(Environment.SpecialFolder.DesktopDirectory) };
    readonly CheckBox desktop = new() { Content = "Área de Trabalho" };
    readonly CheckBox menu = new() { Content = "Menu Iniciar", IsChecked = true };
    readonly CheckBox barra = new() { Content = "Barra de tarefas", IsChecked = true };
    readonly TextBlock estado = new() { TextWrapping = TextWrapping.Wrap };
    readonly ProgressBar progresso = new() { Maximum = 100 };
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    string editor = "";
    string fonte = "";
    bool instalando;
    static string Pasta(Environment.SpecialFolder pasta) => Environment.GetFolderPath(pasta);
    public Assistente()
    {
        Title = "Análise de Projetos — Instalador";
        SystemBackdrop = new MicaBackdrop();
        painel.RequestedTheme = ElementTheme.Light;
        painel.Background = new SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);
        Content = new ScrollViewer { Content = painel, Background = painel.Background };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(800, 740));
        AppWindow.Closing += (_, e) => { if (instalando) e.Cancel = true; };
        Inicio();
    }
    void Cabecalho(string titulo)
    {
        painel.Children.Clear();
        painel.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///cabecalho.svg")), Width = 330, Height = 60, HorizontalAlignment = HorizontalAlignment.Left });
        painel.Children.Add(new TextBlock { Text = titulo, FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
    }
    Button Botao(string texto, Func<Task> acao)
    {
        var b = new Button { Content = texto };
        b.Click += async (_, _) => {
            b.IsEnabled = false;
            try { await acao(); }
            catch (Exception ex) { await Aviso(ex.Message); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }
    async Task Aviso(string texto) => await new ContentDialog { XamlRoot = painel.XamlRoot, Title = "Instalação do kit", Content = texto, CloseButtonText = "Fechar" }.ShowAsync();
    async Task<string?> Selecionar()
    {
        var p = new FolderPicker();
        p.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(p, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await p.PickSingleFolderAsync())?.Path;
    }
    void Campo(TextBox campo)
    {
        painel.Children.Add(campo);
        painel.Children.Add(Botao(campo == destino ? "Alterar destino…" : "Selecionar processos…", async () => campo.Text = await Selecionar() ?? campo.Text));
    }
    void Inicio()
    {
        Cabecalho("Onde deseja instalar?");
        Campo(processos);
        Campo(destino);
        painel.Children.Add(new TextBlock { Text = "Atalhos do Editor" });
        var atalhos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        foreach (var opcao in new[] { desktop, menu, barra }) atalhos.Children.Add(opcao);
        painel.Children.Add(atalhos);
        painel.Children.Add(Botao("Instalar", Instalar));
    }
    static string Script(string pasta) => Path.Combine(pasta, "INSTALAR_KIT_PMVNI.cmd");
    async Task Instalar()
    {
        if (!Directory.Exists(processos.Text) || !Directory.Exists(destino.Text)) { await Aviso("Selecione pastas existentes para os processos e o Editor."); return; }
        var candidatos = new[] { Path.GetDirectoryName(Environment.ProcessPath)!, @"\\srv-fileserver.PMVNI.PREF\pmvni\Obras\Engenharia\KIT DE TREINAMENTO DE IA (PMVNI)", @"Z:\Engenharia\KIT DE TREINAMENTO DE IA (PMVNI)" };
        fonte = await Task.Run(() => candidatos.FirstOrDefault(p => File.Exists(Script(p)))) ?? "";
        if (fonte == "") {
            await Aviso("Conecte-se à rede da Prefeitura ou selecione a pasta que contém o instalador oficial do kit.");
            fonte = await Selecionar() ?? "";
            if (fonte == "" || !File.Exists(Script(fonte))) return;
        }
        editor = Path.Combine(destino.Text, RegrasAtualizacao.NomeExecutavelPublicado);
        if (File.Exists(editor)) {
            var dlg = new ContentDialog { XamlRoot = painel.XamlRoot, Title = "Atualizar o Editor existente?", Content = editor, PrimaryButtonText = "Atualizar", CloseButtonText = "Cancelar" };
            if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        }
        instalando = true;
        try {
            Cabecalho("Preparando sua estação");
            painel.Children.Add(estado);
            painel.Children.Add(progresso);
            Andamento(0, "Baixando o Editor");
            await Baixar();
            if (PecasDoEditor().Codex == null) {
                Andamento(58,"Instalando o Codex CLI para a rota ChatGPT");
                using var wg = Process.Start(new ProcessStartInfo("winget.exe","install --exact --id OpenAI.Codex --source winget --scope user --silent --disable-interactivity --accept-package-agreements --accept-source-agreements") { UseShellExecute = false })!;
                await wg.WaitForExitAsync();
                if (PecasDoEditor().Codex == null) throw new IOException($"Codex CLI indisponível após o WinGet (código {wg.ExitCode}).");
                Environment.SetEnvironmentVariable("PATH", Environment.GetEnvironmentVariable("PATH")+";"+ Environment.GetEnvironmentVariable("Path",EnvironmentVariableTarget.User));
            }
            Andamento(60, "Instalando o kit e suas dependências. A janela do kit mostra os detalhes e solicita as confirmações necessárias.");
            await ExecutarInstaladorDoKit();
            Andamento(95, "Preparando atalhos");
            if (menu.IsChecked == true) Atalho(Pasta(Environment.SpecialFolder.Programs));
            if (desktop.IsChecked == true) Atalho(Pasta(Environment.SpecialFolder.DesktopDirectory));
            Fim();
        } catch {
            Cabecalho("Instalação interrompida");
            painel.Children.Add(Botao("Tentar novamente", Instalar));
            throw;
        } finally { instalando = false; }
    }

    async Task ExecutarInstaladorDoKit()
    {
        var pasta = Path.Combine(Pasta(Environment.SpecialFolder.LocalApplicationData), "PMVNI", "EditorInstalador");
        Directory.CreateDirectory(pasta);
        var comando = Path.Combine(pasta, $"instalar-{Guid.NewGuid():N}.cmd");
        var resultado = Path.ChangeExtension(comando, ".resultado.txt");
        // CALL expande as porcentagens duas vezes. Aspas protegem os outros caracteres válidos em caminhos.
        static string Aspas(string caminho) => "\"" + caminho.Replace("%", "%%%%", StringComparison.Ordinal) + "\"";
        var corpo = "@echo off\r\nchcp 65001 >nul\r\nsetlocal DisableDelayedExpansion\r\n"
            + $"call {Aspas(Script(fonte))} instalar --sem-pausa --processos {Aspas(processos.Text)}\r\n"
            + "> \"%~dpn0.resultado.tmp\" echo %errorlevel%\r\n"
            + "move /y \"%~dpn0.resultado.tmp\" \"%~dpn0.resultado.txt\" >nul\r\n";
        await File.WriteAllTextAsync(comando, corpo);
        var inicia = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        inicia.ArgumentList.Add(comando);
        using var explorer = Process.Start(inicia) ?? throw new IOException("Falha ao abrir o instalador do kit pelo Explorer.");
        var espera = Stopwatch.StartNew();
        while (!File.Exists(resultado) && espera.Elapsed < TimeSpan.FromMinutes(30))
            await Task.Delay(750);
        if (!File.Exists(resultado))
            throw new IOException("O Windows ainda não confirmou a conclusão do kit. Confira a janela do instalador.");
        var codigo = (await File.ReadAllTextAsync(resultado)).Trim();
        File.Delete(resultado);
        File.Delete(comando);
        if (codigo != "0") throw new IOException($"O kit encerrou com código {codigo}. Confira o registro em Documentos\\PMVNI.");
    }
    void Andamento(double valor, string etapa)
    {
        progresso.Value = valor;
        estado.Text = $"{valor:0}%  ·  {etapa}";
    }
    async Task Baixar()
    {
        var m = RegrasAtualizacao.Ler(await http.GetStringAsync(RegrasAtualizacao.UrlManifesto));
        if (m == null || !RegrasAtualizacao.Aceitavel(m, "00000000000000")) throw new InvalidDataException("Manifesto de atualização inválido.");
        if (await Task.Run(() => RegrasAtualizacao.PodeReaproveitar(editor,m))) return;
        var tmp = editor + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using var resposta = await http.GetAsync(m.Url, HttpCompletionOption.ResponseHeadersRead);
            resposta.EnsureSuccessStatusCode();
            using var origem = await resposta.Content.ReadAsStreamAsync();
            using (var saida = new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true)) {
                var buf = new byte[131072];
                long total = 0;
                int lidos;
                while ((lidos = await origem.ReadAsync(buf)) > 0) {
                    total += lidos;
                    if (total > m.Tamanho) throw new InvalidDataException("Download excedeu o tamanho publicado.");
                    await saida.WriteAsync(buf.AsMemory(0,lidos));
                    Andamento(55d*total/m.Tamanho,"Baixando o Editor");
                }
            }
            await Task.Run(() => RegrasAtualizacao.Conferir(tmp,m));
            File.Move(tmp,editor,true);
        } finally { File.Delete(tmp); }
    }
    void Atalho(string pasta)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)!)!;
        dynamic lnk = shell.CreateShortcut(Path.Combine(pasta,"Editor de Relatórios PMVNI.lnk"));
        lnk.TargetPath = editor;
        lnk.WorkingDirectory = Path.GetDirectoryName(editor);
        lnk.Save();
    }
    static Task Abrir(string arquivo, string argumentos = "")
    {
        Process.Start(new ProcessStartInfo(arquivo,argumentos) { UseShellExecute = true })?.Dispose();
        return Task.CompletedTask;
    }
    void Fim()
    {
        Cabecalho("Instalação concluída");
        Andamento(100,editor);
        painel.Children.Add(estado);
        painel.Children.Add(progresso);
        if (barra.IsChecked == true) painel.Children.Add(Botao("Mostrar Editor para fixar na barra de tarefas",async () => {
            await Abrir("explorer.exe","/select,\""+editor+"\"");
            await Aviso("No menu do Editor, escolha Mostrar mais opções e Fixar na barra de tarefas.");
        }));
        painel.Children.Add(new TextBlock { Text = "Conectar contas ao Editor", FontSize = 20 });
        painel.Children.Add(new TextBlock { Text = "Entre com os provedores que deseja usar. O Editor reutiliza a sessão deste usuário do Windows.", TextWrapping = TextWrapping.Wrap });
        foreach (var provedor in Provedores.Ativos)
            painel.Children.Add(Botao("Entrar com "+Provedores.Nome(provedor), () => Login(provedor)));
        painel.Children.Add(Botao("Abrir o Editor",() => Abrir(editor)));
    }
    Pecas PecasDoEditor()
    {
        var opcoes = OpcoesLocalizacao.DoAmbiente(fonte,null,null);
        var paths = opcoes.Path.Concat((Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? "").Split(';')).Select(Environment.ExpandEnvironmentVariables).ToArray();
        return Localizacao.Ler(opcoes with { Path = paths });
    }
    void Vincular(string provedor,string modo)
    {
        var custom = Environment.GetEnvironmentVariable("PMVNI_DADOS_DIR");
        var pasta = custom ?? Path.Combine(Pasta(Environment.SpecialFolder.ApplicationData),"EditorRelatoriosPMVNI");
        var arquivo = Path.Combine(pasta,"ajustes.json");
        var antigo = Path.Combine(Pasta(Environment.SpecialFolder.LocalApplicationData),"EditorRelatoriosPMVNI","ajustes.json");
        var origem = !File.Exists(arquivo) && custom == null ? antigo : arquivo;
        var ajustes = File.Exists(origem) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(origem))!.AsObject() : new System.Text.Json.Nodes.JsonObject();
        if (ajustes["agentes"]?[provedor]?["modo"]?.GetValue<string>() == modo) return;
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(editor)).Length != 0)
            throw new InvalidOperationException("Feche o Editor para salvar o vínculo da conta e tente novamente.");
        ajustes["agentes"] ??= new System.Text.Json.Nodes.JsonObject();
        var agentes = ajustes["agentes"]!.AsObject();
        agentes[provedor] ??= new System.Text.Json.Nodes.JsonObject();
        agentes[provedor]!["modo"] = modo;
        Directory.CreateDirectory(pasta);
        var tmp = arquivo + "." + Guid.NewGuid().ToString("N");
        try {
            File.WriteAllText(tmp,ajustes.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp,arquivo,true);
        } finally { File.Delete(tmp); }
    }
    async Task Login(Provedor provedor)
    {
        if (!Provedores.Ativos.Contains(provedor)) throw new InvalidOperationException(Provedores.RotaDesativada);
        var pecas = PecasDoEditor();
        var exe = provedor == Provedor.Claude ? pecas.Claude : pecas.Codex;
        if (exe == null) throw new FileNotFoundException("O programa de login de "+provedor+" não foi encontrado neste perfil.");
        using var p = Process.Start(new ProcessStartInfo(exe,provedor == Provedor.Claude ? "auth login" : "login") { UseShellExecute = true })!;
        await p.WaitForExitAsync();
        if (p.ExitCode != 0) throw new IOException("Login interrompido. Você pode tentar novamente.");
        Vincular(Provedores.Sufixo(provedor),"conta");
        await Aviso("Conta vinculada ao Editor.");
    }
}
