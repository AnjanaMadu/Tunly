using System.Runtime.InteropServices;

namespace Tunly.Core.Support;

/// <summary>
/// Windows job object that kills the child process tree when the handle is closed,
/// so closing (or crashing) Tunly never leaves a sing-box process behind.
/// </summary>
internal sealed class NativeJob : IDisposable
{
    const uint JobObjectLimitKillOnJobClose = 0x00002000;
    const int JobObjectInfoClassExtendedLimitInformation = 9;

    IntPtr _handle;

    public NativeJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero) return;

        var info = new JobObjectExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            SetInformationJobObject(_handle, JobObjectInfoClassExtendedLimitInformation, pointer, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public bool IsValid => _handle != IntPtr.Zero;

    public bool Assign(IntPtr processHandle) =>
        _handle != IntPtr.Zero && processHandle != IntPtr.Zero && AssignProcessToJobObject(_handle, processHandle);

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectBasicLimitInformation
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
    struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(
        IntPtr job, int informationClass, IntPtr information, uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
}
