using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;
namespace Dedsec.Tests;
public static class SensorComponents
{
    public const string PackageHash="1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    public static bool Elevated {get{using var identity=WindowsIdentity.GetCurrent();return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);}}
    public static Version? InstalledVersion
    {
        get{foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})try{using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);using var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");if(Version.TryParse(key?.GetValue("DisplayVersion") as string,out var version))return version;}catch{}return null;}
    }
    public static string Status=>!Environment.Is64BitOperatingSystem?"Базовые счётчики Windows · низкоуровневый компонент не поддерживает 32-битную ОС":InstalledVersion==null?"Базовый мониторинг · полные датчики можно включить здесь":Elevated?"Аппаратный компонент готов · запуск с правами администратора":"Компонент установлен · часть датчиков требует запуска от администратора";
    public static FileStream OpenVerified(string file,string expected=PackageHash)
    {var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);try{if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Пакет датчиков повреждён: SHA-256 не совпадает. Установите полную сборку Dedsec Tests.");stream.Position=0;return stream;}catch{stream.Dispose();throw;}}
    public static async Task<bool> Install(Func<string,string,Task<int>>? runner=null,string? package=null)
    {
        if(!Environment.Is64BitOperatingSystem)throw new PlatformNotSupportedException("PawnIO поддерживает только 64-битную ОС. Базовый мониторинг Windows продолжит работать.");
        if(InstalledVersion is Version installed&&installed>=new Version(2,2,0))return false;
        string file=package??Path.Combine(AppContext.BaseDirectory,"tools","PawnIO_setup.exe");using var locked=OpenVerified(file);
        int code=await (runner??Run)(file,"-install -silent");
        if(code!=0&&code!=3010)throw new IOException($"Не удалось подготовить датчики. Код установщика: {code}. Системная защита не изменялась.");
        if(runner==null&&code==0&&InstalledVersion==null)throw new IOException("Установщик завершился, но компонент не обнаружен. Перезагрузите Windows и проверьте состояние компонента.");
        return code==3010;
    }
    private static async Task<int> Run(string file,string args)
    {using var process=Process.Start(new ProcessStartInfo(file,args){UseShellExecute=true,Verb="runas"})??throw new IOException("Установщик не запустился.");await process.WaitForExitAsync();return process.ExitCode;}
}
