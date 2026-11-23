using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LibreHardwareMonitor.Hardware;
namespace Dedsec.Tests;
public sealed record SensorRow(string Device, string Name, string Type, float Value, string Unit);
public sealed record ProcessRow(string Name, int Pid, double Cpu, double MemoryMb);
public sealed class Telemetry : IDisposable
{
    private readonly Computer computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = false, IsControllerEnabled = false };
    private bool opened;
    public string Status { get; private set; } = "Датчики инициализируются";
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Time)> previous = new();
    public List<SensorRow> Read()
    {
        try { if (!opened) { computer.Open(); opened = true; } var list = new List<SensorRow>(); foreach (var h in computer.Hardware) Visit(h,list); Status = list.Any(s=>s.Type=="Temperature") ? "Датчики доступны" : "Температуры недоступны: могут требоваться права администратора и PawnIO"; return list; }
        catch (Exception e) { Status = "Датчики недоступны: " + e.Message; return new(); }
    }
    private static void Visit(IHardware h, List<SensorRow> list)
    {
        h.Update(); foreach (var s in h.Sensors) if (s.Value is float value && float.IsFinite(value)) list.Add(new(h.Name,s.Name,s.SensorType.ToString(),value,s.SensorType switch { SensorType.Temperature => "°C", SensorType.Load => "%", SensorType.Clock => "MHz", SensorType.Power => "W", SensorType.Fan => "RPM", SensorType.Data => "GB", SensorType.SmallData => "MB", _ => "" })); foreach (var child in h.SubHardware) Visit(child,list);
    }
    public List<ProcessRow> Processes()
    {
        var rows = new List<ProcessRow>(); var now = DateTime.UtcNow; var alive = new HashSet<int>();
        foreach (var p in Process.GetProcesses()) using (p) { try { int id=p.Id; var cpu=p.TotalProcessorTime; double load = previous.TryGetValue(id,out var old) ? Math.Max(0, Math.Min(100,(cpu-old.Cpu).TotalMilliseconds / Math.Max(1,(now-old.Time).TotalMilliseconds) / Environment.ProcessorCount * 100)) : 0; previous[id]=(cpu,now); alive.Add(id); rows.Add(new(p.ProcessName,id,Math.Round(load,1),Math.Round(p.WorkingSet64 / 1048576d,1))); } catch { } }
        foreach (var id in previous.Keys.Where(id=>!alive.Contains(id)).ToArray()) previous.Remove(id);
        return rows.OrderByDescending(p=>p.Cpu).ThenByDescending(p=>p.MemoryMb).Take(80).ToList();
    }
    public void Dispose() { try { computer.Close(); } catch { } }
}
