using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
namespace Dedsec.Tests;
public sealed record AvailableUpdate(Version Version,string ManifestUrl,string SignatureUrl,string Notes);
public static class Updates
{
    public const string ReleasesUrl="https://github.com/Dedseckms/Dedsec-Tests/releases";
    private static UpdateWindow? active;
    public static Task Check(Window owner)
    {
        if(active!=null){active.Window.Activate();return Task.CompletedTask;}
        if(owner is MainWindow main&&!main.CanUpdate){BrandedDialog.Notify(owner,"Обновление недоступно","Сначала дождись завершения операции диска, диагностики или останови тест.");return Task.CompletedTask;}
        try{active=new UpdateWindow(owner);active.Window.ShowDialog();}finally{active=null;}return Task.CompletedTask;
    }
    public static AvailableUpdate? Select(JsonElement releases,string arch,Version current)
    {
        AvailableUpdate? best=null;foreach(var release in releases.EnumerateArray())
        {
            if(release.GetProperty("draft").GetBoolean())continue;string tag=release.GetProperty("tag_name").GetString()??"";if(!Version.TryParse(tag.TrimStart('v'),out var version)||version<=current||best!=null&&version<=best.Version)continue;
            string? manifest=null,signature=null;foreach(var asset in release.GetProperty("assets").EnumerateArray()){string? name=asset.GetProperty("name").GetString();if(name==$"update-{arch}.json")manifest=asset.GetProperty("browser_download_url").GetString();if(name==$"update-{arch}.sig")signature=asset.GetProperty("browser_download_url").GetString();}
            if(manifest!=null&&signature!=null){ValidateMetadataUrl(manifest);ValidateMetadataUrl(signature);best=new(version,manifest,signature,release.GetProperty("body").GetString()??"");}
        }return best;
    }
    private static void ValidateMetadataUrl(string value)
    {if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/Dedseckms/Dedsec-Tests/releases/download/",StringComparison.Ordinal)||uri.Query.Length>0||uri.Fragment.Length>0||uri.UserInfo.Length>0)throw new InvalidDataException("Недопустимый адрес метаданных обновления.");}
    public static string Architecture=>RuntimeInformation.ProcessArchitecture==System.Runtime.InteropServices.Architecture.X86?"x86":"x64";
    public static void Install(string stage)
    {
        string worker=Path.Combine(stage,"worker");Directory.CreateDirectory(worker);
        foreach(string file in Directory.GetFiles(AppContext.BaseDirectory)){string ext=Path.GetExtension(file);if(ext is ".exe" or ".dll" or ".json")File.Copy(file,Path.Combine(worker,Path.GetFileName(file)),true);}
        string exe=Path.Combine(worker,"Dedsec.Tests.Updater.exe");if(!File.Exists(exe))throw new FileNotFoundException("Помощник установки отсутствует. Скачай полный Setup или Portable со страницы выпуска.");
        var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};foreach(string argument in new[]{stage,AppContext.BaseDirectory,Environment.ProcessId.ToString()})start.ArgumentList.Add(argument);using var process=Process.Start(start)??throw new IOException("Помощник установки не запустился.");
    }
}
