using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Pmvni.Revisao;

public class GrupoDeProcessos : IDisposable
{
    readonly SafeFileHandle job;
    internal SafeFileHandle Identificador => job;

    public GrupoDeProcessos()
    {
        job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            var erro = Marshal.GetLastWin32Error();
            job.Dispose();
            throw new Win32Exception(erro);
        }
    }

    /// <summary>Põe o processo no job. false se o Windows recusar (processo já saiu, ou num job que não permite outro): o processo segue sem a proteção.</summary>
    public bool Atribuir(Process processo)
    {
        try { return AssignProcessToJobObject(job, processo.Handle); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception) { return false; }
    }

    /// <summary>true se o processo está neste job.</summary>
    public bool Contem(Process processo)
    {
        try { return IsProcessInJob(processo.Handle, job, out var dentro) && dentro; }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception) { return false; }
    }

    /// <summary>Fecha o job: tudo o que estiver nele é encerrado.</summary>
    public void Dispose() => job.Dispose();

    /// <summary>O acompanhamento perdeu contato com um trabalhador que ainda pode estar em execução.
    /// Preserva o job da sessão e solta somente o vínculo de encerramento desta operação.</summary>
    public void ManterProcessosAoFechar()
    {
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateJobObjectW(IntPtr atributos, string? nome);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(SafeFileHandle job, int classe, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint tamanho);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr processo);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool IsProcessInJob(IntPtr processo, SafeFileHandle job, out bool dentro);
}
