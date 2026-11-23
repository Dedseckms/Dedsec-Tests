using System;
using System.Globalization;
namespace Dedsec.Tests;
public static class MeasurementDisplay
{
    public const string ScoreMethod = "DT v1: 1000 баллов за 1 GiB/s для CPU, RAM и диска; 1000 баллов за 1 Giter/s для GPU. Сравнивайте только одинаковые тесты и версии методики. Это пересчёт измеренной скорости, не оценка исправности или FPS.";
    public static string Name(string metric)=>metric switch {"cpu_single"=>"CPU · один поток","cpu_multi"=>"CPU · все потоки","gpu_compute"=>"GPU · compute","ram_copy"=>"RAM · копирование","ram_errors"=>"RAM · ошибки","disk_read"=>"Диск · чтение","disk_write"=>"Диск · запись","disk_latency"=>"Диск · задержка",_=>metric};
    public static double? Points(Measurement m)
    {
        if(m.Status!="ok"||!double.IsFinite(m.Value)||m.Value<=0||!double.IsFinite(m.Seconds)||m.Seconds<=0)return null;
        return m.Metric switch {"cpu_single" or "cpu_multi" or "ram_copy" or "disk_read" or "disk_write" when m.Unit=="MiB/s"=>m.Value*1000/1024,"gpu_compute" when m.Unit=="Miter/s"=>m.Value,_=>null};
    }
    public static string Format(Measurement m)
    {
        if(m.Status!="ok")return m.Status=="skipped"?"Пропущен":"Ошибка проверки";
        if(!double.IsFinite(m.Value)||m.Value<0)return "Недопустимое измерение";
        double value=m.Value;string unit=m.Unit;
        if(unit=="MiB/s"){if(value>=1048576){value/=1048576;unit="TiB/s";}else if(value>=1024){value/=1024;unit="GiB/s";}}
        else if(unit=="Miter/s"&&value>=1000){value/=1000;unit="Giter/s";}
        else if(unit=="errors")unit="ошибок";
        return value.ToString("N2",CultureInfo.GetCultureInfo("ru-RU"))+" "+unit;
    }
    public static string Headline(Measurement m)=>Points(m) is double p?p.ToString("N0",CultureInfo.GetCultureInfo("ru-RU"))+" DT · "+Format(m):Format(m);
}
