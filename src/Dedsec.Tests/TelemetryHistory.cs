using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
namespace Dedsec.Tests;
public sealed record TimelinePoint(DateTime Time,double? Primary,double? Secondary);
public sealed record DashboardMetric(string Key,string Title,string Detail,string PrimaryName,string PrimaryUnit,string SecondaryName,string SecondaryUnit,double? Primary,double? Secondary,string? Source);
public sealed class TelemetryHistory
{
    public const int Capacity=460;
    private readonly Dictionary<string,List<TimelinePoint>> samples=new();
    public IReadOnlyList<DashboardMetric> Latest {get;private set;}=Array.Empty<DashboardMetric>();
    public static IReadOnlyList<DashboardMetric> Select(IReadOnlyList<SensorRow> sensors)
    {
        SensorRow? ById(string id)=>sensors.FirstOrDefault(s=>s.Id==id);
        SensorRow? Pick(string group,string type,params string[] names){var rows=sensors.Where(s=>s.Group==group&&s.Type==type).ToList();foreach(string name in names){var row=rows.FirstOrDefault(s=>s.Name.Contains(name,StringComparison.OrdinalIgnoreCase));if(row!=null)return row;}return type=="Temperature"?rows.MaxBy(s=>s.Value):rows.FirstOrDefault();}
        var cpu=ById("/windows/cpu/load")??Pick("Cpu","Load","Total");var cpuTemp=Pick("Cpu","Temperature","Package","Tctl","Average");
        var gpu=sensors.Where(s=>s.Group.StartsWith("Gpu")&&s.Type=="Load").OrderByDescending(s=>s.Name.Contains("Core",StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        var gpuTemp=sensors.Where(s=>s.Group.StartsWith("Gpu")&&s.Type=="Temperature"&& (gpu==null||s.Device==gpu.Device)).OrderByDescending(s=>s.Name.Equals("GPU Core",StringComparison.OrdinalIgnoreCase)).ThenByDescending(s=>s.Value).FirstOrDefault();
        var cpuPower=Pick("Cpu","Power","Package");var gpuPower=sensors.FirstOrDefault(s=>s.Group.StartsWith("Gpu")&&s.Type=="Power"&&s.Name.Contains("Package",StringComparison.OrdinalIgnoreCase))??sensors.FirstOrDefault(s=>s.Group.StartsWith("Gpu")&&s.Type=="Power");
        var diskTemp=sensors.Where(s=>s.Group=="Storage"&&s.Type=="Temperature"&&!s.Name.Contains("#")).MaxBy(s=>s.Value)??sensors.Where(s=>s.Group=="Storage"&&s.Type=="Temperature").MaxBy(s=>s.Value);
        var cpuFan=sensors.FirstOrDefault(s=>s.Type=="Fan"&&s.Name.Contains("CPU",StringComparison.OrdinalIgnoreCase));var gpuFan=sensors.FirstOrDefault(s=>s.Type=="Fan"&&s.Group.StartsWith("Gpu"));
        DashboardMetric Metric(string key,string title,string detail,string first,string unit,string second,string unit2,SensorRow? a,SensorRow? b)=>new(key,title,detail,first,unit,second,unit2,a?.Value,b?.Value,a==null?null:a.Device+" / "+a.Name);
        return new[]{Metric("cpu","Процессор",cpuTemp?.Device??"Нагрузка из Windows · температура из аппаратного датчика","Загрузка","%","Температура","°C",cpu,cpuTemp),
            Metric("gpu","Видеокарта",gpu?.Device??gpuTemp?.Device??"Аппаратный датчик пока недоступен","Загрузка","%","Температура","°C",gpu,gpuTemp),
            Metric("ram","Оперативная память","Физическая RAM · данные Windows","Занято","%","Используется","GiB",ById("/windows/memory/load"),ById("/windows/memory/used")),
            Metric("storage","Температура SSD / HDD",diskTemp?.Device??"Датчик текущей температуры не получен","Температура","°C","","",diskTemp,null),
            Metric("disk","Накопители","Текущий обмен всех физических дисков · это не бенчмарк","Чтение","MiB/s","Запись","MiB/s",ById("/windows/disk/read"),ById("/windows/disk/write")),
            Metric("network","Сеть","Сумма активных Ethernet / Wi-Fi, включая виртуальные · не скорость интернета","Приём","MiB/s","Отправка","MiB/s",ById("/windows/network/receive"),ById("/windows/network/send")),
            Metric("power","Энергопотребление","Датчики CPU Package и GPU · не потребление ПК из розетки","CPU","W","GPU","W",cpuPower,gpuPower),
            Metric("fans","Охлаждение","Обороты доступных CPU / GPU вентиляторов · не все модели передают RPM","CPU","RPM","GPU","RPM",cpuFan,gpuFan)};
    }
    public void Record(IReadOnlyList<SensorRow> sensors,DateTime now)
    {
        Latest=Select(sensors);
        foreach(var metric in Latest){if(!samples.TryGetValue(metric.Key,out var series))samples[metric.Key]=series=new();series.Add(new(now,Valid(metric.Primary),Valid(metric.Secondary)));if(series.Count>Capacity)series.RemoveRange(0,series.Count-Capacity);}
    }
    private static double? Valid(double? value)=>value.HasValue&&double.IsFinite(value.Value)?value:null;
    public IReadOnlyList<TimelinePoint> Points(string key,DateTime now,int seconds)=>samples.TryGetValue(key,out var values)?values.Where(s=>s.Time>=now.AddSeconds(-seconds)&&s.Time<=now).ToArray():Array.Empty<TimelinePoint>();
    public void Clear(){samples.Clear();}
    public static string Cell(string value){if(value.Length>0&&"=+-@".Contains(value[0]))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
    public void ExportCsv(string path)
    {
        using var writer=new StreamWriter(path,false,new UTF8Encoding(true));writer.WriteLine("time_utc;component;metric;value;unit");
        foreach(var metric in Latest)if(samples.TryGetValue(metric.Key,out var values))foreach(var point in values){void Line(string name,string unit,double? value){if(value.HasValue)writer.WriteLine(string.Join(";",point.Time.ToString("O"),Cell(metric.Title),Cell(name),value.Value.ToString("R",CultureInfo.InvariantCulture),Cell(unit)));}Line(metric.PrimaryName,metric.PrimaryUnit,point.Primary);Line(metric.SecondaryName,metric.SecondaryUnit,point.Secondary);}
    }
}
