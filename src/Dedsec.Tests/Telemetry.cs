using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LibreHardwareMonitor.Hardware;
namespace Dedsec.Tests;
public sealed record SensorRow(string Device, string Name, string Type, float Value, string Unit,string Group="",string Id="");
public sealed record ProcessRow(string Name, int Pid, double Cpu, double MemoryMb);
public sealed class Telemetry : IDisposable
{
    private readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = false };
    private bool opened;
    private readonly WindowsCounters windows=new();
    public string Status { get; private set; } = "Датчики инициализируются";
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Time)> previous = new();
    public List<SensorRow> Read()
    {
        var list=new List<SensorRow>();
        try { if (!opened) { computer.Open(); opened = true; } foreach (var h in computer.Hardware) {try{Visit(h,list);}catch{}} }
        catch {try{computer.Close();}catch{}opened=false;}
        try{windows.Read(list);}catch{}
        Status = list.Any(s=>s.Type=="Temperature") ? "Доступные датчики обновляются" : "Базовые счётчики Windows · аппаратные температуры пока недоступны";return list;
    }
    private static void Visit(IHardware h, List<SensorRow> list)
    {
        h.Update(); foreach (var s in h.Sensors) if (s.Value is float value && float.IsFinite(value)) list.Add(MapSensor(h.Name,s.Name,h.HardwareType,s.SensorType,s.Index,value) with{Id=s.Identifier.ToString()}); foreach (var child in h.SubHardware) Visit(child,list);
    }
    public static SensorRow MapSensor(string device,string name,HardwareType hardwareType,SensorType sensorType,int index,float value)
    {
        // LHM 0.9.6 StorageDevice exposes NVMe SMART warning/critical limits as
        // Temperature at indices 10/11. They are configuration, never readings.
        // Match provider indices, not display names (which can be renamed).
        bool limit=hardwareType==HardwareType.Storage&&sensorType==SensorType.Temperature&&(index==10||index==11);
        bool delta=hardwareType==HardwareType.Cpu&&sensorType==SensorType.Temperature&&name.Contains("Distance to TjMax",StringComparison.OrdinalIgnoreCase);
        return new(device,limit?(index==10?"Порог предупреждения NVMe":"Критический порог NVMe"):name,
            limit?"TemperatureLimit":delta?"TemperatureDelta":sensorType.ToString(),value,
            delta?"°C Δ":sensorType switch { SensorType.Temperature => "°C", SensorType.Load => "%", SensorType.Clock => "MHz", SensorType.Power => "W", SensorType.Fan => "RPM", SensorType.Data => "GB", SensorType.SmallData => "MB", _ => "" },hardwareType.ToString());
    }
    public List<ProcessRow> Processes()
    {
        var rows = new List<ProcessRow>(); var now = DateTime.UtcNow; var alive = new HashSet<int>();
        foreach (var p in Process.GetProcesses()) using (p) { try { int id=p.Id; var cpu=p.TotalProcessorTime; double load = previous.TryGetValue(id,out var old) ? Math.Max(0, Math.Min(100,(cpu-old.Cpu).TotalMilliseconds / Math.Max(1,(now-old.Time).TotalMilliseconds) / Environment.ProcessorCount * 100)) : 0; previous[id]=(cpu,now); alive.Add(id); rows.Add(new(p.ProcessName,id,Math.Round(load,1),Math.Round(p.WorkingSet64 / 1048576d,1))); } catch { } }
        foreach (var id in previous.Keys.Where(id=>!alive.Contains(id)).ToArray()) previous.Remove(id);
        return rows.OrderByDescending(p=>p.Cpu).ThenByDescending(p=>p.MemoryMb).Take(300).ToList();
    }
    public void Dispose() { try { computer.Close(); } catch { } windows.Dispose(); }
}
