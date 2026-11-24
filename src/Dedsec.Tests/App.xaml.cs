using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Threading.Tasks;
namespace Dedsec.Tests;
public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 4 && e.Args[0] == "--gpu-worker") { try { var value=GpuBenchmark.Run(int.Parse(e.Args[1]),Math.Clamp(double.Parse(e.Args[2],System.Globalization.CultureInfo.InvariantCulture),.1,600),System.Threading.CancellationToken.None); File.WriteAllText(e.Args[3],System.Text.Json.JsonSerializer.Serialize(value,Json.Options)); Shutdown(0); } catch(Exception ex) { string status=ex is InvalidOperationException or System.IO.InvalidDataException or TimeoutException?"failed":"skipped";File.WriteAllText(e.Args[3],System.Text.Json.JsonSerializer.Serialize(new Measurement("gpu_compute",0,"Miter/s",status,ex.Message,0),Json.Options)); Shutdown(0); } return; }
        if (e.Args.Contains("--smoke")) { _ = Smoke(); return; }
        int waiting=Array.IndexOf(e.Args,"--wait-pid");if(waiting>=0&&waiting+1<e.Args.Length&&int.TryParse(e.Args[waiting+1],out int parent)&&parent!=Environment.ProcessId)try{using var previous=System.Diagnostics.Process.GetProcessById(parent);if(!previous.WaitForExit(15000)){Shutdown(1);return;}}catch(ArgumentException){}
        instance=new System.Threading.Mutex(true,e.Args.Contains("--capture")?@"Local\DedsecTestsCapture":@"Local\DedsecTests",out bool created);if(!created){MessageBox.Show("Dedsec Tests уже запущен. Проверьте окно или значок в трее.");Shutdown(0);return;}
        DispatcherUnhandledException += (_, args) => { if(e.Args.Contains("--capture")){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"capture-error.txt"),args.Exception.ToString());args.Handled=true;Shutdown(1);return;}MessageBox.Show(args.Exception.Message, "Dedsec Tests · ошибка"); args.Handled = true; };
        base.OnStartup(e);
        var window=new MainWindow();if(e.Args.Contains("--capture")){window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-16000;window.Top=-16000;window.ShowInTaskbar=false;}window.Show();
    }
    private async Task Smoke()
    {
        try { using var component=SensorComponents.OpenVerified(Path.Combine(AppContext.BaseDirectory,"tools","PawnIO_setup.exe"));if(!File.Exists(Path.Combine(AppContext.BaseDirectory,"tools","PawnIO-2.2.0-source.zip")))throw new FileNotFoundException("Bundled component sources missing");var inventory = await Task.Run(Inventory.Read); var results = await Benchmarks.Smoke();var sensors=await Task.Run(()=>{using var monitor=new Telemetry();monitor.Read();System.Threading.Thread.Sleep(150);return monitor.Read();});File.WriteAllText(evidencePath(), System.Text.Json.JsonSerializer.Serialize(new { inventory, results,sensors,component="Pinned offline package verified; no installation performed" }, Json.Options)); Shutdown(0); }
        catch (Exception ex) { File.WriteAllText(evidencePath(), ex.ToString()); Shutdown(1); }
    }
    private static string evidencePath() => Path.Combine(AppContext.BaseDirectory, "smoke.json");
}
