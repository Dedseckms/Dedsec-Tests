using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
namespace Dedsec.Tests;
// Built-in Windows APIs. No downloaded drivers, polling processes or WMI per tick.
public sealed class WindowsCounters:IDisposable
{
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhOpenQuery(string? source,UIntPtr user,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]private static extern uint PdhAddEnglishCounter(IntPtr query,string path,UIntPtr user,out IntPtr counter);
    [DllImport("pdh.dll")]private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]private static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out CounterValue value);
    [DllImport("pdh.dll")]private static extern uint PdhCloseQuery(IntPtr query);
    [StructLayout(LayoutKind.Sequential)]private struct CounterValue{public uint Status;public double Value;}
    private long? idleLast,totalLast;private IntPtr query,read,write;private bool pdhTried;
    private readonly Dictionary<string,(long Received,long Sent,DateTime Time)> network=new();
    public static double? CpuUsage(long idle,long total,long? previousIdle,long? previousTotal)
    {if(previousIdle==null||previousTotal==null||total<=previousTotal||idle<previousIdle)return null;return Math.Clamp(100*(1-(idle-previousIdle.Value)/(double)(total-previousTotal.Value)),0,100);}
    public void Read(List<SensorRow> rows)
    {
        if(GetSystemTimes(out long idle,out long kernel,out long user)){long total=kernel+user;var cpu=CpuUsage(idle,total,idleLast,totalLast);idleLast=idle;totalLast=total;if(cpu.HasValue)rows.Add(new("Windows","CPU · загрузка Windows","Load",(float)cpu.Value,"%","Cpu","/windows/cpu/load"));}
        var memory=Inventory.Memory();if(memory.Total>0){rows.Add(new("Windows","Загрузка RAM","Load",memory.Load,"%","Memory","/windows/memory/load"));rows.Add(new("Windows","Используется RAM","Data",(float)((memory.Total-memory.Available)/1073741824d),"GiB","Memory","/windows/memory/used"));}
        try
        {
            if(!pdhTried){pdhTried=true;if(PdhOpenQuery(null,UIntPtr.Zero,out query)==0){PdhAddEnglishCounter(query,@"\PhysicalDisk(_Total)\Disk Read Bytes/sec",UIntPtr.Zero,out read);PdhAddEnglishCounter(query,@"\PhysicalDisk(_Total)\Disk Write Bytes/sec",UIntPtr.Zero,out write);}}
            if(query!=IntPtr.Zero&&PdhCollectQueryData(query)==0){AddCounter(rows,read,"Чтение дисков","/windows/disk/read");AddCounter(rows,write,"Запись дисков","/windows/disk/write");}
        }catch{}
        try
        {
            var now=DateTime.UtcNow;var alive=new HashSet<string>();double received=0,sent=0;bool measured=false;
            foreach(var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a=>a.OperationalStatus==OperationalStatus.Up&&a.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
            {try{var bytes=adapter.GetIPStatistics();alive.Add(adapter.Id);if(network.TryGetValue(adapter.Id,out var old)){double seconds=(now-old.Time).TotalSeconds;if(seconds>0&&seconds<30&&bytes.BytesReceived>=old.Received&&bytes.BytesSent>=old.Sent){received+=(bytes.BytesReceived-old.Received)/seconds;sent+=(bytes.BytesSent-old.Sent)/seconds;measured=true;}}network[adapter.Id]=(bytes.BytesReceived,bytes.BytesSent,now);}catch{}}
            foreach(string id in network.Keys.Where(k=>!alive.Contains(k)).ToArray())network.Remove(id);
            if(measured){rows.Add(new("Активные Ethernet / Wi-Fi","Приём","Throughput",(float)(received/1048576),"MiB/s","Network","/windows/network/receive"));rows.Add(new("Активные Ethernet / Wi-Fi","Отправка","Throughput",(float)(sent/1048576),"MiB/s","Network","/windows/network/send"));}
        }catch{}
    }
    private static void AddCounter(List<SensorRow> rows,IntPtr counter,string name,string id)
    {if(counter!=IntPtr.Zero&&PdhGetFormattedCounterValue(counter,0x200,out _,out var value)==0&&value.Status<=1&&double.IsFinite(value.Value)&&value.Value>=0)rows.Add(new("Все физические диски",name,"Throughput",(float)(value.Value/1048576),"MiB/s","Storage",id));}
    public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
}
