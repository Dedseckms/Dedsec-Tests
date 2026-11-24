using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Dedsec.Tests;
public partial class MainWindow
{
    private List<Finding> auditFindings=new();private DateTime? auditUtc;private string auditMode="state";private CancellationTokenSource? toolRun;private bool auditing;
    private void Diagnostics()
    {
        var root=PageWorkspace("Диагностика",auditUtc.HasValue?"Последняя проверка: "+auditUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"):"Проверь систему: причины, подтверждения и следующие шаги в одном разделе");
        var body=new Grid();body.RowDefinitions.Add(new(){Height=GridLength.Auto});body.RowDefinitions.Add(new());AddRow(root,body,1);var toolbar=new DockPanel{Margin=new Thickness(0,0,0,10)};
        var scan=Action(auditing?"Проверяем…":"Проверить систему",Scan,true);scan.Padding=new Thickness(12,7,12,7);scan.Margin=new Thickness(10,0,0,0);scan.FontSize=11;scan.IsEnabled=!auditing&&toolRun==null&&run==null&&!storageBusy;DockPanel.SetDock(scan,Dock.Right);toolbar.Children.Add(scan);var tabs=new StackPanel{Orientation=Orientation.Horizontal};toolbar.Children.Add(tabs);body.Children.Add(toolbar);var area=new Grid();AddRow(body,area,1);
        void Render(){area.Children.Clear();if(auditMode=="tools")WindowsTools(area);else if(auditMode=="codes")ErrorDecoder(area);else FindingsBrowser(area,auditMode=="events");}
        foreach(var mode in new[]{("state","Состояние"),("events","События"),("tools","Проверки Windows"),("codes","Код ошибки")}){var selected=mode;TabAction(tabs,mode.Item2,()=>{auditMode=selected.Item1;Render();},auditMode==mode.Item1);}Render();
        var footer=Txt($"{auditFindings.Count(f=>f.Severity=="critical")} критических · {auditFindings.Count(f=>f.Severity=="warning")} предупреждений · {auditFindings.Count(f=>f.Severity=="unknown")} без данных   /   сообщения и вывод команд остаются локально",11,"#91A7B8",new Thickness(0,12,0,0));footer.TextWrapping=TextWrapping.NoWrap;footer.TextTrimming=TextTrimming.CharacterEllipsis;AddRow(root,footer,2);
    }
    private void FindingsBrowser(Grid host,bool events)
    {
        var layout=new Grid();layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new());host.Children.Add(layout);
        var filters=new Grid{Margin=new Thickness(0,0,0,10)};filters.ColumnDefinitions.Add(new());filters.ColumnDefinitions.Add(new());
        var category=new ComboBox{ItemsSource=events?new[]{"События"}:new[]{"Все категории","Память","Накопители","Нагрузка","Защита","Windows и устройства"},SelectedIndex=0,MinHeight=32,FontSize=11,Margin=new Thickness(0,0,6,0)};filters.Children.Add(category);
        var severity=new ComboBox{ItemsSource=new[]{"Все результаты","Только проблемы","Нет данных"},SelectedIndex=0,MinHeight=32,FontSize=11,Margin=new Thickness(6,0,0,0)};Grid.SetColumn(severity,1);filters.Children.Add(severity);layout.Children.Add(filters);
        var columns=new Grid();columns.ColumnDefinitions.Add(new(){Width=new GridLength(.42,GridUnitType.Star)});columns.ColumnDefinitions.Add(new(){Width=new GridLength(.58,GridUnitType.Star)});AddRow(layout,columns,1);var list=new ListBox{Margin=new Thickness(0,0,10,0),HorizontalContentAlignment=HorizontalAlignment.Stretch};columns.Children.Add(list);var detail=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(detail,1);columns.Children.Add(detail);
        void Refresh()
        {
            list.Items.Clear();foreach(var finding in auditFindings.Where(f=>!f.Title.StartsWith("Образ Windows ·")&&!f.Title.StartsWith("Системные файлы ·")).Where(f=>(Dedsec.Tests.Diagnostics.Category(f)=="События")==events).Where(f=>events||category.SelectedIndex==0||Dedsec.Tests.Diagnostics.Category(f)==category.SelectedItem as string).Where(f=>severity.SelectedIndex==0||severity.SelectedIndex==1&&f.Severity is "warning" or "critical"||severity.SelectedIndex==2&&f.Severity=="unknown").OrderBy(f=>f.Severity=="critical"?0:f.Severity=="warning"?1:f.Severity=="unknown"?2:3))
            {var text=new StackPanel{Margin=new Thickness(9,7,9,7)};text.Children.Add(Txt(finding.Title,12));text.Children.Add(Txt(SeverityLabel(finding.Severity)+" · "+Dedsec.Tests.Diagnostics.Category(finding),10,finding.Severity is "warning" or "critical"?"#FFBF7F":"#91A7B8"));list.Items.Add(new ListBoxItem{Content=text,Tag=finding,Padding=new Thickness(0),Margin=new Thickness(0,0,0,4)});}
            if(list.Items.Count>0)list.SelectedIndex=0;else detail.Content=Txt(auditFindings.Count==0?"Проверка ещё не запускалась. Нажми «Проверить систему».":"Для выбранного фильтра записей нет.",16,"#91A7B8");
        }
        list.SelectionChanged+=(_,_)=>{if(list.SelectedItem is ListBoxItem item&&item.Tag is Finding finding){var panel=new StackPanel{Margin=new Thickness(14)};panel.Children.Add(Txt(finding.Title,20));panel.Children.Add(Txt("ЧТО ОБНАРУЖЕНО",10,"#C1FA55",new Thickness(0,18,0,8)));panel.Children.Add(Txt(finding.Evidence,13));panel.Children.Add(Txt("ЧТО ДЕЛАТЬ ДАЛЬШЕ",10,"#C1FA55",new Thickness(0,18,0,8)));panel.Children.Add(Txt(finding.Solution,13,"#A7BBC9"));if(finding.Link!=null)panel.Children.Add(SmallAction("Документация",()=>Open(finding.Link)));detail.Content=panel;detail.ScrollToTop();}};
        category.SelectionChanged+=(_,_)=>Refresh();severity.SelectionChanged+=(_,_)=>Refresh();Refresh();
    }
    private static string SeverityLabel(string severity)=>severity switch{"critical"=>"Критично","warning"=>"Внимание","unknown"=>"Нет данных","ok"=>"Подтверждено",_=>"Наблюдение"};
    private void ErrorDecoder(Grid host)
    {
        var panel=new StackPanel{Margin=new Thickness(18)};panel.Children.Add(Txt("Объяснение кода ошибки",22));var code=new TextBox{Text="0x80070005",Margin=new Thickness(0,14,0,10)};panel.Children.Add(code);var bsod=new CheckBox{Content="STOP-код синего экрана (BSOD)",FontSize=12};panel.Children.Add(bsod);var answer=Txt("Win32/HRESULT и BSOD используют разные пространства кодов.",13,"#91A7B8");panel.Children.Add(SmallAction("Расшифровать",()=>answer.Text=ErrorCodes.Explain(code.Text,bsod.IsChecked==true)));panel.Children.Add(answer);host.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private void WindowsTools(Grid host)
    {
        var panel=new StackPanel{Margin=new Thickness(18)};panel.Children.Add(Txt("Целостность Windows",22));panel.Children.Add(Txt("Штатные SFC / DISM. Результат и вывод сохраняются в программе. Восстановление не запускается.",13,"#91A7B8",new Thickness(0,10,0,18)));
        var choice=new ComboBox{ItemsSource=new[]{"Образ Windows · известные повреждения","Образ Windows · полное сканирование","Системные файлы · SFC /verifyonly"},SelectedIndex=0,MinHeight=38,FontSize=12,IsEnabled=toolRun==null};panel.Children.Add(choice);
        panel.Children.Add(Txt("CheckHealth проверяет уже известные признаки; ScanHealth и SFC могут занять несколько минут. Нужны права администратора. Проверка не скачивает дополнительные программы.",12,"#91A7B8",new Thickness(0,14,0,14)));
        var start=Action(toolRun==null?"Выполнить проверку":"Проверка выполняется…",async()=>await RunWindowsCheck(choice.SelectedIndex==0?"image_quick":choice.SelectedIndex==1?"image_scan":"system_files"),true);start.IsEnabled=toolRun==null&&run==null&&!auditing&&!storageBusy;start.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(start);
        if(toolRun!=null)panel.Children.Add(Txt("Остановка — в нижней панели окна. Незавершённая проверка не считается успешной.",12,"#C1FA55"));
        foreach(var finding in auditFindings.Where(f=>f.Title.StartsWith("Образ Windows ·")||f.Title.StartsWith("Системные файлы ·")))panel.Children.Add(FindingCard(finding));host.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
    }
    private async Task RunWindowsCheck(string kind)
    {
        if(run!=null||toolRun!=null||auditing||storageBusy)return;
        if(!SensorComponents.Elevated){if(BrandedDialog.Ask(this,"Нужны права Windows","Для этой проверки нужен запуск от администратора. Перезапустить приложение с запросом UAC? После запуска выбери проверку снова.","Перезапустить")){var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"};start.ArgumentList.Add("--wait-pid");start.ArgumentList.Add(Environment.ProcessId.ToString());Process.Start(start);exiting=true;Close();}return;}
        toolRun=new CancellationTokenSource();GlobalStop.Visibility=Visibility.Visible;Activity.Text="Проверка Windows выполняется · исправления не применяются";ShowPage("diagnostics");
        try{var result=await WindowsChecks.Run(kind,toolRun.Token);var finding=WindowsChecks.Explain(result);auditFindings.RemoveAll(f=>f.Title==finding.Title);auditFindings.Add(finding);auditUtc=DateTime.UtcNow;SaveReport(new Report{System=info,Findings=auditFindings.ToList()});Activity.Text=result.Cancelled?"Проверка Windows прервана":"Проверка Windows завершена · вывод сохранён локально";}
        catch(Exception ex){Activity.Text="Проверка Windows не завершена: "+ex.Message;BrandedDialog.Notify(this,"Проверка не завершена",ex.Message);}
        finally{toolRun.Dispose();toolRun=null;GlobalStop.Visibility=Visibility.Collapsed;if(current=="diagnostics")ShowPage("diagnostics");}
    }
}
