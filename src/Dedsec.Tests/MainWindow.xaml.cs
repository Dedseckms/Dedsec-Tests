using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace Dedsec.Tests;
public partial class MainWindow : Window
{
    public bool CanUpdate => run==null&&toolRun==null&&!auditing&&!storageBusy&&!preparingSensors;
    private readonly Settings settings=Settings.Load(); private readonly Telemetry telemetry=new(); private readonly DispatcherTimer timer=new(); private readonly Stopwatch runClock=new(); private string phase=""; private double peakTemperature;
    private readonly Forms.NotifyIcon tray=new(); private SystemInfo? info; private List<SensorRow> sensors=new(); private List<ProcessRow> processes=new(); private Report? report; private CancellationTokenSource? run;
    private string current="overview"; private bool polling,exiting,thermalRequired,publishing; private int gpuIndex; private DateTime? lastSensorRead; private ProgressBar? progress; private TextBlock? runText;
    private DataGrid? liveGrid;
    private readonly ObservableCollection<SensorRow> sensorRows=new();
    private readonly ObservableCollection<ProcessRow> processRows=new();
    public MainWindow()
    {
        InitializeComponent(); VersionLabel.Text="  PREVIEW "+typeof(MainWindow).Assembly.GetName().Version?.ToString(3); settings.ChartSeconds=settings.ChartSeconds is 60 or 300 or 900?settings.ChartSeconds:300;settings.StressSeconds=settings.StressSeconds is 30 or 60 or 180 or 300?settings.StressSeconds:60; BuildNavigation(); ApplyAppearance();
        var workArea=SystemParameters.WorkArea;MinWidth=Math.Min(MinWidth,workArea.Width);MinHeight=Math.Min(MinHeight,workArea.Height);Width=Math.Min(Width,workArea.Width);Height=Math.Min(Height,workArea.Height);
        tray.Text="Dedsec Tests"; tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Restore);
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized&&settings.Tray){Hide();tray.Visible=true;}};
        var menu=new Forms.ContextMenuStrip(); menu.Items.Add("Открыть",null,(_,_)=>Dispatcher.Invoke(Restore)); menu.Items.Add("Остановить тест",null,(_,_)=>Dispatcher.Invoke(()=>run?.Cancel())); menu.Items.Add("Выход",null,(_,_)=>Dispatcher.Invoke(()=>{exiting=true;Close();})); tray.ContextMenuStrip=menu;
        timer.Interval=TimeSpan.FromSeconds(2); timer.Tick+=async(_,_)=>{if(run!=null&&lastSensorRead.HasValue&&(DateTime.UtcNow-lastSensorRead.Value).TotalSeconds>15){run.Cancel();Activity.Text="Тест остановлен: мониторинг не отвечает";}await Poll();}; Loaded+=async(_,_)=>await Initialize(); Closing+=(_,e)=>{ if(storageWrite){e.Cancel=true;Activity.Text="Операция диска ещё выполняется. Дождитесь результата перед закрытием.";return;}if(!exiting&&settings.Tray) {e.Cancel=true;Hide();tray.Visible=true;return;} run?.Cancel();toolRun?.Cancel();timer.Stop();tray.Dispose(); if(!polling)telemetry.Dispose(); };
    }
    private void BuildNavigation()
    {
        var names=new Dictionary<string,string>{{"overview","Мониторинг"},{"tests","Тестирование"},{"processes","Процессы"},{"diagnostics","Диагностика"},{"storage","Диски"},{"reports","Отчёты"},{"settings","Настройки"}};
        var existing=Navigation.Children.OfType<Button>().ToArray();foreach(var b in existing)Navigation.Children.Remove(b);
        foreach(var item in names){var p=new StackPanel{Orientation=Orientation.Horizontal};p.Children.Add(Icons.Create(item.Key));p.Children.Add(new TextBlock{Text=item.Value,VerticalAlignment=VerticalAlignment.Center});var b=new Button{Tag=item.Key,Content=p,Padding=new Thickness(10,8,4,8),HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=Navigate;Navigation.Children.Add(b);}
    }
    private async Task Initialize()
    {
        bool capture=Environment.GetCommandLineArgs().Contains("--capture");
        if(!capture && (!settings.Agreement || settings.AgreementVersion!="2026-11-24-disks")) { if(!Agreement()) {exiting=true;Close();return;} settings.Agreement=true;settings.AgreementVersion="2026-11-24-disks";settings.Save(); }
        Page.Children.Add(Txt("Определяем конфигурацию…",28)); info=await Task.Run(Inventory.Read); Activity.Text="Готово · данные хранятся локально"; ShowPage("overview");timer.Start();await Poll();
        // Update helper waits for a successfully loaded UI before deleting its rollback copy.
        var args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"--update-ready"); if(index>=0&&index+1<args.Length) {string marker=System.IO.Path.GetFullPath(args[index+1]); string local=System.IO.Path.GetFullPath(Settings.DataDir)+System.IO.Path.DirectorySeparatorChar;if(marker.StartsWith(local,StringComparison.OrdinalIgnoreCase))File.WriteAllText(marker,"ready");}
        if(capture)await CapturePreview();
    }
    private bool Agreement()
    {
        string file=System.IO.Path.Combine(AppContext.BaseDirectory,"LICENSE-RU.txt");
        string text=File.Exists(file)?File.ReadAllText(file):"Условия недоступны. Установите полную сборку из GitHub Releases.";
        if(!File.Exists(file)){BrandedDialog.Notify(this,"Соглашение недоступно",text);return false;}
        return Confirm("Лицензия и локальная диагностика",text,"Я прочитал условия и разрешаю чтение конфигурации, датчиков, процессов и журналов Windows локально","Принять и продолжить");
    }
    private bool Confirm(string title,string body,string checkText,string action)=>BrandedDialog.Confirm(this,title,body,checkText,action);
    private async Task Poll()
    {
        if(polling)return; polling=true;
        try
        {
            var state=await Task.Run(()=> (Sensors:telemetry.Read(),Processes:telemetry.Processes()));sensors=state.Sensors;processes=state.Processes;lastSensorRead=DateTime.UtcNow;
            double max=sensors.Where(s=>s.Type=="Temperature").Select(s=>(double)s.Value).DefaultIfEmpty(0).Max();
            if(run!=null){peakTemperature=Math.Max(peakTemperature,max);if(!run.IsCancellationRequested)Activity.Text=$"{phase} · {runClock.Elapsed:mm\\:ss} · {(max>0?$"{max:F0} °C":"нет температурного датчика")}";}
            if(run!=null&&thermalRequired&&max==0){run.Cancel();Activity.Text="Тест остановлен: температурные датчики перестали отвечать";}
            if(run!=null&&max>=settings.TemperatureLimit){Activity.Text=$"Остановлено: {max:F0} °C ≥ лимита {settings.TemperatureLimit} °C";run.Cancel();}
            timeline.Record(sensors,DateTime.UtcNow);RefreshDashboard();if(storageWrite)Activity.Text=$"Операция диска · {storageClock.Elapsed.Hours:00}:{storageClock.Elapsed.Minutes:00}:{storageClock.Elapsed.Seconds:00} · Windows выполняет изменение, дождитесь результата";
            if(liveGrid!=null&&current=="overview")LiveTable.Refresh(liveGrid,sensorRows,sensors,s=>s.Id.Length>0?s.Id:s.Device+"/"+s.Name+"/"+s.Type);
            if(liveGrid!=null&&current=="processes")LiveTable.Refresh(liveGrid,processRows,processes,p=>p.Pid.ToString());
        }
        catch(Exception ex){Activity.Text="Мониторинг: "+ex.Message;}finally{polling=false;}
    }
    private void ShowPage(string name)
    {
        current=name;Page.Children.Clear();FixedPage.Children.Clear();FixedPage.Visibility=Visibility.Collapsed;PageScroll.Visibility=Visibility.Visible;dashboard=null;monitorSummary=null;monitorAlert=null;liveGrid=null;PageScroll.ScrollToTop();
        foreach(var child in Navigation.Children.OfType<Button>())child.Background=Brush(child.Tag as string==name?"#34452A":"#222B34");
        switch(name){case "overview":Overview();break;case "tests":TestsPage();break;case "processes":ShowProcesses();break;case "diagnostics":Diagnostics();break;case "storage":StoragePage();break;case "reports":Reports();break;case "settings":SettingsPage();break;}
        if(settings.Animations)Page.BeginAnimation(OpacityProperty,new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(180)));
    }
    private void Header(string eyebrow,string title,string subtitle){Page.Children.Add(Txt(eyebrow,10,"#C1FA55"));Page.Children.Add(Txt(title,27));Page.Children.Add(Txt(subtitle,13,"#96A2AF",new Thickness(0,4,0,14)));}
    private void Overview()=>ShowDashboard();
    private async Task Start(string mode)
    {
        if(run!=null||toolRun!=null||auditing||storageBusy||preparingSensors){Activity.Text="Сначала дождитесь завершения текущей операции.";return;}if(info==null)return;
        bool temperature=sensors.Any(s=>s.Type=="Temperature");
        string coverage=$"CPU: {(sensors.Any(s=>s.Group=="Cpu"&&s.Type=="Temperature")?"температура доступна":"температура НЕ контролируется")}; GPU: {(sensors.Any(s=>s.Group.StartsWith("Gpu")&&s.Type=="Temperature")?"температура доступна":"температура НЕ контролируется")}.";
        if(!Confirm("Нагрузочный тест",$"Будет запущен режим: {mode}.\n\nCPU/GPU тесты повышают нагрузку и нагрев. RAM-тест выделяет до 256 MiB. Дисковый тест записывает временный файл 256 MiB на {settings.DiskRoot}; это создаёт износ и может занять время на HDD/USB.\n\nЛимит температур: {settings.TemperatureLimit} °C. {coverage} {(temperature?"Есть доступные температурные датчики; проверяется максимум всех доступных температур.":"Температурный контроль НЕДОСТУПЕН. Следите за состоянием оборудования самостоятельно.")}\n\nПрогон можно отменить. Автотест пропускает диск при предупреждении SMART. Это не полная проверка всего оборудования и не замена MemTest86 или сервисной диагностике.","Работа сохранена, я разрешаю выбранную нагрузку","Начать тест"))return;
        thermalRequired=temperature;peakTemperature=0;runClock.Restart();phase="Проверки системы";run=new CancellationTokenSource();GlobalStop.Visibility=Visibility.Visible;var token=run.Token;var r=new Report{System=info,Profile=mode=="stress"?"stress":"standard"};report=r;ShowPage("tests");Activity.Text="Тест выполняется · можно остановить";
        try
        {
            r.Findings=await Task.Run(()=>Dedsec.Tests.Diagnostics.Analyze(info),token);auditFindings=r.Findings.ToList();auditUtc=DateTime.UtcNow;
            if(mode=="stress")
            {
                phase="Нагрузка CPU/GPU";
                using var duration=CancellationTokenSource.CreateLinkedTokenSource(token); duration.CancelAfter(TimeSpan.FromSeconds(settings.StressSeconds+10));
                var tasks=new List<Task<Measurement>>{Benchmarks.Cpu(true,duration.Token,settings.StressSeconds)};if(settings.IncludeGpu)tasks.Add(Benchmarks.Gpu(gpuIndex,settings.StressSeconds,duration.Token));
                var stress=await Task.WhenAll(tasks);r.Measurements.AddRange(stress.Select(m=>m with{Metric="stress_"+m.Metric}));r.Completed=true;
            }
            else
            {
                var steps=new List<(string Label,Func<Task<List<Measurement>>> Work)>();
                if(mode=="auto"||mode=="cpu"){steps.Add(("CPU · один поток",async()=>new(){await Benchmarks.Cpu(false,token)}));steps.Add(("CPU · все потоки",async()=>new(){await Benchmarks.Cpu(true,token)}));}
                if(mode=="gpu"||mode=="auto"&&settings.IncludeGpu)steps.Add(("GPU · compute",async()=>new(){await Benchmarks.Gpu(gpuIndex,5,token)}));
                if(mode=="ram"||mode=="auto"&&settings.IncludeMemory){steps.Add(("RAM · копирование",()=>Task.Run(()=>new List<Measurement>{Benchmarks.MemoryCopy(token)},token)));steps.Add(("RAM · целостность",()=>Task.Run(()=>new List<Measurement>{Benchmarks.MemoryVerify(token)},token)));}
                if(mode=="disk"||mode=="auto"&&settings.IncludeDisk)steps.Add(("Диск · временный файл",()=>Task.Run(()=>r.Findings.Any(f=>f.Severity=="critical")?new List<Measurement>{new("disk_read",0,"MiB/s","skipped","Пропущено из-за предупреждения SMART",0)}:Benchmarks.Disk(settings.DiskRoot,token),token)));
                for(int i=0;i<steps.Count;i++)
                {
                    token.ThrowIfCancellationRequested();phase=steps[i].Label;if(runText!=null)runText.Text=steps[i].Label;if(progress!=null)progress.Value=i*100d/steps.Count;
                    if(lastSensorRead.HasValue&&(DateTime.UtcNow-lastSensorRead.Value).TotalSeconds>15)throw new InvalidOperationException("Мониторинг перестал отвечать: тест остановлен");
                    try{var measured=await steps[i].Work();r.Measurements.AddRange(measured);foreach(var failed in measured.Where(m=>m.Status=="failed"))r.Findings.Add(new("warning",steps[i].Label+": проверка не пройдена",failed.Detail,"Сохраните данные и остановите дальнейшие нагрузки. Проверьте повторяемость, температуры и драйверы. Ошибка вычислений может быть связана с аппаратной нестабильностью или программным сбоем; один тест не определяет виновный компонент."));if(measured.Any(m=>m.Status=="failed"))throw new InvalidDataException("Проверка вычислений не пройдена; дальнейшие нагрузки отменены.");}catch(OperationCanceledException){throw;}catch(InvalidDataException){throw;}catch(Exception ex){r.Findings.Add(new("warning",steps[i].Label+" не завершён",ex.Message,"Проверьте права доступа, поддержку оборудования и драйвер. Остальные тесты продолжатся."));}
                }
                token.ThrowIfCancellationRequested();r.Completed=true;
            }
            Activity.Text="Прогон завершён · отчёт сохранён локально";
        }
        catch(OperationCanceledException){r.Completed=false;r.Findings.Add(new("warning","Прогон остановлен","Пользователь, таймаут или температурный лимит остановил тест.","Неполный прогон не публикуется. Проверьте температуры и повторите после устранения причины."));Activity.Text="Тест остановлен · неполный отчёт сохранён";}
        catch(Exception ex){r.Findings.Add(new("warning","Ошибка прогона",ex.Message,"Проверьте оборудование и повторите тест."));Activity.Text="Прогон завершился с ошибкой";}
        finally{runClock.Stop();GlobalStop.Visibility=Visibility.Collapsed;r.Findings.Add(new(peakTemperature>=settings.TemperatureLimit?"warning":peakTemperature>0?"info":"unknown","Пиковая наблюдавшаяся температура",peakTemperature>0?$"Максимум доступных датчиков за прогон: {peakTemperature:F0} °C. Длительность процесса: {runClock.Elapsed.TotalSeconds:F0} с.":"Во время прогона температуры не были получены.","Это максимум полученных показаний, не гарантия отсутствия кратковременных пиков. Рабочие пределы нужно сверять с моделью компонента."));run.Dispose();run=null;SaveReport(r);ShowPage("reports");}
    }
    private void ShowProcesses()
    {
        processRows.Clear();foreach(var row in processes)processRows.Add(row);var root=PageWorkspace("Процессы",$"До 300 процессов · CPU нормирован на {Environment.ProcessorCount} потоков · имена и PID остаются локально");var body=new Grid();body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());AddRow(root,body,1);
        var search=new TextBox{Margin=new Thickness(0,0,0,12),ToolTip="Поиск по имени процесса или PID"};body.Children.Add(search);var view=System.Windows.Data.CollectionViewSource.GetDefaultView(processRows);view.Filter=item=>item is ProcessRow p&&(string.IsNullOrWhiteSpace(search.Text)||p.Name.Contains(search.Text,StringComparison.OrdinalIgnoreCase)||p.Pid.ToString().Contains(search.Text));search.TextChanged+=(_,_)=>view.Refresh();
        liveGrid=new DataGrid{ItemsSource=processRows,IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,RowHeight=30};foreach(var column in new[]{("Процесс","Name"),("PID","Pid"),("CPU, %","Cpu"),("RAM, MiB","MemoryMb")})liveGrid.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new System.Windows.Data.Binding(column.Item2),Width=new DataGridLength(column.Item2=="Name"?2:1,DataGridLengthUnitType.Star)});AddRow(body,liveGrid,1);
    }
    private async Task Scan()
    {
        if(info==null||run!=null||toolRun!=null||auditing||storageBusy||preparingSensors)return;auditing=true;Activity.Text="Проверяем систему, накопители и журналы…";ShowPage("diagnostics");
        try{var findings=await Task.Run(()=>Dedsec.Tests.Diagnostics.Analyze(info));findings.AddRange(SessionAnalysis.Read(timeline,DateTime.UtcNow,settings.TemperatureLimit));findings.AddRange(Dedsec.Tests.Diagnostics.Live(sensors,processes,settings.TemperatureLimit));auditFindings=findings;auditUtc=DateTime.UtcNow;SaveReport(new Report{System=info,Findings=findings});Activity.Text=$"Диагностика завершена · {findings.Count} наблюдений и проверок";}
        catch(Exception ex){Activity.Text="Диагностика не завершена: "+ex.Message;}
        finally{auditing=false;if(current=="diagnostics")ShowPage("diagnostics");}
    }
    private async Task Publish(Report r)
    {
        if(run!=null||publishing||r.ResultUrl!=null||!r.Completed||r.Profile=="stress")return;

        if(BrandedDialog.Publication(this,r).ShowDialog()!=true)return;
        publishing=true;Activity.Text="Отправляем отчёт и проверяем его сохранение на сервере…";
        try { r.DeleteToken??=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));r.PublicationAttemptUtc=DateTime.UtcNow;r.PublicationError=null;SaveReport(r);using var http=new HttpClient{BaseAddress=new Uri("https://tests.dedseckms.ru/"),Timeout=TimeSpan.FromSeconds(45)};r.ResultUrl=await PublicationClient.Send(http,r);SaveReport(r);Activity.Text="Опубликовано · сервер подтвердил результат · ссылка сохранена";ShowPage("reports");if(BrandedDialog.Ask(this,"Отчёт опубликован","Сервер подтвердил сохранение результата.\n\n"+r.ResultUrl,"Открыть результат"))Open(r.ResultUrl);}
        catch(Exception ex){r.PublicationError=ex.Message;try{SaveReport(r);}catch{}Activity.Text="Публикация не подтверждена · отчёт сохранён локально";ShowPage("reports");BrandedDialog.Notify(this,"Публикация не подтверждена","Отчёт и ключ сохранены локально. Повторите отправку: тот же ключ не создаст дубль.\n\n"+ex.Message);}finally{publishing=false;}
    }
    private async Task Delete(Report r)
    {
        if(r.ResultUrl==null||r.DeleteToken==null)return;if(!BrandedDialog.Ask(this,"Удалить публикацию?","Результат будет удалён с сайта. Локальный отчёт останется.","Удалить с сайта"))return;
        try{var id=new Uri(r.ResultUrl).Segments.Last();using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};using var request=new HttpRequestMessage(HttpMethod.Delete,"https://tests.dedseckms.ru/api/results/"+id);request.Headers.Add("Authorization","Bearer "+r.DeleteToken);var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();r.ResultUrl=null;r.DeleteToken=null;SaveReport(r);ShowPage("reports");Activity.Text="Публичный результат удалён";}catch(Exception ex){Activity.Text="Удаление: "+ex.Message;}
    }
    private static void SaveReport(Report r){string dir=System.IO.Path.Combine(Settings.DataDir,"reports");Directory.CreateDirectory(dir);File.WriteAllText(System.IO.Path.Combine(dir,r.CreatedUtc.ToString("yyyyMMdd-HHmmss-ffff")+".json"),JsonSerializer.Serialize(r,Json.Options));}
    private void Export(Report r,bool html)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter=html?"HTML report|*.html":"JSON report (содержит локальный ключ удаления)|*.json",FileName="DedsecTests-"+r.CreatedUtc.ToString("yyyyMMdd-HHmmss")+(html?".html":".json")};if(dialog.ShowDialog()!=true)return;
        if(!html)File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(r,Json.Options));else{string E(string? s)=>System.Net.WebUtility.HtmlEncode(s)??"";var sb=new StringBuilder("<!doctype html><html lang=ru><meta charset=utf-8><meta name=viewport content='width=device-width'><title>Dedsec Tests · отчёт</title><style>body{font:16px system-ui;background:#0b0e12;color:#e9eff5;max-width:960px;margin:40px auto;padding:20px}section{background:#151b22;padding:20px;margin:15px 0;border-radius:12px}h1{color:#c1fa55}small{color:#a9b5c1}</style><h1>DEDSEC / TESTS</h1>");sb.Append($"<p>{E(r.System?.Cpu)}<br>{E(r.System?.Gpu)}<br>{E(r.System?.Windows)}</p><small>{E(r.SuiteVersion)} · {E(r.CreatedUtc.ToString("O"))} · completed={r.Completed}</small>");sb.Append($"<p>{E(MeasurementDisplay.ScoreMethod)}</p>");foreach(var m in r.Measurements)sb.Append($"<section><h2>{E(MeasurementDisplay.Name(m.Metric))}: {E(MeasurementDisplay.Headline(m))}</h2><p>{m.Seconds:F3} с · {E(m.Status)} · {E(m.Detail)}</p></section>");foreach(var f in r.Findings)sb.Append($"<section><h2>{E(f.Title)}</h2><p>{E(f.Evidence)}</p><p>{E(f.Solution)}</p></section>");File.WriteAllText(dialog.FileName,sb.ToString());}Activity.Text="Отчёт экспортирован";
    }
    private void Capture(string page){string dir=System.IO.Path.Combine(AppContext.BaseDirectory,"screenshots");Directory.CreateDirectory(dir);UpdateLayout();var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var stream=File.Create(System.IO.Path.Combine(dir,page+".png"));encoder.Save(stream);}
    private static SolidColorBrush Brush(string color)=>new((Color)(ColorConverter.ConvertFromString(color)??Colors.Lime));
    private static TextBlock Txt(string text,double size=14,string? color=null,Thickness? margin=null)=>new(){Text=text,FontSize=size,Foreground=color==null?Brush("#EDF2F6"):Brush(color),Margin=margin??new Thickness(0,4,0,4)};
    private Border WrapCard(UIElement child)=>new(){Style=(Style)FindResource("Card"),Child=child};
    private Border HardwareCard(string asset,string label,string title,string detail){var p=new DockPanel();var image=new Image{Source=new BitmapImage(new Uri($"pack://application:,,,/Assets/{asset}.png")),Width=70,Height=70,Margin=new Thickness(0,0,16,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(image,Dock.Left);p.Children.Add(image);var text=new StackPanel();text.Children.Add(Txt(label,10,"#96A2AF"));text.Children.Add(Txt(title,18));text.Children.Add(Txt(detail,12,"#96A2AF"));p.Children.Add(text);return WrapCard(p);}
    private Border Card(string label,string title,string detail){var p=new StackPanel();p.Children.Add(Txt(label,11,"#96A2AF"));p.Children.Add(Txt(title,21));p.Children.Add(Txt(detail,12,"#96A2AF"));return WrapCard(p);}
    private Border FindingCard(Finding f){var p=new StackPanel();p.Children.Add(Txt(f.Severity.ToUpperInvariant()+" / "+f.Title,18,f.Severity=="critical"?"#FF7777":f.Severity=="warning"?"#FFCC77":"#C1FA55"));p.Children.Add(Txt(f.Evidence,14));p.Children.Add(Txt(f.Solution,14,"#A7B5C1"));if(f.Link!=null)p.Children.Add(Action("Открыть",()=>Open(f.Link)));return WrapCard(p);}
    private Button Action(string text,System.Action action,bool primary=false){var b=new Button{Content=text};if(primary)b.Style=(Style)FindResource("Primary");b.Click+=(_,_)=>action();return b;}
    private Button Action(string text,Func<Task> action,bool primary=false){var b=new Button{Content=text};if(primary)b.Style=(Style)FindResource("Primary");b.Click+=async(_,_)=>{b.IsEnabled=false;try{await action();}catch(Exception ex){Activity.Text=ex.Message;}finally{b.IsEnabled=true;}};return b;}
    private void ApplyAppearance(){Resources["Accent"]=Brush(settings.Accent);Resources["Base"]=Brush(settings.WindowColor);System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.Default;}
    private static void Open(string url){try{bool allowed=Directory.Exists(url)||url=="eventvwr.msc"||Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "ms-settings" or "windowsdefender";if(allowed)Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{}}
    private void AdminCheck(string command,string args){if(!BrandedDialog.Ask(this,"Системная диагностика",$"Открыть {command} {args} с запросом прав администратора?","Открыть консоль"))return;try{Process.Start(new ProcessStartInfo("cmd.exe",$"/k {command} {args}"){UseShellExecute=true,Verb="runas"});}catch(Exception ex){Activity.Text=ex.Message;}}
    private void Navigate(object sender,RoutedEventArgs e)=>ShowPage((string)((Button)sender).Tag);
    private void DragTitle(object sender,MouseButtonEventArgs e){if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else if(e.ButtonState==MouseButtonState.Pressed)DragMove();}
    private void Restore(){Show();WindowState=WindowState.Normal;Activate();tray.Visible=false;}
    private void Minimize(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseWindow(object sender,RoutedEventArgs e)=>Close();
    private void StopAll(object sender,RoutedEventArgs e){run?.Cancel();toolRun?.Cancel();}
    private void OpenWebsite(object sender,RoutedEventArgs e)=>Open("https://tests.dedseckms.ru");
}
public sealed class UniformGridColumns : System.Windows.Controls.Primitives.UniformGrid{public UniformGridColumns(int columns){Columns=columns;}}
