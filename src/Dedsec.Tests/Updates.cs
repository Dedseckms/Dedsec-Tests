using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Dedsec.Update;
namespace Dedsec.Tests;
public static class Updates
{
    private static async Task<byte[]> Limited(HttpClient http,string url,int limit)
    {
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Метаданные обновления слишком велики");using var input=await response.Content.ReadAsStreamAsync();using var memory=new MemoryStream();byte[] buffer=new byte[8192];int n;while((n=await input.ReadAsync(buffer))>0){if(memory.Length+n>limit)throw new InvalidDataException("Метаданные обновления слишком велики");memory.Write(buffer,0,n);}return memory.ToArray();
    }
    public static async Task Check(Window owner)
    {
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(10)};http.DefaultRequestHeaders.UserAgent.ParseAdd("DedsecTests/0.1.0");
            // Both preview and stable releases participate; never install an older or equal version.
            var releases=JsonDocument.Parse(await Limited(http,"https://api.github.com/repos/Dedseckms/Dedsec-Tests/releases?per_page=20",2_000_000));string arch=RuntimeInformation.ProcessArchitecture==Architecture.X86?"x86":"x64";
            string? manifestUrl=null,signatureUrl=null,notes=null;Version? best=null;
            var current=typeof(Updates).Assembly.GetName().Version??new Version(0,1,0);
            foreach(var release in releases.RootElement.EnumerateArray())
            {
                if(release.GetProperty("draft").GetBoolean())continue;
                string tag=release.GetProperty("tag_name").GetString()??"";if(!Version.TryParse(tag.TrimStart('v'),out var v)||v<=current||best!=null&&v<=best)continue;
                string? m=null,s=null;foreach(var asset in release.GetProperty("assets").EnumerateArray()){string? name=asset.GetProperty("name").GetString();if(name==$"update-{arch}.json")m=asset.GetProperty("browser_download_url").GetString();if(name==$"update-{arch}.sig")s=asset.GetProperty("browser_download_url").GetString();}
                if(m!=null&&s!=null){manifestUrl=m;signatureUrl=s;notes=release.GetProperty("body").GetString();best=v;}
            }
            if(manifestUrl==null||signatureUrl==null){MessageBox.Show(owner,$"Установлена актуальная версия {current.ToString(3)}. Совместимых подписанных обновлений новее нет.","Обновления GitHub");return;}
            var bytes=await Limited(http,manifestUrl,16384);var signature=await Limited(http,signatureUrl,512);var manifest=Trust.Verify(bytes,signature);
            if(manifest.Architecture!=arch||Version.Parse(manifest.Version)!=best)throw new InvalidDataException("Версия или архитектура манифеста не совпадает с релизом");
            if(MessageBox.Show(owner,$"Доступна версия {manifest.Version} ({manifest.Size/1048576d:F1} MiB).\n\n{notes?[..Math.Min(notes.Length,2200)]}\n\nСкачать, проверить и подготовить обновление?","Обновления",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            string stage=Path.Combine(Settings.DataDir,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);string zip=Path.Combine(stage,"package.zip");
            using(var response=await http.GetAsync(manifest.Url,HttpCompletionOption.ResponseHeadersRead)){response.EnsureSuccessStatusCode();using var input=await response.Content.ReadAsStreamAsync();using var output=File.Create(zip);byte[] buffer=new byte[65536];long total=0;int n;while((n=await input.ReadAsync(buffer))>0){total+=n;if(total>manifest.Size)throw new InvalidDataException("Download exceeds signed size");await output.WriteAsync(buffer.AsMemory(0,n));}}
            Trust.VerifyPackage(zip,manifest);File.WriteAllBytes(Path.Combine(stage,"manifest.json"),bytes);File.WriteAllBytes(Path.Combine(stage,"manifest.sig"),signature);
            if(MessageBox.Show(owner,"Пакет скачан и подпись проверена. Установить сейчас? Приложение закроется, отчёты останутся на месте. Если тест выполняется, сначала остановите его.","Обновление готово",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
            if(owner is MainWindow window&&!window.CanUpdate){MessageBox.Show(owner,"Сначала остановите выполняющийся тест.");return;}
            string worker=Path.Combine(stage,"worker");Directory.CreateDirectory(worker);
            foreach(string file in Directory.GetFiles(AppContext.BaseDirectory)){string ext=Path.GetExtension(file);if(ext is ".exe" or ".dll" or ".json")File.Copy(file,Path.Combine(worker,Path.GetFileName(file)),true);}
            string exe=Path.Combine(worker,"Dedsec.Tests.Updater.exe");if(!File.Exists(exe))throw new FileNotFoundException("Updater отсутствует: установите полную сборку из Releases");
            var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};foreach(string arg in new[]{stage,AppContext.BaseDirectory,Environment.ProcessId.ToString()})start.ArgumentList.Add(arg);Process.Start(start);System.Windows.Application.Current.Shutdown();
        }
        catch(Exception ex){MessageBox.Show(owner,"Обновление не установлено. Текущая версия сохранена.\n\n"+ex.Message,"Обновления");}
    }
}
