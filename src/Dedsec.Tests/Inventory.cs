using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace Dedsec.Tests;
public static class Inventory
{
    public static List<Dictionary<string, object>> Query(string scope, string query)
    {
        var list = new List<Dictionary<string, object>>();
        try { using var search = new ManagementObjectSearcher(scope, query); search.Options.Timeout = TimeSpan.FromSeconds(8); using var rows = search.Get(); foreach (ManagementObject row in rows) { using (row) { var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); foreach (PropertyData p in row.Properties) if (p.Value != null) d[p.Name] = p.Value; list.Add(d); } } } catch { }
        return list;
    }
    public static List<Dictionary<string, object>> Query(string query) => Query("root\\cimv2", query);
    public static string Text(Dictionary<string, object>? row, string key, string fallback = "Не определено") => row != null && row.TryGetValue(key, out var v) ? Convert.ToString(v)?.Trim() ?? fallback : fallback;
    public static long Number(Dictionary<string, object>? row, string key) { try { return Convert.ToInt64(row?.GetValueOrDefault(key) ?? 0); } catch { return 0; } }
    public static SystemInfo Read()
    {
        var cpu = Query("SELECT Name FROM Win32_Processor").FirstOrDefault();
        var gpu = Query("SELECT Name FROM Win32_VideoController");
        var os = Query("SELECT Caption,Version,BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        var disks = Query("SELECT Model,Size,MediaType FROM Win32_DiskDrive").Select(x => new DiskInfo(Text(x,"Model"), Text(x,"MediaType"), (long)Math.Round(Number(x,"Size") / 1e9))).ToList();
        // Storage namespace exposes bus/media without collecting serials, device IDs or volume names.
        var physical = Query("root\\Microsoft\\Windows\\Storage", "SELECT FriendlyName,MediaType,Size FROM MSFT_PhysicalDisk");
        // FriendlyName may be user-assigned. Publish the hardware Model, never a friendly label.
        disks=disks.Select(d=>{var matches=physical.Where(p=>Text(p,"FriendlyName").Equals(d.Model,StringComparison.OrdinalIgnoreCase)&&Math.Abs(Number(p,"Size")/1e9-d.CapacityGb)<2).ToList();return matches.Count==1?d with {Media=Number(matches[0],"MediaType") switch{3=>"HDD",4=>"SSD",5=>"SCM",_=>"Unknown"}}:d;}).ToList();
        return new(Text(cpu,"Name"), Environment.ProcessorCount, string.Join(" / ", gpu.Select(x => Text(x,"Name"))), (int)Math.Round(Memory().Total / 1073741824d), $"{Text(os,"Caption")} · {Text(os,"Version")} (build {Text(os,"BuildNumber")})", RuntimeInformation.OSArchitecture.ToString(), disks.Take(16).ToList());
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong Total, Available, PageTotal, PageAvailable, VirtualTotal, VirtualAvailable, Extended; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public static (ulong Total, ulong Available, uint Load) Memory() { var s = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() }; if (!GlobalMemoryStatusEx(ref s)) return (0,0,0); return (s.Total,s.Available,s.Load); }
    public static List<Finding> Diagnose(SystemInfo info)
    {
        var findings = new List<Finding>();
        if (info.RamGb < 8) findings.Add(new("warning", "Мало оперативной памяти", $"Установлено около {info.RamGb} ГБ RAM.", "Оставьте файл подкачки под управлением Windows, сократите автозагрузку. Для браузера и современных программ полезно 8–16 ГБ. Не отключайте системные службы по случайным спискам."));
        else findings.Add(new("info", "Профиль памяти", $"Установлено около {info.RamGb} ГБ RAM.", "Оставьте файл подкачки под управлением Windows. При нехватке памяти сначала найдите приложения с большим потреблением на вкладке «Процессы»."));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        { try { var free = drive.AvailableFreeSpace / (double)drive.TotalSize; if (free < .15) findings.Add(new("warning", "Мало свободного места", $"Том {drive.Name}: свободно {free:P0}.", "Откройте Параметры → Система → Память и удалите ненужные временные файлы. Перед очисткой проверьте выбранные категории.")); } catch { } }
        var defenders = Query("root\\Microsoft\\Windows\\Defender", "SELECT AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureAge FROM MSFT_MpComputerStatus").FirstOrDefault();
        if (defenders == null) findings.Add(new("unknown", "Состояние антивируса не получено", "Защитник может быть заменён другим антивирусом или WMI недоступен.", "Проверьте раздел «Безопасность Windows». Отсутствие ответа не означает отсутствие защиты.", "windowsdefender:"));
        else if (Text(defenders,"RealTimeProtectionEnabled") == "False") findings.Add(new("warning", "Защита в реальном времени отключена", "Защитник сообщает RealTimeProtectionEnabled=False.", "Проверьте, установлен ли другой антивирус. Если нет — включите защиту в разделе «Безопасность Windows».", "windowsdefender:"));
        else findings.Add(new("ok", "Защитник: защита включена", $"Возраст сигнатур: {Number(defenders,"AntivirusSignatureAge")} дн.", "Регулярно обновляйте сигнатуры и Windows. Эта проверка не заменяет сканирование на вредоносное ПО.", "windowsdefender:"));
        var pending = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        using (pending) if (pending != null) findings.Add(new("warning", "Требуется перезагрузка", "Windows Component Based Servicing отмечает ожидающую перезагрузку.", "Сохраните работу и перезагрузите компьютер перед сравнительным тестом."));
        var errors = Query("SELECT Name,ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
        foreach (var e in errors.Take(12)) findings.Add(new("warning", "Устройство с ошибкой", $"{Text(e,"Name")}: код {Number(e,"ConfigManagerErrorCode")}.", "Проверьте устройство в Диспетчере устройств. Устанавливайте драйвер с сайта производителя компьютера или компонента.", "https://support.microsoft.com/windows"));
        if (errors.Count == 0) findings.Add(new("info", "Устройства PnP", "Запрос WMI не вернул устройств с ненулевым кодом ошибки; недоступный провайдер тоже может дать пустой ответ.", "При симптомах проверьте Диспетчер устройств вручную."));
        var startup = Query("SELECT Caption FROM Win32_StartupCommand");
        findings.Add(new(startup.Count > 12 ? "warning" : "info", "Автозагрузка", $"WMI нашёл {startup.Count} записей автозагрузки.", "В Диспетчере задач отключайте только знакомые ненужные приложения. Драйверы, защиту и резервное копирование сохраняйте.", "ms-settings:startupapps"));
        var failure = Query("root\\wmi", "SELECT PredictFailure FROM MSStorageDriver_FailurePredictStatus");
        if (failure.Any(x => Text(x,"PredictFailure") == "True")) findings.Add(new("critical", "SMART прогнозирует отказ накопителя", "Драйвер сообщил PredictFailure=True.", "Сначала скопируйте важные данные. Нагрузочный тест диска выполнять не следует; проверьте накопитель утилитой производителя."));
        else findings.Add(new("unknown", "Ограниченная проверка SMART", failure.Count == 0 ? "Драйвер не предоставил ATA SMART. Для NVMe/USB это возможно." : "Прогноз отказа ATA SMART не сработал; полный журнал SMART не читался.", "Это не гарантия исправности. Для полной проверки используйте официальную утилиту накопителя и резервные копии."));
        findings.Add(new("info", "Рекомендации для этой конфигурации", $"CPU: {info.LogicalCores} потоков; RAM: {info.RamGb} ГБ; накопители: {string.Join(", ", info.Disks.Select(d=>d.Media).Distinct())}.", info.Disks.Any(d=>d.Media=="HDD") ? "Для отзывчивости системы приоритетнее SSD под Windows. HDD оставьте для больших файлов. Используйте штатную «Оптимизацию дисков» — она учитывает тип носителя." : "Начните со сбалансированной схемы питания и актуальных драйверов производителя. Сравните повторный тест при питании от сети. Высокая производительность может повысить шум и нагрев."));
        return findings;
    }
}
