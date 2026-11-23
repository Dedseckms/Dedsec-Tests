using System;
using System.Collections.Generic;
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
    public bool CanUpdate => run==null;
    private readonly Settings settings=Settings.Load(); private readonly Telemetry telemetry=new(); private readonly DispatcherTimer timer=new(); private readonly Stopwatch runClock=new(); private string phase=""; private double peakTemperature;
    private readonly Forms.NotifyIcon tray=new(); private SystemInfo? info; private List<SensorRow> sensors=new(); private List<ProcessRow> processes=new(); private Report? report; private CancellationTokenSource? run;
    private string current="overview"; private bool polling,exiting,thermalRequired,publishing; private int gpuIndex; private DateTime? lastSensorRead; private TextBlock? liveSummary; private StackPanel? liveIssues; private ProgressBar? progress; private TextBlock? runText; private readonly List<double> history=new(); private Polyline? graph;
    public MainWindow()
    {
        InitializeComponent(); BuildNavigation(); ApplyAppearance();
        var workArea=SystemParameters.WorkArea;MinWidth=Math.Min(MinWidth,workArea.Width);MinHeight=Math.Min(MinHeight,workArea.Height);Width=Math.Min(Width,workArea.Width);Height=Math.Min(Height,workArea.Height);
        tray.Text="Dedsec Tests"; tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Restore);
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized&&settings.Tray){Hide();tray.Visible=true;}};
        var menu=new Forms.ContextMenuStrip(); menu.Items.Add("Открыть",null,(_,_)=>Dispatcher.Invoke(Restore)); menu.Items.Add("Остановить тест",null,(_,_)=>Dispatcher.Invoke(()=>run?.Cancel())); menu.Items.Add("Выход",null,(_,_)=>Dispatcher.Invoke(()=>{exiting=true;Close();})); tray.ContextMenuStrip=menu;
        timer.Interval=TimeSpan.FromSeconds(2); timer.Tick+=async(_,_)=>{if(run!=null&&lastSensorRead.HasValue&&(DateTime.UtcNow-lastSensorRead.Value).TotalSeconds>15){run.Cancel();Activity.Text="Тест остановлен: мониторинг не отвечает";}await Poll();}; Loaded+=async(_,_)=>await Initialize(); Closing+=(_,e)=>{ if(!exiting&&settings.Tray) {e.Cancel=true;Hide();tray.Visible=true;return;} run?.Cancel();timer.Stop();tray.Dispose(); if(!polling)telemetry.Dispose(); };
    }
    private void BuildNavigation()
    {
        var names=new Dictionary<string,string>{{"overview","Обзор"},{"tests","Тестирование"},{"sensors","Датчики"},{"processes","Процессы"},{"diagnostics","Диагностика"},{"events","События Windows"},{"reports","Отчёты"},{"settings","Настройки"}};
        var existing=Navigation.Children.OfType<Button>().ToArray();foreach(var b in existing)Navigation.Children.Remove(b);
        foreach(var item in names){var p=new StackPanel{Orientation=Orientation.Horizontal};p.Children.Add(Icons.Create(item.Key));p.Children.Add(new TextBlock{Text=item.Value,VerticalAlignment=VerticalAlignment.Center});var b=new Button{Tag=item.Key,Content=p,Padding=new Thickness(12,12,5,12),HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=Navigate;Navigation.Children.Add(b);}
    }
    private async Task Initialize()
    {
        bool capture=Environment.GetCommandLineArgs().Contains("--capture");
        if(!capture && (!settings.Agreement || settings.AgreementVersion!="2026-11-23")) { if(!Agreement()) {exiting=true;Close();return;} settings.Agreement=true;settings.AgreementVersion="2026-11-23";settings.Save(); }
        Page.Children.Add(Txt("Определяем конфигурацию…",28)); info=await Task.Run(Inventory.Read); Activity.Text="Готово · данные хранятся локально"; ShowPage("overview");timer.Start();await Poll();
        // Update helper waits for a successfully loaded UI before deleting its rollback copy.
        var args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"--update-ready"); if(index>=0&&index+1<args.Length) {string marker=System.IO.Path.GetFullPath(args[index+1]); string local=System.IO.Path.GetFullPath(Settings.DataDir)+System.IO.Path.DirectorySeparatorChar;if(marker.StartsWith(local,StringComparison.OrdinalIgnoreCase))File.WriteAllText(marker,"ready");}
        if(capture) {await Task.Delay(1500);Capture("overview");ShowPage("tests");Capture("tests");ShowPage("settings");Capture("settings");report=new(){System=info,Findings=await Task.Run(()=>Dedsec.Tests.Diagnostics.Analyze(info))};ShowPage("diagnostics");Capture("diagnostics");exiting=true;Close();}
    }
    private bool Agreement()
    {
        string file=System.IO.Path.Combine(AppContext.BaseDirectory,"LICENSE-RU.txt");
        string text=File.Exists(file)?File.ReadAllText(file):"Условия недоступны. Установите полную сборку из GitHub Releases.";
        if(!File.Exists(file)){MessageBox.Show(text);return false;}
        return Confirm("Лицензия и локальная диагностика",text,"Я прочитал условия и разрешаю чтение конфигурации, датчиков, процессов и журналов Windows локально","Принять и продолжить");
    }
    private bool Confirm(string title,string body,string checkText,string action)
    {
        var w=new Window{Title=title,Owner=this,Width=660,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.CanResize};
        var p=new DockPanel{Margin=new Thickness(26)};var bottom=new StackPanel();DockPanel.SetDock(bottom,Dock.Bottom);var check=new CheckBox{Content=new TextBlock{Text=checkText,TextWrapping=TextWrapping.Wrap}};var ok=new Button{Content=action,IsEnabled=false,Style=(Style)FindResource("Primary")};check.Checked+=(_,_)=>ok.IsEnabled=true;check.Unchecked+=(_,_)=>ok.IsEnabled=false;ok.Click+=(_,_)=>w.DialogResult=true;bottom.Children.Add(check);bottom.Children.Add(ok);p.Children.Add(bottom);p.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=Txt(body,14)});w.Content=p;return w.ShowDialog()==true;
    }
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
            history.Add(max);if(history.Count>80)history.RemoveAt(0);
            if(current=="overview"){if(liveSummary!=null)liveSummary.Text=$"{(max>0?$"{max:F0} °C":"— °C")}   /   RAM {Inventory.Memory().Load}%   /   {telemetry.Status}";if(graph!=null){graph.Points=new PointCollection(history.Select((v,i)=>new Point(i*7,110-Math.Min(100,v))));}if(liveIssues!=null){liveIssues.Children.Clear();foreach(var finding in Dedsec.Tests.Diagnostics.Live(sensors,processes,settings.TemperatureLimit).Take(2))liveIssues.Children.Add(FindingCard(finding));}}
            if(current=="sensors")ShowSensors();if(current=="processes")ShowProcesses();
        }
        catch(Exception ex){Activity.Text="Мониторинг: "+ex.Message;}finally{polling=false;}
    }
    private void ShowPage(string name)
    {
        current=name;Page.Children.Clear();liveSummary=null;liveIssues=null;graph=null;
        foreach(var child in Navigation.Children.OfType<Button>())child.Background=Brush(child.Tag as string==name?"#34452A":"#222B34");
        switch(name){case "overview":Overview();break;case "tests":TestsPage();break;case "sensors":ShowSensors();break;case "processes":ShowProcesses();break;case "diagnostics":Diagnostics();break;case "events":EventsPage();break;case "reports":Reports();break;case "settings":SettingsPage();break;}
        if(settings.Animations)Page.BeginAnimation(OpacityProperty,new DoubleAnimation(.35,1,TimeSpan.FromMilliseconds(180)));
    }
    private void Header(string eyebrow,string title,string subtitle){Page.Children.Add(Txt(eyebrow,11,"#C1FA55"));Page.Children.Add(Txt(title,34));Page.Children.Add(Txt(subtitle,14,"#96A2AF",new Thickness(0,8,0,24)));}
    private void Overview()
    {
        Header("СИСТЕМА ПОД КОНТРОЛЕМ","Узнай, на что способен твой ПК.","Диагностика, нагрузка и честные измерения. Всё в одном лёгком приложении.");
        var actions=new WrapPanel();actions.Children.Add(Action("Запустить автотест",async()=>await Start("auto"),true));actions.Children.Add(Action("Настроить тесты",()=>ShowPage("tests")));Page.Children.Add(actions);
        var diagnosticsActions=new WrapPanel();diagnosticsActions.Children.Add(Action("Найти проблемы без нагрузки",async()=>await Scan(),true));diagnosticsActions.Children.Add(Action("Ошибки Windows",()=>ShowPage("events")));Page.Children.Add(diagnosticsActions);
        liveIssues=new StackPanel();Page.Children.Add(liveIssues);foreach(var f in Dedsec.Tests.Diagnostics.Live(sensors,processes,settings.TemperatureLimit).Take(2))liveIssues.Children.Add(FindingCard(f));
        var grid=new UniformGridColumns(2);grid.Children.Add(HardwareCard("cpu","ПРОЦЕССОР",info?.Cpu??"…",$"{info?.LogicalCores} логических потоков · {info?.Architecture}"));grid.Children.Add(HardwareCard("gpu","ВИДЕОКАРТА",info?.Gpu??"…","Direct3D 11 compute · адаптер выбирается перед тестом"));grid.Children.Add(HardwareCard("ram","ОПЕРАТИВНАЯ ПАМЯТЬ",$"{info?.RamGb} ГБ",$"Доступно {Inventory.Memory().Available/1073741824d:F1} ГБ · нагрузка {Inventory.Memory().Load}%"));grid.Children.Add(HardwareCard("disk","НАКОПИТЕЛИ",string.Join(" · ",info?.Disks.Select(d=>$"{d.CapacityGb} GB {d.Media}")??Array.Empty<string>()),string.Join(" / ",info?.Disks.Select(d=>d.Model)??Array.Empty<string>())));Page.Children.Add(grid);
        var panel=new StackPanel();panel.Children.Add(Txt("ТЕЛЕМЕТРИЯ В РЕАЛЬНОМ ВРЕМЕНИ",11,"#96A2AF"));liveSummary=Txt("Получаем датчики…",17);panel.Children.Add(liveSummary);var canvas=new Canvas{Height=124,ClipToBounds=true};graph=new Polyline{Stroke=(System.Windows.Media.Brush)FindResource("Accent"),StrokeThickness=2};canvas.Children.Add(graph);panel.Children.Add(canvas);panel.Children.Add(Txt(info?.Windows??"",12,"#96A2AF"));Page.Children.Add(WrapCard(panel));
    }
    private void TestsPage()
    {
        Header("BENCHMARK SUITE / DEDSEC V1","Один запуск. Полная картина.","Сначала сохрани работу. Метрики CPU/GPU/RAM измеряются раздельно; это не рейтинг FPS в играх.");
        var p=new StackPanel();p.Children.Add(Txt("Состав автопрогона",21));var gpu=new CheckBox{Content="GPU · Direct3D 11 compute",IsChecked=settings.IncludeGpu};gpu.Click+=(_,_)=>{settings.IncludeGpu=gpu.IsChecked==true;settings.Save();};p.Children.Add(gpu);var memory=new CheckBox{Content="RAM · копирование и проверка шаблонами",IsChecked=settings.IncludeMemory};memory.Click+=(_,_)=>{settings.IncludeMemory=memory.IsChecked==true;settings.Save();};p.Children.Add(memory);var disk=new CheckBox{Content="Накопитель · запись, чтение, задержка (256 MiB)",IsChecked=settings.IncludeDisk};disk.Click+=(_,_)=>{settings.IncludeDisk=disk.IsChecked==true;settings.Save();};p.Children.Add(disk);
        p.Children.Add(Txt("Видеокарта для теста",12,"#96A2AF"));var adapters=GpuBenchmark.Adapters();var gpuBox=new ComboBox{ItemsSource=adapters,SelectedIndex=Math.Min(gpuIndex,adapters.Count-1),Margin=new Thickness(0,6,0,16)};gpuBox.SelectionChanged+=(_,_)=>gpuIndex=Math.Max(0,gpuBox.SelectedIndex);p.Children.Add(gpuBox);
        p.Children.Add(Txt("Том для временного тестового файла",12,"#96A2AF"));var drives=DriveInfo.GetDrives().Where(d=>(d.DriveType==DriveType.Fixed||d.DriveType==DriveType.Removable)&&d.IsReady).Select(d=>d.Name).ToList();var driveBox=new ComboBox{ItemsSource=drives,SelectedItem=settings.DiskRoot,Margin=new Thickness(0,6,0,16)};driveBox.SelectionChanged+=(_,_)=>{if(driveBox.SelectedItem is string s){settings.DiskRoot=s;settings.Save();}};p.Children.Add(driveBox);
        p.Children.Add(Action("Выбрать папку на накопителе",()=>{using var dialog=new Forms.FolderBrowserDialog{Description="Папка для временного файла 256 MiB. Пользовательские файлы не изменяются.",UseDescriptionForTitle=true};if(dialog.ShowDialog()==Forms.DialogResult.OK){settings.DiskRoot=dialog.SelectedPath;settings.Save();ShowPage("tests");}}));p.Children.Add(Txt("Выбрано: "+settings.DiskRoot+". Для системного тома используется доступная временная папка пользователя; можно выбрать другую папку.",12,"#96A2AF"));
        var actions=new WrapPanel();actions.Children.Add(Action("Автотест",async()=>await Start("auto"),true));actions.Children.Add(Action("CPU",async()=>await Start("cpu")));actions.Children.Add(Action("GPU",async()=>await Start("gpu")));actions.Children.Add(Action("RAM",async()=>await Start("ram")));actions.Children.Add(Action("Диск",async()=>await Start("disk")));actions.Children.Add(Action("Нагрузка CPU + GPU · 60 с",async()=>await Start("stress")));p.Children.Add(actions);Page.Children.Add(WrapCard(p));
        var status=new StackPanel();runText=Txt(run==null?"Готов к запуску":"Тест выполняется…",18);status.Children.Add(runText);progress=new ProgressBar{Minimum=0,Maximum=100,Margin=new Thickness(0,16,0,20)};status.Children.Add(progress);status.Children.Add(Action("Остановить",()=>run?.Cancel()));status.Children.Add(Txt("Фиксированная методика для сравнений. Стресс-тест не публикуется в таблице. При отсутствии температурного датчика автоматическая защита по температуре недоступна.",12,"#96A2AF"));Page.Children.Add(WrapCard(status));
    }
    private async Task Start(string mode)
    {
        if(run!=null){Activity.Text="Уже выполняется тест. Сначала остановите его.";return;}if(info==null)return;
        bool temperature=sensors.Any(s=>s.Type=="Temperature");
        if(!Confirm("Нагрузочный тест",$"Будет запущен режим: {mode}.\n\nCPU/GPU тесты повышают нагрузку и нагрев. RAM-тест выделяет до 256 MiB. Дисковый тест записывает временный файл 256 MiB на {settings.DiskRoot}; это создаёт износ и может занять время на HDD/USB.\n\nЛимит температур: {settings.TemperatureLimit} °C. {(temperature?"Есть доступные температурные датчики; проверяется максимум всех доступных температур.":"Температурный контроль НЕДОСТУПЕН. Следите за состоянием оборудования самостоятельно.")}\n\nПрогон можно отменить. Автотест пропускает диск при предупреждении SMART. Это не полная проверка всего оборудования и не замена MemTest86 или сервисной диагностике.","Работа сохранена, я разрешаю выбранную нагрузку","Начать тест"))return;
        thermalRequired=temperature;peakTemperature=0;runClock.Restart();phase="Проверки системы";run=new CancellationTokenSource();GlobalStop.Visibility=Visibility.Visible;var token=run.Token;var r=new Report{System=info,Profile=mode=="stress"?"stress":"standard"};report=r;ShowPage("tests");Activity.Text="Тест выполняется · можно остановить";
        try
        {
            r.Findings=await Task.Run(()=>Dedsec.Tests.Diagnostics.Analyze(info),token);
            if(mode=="stress")
            {
                phase="Нагрузка CPU/GPU";
                using var duration=CancellationTokenSource.CreateLinkedTokenSource(token); duration.CancelAfter(TimeSpan.FromSeconds(70));
                var tasks=new List<Task<Measurement>>{Benchmarks.Cpu(true,duration.Token,60)};if(settings.IncludeGpu)tasks.Add(Benchmarks.Gpu(gpuIndex,60,duration.Token));
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
    private void ShowSensors(){Page.Children.Clear();Header("ЖИВАЯ ТЕЛЕМЕТРИЯ","Температуры и частоты",telemetry.Status+" · обновление раз в 2 с");var grid=new DataGrid{ItemsSource=sensors,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,Height=470,Background=Brush("#14191F"),Foreground=Brush("#E9F1F6"),RowHeight=30};foreach(var c in new[]{("Устройство","Device"),("Датчик","Name"),("Тип","Type"),("Значение","Value"),("Ед.","Unit")})grid.Columns.Add(new DataGridTextColumn{Header=c.Item1,Binding=new System.Windows.Data.Binding(c.Item2),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});Page.Children.Add(grid);Page.Children.Add(Txt("Для CPU-датчиков на части систем нужны администратор и установленный PawnIO. Установка драйвера не выполняется автоматически. GPU и часть других датчиков могут работать без него.",13,"#96A2AF",new Thickness(0,16,0,0)));Page.Children.Add(Action("Официальный PawnIO",()=>Open("https://pawnio.eu/")));}
    private void ShowProcesses(){Page.Children.Clear();Header("ТОЛЬКО НА ЭТОМ КОМПЬЮТЕРЕ","Что использует ресурсы",$"Топ 80 процессов · CPU нормирован на {Environment.ProcessorCount} потоков · имена и PID не публикуются");var grid=new DataGrid{ItemsSource=processes,IsReadOnly=true,AutoGenerateColumns=true,CanUserAddRows=false,Height=500,RowHeight=28,Foreground=Brush("#E9F1F6")};Page.Children.Add(grid);}
    private async Task Scan()
    {
        if(info==null)return;if(run!=null){Activity.Text="Дождитесь завершения нагрузки";return;}Activity.Text="Проверяем систему и журналы событий…";var findings=await Task.Run(()=>Dedsec.Tests.Diagnostics.Analyze(info));var snapshot=Dedsec.Tests.Diagnostics.Live(sensors,processes,settings.TemperatureLimit);report=new(){System=info,Findings=findings.Concat(snapshot).ToList()};SaveReport(report);ShowPage("diagnostics");Activity.Text=$"Диагностика завершена · предупреждений: {report.Findings.Count(f=>f.Severity is "warning" or "critical")}";
    }
    private void EventsPage()
    {
        Header("ПРИЧИНЫ ВМЕСТО НЕПОНЯТНЫХ КОДОВ","События Windows", "System и Application за 7 дней. Сообщения читаются и сохраняются только локально.");Page.Children.Add(Action("Прочитать журналы",async()=>{Activity.Text="Читаем журналы…";var events=await Task.Run(Dedsec.Tests.Diagnostics.ReadEvents);report??=new(){System=info};report.Findings.RemoveAll(f=>f.Title.Contains("Event ")||f.Title.StartsWith("Журнал "));report.Findings.AddRange(events);SaveReport(report);ShowPage("events");Activity.Text="Журналы обработаны";},true));Page.Children.Add(Action("Открыть Просмотр событий",()=>Open("eventvwr.msc")));foreach(var f in report?.Findings.Where(f=>f.Title.Contains("Event ")||f.Title.StartsWith("Журнал "))??Enumerable.Empty<Finding>())Page.Children.Add(FindingCard(f));
    }
    private void Diagnostics()
    {
        Header("ПРОВЕРКИ И РЕКОМЕНДАЦИИ","Разберёмся с проблемами", "Проверки читают состояние. Рекомендации показывают причину и не применяются автоматически.");
        Page.Children.Add(Action("Найти проблемы без нагрузки",async()=>await Scan(),true));
        var decode=new StackPanel();decode.Children.Add(Txt("Объяснить код ошибки",20));var code=new TextBox{Text="0x80070005",Margin=new Thickness(0,8,0,8)};var stop=new CheckBox{Content="Это STOP-код синего экрана (BSOD)"};var answer=Txt("Укажи источник: Win32/HRESULT и BSOD используют разные пространства кодов.",13,"#96A2AF");decode.Children.Add(code);decode.Children.Add(stop);decode.Children.Add(Action("Расшифровать локально",()=>answer.Text=ErrorCodes.Explain(code.Text,stop.IsChecked==true)));decode.Children.Add(answer);Page.Children.Add(WrapCard(decode));
        var findings=(report?.Findings??new()).Where(f=>!f.Title.Contains("Event ")).ToList();Page.Children.Add(Card("ИТОГ ДИАГНОСТИКИ",$"{findings.Count(f=>f.Severity=="critical")} критических · {findings.Count(f=>f.Severity=="warning")} предупреждений",$"{findings.Count(f=>f.Severity=="unknown")} проверок не получили данных. События находятся в отдельном разделе. Это наблюдения, не гарантия исправности."));foreach(var f in findings.OrderBy(f=>f.Severity=="critical"?0:f.Severity=="warning"?1:f.Severity=="unknown"?2:3))Page.Children.Add(FindingCard(f));
        Page.Children.Add(Txt("Системные инструменты",23));var tools=new WrapPanel();tools.Children.Add(Action("Обновления Windows",()=>Open("ms-settings:windowsupdate")));tools.Children.Add(Action("Автозагрузка",()=>Open("ms-settings:startupapps")));tools.Children.Add(Action("Питание",()=>Open("ms-settings:powersleep")));tools.Children.Add(Action("Проверить системные файлы",()=>AdminCheck("sfc.exe","/verifyonly")));tools.Children.Add(Action("Проверить образ Windows",()=>AdminCheck("DISM.exe","/Online /Cleanup-Image /ScanHealth")));Page.Children.Add(tools);Page.Children.Add(Txt("SFC и DISM откроют отдельную консоль с запросом UAC. Эти команды выполняют диагностику и не запускают автоматическое восстановление. Результаты остаются в консоли и системных журналах.",12,"#96A2AF"));
    }
    private void Reports()
    {
        Header("ЛОКАЛЬНАЯ ИСТОРИЯ","Результаты принадлежат тебе", "Отчёт сохраняется здесь. Для публикации потребуется отдельное согласие и просмотр отправляемых данных.");
        if(report!=null)
        {
            var r=report;Page.Children.Add(Card(r.Completed?"ПРОГОН ЗАВЕРШЁН":"ЛОКАЛЬНЫЙ ОТЧЁТ",r.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),$"{r.SuiteVersion} · {r.Profile} · {r.System?.Cpu}"));
            foreach(var m in r.Measurements)Page.Children.Add(Card(m.Metric,$"{m.Value:N2} {m.Unit} · {m.Status}",m.Detail));
            var actions=new WrapPanel();actions.Children.Add(Action("Экспорт JSON",()=>Export(r,false)));actions.Children.Add(Action("Экспорт HTML",()=>Export(r,true)));if(r.ResultUrl==null&&r.Completed&&r.Profile!="stress"&&r.Measurements.Any(m=>m.Status=="ok"))actions.Children.Add(Action("Опубликовать анонимно",async()=>await Publish(r),true));if(r.ResultUrl!=null){actions.Children.Add(Action("Открыть результат",()=>Open(r.ResultUrl)));actions.Children.Add(Action("Удалить с сайта",async()=>await Delete(r)));}Page.Children.Add(actions);
            foreach(var f in r.Findings)Page.Children.Add(FindingCard(f));
        }
        Page.Children.Add(Txt("Сохранённые отчёты",22));string dir=System.IO.Path.Combine(Settings.DataDir,"reports");if(Directory.Exists(dir))foreach(var file in Directory.GetFiles(dir,"*.json").OrderByDescending(f=>f).Take(30)){string captured=file;Page.Children.Add(Action(System.IO.Path.GetFileNameWithoutExtension(file),()=>{try{report=JsonSerializer.Deserialize<Report>(File.ReadAllText(captured),Json.Options);ShowPage("reports");}catch(Exception ex){Activity.Text=ex.Message;}}));}
    }
    private async Task Publish(Report r)
    {
        if(run!=null||publishing||r.ResultUrl!=null||!r.Completed||r.Profile=="stress")return;
        string preview=JsonSerializer.Serialize(r.PublicPayload(),Json.Options);
        if(!Confirm("Согласие на публикацию", "На tests.dedseckms.ru станет публичным показанный ниже набор данных: модели компонентов, RAM, накопители, Windows, выбранные метрики и версии методики.\n\nНе отправляются: имя ПК/пользователя, серийные номера, IP в составе отчёта, MAC, пути, процессы и локальные рекомендации. Сервер технически получает IP соединения и использует временный ограничитель запросов без сохранения исходного адреса. Редкая конфигурация может быть узнаваема.\n\nРезультат помечается как предоставленный клиентом, без независимой проверки. Ключ удаления хранится только в локальном отчёте: не делитесь этим JSON.\n\n"+preview,"Я согласен опубликовать именно этот результат и показанную конфигурацию","Опубликовать"))return;
        publishing=true;
        try { r.DeleteToken??=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));SaveReport(r);using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};http.DefaultRequestHeaders.Add("X-Deletion-Key",r.DeleteToken);var challenge=await http.GetFromJsonAsync<JsonElement>("https://tests.dedseckms.ru/api/challenge");var data=JsonSerializer.SerializeToNode(r.PublicPayload(),Json.Options)!;data["nonce"]=challenge.GetProperty("nonce").GetString();var response=await http.PostAsJsonAsync("https://tests.dedseckms.ru/api/results",data);string text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode)throw new IOException($"Сервер: {(int)response.StatusCode} {text}");var answer=JsonDocument.Parse(text).RootElement;r.ResultUrl=answer.GetProperty("url").GetString();r.DeleteToken=answer.GetProperty("delete_token").GetString();SaveReport(r);Activity.Text="Опубликовано · ссылка и ключ удаления сохранены в локальном отчёте";ShowPage("reports");}catch(Exception ex){MessageBox.Show(this,"Отчёт и ключ сохранены локально. Сервер мог успеть принять публикацию: повторный запрос с тем же ключом не создаст дубль. "+ex.Message,"Публикация не подтверждена");}finally{publishing=false;}
    }
    private async Task Delete(Report r)
    {
        if(r.ResultUrl==null||r.DeleteToken==null)return;if(MessageBox.Show(this,"Удалить этот результат с сайта? Локальный отчёт останется.","Удаление",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{var id=new Uri(r.ResultUrl).Segments.Last();using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};using var request=new HttpRequestMessage(HttpMethod.Delete,"https://tests.dedseckms.ru/api/results/"+id);request.Headers.Add("Authorization","Bearer "+r.DeleteToken);var response=await http.SendAsync(request);response.EnsureSuccessStatusCode();r.ResultUrl=null;r.DeleteToken=null;SaveReport(r);ShowPage("reports");Activity.Text="Публичный результат удалён";}catch(Exception ex){Activity.Text="Удаление: "+ex.Message;}
    }
    private void SettingsPage()
    {
        Header("СДЕЛАЙ ПОД СЕБЯ","Настройки", "Окно перетаскивается за верхнюю панель. Закрытие в трей включается только здесь.");
        var p=new StackPanel();var t=new CheckBox{Content="Сворачивать в трей при закрытии окна",IsChecked=settings.Tray};t.Click+=(_,_)=>{settings.Tray=t.IsChecked==true;settings.Save();};p.Children.Add(t);var a=new CheckBox{Content="Анимации переходов",IsChecked=settings.Animations};a.Click+=(_,_)=>{settings.Animations=a.IsChecked==true;settings.Save();ApplyAppearance();};p.Children.Add(a);
        p.Children.Add(Txt("Акцент интерфейса",17));var colors=new WrapPanel();foreach(string color in new[]{"#C1FA55","#55DDFC","#B69CFF","#FFAD66","#F07CB7"}){string c=color;var b=Action("●",()=>{settings.Accent=c;settings.Save();ApplyAppearance();});b.Foreground=Brush(c);b.FontSize=24;colors.Children.Add(b);}p.Children.Add(colors);
        p.Children.Add(Txt("Цвет окна",17));var themes=new WrapPanel();foreach(var theme in new[]{("Уголь","#0B0E12"),("Графит","#1B1E22"),("Ночной синий","#0D1727"),("Сливовый","#1B1022")}){var captured=theme;themes.Children.Add(Action(captured.Item1,()=>{settings.WindowColor=captured.Item2;settings.Save();ApplyAppearance();}));}p.Children.Add(themes);
        p.Children.Add(Txt("Лимит температуры (°C, 65–100)",14));var limit=new TextBox{Text=settings.TemperatureLimit.ToString(),Width=100,HorizontalAlignment=HorizontalAlignment.Left};limit.LostFocus+=(_,_)=>{if(int.TryParse(limit.Text,out int v)){settings.TemperatureLimit=Math.Clamp(v,65,100);settings.Save();}limit.Text=settings.TemperatureLimit.ToString();};p.Children.Add(limit);p.Children.Add(Txt("Лимит применяется к максимуму доступных температур. Он не заменяет аппаратную защиту; отсутствующий датчик нельзя проверить.",12,"#96A2AF",new Thickness(0,10,0,16)));Page.Children.Add(WrapCard(p));
        var update=new StackPanel();update.Children.Add(Txt("Обновления из GitHub",23));update.Children.Add(Txt("Проверка запускается по кнопке. Пакет проверяется по подписанному манифесту. Скачивание и установка — отдельные действия; отчёты сохраняются.",14,"#96A2AF"));update.Children.Add(Action("Проверить обновления",async()=>await Updates.Check(this),true));Page.Children.Add(WrapCard(update));
        var links=new WrapPanel();links.Children.Add(Action("GitHub",()=>Open("https://github.com/Dedseckms/Dedsec-Tests")));links.Children.Add(Action("Политика публикации",()=>Open("https://tests.dedseckms.ru/privacy")));links.Children.Add(Action("Открыть локальные отчёты",()=>{Directory.CreateDirectory(Settings.DataDir);Open(Settings.DataDir);}));links.Children.Add(Action("Показать соглашение",()=>Agreement()));Page.Children.Add(links);
    }
    private static void SaveReport(Report r){string dir=System.IO.Path.Combine(Settings.DataDir,"reports");Directory.CreateDirectory(dir);File.WriteAllText(System.IO.Path.Combine(dir,r.CreatedUtc.ToString("yyyyMMdd-HHmmss-ffff")+".json"),JsonSerializer.Serialize(r,Json.Options));}
    private void Export(Report r,bool html)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter=html?"HTML report|*.html":"JSON report (содержит локальный ключ удаления)|*.json",FileName="DedsecTests-"+r.CreatedUtc.ToString("yyyyMMdd-HHmmss")+(html?".html":".json")};if(dialog.ShowDialog()!=true)return;
        if(!html)File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(r,Json.Options));else{string E(string? s)=>System.Net.WebUtility.HtmlEncode(s)??"";var sb=new StringBuilder("<!doctype html><html lang=ru><meta charset=utf-8><meta name=viewport content='width=device-width'><title>Dedsec Tests · отчёт</title><style>body{font:16px system-ui;background:#0b0e12;color:#e9eff5;max-width:960px;margin:40px auto;padding:20px}section{background:#151b22;padding:20px;margin:15px 0;border-radius:12px}h1{color:#c1fa55}small{color:#a9b5c1}</style><h1>DEDSEC / TESTS</h1>");sb.Append($"<p>{E(r.System?.Cpu)}<br>{E(r.System?.Gpu)}<br>{E(r.System?.Windows)}</p><small>{E(r.SuiteVersion)} · {E(r.CreatedUtc.ToString("O"))} · completed={r.Completed}</small>");foreach(var m in r.Measurements)sb.Append($"<section><h2>{E(m.Metric)}: {m.Value:N2} {E(m.Unit)}</h2><p>{E(m.Status)} · {E(m.Detail)}</p></section>");foreach(var f in r.Findings)sb.Append($"<section><h2>{E(f.Title)}</h2><p>{E(f.Evidence)}</p><p>{E(f.Solution)}</p></section>");File.WriteAllText(dialog.FileName,sb.ToString());}Activity.Text="Отчёт экспортирован";
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
    private void AdminCheck(string command,string args){if(MessageBox.Show(this,$"Открыть {command} {args} с запросом прав администратора?","Системная диагностика",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;try{Process.Start(new ProcessStartInfo("cmd.exe",$"/k {command} {args}"){UseShellExecute=true,Verb="runas"});}catch(Exception ex){Activity.Text=ex.Message;}}
    private void Navigate(object sender,RoutedEventArgs e)=>ShowPage((string)((Button)sender).Tag);
    private void DragTitle(object sender,MouseButtonEventArgs e){if(e.ClickCount==2)WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;else if(e.ButtonState==MouseButtonState.Pressed)DragMove();}
    private void Restore(){Show();WindowState=WindowState.Normal;Activate();tray.Visible=false;}
    private void Minimize(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void Maximize(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseWindow(object sender,RoutedEventArgs e)=>Close();
    private void StopAll(object sender,RoutedEventArgs e)=>run?.Cancel();
    private void OpenWebsite(object sender,RoutedEventArgs e)=>Open("https://tests.dedseckms.ru");
}
public sealed class UniformGridColumns : System.Windows.Controls.Primitives.UniformGrid{public UniformGridColumns(int columns){Columns=columns;}}
