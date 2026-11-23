using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dedsec.Tests;
public static class LiveTable
{
    public static T? Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T found)return found;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Find<T>(VisualTreeHelper.GetChild(root,i)) is T child)return child;
        return null;
    }
    public static void Refresh<T>(DataGrid grid,ObservableCollection<T> rows,IReadOnlyList<T> values,Func<T,string> key)
    {
        var scroll=Find<ScrollViewer>(grid);double vertical=scroll?.VerticalOffset??0,horizontal=scroll?.HorizontalOffset??0;
        string? selected=grid.SelectedItem is T item?key(item):null;
        // Keep the control, collection, column sort state and scroll viewers alive.
        for(int i=0;i<values.Count;i++){if(i>=rows.Count)rows.Add(values[i]);else if(!EqualityComparer<T>.Default.Equals(rows[i],values[i]))rows[i]=values[i];}
        while(rows.Count>values.Count)rows.RemoveAt(rows.Count-1);
        if(selected!=null)grid.SelectedItem=rows.FirstOrDefault(r=>key(r)==selected);
        grid.UpdateLayout();scroll?.ScrollToVerticalOffset(vertical);scroll?.ScrollToHorizontalOffset(horizontal);
    }
}
