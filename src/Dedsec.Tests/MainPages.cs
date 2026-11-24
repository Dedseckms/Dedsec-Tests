using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms=System.Windows.Forms;
namespace Dedsec.Tests;
public partial class MainWindow
{
    private Grid PageWorkspace(string title,string detail)
    {
        var root=Workspace();root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new StackPanel{Margin=new Thickness(0,0,0,14)};heading.Children.Add(Txt(title,26));heading.Children.Add(Txt(detail,12,"#91A7B8"));AddRow(root,heading,0);return root;
    }
    private Border CompactCard(UIElement content)=>new(){Background=(Brush)FindResource("Panel"),CornerRadius=new CornerRadius(12),BorderBrush=Brush("#2B3943"),BorderThickness=new Thickness(1),Padding=new Thickness(16),Child=content};
    private Button SmallAction(string text,System.Action action){var b=Action(text,action);b.Padding=new Thickness(12,8,12,8);b.FontSize=12;return b;}
    private void TabAction(Panel tabs,string text,System.Action action)
    {var button=SmallAction(text,action);button.FontSize=11;button.Padding=new Thickness(10,7,10,7);button.Margin=new Thickness(0,0,5,0);if(tabs.Children.Count==0)button.Background=Brush("#34452A");button.Click+=(_,_)=>{foreach(Button sibling in tabs.Children.OfType<Button>())sibling.Background=Brush("#222B34");button.Background=Brush("#34452A");};tabs.Children.Add(button);}
    private void TestsPage()
    {
        var root=PageWorkspace("Тестирование","Фиксированные бенчмарки CPU / GPU / RAM / дисков · методика Dedsec v1");
        var columns=new Grid();columns.ColumnDefinitions.Add(new(){Width=new GridLength(1.15,GridUnitType.Star)});columns.ColumnDefinitions.Add(new());
        var options=new StackPanel();options.Children.Add(Txt("Состав автотеста",18));
        foreach(var item in new[]{("GPU · Direct3D 11 compute",settings.IncludeGpu,(System.Action<bool>)(v=>settings.IncludeGpu=v)),("RAM · копирование и проверка",settings.IncludeMemory,(System.Action<bool>)(v=>settings.IncludeMemory=v)),("Диск · чтение, запись, задержка",settings.IncludeDisk,(System.Action<bool>)(v=>settings.IncludeDisk=v))})
        {var captured=item;var check=new CheckBox{Content=item.Item1,IsChecked=item.Item2,FontSize=12};check.Click+=(_,_)=>{captured.Item3(check.IsChecked==true);settings.Save();};options.Children.Add(check);}
        options.Children.Add(Txt("Видеокарта",11,"#91A7B8",new Thickness(0,10,0,6)));var adapters=GpuBenchmark.Adapters();var gpuBox=new ComboBox{ItemsSource=adapters,SelectedIndex=Math.Min(gpuIndex,adapters.Count-1),MinHeight=34,FontSize=11};gpuBox.SelectionChanged+=(_,_)=>gpuIndex=Math.Max(0,gpuBox.SelectedIndex);options.Children.Add(gpuBox);
        options.Children.Add(Txt("Накопитель · временный файл 256 MiB",11,"#91A7B8",new Thickness(0,12,0,6)));
        var drives=DriveInfo.GetDrives().Where(d=>(d.DriveType==DriveType.Fixed||d.DriveType==DriveType.Removable)&&d.IsReady).Select(d=>d.Name).ToList();var driveBox=new ComboBox{ItemsSource=drives,SelectedItem=settings.DiskRoot,MinHeight=34,FontSize=12};driveBox.SelectionChanged+=(_,_)=>{if(driveBox.SelectedItem is string s){settings.DiskRoot=s;settings.Save();}};options.Children.Add(driveBox);
        options.Children.Add(SmallAction("Выбрать другую папку",()=>{using var dialog=new Forms.FolderBrowserDialog{Description="Папка для временного файла 256 MiB. Пользовательские файлы не изменяются.",UseDescriptionForTitle=true};if(dialog.ShowDialog()==Forms.DialogResult.OK){settings.DiskRoot=dialog.SelectedPath;settings.Save();ShowPage("tests");}}));
        var folder=Txt(settings.DiskRoot,10,"#91A7B8");folder.TextTrimming=TextTrimming.CharacterEllipsis;folder.TextWrapping=TextWrapping.NoWrap;folder.ToolTip=settings.DiskRoot;options.Children.Add(folder);
        var left=CompactCard(new ScrollViewer{Content=options,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});left.Margin=new Thickness(0,0,6,0);columns.Children.Add(left);
        var status=new StackPanel();status.Children.Add(Txt("Управление прогоном",18));runText=Txt(run==null?"Готов к запуску":"Тест выполняется…",16,"#C1FA55");status.Children.Add(runText);progress=new ProgressBar{Minimum=0,Maximum=100,Margin=new Thickness(0,14,0,16)};status.Children.Add(progress);
        var stop=SmallAction("Остановить",()=>run?.Cancel());stop.IsEnabled=run!=null;status.Children.Add(stop);status.Children.Add(Txt("Автотест проверяет систему и выполняет выбранные бенчмарки. Результат сохраняется локально; публикация — отдельным действием.",12,"#9BB1C1",new Thickness(0,10,0,12)));
        status.Children.Add(Txt($"Температурный лимит: {settings.TemperatureLimit} °C",13));status.Children.Add(Txt("Недоступный датчик не обеспечивает температурную остановку. Перед нагрузкой сохрани работу.",12,"#9BB1C1"));
        var right=CompactCard(new ScrollViewer{Content=status,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});right.Margin=new Thickness(6,0,0,0);Grid.SetColumn(right,1);columns.Children.Add(right);AddRow(root,columns,1);
        var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};actions.Children.Add(Action("Автотест",async()=>await Start("auto"),true));foreach(string mode in new[]{"cpu","gpu","ram","disk","stress"}){string selected=mode;actions.Children.Add(Action(mode=="stress"?$"Нагрузка · {settings.StressSeconds} с":mode=="disk"?"Диск":mode.ToUpperInvariant(),async()=>await Start(selected)));}foreach(Button b in actions.Children){b.Padding=new Thickness(14,10,14,10);b.FontSize=12;b.Margin=new Thickness(0,0,6,0);}AddRow(root,actions,2);
    }
    private void Reports()
    {
        var root=PageWorkspace("Отчёты","Измерения сохраняются локально. Публикация требует отдельного согласия.");var body=new Grid();body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());AddRow(root,body,1);
        string dir=Path.Combine(Settings.DataDir,"reports");var files=Directory.Exists(dir)?Directory.GetFiles(dir,"*.json").OrderByDescending(f=>f).Take(100).ToArray():Array.Empty<string>();
        if(files.Length>0){var history=new ComboBox{ItemsSource=files.Select(f=>new{Path=f,Name=Path.GetFileNameWithoutExtension(f)}).ToArray(),DisplayMemberPath="Name",SelectedIndex=-1,Margin=new Thickness(0,0,0,10),MinHeight=34,ToolTip="Выбрать сохранённый отчёт"};history.SelectionChanged+=(_,_)=>{if(history.SelectedIndex<0)return;try{report=JsonSerializer.Deserialize<Report>(File.ReadAllText(files[history.SelectedIndex]),Json.Options);ShowPage("reports");}catch(Exception ex){Activity.Text=ex.Message;}};body.Children.Add(history);}
        if(report==null){AddRow(body,Txt("Пока нет выбранного отчёта. Выполни автотест или выбери сохранённый результат.",17,"#9BB1C1"),1);return;}
        var r=report;var summary=new StackPanel{Margin=new Thickness(0,0,0,10)};summary.Children.Add(Txt($"{r.CreatedUtc.ToLocalTime():dd.MM.yyyy · HH:mm}   /   {(r.Completed?"ПРОГОН ЗАВЕРШЁН":"ЛОКАЛЬНАЯ ДИАГНОСТИКА")}",12,"#C1FA55"));summary.Children.Add(Txt(r.System?.Cpu+" · "+r.System?.Gpu,12,"#9BB1C1"));AddRow(body,summary,1);
        var area=new Grid();AddRow(body,area,2);
        void Measurements()
        {
            area.Children.Clear();var grid=new DataGrid{ItemsSource=r.Measurements.Select(m=>new{Name=MeasurementDisplay.Name(m.Metric),Result=MeasurementDisplay.Headline(m),Seconds=m.Seconds.ToString("0.000")+" с",Status=m.Status,Detail=m.Detail}).ToArray(),AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,RowHeight=43};
            foreach(var col in new[]{("Измерение","Name",1.25),("Результат","Result",1.5),("Время","Seconds",.65),("Статус","Status",.55)})grid.Columns.Add(new DataGridTextColumn{Header=col.Item1,Binding=new Binding(col.Item2),MinWidth=col.Item2=="Name"?150:col.Item2=="Result"?190:70,Width=new DataGridLength(col.Item3,DataGridLengthUnitType.Star)});grid.ToolTip=MeasurementDisplay.ScoreMethod;area.Children.Add(grid);
        }
        void Findings(){area.Children.Clear();var list=new StackPanel();foreach(var finding in r.Findings)list.Children.Add(FindingCard(finding));area.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});}
        Measurements();var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var tabs=new WrapPanel();tabs.Children.Add(SmallAction("Измерения",Measurements));tabs.Children.Add(SmallAction($"Диагностика · {r.Findings.Count}",Findings));tabs.Children.Add(SmallAction("JSON",()=>Export(r,false)));tabs.Children.Add(SmallAction("HTML",()=>Export(r,true)));
        if(r.ResultUrl==null&&r.Completed&&r.Profile!="stress"&&r.Measurements.Any(m=>m.Status=="ok")){var publish=Action("Опубликовать анонимно",async()=>await Publish(r),true);publish.Padding=new Thickness(12,8,12,8);publish.FontSize=12;tabs.Children.Add(publish);}
        if(r.ResultUrl!=null){tabs.Children.Add(SmallAction("Открыть на сайте",()=>Open(r.ResultUrl)));tabs.Children.Add(Action("Удалить",async()=>await Delete(r)));}footer.Children.Add(tabs);
        string state=r.ResultUrl!=null?"Сервер подтвердил результат · ключ удаления хранится локально":r.PublicationError!=null?"Публикация не подтверждена: "+r.PublicationError:"На сервер ничего не отправлено · DT вычисляются из измеренной пропускной способности";var status=Txt(state,11,r.PublicationError!=null?"#FFBF7F":"#91A7B8");status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;status.ToolTip=state;footer.Children.Add(status);AddRow(root,footer,2);
    }
    private void SettingsPage()
    {
        var root=PageWorkspace("Настройки","Внешний вид, управление нагрузкой и обновления");var body=new Grid();body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());AddRow(root,body,1);var tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6)};body.Children.Add(tabs);var area=new Grid();AddRow(body,area,1);
        void Columns(UIElement left,UIElement right){area.Children.Clear();var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());var a=CompactCard(new ScrollViewer{Content=left,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var b=CompactCard(new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});a.Margin=new Thickness(0,0,6,0);b.Margin=new Thickness(6,0,0,0);Grid.SetColumn(b,1);grid.Children.Add(a);grid.Children.Add(b);area.Children.Add(grid);}
        void Appearance()
        {
            var left=new StackPanel();left.Children.Add(Txt("Поведение окна",18));var trayCheck=new CheckBox{Content="Закрытие и сворачивание в трей",IsChecked=settings.Tray,FontSize=12};trayCheck.Click+=(_,_)=>{settings.Tray=trayCheck.IsChecked==true;settings.Save();};left.Children.Add(trayCheck);var animations=new CheckBox{Content="Анимации переходов",IsChecked=settings.Animations,FontSize=12};animations.Click+=(_,_)=>{settings.Animations=animations.IsChecked==true;settings.Save();};left.Children.Add(animations);left.Children.Add(Txt("Перетаскивание — за верхнюю панель. Размер окна меняется за края.",12,"#91A7B8",new Thickness(0,15,0,0)));
            var right=new StackPanel();right.Children.Add(Txt("Акцент интерфейса",18));var colors=new WrapPanel();foreach(string color in new[]{"#C1FA55","#55DDFC","#B69CFF","#FFAD66","#F07CB7"}){string c=color;var b=SmallAction("●",()=>{settings.Accent=c;settings.Save();ApplyAppearance();});b.Foreground=Brush(c);b.FontSize=23;b.Padding=new Thickness(10,5,10,5);colors.Children.Add(b);}right.Children.Add(colors);right.Children.Add(Txt("Цвет окна",16));var themes=new WrapPanel();foreach(var theme in new[]{("Уголь","#0B0E12"),("Графит","#1B1E22"),("Синий","#0D1727"),("Сливовый","#1B1022")}){var selected=theme;themes.Children.Add(SmallAction(theme.Item1,()=>{settings.WindowColor=selected.Item2;settings.Save();ApplyAppearance();}));}right.Children.Add(themes);Columns(left,right);
        }
        void Protection()
        {
            var left=new StackPanel();left.Children.Add(Txt("Нагрузка и температура",18));left.Children.Add(Txt("Лимит остановки · 65–100 °C",12,"#91A7B8"));var limit=new TextBox{Text=settings.TemperatureLimit.ToString(),Width=100,HorizontalAlignment=HorizontalAlignment.Left};limit.LostFocus+=(_,_)=>{if(int.TryParse(limit.Text,out int value)){settings.TemperatureLimit=Math.Clamp(value,65,100);settings.Save();}limit.Text=settings.TemperatureLimit.ToString();};left.Children.Add(limit);left.Children.Add(Txt("Длительность стресса CPU + GPU",12,"#91A7B8",new Thickness(0,16,0,8)));var duration=new ComboBox{ItemsSource=new[]{"30 секунд","1 минута","3 минуты","5 минут"},SelectedIndex=settings.StressSeconds==30?0:settings.StressSeconds==180?2:settings.StressSeconds==300?3:1,MinHeight=34};duration.SelectionChanged+=(_,_)=>{settings.StressSeconds=duration.SelectedIndex==0?30:duration.SelectedIndex==2?180:duration.SelectedIndex==3?300:60;settings.Save();};left.Children.Add(duration);left.Children.Add(Txt("Методика публикуемых бенчмарков остаётся фиксированной. Стресс в рейтинг не отправляется.",12,"#91A7B8",new Thickness(0,16,0,0)));
            var right=new StackPanel();right.Children.Add(Txt("Аппаратные датчики",18));right.Children.Add(Txt(SensorComponents.Status,12,"#C1FA55"));right.Children.Add(Txt("Подписанный компонент включён в программу. Подготовка запускается по кнопке, с запросом прав Windows.",12,"#91A7B8",new Thickness(0,14,0,14)));right.Children.Add(Action("Подготовить датчики",PrepareSensors,true));right.Children.Add(Txt("Температурная остановка работает только по доступным показаниям и не заменяет защиту оборудования.",12,"#91A7B8"));Columns(left,right);
        }
        void Update()
        {
            area.Children.Clear();var panel=new StackPanel{Margin=new Thickness(22)};panel.Children.Add(Txt("GitHub Releases",23));panel.Children.Add(Txt("Установлена версия "+typeof(MainWindow).Assembly.GetName().Version?.ToString(3),13,"#C1FA55"));panel.Children.Add(Txt("Проверка, скачивание и установка запускаются по вашему действию. Подпись манифеста, SHA-256 и размер пакета проверяются. Локальные отчёты сохраняются; при неудачном запуске предусмотрен откат.",13,"#9BB1C1",new Thickness(0,16,0,20)));var button=Action("Проверить обновления",async()=>await Updates.Check(this),true);button.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(button);area.Children.Add(CompactCard(panel));
        }
        TabAction(tabs,"Внешний вид",Appearance);TabAction(tabs,"Нагрузка и датчики",Protection);TabAction(tabs,"Обновления",Update);Appearance();
        var links=new WrapPanel{Margin=new Thickness(0,12,0,0)};links.Children.Add(SmallAction("GitHub",()=>Open("https://github.com/Dedseckms/Dedsec-Tests")));links.Children.Add(SmallAction("Соглашение",()=>Agreement()));links.Children.Add(SmallAction("Публикация и данные",()=>Open("https://tests.dedseckms.ru/privacy")));links.Children.Add(SmallAction("Локальные отчёты",()=>{Directory.CreateDirectory(Settings.DataDir);Open(Settings.DataDir);}));AddRow(root,links,2);
    }
}
