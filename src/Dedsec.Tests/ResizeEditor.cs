using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dedsec.Tests;
public sealed class ResizeEditor:StackPanel
{
    public ulong SelectedBytes=>boundary.SelectedBytes;
    public bool InputValid{get;private set;}=true;
    public event EventHandler? Changed;
    public readonly ResizeBoundary Boundary;
    private readonly ResizeBoundary boundary;
    private readonly TextBlock result=new(){FontSize=20,Foreground=Brush("#C1FA55"),Margin=new Thickness(0,12,0,6)};
    private readonly TextBlock delta=new(){FontSize=12,Foreground=Brush("#A6B9C7"),TextWrapping=TextWrapping.Wrap};
    private readonly TextBox exact=new(){Width=145,Margin=new Thickness(0,0,10,0)};
    private readonly ComboBox units=new(){ItemsSource=new[]{"GiB","MiB"},SelectedIndex=0,Width=100,MinHeight=34};
    private bool syncing;private readonly ulong original;
    public ResizeEditor(ulong minimum,ulong maximum,ulong current)
    {
        original=current;boundary=new(minimum,maximum,current);Boundary=boundary;
        Children.Add(new TextBlock{Text="Потяни светящуюся правую границу раздела",FontSize=15,Foreground=Brush("#EEF4F8"),Margin=new Thickness(0,14,0,8)});Children.Add(boundary);
        Children.Add(new TextBlock{Text="Подробный масштаб допустимого диапазона. Начало раздела фиксировано.",FontSize=11,Foreground=Brush("#90A6B8"),Margin=new Thickness(0,8,0,0)});Children.Add(result);Children.Add(delta);
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,8)};row.Children.Add(exact);row.Children.Add(units);Children.Add(row);
        units.SelectionChanged+=(_,_)=>Refresh();exact.TextChanged+=(_,_)=>{if(syncing)return;if(decimal.TryParse(exact.Text,NumberStyles.Number,CultureInfo.CurrentCulture,out decimal value)&&value>=0&&value<ulong.MaxValue/1024m){decimal mib=units.SelectedIndex==0?value*1024:value;if(mib<=ulong.MaxValue/StorageBackend.MiB){ulong bytes=(ulong)decimal.Round(mib,0,MidpointRounding.AwayFromZero)*StorageBackend.MiB;if(bytes>=boundary.Minimum&&bytes<=boundary.Maximum){InputValid=true;boundary.SetBytes(bytes);exact.BorderBrush=Brush("#384652");Changed?.Invoke(this,EventArgs.Empty);return;}}}InputValid=false;exact.BorderBrush=Brush("#FF9966");Changed?.Invoke(this,EventArgs.Empty);};
        boundary.Changed+=(_,_)=>{Refresh();Changed?.Invoke(this,EventArgs.Empty);};Refresh();
    }
    private void Refresh()
    {
        InputValid=true;syncing=true;exact.Text=units.SelectedIndex==0?(SelectedBytes/1073741824m).ToString("0.######",CultureInfo.CurrentCulture):(SelectedBytes/StorageBackend.MiB).ToString(CultureInfo.CurrentCulture);syncing=false;
        result.Text="Новый размер: "+StorageBackend.Size(SelectedBytes);delta.Text=(SelectedBytes<original?"Освободится после раздела: "+StorageBackend.Size(original-SelectedBytes):SelectedBytes>original?"Будет добавлено к разделу: "+StorageBackend.Size(SelectedBytes-original):"Размер пока не изменён")+"\nГраницы Windows: "+StorageBackend.Size(boundary.Minimum)+" … "+StorageBackend.Size(boundary.Maximum);
    }
    private static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
}
public sealed class ResizeBoundary:FrameworkElement
{
    public ulong Minimum{get;}public ulong Maximum{get;}public ulong SelectedBytes{get;private set;}
    public event EventHandler? Changed;private bool dragging;
    public ResizeBoundary(ulong minimum,ulong maximum,ulong current)
    {
        if(minimum>ulong.MaxValue-StorageBackend.MiB)throw new ArgumentOutOfRangeException(nameof(minimum));Minimum=(minimum+StorageBackend.MiB-1)/StorageBackend.MiB*StorageBackend.MiB;Maximum=maximum/StorageBackend.MiB*StorageBackend.MiB;if(Maximum<Minimum)throw new ArgumentException("Windows не вернула доступный выровненный диапазон.");SelectedBytes=Math.Clamp(current/StorageBackend.MiB*StorageBackend.MiB,Minimum,Maximum);Height=94;MinWidth=200;Cursor=Cursors.SizeWE;Focusable=true;
        MouseLeftButtonDown+=(_,e)=>{if(Maximum==Minimum)return;Focus();dragging=true;CaptureMouse();Move(e.GetPosition(this).X);e.Handled=true;};MouseMove+=(_,e)=>{if(dragging)Move(e.GetPosition(this).X);};MouseLeftButtonUp+=(_,e)=>{dragging=false;ReleaseMouseCapture();e.Handled=true;};LostMouseCapture+=(_,_)=>dragging=false;
        KeyDown+=(_,e)=>{long delta=e.Key is Key.Left or Key.Down?-1:e.Key is Key.Right or Key.Up?1:0;if(delta==0)return;ulong step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?1024*StorageBackend.MiB:StorageBackend.MiB;SetBytes(delta<0?(SelectedBytes>Minimum+step?SelectedBytes-step:Minimum):(Maximum-SelectedBytes>step?SelectedBytes+step:Maximum));e.Handled=true;};
    }
    public void SetFraction(double fraction){if(!double.IsFinite(fraction))return;ulong steps=(Maximum-Minimum)/StorageBackend.MiB;SetBytes(Minimum+(ulong)Math.Round(Math.Clamp(fraction,0,1)*steps)*StorageBackend.MiB);}
    public void SetBytes(ulong value){ulong aligned=Math.Clamp(value/StorageBackend.MiB*StorageBackend.MiB,Minimum,Maximum);if(aligned==SelectedBytes)return;SelectedBytes=aligned;InvalidateVisual();Changed?.Invoke(this,EventArgs.Empty);}
    private void Move(double x){double start=Math.Min(135,ActualWidth*.28),width=Math.Max(1,ActualWidth-start-18);SetFraction((x-start)/width);}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);double w=ActualWidth,h=ActualHeight;if(w<1||h<1)return;double start=Math.Min(135,w*.28),end=w-18,span=Math.Max(1,end-start),fraction=Maximum==Minimum?1:(SelectedBytes-Minimum)/(double)(Maximum-Minimum),handle=start+span*fraction;
        dc.DrawRoundedRectangle(Brush("#202E36"),null,new Rect(0,0,w,h),8,8);dc.DrawRoundedRectangle(Brush("#34593C"),null,new Rect(0,0,Math.Max(1,handle),h),8,8);dc.DrawLine(new Pen(Brush("#66806B"),1){DashStyle=DashStyles.Dash},new Point(start,10),new Point(start,h-10));
        Draw(dc,"Фиксировано",12,"#BACDBD",10,15);Draw(dc,StorageBackend.Size(Minimum),12,"#EDF5EE",10,38);if(end-handle>95)Draw(dc,"Свободно",12,"#A9BDCB",handle+15,33);
        dc.DrawRoundedRectangle(Brush("#C1FA55"),null,new Rect(handle-6,9,12,h-18),4,4);dc.DrawLine(new Pen(Brush("#23351D"),2),new Point(handle,35),new Point(handle,h-35));
    }
    private void Draw(DrawingContext dc,string text,double size,string color,double x,double y)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Brush(color),VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
    private static SolidColorBrush Brush(string value)=>new((Color)ColorConverter.ConvertFromString(value));
}
