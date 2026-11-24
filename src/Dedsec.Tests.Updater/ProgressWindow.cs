using System.Runtime.InteropServices;
namespace Dedsec.Tests.Updater;
internal sealed class ProgressWindow:Form
{
    private bool working=true;
    private readonly Label status=new(){AutoSize=false,ForeColor=Color.FromArgb(235,244,248),Font=new Font("Segoe UI",17),Text="Готовим установку…",Location=new Point(30,126),Size=new Size(610,65)};
    private readonly Label detail=new(){ForeColor=Color.FromArgb(158,181,196),Font=new Font("Segoe UI",10),Location=new Point(30,235),Size=new Size(610,65),Text="Дождись завершения. Помощник сохранит предыдущие файлы и запустит обновлённую программу."};
    private readonly Panel track=new(){BackColor=Color.FromArgb(35,49,57),Location=new Point(30,206),Size=new Size(610,8)};
    private readonly Panel fill=new(){BackColor=Color.FromArgb(193,250,85),Size=new Size(0,8)};
    private readonly Button close=new(){Text="✕",FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(33,45,55),ForeColor=Color.White,Location=new Point(596,20),Size=new Size(44,36),Enabled=false};
    private readonly TextBox error=new(){ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(17,26,32),ForeColor=Color.FromArgb(224,236,242),BorderStyle=BorderStyle.None,Location=new Point(30,303),Size=new Size(610,105),Visible=false};
    internal ProgressWindow(Action apply)
    {
        Text="Dedsec Tests · установка обновления";ClientSize=new Size(670,330);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(11,17,23);ForeColor=Color.White;Font=new Font("Segoe UI",11);Icon=Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        var title=new Label{Text="DEDSEC / TESTS    •    ОБНОВЛЕНИЕ",ForeColor=Color.FromArgb(193,250,85),Location=new Point(30,28),Size=new Size(540,28)};var steps=new Label{Text="Пакет     →     Копия файлов     →     Установка     →     Запуск",ForeColor=Color.FromArgb(139,160,174),Font=new Font("Segoe UI",10),Location=new Point(30,85),Size=new Size(610,28)};Controls.AddRange(new Control[]{title,steps,status,detail,track,close,error});track.Controls.Add(fill);close.FlatAppearance.BorderSize=0;close.Click+=(_,_)=>Close();title.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,2,0);}};
        FormClosing+=(_,e)=>{if(working)e.Cancel=true;};Shown+=async(_,_)=>
        {
            Program.Progress=(message,percent)=>{if(IsDisposed)return;BeginInvoke(new Action(()=>{status.Text=message;fill.Width=(int)Math.Round(track.Width*Math.Clamp(percent,0,100)/100);}));};
            try{await Task.Run(apply);working=false;detail.Text="Готово. Новая версия запущена; это окно закроется автоматически.";await Task.Delay(1500);Close();}
            catch(Exception exception){working=false;Environment.ExitCode=1;status.Text="Установка остановлена";detail.Text="Посмотри подробности ниже. Резервные файлы, если они были созданы, сохранены в папке rollback пакета.";error.Text=exception.Message;error.Visible=true;ClientSize=new Size(670,435);close.Enabled=true;}
            finally{Program.Progress=null;}
        };
    }
    [DllImport("user32.dll")]private static extern bool ReleaseCapture();
    [DllImport("user32.dll")]private static extern nint SendMessage(nint window,int message,nint wParam,nint lParam);
}
