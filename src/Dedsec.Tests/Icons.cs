using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Dedsec.Tests;
public static class Icons
{
    // Original vector paths: no font dependencies, consistent on Windows 10/11.
    private static readonly Dictionary<string,string> Paths=new()
    {
        ["overview"]="M3,10 L12,3 21,10 21,21 15,21 15,14 9,14 9,21 3,21 Z",
        ["tests"]="M8,3 L16,3 M10,3 L10,9 4,18 Q3,21 7,21 L17,21 Q21,21 19,18 L14,9 14,3 M7,15 L17,15 M10,18 L11,18 M14,17 L15,17",
        ["sensors"]="M4,12 L8,12 11,5 14,19 17,12 21,12 M3,5 L3,3 7,3 M17,3 L21,3 21,7 M21,18 L21,21 17,21 M7,21 L3,21 3,17",
        ["processes"]="M3,21 L21,21 M5,17 L5,12 M10,17 L10,6 M15,17 L15,10 M20,17 L20,3",
        ["storage"]="M4,4 L20,4 23,16 23,21 1,21 1,16 Z M1,16 L23,16 M16,19 L17,19 M20,19 L21,19 M7,8 L17,8 M6,11 L18,11",
        ["network"]="M2,8 Q12,-1 22,8 M5,12 Q12,5 19,12 M9,16 Q12,13 15,16 M12,20 L12,21",
        ["power"]="M14,2 L5,14 11,14 9,24 21,10 14,10 Z",
        ["fans"]="M15,12 Q23,3 17,2 Q10,0 11,9 M12,15 Q20,23 23,17 Q25,10 17,11 M9,13 Q0,20 2,13 Q0,6 9,10 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12",
        ["diagnostics"]="M12,2 L21,6 20,14 Q18,20 12,23 Q6,20 4,14 L3,6 Z M8,12 L11,15 17,9",
        ["events"]="M5,3 L16,3 20,7 20,21 5,21 Z M15,3 L15,8 20,8 M9,11 L16,11 M9,15 L16,15 M9,18 L13,18",
        ["reports"]="M4,4 L4,10 10,10 M4,10 A9,9 0 1 1 5,18 M12,7 L12,13 16,15",
        ["settings"]="M10,2 L14,2 15,5 18,6 21,5 23,9 20,12 21,15 23,17 21,21 18,20 15,21 14,24 10,24 9,21 6,20 3,21 1,17 4,15 4,12 1,9 3,5 6,6 9,5 Z M16,13 A4,4 0 1 1 8,13 A4,4 0 1 1 16,13",
    };
    public static Viewbox Create(string name,double size=22,Brush? stroke=null)
    {
        var path=new System.Windows.Shapes.Path{Data=Geometry.Parse(Paths.GetValueOrDefault(name,Paths["diagnostics"])),Stroke=stroke??new SolidColorBrush(Color.FromRgb(175,194,207)),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Fill=Brushes.Transparent};var canvas=new Canvas{Width=26,Height=26};canvas.Children.Add(path);return new Viewbox{Width=size,Height=size,Child=canvas,Margin=new Thickness(0,0,12,0)};
    }
}
