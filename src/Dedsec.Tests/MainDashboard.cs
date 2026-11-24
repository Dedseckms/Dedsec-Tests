using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dedsec.Tests;
public partial class MainWindow
{
    private readonly TelemetryHistory timeline=new();private DashboardView? dashboard;private TextBlock? monitorSummary,monitorAlert;private Button? freezeButton;
    private Dictionary<string,IReadOnlyList<TimelinePoint>>? frozen;private DateTime frozenTime;private bool preparingSensors;private Grid? dashboardHost;private string monitorMode="system";
    private Grid Workspace()
    {
        PageScroll.Visibility=Visibility.Collapsed;FixedPage.Visibility=Visibility.Visible;
        var root=new Grid();FixedPage.Children.Add(root);return root;
    }
    private static void AddRow(Grid root,UIElement child,int row){Grid.SetRow(child,row);root.Children.Add(child);}
    private void ShowDashboard()
    {
        var root=Workspace();foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto,GridLength.Auto})root.RowDefinitions.Add(new(){Height=height});
        var heading=new DockPanel{Margin=new Thickness(0,0,0,10)};var title=new StackPanel();title.Children.Add(Txt("Мониторинг",26));
        var sub=Txt(info?.Windows??"Определяем систему",11,"#8FA6B7");sub.TextWrapping=TextWrapping.NoWrap;sub.TextTrimming=TextTrimming.CharacterEllipsis;title.Children.Add(sub);
        monitorSummary=Txt("● LIVE",11,"#C1FA55");monitorSummary.HorizontalAlignment=HorizontalAlignment.Right;monitorSummary.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(monitorSummary,Dock.Right);heading.Children.Add(monitorSummary);heading.Children.Add(title);AddRow(root,heading,0);
        var toolbar=new DockPanel{Margin=new Thickness(0,0,0,10)};
        var controls=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(controls,Dock.Right);
        var period=new ComboBox{ItemsSource=new[]{"1 мин","5 мин","15 мин"},SelectedIndex=settings.ChartSeconds==60?0:settings.ChartSeconds==900?2:1,Width=88,MinHeight=30,FontSize=11,Margin=new Thickness(0,0,6,0)};
        period.SelectionChanged+=(_,_)=>{settings.ChartSeconds=period.SelectedIndex==0?60:period.SelectedIndex==2?900:300;settings.Save();frozen=null;if(freezeButton!=null)freezeButton.Content="Пауза";RefreshDashboard();};controls.Children.Add(period);
        freezeButton=Action(frozen==null?"Пауза":"Продолжить",()=>{if(frozen==null){frozenTime=DateTime.UtcNow;frozen=timeline.Latest.ToDictionary(m=>m.Key,m=>(IReadOnlyList<TimelinePoint>)timeline.Points(m.Key,frozenTime,settings.ChartSeconds).ToArray());}else frozen=null;freezeButton!.Content=frozen==null?"Пауза":"Продолжить";RefreshDashboard();});freezeButton.ToolTip="Пауза просмотра. Измерения и температурная защита продолжают работать.";
        foreach(var button in new[]{freezeButton,Action("CSV",ExportTelemetry)}){button.Padding=new Thickness(10,6,10,6);button.Margin=new Thickness(0,0,6,0);button.FontSize=11;controls.Children.Add(button);}toolbar.Children.Add(controls);
        var tabs=new StackPanel{Orientation=Orientation.Horizontal};toolbar.Children.Add(tabs);
        dashboardHost=new Grid();AddRow(root,dashboardHost,2);
        void Graphs(params string[] keys){liveGrid=null;dashboardHost.Children.Clear();dashboard=new DashboardView(keys);dashboardHost.Children.Add(dashboard);RefreshDashboard();}
        foreach(var mode in new[]{("system","Компоненты"),("activity","Активность"),("raw","Датчики")})
        {var selected=mode;TabAction(tabs,mode.Item2,()=>{monitorMode=selected.Item1;RenderMode();},monitorMode==mode.Item1);}
        void RenderMode(){if(monitorMode=="raw")ShowSensorTable();else if(monitorMode=="activity")Graphs("disk","network","power","fans");else Graphs("cpu","gpu","ram","storage");}
        RenderMode();
        AddRow(root,toolbar,1);
        monitorAlert=Txt("",11,"#FFBF7F",new Thickness(0,8,0,0));monitorAlert.TextWrapping=TextWrapping.NoWrap;monitorAlert.TextTrimming=TextTrimming.CharacterEllipsis;monitorAlert.Visibility=Visibility.Collapsed;AddRow(root,monitorAlert,3);
        var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};
        var clear=Action("Очистить историю",()=>{timeline.Clear();frozen=null;freezeButton.Content="Пауза";RefreshDashboard();});clear.Padding=new Thickness(12,7,12,7);clear.FontSize=11;clear.Margin=new Thickness(0);DockPanel.SetDock(clear,Dock.Right);footer.Children.Add(clear);footer.Children.Add(Txt("История только на этом ПК · курсор на графике показывает время и значение",11,"#8298A9"));
        AddRow(root,footer,4);RefreshDashboard();
    }
    private void ShowSensorTable()
    {
        if(dashboardHost==null)return;dashboard=null;dashboardHost.Children.Clear();sensorRows.Clear();foreach(var row in sensors)sensorRows.Add(row);
        liveGrid=new DataGrid{ItemsSource=sensorRows,AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,RowHeight=30};
        foreach(var column in new[]{("Устройство","Device"),("Датчик","Name"),("Тип","Type"),("Значение","Value"),("Ед.","Unit")})liveGrid.Columns.Add(new DataGridTextColumn{Header=column.Item1,Binding=new System.Windows.Data.Binding(column.Item2),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
        dashboardHost.Children.Add(liveGrid);
    }
    private void RefreshDashboard()
    {
        var now=DateTime.UtcNow;dashboard?.Update(timeline,settings.ChartSeconds,frozen==null?now:frozenTime,frozen);
        if(monitorSummary!=null){monitorSummary.Text=frozen==null?"● LIVE  ·  "+now.ToLocalTime().ToString("HH:mm:ss"):"Ⅱ ПАУЗА ПРОСМОТРА";monitorSummary.ToolTip=$"Обновление каждые 2 с · {sensors.Count(s=>s.Type=="Temperature")} температурных датчиков. История только локально. Защита активна независимо от паузы.";}
        if(monitorAlert!=null){var warning=Dedsec.Tests.Diagnostics.Live(sensors,processes,settings.TemperatureLimit).FirstOrDefault(f=>f.Severity is "warning" or "critical");monitorAlert.Visibility=warning==null?Visibility.Collapsed:Visibility.Visible;if(warning!=null){monitorAlert.Text="⚠ "+warning.Title+" · "+warning.Evidence;monitorAlert.ToolTip=warning.Solution;}}
    }
    private void ExportTelemetry()
    {
        var dialog=new Microsoft.Win32.SaveFileDialog{Filter="CSV · локальная история показаний|*.csv",FileName="DedsecTests-telemetry-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"};if(dialog.ShowDialog(this)!=true)return;timeline.ExportCsv(dialog.FileName);Activity.Text="История экспортирована локально · на сервер не отправлялась";
    }
    private async Task PrepareSensors()
    {
        if(run!=null||toolRun!=null||auditing||storageBusy||preparingSensors){Activity.Text="Сначала дождитесь окончания теста";return;}
        if(!Environment.Is64BitOperatingSystem){BrandedDialog.Notify(this,"Базовый мониторинг доступен","Официальный низкоуровневый компонент поддерживает 64-битную ОС. На 32-битной Windows продолжают работать штатные счётчики CPU, RAM, дисков и сети, а также доступные датчики оборудования.");return;}
        bool install=SensorComponents.InstalledVersion is not Version v||v<new Version(2,2,0);
        if(!install&&SensorComponents.Elevated){BrandedDialog.Notify(this,"Компонент уже готов","Установлена версия "+SensorComponents.InstalledVersion+". Приложение запущено от администратора.\n\nЕсли отдельной температуры или RPM нет, устройство либо его драйвер не предоставили поддерживаемый датчик. Значение не будет выдумано.");return;}
        if(!Confirm("Включить аппаратные датчики",(install?"Из встроенного проверенного пакета будет установлен подписанный компонент PawnIO 2.2.0 (GPL, исходники и лицензия включены). Интернет не нужен. Windows запросит права администратора. Компонент также может использоваться другими программами; при удалении Dedsec Tests он автоматически не удаляется.":"PawnIO уже установлен. Для чтения части CPU и датчиков платы нужен запуск приложения от администратора.")+"\n\nДалее приложение предложит перезапуск с нужными правами. Отчёты сохранятся. Защита Windows, Secure Boot и проверка подписей не отключаются.","Разрешаю подготовку компонента и запрос Windows на повышение прав","Подготовить датчики"))return;
        preparingSensors=true;
        try
        {
            Activity.Text="Проверяем встроенный пакет и готовим датчики…";bool reboot=install&&await SensorComponents.Install();if(reboot){BrandedDialog.Notify(this,"Требуется перезагрузка Windows","Компонент установлен. Windows запросила перезагрузку для его активации. Сохраните работу и перезагрузите ПК вручную.");return;}
            if(BrandedDialog.Ask(this,"Датчики подготовлены","Перезапустить Dedsec Tests с правами администратора? Это нужно для аппаратных температур на части систем. Локальные отчёты сохранятся.","Перезапустить"))
            {var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};start.ArgumentList.Add("--wait-pid");start.ArgumentList.Add(Environment.ProcessId.ToString());Process.Start(start);exiting=true;Close();}
        }
        catch(Exception ex){BrandedDialog.Notify(this,"Компонент не активирован",ex.Message+"\n\nБазовый мониторинг продолжает работать.");}
        finally{preparingSensors=false;RefreshDashboard();}
    }
}
