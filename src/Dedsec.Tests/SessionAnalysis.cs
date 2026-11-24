using System;
using System.Collections.Generic;
using System.Linq;
namespace Dedsec.Tests;
public static class SessionAnalysis
{
    public static List<Finding> Read(TelemetryHistory history,DateTime now,int temperatureLimit)
    {
        var result=new List<Finding>();foreach(string key in new[]{"cpu","gpu","ram"})
        {
            var points=history.Points(key,now,60);var valid=points.Where(p=>p.Primary.HasValue).ToArray();string title=key=="cpu"?"CPU":key=="gpu"?"GPU":"RAM";
            if(valid.Length<10||(valid[^1].Time-valid[0].Time).TotalSeconds<18){result.Add(new("unknown","Длительная загрузка · "+title,"Недостаточно измерений за последнюю минуту (нужно ≥10 и интервал ≥18 с).","Оставьте мониторинг работать и выполните обычную задачу. Короткий всплеск не объявляется узким местом."));continue;}
            double percent=100*valid.Count(p=>p.Primary>=95)/(double)valid.Length;result.Add(new(percent>=60?"warning":"info","Длительная загрузка · "+title,$"{valid.Length} измерений за {(valid[^1].Time-valid[0].Time).TotalSeconds:F0} с; средняя {valid.Average(p=>p.Primary):F0}%; пик {valid.Max(p=>p.Primary):F0}%; ≥95% в {percent:F0}% измерений.",percent>=60?$"{title} долго занят почти полностью. Во время бенчмарка это ожидаемо. В обычной задаче сопоставьте с задержками и процессами; только по этому признаку нельзя рекомендовать замену компонента.":"В этом коротком интервале нет устойчивого насыщения. Это не гарантирует отсутствие ограничений в другой задаче."));
            if(key=="ram")continue;var temp=points.Where(p=>p.Secondary.HasValue).ToArray();if(temp.Length>=10){double hot=100*temp.Count(p=>p.Secondary>=temperatureLimit-8)/(double)temp.Length;if(hot>=60)result.Add(new("warning","Длительный нагрев · "+title,$"Температура ≥{temperatureLimit-8} °C в {hot:F0}% из {temp.Length} измерений; пик {temp.Max(p=>p.Secondary):F0} °C.","Снизьте нагрузку и проверьте охлаждение. Это наблюдение нагрева, а не доказанный thermal throttling. Рабочие пределы зависят от модели."));}
        }return result;
    }
}
