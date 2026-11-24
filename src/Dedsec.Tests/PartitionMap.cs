using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dedsec.Tests;
public static class PartitionMap
{
    public static Grid Create(StorageDisk disk)
    {
        var map=new Grid{Height=90,HorizontalAlignment=HorizontalAlignment.Left};
        var parts=disk.Partitions.Select(p=>(Offset:p.Offset,Bytes:p.Size,Title:$"#{p.Number} · {(p.Letter.Length>0?p.Letter+": · ":"")}{p.Role}",Subtitle:p.Filesystem.Length>0?p.Filesystem:p.Type,Color:p.Protected?"#344453":"#254C39",Partition:(StoragePartition?)p))
            .Concat(StorageBackend.Gaps(disk).Select(g=>(Offset:g.Offset,Bytes:g.Size,Title:"Свободное место",Subtitle:"Без раздела",Color:"#252D36",Partition:(StoragePartition?)null))).OrderBy(s=>s.Offset).ToArray();
        int column=0;foreach(var part in parts)
        {
            // Readable minimum width is intentional; this is a schematic, not a sector-accurate scale.
            map.ColumnDefinitions.Add(new(){Width=new GridLength(Math.Clamp(part.Bytes/(double)Math.Max(1UL,disk.Size)*540,175,430)),MinWidth=150,MaxWidth=900});
            var title=new TextBlock{Text=part.Title,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=Brush("#EDF4FA"),TextWrapping=TextWrapping.Wrap};
            var text=new StackPanel{Margin=new Thickness(12,8,10,8)};text.Children.Add(title);text.Children.Add(new TextBlock{Text=StorageBackend.Size(part.Bytes),Foreground=Brush("#C1FA55"),FontSize=13,Margin=new Thickness(0,5,0,2)});text.Children.Add(new TextBlock{Text=part.Subtitle,Foreground=Brush("#A4B7C6"),FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis});
            var card=new Border{Background=Brush(part.Color),CornerRadius=new CornerRadius(6),Child=text,ToolTip=part.Title+"\n"+StorageBackend.Size(part.Bytes)+" · "+part.Subtitle+"\nСмещение: "+StorageBackend.Size(part.Offset),Tag=part.Partition};Grid.SetColumn(card,column++);map.Children.Add(card);
            map.ColumnDefinitions.Add(new(){Width=new GridLength(6)});column++;
        }
        return map;
    }
    private static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
}
