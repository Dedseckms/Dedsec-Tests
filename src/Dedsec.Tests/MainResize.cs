using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dedsec.Tests;
public partial class MainWindow
{
    private readonly Dictionary<string,(JsonElement Bounds,DateTime Utc)> resizeBounds=new();
    private async Task ConfigureResize(StorageDisk disk,StoragePartition part)
    {
        if(!CanUpdate)return;
        if(part.Protected&&!part.Boot){BrandedDialog.Notify(this,"Защищённый раздел","Изменение размера EFI, Recovery и служебных разделов не выполняется.");return;}
        var dialog=new BrandedDialog(this,"Изменение размера раздела","Применить"){Height=Math.Min(760,SystemParameters.WorkArea.Height-28)};dialog.AcceptButton.IsEnabled=false;
        dialog.Body.Children.Add(Txt(disk.Description,15,"#C1FA55"));dialog.Body.Children.Add(Txt(part.Description,13));var state=Txt("Windows рассчитывает допустимые границы…",14);dialog.Body.Children.Add(state);
        var progress=new ProgressBar{IsIndeterminate=true,Height=7,Margin=new Thickness(0,12,0,14)};dialog.Body.Children.Add(progress);var host=new StackPanel();dialog.Body.Children.Add(host);
        var note=Txt("Расчёт может занять несколько минут: Windows анализирует файловую систему. Отмена закрывает ожидание; служба Windows может завершить анализ в фоне. Форматирование и изменение размера сейчас не выполняются.",12,"#91A7B8");dialog.Body.Children.Add(note);
        string token=$"ДИСК {disk.Number} РАЗДЕЛ {part.Number}";dialog.Body.Children.Add(Txt("Для применения введи: "+token,12,"#FFBF7F",new Thickness(0,14,0,6)));var typed=new TextBox();dialog.Body.Children.Add(typed);dialog.RequireConsent("Цель проверена. Важные данные сохранены отдельно. Я подтверждаю выбранный размер.");
        bool closed=false,ready=false,touched=false;ResizeEditor? editor=null;var clock=Stopwatch.StartNew();var ticker=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};ticker.Tick+=(_,_)=>state.Text=$"Расчёт границ Windows · {clock.Elapsed:mm\\:ss}";ticker.Start();
        void Gate()=>dialog.AcceptButton.IsEnabled=ready&&touched&&editor!=null&&editor.InputValid&&editor.SelectedBytes!=part.Size&&typed.Text==token&&dialog.Consent!.IsChecked==true;
        typed.TextChanged+=(_,_)=>Gate();dialog.Consent!.Checked+=(_,_)=>Gate();dialog.Consent.Unchecked+=(_,_)=>Gate();var cancel=dialog.Actions.Children[0] as Button;if(cancel!=null)cancel.Content="Отменить ожидание";
        string key=disk.Key+"|"+disk.LayoutKey+"|"+part.Number;storageBusy=true;
        dialog.Loaded+=async(_,_)=>
        {
            try
            {
                JsonElement bounds;if(resizeBounds.TryGetValue(key,out var cached)&&(DateTime.UtcNow-cached.Utc).TotalSeconds<30)bounds=cached.Bounds;else{bounds=await StorageBackend.Run(StorageBackend.Request("bounds",disk,part));resizeBounds[key]=(bounds,DateTime.UtcNow);}
                if(closed)return;editor=new(bounds.GetProperty("min").GetUInt64(),bounds.GetProperty("max").GetUInt64(),part.Size);host.Children.Add(editor);editor.Changed+=(_,_)=>{touched=true;dialog.Consent.IsChecked=false;Gate();};state.Text="Границы получены · потяни правый край на схеме";note.Text="Расширение использует доступное смежное место. Если Windows разрешает освободить лишь малую часть тома, причиной могут быть неперемещаемые файлы. Перед применением границы и разметка проверяются снова.";ready=true;
            }
            catch(Exception error){if(!closed){state.Text="Не удалось получить границы";note.Text=error.Message;}}
            finally{storageBusy=false;ticker.Stop();if(!closed){progress.Visibility=Visibility.Collapsed;if(cancel!=null)cancel.Content="Отмена";Gate();}else if(!Dispatcher.HasShutdownStarted&&current=="storage")ShowPage("storage");}
        };
        dialog.Closed+=(_,_)=>{closed=true;ticker.Stop();if(storageBusy)Activity.Text="Ожидание закрыто · Windows завершает запрос границ в фоне";};
        if(dialog.ShowDialog()!=true||!ready||editor==null||!editor.InputValid)return;
        var request=StorageBackend.Request("resize",disk,part);request["bytes"]=editor.SelectedBytes;resizeBounds.Clear();await ApplyStorage(request);
    }
}
