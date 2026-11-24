using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dedsec.Tests;
public partial class MainWindow
{
    // QA only: captures real local counters/workloads, never publishes a result.
    private async Task CapturePreview()
    {
        settings.ChartSeconds=60;settings.Animations=false;var nav=Navigation.Children.OfType<System.Windows.Controls.Button>().Select(b=>b.Tag?.ToString()).ToArray();if(nav.Length!=7||nav.Distinct().Count()!=7||nav.Contains("sensors")||nav.Contains("events"))throw new InvalidDataException("Duplicate navigation view detected");ShowPage("overview");var captureClock=System.Diagnostics.Stopwatch.StartNew();var captured=new Report{System=info};
        await Task.Delay(6000);await Snap("overview-layout");Width=920;Height=640;UpdateLayout();await Snap("overview-small");Width=1200;Height=820;UpdateLayout();
        run=new CancellationTokenSource(TimeSpan.FromSeconds(90));var token=run.Token;thermalRequired=sensors.Any(s=>s.Type=="Temperature");runClock.Restart();GlobalStop.Visibility=Visibility.Visible;
        try
        {
            phase="QA · CPU single";captured.Measurements.Add(await Benchmarks.Cpu(false,token));phase="QA · CPU multi";captured.Measurements.Add(await Benchmarks.Cpu(true,token));phase="QA · GPU";captured.Measurements.Add(await Benchmarks.Gpu(0,5,token));phase="QA · RAM";captured.Measurements.Add(await Task.Run(()=>Benchmarks.MemoryCopy(token),token));captured.Measurements.Add(await Task.Run(()=>Benchmarks.MemoryVerify(token),token));phase="QA · Диск";captured.Measurements.AddRange(await Task.Run(()=>Benchmarks.Disk(settings.DiskRoot,token),token));captured.Completed=captured.Measurements.Count==8&&captured.Measurements.All(m=>m.Status=="ok");
        }
        catch(Exception e){captured.Findings.Add(new("warning","Проверка сборки",e.Message,"Проверьте локальный журнал."));}
        finally{runClock.Stop();GlobalStop.Visibility=Visibility.Collapsed;run.Dispose();run=null;}
        // Wait for actual samples to fill a one-minute view; do not synthesize a graph.
        if(captureClock.Elapsed.TotalSeconds<64)await Task.Delay(TimeSpan.FromSeconds(64-captureClock.Elapsed.TotalSeconds));
        Activity.Text="PREVIEW · реальные локальные показания · публикация не выполнялась";ShowPage("overview");await Snap("overview");Width=920;Height=640;UpdateLayout();await Snap("overview-small");Width=1200;Height=820;UpdateLayout();monitorMode="activity";ShowPage("overview");await Snap("monitoring");monitorMode="system";ShowPage("tests");await Snap("tests");Width=920;Height=640;UpdateLayout();await Snap("tests-small");Width=1200;Height=820;ShowPage("settings");await Snap("settings");Width=920;Height=640;UpdateLayout();await Snap("settings-small");Width=1200;Height=820;
        captured.Findings=await Task.Run(()=>Dedsec.Tests.Diagnostics.Analyze(info!));storage=await StorageBackend.Read();selectedDisk=storage.Disks.FirstOrDefault(d=>d.Boot)??storage.Disks.FirstOrDefault();storageAttempted=true;ShowPage("storage");await Snap("storage");Width=920;Height=640;UpdateLayout();await Snap("storage-small");Width=1200;Height=820;report=captured;auditFindings=captured.Findings.ToList();auditUtc=DateTime.UtcNow;ShowPage("diagnostics");await Snap("diagnostics");Width=920;Height=640;UpdateLayout();await Snap("diagnostics-small");Width=1200;Height=820;auditMode="tools";ShowPage("diagnostics");await Snap("windows-tools");auditMode="state";ShowPage("reports");await Snap("reports");
        var dialog=BrandedDialog.Publication(this,captured);dialog.Show();await Task.Delay(100);dialog.UpdateLayout();CaptureWindow(dialog,"publication");dialog.Close();
        ShowPage("storage");var boot=selectedDisk?.Partitions.FirstOrDefault(p=>p.Boot);if(selectedDisk!=null&&boot!=null)
        {
            var waiting=System.Diagnostics.Stopwatch.StartNew();var resizeTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};
            resizeTimer.Tick+=(_,_)=>{var editorWindow=System.Windows.Application.Current.Windows.OfType<BrandedDialog>().FirstOrDefault(w=>w.Title.Contains("Изменение размера"));if(editorWindow==null)return;var editor=LiveTable.Find<ResizeEditor>(editorWindow);if(editor!=null){resizeTimer.Stop();editor.Boundary.SetFraction(.35);if(editorWindow.AcceptButton.IsEnabled)throw new InvalidDataException("Resize action was enabled without typed consent");CaptureWindow(editorWindow,"resize-editor");editorWindow.Close();}else if(waiting.Elapsed.TotalSeconds>35){resizeTimer.Stop();CaptureWindow(editorWindow,"resize-waiting");editorWindow.Close();}};resizeTimer.Start();await ConfigureResize(selectedDisk,boot);resizeTimer.Stop();if(!IsVisible)throw new InvalidDataException("Closing resize preview hid the main window");
        }
        var updates=new UpdateWindow(this);updates.Window.Show();var updateClock=System.Diagnostics.Stopwatch.StartNew();while(updates.Step==UpdateStep.Checking&&updateClock.Elapsed.TotalSeconds<40)await Task.Delay(100);CaptureWindow(updates.Window,"updates");updates.Window.Close();
        string output=Path.Combine(AppContext.BaseDirectory,"screenshots");File.WriteAllText(Path.Combine(output,"capture-evidence.json"),System.Text.Json.JsonSerializer.Serialize(new{source="Real local counters and fixed workloads; no publication",completed=captured.Completed,measurements=captured.Measurements,sensors=sensors.Count,component=SensorComponents.Status},Json.Options));
        exiting=true;Close();
    }
    private async Task Snap(string name){await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();Capture(name);}
    private static void CaptureWindow(Window window,string name)
    {string dir=Path.Combine(AppContext.BaseDirectory,"screenshots");Directory.CreateDirectory(dir);window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(dir,name+".png"));encoder.Save(file);}
}
