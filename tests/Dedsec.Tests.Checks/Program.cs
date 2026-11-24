using Dedsec.Tests;
using Dedsec.Update;
using System.Text.Json;
using System.Reflection;
if(args.Contains("--agreement-ready"))
{
    if(!args.Contains("--capture"))throw new InvalidOperationException("Agreement probe requires isolated capture settings.");
    int markerIndex=Array.IndexOf(args,"--update-ready");string marker=Path.GetFullPath(args[markerIndex+1]);if(!marker.StartsWith(Path.GetFullPath(Settings.DataDir)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||File.Exists(marker))throw new InvalidOperationException("Fresh isolated marker required.");Directory.CreateDirectory(Settings.DataDir);
    Exception? failure=null;var ui=new Thread(()=>
    {
        try
        {
            var app=new Dedsec.Tests.App();app.InitializeComponent();var owner=new MainWindow{WindowStartupLocation=System.Windows.WindowStartupLocation.Manual,Left=-16000,Top=-16000,ShowInTaskbar=false};
            System.Windows.EventManager.RegisterClassHandler(typeof(BrandedDialog),System.Windows.FrameworkElement.LoadedEvent,new System.Windows.RoutedEventHandler((sender,_)=>{var dialog=(BrandedDialog)sender;dialog.WindowStartupLocation=System.Windows.WindowStartupLocation.Manual;dialog.Left=-16000;dialog.Top=-16000;}));
            bool waitingWithConsent=false;var clock=System.Diagnostics.Stopwatch.StartNew();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};timer.Tick+=(_,_)=>{var dialog=app.Windows.OfType<BrandedDialog>().FirstOrDefault();if(dialog==null)return;if(File.Exists(marker)){waitingWithConsent=dialog.Consent!=null&&dialog.Consent.IsChecked!=true&&!dialog.AcceptButton.IsEnabled;timer.Stop();dialog.DialogResult=false;}else if(clock.Elapsed.TotalSeconds>10){timer.Stop();dialog.DialogResult=false;}};timer.Start();
            owner.Show();bool accepted=(bool)typeof(MainWindow).GetMethod("Agreement",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(owner,null)!;timer.Stop();if(!File.Exists(marker)||!waitingWithConsent||accepted)throw new Exception("Rendered agreement did not signal readiness while consent was pending.");owner.Close();app.Shutdown();
        }catch(Exception error){failure=error;}
    });ui.IsBackground=true;ui.SetApartmentState(ApartmentState.STA);ui.Start();if(!ui.Join(TimeSpan.FromSeconds(20)))throw new TimeoutException("Agreement probe timed out");if(failure!=null)throw failure;Console.WriteLine("PASS Actual agreement signals updater readiness while consent remains unchecked; no consent accepted");return;
}
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
var ssd=Telemetry.MapSensor("Kingston NV1","Composite Temperature",LibreHardwareMonitor.Hardware.HardwareType.Storage,LibreHardwareMonitor.Hardware.SensorType.Temperature,0,36);
var warningLimit=Telemetry.MapSensor("Kingston NV1","Warning Temperature",LibreHardwareMonitor.Hardware.HardwareType.Storage,LibreHardwareMonitor.Hardware.SensorType.Temperature,10,84);
var criticalLimit=Telemetry.MapSensor("Kingston NV1","Critical Temperature",LibreHardwareMonitor.Hardware.HardwareType.Storage,LibreHardwareMonitor.Hardware.SensorType.Temperature,11,94);
var driveSensors=new List<SensorRow>{ssd,warningLimit,criticalLimit};
Check(driveSensors.Where(s=>s.Type=="Temperature").Max(s=>s.Value)==36&&warningLimit.Value==84&&criticalLimit.Value==94,"NVMe thresholds stay visible but cannot become live maximum, graph, peak or thermal-stop inputs");
Check(!Diagnostics.Live(driveSensors,new(),90).Any(f=>f.Title=="Высокая температура"),"NVMe warning/critical limits cannot generate overheating alerts");
Check(Diagnostics.Live(new(){warningLimit,criticalLimit},new(),90).Any(f=>f.Severity=="unknown"),"Thresholds alone never establish temperature monitoring availability");
var hotSsd=Telemetry.MapSensor("Kingston NV1","Temperature #2",LibreHardwareMonitor.Hardware.HardwareType.Storage,LibreHardwareMonitor.Hardware.SensorType.Temperature,2,84);
Check(Diagnostics.Live(new(){hotSsd},new(),90).Any(f=>f.Title=="Высокая температура"),"Real 84 degree SSD reading remains visible and warns");
Check(Telemetry.MapSensor("CPU","Core 10",LibreHardwareMonitor.Hardware.HardwareType.Cpu,LibreHardwareMonitor.Hardware.SensorType.Temperature,10,98).Type=="Temperature","CPU temperature index 10 is not an NVMe threshold");
Check(ErrorCodes.Explain("0x80070005",false).Contains("(5)"),"HRESULT_FROM_WIN32 unwraps access-denied code");
Check(ErrorCodes.Explain("0x124",true).Contains("WHEA_UNCORRECTABLE_ERROR")&&!ErrorCodes.Explain("0x124",false).Contains("WHEA_UNCORRECTABLE_ERROR"),"BSOD and Win32 numeric domains stay separate");
var scaled=new Measurement("ram_copy",32768,"MiB/s","ok","measured",4);
Check(MeasurementDisplay.Format(scaled).EndsWith("GiB/s")&&MeasurementDisplay.Points(scaled)==32000&&scaled.Value==32768,"Human units and DT points are derived without modifying raw measurements");
Check(MeasurementDisplay.Points(scaled with{Status="skipped"})==null&&MeasurementDisplay.Points(scaled with{Value=double.NaN})==null&&MeasurementDisplay.Points(scaled with{Seconds=0})==null,"Skipped, non-finite or untimed values never receive DT points");
Check(MeasurementDisplay.Points(new("disk_latency",1,"ms","ok","measured",1))==null&&MeasurementDisplay.Points(new("ram_errors",0,"errors","ok","measured",1))==null,"Latency and integrity counts are not falsely presented as throughput scores");
var delta=Telemetry.MapSensor("CPU","Core #1 Distance to TjMax",LibreHardwareMonitor.Hardware.HardwareType.Cpu,LibreHardwareMonitor.Hardware.SensorType.Temperature,20,89);
Check(delta.Type=="TemperatureDelta"&&!Diagnostics.Live(new(){delta},new(),90).Any(f=>f.Title=="Высокая температура"),"Distance to TjMax is a thermal margin, never an absolute overheat reading");
Check(WindowsCounters.CpuUsage(100,1000,50,500)==90&&WindowsCounters.CpuUsage(100,1000,null,null)==null&&WindowsCounters.CpuUsage(10,100,20,200)==null,"Windows CPU load uses counter deltas; first and reset samples stay unknown");
var history=new TelemetryHistory();var started=DateTime.UtcNow;
for(int i=0;i<600;i++)history.Record(new List<SensorRow>{new("Windows","CPU","Load",0,"%","Cpu","/windows/cpu/load"),new("SSD","Composite Temperature","Temperature",40,"°C","Storage"),warningLimit with{Group="Storage"},delta},started.AddSeconds(i*2));
Check(history.Points("cpu",started.AddSeconds(1198),900).Count<=TelemetryHistory.Capacity&&history.Latest.Single(m=>m.Key=="cpu").Primary==0&&history.Latest.Single(m=>m.Key=="cpu").Secondary==null,"Bounded chart history preserves measured zero and missing CPU temperature separately");
Check(history.Latest.Single(m=>m.Key=="storage").Primary==40&&history.Latest.Single(m=>m.Key=="gpu").Primary==null,"Dashboard never plots NVMe thresholds or invents unsupported GPU load");
string csv=Path.Combine(Path.GetTempPath(),"DedsecHistory-"+Guid.NewGuid().ToString("N")+".csv");history.ExportCsv(csv);string exported=File.ReadAllText(csv);File.Delete(csv);Check(exported.Contains("time_utc;component")&&!exported.Contains("NaN")&&!exported.Contains("Infinity")&&TelemetryHistory.Cell("=SUM(A1)").StartsWith("\"'"),"Local CSV exports actual samples and escapes spreadsheet formulas");
string packageProbe=Path.Combine(Path.GetTempPath(),"DedsecComponent-"+Guid.NewGuid().ToString("N"));File.WriteAllBytes(packageProbe,new byte[]{1,2,3});try{using var unexpected=SensorComponents.OpenVerified(packageProbe);throw new Exception("Untrusted sensor package accepted");}catch(InvalidDataException){Check(true,"Modified offline sensor installer is rejected before elevation");}finally{File.Delete(packageProbe);}
Check(Diagnostics.CommitFinding(95,100,98).Severity=="warning"&&Diagnostics.CommitFinding(10,100,90).Severity=="info"&&Diagnostics.CommitFinding(0,0,0).Severity=="unknown","Commit pressure is distinct from RAM load and invalid limits remain unknown");
Check(Diagnostics.VolumeFinding("Q:","NTFS",true).Severity=="warning"&&Diagnostics.VolumeFinding("Q:","NTFS",null).Severity=="unknown","Dirty volumes warn and unavailable flags never become clean results");
Check(Diagnostics.DiskHealthFinding("QA",2,"NVMe").Severity=="critical"&&Diagnostics.DiskHealthFinding("QA",null,"USB").Severity=="unknown","Physical disk health preserves unhealthy and unavailable states");
var shortHistory=new TelemetryHistory();shortHistory.Record(new List<SensorRow>{new("Windows","CPU","Load",100,"%","Cpu","/windows/cpu/load")},started);Check(SessionAnalysis.Read(shortHistory,started,90).All(f=>f.Severity=="unknown"),"One load spike is not classified as a sustained bottleneck");
for(int i=1;i<20;i++)shortHistory.Record(new List<SensorRow>{new("Windows","CPU","Load",99,"%","Cpu","/windows/cpu/load")},started.AddSeconds(i*2));Check(SessionAnalysis.Read(shortHistory,started.AddSeconds(38),90).Any(f=>f.Title.EndsWith("CPU")&&f.Severity=="warning"),"Sustained saturation uses real sample count and time window");
Check(WindowsChecks.Command("system_files").Arguments=="/verifyonly"&&WindowsChecks.Command("image_scan").Arguments.Contains("/ScanHealth")&&!WindowsChecks.Command("image_scan").Arguments.Contains("/RestoreHealth"),"Integrated Windows checks never run repair commands");
Check(WindowsChecks.Explain(new("image_quick",0,"The component store is repairable.",1,false)).Severity=="warning"&&WindowsChecks.Explain(new("image_quick",0,"unrecognized",1,false)).Severity=="unknown"&&WindowsChecks.Explain(new("image_quick",0,"No component store corruption detected.",1,true)).Severity=="unknown","Exit zero, unknown output or cancellation cannot fabricate Windows integrity success");
Check(WindowsChecks.Decode(System.Text.Encoding.Unicode.GetBytes("Windows integrity verification test"))=="Windows integrity verification test","UTF16 Windows diagnostic output decodes without embedded nulls");
var gapDisk=new StorageDisk{Style="GPT",Size=512*StorageBackend.MiB,Partitions=new(){new(){Number=1,Offset=32*StorageBackend.MiB,Size=96*StorageBackend.MiB},new(){Number=2,Offset=192*StorageBackend.MiB,Size=64*StorageBackend.MiB}}};var gaps=StorageBackend.Gaps(gapDisk);Check(gaps.Count==3&&gaps.All(g=>g.Offset%StorageBackend.MiB==0&&g.Size%StorageBackend.MiB==0)&&gaps.All(g=>gapDisk.Partitions.All(p=>g.Offset+g.Size<=p.Offset||g.Offset>=p.Offset+p.Size)),"Unallocated ranges remain aligned and never overlap existing partitions");
if(args.Contains("--storage-read")){var actual=await StorageBackend.Read();Check(actual.Disks.Count>0&&actual.Disks.All(d=>d.Key.Length==64&&d.Size>0),"Embedded storage helper reads actual disk identities and partitions");Console.WriteLine("Storage disks="+actual.Disks.Count+"; partitions="+actual.Disks.Sum(d=>d.Partitions.Count));}
if(args.Contains("--boot-read")){var actual=await StorageBackend.Run(StorageBackend.Request("boot_inventory"));Check(actual.GetProperty("entries").GetArrayLength()>0&&actual.GetProperty("key").GetString()!.Length==64,"Read actual firmware boot inventory without changing entries");Console.WriteLine("Firmware entries="+actual.GetProperty("entries").GetArrayLength());}
if(args.Contains("--windows-check")){var actual=await WindowsChecks.Run("image_quick",CancellationToken.None);Check(actual.Output.Length>0&&!actual.Cancelled,"Actual DISM CheckHealth output captured inside the application");Console.WriteLine("Windows check exit="+actual.ExitCode+"; interpretation="+WindowsChecks.Explain(actual).Severity);}
int publicationIndex=Array.IndexOf(args,"--publication-url");if(publicationIndex>=0)
{
    var publicationBase=new Uri(args[publicationIndex+1]);using var http=new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler{UseProxy=!publicationBase.IsLoopback}){BaseAddress=publicationBase,Timeout=TimeSpan.FromSeconds(20)};
    var fixture=new Report{Completed=true,System=new("DEDSEC QA TEMPORARY CLIENT FIXTURE",4,"Synthetic API fixture — not a real benchmark",8,"Windows QA fixture — temporary test record","X64",new()),DeleteToken=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),Measurements=new(){new("cpu_multi",123.4,"MiB/s","ok","Synthetic disposable client transport test; not a hardware result",5)}};
    if(args.Contains("--framing-probe"))
    {
        var challenge=await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<JsonElement>(http,"api/challenge");var legacy=JsonSerializer.SerializeToNode(fixture.PublicPayload(),Json.Options)!;legacy["nonce"]=challenge.GetProperty("nonce").GetString();
        using var rejected=await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(http,"api/results",legacy);Check((int)rejected.StatusCode==400&&(await rejected.Content.ReadAsStringAsync()).Contains("Transfer encoding unsupported"),"Original streaming JsonContent reproduces the reported registry transport error");
    }
    string? url=null;
    try
    {
        url=await PublicationClient.Send(http,fixture);string id=new Uri(url).Segments.Last();
        var stored=await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<JsonElement>(http,"api/results/"+id);
        Check(stored.GetProperty("measurements")[0].GetProperty("value").GetDouble()==123.4&&stored.GetProperty("system").GetProperty("cpu").GetString()==fixture.System.Cpu,"Production .NET publisher succeeds against the real registry handler and preserves raw data");
        Check(await PublicationClient.Send(http,fixture)==url,"Publication retry with a fresh nonce and saved key returns the same result");
    }
    finally
    {
        if(url!=null){using var request=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Delete,"api/results/"+new Uri(url).Segments.Last());request.Headers.Add("Authorization","Bearer "+fixture.DeleteToken);using var deleted=await http.SendAsync(request);deleted.EnsureSuccessStatusCode();}
    }
    Check(true,"Disposable client fixture removed; no synthetic score retained");
}
if(args.Contains("--ui"))
{
    Exception? uiError=null;var thread=new Thread(()=>
    {
        try
        {
            Console.WriteLine("UI: load application resources");var app=new Dedsec.Tests.App();app.InitializeComponent();Console.WriteLine("UI: create live grid");var rows=new System.Collections.ObjectModel.ObservableCollection<ProcessRow>(Enumerable.Range(0,100).Select(i=>new ProcessRow("Process "+i,i,1,10)));
            var grid=new System.Windows.Controls.DataGrid{ItemsSource=rows,IsReadOnly=true,CanUserAddRows=false,Height=240,Width=500,AutoGenerateColumns=true};
            var window=new System.Windows.Window{Content=grid,Width=520,Height=280,ShowInTaskbar=false,WindowStyle=System.Windows.WindowStyle.ToolWindow};window.Show();window.UpdateLayout();
            Console.WriteLine("UI: window layout ready");
            var viewer=LiveTable.Find<System.Windows.Controls.ScrollViewer>(grid)!;viewer.ScrollToVerticalOffset(40);grid.UpdateLayout();double before=viewer.VerticalOffset;grid.SelectedItem=rows[45];
            var values=rows.Select(r=>r with{Cpu=r.Cpu+1}).ToList();LiveTable.Refresh(grid,rows,values,p=>p.Pid.ToString());grid.UpdateLayout();
            Check(before>0&&Math.Abs(viewer.VerticalOffset-before)<.01&&((ProcessRow)grid.SelectedItem).Pid==45,"Live grid refresh preserves scroll position and selected process while values change");
            Check(((System.Windows.Media.SolidColorBrush)grid.Background).Color.R<80&&LiveTable.Find<System.Windows.Controls.Primitives.ScrollBar>(grid)!.Template!=null,"Packaged application resources provide dark table and explicit scrollbar templates");
            var filtered=System.Windows.Data.CollectionViewSource.GetDefaultView(rows);filtered.Filter=item=>item is ProcessRow process&&process.Pid>=20;grid.UpdateLayout();viewer.ScrollToVerticalOffset(20);grid.UpdateLayout();double filteredOffset=viewer.VerticalOffset;grid.SelectedItem=rows[45];LiveTable.Refresh(grid,rows,rows.Select(r=>r with{Cpu=r.Cpu+1}).ToList(),p=>p.Pid.ToString());grid.UpdateLayout();Check(filteredOffset>0&&Math.Abs(viewer.VerticalOffset-filteredOffset)<.01&&((ProcessRow)grid.SelectedItem).Pid==45,"Filtered live process table preserves scroll and selection during updates");
            var consentWindow=BrandedDialog.Publication(window,new Report{System=info,Completed=true,Measurements=new(){cpu}});consentWindow.Show();consentWindow.UpdateLayout();Check(consentWindow.WindowStyle==System.Windows.WindowStyle.None&&((System.Windows.Media.SolidColorBrush)consentWindow.Background).Color.R<40&&!consentWindow.AcceptButton.IsEnabled,"Publication uses branded dark window and cannot accept without explicit consent");consentWindow.Consent!.IsChecked=true;Check(consentWindow.AcceptButton.IsEnabled,"Publication checkbox gates the actual action button");consentWindow.Close();
            var diskChoice=new System.Windows.Controls.ComboBox{ItemsSource=new[]{new StorageDisk{Number=7,Name="QA-Disk",Size=512UL*1024*1024*1024,Style="GPT"}},DisplayMemberPath="Description",SelectedIndex=0};window.Content=diskChoice;window.UpdateLayout();
            string selectedDiskText=LiveTable.Find<System.Windows.Controls.TextBlock>(diskChoice)?.Text??"";
            Check(selectedDiskText.Contains("QA-Disk")&&!selectedDiskText.Contains("Dedsec.Tests.StorageDisk"),"Dark disk selector renders the model and size instead of the CLR type name");
            var chartView=new DashboardView("cpu","gpu","ram","disk");window.Content=chartView;window.Width=696;window.Height=370;window.UpdateLayout();chartView.Update(history,900,started.AddSeconds(1198));window.UpdateLayout();
            Check(chartView.Children.Count==4&&LiveTable.Find<LivePlot>(chartView)!.ActualHeight>=50,"All dashboard cards render together with usable chart height in the minimum viewport");
            string capture=Path.Combine(Environment.CurrentDirectory,"artifacts","ui-dark-grid.png");Directory.CreateDirectory(Path.GetDirectoryName(capture)!);var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var image=File.Create(capture))png.Save(image);window.Close();app.Shutdown();
        }catch(Exception ex){uiError=ex;}
    });thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();if(!thread.Join(TimeSpan.FromSeconds(25)))throw new TimeoutException("UI regression timed out");if(uiError!=null)throw new Exception("UI regression failed",uiError);
}
if(args.Contains("--hardware"))
{
    using(var counters=new WindowsCounters()){var actual=new List<SensorRow>();counters.Read(actual);await Task.Delay(150);actual.Clear();counters.Read(actual);Check(actual.Any(s=>s.Id=="/windows/cpu/load"&&s.Value>=0&&s.Value<=100)&&actual.Any(s=>s.Id=="/windows/memory/used"&&s.Value>0)&&actual.All(s=>float.IsFinite(s.Value)),"Actual Windows CPU and RAM counters return finite measured data without an extra driver");}
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
