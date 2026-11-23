using System.Diagnostics;
using System.IO.Compression;
using Dedsec.Update;
namespace Dedsec.Tests.Updater;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try{Apply(args);}catch(Exception ex){System.Windows.Forms.MessageBox.Show("Обновление не завершено. Прочитайте сведения ниже; резервные файлы сохранены в папке rollback пакета.\n"+ex.Message,"Dedsec Tests · обновление");}
    }
    private static void SafePath(string root,string relative)
    {
        if(string.IsNullOrEmpty(relative)||relative.Contains(':')||relative.Contains('\\')||relative.StartsWith('/')||relative.Split('/').Any(p=>p is ".." or "." or ""))throw new InvalidDataException("Unsafe archive path");
        string full=Path.GetFullPath(Path.Combine(root,relative));if(!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Archive traversal");
        string? check=full;while(check!=null&&check.Length>=root.Length){if((File.Exists(check)||Directory.Exists(check))&&(File.GetAttributes(check)&FileAttributes.ReparsePoint)!=0)throw new IOException("Reparse points are unsupported for updates");check=Path.GetDirectoryName(check);}
    }
    private static void Apply(string[] args)
    {
        if(args.Length!=3)throw new ArgumentException("Expected stage, install directory and process ID");
        string stage=Path.GetFullPath(args[0]),target=Path.GetFullPath(args[1]);var manifest=Trust.Verify(File.ReadAllBytes(Path.Combine(stage,"manifest.json")),File.ReadAllBytes(Path.Combine(stage,"manifest.sig")));string zip=Path.Combine(stage,"package.zip");Trust.VerifyPackage(zip,manifest);
        if(!File.Exists(Path.Combine(target,"Dedsec.Tests.exe")))throw new IOException("Install directory does not contain Dedsec Tests");
        if(manifest.Architecture!=(Environment.Is64BitProcess?"x64":"x86"))throw new InvalidDataException("Wrong package architecture");
        string extract=Path.Combine(stage,"extracted"),backup=Path.Combine(stage,"rollback");Directory.CreateDirectory(extract);Directory.CreateDirectory(backup);
        var files=new List<string>();var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long size=0;
        using(var archive=ZipFile.OpenRead(zip))foreach(var entry in archive.Entries)
        {
            if(entry.FullName.EndsWith('/'))continue;SafePath(extract,entry.FullName);SafePath(target,entry.FullName);if(!unique.Add(entry.FullName)||files.Count>2000||(size+=entry.Length)>1_000_000_000)throw new InvalidDataException("Archive limits");
            if((entry.ExternalAttributes>>16&0xf000)==0xa000)throw new InvalidDataException("Symlink entry");
            string destination=Path.Combine(extract,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);entry.ExtractToFile(destination);files.Add(entry.FullName);
        }
        if(!files.Contains("Dedsec.Tests.exe")||!files.Contains("Dedsec.Tests.dll"))throw new InvalidDataException("Missing application payload");
        int pid=int.Parse(args[2]);try{using var parent=Process.GetProcessById(pid);if(!parent.WaitForExit(30000))throw new IOException("Приложение не закрылось");}catch(ArgumentException){}
        var changed=new List<(string File,bool Existed)>();Process? launched=null;
        try
        {
            // Probe permissions before touching the payload. Portable folders must be writable by their owner.
            string probe=Path.Combine(target,".dedsec-update-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);
            foreach(string file in files){SafePath(target,file);string dest=Path.Combine(target,file),old=Path.Combine(backup,file);bool exists=File.Exists(dest);if(exists){Directory.CreateDirectory(Path.GetDirectoryName(old)!);File.Copy(dest,old,true);}changed.Add((file,exists));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(Path.Combine(extract,file),dest,true);}
            string local=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DedsecTests");Directory.CreateDirectory(local);string marker=Path.Combine(local,"update-ready-"+Guid.NewGuid().ToString("N"));
            var start=new ProcessStartInfo(Path.Combine(target,"Dedsec.Tests.exe")){UseShellExecute=false,WorkingDirectory=target};start.ArgumentList.Add("--update-ready");start.ArgumentList.Add(marker);launched=Process.Start(start)??throw new IOException("Launch failed");var clock=Stopwatch.StartNew();
            while(!File.Exists(marker)&&!launched.HasExited&&clock.Elapsed.TotalSeconds<60)Thread.Sleep(250);
            if(!File.Exists(marker))throw new IOException("Новая версия не подтвердила запуск за 60 секунд");File.Delete(marker);
            // Retain the local rollback directory; no recursive removal of an installation or user data.
        }
        catch(Exception original)
        {
            if(launched!=null&&!launched.HasExited){try{launched.Kill(true);launched.WaitForExit(5000);}catch{}}
            var failures=new List<Exception>();foreach(var (file,existed) in changed.AsEnumerable().Reverse()){try{string dest=Path.Combine(target,file);if(existed)File.Copy(Path.Combine(backup,file),dest,true);else if(File.Exists(dest))File.Delete(dest);}catch(Exception error){failures.Add(error);}}
            if(failures.Count>0)throw new IOException("Не все файлы удалось восстановить. Резервная копия: "+backup,new AggregateException(failures.Prepend(original)));
            try{Process.Start(new ProcessStartInfo(Path.Combine(target,"Dedsec.Tests.exe")){UseShellExecute=true});}catch{}
            throw;
        }
    }
}
