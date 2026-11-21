using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Pmvni.Revisao;

/// <summary>Programa de uma operação interna do Editor. No Windows a lista de jobs e os três
/// canais são entregues ao kernel na criação. Até a primeira instrução do filho já roda no grupo.</summary>
public sealed class ProcessoInterno : IDisposable
{
    public Process Processo { get; }
    public StreamWriter StandardInput { get; private set; } = null!;
    public StreamReader StandardOutput { get; private set; } = null!;
    public StreamReader StandardError { get; private set; } = null!;
    readonly GrupoDeProcessos? proprio;
    static readonly object travaSessao = new();
    static GrupoDeProcessos? sessao;
    static bool encerrada;

    static Referencia ReferenciaDaSessao()
    {
        lock (travaSessao)
        {
            if (encerrada) throw new InvalidOperationException("O Editor já está fechando.");
            return new Referencia((sessao ??= new GrupoDeProcessos()).Identificador);
        }
    }

    public static void EncerrarSessao()
    {
        lock (travaSessao)
        {
            encerrada = true;
            sessao?.Dispose();
        }
    }

    ProcessoInterno(Process processo, GrupoDeProcessos? proprio)
    {
        Processo = processo;
        this.proprio = proprio;
    }

    public static ProcessoInterno Iniciar(ProcessStartInfo inicio, GrupoDeProcessos? pai = null, GrupoDeProcessos? operacao = null, bool consoleInterativo = false)
    {
        if (inicio.UseShellExecute) throw new ArgumentException("Execução interna precisa de um executável.", nameof(inicio));
        TravaDeProgramas.Conferir(inicio.FileName);
        if (!OperatingSystem.IsWindows())
        {
            var p = Process.Start(inicio) ?? throw new InvalidOperationException("O programa não abriu.");
            return new ProcessoInterno(p, null)
            {
                StandardInput = inicio.RedirectStandardInput ? p.StandardInput : null!,
                StandardOutput = inicio.RedirectStandardOutput ? p.StandardOutput : null!,
                StandardError = inicio.RedirectStandardError ? p.StandardError : null!,
            };
        }

        var proprio = operacao is null ? new GrupoDeProcessos() : null;
        var dono = operacao ?? proprio!;
        ProcessoInterno? iniciado = null;
        try
        {
            using var entrada = new Canal(PipeDirection.Out, inicio.RedirectStandardInput);
            using var saida = new Canal(PipeDirection.In, inicio.RedirectStandardOutput);
            using var erro = new Canal(PipeDirection.In, inicio.RedirectStandardError);
            using var atributos = new Atributos();
            atributos.Adicionar(0x20002, [entrada.Filho, saida.Filho, erro.Filho]); // HANDLE_LIST
            using var referenciaSessao = ReferenciaDaSessao();
            using var referencia = new Referencia(dono.Identificador);
            using var referenciaPai = pai is null ? null : new Referencia(pai.Identificador);
            atributos.Adicionar(0x2000D, referenciaPai is null ? [referenciaSessao.Valor, referencia.Valor] : [referenciaSessao.Valor, referenciaPai.Valor, referencia.Valor]); // JOB_LIST
            var si = new Inicio { Tamanho = Marshal.SizeOf<Inicio>(), Flags = consoleInterativo ? 0 : 0x100, Entrada = entrada.Filho, Saida = saida.Filho,
                Erro = erro.Filho, Lista = atributos.Lista };
            var linha = new StringBuilder(Argumento(inicio.FileName));
            if (inicio.Arguments.Length > 0) linha.Append(' ').Append(inicio.Arguments);
            foreach (var arg in inicio.ArgumentList) linha.Append(' ').Append(Argumento(arg));
            var ambiente = string.Join('\0', inicio.Environment.Where(x => x.Value is not null)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + "=" + x.Value)) + "\0\0";
            // EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED
            var flags = 0x80404u | (consoleInterativo ? 0x10u : inicio.CreateNoWindow ? 0x08000000u : 0);
            if (!CreateProcessW(null, linha, 0, 0, true, flags, ambiente, string.IsNullOrEmpty(inicio.WorkingDirectory) ? null : inicio.WorkingDirectory,
                    ref si, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            using var raiz = new SafeProcessHandle(info.Processo, true);
            using var thread = new SafeFileHandle(info.Thread, true);
            try
            {
                var processo = Process.GetProcessById(info.Pid);
                iniciado = new ProcessoInterno(processo, proprio);
                _ = processo.Handle; // mantém identidade e código de saída, mesmo para um comando instantâneo
                if (inicio.RedirectStandardInput)
                    iniciado.StandardInput = new StreamWriter(entrada.Tomar(), inicio.StandardInputEncoding ?? Console.InputEncoding) { AutoFlush = true };
                if (inicio.RedirectStandardOutput)
                    iniciado.StandardOutput = new StreamReader(saida.Tomar(), inicio.StandardOutputEncoding ?? Console.OutputEncoding);
                if (inicio.RedirectStandardError)
                    iniciado.StandardError = new StreamReader(erro.Tomar(), inicio.StandardErrorEncoding ?? Console.OutputEncoding);
                if (proprio is not null)
                {
                    processo.Exited += (_, _) => proprio.Dispose();
                    processo.EnableRaisingEvents = true;
                }
                TravaDeProgramas.AoIniciar?.Invoke(Path.GetFullPath(inicio.FileName));
                if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                return iniciado;
            }
            catch { TerminateProcess(raiz, 1); throw; }
        }
        catch { iniciado?.Dispose(); proprio?.Dispose(); throw; }
    }

    // Regras de aspas do argv do Windows, inclusive barras antes de aspas e no fim.
    static string Argumento(string valor)
    {
        if (valor.Length > 0 && !valor.Any(c => char.IsWhiteSpace(c) || c == '"')) return valor;
        var texto = new StringBuilder("\"");
        int barras = 0;
        foreach (char c in valor)
        {
            if (c == '\\') { barras++; continue; }
            texto.Append('\\', c == '"' ? 2 * barras + 1 : barras).Append(c);
            barras = 0;
        }
        return texto.Append('\\', 2 * barras).Append('"').ToString();
    }

    public void Dispose()
    {
        proprio?.Dispose();
        try { StandardInput?.Dispose(); } catch (IOException) { }
        StandardOutput?.Dispose();
        StandardError?.Dispose();
        Processo.Dispose();
    }

    sealed class Canal : IDisposable
    {
        AnonymousPipeServerStream? canal;
        readonly SafeFileHandle? nulo;
        public nint Filho { get; }
        public Canal(PipeDirection direcao, bool redirecionar)
        {
            if (redirecionar)
            {
                canal = new(direcao, HandleInheritability.Inheritable);
                Filho = canal.ClientSafePipeHandle.DangerousGetHandle();
            }
            else
            {
                var seguranca = new Seguranca { Tamanho = Marshal.SizeOf<Seguranca>(), Herdar = 1 };
                nulo = CreateFileW("NUL", direcao == PipeDirection.Out ? 0x80000000u : 0x40000000u, 3, ref seguranca, 3, 0, 0);
                if (nulo.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                Filho = nulo.DangerousGetHandle();
            }
        }
        public Stream Tomar()
        {
            var s = canal!;
            s.DisposeLocalCopyOfClientHandle();
            canal = null;
            return s;
        }
        public void Dispose() { canal?.Dispose(); nulo?.Dispose(); }
    }

    sealed class Referencia : IDisposable
    {
        readonly SafeFileHandle handle;
        public nint Valor => handle.DangerousGetHandle();
        public Referencia(SafeFileHandle handle)
        {
            this.handle = handle;
            bool obtida = false;
            handle.DangerousAddRef(ref obtida);
        }
        public void Dispose() => handle.DangerousRelease();
    }

    sealed class Atributos : IDisposable
    {
        public nint Lista { get; }
        readonly List<nint> valores = [];
        public Atributos()
        {
            nuint tamanho = 0;
            InitializeProcThreadAttributeList(0, 2, 0, ref tamanho);
            Lista = Marshal.AllocHGlobal((nint)tamanho);
            if (InitializeProcThreadAttributeList(Lista, 2, 0, ref tamanho)) return;
            var erro = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(Lista);
            throw new Win32Exception(erro);
        }
        public void Adicionar(nuint atributo, nint[] handles)
        {
            var tamanho = handles.Length * IntPtr.Size;
            var valor = Marshal.AllocHGlobal(tamanho);
            valores.Add(valor);
            Marshal.Copy(handles, 0, valor, handles.Length);
            if (!UpdateProcThreadAttribute(Lista, 0, atributo, valor, (nuint)tamanho, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Dispose()
        {
            DeleteProcThreadAttributeList(Lista);
            foreach (var p in valores) Marshal.FreeHGlobal(p);
            Marshal.FreeHGlobal(Lista);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Seguranca { public int Tamanho; public nint Descritor; public int Herdar; }
    [StructLayout(LayoutKind.Sequential)]
    struct Inicio
    {
        public int Tamanho;
        public nint Reservado, Desktop, Titulo;
        public int X, Y, Largura, Altura, Colunas, Linhas, Cores, Flags;
        public short Mostrar, Reservado2;
        public nint Dados, Entrada, Saida, Erro, Lista;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Informacao { public nint Processo, Thread; public int Pid, Tid; }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern bool CreateProcessW(string? app, StringBuilder linha, nint processo, nint thread, bool herdar, uint flags,
        string ambiente, string? pasta, ref Inicio inicio, out Informacao info);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern SafeFileHandle CreateFileW(string nome, uint acesso, uint compartilhar, ref Seguranca seguranca, uint modo, uint flags, nint modelo);
    [DllImport("kernel32", SetLastError = true)]
    static extern bool InitializeProcThreadAttributeList(nint lista, int quantidade, uint flags, ref nuint tamanho);
    [DllImport("kernel32", SetLastError = true)]
    static extern bool UpdateProcThreadAttribute(nint lista, uint flags, nuint atributo, nint valor, nuint tamanho, nint anterior, nint devolvido);
    [DllImport("kernel32")] static extern void DeleteProcThreadAttributeList(nint lista);
    [DllImport("kernel32", SetLastError = true)] static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32")] static extern bool TerminateProcess(SafeProcessHandle processo, uint codigo);
}
