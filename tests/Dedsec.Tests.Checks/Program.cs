using Dedsec.Tests;
using Dedsec.Update;
using System.Text.Json;
using System.Reflection;
if(args.Length==2&&args[0]=="--job-owner")
{
    var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"ping.exe"),"-n 60 127.0.0.1"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})!;
    typeof(Benchmarks).Assembly.GetType("Dedsec.Tests.ChildJob")!.GetMethod("Add",BindingFlags.Public|BindingFlags.Static)!.Invoke(null,new object[]{child});File.WriteAllText(args[1],child.Id.ToString());return;
}
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
using(var cancel=new CancellationTokenSource()){cancel.Cancel();try{await Benchmarks.Cpu(true,cancel.Token,.1);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){Check(true,"CPU cancellation observed");}}
var cpu=await Benchmarks.Cpu(false,CancellationToken.None,.15);Check(cpu.Value>0&&cpu.Seconds>=.15,"CPU returns timed finite throughput");
var dataset=(byte[])typeof(Benchmarks).GetField("Data",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;byte original=dataset[0];dataset[0]^=1;try{await Benchmarks.Cpu(false,CancellationToken.None,.1);throw new Exception("Corrupt SHA payload accepted");}catch(InvalidDataException){Check(true,"CPU digest mismatch fails instead of producing a score");}finally{dataset[0]=original;}
var info=new SystemInfo("CPU",4,"GPU",8,"Windows 11","X64",new());var report=new Report{System=info,Findings=new(){new("warning","secret user path","C:\\Users\\private-name","local")},DeleteToken="do-not-share",ResultUrl="https://example.test",Measurements=new(){new("gpu_compute",0,"Miter/s","skipped","C:\\Users\\private-name",0)}};
string payload=JsonSerializer.Serialize(report.PublicPayload(),Json.Options);Check(!payload.Contains("private-name")&&!payload.Contains("do-not-share")&&!payload.Contains("findings")&&!payload.Contains("result_url"),"Public projection excludes local diagnostics, keys and exception paths");
Check(Diagnostics.Live(new(){new("CPU","Package","Temperature",98,"°C")},new(),90).Any(f=>f.Severity=="critical"),"High temperature produces actionable alert");
Check(Diagnostics.Live(new(),new(),90).Any(f=>f.Severity=="unknown"),"Missing sensors never become an all-clear");
Check(ErrorCodes.Explain("0x80070005",false).Contains("(5)"),"HRESULT_FROM_WIN32 unwraps access-denied code");
Check(ErrorCodes.Explain("0x124",true).Contains("WHEA_UNCORRECTABLE_ERROR")&&!ErrorCodes.Explain("0x124",false).Contains("WHEA_UNCORRECTABLE_ERROR"),"BSOD and Win32 numeric domains stay separate");
if(args.Contains("--hardware"))
{
    var multi=await Benchmarks.Cpu(true,CancellationToken.None,.25);Check(multi.Status=="ok"&&multi.Value>0,"All-logical-CPU workers verify SHA digests and return throughput");
    var ram=Benchmarks.MemoryVerify(CancellationToken.None);Check(ram.Status=="ok"&&ram.Value==0,"Allocated RAM integrity patterns");
    var copy=Benchmarks.MemoryCopy(CancellationToken.None,.2);Check(copy.Value>0,"RAM copy throughput");
    var gpuClock=System.Diagnostics.Stopwatch.StartNew();var gpu=GpuBenchmark.Run(0,.5,CancellationToken.None);Check(gpu.Status=="ok"&&gpu.Value>0,"D3D11 dispatch and all integer outputs verified");Check(gpu.Detail.Contains("GPU timestamps")&&gpu.Seconds>0&&gpu.Seconds<=gpuClock.Elapsed.TotalSeconds+.01,"GPU throughput uses valid hardware timestamps, not CPU sleep duration");
    var disk=Benchmarks.Disk(Path.GetPathRoot(Environment.SystemDirectory)!,CancellationToken.None);Check(disk.Count==3&&disk.All(m=>m.Value>0&&m.Status=="ok"),"Unbuffered 256 MiB write/read/verify/random reads");
    var before=Directory.GetDirectories(Path.GetPathRoot(Environment.SystemDirectory)!,"DedsecTests-*").Length;using var cancel=new CancellationTokenSource();cancel.Cancel();try{Benchmarks.Disk(Path.GetPathRoot(Environment.SystemDirectory)!,cancel.Token);}catch(OperationCanceledException){}
    Check(Directory.GetDirectories(Path.GetPathRoot(Environment.SystemDirectory)!,"DedsecTests-*").Length==before,"Cancelled disk test cleans only its temporary directory");
    var findings=Diagnostics.Analyze(Inventory.Read());Check(findings.Count>10&&findings.Any(f=>f.Title.Contains("Журнал")),"Real Windows diagnostic providers and bounded event analysis");
    string jobProbe=Path.Combine(Path.GetTempPath(),"DedsecJob-"+Guid.NewGuid().ToString("N"));var ownerStart=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};ownerStart.ArgumentList.Add("--job-owner");ownerStart.ArgumentList.Add(jobProbe);using var owner=System.Diagnostics.Process.Start(ownerStart)!;await owner.WaitForExitAsync();Check(owner.ExitCode==0&&File.Exists(jobProbe),"Child process assigned to kill-on-close Job Object");int childId=int.Parse(File.ReadAllText(jobProbe));File.Delete(jobProbe);await Task.Delay(250);bool gone;try{using var child=System.Diagnostics.Process.GetProcessById(childId);gone=child.HasExited;if(!gone&&child.ProcessName.Equals("PING",StringComparison.OrdinalIgnoreCase))child.Kill();}catch(ArgumentException){gone=true;}Check(gone,"Worker process dies when its owning process exits");
    Console.WriteLine($"Diagnostic findings: {findings.Count}; warnings: {findings.Count(f=>f.Severity is "warning" or "critical")}; unavailable: {findings.Count(f=>f.Severity=="unknown")}");
}
int mi=Array.IndexOf(args,"--signed-package");if(mi>=0){var data=File.ReadAllBytes(args[mi+1]);var sig=File.ReadAllBytes(args[mi+2]);var m=Trust.Verify(data,sig);Trust.VerifyPackage(args[mi+3],m);Check(true,"Release manifest signature, package SHA256 and length");data[5]^=1;try{Trust.Verify(data,sig);throw new Exception("Tampered manifest accepted");}catch(System.IO.InvalidDataException){Check(true,"Modified update metadata rejected");}sig[0]^=1;try{Trust.Verify(File.ReadAllBytes(args[mi+1]),sig);throw new Exception("Tampered signature accepted");}catch(System.IO.InvalidDataException){Check(true,"Modified signature rejected");}}
Console.WriteLine($"{passed} checks passed");
int fixtureIndex=Array.IndexOf(args,"--updater-fixture");if(fixtureIndex>=0)
{
    string fixture=Path.GetFullPath(args[fixtureIndex+1]),helper=Path.GetFullPath(args[fixtureIndex+2]),key=Path.GetFullPath(args[fixtureIndex+3]);
    string sandbox=Path.Combine(Path.GetTempPath(),"DedsecUpdateChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(sandbox);
    var assembly=System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(helper);var apply=assembly.GetType("Dedsec.Tests.Updater.Program")!.GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic)!;
    using var rsa=System.Security.Cryptography.RSA.Create();rsa.ImportFromPem(File.ReadAllText(key));
    foreach(string mode in new[]{"ready","fail","traversal"})
    {
        string root=Path.Combine(sandbox,mode),target=Path.Combine(root,"installed"),stage=Path.Combine(root,"stage"),payloadDir=Path.Combine(root,"payload");Directory.CreateDirectory(target);Directory.CreateDirectory(stage);Directory.CreateDirectory(payloadDir);
        foreach(string file in Directory.GetFiles(fixture)){File.Copy(file,Path.Combine(target,Path.GetFileName(file)));File.Copy(file,Path.Combine(payloadDir,Path.GetFileName(file)));}File.WriteAllText(Path.Combine(target,"mode.txt"),"old");File.WriteAllText(Path.Combine(payloadDir,"mode.txt"),mode);
        string package=Path.Combine(stage,"package.zip");System.IO.Compression.ZipFile.CreateFromDirectory(payloadDir,package);
        if(mode=="traversal"){using var zip=System.IO.Compression.ZipFile.Open(package,System.IO.Compression.ZipArchiveMode.Update);var entry=zip.CreateEntry("../outside.txt");using var writer=new StreamWriter(entry.Open());writer.Write("must-not-extract");}
        using(var stream=File.OpenRead(package)){var manifest=new UpdateManifest("0.1.1","https://github.com/Dedseckms/Dedsec-Tests/releases/download/v0.1.1/fixture.zip",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)),stream.Length,"x64");var data=JsonSerializer.SerializeToUtf8Bytes(manifest);File.WriteAllBytes(Path.Combine(stage,"manifest.json"),data);File.WriteAllBytes(Path.Combine(stage,"manifest.sig"),rsa.SignData(data,System.Security.Cryptography.HashAlgorithmName.SHA256,System.Security.Cryptography.RSASignaturePadding.Pss));}
        bool failed=false;try{apply.Invoke(null,new object[]{new[]{stage,target,int.MaxValue.ToString()}});}catch(TargetInvocationException){failed=true;}
        if(mode=="ready")Check(!failed&&File.ReadAllText(Path.Combine(target,"mode.txt"))=="ready"&&File.ReadAllText(Path.Combine(stage,"rollback","mode.txt"))=="old","Updater transaction and ready handshake");
        if(mode=="fail")Check(failed&&File.ReadAllText(Path.Combine(target,"mode.txt"))=="old","Failed launch restores previous payload");
        if(mode=="traversal")Check(failed&&!File.Exists(Path.Combine(stage,"outside.txt"))&&File.ReadAllText(Path.Combine(target,"mode.txt"))=="old","Even a signed ZIP cannot escape extraction root");
    }
    Console.WriteLine($"{passed} checks passed including isolated updater integration");
}
