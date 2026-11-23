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
        if (e.Args.Length == 4 && e.Args[0] == "--gpu-worker") { try { var value=GpuBenchmark.Run(int.Parse(e.Args[1]),Math.Clamp(double.Parse(e.Args[2],System.Globalization.CultureInfo.InvariantCulture),.1,600),System.Threading.CancellationToken.None); File.WriteAllText(e.Args[3],System.Text.Json.JsonSerializer.Serialize(value,Json.Options)); Shutdown(0); } catch(Exception ex) { File.WriteAllText(e.Args[3],System.Text.Json.JsonSerializer.Serialize(new Measurement("gpu_compute",0,"Miter/s","skipped",ex.Message,0),Json.Options)); Shutdown(0); } return; }
        if (e.Args.Contains("--smoke")) { _ = Smoke(); return; }
        instance=new System.Threading.Mutex(true,@"Local\DedsecTests",out bool created);if(!created){MessageBox.Show("Dedsec Tests уже запущен. Проверьте окно или значок в трее.");Shutdown(0);return;}
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Dedsec Tests · ошибка"); args.Handled = true; };
        base.OnStartup(e);
        new MainWindow().Show();
    }
    private async Task Smoke()
    {
        try { var inventory = await Task.Run(Inventory.Read); var results = await Benchmarks.Smoke(); File.WriteAllText(evidencePath(), System.Text.Json.JsonSerializer.Serialize(new { inventory, results }, Json.Options)); Shutdown(0); }
        catch (Exception ex) { File.WriteAllText(evidencePath(), ex.ToString()); Shutdown(1); }
    }
    private static string evidencePath() => Path.Combine(AppContext.BaseDirectory, "smoke.json");
}
