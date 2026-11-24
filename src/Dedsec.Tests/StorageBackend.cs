using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace Dedsec.Tests;
public sealed class StoragePartition
{
    public int Number{get;set;}public ulong Offset{get;set;}public ulong Size{get;set;}public string GptType{get;set;}="";public string Type{get;set;}="";public string Letter{get;set;}="";public string Filesystem{get;set;}="";public bool Boot{get;set;}public bool System{get;set;}public bool Protected{get;set;}
    public bool Efi=>GptType.Trim('{','}').Equals("c12a7328-f81f-11d2-ba4b-00a0c93ec93b",StringComparison.OrdinalIgnoreCase);
    public string Description=>$"#{Number}  {(Letter.Length>0?Letter+":":"Без буквы")} · {StorageBackend.Size(Size)} · {(Efi?"EFI":Filesystem.Length>0?Filesystem:Type)}";
    public string SizeText=>StorageBackend.Size(Size);
    public string Role=>Boot?"Windows":Efi?"EFI":System?"Системный":Protected?"Служебный":"Данные";
}
public sealed class StorageDisk
{
    public int Number{get;set;}public string Key{get;set;}="";public string LayoutKey{get;set;}="";public string Name{get;set;}="";public ulong Size{get;set;}public string Style{get;set;}="";public bool Boot{get;set;}public bool System{get;set;}public bool ReadOnly{get;set;}public bool Offline{get;set;}public bool Clustered{get;set;}public string Error{get;set;}="";public List<StoragePartition> Partitions{get;set;}=new();
    public string Description=>$"Диск {Number} · {Name} · {StorageBackend.Size(Size)} · {Style}";
}
public sealed class StorageSnapshot{public List<StorageDisk> Disks{get;set;}=new();public DateTime CapturedUtc{get;set;}}
public sealed record DiskGap(ulong Offset,ulong Size){public string Description=>$"Свободно {StorageBackend.Size(Size)} · смещение {StorageBackend.Size(Offset)}";}
public static class StorageBackend
{
    public const ulong MiB=1048576;
    public static string Size(ulong bytes)=>bytes>=1099511627776?$"{bytes/1099511627776d:0.##} TiB":bytes>=1073741824?$"{bytes/1073741824d:0.##} GiB":$"{bytes/1048576d:0.##} MiB";
    public static IReadOnlyList<DiskGap> Gaps(StorageDisk disk)
    {
        var gaps=new List<DiskGap>();if(disk.Style is not("GPT" or "MBR")||disk.Size<2*MiB)return gaps;ulong cursor=MiB,end=disk.Size-MiB;
        foreach(var part in disk.Partitions.OrderBy(p=>p.Offset)){if(part.Offset>cursor){ulong aligned=(cursor+MiB-1)/MiB*MiB,size=(Math.Min(part.Offset,end)>aligned?(Math.Min(part.Offset,end)-aligned)/MiB:0)*MiB;if(size>=MiB)gaps.Add(new(aligned,size));}if(part.Size>ulong.MaxValue-part.Offset)return Array.Empty<DiskGap>();cursor=Math.Max(cursor,part.Offset+part.Size);}
        ulong last=(cursor+MiB-1)/MiB*MiB;if(end>last&&(end-last)/MiB>0)gaps.Add(new(last,(end-last)/MiB*MiB));return gaps;
    }
    public static Dictionary<string,object?> Request(string action,StorageDisk? disk=null,StoragePartition? part=null)
    =>new(){["action"]=action,["operation_id"]=Guid.NewGuid().ToString("N"),["application_path"]=AppContext.BaseDirectory,["data_path"]=Settings.DataDir,["user_profile"]=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),["reserved_letters"]=DriveInfo.GetDrives().Select(d=>d.Name[..1]).ToArray(),["disk"]=disk?.Number,["disk_key"]=disk?.Key,["layout_key"]=disk?.LayoutKey,["partition"]=part?.Number,["expected_offset"]=part?.Offset,["expected_size"]=part?.Size};
    public static bool Writes(string action)=>action is "initialize" or "create" or "resize" or "format" or "delete" or "letter" or "efi_cleanup" or "boot_delete";
    public static async Task<JsonElement> Run(Dictionary<string,object?> request)
    {
        string action=(string)request["action"]!;if(!Guid.TryParseExact((string)request["operation_id"]!,"N",out _))throw new ArgumentException("Некорректный ID операции");bool elevate=action!="inventory";string dir=Path.Combine(Settings.DataDir,"disk-operations",(string)request["operation_id"]!);Directory.CreateDirectory(dir);
        string input=Path.Combine(dir,"request.json"),output=Path.Combine(dir,"response.json"),script=Path.Combine(dir,"StorageWorker.ps1");
        using(var resource=typeof(StorageBackend).Assembly.GetManifestResourceStream("StorageWorker.ps1")??throw new FileNotFoundException("Встроенный менеджер дисков отсутствует"))using(var reader=new StreamReader(resource,Encoding.UTF8))await File.WriteAllTextAsync(script,await reader.ReadToEndAsync(),new UTF8Encoding(true));
        // UTF-8 BOM is required for Windows PowerShell 5.1 on non-English systems.
        await File.WriteAllTextAsync(input,JsonSerializer.Serialize(request,Json.Options),new UTF8Encoding(true));
        using var lockedRequest=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read);using var lockedScript=new FileStream(script,FileMode.Open,FileAccess.Read,FileShare.Read);
        string system=Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Sysnative"):Environment.SystemDirectory;
        var start=new ProcessStartInfo(Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=elevate&&!SensorComponents.Elevated,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};if(start.UseShellExecute)start.Verb="runas";
        static string Quote(string value)=>"'"+value.Replace("'","''")+"'";
        string command="& ([ScriptBlock]::Create([IO.File]::ReadAllText("+Quote(script)+"))) -RequestPath "+Quote(input)+" -OutputPath "+Quote(output);
        foreach(string argument in new[]{"-NoProfile","-NonInteractive","-Command",command})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Проверка дисков не запустилась");
        // Never terminate a formatting/resizing worker because the UI changed or a timer expired.
        await process.WaitForExitAsync();if(!File.Exists(output))throw new IOException("Windows не вернула результат. Журнал операции: "+dir);
        if(new FileInfo(output).Length>4_000_000)throw new IOException("Ответ Storage превышает допустимый размер");
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(output));var result=document.RootElement;
        if(result.GetProperty("operation_id").GetString()!=(string)request["operation_id"]!)throw new IOException("Идентификатор операции не совпадает");
        if(!result.GetProperty("success").GetBoolean()||process.ExitCode!=0)throw new IOException((result.GetProperty("error").GetString()??"Windows не подтвердила операцию")+"\n\nЖурнал: "+dir);
        return result.GetProperty("data").Clone();
    }
    public static async Task<StorageSnapshot> Read()=>JsonSerializer.Deserialize<StorageSnapshot>(await Run(Request("inventory")),Json.Options)??new();
}
