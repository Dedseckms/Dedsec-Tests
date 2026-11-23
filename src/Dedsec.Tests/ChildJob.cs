using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Dedsec.Tests;
internal static class ChildJob
{
    // The GPU child must not keep heating hardware after the main application exits.
    private static readonly SafeFileHandle Handle=Create();
    [StructLayout(LayoutKind.Sequential)] private struct Basic {public long PerProcess,PerJob;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint ActiveLimit;public UIntPtr Affinity;public uint Priority,Scheduling;}
    [StructLayout(LayoutKind.Sequential)] private struct Io {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
    [StructLayout(LayoutKind.Sequential)] private struct Extended {public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateJobObject(IntPtr attrs,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetInformationJobObject(SafeFileHandle job,int info,ref Extended value,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
    private static SafeFileHandle Create(){var h=CreateJobObject(IntPtr.Zero,null);var limits=new Extended{Basic=new Basic{Flags=0x2000}};if(h.IsInvalid||!SetInformationJobObject(h,9,ref limits,(uint)Marshal.SizeOf<Extended>()))throw new Win32Exception(Marshal.GetLastWin32Error());return h;}
    public static void Add(System.Diagnostics.Process process){if(!AssignProcessToJobObject(Handle,process.Handle)){try{process.Kill(true);}catch{}throw new Win32Exception(Marshal.GetLastWin32Error(),"Не удалось включить защиту GPU worker");}}
}
