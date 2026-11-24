using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace Dedsec.Tests;
public sealed class LivePlot:FrameworkElement
{
    private IReadOnlyList<TimelinePoint> points=Array.Empty<TimelinePoint>();private string unit="%",secondaryUnit="";private DateTime end;private int seconds=60;
    public Brush PrimaryBrush {get;set;}=new SolidColorBrush(Color.FromRgb(193,250,85));
    public void Update(IReadOnlyList<TimelinePoint> data,string firstUnit,string secondUnit,DateTime now,int windowSeconds)
    {points=data;unit=firstUnit;secondaryUnit=secondUnit;end=now;seconds=windowSeconds;InvalidateVisual();}
    public static double Scale(string unit,IEnumerable<double?> values)
    {if(unit=="%")return 100;double max=values.Where(v=>v.HasValue).Select(v=>v!.Value).DefaultIfEmpty(0).Max();if(unit=="°C")return Math.Max(100,Math.Ceiling(max/10)*10);if(max<=0)return 1;return Math.Pow(10,Math.Floor(Math.Log10(max)))*Math.Ceiling(max/Math.Pow(10,Math.Floor(Math.Log10(max))));}
    private void Label(DrawingContext dc,string text,double x,double y,Brush color,double size=10)
    {var ft=new FormattedText(text,CultureInfo.GetCultureInfo("ru-RU"),FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);dc.DrawText(ft,new Point(x,y));}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double width=Math.Max(1,ActualWidth),height=Math.Max(1,ActualHeight),left=32,right=secondaryUnit.Length>0?36:8,top=6,bottom=20,w=Math.Max(1,width-left-right),h=Math.Max(1,height-top-bottom);
        var muted=new SolidColorBrush(Color.FromRgb(111,133,149));var grid=new Pen(new SolidColorBrush(Color.FromRgb(41,57,69)),1);var second=new SolidColorBrush(Color.FromRgb(255,178,106));
        if(!points.Any(p=>p.Primary.HasValue||p.Secondary.HasValue)){Label(dc,"Нет датчика",left+5,top+h/2-8,muted,10);return;}
        double max=Scale(unit,points.Select(p=>p.Primary)),secondMax=Scale(secondaryUnit,points.Select(p=>p.Secondary));
        for(int line=0;line<=2;line++){double y=top+h*line/2;dc.DrawLine(grid,new Point(left,y),new Point(left+w,y));Label(dc,(max*(1-line/2d)).ToString("0.#"),0,y-5,muted);if(secondaryUnit.Length>0)Label(dc,(secondMax*(1-line/2d)).ToString("0.#"),left+w+5,y-5,second);}
        double X(TimelinePoint p)=>left+Math.Clamp(1-(end-p.Time).TotalSeconds/seconds,0,1)*w;
        void Draw(bool secondary,Brush brush,double maximum)
        {
            var path=new StreamGeometry();using(var context=path.Open()){bool active=false;DateTime? previous=null;foreach(var point in points){double? value=secondary?point.Secondary:point.Primary;if(!value.HasValue){active=false;previous=null;continue;}var at=new Point(X(point),top+h*(1-Math.Clamp(value.Value/maximum,0,1)));if(!active||previous.HasValue&&(point.Time-previous.Value).TotalSeconds>8){context.BeginFigure(at,false,false);active=true;}else context.LineTo(at,true,false);previous=point.Time;}}
            path.Freeze();dc.DrawGeometry(null,new Pen(brush,1.8),path);
            var last=points.LastOrDefault(p=>(secondary?p.Secondary:p.Primary).HasValue);if(last!=null){double value=(secondary?last.Secondary:last.Primary)!.Value;dc.DrawEllipse(brush,null,new Point(X(last),top+h*(1-Math.Clamp(value/maximum,0,1))),2.6,2.6);}
        }
        dc.PushClip(new RectangleGeometry(new Rect(left-3,0,w+6,top+h+4)));Draw(false,PrimaryBrush,max);if(secondaryUnit.Length>0)Draw(true,second,secondMax);dc.Pop();
        Label(dc,$"−{seconds/60d:0.#} мин",left,top+h+5,muted);Label(dc,"сейчас",Math.Max(left,left+w-38),top+h+5,muted);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);if(points.Count==0)return;double ratio=Math.Clamp((e.GetPosition(this).X-32)/Math.Max(1,ActualWidth-68),0,1);DateTime time=end.AddSeconds(-seconds*(1-ratio));var p=points.MinBy(p=>Math.Abs((p.Time-time).Ticks));if(p!=null)ToolTip=$"{p.Time.ToLocalTime():HH:mm:ss}\n{(p.Primary.HasValue?p.Primary.Value.ToString("0.##")+" "+unit:"нет данных")}"+(secondaryUnit.Length>0?$"\n{(p.Secondary.HasValue?p.Secondary.Value.ToString("0.##")+" "+secondaryUnit:"нет данных")}":"");
    }
}
