using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dedsec.Tests;
public sealed record WindowsCheckResult(string Kind,int ExitCode,string Output,double Seconds,bool Cancelled);
public static class WindowsChecks
{
    public static (string Executable,string Arguments) Command(string kind)=>kind switch
    {"image_quick"=>("dism.exe","/Online /Cleanup-Image /CheckHealth /English"),"image_scan"=>("dism.exe","/Online /Cleanup-Image /ScanHealth /English"),"system_files"=>("sfc.exe","/verifyonly"),_=>throw new ArgumentException("Неизвестная проверка Windows")};
    public static string Decode(byte[] bytes)
    {
        if(bytes.Length==0)return "";if(bytes.Take(Math.Min(200,bytes.Length)).Count(b=>b==0)>Math.Min(200,bytes.Length)/5)return Encoding.Unicode.GetString(bytes).Trim('\uFEFF','\0');
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage).GetString(bytes);
    }
    private static async Task<byte[]> Drain(Stream stream)
    {using var saved=new MemoryStream();byte[] buffer=new byte[8192];int n;while((n=await stream.ReadAsync(buffer))>0){int keep=(int)Math.Min(n,131072-saved.Length);if(keep>0)saved.Write(buffer,0,keep);}return saved.ToArray();}
    public static async Task<WindowsCheckResult> Run(string kind,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();if(!SensorComponents.Elevated)throw new UnauthorizedAccessException("Проверка требует запуска приложения от администратора.");var command=Command(kind);
        string system=Environment.Is64BitOperatingSystem&&!Environment.Is64BitProcess?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Sysnative"):Environment.SystemDirectory;
        var start=new ProcessStartInfo(Path.Combine(system,command.Executable),command.Arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        using var process=Process.Start(start)??throw new IOException("Системная проверка не запустилась");ChildJob.Add(process);var clock=Stopwatch.StartNew();var output=Drain(process.StandardOutput.BaseStream);var error=Drain(process.StandardError.BaseStream);bool cancelled=false;
        using var duration=CancellationTokenSource.CreateLinkedTokenSource(cancellation);duration.CancelAfter(TimeSpan.FromMinutes(30));
        try{await process.WaitForExitAsync(duration.Token);}catch(OperationCanceledException){cancelled=true;try{process.Kill(true);}catch{}await process.WaitForExitAsync();}
        string text=(Decode(await output)+"\n"+Decode(await error)).Trim();clock.Stop();return new(kind,process.ExitCode,text,clock.Elapsed.TotalSeconds,cancelled);
    }
    public static Finding Explain(WindowsCheckResult result)
    {
        string title=result.Kind=="system_files"?"Системные файлы · SFC":result.Kind=="image_scan"?"Образ Windows · полное сканирование":"Образ Windows · известные повреждения";
        string normalized=System.Text.RegularExpressions.Regex.Replace(result.Output.ToLowerInvariant(),@"\s+"," ");string severity="unknown",meaning="Команда завершилась, но однозначный итог автоматически не распознан. Проверьте полный текст Windows ниже.";
        if(result.Cancelled)meaning="Проверка прервана или превысила 30 минут. Частичный вывод не подтверждает исправность.";
        else if(result.ExitCode!=0)meaning="Системная команда завершилась с ошибкой. Это может быть ошибка доступа или обслуживания, а не повреждение компонентов.";
        else if(normalized.Contains("no component store corruption detected")||normalized.Contains("did not find any integrity violations")||normalized.Contains("не обнаружила нарушений целостности")){severity="info";meaning=result.Kind=="image_quick"?"Ранее отмеченное повреждение образа не обнаружено. CheckHealth не выполняет полное сканирование.":"Windows не обнаружила нарушений в рамках выполненной проверки. Это не тест всего оборудования.";}
        else if(normalized.Contains("component store is repairable")||normalized.Contains("component store cannot be repaired")||normalized.Contains("found integrity violations")||normalized.Contains("обнаружила нарушения целостности")||normalized.Contains("обнаружила поврежденные файлы")||normalized.Contains("обнаружила повреждённые файлы")){severity="warning";meaning="Windows сообщает о повреждении. Автоматическое восстановление программой не запускалось.";}
        return new(severity,title,$"{meaning}\n\nКод завершения: {result.ExitCode}; {result.Seconds:F1} с.\n\nВывод Windows (локально):\n{result.Output}","Сохраните важные данные. При нарушениях проверьте журнал CBS/DISM и штатные инструкции Microsoft. /RestoreHealth и /scannow автоматически не выполняются.","https://learn.microsoft.com/windows-hardware/manufacture/desktop/repair-a-windows-image");
    }
}
