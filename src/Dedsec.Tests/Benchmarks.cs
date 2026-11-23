using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
namespace Dedsec.Tests;
public static class Benchmarks
{
    private static readonly byte[] Data = MakeData();
    private static readonly byte[] ExpectedHash=Convert.FromHexString("4C504B067EE56397A22A33527C9A6E29D9B2208BDB1B09594B4D146E0D177869");
    private static byte[] MakeData() { var a=new byte[1048576];uint seed=0xDED;for(int i=0;i<a.Length;i++){seed^=seed<<13;seed^=seed>>17;seed^=seed<<5;a[i]=(byte)seed;}return a; }
    public static async Task<Measurement> Cpu(bool multi, CancellationToken token, double seconds = 5)
    {
        int workers = multi ? Environment.ProcessorCount : 1; var clock = Stopwatch.StartNew(); long total = 0;
        var tasks = Enumerable.Range(0,workers).Select(_ => Task.Run(() => { long n=0; Span<byte> hash=stackalloc byte[32]; while(clock.Elapsed.TotalSeconds < seconds) { token.ThrowIfCancellationRequested(); SHA256.HashData(Data,hash);if(!hash.SequenceEqual(ExpectedHash))throw new InvalidDataException("CPU SHA-256 output does not match the independent reference digest"); n++; } Interlocked.Add(ref total,n); },token)).ToArray();
        await Task.WhenAll(tasks); clock.Stop();
        return new(multi?"cpu_multi":"cpu_single", Math.Round(total / clock.Elapsed.TotalSeconds,2), "MiB/s", "ok", $"SHA-256, xorshift32 seeded 1 MiB blocks, every digest verified, {workers} workers; includes scheduling; hardware SHA acceleration allowed", clock.Elapsed.TotalSeconds);
    }
    public static Measurement MemoryCopy(CancellationToken token, double seconds = 4)
    {
        const int size=64*1024*1024;
        if (Inventory.Memory().Available < 256UL*1024*1024) return new("ram_copy",0,"MiB/s","skipped","Нужно 256 MiB доступной памяти",0);
        var a=new byte[size]; var b=new byte[size]; new Random(0xDED).NextBytes(a); Buffer.BlockCopy(a,0,b,0,size); var watch=Stopwatch.StartNew(); long passes=0;
        while(watch.Elapsed.TotalSeconds<seconds) { token.ThrowIfCancellationRequested(); Buffer.BlockCopy(a,0,b,0,size); passes++; }
        watch.Stop(); if (!a.AsSpan().SequenceEqual(b)) throw new InvalidDataException("RAM copy verification failed");
        return new("ram_copy",Math.Round(passes*64/watch.Elapsed.TotalSeconds,2),"MiB/s","ok","64 MiB copy, one thread, source bytes counted once; includes cache effects",watch.Elapsed.TotalSeconds);
    }
    public static Measurement MemoryVerify(CancellationToken token)
    {
        long available=(long)Inventory.Memory().Available; int mib=(int)Math.Min(256,available/4/1048576); if(mib<16) return new("ram_errors",0,"errors","skipped","Недостаточно доступной памяти",0);
        var watch=Stopwatch.StartNew(); var buffer=new uint[mib*1048576/4]; long errors=0;
        foreach(uint pattern in new uint[] {0,0xffffffff,0xaaaaaaaa,0x55555555})
        {
            for(int i=0;i<buffer.Length;i++) { if((i&0xffff)==0) token.ThrowIfCancellationRequested(); buffer[i]=pattern^(uint)i; }
            for(int i=0;i<buffer.Length;i++) { if((i&0xffff)==0) token.ThrowIfCancellationRequested(); if(buffer[i]!=(pattern^(uint)i)) errors++; }
        }
        return new("ram_errors",errors,"errors",errors==0?"ok":"failed",$"{mib} MiB, 4 address-dependent patterns; only process memory, not all installed RAM",watch.Elapsed.TotalSeconds);
    }
    public static async Task<List<Measurement>> Smoke()
    { var list=new List<Measurement> { await Cpu(false,CancellationToken.None,.25), MemoryCopy(CancellationToken.None,.25), MemoryVerify(CancellationToken.None) }; try { list.Add(GpuBenchmark.Run(0, .5, CancellationToken.None)); } catch(Exception e) { list.Add(new("gpu_compute",0,"Miter/s","skipped",e.Message,0)); } return list; }
    public static Task<Measurement> Gpu(int index, double seconds, CancellationToken token) => Task.Run(async () =>
    {
        string file=Path.Combine(Settings.DataDir,"gpu-"+Guid.NewGuid().ToString("N")+".json"); Directory.CreateDirectory(Settings.DataDir);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true };
        foreach(var arg in new[]{"--gpu-worker",index.ToString(),seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),file}) start.ArgumentList.Add(arg);
        using var p=Process.Start(start) ?? throw new IOException("Не удалось запустить GPU worker");
        ChildJob.Add(p);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(seconds+25));
        try { await p.WaitForExitAsync(timeout.Token); if(p.ExitCode!=0 || !File.Exists(file)) throw new IOException("GPU worker завершился с ошибкой драйвера"); return System.Text.Json.JsonSerializer.Deserialize<Measurement>(File.ReadAllText(file),Json.Options)!; }
        catch(OperationCanceledException) { try{p.Kill(true);}catch{} token.ThrowIfCancellationRequested(); throw new TimeoutException("GPU worker остановлен по таймауту"); }
        finally { try{File.Delete(file);}catch{} }
    },token);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr sa,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool WriteFile(SafeFileHandle file,IntPtr buffer,uint bytes,out uint written,IntPtr overlapped);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool ReadFile(SafeFileHandle file,IntPtr buffer,uint bytes,out uint read,IntPtr overlapped);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetFilePointerEx(SafeFileHandle file,long distance,out long position,uint method);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool FlushFileBuffers(SafeFileHandle file);
    public static unsafe List<Measurement> Disk(string root,CancellationToken token)
    {
        var drive=new DriveInfo(root); if(!drive.IsReady || drive.AvailableFreeSpace<1024L*1024*1024) throw new IOException("Для теста нужен 1 GiB свободного места");
        if(drive.DriveType!=DriveType.Fixed && drive.DriveType!=DriveType.Removable) throw new IOException("Выберите локальный HDD, SSD или USB-накопитель");
        string selected=Path.GetFullPath(root);string location=Directory.Exists(selected)&&!selected.TrimEnd('\\').Equals(drive.RootDirectory.FullName.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)?selected:Path.GetPathRoot(Path.GetTempPath())!.Equals(drive.RootDirectory.FullName,StringComparison.OrdinalIgnoreCase)?Path.GetTempPath():drive.RootDirectory.FullName;
        string dir=Path.Combine(location,"DedsecTests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string file=Path.Combine(dir,"test.tmp");
        const uint block=1048576; const int blocks=256; void* ptr=NativeMemory.AlignedAlloc(block,4096); if(ptr==null) throw new OutOfMemoryException(); var buffer=new Span<byte>(ptr,(int)block); Data.CopyTo(buffer);
        try
        {
            using var handle=CreateFile(file,0xC0000000,0,IntPtr.Zero,1,0x20000000|0x80000000|0x04000000,IntPtr.Zero);
            if(handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Не удалось создать временный файл без кэширования");
            var watch=Stopwatch.StartNew();
            for(int i=0;i<blocks;i++) { token.ThrowIfCancellationRequested(); if(!WriteFile(handle,(IntPtr)ptr,block,out uint done,IntPtr.Zero) || done!=block) throw new IOException("Ошибка записи временного файла"); }
            if(!FlushFileBuffers(handle)) throw new IOException("Не удалось сбросить данные на диск"); watch.Stop(); double writeSeconds=watch.Elapsed.TotalSeconds;
            if(!SetFilePointerEx(handle,0,out _,0)) throw new IOException("Seek failed"); watch.Restart();
            for(int i=0;i<blocks;i++) { token.ThrowIfCancellationRequested(); if(!ReadFile(handle,(IntPtr)ptr,block,out uint read,IntPtr.Zero) || read!=block) throw new IOException("Ошибка чтения временного файла"); if(!buffer.SequenceEqual(Data)) throw new InvalidDataException("Записанные и прочитанные данные различаются"); }
            watch.Stop(); double readSeconds=watch.Elapsed.TotalSeconds;
            var random=new Random(0xDED); watch.Restart();
            for(int i=0;i<128;i++) { token.ThrowIfCancellationRequested(); if(!SetFilePointerEx(handle,(long)random.Next(blocks*256)*4096,out _,0) || !ReadFile(handle,(IntPtr)ptr,4096,out uint read,IntPtr.Zero) || read!=4096) throw new IOException("Ошибка случайного чтения"); }
            watch.Stop();
            string detail="256 MiB temporary file; 1 MiB blocks; unbuffered+write-through; verification included in read time; drive model mapping not inferred";
            return new(){new("disk_write",Math.Round(256/writeSeconds,2),"MiB/s","ok",detail,writeSeconds),new("disk_read",Math.Round(256/readSeconds,2),"MiB/s","ok",detail,readSeconds),new("disk_latency",Math.Round(watch.Elapsed.TotalMilliseconds/128,3),"ms","ok","128 seeded 4 KiB random reads; unbuffered; device cache may apply",watch.Elapsed.TotalSeconds)};
        }
        finally { NativeMemory.AlignedFree(ptr); try{File.Delete(file);}catch{} try{Directory.Delete(dir);}catch{} }
    }
}
