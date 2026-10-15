using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexAccounts.Core;

// The isolated app-server and every child it creates belong to this job.
// Closing it releases helpers that otherwise keep staging credentials/databases open.
sealed class OwnedProcessJob : IDisposable
{
    IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] struct Basic
    {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr Minimum, Maximum;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] struct Counters { public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] struct Extended
    { public Basic Basic; public Counters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int type, ref Extended info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr value);
    public OwnedProcessJob(Process process)
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new Extended { Basic = new Basic { Flags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf<Extended>()) || !AssignProcessToJobObject(handle, process.Handle))
        { var error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error, "The isolated Codex helper could not be contained safely."); }
    }
    public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
}
