using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Dedsec.Tests;
public static class Json { public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }; }
public sealed record DiskInfo(string Model, string Media, long CapacityGb);
public sealed record SystemInfo(string Cpu, int LogicalCores, string Gpu, int RamGb, string Windows, string Architecture, List<DiskInfo> Disks);
public sealed record Measurement(string Metric, double Value, string Unit, string Status, string Detail, double Seconds);
public sealed record Finding(string Severity, string Title, string Evidence, string Solution, string? Link = null);
public sealed class Report
{
    public string SuiteVersion { get; set; } = "dedsec-v1";
    public string AppVersion { get; set; } = typeof(Report).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    public string Profile { get; set; } = "standard";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public SystemInfo? System { get; set; }
    public List<Measurement> Measurements { get; set; } = new();
    public List<Finding> Findings { get; set; } = new();
    public string? ResultUrl { get; set; }
    public string? DeleteToken { get; set; }
    public bool Completed { get; set; }
    public DateTime? PublicationAttemptUtc { get; set; }
    public string? PublicationError { get; set; }
    public object PublicPayload() => new { suite_version = SuiteVersion, app_version = AppVersion, profile = Profile, system = System, measurements = Measurements.ConvertAll(m=>m.Status=="ok"?m:m with { Detail="Unsupported or unavailable; see local report" }), consent = true, consent_version = "2026-11-23" };
}
public sealed class Settings
{
    public bool Agreement { get; set; }
    public string AgreementVersion { get; set; } = "";
    public bool Tray { get; set; }
    public bool Animations { get; set; } = true;
    public string Accent { get; set; } = "#C1FA55";
    public string WindowColor { get; set; } = "#0B0E12";
    public int TemperatureLimit { get; set; } = 90;
    public string DiskRoot { get; set; } = Path.GetPathRoot(Environment.SystemDirectory)!;
    public bool IncludeGpu { get; set; } = true;
    public bool IncludeDisk { get; set; } = true;
    public bool IncludeMemory { get; set; } = true;
    public int ChartSeconds { get; set; } = 60;
    public int StressSeconds { get; set; } = 60;
    public static string DataDir => Environment.GetCommandLineArgs().Contains("--capture") ? Path.Combine(AppContext.BaseDirectory,"capture-local") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DedsecTests");
    public static Settings Load() { try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(DataDir, "settings.json")), Json.Options) ?? new(); } catch { return new(); } }
    public void Save() { Directory.CreateDirectory(DataDir); File.WriteAllText(Path.Combine(DataDir, "settings.json"), JsonSerializer.Serialize(this, Json.Options)); }
}
