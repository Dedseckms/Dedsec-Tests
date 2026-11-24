using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
namespace Dedsec.Tests;
public sealed class BrandedDialog:Window
{
    private bool userDismissed;
    public StackPanel Body {get;}=new();
    public StackPanel Footer {get;}=new();
    public StackPanel Actions {get;}=new(){Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
    public Button AcceptButton {get;}
    public CheckBox? Consent {get;private set;}
    private static SolidColorBrush ColorBrush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
    private static TextBlock Text(string value,double size=14,string color="#B8C7D3")=>new(){Text=value,FontSize=size,Foreground=ColorBrush(color),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    public BrandedDialog(Window? owner,string title,string action,bool cancel=true,bool closeOnAccept=true)
    {
        Owner=owner;Title="Dedsec Tests · "+title;Width=700;Height=640;MinWidth=360;MinHeight=300;WindowStartupLocation=owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
        Width=Math.Min(Width,SystemParameters.WorkArea.Width-28);Height=Math.Min(Height,SystemParameters.WorkArea.Height-28);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;ShowInTaskbar=false;Background=ColorBrush("#0B1117");Foreground=ColorBrush("#EDF4F8");FontFamily=new FontFamily("Segoe UI");FontSize=14;
        WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0)});
        var root=new DockPanel{Margin=new Thickness(26)};
        var chrome=new DockPanel{Margin=new Thickness(0,0,0,24)};DockPanel.SetDock(chrome,Dock.Top);var close=new Button{Content="✕",Padding=new Thickness(10,5,10,5),Margin=new Thickness(0),HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(close,Dock.Right);close.Click+=(_,_)=>{userDismissed=true;Close();};chrome.Children.Add(close);var brand=Text("DEDSEC / TESTS     •     ТВОЙ КОНТРОЛЬ",12,"#C1FA55");brand.VerticalAlignment=VerticalAlignment.Center;brand.Margin=new Thickness(0);chrome.Children.Add(brand);chrome.MouseLeftButtonDown+=(_,e)=>{if(e.ChangedButton==MouseButton.Left)DragMove();};root.Children.Add(chrome);
        DockPanel.SetDock(Footer,Dock.Bottom);var buttons=Actions;
        if(cancel){var back=new Button{Content="Отмена",IsCancel=true};back.Click+=(_,_)=>{userDismissed=true;DialogResult=false;};buttons.Children.Add(back);}
        AcceptButton=new Button{Content=action,IsDefault=true,Style=(Style)FindResource("Primary"),Margin=new Thickness(0)};AcceptButton.Click+=(_,_)=>{if(closeOnAccept){userDismissed=true;DialogResult=true;}};buttons.Children.Add(AcceptButton);Footer.Children.Add(buttons);root.Children.Add(Footer);
        Body.Children.Add(Text(title,28,"#F0F6FA"));root.Children.Add(new ScrollViewer{Content=Body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0)});
        Content=new Border{BorderBrush=ColorBrush("#40513D"),BorderThickness=new Thickness(1),Background=new LinearGradientBrush(Color.FromRgb(17,27,32),Color.FromRgb(10,16,22),70),Child=root};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){userDismissed=true;Close();e.Handled=true;}};
        Closed+=(_,_)=>{if(userDismissed&&owner is MainWindow main&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(main.RestoreAfterDialog));};
    }
    public void RequireConsent(string text)
    {
        Consent=new CheckBox{Content=Text(text,13,"#E7F0DA"),Margin=new Thickness(0,16,0,0),VerticalContentAlignment=VerticalAlignment.Top};AcceptButton.IsEnabled=false;
        Consent.Checked+=(_,_)=>AcceptButton.IsEnabled=true;Consent.Unchecked+=(_,_)=>AcceptButton.IsEnabled=false;Footer.Children.Insert(0,Consent);
    }
    public static BrandedDialog CreateConsent(Window? owner,string title,string body,string check,string action)
    {var dialog=new BrandedDialog(owner,title,action);dialog.Body.Children.Add(Text(body));dialog.RequireConsent(check);return dialog;}
    public static bool Confirm(Window? owner,string title,string body,string check,string action)=>CreateConsent(owner,title,body,check,action).ShowDialog()==true;
    public static bool Ask(Window? owner,string title,string body,string action="Продолжить")
    {var dialog=new BrandedDialog(owner,title,action){Height=Math.Min(440,SystemParameters.WorkArea.Height-28)};dialog.Body.Children.Add(Text(body));return dialog.ShowDialog()==true;}
    public static void Notify(Window? owner,string title,string body)
    {var dialog=new BrandedDialog(owner,title,"Понятно",false){Height=Math.Min(470,SystemParameters.WorkArea.Height-28)};dialog.Body.Children.Add(Text(body));dialog.ShowDialog();}
    public static BrandedDialog Publication(Window owner,Report report)
    {
        var dialog=new BrandedDialog(owner,"Поделиться результатом","Опубликовать результат");
        dialog.Body.Children.Add(Text("В таблице станет видна конфигурация ПК и измеренные результаты. Имя пользователя, имя ПК, серийные номера, процессы и локальные ошибки не отправляются.",14));
        var summary=new StackPanel{Margin=new Thickness(16)};summary.Children.Add(Text(report.System?.Cpu??"CPU не определён",17,"#EAF4DB"));summary.Children.Add(Text(report.System?.Gpu??"GPU не определён",14));summary.Children.Add(Text($"{report.System?.RamGb} ГБ RAM · {report.System?.Windows}",12));summary.Children.Add(Text($"{report.Measurements.Count} метрик · {report.SuiteVersion} · приложение {report.AppVersion}",12,"#C1FA55"));dialog.Body.Children.Add(new Border{Background=ColorBrush("#18251F"),CornerRadius=new CornerRadius(10),BorderBrush=ColorBrush("#354B38"),BorderThickness=new Thickness(1),Child=summary,Margin=new Thickness(0,3,0,15)});
        dialog.Body.Children.Add(Text("Результат будет доступен всем на tests.dedseckms.ru. Сочетание редких компонентов может быть узнаваемо. Сервер технически получает IP соединения и использует временный ограничитель без сохранения исходного IP. Результат сообщён клиентом и не проверен независимо. Ключ удаления сохранится в локальном отчёте — не делитесь этим JSON.",12));
        var json=new TextBox{Text=JsonSerializer.Serialize(report.PublicPayload(),Json.Options),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Consolas"),FontSize=11,MaxHeight=260,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};dialog.Body.Children.Add(new Expander{Header="Посмотреть точный состав отправки",Content=json,Foreground=ColorBrush("#C1FA55"),Margin=new Thickness(0,0,0,12)});
        dialog.RequireConsent("Я согласен опубликовать этот результат и показанную конфигурацию на tests.dedseckms.ru");return dialog;
    }
}
