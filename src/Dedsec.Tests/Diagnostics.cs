using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;
namespace Dedsec.Tests;
public static class Diagnostics
{
    private const string WindowsHelp="https://support.microsoft.com/windows";
    private sealed record Probe(bool Available,List<Dictionary<string,object>> Rows,string Error);
    private static Probe Read(string scope,string query)
    {
        try {using var search=new ManagementObjectSearcher(scope,query);search.Options.Timeout=TimeSpan.FromSeconds(6);using var result=search.Get();var rows=new List<Dictionary<string,object>>();foreach(ManagementObject item in result)using(item){var row=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);foreach(PropertyData p in item.Properties)if(p.Value!=null)row[p.Name]=p.Value;rows.Add(row);}return new(true,rows,"");}
        catch(Exception ex){return new(false,new(),ex is UnauthorizedAccessException?"Нужны дополнительные права":ex.GetType().Name);}
    }
    private static Probe Read(string query)=>Read("root\\cimv2",query);
    public static List<Finding> Analyze(SystemInfo system)
    {
        var list=Inventory.Diagnose(system);
        void Add(string severity,string title,string evidence,string solution,string? link=null)=>list.Add(new(severity,title,evidence,solution,link));
        var os=Read("SELECT LastBootUpTime,FreePhysicalMemory,TotalVisibleMemorySize,FreeSpaceInPagingFiles,SizeStoredInPagingFiles FROM Win32_OperatingSystem");
        if(os.Available&&os.Rows.FirstOrDefault() is {} row)
        {
            try{DateTime boot=ManagementDateTimeConverter.ToDateTime(Inventory.Text(row,"LastBootUpTime"));var days=(DateTime.Now-boot).TotalDays;Add(days>14?"warning":"info","Время с загрузки Windows",$"Около {days:F1} суток с загрузки ядра. Быстрый запуск может сохранять эту дату.",days>14?"После установки драйверов и обновлений выполните именно «Перезагрузка». Длительное время работы само по себе не является неисправностью.":"Перед сравнительным прогоном дождитесь завершения обновлений и фоновых задач.");}catch{}
            long total=Inventory.Number(row,"TotalVisibleMemorySize"),free=Inventory.Number(row,"FreePhysicalMemory");if(total>0){double load=100*(1-free/(double)total);Add(load>88?"warning":"info","Давление на оперативную память",$"Используется {load:F0}% видимой RAM; доступно {free/1048576d:F1} ГБ в момент проверки.",load>88?"Откройте «Процессы», закройте ненужные программы и проверьте расход RAM. Если память постоянно заканчивается в обычной работе, полезно расширение RAM. Не используйте «очистители памяти».":"Это моментальный снимок. Кэш Windows использует свободную RAM и сам по себе не означает утечку.");}
            if(Inventory.Number(row,"SizeStoredInPagingFiles")==0)Add("warning","Файл подкачки не обнаружен","Win32_OperatingSystem сообщает нулевой размер файла подкачки.","Некоторые программы и дампы сбоев требуют файл подкачки. Обычно подходит «Автоматически выбирать объём». После изменения нужна перезагрузка.","ms-settings:about");
        }else Add("unknown","Память и время загрузки","Провайдер состояния ОС недоступен: "+os.Error,"Проверьте WMI и права доступа. Не считайте эту проверку успешно пройденной.");
        var modules=Read("SELECT Capacity,ConfiguredClockSpeed,Speed FROM Win32_PhysicalMemory");
        if(modules.Available&&modules.Rows.Count>0){string detail=string.Join("; ",modules.Rows.Select(m=>$"{Inventory.Number(m,"Capacity")/1073741824d:F0} ГБ, {Inventory.Number(m,"ConfiguredClockSpeed")} MT/s (WMI)"));Add("info","Модули оперативной памяти",detail,"Разные объёмы или частоты не доказывают проблему. Доступные каналы и XMP/EXPO зависят от платы и CPU. Не включайте разгон без проверки поддержки и стабильности.");}
        var battery=Read("SELECT BatteryStatus,EstimatedChargeRemaining FROM Win32_Battery");
        if(battery.Available&&battery.Rows.Count>0){var b=battery.Rows[0];long status=Inventory.Number(b,"BatteryStatus"),charge=Inventory.Number(b,"EstimatedChargeRemaining");Add(status==1||charge<20?"warning":"info","Питание ноутбука",$"Заряд {charge}%; BatteryStatus={status}.",status==1?"Перед бенчмарком подключите штатный блок питания. На батарее ноутбук часто ограничивает мощность; такой результат не доказывает неисправность CPU/GPU.":"Для воспроизводимого теста используйте питание от сети и одинаковый режим питания.");}
        var design=Read("root\\wmi","SELECT DesignedCapacity FROM BatteryStaticData");var full=Read("root\\wmi","SELECT FullChargedCapacity FROM BatteryFullChargedCapacity");
        if(design.Available&&full.Available&&design.Rows.Count>0&&full.Rows.Count>0&&Inventory.Number(design.Rows[0],"DesignedCapacity")>0){double health=100*Inventory.Number(full.Rows[0],"FullChargedCapacity")/(double)Inventory.Number(design.Rows[0],"DesignedCapacity");Add(health<60?"warning":"info","Оценка износа батареи",$"Полная ёмкость составляет около {health:F0}% проектной (данные контроллера, первый аккумулятор).","Это оценка контроллера, а не лабораторный замер. При внезапных выключениях проверьте батарею и адаптер в сервисе; вздутую батарею не нагружайте.");}
        var reliability=Read("root\\Microsoft\\Windows\\Storage","SELECT Temperature,Wear,PowerOnHours,ReadErrorsUncorrected,WriteErrorsUncorrected FROM MSFT_StorageReliabilityCounter");
        if(!reliability.Available||reliability.Rows.Count==0)Add("unknown","Расширенные счётчики накопителей","Счётчики Storage Reliability недоступны для текущих драйверов/прав.","Для NVMe, RAID и USB могут потребоваться утилиты производителя. Отсутствующие показатели не интерпретируются как нули.");
        else foreach(var d in reliability.Rows){long temp=Inventory.Number(d,"Temperature"),wear=Inventory.Number(d,"Wear"),errors=Inventory.Number(d,"ReadErrorsUncorrected")+Inventory.Number(d,"WriteErrorsUncorrected");Add(errors>0?"critical":temp>=65||wear>=90?"warning":"info","Состояние накопителя по Windows",$"Температура: {(d.ContainsKey("Temperature")?temp+" °C":"н/д")}; износ: {(d.ContainsKey("Wear")?wear+"%":"н/д")}; наработка: {(d.ContainsKey("PowerOnHours")?Inventory.Number(d,"PowerOnHours")+" ч":"н/д")}; неисправленные ошибки: {(d.ContainsKey("ReadErrorsUncorrected")&&d.ContainsKey("WriteErrorsUncorrected")?errors.ToString():"н/д")}.",errors>0?"Сначала сохраните данные. Счётчик может включать исторические события; проверьте SMART и диагностику производителя до нагрузок.":"Порог температуры и значение Wear зависят от производителя. Проверьте охлаждение и официальную спецификацию; эти данные не гарантируют исправность.");}
        var quickfix=Read("SELECT InstalledOn FROM Win32_QuickFixEngineering");var dates=new List<DateTime>();foreach(var q in quickfix.Rows){string t=Inventory.Text(q,"InstalledOn","");if(DateTime.TryParse(t,CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)||DateTime.TryParse(t,out d))dates.Add(d);}
        if(dates.Count>0){double age=(DateTime.Now-dates.Max()).TotalDays;Add(age>60?"warning":"info","История системных обновлений",$"Последняя дата QFE: {dates.Max():dd.MM.yyyy}, около {age:F0} дней назад. QFE не отражает все виды обновлений.","Проверьте фактический статус в Центре обновления Windows. Дата QFE не доказывает наличие всех обновлений и не определяет срок поддержки редакции.","ms-settings:windowsupdate");}
        var gpus=Read("SELECT Name,DriverVersion,DriverDate,Status FROM Win32_VideoController");foreach(var g in gpus.Rows)Add("info","Драйвер видеокарты",$"{Inventory.Text(g,"Name")}: версия {Inventory.Text(g,"DriverVersion")}; Status={Inventory.Text(g,"Status")}.","При сбоях или сбросах драйвера сверяйте версию с сайтом производителя ноутбука/GPU. Новейшая версия не всегда обязательна; избегайте автоматических пакетов драйверов.");
        var network=Read("SELECT NetConnectionStatus FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True");
        if(network.Available)Add(network.Rows.Any(r=>Inventory.Number(r,"NetConnectionStatus")==2)?"info":"warning","Сетевое подключение",network.Rows.Any(r=>Inventory.Number(r,"NetConnectionStatus")==2)?"Есть физический адаптер со статусом Connected.":"WMI не видит подключённого физического адаптера. Виртуальные адаптеры и VPN этим запросом не оцениваются.","Соединение с сетью не гарантирует доступ в интернет. Если сайты не открываются, проверьте дату, DNS, шлюз и настройки прокси в Windows.","ms-settings:network-status");
        foreach(string profile in new[]{"DomainProfile","StandardProfile","PublicProfile"})
        {
            using var key=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\"+profile);if(key?.GetValue("EnableFirewall") is int enabled&&enabled==0)Add("warning","Профиль брандмауэра отключён",profile+": EnableFirewall=0 в локальной конфигурации. Политики организации могут переопределять значение.","Проверьте активный профиль в «Безопасность Windows». Для нужных приложений создавайте узкие исключения; не отключайте защиту целиком.","windowsdefender:");
        }
        list.AddRange(ReadEvents());return list;
    }
    public static List<Finding> Live(List<SensorRow> sensors,List<ProcessRow> processes,int limit)
    {
        var list=new List<Finding>();foreach(var s in sensors.Where(s=>s.Type=="Temperature"&&s.Value>=limit-8).OrderByDescending(s=>s.Value).Take(6))list.Add(new(s.Value>=limit?"critical":"warning","Высокая температура",$"{s.Device} / {s.Name}: {s.Value:F0} °C (порог нагрузки {limit} °C).","Остановите нагрузку, проверьте вентиляцию и обороты вентиляторов. Рабочие пределы зависят от модели. Перед чисткой выключите и обесточьте ПК; обслуживание термоинтерфейса лучше поручить специалисту."));
        foreach(var s in sensors.Where(s=>s.Type=="Load"&&s.Value>=95).Take(4))list.Add(new("warning","Ресурс почти полностью занят",$"{s.Device} / {s.Name}: {s.Value:F0}% в момент измерения.","Во время бенчмарка высокая нагрузка ожидаема. В обычной работе посмотрите процессы и характер задачи. По одному проценту нельзя определить постоянное «узкое место» или неисправность."));
        var mem=Inventory.Memory();if(mem.Load>=88)list.Add(new("warning","Не хватает свободной RAM",$"Занято {mem.Load}%, доступно {mem.Available/1048576d:F0} MiB.","Проверьте процессы с большим расходом памяти. Сократите фоновые приложения; если проблема повторяется в обычной работе, рассмотрите расширение RAM."));
        foreach(var p in processes.Where(p=>p.Cpu>=25).Take(3))list.Add(new("info","Процесс создаёт заметную нагрузку",$"{p.Name}: CPU {p.Cpu:F1}% всего процессора, RAM {p.MemoryMb:F0} MiB.","Проверьте, ожидаема ли такая работа программы. Не завершайте незнакомые системные процессы; сначала сохраните данные в приложении."));
        if(!sensors.Any(s=>s.Type=="Temperature"))list.Add(new("unknown","Нет температурного контроля","Температурные датчики сейчас недоступны.","Нагрузочный тест не сможет автоматически остановиться по температуре. Используйте встроенную подготовку датчиков в главном экране. Для части моделей потребуется запрос Windows на повышение прав."));return list;
    }
    public static List<Finding> ReadEvents()
    {
        var findings=new List<Finding>();foreach(string channel in new[]{"System","Application"})
        {
            int read=0;var groups=new Dictionary<string,(int Count,int Id,string Provider,DateTime? Time,string Message)>();
            try
            {
                var query=new EventLogQuery(channel,PathType.LogName,"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 604800000]]]"){ReverseDirection=true};using var reader=new EventLogReader(query);var clock=Stopwatch.StartNew();
                while(read<1200&&clock.Elapsed.TotalSeconds<8)
                {
                    using var record=reader.ReadEvent();if(record==null)break;read++;int id=record.Id;string provider=record.ProviderName??"Unknown";string key=provider+":"+id;
                    if(groups.TryGetValue(key,out var old)){groups[key]=old with{Count=old.Count+1};continue;}
                    string message;try{message=record.FormatDescription()??"Описание отсутствует";}catch{message="Ресурс описания события недоступен; откройте событие в Просмотре событий.";}
                    groups[key]=(1,id,provider,record.TimeCreated,message[..Math.Min(message.Length,1800)]);
                }
                findings.Add(new("info",$"Журнал {channel}: проверен интервал 7 дней",$"Прочитано {read} записей уровней Critical/Error/Warning; лимит 1200 записей или 8 секунд. Одинаковые provider + Event ID сгруппированы.","Это ограниченная выборка, а не полная история. События могут описывать уже устранённые сбои. Локальные сообщения не отправляются на сайт."));
                foreach(var item in groups.Values.OrderByDescending(g=>Explain(g.Provider,g.Id).Severity switch{"critical"=>3,"warning"=>2,_=>1}).ThenByDescending(g=>g.Count).Take(24))
                {
                    var (severity,title,meaning,solution)=Explain(item.Provider,item.Id);
                    findings.Add(new(severity,$"{title} · Event {item.Id}",$"{channel} / {item.Provider}; записей в выборке: {item.Count}; последнее: {item.Time:dd.MM.yyyy HH:mm}.\n{meaning}\n\nСообщение Windows (локально):\n{item.Message}",solution,WindowsHelp));
                }
            }
            catch(Exception ex){findings.Add(new("unknown",$"Журнал {channel} недоступен",ex.GetType().Name,"Проверьте права на журнал, службу Windows Event Log или откройте Просмотр событий вручную. Недоступный журнал не означает отсутствие ошибок."));}
        }
        return findings;
    }
    private static (string Severity,string Title,string Meaning,string Solution) Explain(string source,int id)
    {
        string s=source.ToLowerInvariant();
        if(s.Contains("kernel-power")&&id==41 || s=="eventlog"&&id==6008)return("warning","Нештатное выключение","Windows зафиксировала запуск после некорректного завершения. Событие не определяет причину и само по себе не доказывает неисправность БП.","Сопоставьте время со сбоем. Проверьте BugCheck, WHEA и соседние события, питание и температуры. Учитывайте принудительное выключение кнопкой и пропадание сети питания.");
        if(s.Contains("whea"))return("warning","Аппаратное сообщение WHEA","Аппаратная подсистема сообщила исправленную или неисправленную ошибку. Точный компонент и тип ошибки указаны в самом событии.","Сохраните данные. Проверьте BIOS/драйверы производителя, температуры и питание. Верните разгон/undervolt к штатным значениям, если они применялись. Повторяющиеся ошибки требуют отдельной диагностики CPU/RAM/PCIe; одного Event ID недостаточно.");
        if(s=="disk"&&id==7)return("critical","Сообщение о повреждённом блоке","Драйвер диска сообщил bad block в момент события. Запись может быть исторической.","Сначала сделайте резервную копию. Не запускайте запись и стресс диска до проверки SMART, кабеля/корпуса USB и диагностики производителя. При повторении планируйте замену накопителя.");
        if((s.Contains("disk")||s.Contains("stor")||s.Contains("iastor")||s.Contains("nvme"))&&new[]{11,51,129,153,157}.Contains(id))return("warning","Сбой ввода-вывода накопителя","Возможны таймаут, повтор операции, сброс контроллера или отключение устройства. Причиной бывают питание, кабель, драйвер, корпус USB либо сам диск.","Сохраните важные данные, сопоставьте время и устройство в сообщении. Проверьте соединения, SMART и официальные драйверы. Не делайте вывод о неисправном диске только по одному событию.");
        if(s.Contains("ntfs")&&new[]{55,57,98,140}.Contains(id))return("warning","Проблема файловой системы","NTFS зарегистрировала проблему структуры/записи или необходимость проверки тома.","Начните с резервной копии. Проверьте состояние диска; для диагностики NTFS используйте chkdsk /scan из терминала администратора. Исправления и офлайн-проверку выполняйте после сохранения данных.");
        if(s=="display"&&id==4101||s.Contains("nvlddmkm")||s.Contains("amdwddmg"))return("warning","Сбой или перезапуск видеодрайвера","Видеоподсистема зарегистрировала ошибку; при 4101 Windows восстановила драйвер после таймаута.","Проверьте температуры GPU и питание, уберите нестабильный разгон, сверьте драйвер с производителем. Сопоставьте сбой с игрой или нагрузкой. Событие не доказывает поломку видеокарты.");
        if(id==1001&&(s.Contains("wer")||s.Contains("bugcheck")))return("warning","Отчёт о сбое или BSOD","Служба отчётов Windows записала сведения о сбое; код и дамп могут помочь найти причину.","Запишите код STOP и имя модуля из события. Для точной причины нужен анализ дампа в WinDbg; имя драйвера в стеке не всегда означает, что виноват именно он. Не загружайте дампы публично: в них может быть содержимое памяти.");
        if(s.Contains("application error")||s.Contains(".net runtime")||id==1002&&s.Contains("hang"))return("warning","Сбой приложения","Программа завершилась с ошибкой или перестала отвечать. Сообщение содержит имя приложения и иногда модуль/код исключения.","Сначала обновите или восстановите указанную программу, проверьте повторяемость и её журнал. Если сбои затрагивают много разных программ, проверьте RAM, системные файлы и накопитель. Не скачивайте отдельные DLL с посторонних сайтов.");
        if(s.Contains("service control manager"))return("warning","Ошибка службы","Служба не запустилась, завершилась либо превысила таймаут. Имя и код ошибки приведены в сообщении.","Проверьте, нужна ли служба и работает ли связанная программа. Откройте services.msc, изучите зависимости и код ошибки. Не включайте и не отключайте все службы подряд.");
        if(s.Contains("resource-exhaustion"))return("warning","Windows сообщала о нехватке ресурсов","Система фиксировала истощение памяти/commit. Большой объём занятой RAM не всегда единственная причина.","Проверьте файл подкачки, доступное место на системном диске и процессы с растущим Commit Size. При повторении найдите утечку в программе; простая очистка RAM причину не устраняет.");
        if(s.Contains("distributedcom")&&id==10016)return("info","Событие разрешений DCOM","Некоторые события DCOM 10016 возникают при штатной работе Windows. Само наличие записи не означает, что требуется ремонт.","Если нет связанного симптома, не меняйте владельцев ключей реестра и разрешения DCOM ради очистки журнала. Если симптом есть, исследуйте конкретный CLSID/APPID и документацию компонента.");
        if(s.Contains("dns")||s.Contains("dhcp"))return("warning","Сетевое предупреждение","В момент события были трудности DNS/DHCP или сетевого разрешения. Ошибка может быть кратковременной.","Сопоставьте время с потерей сети. Проверьте подключение, настройки DNS/прокси и роутер. Не сбрасывайте всю сеть без необходимости, особенно если используете VPN или рабочую конфигурацию.");
        return("info","Событие требует контекста","Найдено предупреждение или ошибка Windows. Для этой пары источника и Event ID нет специального правила; причина не определяется автоматически.","Сопоставьте время и сообщение с реальным симптомом. Найдите документацию именно для указанного источника и Event ID; одинаковые номера у разных источников означают разное. Не применяйте случайные «исправления реестра».");
    }
}
