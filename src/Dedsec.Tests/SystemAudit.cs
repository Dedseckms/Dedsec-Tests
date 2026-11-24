using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace Dedsec.Tests;
public static partial class Diagnostics
{
    [StructLayout(LayoutKind.Sequential)]private struct PerformanceInfo
    {public uint Size;public UIntPtr CommitTotal,CommitLimit,CommitPeak,PhysicalTotal,PhysicalAvailable,SystemCache,KernelTotal,KernelPaged,KernelNonpaged,PageSize;public uint Handles,Processes,Threads;}
    [DllImport("psapi.dll",SetLastError=true)]private static extern bool GetPerformanceInfo(ref PerformanceInfo info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetFirmwareType(out uint type);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kib);
    public static Finding CommitFinding(double used,double limit,double peak)
    {
        if(!double.IsFinite(used)||!double.IsFinite(limit)||limit<=0||used<0||used>limit)return new("unknown","Предел выделенной памяти","Корректный Commit Limit не получен.","Проверку нельзя считать пройденной. Откройте Диспетчер задач → Память → Выделено.");
        double percent=100*used/limit;return new(percent>=90?"warning":"info","Предел выделенной памяти",$"Выделено {used/1073741824:F2} из {limit/1073741824:F2} GiB ({percent:F0}%). Пик с загрузки: {peak/1073741824:F2} GiB.",percent>=90?"Приложения приближаются к текущему Commit Limit и могут получать ошибки выделения памяти. Найдите потребителя, проверьте свободное место и управление файлом подкачки. Это отличается от процента занятой физической RAM.":"Commit — обязательства Windows по памяти, а не объём занятой RAM. Лимит может вырасти при расширении файла подкачки; отключать его ради ускорения не следует.");
    }
    public static Finding VolumeFinding(string drive,string filesystem,bool? dirty)
    =>new(dirty==true?"warning":dirty==false?"info":"unknown","Файловая система · "+drive,$"{filesystem}; признак Dirty: {(dirty==true?"установлен":dirty==false?"не установлен":"не получен")}.",dirty==true?"Том помечен как требующий проверки. Сначала сохраните данные. Проверьте системные события диска и выполните штатную проверку файловой системы. Не запускайте исправление и запись вслепую.":"Отсутствие Dirty-флага не заменяет сканирование поверхности, SMART и резервную копию. Программа только читает признак и не изменяет файловую систему.");
    public static Finding DiskHealthFinding(string name,int? health,string bus)
    {string state=health switch{0=>"Healthy",1=>"Warning",2=>"Unhealthy",5=>"Unknown",_=>"Не получен"};return new(health==2?"critical":health==1?"warning":health==0?"info":"unknown","Состояние физического диска · "+name,$"Windows Storage: {state}; интерфейс {bus}.",health is 1 or 2?"Сначала создайте резервную копию. Windows сообщает ухудшение состояния. Сопоставьте со SMART, событиями ввода-вывода и официальной диагностикой накопителя до нагрузок.":"Это статус провайдера Windows, а не полная проверка NAND/поверхности. RAID, USB-мост и драйвер могут скрывать показатели. Unknown не означает исправность.");}
    private static List<Finding> Extended()
    {
        var list=new List<Finding>();void Add(string severity,string title,string evidence,string next)=>list.Add(new(severity,title,evidence,next));
        var perf=new PerformanceInfo{Size=(uint)Marshal.SizeOf<PerformanceInfo>()};
        if(GetPerformanceInfo(ref perf,perf.Size)){double page=perf.PageSize.ToUInt64();list.Add(CommitFinding(perf.CommitTotal.ToUInt64()*page,perf.CommitLimit.ToUInt64()*page,perf.CommitPeak.ToUInt64()*page));}
        else list.Add(CommitFinding(0,0,0));
        var memory=Inventory.Memory();if(GetPhysicallyInstalledSystemMemory(out ulong kib)&&memory.Total>0&&kib*1024>=memory.Total)
        {double reserved=kib*1024-memory.Total;bool large=reserved>=1073741824d&&reserved/(kib*1024)>=.15;Add(large?"warning":"info","Установленная и доступная Windows RAM",$"Физически установлено {kib/1048576d:F2} GiB; Windows видит {memory.Total/1073741824d:F2} GiB; разница {reserved/1073741824:F2} GiB.",large?"Заметная часть RAM недоступна ОС. Возможны резерв iGPU, ограничения редакции/разрядности, BIOS или установки модулей. Не считайте всю разницу неисправной памятью; проверьте аппаратно зарезервировано в Диспетчере задач и документацию платы.":"Часть памяти резервируется прошивкой и устройствами, особенно встроенной графикой. Это не тест целостности RAM.");}
        var pages=Read("SELECT AllocatedBaseSize,CurrentUsage,PeakUsage FROM Win32_PageFileUsage");
        if(pages.Available&&pages.Rows.Count>0)Add("info","Использование файла подкачки",$"Выделено на дисках {pages.Rows.Sum(r=>Inventory.Number(r,"AllocatedBaseSize"))/1024d:F2} GiB; сейчас занято {pages.Rows.Sum(r=>Inventory.Number(r,"CurrentUsage"))/1024d:F2} GiB; сумма пиков {pages.Rows.Sum(r=>Inventory.Number(r,"PeakUsage"))/1024d:F2} GiB.","Наличие занятого файла подкачки само по себе не доказывает нехватку RAM или медленную работу. Оцените Commit Limit и устойчивую загрузку памяти во время своей задачи.");
        var volumes=Read("SELECT DriveLetter,FileSystem,DirtyBitSet FROM Win32_Volume WHERE DriveLetter IS NOT NULL AND DriveType=3");
        if(!volumes.Available||volumes.Rows.Count==0)Add("unknown","Файловые системы томов","Провайдер Win32_Volume не вернул проверяемые тома: "+volumes.Error,"Проверьте права и штатные свойства накопителей. Результат не подменяется состоянием «чисто».");
        foreach(var volume in volumes.Rows)list.Add(VolumeFinding(Inventory.Text(volume,"DriveLetter"),Inventory.Text(volume,"FileSystem"),volume.TryGetValue("DirtyBitSet",out var dirty)&&dirty is bool value?value:null));
        var physical=Read("root\\Microsoft\\Windows\\Storage","SELECT FriendlyName,HealthStatus,BusType FROM MSFT_PhysicalDisk");
        if(!physical.Available||physical.Rows.Count==0)Add("unknown","Состояние физических дисков","Windows Storage не предоставил HealthStatus: "+physical.Error,"Это не положительный результат SMART. Некоторые контроллеры требуют утилиту производителя.");
        foreach(var disk in physical.Rows){string bus=Inventory.Number(disk,"BusType") switch{7=>"USB",8=>"RAID",11=>"SATA",17=>"NVMe",_=>"код "+Inventory.Number(disk,"BusType")};list.Add(DiskHealthFinding(Inventory.Text(disk,"FriendlyName"),disk.ContainsKey("HealthStatus")?(int)Inventory.Number(disk,"HealthStatus"):null,bus));}
        uint firmware=0;bool gotFirmware=GetFirmwareType(out firmware);Add(gotFirmware&&firmware is 1 or 2?"info":"unknown","Режим загрузки прошивки",gotFirmware?firmware switch{1=>"Windows загружена в режиме Legacy BIOS.",2=>"Windows загружена в режиме UEFI.",_=>"Тип прошивки не определён."}:"GetFirmwareType не вернул режим.","Режим загрузки сам по себе не является оценкой производительности. Не переключайте Legacy/UEFI без проверки разметки диска и подготовки загрузчика.");
        try{using var secure=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");var enabled=secure?.GetValue("UEFISecureBootEnabled");Add(enabled is int?"info":"unknown","Secure Boot",enabled is int state?state==1?"Windows сообщает: Secure Boot включён.":"Windows сообщает: Secure Boot выключен.":"Состояние Secure Boot не получено.","Не включайте настройку вслепую: проверьте поддержку прошивки, загрузчика и шифрования. Выключенное состояние не доказывает поломку или заражение.");}catch{Add("unknown","Secure Boot","Нет доступа к состоянию.","Проверьте сведения о системе Windows.");}
        var tpm=Read("root\\cimv2\\Security\\MicrosoftTpm","SELECT SpecVersion,IsEnabled_InitialValue,IsActivated_InitialValue FROM Win32_Tpm");
        if(tpm.Available&&tpm.Rows.Count>0){var t=tpm.Rows[0];Add("info","TPM",$"Спецификация {Inventory.Text(t,"SpecVersion")}; Enabled={Inventory.Text(t,"IsEnabled_InitialValue")}; Activated={Inventory.Text(t,"IsActivated_InitialValue")} (снимок провайдера).","TPM используется защитными функциями Windows. Не очищайте его: это может затронуть ключи шифрования. Эта проверка не подтверждает полную совместимость CPU и ПК с Windows 11.");}
        else Add("unknown","TPM",tpm.Available?"Провайдер не перечислил TPM.":"Состояние недоступно: "+tpm.Error,"Возможны отсутствие модуля, выключенный fTPM/PTT или нехватка прав. Проверьте «Безопасность Windows» и документацию производителя; ключи не очищаются.");
        return list;
    }
    public static string Category(Finding f)
    {string t=f.Title.ToLowerInvariant();if(t.Contains("event ")||t.StartsWith("журнал"))return "События";if(t.Contains("памят")||t.Contains("ram")||t.Contains("подкач"))return "Память";if(t.Contains("диск")||t.Contains("накоп")||t.Contains("файловая")||t.Contains("smart")||t.Contains("места"))return "Накопители";if(t.Contains("температур")||t.Contains("нагрев")||t.Contains("нагруз")||t.Contains("ресурс"))return "Нагрузка";if(t.Contains("tpm")||t.Contains("boot")||t.Contains("прошив")||t.Contains("защит")||t.Contains("антивирус")||t.Contains("брандмауэр"))return "Защита";return "Windows и устройства";}
}
