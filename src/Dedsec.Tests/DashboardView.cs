using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dedsec.Tests;

// Four cards occupy the viewport. No outer scrolling or chart library.
public sealed class DashboardView:Grid
{
    private readonly Dictionary<string,MetricCard> cards=new();
    public DashboardView(params string[] keys)
    {
        ColumnDefinitions.Add(new());ColumnDefinitions.Add(new());RowDefinitions.Add(new());RowDefinitions.Add(new());
        var metrics=TelemetryHistory.Select(Array.Empty<SensorRow>());int index=0;
        foreach(string key in keys.Take(4))
        {
            var metric=metrics.Single(m=>m.Key==key);var card=new MetricCard(metric){Margin=new Thickness(index%2==0?0:6,index<2?0:6,index%2==0?6:0,index<2?6:0)};
            SetColumn(card,index%2);SetRow(card,index/2);Children.Add(card);cards[key]=card;index++;
        }
    }
    public void Update(TelemetryHistory history,int seconds,DateTime now,IReadOnlyDictionary<string,IReadOnlyList<TimelinePoint>>? frozen=null)
    {foreach(var metric in history.Latest)if(cards.TryGetValue(metric.Key,out var card))card.Update(metric,frozen!=null&&frozen.TryGetValue(metric.Key,out var points)?points:history.Points(metric.Key,now,seconds),now,seconds);}
    private sealed class MetricCard:Border
    {
        private readonly TextBlock primary,secondary,subtitle,stats;private readonly LivePlot first,second;private readonly bool paired;
        private static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(141,163,179)),Lime=new SolidColorBrush(Color.FromRgb(193,250,85)),Cyan=new SolidColorBrush(Color.FromRgb(95,211,231));
        private static TextBlock Text(string s,double size,Brush color)=>new(){Text=s,FontSize=size,Foreground=color,TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis};
        public MetricCard(DashboardMetric metric)
        {
            Padding=new Thickness(14,12,14,10);CornerRadius=new CornerRadius(13);BorderThickness=new Thickness(1);BorderBrush=new SolidColorBrush(Color.FromRgb(43,59,70));Background=new LinearGradientBrush(Color.FromRgb(23,34,41),Color.FromRgb(15,23,30),65);
            var panel=new Grid();foreach(var height in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})panel.RowDefinitions.Add(new(){Height=height});
            var heading=new DockPanel{Margin=new Thickness(0,0,0,7)};string asset=metric.Key is "cpu" or "gpu" or "ram"?metric.Key:"disk";
            var image=new Image{Source=new BitmapImage(new Uri($"pack://application:,,,/Dedsec.Tests;component/Assets/{asset}.png")),Width=34,Height=34,Margin=new Thickness(0,0,10,0)};DockPanel.SetDock(image,Dock.Left);heading.Children.Add(image);
            var names=new StackPanel();names.Children.Add(Text(metric.Title,14,Brushes.WhiteSmoke));subtitle=Text(metric.Detail,10,Muted);names.Children.Add(subtitle);heading.Children.Add(names);panel.Children.Add(heading);
            paired=metric.SecondaryName.Length>0&&metric.Key!="ram";
            var readings=new Grid{Margin=new Thickness(0,0,0,4)};readings.ColumnDefinitions.Add(new());if(paired)readings.ColumnDefinitions.Add(new());
            StackPanel Reading(string title,Brush brush,out TextBlock number){var p=new StackPanel();p.Children.Add(Text(title,10,Muted));number=Text("—",23,brush);number.FontWeight=FontWeights.SemiBold;p.Children.Add(number);return p;}
            readings.Children.Add(Reading(metric.PrimaryName,Lime,out primary));var reading=Reading(metric.SecondaryName,Cyan,out secondary);
            if(paired){SetColumn(reading,1);reading.Margin=new Thickness(12,0,0,0);readings.Children.Add(reading);}else if(metric.Key=="ram"){reading.Children.Remove(secondary);secondary.FontSize=12;secondary.HorizontalAlignment=HorizontalAlignment.Right;secondary.VerticalAlignment=VerticalAlignment.Bottom;readings.Children.Add(secondary);}
            SetRow(readings,1);panel.Children.Add(readings);
            var plots=new Grid();plots.ColumnDefinitions.Add(new());if(paired)plots.ColumnDefinitions.Add(new());first=new LivePlot{MinHeight=24,PrimaryBrush=Lime};second=new LivePlot{MinHeight=24,PrimaryBrush=Cyan};plots.Children.Add(first);if(paired){SetColumn(second,1);second.Margin=new Thickness(12,0,0,0);plots.Children.Add(second);}SetRow(plots,2);panel.Children.Add(plots);
            stats=Text("Ожидаем показания…",10,Muted);stats.Margin=new Thickness(0,5,0,0);SetRow(stats,3);panel.Children.Add(stats);Child=panel;
            SizeChanged+=(_,_)=>{bool compact=ActualHeight<215;subtitle.Visibility=compact?Visibility.Collapsed:Visibility.Visible;stats.Visibility=compact?Visibility.Collapsed:Visibility.Visible;image.Width=image.Height=compact?24:34;heading.Margin=new Thickness(0,0,0,compact?4:7);primary.FontSize=compact?20:23;if(metric.Key!="ram")secondary.FontSize=compact?20:23;};
            Loaded+=(_,_)=>{if(TryFindResource("Accent") is Brush accent){primary.Foreground=accent;first.PrimaryBrush=accent;}};
        }
        private static string Value(double? value,string unit)=>value.HasValue?value.Value.ToString(unit=="%"||unit=="RPM"?"0":value.Value is >0 and <1?"0.##":"0.#")+" "+unit:"—";
        public void Update(DashboardMetric metric,IReadOnlyList<TimelinePoint> points,DateTime now,int seconds)
        {
            var latest=points.LastOrDefault();primary.Text=Value(latest?.Primary,metric.PrimaryUnit);secondary.Text=metric.SecondaryName.Length>0?Value(latest?.Secondary,metric.SecondaryUnit):"";
            subtitle.Text=metric.Detail;ToolTip=metric.Detail+(metric.Source!=null?"\nИсточник: "+metric.Source:"");
            first.Update(points.Select(p=>p with{Secondary=null}).ToArray(),metric.PrimaryUnit,"",now,seconds);
            if(paired)second.Update(points.Select(p=>new TimelinePoint(p.Time,p.Secondary,null)).ToArray(),metric.SecondaryUnit,"",now,seconds);
            var values=points.Where(p=>p.Primary.HasValue).Select(p=>p.Primary!.Value).ToArray();stats.Text=values.Length>0?$"МИН {Value(values.Min(),metric.PrimaryUnit)}     СРЕД {Value(values.Average(),metric.PrimaryUnit)}     ПИК {Value(values.Max(),metric.PrimaryUnit)}":"Датчик не предоставил измерения";
        }
    }
}
