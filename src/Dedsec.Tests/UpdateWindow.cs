using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dedsec.Update;
namespace Dedsec.Tests;
public enum UpdateStep{Checking,Available,Downloading,Verifying,Ready,Installing,Current,Failed,Cancelled}
public sealed class UpdateWindow
{
    public BrandedDialog Window{get;}
    public UpdateStep Step{get;private set;}
    public string? Stage{get;private set;}
    private readonly System.Windows.Window owner;
    private readonly HttpClient http;
    private readonly Version current;
    private readonly string api;
    private readonly TextBlock headline=Text("Подключаемся к GitHub…",23,"#F0F6FA"),detail=Text("",13),transfer=Text("",13,"#C1FA55");
    private readonly ProgressBar bar=new(){Minimum=0,Maximum=100,Height=8,Margin=new Thickness(0,12,0,12)};
    private readonly TextBlock[] steps={Text("01  Поиск"),Text("02  Скачивание"),Text("03  Проверка"),Text("04  Установка")};
    private readonly TextBox technical=new(){IsReadOnly=true,TextWrapping=TextWrapping.Wrap,FontSize=11,MaxHeight=130,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly TextBlock notes=Text("",12);
    private readonly Expander notesPanel=new(){Header="Что нового",Margin=new Thickness(0,12,0,0)},detailsPanel=new(){Header="Технические сведения",Margin=new Thickness(0,12,0,0),Visibility=Visibility.Collapsed};
    private readonly Button cancel=new(){Content="Отмена",Margin=new Thickness(0,0,10,0)},manual=new(){Content="Скачать через браузер",FontSize=12,Margin=new Thickness(0,0,10,0)};
    private CancellationTokenSource? operation;
    private AvailableUpdate? available;private UpdateManifest? manifest;private byte[]? metadata,signature;private bool busy,started,closed;
    public UpdateWindow(System.Windows.Window owner,HttpClient? client=null,Version? currentVersion=null,string? releaseApi=null)
    {
        this.owner=owner;http=client??UpdateNetwork.Create();current=currentVersion??typeof(Updates).Assembly.GetName().Version!;api=releaseApi??"https://api.github.com/repos/Dedseckms/Dedsec-Tests/releases?per_page=20";
        Window=new BrandedDialog(owner,"Обновление Dedsec Tests","Подождите…",false,false){Height=Math.Min(630,SystemParameters.WorkArea.Height-28),Width=Math.Min(750,SystemParameters.WorkArea.Width-28)};
        Window.Body.Children.Add(Text($"Установлена {current.ToString(3)} · {Updates.Architecture} · GitHub Releases",12));var flow=new Grid{Margin=new Thickness(0,16,0,24)};for(int i=0;i<steps.Length;i++){flow.ColumnDefinitions.Add(new());Grid.SetColumn(steps[i],i);steps[i].FontSize=11;flow.Children.Add(steps[i]);}Window.Body.Children.Add(flow);
        Window.Body.Children.Add(headline);Window.Body.Children.Add(detail);Window.Body.Children.Add(bar);Window.Body.Children.Add(transfer);notesPanel.Content=notes;Window.Body.Children.Add(notesPanel);detailsPanel.Content=technical;Window.Body.Children.Add(detailsPanel);
        Window.Footer.Children.Insert(0,Text("Локальные отчёты и настройки сохраняются. Установка начнётся после подтверждения.",11));Window.Actions.Children.Insert(0,manual);Window.Actions.Children.Insert(1,cancel);
        manual.Click+=(_,_)=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Updates.ReleasesUrl){UseShellExecute=true});cancel.Click+=(_,_)=>{if(busy){operation?.Cancel();cancel.IsEnabled=false;detail.Text="Отменяем запрос и закрываем файл загрузки…";}else Window.Close();};
        Window.AcceptButton.Click+=async(_,_)=>{if(busy)return;if(Step==UpdateStep.Available||Step==UpdateStep.Cancelled&&manifest!=null)await Download();else if(Step==UpdateStep.Ready)Install();else await Search();};
        Window.Loaded+=async(_,_)=>{if(!started){started=true;await Search();}};Window.Closing+=(_,_)=>{operation?.Cancel();closed=true;};Window.Closed+=(_,_)=>{http.Dispose();};Set(UpdateStep.Checking,"Ищем новую версию","Получаем список выпусков с GitHub. Это может занять несколько секунд.");
    }
    private static TextBlock Text(string value,double size=13,string color="#9BB1C1")=>new(){Text=value,FontSize=size,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};
    private void Set(UpdateStep step,string title,string description)
    {
        Step=step;headline.Text=title;detail.Text=description;int index=step switch{UpdateStep.Downloading=>1,UpdateStep.Verifying or UpdateStep.Ready=>2,UpdateStep.Installing=>3,_=>0};for(int i=0;i<steps.Length;i++)steps[i].Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(i<=index?"#C1FA55":"#6D8190"));
        bar.IsIndeterminate=step is UpdateStep.Checking or UpdateStep.Verifying;bar.Value=step is UpdateStep.Ready or UpdateStep.Current?100:0;transfer.Text="";Window.AcceptButton.Content=step switch{UpdateStep.Available=>$"Скачать {manifest!.Size/1048576d:F1} MiB",UpdateStep.Ready=>"Установить и перезапустить",UpdateStep.Current=>"Проверить ещё раз",UpdateStep.Cancelled when manifest!=null=>"Повторить скачивание",UpdateStep.Failed or UpdateStep.Cancelled=>"Повторить проверку",_=>"Подождите…"};Window.AcceptButton.IsEnabled=!busy;cancel.IsEnabled=true;cancel.Content=busy?"Отмена":"Закрыть";
    }
    private CancellationToken Begin()
    {busy=true;operation?.Dispose();operation=new();Window.AcceptButton.IsEnabled=false;detailsPanel.Visibility=Visibility.Collapsed;cancel.Content="Отмена";return operation.Token;}
    private void End(){busy=false;if(!closed){Window.AcceptButton.IsEnabled=true;cancel.IsEnabled=true;cancel.Content="Закрыть";}}
    private IProgress<string> Status()=>new Progress<string>(text=>{if(!closed)detail.Text=text;});
    public async Task Search()
    {
        if(busy)return;var token=Begin();manifest=null;available=null;notesPanel.Visibility=Visibility.Collapsed;Set(UpdateStep.Checking,"Ищем новую версию","Получаем список выпусков с GitHub…");
        try
        {
            using var releases=JsonDocument.Parse(await UpdateNetwork.Metadata(http,api,2_000_000,token,Status()));available=Updates.Select(releases.RootElement,Updates.Architecture,current);
            if(available==null){Set(UpdateStep.Current,"Установлена актуальная версия",$"Проверено: новых подписанных выпусков для {Updates.Architecture} новее {current.ToString(3)} нет.");return;}
            detail.Text="Новая версия найдена. Проверяем подпись сведений о пакете…";metadata=await UpdateNetwork.Metadata(http,available.ManifestUrl,16384,token,Status());signature=await UpdateNetwork.Metadata(http,available.SignatureUrl,512,token,Status());manifest=Trust.Verify(metadata,signature);
            if(manifest.Architecture!=Updates.Architecture||Version.Parse(manifest.Version)!=available.Version)throw new InvalidDataException("Версия или архитектура подписанного пакета не совпадает с выпуском.");
            notes.Text=available.Notes[..Math.Min(available.Notes.Length,6000)];notesPanel.Visibility=Visibility.Visible;Set(UpdateStep.Available,$"Доступна версия {manifest.Version}",$"{current.ToString(3)} → {manifest.Version} · пакет {manifest.Size/1048576d:F1} MiB. Нажми «Скачать», чтобы начать.");
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){if(!closed)Set(UpdateStep.Cancelled,"Проверка отменена","Программа не изменена. Можно повторить поиск.");}
        catch(Exception error){Fail(error);}
        finally{End();}
    }
    public async Task Download()
    {
        if(busy||manifest==null)return;var token=Begin();Stage=Path.Combine(Settings.DataDir,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Stage);string zip=Path.Combine(Stage,"package.zip");Set(UpdateStep.Downloading,$"Скачиваем {manifest.Version}","Подключаемся к серверу файлов GitHub…");
        try
        {
            var progress=new Progress<PackageProgress>(value=>{if(closed||Step!=UpdateStep.Downloading)return;bar.Value=value.Percent;transfer.Text=value.Text;headline.Text=$"Скачиваем обновление · {value.Percent:F0}%";detail.Text=$"Попытка {value.Attempt} из 3 · отмена доступна до установки";});
            await UpdateNetwork.Download(http,manifest.Url,zip,manifest.Size,progress,token,Status());token.ThrowIfCancellationRequested();Set(UpdateStep.Verifying,"Проверяем скачанный пакет","Вычисляем SHA-256 и сверяем размер с подписанным манифестом…");await Task.Run(()=>Trust.VerifyPackage(zip,manifest),token);token.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(Path.Combine(Stage,"manifest.json"),metadata!,token);await File.WriteAllBytesAsync(Path.Combine(Stage,"manifest.sig"),signature!,token);Set(UpdateStep.Ready,"Всё готово к установке",$"Версия {manifest.Version}. Подпись, размер и SHA-256 проверены. После нажатия программа закроется, помощник покажет установку и запустит новую версию.");
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){DeletePartial(zip);if(!closed)Set(UpdateStep.Cancelled,"Скачивание отменено","Неполный файл удалён. Текущая программа и отчёты сохранены.");}
        catch(Exception error){DeletePartial(zip);Fail(error);}
        finally{End();}
    }
    private static void DeletePartial(string path){try{File.Delete(path);}catch{}}
    private void Fail(Exception error)
    {
        if(closed)return;Set(UpdateStep.Failed,"Обновление не завершено",UpdateNetwork.Explain(error));string details=UpdateNetwork.Details(error);try{string logs=Path.Combine(Settings.DataDir,"updates","logs");Directory.CreateDirectory(logs);string path=Path.Combine(logs,DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(path,DateTimeOffset.Now+"\n"+details);details+="\n\nЛокальный журнал: "+path;}catch{}technical.Text=details;detailsPanel.Visibility=Visibility.Visible;
    }
    private void Install()
    {
        if(Stage==null)return;if(owner is MainWindow main&&!main.CanUpdate){detail.Text="Сначала дождись завершения текущей операции или останови тест.";return;}
        try{Set(UpdateStep.Installing,"Запускаем установку","Сейчас откроется помощник установки. Он покажет копирование и запуск новой версии.");Window.AcceptButton.IsEnabled=false;Updates.Install(Stage);Window.Close();System.Windows.Application.Current.Shutdown();}catch(Exception error){Fail(error);End();}
    }
}
